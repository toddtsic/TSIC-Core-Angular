using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class CustomerJobRevenueRepository : ICustomerJobRevenueRepository
{
    private readonly SqlDbContext _context;

    public CustomerJobRevenueRepository(SqlDbContext context)
    {
        _context = context;
    }

    // =====================================================================
    // EF port of the CustomerJobRevenueRollups sproc pair.
    // Specs: scripts/9-add-echeck-to-cjrr.sql (TSICADN, settlement-based)
    //        scripts/10-add-echeck-to-cjrr-nottsicadn.sql (non-ADN, RA-based)
    // The legacy sprocs stay frozen for TSIC-Unify; only the new system uses
    // these methods. Deltas from the sprocs (deliberate, golden-master vetted):
    //   - decimal-correct math end-to-end (sprocs sum in float);
    //   - the job filter is applied at the SOURCE queries, not at export
    //     (same final rows, far less work);
    //   - dates are optional: null = complete history (specific-jobs scope).
    // =====================================================================

    // Rollup PayMethod labels. Leading spaces are the sprocs' column-ordering
    // mechanism (pivot sorts alphabetically; more spaces sort further right).
    private const string LblCcPayments = "    CC Payments";
    private const string LblCcCredits = "   CC Credits";
    private const string LblEcheckPayments = "    E-Check Payments";
    private const string LblFailedEcheck = "    Failed E-Check Payments";
    private const string LblCheck = "      Check";
    private const string LblCheckClientRecd = "     Check Client Rec'd";
    private const string LblAdminFees = "Admin Fees";
    private const string LblCcFees = "  CC Fees";
    private const string LblEcheckFees = "  E-Check Fees";
    private const string LblTsicFees = " TSIC Fees";

    private sealed record RawRollupRow(
        string JobName, int Year, int Month, string Label, decimal Payment, decimal FeePct);

    /// <summary>Customer-group scope: jobId → customerId → group members (or just the one customer).</summary>
    private async Task<List<Guid>> GetCustomerGroupIdsAsync(Guid jobId, CancellationToken ct)
    {
        var customerId = await _context.Jobs
            .Where(j => j.JobId == jobId)
            .Select(j => j.CustomerId)
            .FirstAsync(ct);

        var groupId = await _context.CustomerGroupCustomers
            .Where(c => c.CustomerId == customerId)
            .Select(c => (int?)c.CustomerGroupId)
            .FirstOrDefaultAsync(ct);

        if (groupId == null)
        {
            return [customerId];
        }

        var ids = await _context.CustomerGroupCustomers
            .AsNoTracking()
            .Where(c => c.CustomerGroupId == groupId)
            .Select(c => c.CustomerId)
            .ToListAsync(ct);

        return ids.Count > 0 ? ids : [customerId];
    }

    public async Task<List<string>> GetAvailableJobNamesAsync(Guid jobId, CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);

        // ISNUMERIC(year) = 1 AND year >= 2022 — Year is varchar; parse client-side.
        var jobs = await _context.Jobs
            .AsNoTracking()
            .Where(j => customerIds.Contains(j.CustomerId) && j.JobName != null && j.Year != null)
            .Select(j => new { j.JobName, j.Year })
            .ToListAsync(ct);

        return jobs
            .Where(j => int.TryParse(j.Year, out var y) && y >= 2022)
            .Select(j => j.JobName!)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<RevenueRollupResponseDto> GetRollupAsync(
        Guid jobId, bool isTsicAdn,
        DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var jobFilter = jobNames.ToList();
        // Sproc contract: @endDate is advanced one day, then all comparisons are < end.
        DateTime? endEx = endDate?.Date.AddDays(1);
        DateTime? start = startDate;

        var raw = new List<RawRollupRow>();

        if (isTsicAdn)
        {
            // --- Settlement-based categories (CC / E-Check families) from adn.vTxs ---
            string[] adnMethods = ["Credit Card Payment", "Credit Card Credit", "E-Check Payment", "Failed E-Check Payment"];
            var vtxRows = await (
                from ra in _context.RegistrationAccounting
                join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
                join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                join j in _context.Jobs on r.JobId equals j.JobId
                join v in _context.VTxs on ra.AdnTransactionId equals v.TransactionId
                where customerIds.Contains(j.CustomerId)
                    && adnMethods.Contains(apm.PaymentMethod!)
                    && v.TransactionStatus != "Declined" && v.TransactionStatus != "Voided"
                    && v.SettlementTs != null
                    && (start == null || v.SettlementTs >= start)
                    && (endEx == null || v.SettlementTs < endEx)
                    && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
                group new { v.SettlementAmount } by new
                {
                    j.JobName,
                    Year = v.SettlementTs!.Value.Year,
                    Month = v.SettlementTs.Value.Month,
                    Method = apm.PaymentMethod,
                    j.ProcessingFeePercent,
                    j.EcprocessingFeePercent
                } into g
                select new
                {
                    g.Key.JobName,
                    g.Key.Year,
                    g.Key.Month,
                    g.Key.Method,
                    Amount = g.Sum(x => x.SettlementAmount ?? 0m),
                    g.Key.ProcessingFeePercent,
                    g.Key.EcprocessingFeePercent
                })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var row in vtxRows)
            {
                var (label, feePct) = row.Method switch
                {
                    "Credit Card Payment" => (LblCcPayments, (row.ProcessingFeePercent ?? 3.5m) / 100m),
                    "Credit Card Credit" => (LblCcCredits, (row.ProcessingFeePercent ?? 3.5m) / 100m),
                    "E-Check Payment" => (LblEcheckPayments, (row.EcprocessingFeePercent ?? 1.5m) / 100m),
                    _ => (LblFailedEcheck, (row.EcprocessingFeePercent ?? 1.5m) / 100m)
                };
                raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, label, row.Amount, feePct));
            }
        }
        else
        {
            // --- Non-ADN: same categories, but amounts/dates come from Registration_Accounting ---
            string[] raMethods = ["Credit Card Payment", "Credit Card Credit", "E-Check Payment", "Failed E-Check Payment"];
            var raRows = await (
                from ra in _context.RegistrationAccounting
                join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
                join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
                join j in _context.Jobs on r.JobId equals j.JobId
                where customerIds.Contains(j.CustomerId)
                    && raMethods.Contains(apm.PaymentMethod!)
                    && ra.Active == true
                    && ra.Createdate != null
                    && (start == null || ra.Createdate >= start)
                    && (endEx == null || ra.Createdate < endEx)
                    && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
                group new { ra.Payamt } by new
                {
                    j.JobName,
                    Year = ra.Createdate!.Value.Year,
                    Month = ra.Createdate.Value.Month,
                    Method = apm.PaymentMethod
                } into g
                select new { g.Key.JobName, g.Key.Year, g.Key.Month, g.Key.Method, Amount = g.Sum(x => x.Payamt ?? 0m) })
                .AsNoTracking()
                .ToListAsync(ct);

            foreach (var row in raRows)
            {
                var label = row.Method switch
                {
                    "Credit Card Payment" => LblCcPayments,
                    "Credit Card Credit" => LblCcCredits,
                    "E-Check Payment" => LblEcheckPayments,
                    _ => LblFailedEcheck
                };
                raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, label, row.Amount, 0m));
            }
        }

        // --- Checks (both variants; TSICADN additionally excludes rows whose ADN tx is Declined/Voided) ---
        string[] checkMethods = ["Check Payment By Client", "Check Payment By TSIC"];
        var checkRows = await (
            from ra in _context.RegistrationAccounting
            join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join j in _context.Jobs on r.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && checkMethods.Contains(apm.PaymentMethod!)
                && ra.Active == true
                && ra.Createdate != null
                && (start == null || ra.Createdate >= start)
                && (endEx == null || ra.Createdate < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
                && (!isTsicAdn || !_context.VTxs.Any(v =>
                        v.TransactionId == ra.AdnTransactionId
                        && (v.TransactionStatus == "Declined" || v.TransactionStatus == "Voided")))
            group new { ra.Payamt } by new { j.JobName, Year = ra.Createdate!.Value.Year, Month = ra.Createdate.Value.Month } into g
            select new { g.Key.JobName, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.Payamt ?? 0m) })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in checkRows)
        {
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblCheck, row.Amount, 0m));
            // Negative mirror: zeroes the check contribution so the pivot grand total stays CC-only.
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblCheckClientRecd, -row.Amount, 0m));
        }

        // --- Admin Fees (negative) — month-bucketed table, filtered on first-of-month like the sprocs ---
        var adminChargeRows = await (
            from jc in _context.JobAdminCharges
            join j in _context.Jobs on jc.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(jc.Year, jc.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(jc.Year, jc.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            group new { jc.ChargeAmount } by new { j.JobName, jc.Year, jc.Month } into g
            select new { g.Key.JobName, g.Key.Year, g.Key.Month, Amount = g.Sum(x => x.ChargeAmount) })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in adminChargeRows)
        {
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblAdminFees, -row.Amount, 0m));
        }

        // --- CC / E-Check processing fee rollups (TSICADN only; derived per raw row like the sproc) ---
        if (isTsicAdn)
        {
            var feeRows = new List<RawRollupRow>();
            foreach (var row in raw)
            {
                if (row.Label.Contains("CC"))
                {
                    feeRows.Add(row with { Label = LblCcFees, Payment = -Math.Abs(row.Payment * row.FeePct), FeePct = 0m });
                }
                // Fee only on positive e-check payments — ADN does not refund the fee on NSF returns.
                else if (row.Label.Contains("E-Check") && row.Payment > 0)
                {
                    feeRows.Add(row with { Label = LblEcheckFees, Payment = -Math.Abs(row.Payment * row.FeePct), FeePct = 0m });
                }
            }
            // The sprocs' fee INSERTs are SELECT DISTINCT over a tuple that excludes the pct —
            // identical fee rows (e.g. a same-month payment + equal refund) collapse to one.
            // FeePct is zeroed above so record equality mirrors that tuple exactly.
            raw.AddRange(feeRows.Distinct());
        }

        // --- TSIC Fees (negative) from Monthly_Job_Stats × per-player/team charges ---
        var statRows = await (
            from mjs in _context.MonthlyJobStats
            join j in _context.Jobs on mjs.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new
            {
                j.JobName,
                mjs.Year,
                mjs.Month,
                mjs.CountNewPlayersThisMonth,
                mjs.CountNewTeamsThisMonth,
                j.PerPlayerCharge,
                j.PerTeamCharge
            })
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var row in statRows)
        {
            // Sproc NULL semantics: a NULL count nullifies the whole expression and the row
            // contributes nothing (SUM skips NULLs) — replicate, don't coalesce counts to 0.
            if (row.CountNewPlayersThisMonth == null || row.CountNewTeamsThisMonth == null)
            {
                continue;
            }
            var fee = (row.CountNewPlayersThisMonth.Value * (row.PerPlayerCharge ?? 0m))
                      + (row.CountNewTeamsThisMonth.Value * (row.PerTeamCharge ?? 0m));
            raw.Add(new RawRollupRow(row.JobName!, row.Year, row.Month, LblTsicFees, -fee, 0m));
        }

        // --- Final rollup: group to PayMethod cells, decimal(8,2) semantics ---
        var revenueRecords = raw
            .GroupBy(r => new { r.JobName, r.Year, r.Month, r.Label })
            .Select(g => new JobRevenueRecordDto
            {
                JobName = g.Key.JobName,
                Year = g.Key.Year,
                Month = g.Key.Month,
                PayMethod = g.Key.Label,
                PayAmount = Math.Round(g.Sum(x => x.Payment), 2, MidpointRounding.AwayFromZero)
            })
            .OrderBy(r => r.JobName, StringComparer.Ordinal)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ThenBy(r => r.PayMethod, StringComparer.Ordinal)
            .ToList();

        // --- Monthly counts (sproc set #2) ---
        var monthlyCounts = await (
            from mjs in _context.MonthlyJobStats
            join j in _context.Jobs on mjs.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(mjs.Year, mjs.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            orderby j.JobName, mjs.Year, mjs.Month
            select new JobMonthlyCountDto
            {
                Aid = mjs.Aid,
                JobName = j.JobName!,
                Year = mjs.Year,
                Month = mjs.Month,
                CountActivePlayersToDate = mjs.CountActivePlayersToDate ?? 0,
                CountActivePlayersToDateLastMonth = mjs.CountActivePlayersToDateLastMonth ?? 0,
                CountNewPlayersThisMonth = mjs.CountNewPlayersThisMonth ?? 0,
                CountActiveTeamsToDate = mjs.CountActiveTeamsToDate ?? 0,
                CountActiveTeamsToDateLastMonth = mjs.CountActiveTeamsToDateLastMonth ?? 0,
                CountNewTeamsThisMonth = mjs.CountNewTeamsThisMonth ?? 0
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // --- Admin fees (sproc set #3) ---
        var adminFees = await (
            from jac in _context.JobAdminCharges
            join j in _context.Jobs on jac.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && (start == null || EF.Functions.DateFromParts(jac.Year, jac.Month, 1) >= start)
                && (endEx == null || EF.Functions.DateFromParts(jac.Year, jac.Month, 1) < endEx)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            orderby j.JobName, jac.Year, jac.Month, jac.ChargeType.Name
            select new JobAdminFeeDto
            {
                JobName = j.JobName!,
                Year = jac.Year,
                Month = jac.Month,
                ChargeType = jac.ChargeType.Name ?? string.Empty,
                ChargeAmount = jac.ChargeAmount,
                Comment = jac.Comment ?? string.Empty
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return new RevenueRollupResponseDto
        {
            RevenueRecords = revenueRecords,
            MonthlyCounts = monthlyCounts,
            AdminFees = adminFees
        };
    }

    public async Task<List<JobPaymentRecordDto>> GetPaymentDetailsAsync(
        Guid jobId, bool isTsicAdn, string method,
        DateTime? startDate, DateTime? endDate, IReadOnlyList<string> jobNames,
        CancellationToken ct = default)
    {
        var customerIds = await GetCustomerGroupIdsAsync(jobId, ct);
        var jobFilter = jobNames.ToList();
        DateTime? endEx = endDate?.Date.AddDays(1);
        DateTime? start = startDate;

        // Sproc detail families. The DEPLOYED procs include 'Credit Card Credit' in the CC
        // detail insert — repo scripts 9/10 are one line stale there (verified via
        // OBJECT_DEFINITION diff against dev DB, 2026-07-29).
        string[] methods = method switch
        {
            "cc" => ["Credit Card Payment", "Credit Card Credit"],
            "check" => ["Check Payment By Client", "Check Payment By TSIC"],
            "echeck" => ["E-Check Payment", "Failed E-Check Payment"],
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Expected cc | check | echeck.")
        };

        var query =
            from ra in _context.RegistrationAccounting
            join apm in _context.AccountingPaymentMethods on ra.PaymentMethodId equals apm.PaymentMethodId
            join r in _context.Registrations on ra.RegistrationId equals r.RegistrationId
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            where customerIds.Contains(j.CustomerId)
                && methods.Contains(apm.PaymentMethod!)
                && (jobFilter.Count == 0 || jobFilter.Contains(j.JobName!))
            select new { ra, apm, r, u, j };

        // Check rows are RA-dated in both variants; CC/E-Check rows are settlement-dated for
        // TSICADN (join adn.vTxs) and RA-dated for non-ADN.
        var useVtx = isTsicAdn && method != "check";

        if (method == "check")
        {
            query = query.Where(x =>
                x.ra.Active == true
                && x.ra.Createdate != null
                && (start == null || x.ra.Createdate >= start)
                && (endEx == null || x.ra.Createdate < endEx)
                && (!isTsicAdn || !_context.VTxs.Any(v =>
                        v.TransactionId == x.ra.AdnTransactionId
                        && (v.TransactionStatus == "Declined" || v.TransactionStatus == "Voided"))));
        }
        else if (!useVtx)
        {
            query = query.Where(x =>
                x.ra.Active == true
                && x.ra.Createdate != null
                && (start == null || x.ra.Createdate >= start)
                && (endEx == null || x.ra.Createdate < endEx));
        }

        List<JobPaymentRecordDto> records;
        if (useVtx)
        {
            records = await (
                from x in query
                join v in _context.VTxs on x.ra.AdnTransactionId equals v.TransactionId
                where v.TransactionStatus != "Declined" && v.TransactionStatus != "Voided"
                    && v.SettlementTs != null
                    && (start == null || v.SettlementTs >= start)
                    && (endEx == null || v.SettlementTs < endEx)
                select new JobPaymentRecordDto
                {
                    JobName = x.j.JobName!,
                    Year = v.SettlementTs!.Value.Year,
                    Month = v.SettlementTs.Value.Month,
                    Registrant = x.r.ClubName == null
                        ? x.u.FirstName + " " + x.u.LastName
                        : x.u.FirstName + " " + x.u.LastName + " (" + x.r.ClubName + ")",
                    PaymentMethod = x.apm.PaymentMethod!,
                    PaymentDate = v.SettlementTs.Value,
                    PaymentAmount = v.SettlementAmount ?? 0m
                })
                .AsNoTracking()
                .ToListAsync(ct);
        }
        else
        {
            var displayAsCheck = method == "check";
            records = await query
                .Select(x => new JobPaymentRecordDto
                {
                    JobName = x.j.JobName!,
                    Year = x.ra.Createdate!.Value.Year,
                    Month = x.ra.Createdate.Value.Month,
                    Registrant = x.r.ClubName == null
                        ? x.u.FirstName + " " + x.u.LastName
                        : x.u.FirstName + " " + x.u.LastName + " (" + x.r.ClubName + ")",
                    PaymentMethod = displayAsCheck ? "Check" : x.apm.PaymentMethod!,
                    PaymentDate = x.ra.Createdate.Value,
                    PaymentAmount = x.ra.Payamt ?? 0m
                })
                .AsNoTracking()
                .ToListAsync(ct);
        }

        return records
            .OrderBy(r => r.JobName, StringComparer.Ordinal)
            .ThenBy(r => r.Year)
            .ThenBy(r => r.Month)
            .ThenBy(r => r.PaymentDate)
            .ToList();
    }

    public async Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default)
    {
        var record = await _context.MonthlyJobStats
            .FirstOrDefaultAsync(m => m.Aid == aid, ct)
            ?? throw new KeyNotFoundException($"MonthlyJobStats record with aid {aid} not found.");

        record.CountActivePlayersToDate = request.CountActivePlayersToDate;
        record.CountActivePlayersToDateLastMonth = request.CountActivePlayersToDateLastMonth;
        record.CountNewPlayersThisMonth = request.CountNewPlayersThisMonth;
        record.CountActiveTeamsToDate = request.CountActiveTeamsToDate;
        record.CountActiveTeamsToDateLastMonth = request.CountActiveTeamsToDateLastMonth;
        record.CountNewTeamsThisMonth = request.CountNewTeamsThisMonth;
        record.LebUserId = userId;
        record.Modified = DateTime.Now;

        await _context.SaveChangesAsync(ct);
    }

}
