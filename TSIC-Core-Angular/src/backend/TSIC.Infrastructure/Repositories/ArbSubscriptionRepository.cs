using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.Arb;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ArbSubscriptionRepository : IArbSubscriptionRepository
{
    private static readonly Guid CreditCardPaymentMethodId =
        Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
    /// <summary>
    /// Marker the sweep stamps into Registration_Accounting.paymeth for every ARB draft it books
    /// (AdnSweepService.ImportRegistrationArbTransactionAsync). Paired with payamt = 0 it identifies
    /// a draft that did NOT settle: a settled installment always books a non-zero payamt, so the two
    /// together separate declined drafts from successful ones without parsing the comment string.
    /// </summary>
    private const string ArbDraftPaymethMarker = "on subscriptionId:";

    private readonly SqlDbContext _context;

    public ArbSubscriptionRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ArbRegistrationProjection>> GetActiveSubscriptionsForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r =>
                r.JobId == jobId
                && !string.IsNullOrEmpty(r.AdnSubscriptionId)
                && r.BActive == true)
            .Select(r => new ArbRegistrationProjection
            {
                RegistrationId = r.RegistrationId,
                SubscriptionId = r.AdnSubscriptionId!,
                SubscriptionStatus = r.AdnSubscriptionStatus,
                SubscriptionStartDate = r.AdnSubscriptionStartDate,
                BillingOccurrences = r.AdnSubscriptionBillingOccurences,
                AmountPerOccurrence = r.AdnSubscriptionAmountPerOccurence,
                IntervalLength = r.AdnSubscriptionIntervalLength,
                RegistrantName = $"{r.User!.LastName}, {r.User.FirstName}",
                FirstName = r.User.FirstName,
                LastName = r.User.LastName,
                Assignment = r.Assignment,
                FamilyUsername = r.FamilyUser != null && r.FamilyUser.FamilyUser != null
                    ? r.FamilyUser.FamilyUser.UserName : null,
                Role = r.Role!.Name,
                RegistrantEmail = r.User.Email,
                MomName = r.FamilyUser != null
                    ? $"{r.FamilyUser.MomFirstName} {r.FamilyUser.MomLastName}" : null,
                MomEmail = r.FamilyUser != null ? r.FamilyUser.MomEmail : null,
                MomPhone = r.FamilyUser != null ? r.FamilyUser.MomCellphone : null,
                DadName = r.FamilyUser != null
                    ? $"{r.FamilyUser.DadFirstName} {r.FamilyUser.DadLastName}" : null,
                DadEmail = r.FamilyUser != null ? r.FamilyUser.DadEmail : null,
                DadPhone = r.FamilyUser != null ? r.FamilyUser.DadCellphone : null,
                FeeTotal = r.FeeTotal,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
                JobName = r.Job!.JobName ?? r.Job.DisplayName ?? "",
                JobPath = r.Job.JobPath ?? "",
                JobId = r.JobId,
                BemailOptOut = r.BemailOptOut,
                LastFailedDraftDate = _context.RegistrationAccounting
                    .Where(ra => ra.RegistrationId == r.RegistrationId
                        && ra.Active == true
                        && ra.AdnTransactionId != null
                        && (ra.Payamt ?? 0m) == 0m
                        && ra.Paymeth != null && ra.Paymeth.Contains(ArbDraftPaymethMarker))
                    .Max(ra => (DateTime?)ra.Createdate),
                LastArbDraftDate = _context.RegistrationAccounting
                    .Where(ra => ra.RegistrationId == r.RegistrationId
                        && ra.Active == true
                        && ra.AdnTransactionId != null
                        && ra.Paymeth != null && ra.Paymeth.Contains(ArbDraftPaymethMarker))
                    .Max(ra => (DateTime?)ra.Createdate),
            })
            .ToListAsync(ct);
    }

    public async Task<List<ArbRegistrationProjection>> GetRegistrationsByInvoiceNumbersAsync(
        List<string> invoiceNumbers, Guid? jobIdFilter,
        CancellationToken ct = default)
    {
        var query = _context.RegistrationAccounting
            .AsNoTracking()
            .Where(ra => invoiceNumbers.Contains(ra.AdnInvoiceNo!));

        if (jobIdFilter.HasValue)
            query = query.Where(ra => ra.Registration!.JobId == jobIdFilter.Value);

        var regIds = await query
            .Select(ra => ra.Registration!.RegistrationId)
            .Distinct()
            .ToListAsync(ct);

        return await _context.Registrations
            .AsNoTracking()
            .Where(r => regIds.Contains(r.RegistrationId))
            .Select(r => new ArbRegistrationProjection
            {
                RegistrationId = r.RegistrationId,
                SubscriptionId = r.AdnSubscriptionId ?? string.Empty,
                SubscriptionStatus = r.AdnSubscriptionStatus,
                SubscriptionStartDate = r.AdnSubscriptionStartDate,
                BillingOccurrences = r.AdnSubscriptionBillingOccurences,
                AmountPerOccurrence = r.AdnSubscriptionAmountPerOccurence,
                IntervalLength = r.AdnSubscriptionIntervalLength,
                RegistrantName = $"{r.User!.LastName}, {r.User.FirstName}",
                FirstName = r.User.FirstName,
                LastName = r.User.LastName,
                Assignment = r.Assignment,
                FamilyUsername = r.FamilyUser != null && r.FamilyUser.FamilyUser != null
                    ? r.FamilyUser.FamilyUser.UserName : null,
                Role = r.Role!.Name,
                RegistrantEmail = r.User.Email,
                MomName = r.FamilyUser != null
                    ? $"{r.FamilyUser.MomFirstName} {r.FamilyUser.MomLastName}" : null,
                MomEmail = r.FamilyUser != null ? r.FamilyUser.MomEmail : null,
                MomPhone = r.FamilyUser != null ? r.FamilyUser.MomCellphone : null,
                DadName = r.FamilyUser != null
                    ? $"{r.FamilyUser.DadFirstName} {r.FamilyUser.DadLastName}" : null,
                DadEmail = r.FamilyUser != null ? r.FamilyUser.DadEmail : null,
                DadPhone = r.FamilyUser != null ? r.FamilyUser.DadCellphone : null,
                FeeTotal = r.FeeTotal,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
                JobName = r.Job!.JobName ?? r.Job.DisplayName ?? "",
                JobPath = r.Job.JobPath ?? "",
                JobId = r.JobId,
                BemailOptOut = r.BemailOptOut,
                LastFailedDraftDate = _context.RegistrationAccounting
                    .Where(ra => ra.RegistrationId == r.RegistrationId
                        && ra.Active == true
                        && ra.AdnTransactionId != null
                        && (ra.Payamt ?? 0m) == 0m
                        && ra.Paymeth != null && ra.Paymeth.Contains(ArbDraftPaymethMarker))
                    .Max(ra => (DateTime?)ra.Createdate),
                LastArbDraftDate = _context.RegistrationAccounting
                    .Where(ra => ra.RegistrationId == r.RegistrationId
                        && ra.Active == true
                        && ra.AdnTransactionId != null
                        && ra.Paymeth != null && ra.Paymeth.Contains(ArbDraftPaymethMarker))
                    .Max(ra => (DateTime?)ra.Createdate),
            })
            .ToListAsync(ct);
    }

    public async Task<ArbRegistrationDetail?> GetRegistrationArbDetailAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        var detail = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new ArbRegistrationDetail
            {
                RegistrationId = r.RegistrationId,
                JobId = r.JobId,
                SubscriptionId = r.AdnSubscriptionId ?? string.Empty,
                SubscriptionStatus = r.AdnSubscriptionStatus,
                SubscriptionStartDate = r.AdnSubscriptionStartDate,
                BillingOccurrences = r.AdnSubscriptionBillingOccurences,
                AmountPerOccurrence = r.AdnSubscriptionAmountPerOccurence,
                IntervalLength = r.AdnSubscriptionIntervalLength,
                RegistrantName = $"{r.User!.FirstName} {r.User.LastName}",
                JobName = r.Job!.JobName ?? r.Job.DisplayName ?? "",
                FeeTotal = r.FeeTotal,
                PaidTotal = r.PaidTotal,
                FirstInvoiceNumber = _context.RegistrationAccounting
                    .Where(ra =>
                        ra.RegistrationId == registrationId
                        && ra.AdnInvoiceNo != null)
                    .Select(ra => ra.AdnInvoiceNo)
                    .FirstOrDefault(),
                LastFailedDraftDate = _context.RegistrationAccounting
                    .Where(ra => ra.RegistrationId == registrationId
                        && ra.Active == true
                        && ra.AdnTransactionId != null
                        && (ra.Payamt ?? 0m) == 0m
                        && ra.Paymeth != null && ra.Paymeth.Contains(ArbDraftPaymethMarker))
                    .Max(ra => (DateTime?)ra.Createdate),
                LastArbDraftDate = _context.RegistrationAccounting
                    .Where(ra => ra.RegistrationId == registrationId
                        && ra.Active == true
                        && ra.AdnTransactionId != null
                        && ra.Paymeth != null && ra.Paymeth.Contains(ArbDraftPaymethMarker))
                    .Max(ra => (DateTime?)ra.Createdate)
            })
            .FirstOrDefaultAsync(ct);

        return detail;
    }

    public async Task<decimal> GetArbPaymentsTotalAsync(
        Guid registrationId, CancellationToken ct = default)
    {
        return await _context.RegistrationAccounting
            .AsNoTracking()
            .Where(ra =>
                ra.Active == true
                && ra.RegistrationId == registrationId
                && ra.PaymentMethodId == CreditCardPaymentMethodId
                && ra.Paymeth != null && ra.Paymeth.Contains("on subscriptionId:"))
            .SumAsync(ra => ra.Payamt ?? 0m, ct);
    }

    public async Task<List<ArbDirectorProjection>> GetDirectorsForJobsAsync(
        List<Guid> jobIds, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r =>
                jobIds.Contains(r.JobId)
                && r.Role!.Name == "Director"
                && r.BActive == true)
            .Select(r => new ArbDirectorProjection
            {
                JobId = r.JobId,
                Name = $"{r.User!.FirstName} {r.User.LastName}",
                Email = r.User.Email ?? string.Empty
            })
            .ToListAsync(ct);
    }

    public async Task<List<ArbDirectorProjection>> GetDefaultDirectorsForJobsAsync(
        List<Guid> jobIds, CancellationToken ct = default)
    {
        // Pull every candidate, then pick in memory: the "primary contact, else earliest" rule is a
        // per-group ordering, and the set is tiny (a handful of directors per job).
        var candidates = await _context.Registrations
            .AsNoTracking()
            .Where(r =>
                jobIds.Contains(r.JobId)
                && r.Role!.Name == "Director"
                && r.BActive == true)
            .Select(r => new
            {
                r.JobId,
                r.RegistrationAi,
                IsPrimary = r.Job!.PrimaryContactRegistrationId == r.RegistrationId,
                Name = $"{r.User!.FirstName} {r.User.LastName}",
                Email = r.User.Email ?? string.Empty
            })
            .ToListAsync(ct);

        return candidates
            // Drop the unusable BEFORE the pick, so a starred primary contact with no email address
            // hands off to the next director rather than leaving the job with no sender at all.
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => c.JobId)
            .Select(g => g
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.RegistrationAi)
                .First())
            .Select(c => new ArbDirectorProjection
            {
                JobId = c.JobId,
                Name = c.Name,
                Email = c.Email
            })
            .ToList();
    }

    public async Task<List<Guid>> GetJobIdsWithLiveSubscriptionsAsync(CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r =>
                r.AdnSubscriptionId != null
                && r.AdnSubscriptionId != ""
                && (r.AdnSubscriptionStatus == "active" || r.AdnSubscriptionStatus == "suspended"))
            .Select(r => r.JobId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<(string Email, string DisplayName)?> GetSenderInfoAsync(
        string userId, CancellationToken ct = default)
    {
        var result = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, DisplayName = $"{u.FirstName} {u.LastName}" })
            .FirstOrDefaultAsync(ct);

        if (result == null || string.IsNullOrEmpty(result.Email))
            return null;

        return (result.Email, result.DisplayName);
    }

    public async Task UpdateSubscriptionStatusAsync(
        Guid registrationId, string newStatus, CancellationToken ct = default)
    {
        var reg = await _context.Registrations.FindAsync(new object[] { registrationId }, ct);
        if (reg != null && reg.AdnSubscriptionStatus != newStatus)
        {
            reg.AdnSubscriptionStatus = newStatus;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<List<ArbStatusRefreshTarget>> GetStatusRefreshTargetsForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Where(r =>
                r.JobId == jobId
                && !string.IsNullOrEmpty(r.AdnSubscriptionId))
            .Select(r => new ArbStatusRefreshTarget
            {
                RegistrationId = r.RegistrationId,
                SubscriptionId = r.AdnSubscriptionId!,
                SubscriptionStatus = r.AdnSubscriptionStatus
            })
            .ToListAsync(ct);
    }

    public async Task UpdateSubscriptionStatusesAsync(
        IReadOnlyDictionary<Guid, string> statusByRegistrationId, CancellationToken ct = default)
    {
        if (statusByRegistrationId.Count == 0) return;

        var ids = statusByRegistrationId.Keys.ToList();
        var regs = await _context.Registrations
            .Where(r => ids.Contains(r.RegistrationId))
            .ToListAsync(ct);

        foreach (var reg in regs)
        {
            var newStatus = statusByRegistrationId[reg.RegistrationId];
            if (reg.AdnSubscriptionStatus != newStatus)
                reg.AdnSubscriptionStatus = newStatus;
        }

        await _context.SaveChangesAsync(ct);
    }
}
