using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Dtos;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface IPlayerRegConfirmationService
{
    Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct);
}

public sealed class PlayerRegConfirmationService : IPlayerRegConfirmationService
{
    private readonly SqlDbContext _db;
    private readonly ITextSubstitutionService _subs;
    private readonly ILogger<PlayerRegConfirmationService> _logger;

    public PlayerRegConfirmationService(SqlDbContext db, ITextSubstitutionService subs, ILogger<PlayerRegConfirmationService> logger)
    {
        _db = db;
        _subs = subs;
        _logger = logger;
    }

    public async Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        var job = await LoadJobAsync(jobId, ct);
        if (job == null)
        {
            _logger.LogWarning("Confirmation build: job {JobId} not found", jobId);
            return EmptyDto();
        }
        var regs = await LoadRegistrationsAsync(jobId, familyUserId, ct);
        var tsic = await BuildTsicFinancialAsync(regs, ct);
        var insurance = BuildInsuranceStatus(regs);
        var html = await BuildConfirmationHtmlAsync(job.JobPath, job.PlayerRegConfirmationOnScreen, familyUserId);
        return new PlayerRegConfirmationDto(tsic, insurance, html);
    }

    private Task<(Guid JobId, string JobName, string JobPath, bool? AdnArb, string? PlayerRegConfirmationOnScreen, int? Arbrecurintervalnumber, int? Arbrecurinterval)?> LoadJobAsync(Guid jobId, CancellationToken ct)
    {
        return _db.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationOnScreen, j.Arbrecurintervalnumber, j.Arbrecurinterval })
            .Select(x => (x.JobId, x.JobName, x.JobPath, x.AdnArb, x.PlayerRegConfirmationOnScreen, x.Arbrecurintervalnumber, x.Arbrecurinterval))
            .FirstOrDefaultAsync(ct);
    }

    private Task<List<dynamic>> LoadRegistrationsAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        return _db.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId)
            .Select(r => new
            {
                r.RegistrationId,
                PlayerFirst = r.User != null ? r.User.FirstName : string.Empty,
                PlayerLast = r.User != null ? r.User.LastName : string.Empty,
                TeamName = r.AssignedTeam != null ? r.AssignedTeam.TeamName : string.Empty,
                r.FeeTotal,
                r.PaidTotal,
                r.OwedTotal,
                r.RegsaverPolicyId,
                r.RegsaverPolicyIdCreateDate,
                r.AdnSubscriptionId,
                r.AdnSubscriptionStatus,
                r.AdnSubscriptionStartDate,
                r.AdnSubscriptionIntervalLength,
                r.AdnSubscriptionBillingOccurences,
                r.AdnSubscriptionAmountPerOccurence
            })
            .Cast<dynamic>()
            .ToListAsync(ct);
    }

    private async Task<PlayerRegTsicFinancialDto> BuildTsicFinancialAsync(List<dynamic> regs, CancellationToken ct)
    {
        var totalOriginal = regs.Sum(r => (decimal)r.FeeTotal);
        var totalDiscounts = 0m;
        var totalNet = totalOriginal - totalDiscounts;
        bool wasArb = regs.Any(r => !string.IsNullOrWhiteSpace((string?)r.AdnSubscriptionId) && string.Equals((string?)r.AdnSubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase));
        decimal amountCharged = regs.Sum(r => (decimal)(r.PaidTotal ?? 0m));
        bool wasImmediateCharge = amountCharged > 0m && !wasArb;
        string? transactionId = await _db.RegistrationAccounting.AsNoTracking()
            .Where(a => regs.Select(r => (Guid)r.RegistrationId).Contains(a.RegistrationId) && !string.IsNullOrWhiteSpace(a.AdnTransactionId))
            .OrderByDescending(a => a.Createdate)
            .Select(a => a.AdnTransactionId)
            .FirstOrDefaultAsync(ct);
        DateTime? nextArbBillDate = null;
        if (wasArb)
        {
            var firstSub = regs.Where(r => r.AdnSubscriptionStartDate != null && r.AdnSubscriptionIntervalLength != null)
                .OrderBy(r => r.AdnSubscriptionStartDate)
                .FirstOrDefault();
            if (firstSub != null)
            {
                try
                {
                    nextArbBillDate = ((DateTime)firstSub.AdnSubscriptionStartDate!).AddMonths((int)firstSub.AdnSubscriptionIntervalLength!);
                }
                catch (Exception ex)
                {
                    // Interval length or start date invalid; safe to ignore and leave nextArbBillDate null.
                    _logger.LogDebug(ex, "ARB next billing date computation failed; ignoring.");
                }
            }
        }
        var lines = regs.Select(r => new PlayerRegFinancialLineDto(
            (Guid)r.RegistrationId,
            ($"{r.PlayerFirst} {r.PlayerLast}").Trim(),
            (string)r.TeamName,
            (decimal)r.FeeTotal,
            new List<string>())).ToList();
        return new PlayerRegTsicFinancialDto(wasImmediateCharge, wasArb, amountCharged, "USD", transactionId, null, nextArbBillDate, totalOriginal, totalDiscounts, totalNet, lines);
    }

    private static PlayerRegInsuranceStatusDto BuildInsuranceStatus(List<dynamic> regs)
    {
        var policies = regs.Where(r => !string.IsNullOrWhiteSpace((string?)r.RegsaverPolicyId))
            .Select(r => new PlayerRegPolicyDto((Guid)r.RegistrationId, (string)r.RegsaverPolicyId!, (DateTime?)(r.RegsaverPolicyIdCreateDate ?? DateTime.UtcNow) ?? DateTime.UtcNow, 0))
            .ToList();
        return new PlayerRegInsuranceStatusDto(regs.Count > 0, policies.Count > 0, policies.Count == 0 && regs.Count > 0, policies.Count > 0, policies);
    }

    private async Task<string> BuildConfirmationHtmlAsync(string jobPath, string? template, string familyUserId)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        try
        {
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
            return await _subs.SubstituteAsync(jobPath, ccPaymentMethodId, null, familyUserId, template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Substitution failed for jobPath {JobPath}", jobPath);
            return string.Empty;
        }
    }

    private static PlayerRegConfirmationDto EmptyDto() => new PlayerRegConfirmationDto(
        new PlayerRegTsicFinancialDto(false, false, 0m, "USD", null, null, null, 0m, 0m, 0m, new List<PlayerRegFinancialLineDto>()),
        new PlayerRegInsuranceStatusDto(false, false, false, false, new List<PlayerRegPolicyDto>()),
        string.Empty);
}
