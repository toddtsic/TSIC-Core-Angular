using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class RegistrationAccountingRepository : IRegistrationAccountingRepository
{
    private readonly SqlDbContext _context;

    public RegistrationAccountingRepository(SqlDbContext context)
    {
        _context = context;
    }

    public void Add(RegistrationAccounting entry)
    {
        _context.RegistrationAccounting.Add(entry);
    }

    public async Task RecordPaymentAndRecomputeAsync(
        RegistrationAccounting row, string userId, CancellationToken cancellationToken = default)
    {
        // The ledger is the source of truth; PaidTotal is a cached projection of it.
        // Persist the row, then re-sum the ledger (now including this row) and stamp the
        // result onto the keyed entity. One transaction — joined to the caller's if it
        // already opened one — so the row and the recomputed total can never commit apart.
        var ownsTransaction = _context.Database.CurrentTransaction is null;
        var transaction = ownsTransaction
            ? await _context.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            _context.RegistrationAccounting.Add(row);
            await _context.SaveChangesAsync(cancellationToken); // row must be visible to the re-sum below

            if (row.TeamId.HasValue)
                await RecomputeTeamPaidTotalAsync(row.TeamId.Value, userId, cancellationToken);
            else if (row.RegistrationId.HasValue)
                await RecomputeRegistrationPaidTotalAsync(row.RegistrationId.Value, userId, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    /// <summary>
    /// Re-derives one registration's PaidTotal from the ledger (sum of the five payment
    /// buckets) and recomputes OwedTotal. Does NOT save — the caller owns the transaction.
    /// </summary>
    private async Task RecomputeRegistrationPaidTotalAsync(Guid registrationId, string userId, CancellationToken ct)
    {
        var totals = await GetPaymentTotalsByEntityAsync(
            PaymentEntityKind.Registration, new[] { registrationId }, ct);
        var paid = totals.TryGetValue(registrationId, out var t) ? t.GrossPaid : 0m;

        var reg = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, ct);
        if (reg is null) return;

        reg.PaidTotal = paid;
        reg.RecalcTotals();
        reg.Modified = DateTime.Now;
        reg.LebUserId = userId;
    }

    /// <summary>
    /// Re-derives one team's PaidTotal from the ledger (sum of the five payment buckets)
    /// and recomputes OwedTotal. Does NOT save — the caller owns the transaction.
    /// </summary>
    private async Task RecomputeTeamPaidTotalAsync(Guid teamId, string userId, CancellationToken ct)
    {
        var totals = await GetPaymentTotalsByEntityAsync(
            PaymentEntityKind.Team, new[] { teamId }, ct);
        var paid = totals.TryGetValue(teamId, out var t) ? t.GrossPaid : 0m;

        var team = await _context.Teams
            .FirstOrDefaultAsync(x => x.TeamId == teamId, ct);
        if (team is null) return;

        team.PaidTotal = paid;
        team.RecalcTotals();
        team.Modified = DateTime.Now;
        team.LebUserId = userId;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> GetLatestAdnTransactionIdAsync(IEnumerable<Guid> registrationIds, CancellationToken cancellationToken = default)
    {
        var regIdSet = registrationIds.ToHashSet();
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(a => a.RegistrationId != null && regIdSet.Contains(a.RegistrationId.Value) && !string.IsNullOrWhiteSpace(a.AdnTransactionId))
            .OrderByDescending(a => a.Createdate)
            .Select(a => a.AdnTransactionId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> AnyByAdnTransactionIdAsync(string adnTransactionId, CancellationToken cancellationToken = default)
    {
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .AnyAsync(a => a.AdnTransactionId == adnTransactionId, cancellationToken);
    }

    public async Task<Dictionary<Guid, PaymentMethodTotals>> GetPaymentTotalsByEntityAsync(
        PaymentEntityKind kind,
        IReadOnlyCollection<Guid> entityIds,
        CancellationToken cancellationToken = default)
    {
        if (entityIds.Count == 0) return new();

        // Local captures so EF translates Contains(...) to a parameterized SQL IN clause.
        // Filtering on PaymentMethodId (reference-table PK) instead of the freeform
        // PaymentMethod text means variants like "Credit Card Payment PIF" and
        // "Online Correction By Client" land in their canonical buckets without
        // depending on exact string matches.
        var ccIds = PaymentMethodIds.CcPaid.ToArray();
        var echeckIds = PaymentMethodIds.Echeck.ToArray();
        var checkIds = PaymentMethodIds.Check.ToArray();
        var cashIds = PaymentMethodIds.Cash.ToArray();
        var correctionIds = PaymentMethodIds.Correction.ToArray();

        var rows = _context.RegistrationAccounting.AsNoTracking().Where(ra => ra.Active == true);

        var keyed = kind switch
        {
            PaymentEntityKind.Registration => rows
                .Where(ra => ra.RegistrationId.HasValue && entityIds.Contains(ra.RegistrationId!.Value))
                .Select(ra => new { EntityId = ra.RegistrationId!.Value, ra.Payamt, ra.PaymentMethodId }),
            PaymentEntityKind.Team => rows
                .Where(ra => ra.TeamId.HasValue && entityIds.Contains(ra.TeamId!.Value))
                .Select(ra => new { EntityId = ra.TeamId!.Value, ra.Payamt, ra.PaymentMethodId }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported PaymentEntityKind"),
        };

        return await keyed
            .GroupBy(x => x.EntityId)
            .Select(g => new
            {
                EntityId = g.Key,
                CreditCard = g.Where(x => ccIds.Contains(x.PaymentMethodId)).Sum(x => x.Payamt ?? 0m),
                Echeck = g.Where(x => echeckIds.Contains(x.PaymentMethodId)).Sum(x => x.Payamt ?? 0m),
                Check = g.Where(x => checkIds.Contains(x.PaymentMethodId)).Sum(x => x.Payamt ?? 0m),
                Cash = g.Where(x => cashIds.Contains(x.PaymentMethodId)).Sum(x => x.Payamt ?? 0m),
                Correction = g.Where(x => correctionIds.Contains(x.PaymentMethodId)).Sum(x => x.Payamt ?? 0m),
            })
            .ToDictionaryAsync(
                x => x.EntityId,
                x => new PaymentMethodTotals
                {
                    CreditCard = x.CreditCard,
                    Echeck = x.Echeck,
                    Check = x.Check,
                    Cash = x.Cash,
                    Correction = x.Correction,
                },
                cancellationToken);
    }

    public async Task<bool> HasPaymentsForTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .AnyAsync(ra => ra.TeamId == teamId && ra.Active == true, cancellationToken);
    }

    public async Task<List<AccountingRecordDto>> GetByRegistrationIdAsync(Guid registrationId, CancellationToken ct = default)
    {
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(a => a.RegistrationId == registrationId)
            .Join(_context.AccountingPaymentMethods,
                a => a.PaymentMethodId,
                pm => pm.PaymentMethodId,
                (a, pm) => new { a, pm })
            .OrderByDescending(x => x.a.Createdate)
            .Select(x => new AccountingRecordDto
            {
                AId = x.a.AId,
                TeamId = x.a.TeamId,
                Date = x.a.Createdate,
                PaymentMethod = x.pm.PaymentMethod ?? x.a.Paymeth ?? "",
                DueAmount = x.a.Dueamt,
                PaidAmount = x.a.Payamt,
                Comment = x.a.Comment,
                CheckNo = x.a.CheckNo,
                PromoCode = x.a.PromoCode,
                Active = x.a.Active,
                AdnTransactionId = x.a.AdnTransactionId,
                AdnCc4 = x.a.AdnCc4,
                AdnCcExpDate = x.a.AdnCcexpDate,
                AdnInvoiceNo = x.a.AdnInvoiceNo,
                CanRefund = x.a.AdnTransactionId != null && x.a.AdnTransactionId != ""
                    && x.pm.PaymentMethod != null && x.pm.PaymentMethod.Contains("Credit Card")
            })
            .ToListAsync(ct);
    }

    public async Task<RegistrationAccounting?> GetByAIdAsync(int aId, CancellationToken ct = default)
    {
        return await _context.RegistrationAccounting
            .Include(a => a.Registration)
            .Where(a => a.AId == aId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<AccountingRecordDto>> GetByTeamIdAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(a => a.TeamId == teamId)
            .Join(_context.AccountingPaymentMethods,
                a => a.PaymentMethodId,
                pm => pm.PaymentMethodId,
                (a, pm) => new { a, pm })
            .OrderByDescending(x => x.a.Createdate)
            .Select(x => new AccountingRecordDto
            {
                AId = x.a.AId,
                TeamId = x.a.TeamId,
                Date = x.a.Createdate,
                PaymentMethod = x.pm.PaymentMethod ?? x.a.Paymeth ?? "",
                DueAmount = x.a.Dueamt,
                PaidAmount = x.a.Payamt,
                Comment = x.a.Comment,
                CheckNo = x.a.CheckNo,
                PromoCode = x.a.PromoCode,
                Active = x.a.Active,
                AdnTransactionId = x.a.AdnTransactionId,
                AdnCc4 = x.a.AdnCc4,
                AdnCcExpDate = x.a.AdnCcexpDate,
                AdnInvoiceNo = x.a.AdnInvoiceNo,
                CanRefund = x.a.AdnTransactionId != null && x.a.AdnTransactionId != ""
                    && x.pm.PaymentMethod != null && x.pm.PaymentMethod.Contains("Credit Card")
            })
            .ToListAsync(ct);
    }

    public async Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default)
    {
        return await _context.AccountingPaymentMethods
            .AsNoTracking()
            .OrderBy(pm => pm.PaymentMethod)
            .Select(pm => new PaymentMethodOptionDto
            {
                PaymentMethodId = pm.PaymentMethodId,
                PaymentMethod = pm.PaymentMethod ?? ""
            })
            .ToListAsync(ct);
    }

    public async Task DeleteByRegistrationIdAsync(Guid registrationId, CancellationToken ct = default)
    {
        var rows = await _context.RegistrationAccounting
            .Where(a => a.RegistrationId == registrationId)
            .ToListAsync(ct);
        if (rows.Count > 0)
        {
            _context.RegistrationAccounting.RemoveRange(rows);
            await _context.SaveChangesAsync(ct);
        }
    }
}
