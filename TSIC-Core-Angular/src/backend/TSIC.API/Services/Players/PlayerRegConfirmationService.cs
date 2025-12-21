using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.API.Services.Shared;

namespace TSIC.API.Services.Players;

public sealed class PlayerRegConfirmationService : IPlayerRegConfirmationService
{
    private sealed record RegRow(
        Guid RegistrationId,
        string PlayerFirst,
        string PlayerLast,
        string TeamName,
        decimal FeeTotal,
        decimal PaidTotal,
        decimal OwedTotal,
        string? RegsaverPolicyId,
        DateTime? RegsaverPolicyIdCreateDate,
        string? AdnSubscriptionId,
        string? AdnSubscriptionStatus,
        DateTime? AdnSubscriptionStartDate,
        int? AdnSubscriptionIntervalLength,
        int? AdnSubscriptionBillingOccurences,
        decimal? AdnSubscriptionAmountPerOccurence);
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
        // Deconstruct tuple to avoid nullable tuple member access issues.
        var (_, _, jobPath, _, confirmationTemplate) = job.Value; // jobName unused; confirmationTemplate may be null

        var regs = await LoadRegistrationsAsync(jobId, familyUserId, ct);
        var tsic = await BuildTsicFinancialAsync(regs, ct);
        var insurance = BuildInsuranceStatus(regs);
        Guid? firstRegistrationId = regs.FirstOrDefault()?.RegistrationId;
        var html = await BuildConfirmationHtmlAsync(jobPath, confirmationTemplate, familyUserId, firstRegistrationId);
        return new PlayerRegConfirmationDto(tsic, insurance, html);
    }

    public async Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        // For email, use the Job.PlayerRegConfirmationEmail template (not the on-screen variant)
        var job = await LoadJobEmailAsync(jobId, ct);
        if (job == null)
        {
            _logger.LogWarning("Email confirmation build: job {JobId} not found", jobId);
            return (string.Empty, string.Empty);
        }
        var (_, jobName, jobPath, _, confirmationTemplate) = job.Value;
        var regs = await LoadRegistrationsAsync(jobId, familyUserId, ct);
        Guid? firstRegistrationId = regs.FirstOrDefault()?.RegistrationId;
        string subject = string.IsNullOrWhiteSpace(jobName) ? "Registration Confirmation" : $"{jobName} Registration Confirmation";
        string? template = confirmationTemplate;
        if (string.IsNullOrWhiteSpace(template)) return (subject, string.Empty);
        // Ensure email mode token present for inline-styled email output
        if (!template.Contains("!EMAILMODE", StringComparison.OrdinalIgnoreCase))
        {
            template = "!EMAILMODE " + template;
        }
        try
        {
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
            var html = await _subs.SubstituteAsync(jobPath, ccPaymentMethodId, firstRegistrationId, familyUserId, template);
            return (subject, html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email substitution failed for jobPath {JobPath}", jobPath);
            return (subject, string.Empty);
        }
    }

    private async Task<(Guid JobId, string? JobName, string JobPath, bool? AdnArb, string? PlayerRegConfirmationOnScreen)?> LoadJobAsync(Guid jobId, CancellationToken ct)
    {
        var x = await _db.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationOnScreen })
            .FirstOrDefaultAsync(ct);
        if (x == null) return null;
        return (x.JobId, x.JobName, x.JobPath, x.AdnArb, x.PlayerRegConfirmationOnScreen);
    }

    private async Task<(Guid JobId, string? JobName, string JobPath, bool? AdnArb, string? PlayerRegConfirmationEmail)?> LoadJobEmailAsync(Guid jobId, CancellationToken ct)
    {
        var x = await _db.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationEmail })
            .FirstOrDefaultAsync(ct);
        if (x == null) return null;
        return (x.JobId, x.JobName, x.JobPath, x.AdnArb, x.PlayerRegConfirmationEmail);
    }

    private Task<List<RegRow>> LoadRegistrationsAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        return _db.Registrations.AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId)
            .Select(r => new RegRow(
                r.RegistrationId,
                (r.User != null ? r.User.FirstName : string.Empty) ?? string.Empty,
                (r.User != null ? r.User.LastName : string.Empty) ?? string.Empty,
                (r.AssignedTeam != null ? r.AssignedTeam.TeamName : string.Empty) ?? string.Empty,
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
                r.AdnSubscriptionAmountPerOccurence))
            .ToListAsync(ct);
    }

    private async Task<PlayerRegTsicFinancialDto> BuildTsicFinancialAsync(List<RegRow> regs, CancellationToken ct)
    {
        var totalOriginal = regs.Sum(r => r.FeeTotal);
        var totalDiscounts = 0m;
        var totalNet = totalOriginal - totalDiscounts;
        bool wasArb = regs.Exists(r => !string.IsNullOrWhiteSpace(r.AdnSubscriptionId) && string.Equals(r.AdnSubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase));
        decimal amountCharged = regs.Sum(r => r.PaidTotal);
        bool wasImmediateCharge = amountCharged > 0m && !wasArb;
        var regIds = regs.Select(r => r.RegistrationId).ToHashSet();
        string? transactionId = await _db.RegistrationAccounting.AsNoTracking()
            .Where(a => a.RegistrationId != null && regIds.Contains(a.RegistrationId.Value) && !string.IsNullOrWhiteSpace(a.AdnTransactionId))
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
        var lines = regs
            .Select(r => new PlayerRegFinancialLineDto(
                r.RegistrationId,
                ($"{r.PlayerFirst} {r.PlayerLast}").Trim(),
                r.TeamName,
                r.FeeTotal,
                new List<string>()))
            .ToList();
        return new PlayerRegTsicFinancialDto(wasImmediateCharge, wasArb, amountCharged, "USD", transactionId, null, nextArbBillDate, totalOriginal, totalDiscounts, totalNet, lines);
    }

    private static PlayerRegInsuranceStatusDto BuildInsuranceStatus(List<RegRow> regs)
    {
        var policies = regs
            .Where(r => !string.IsNullOrWhiteSpace(r.RegsaverPolicyId))
            .Select(r => new PlayerRegPolicyDto(r.RegistrationId, r.RegsaverPolicyId!, r.RegsaverPolicyIdCreateDate ?? DateTime.UtcNow, 0))
            .ToList();
        return new PlayerRegInsuranceStatusDto(regs.Count > 0, policies.Count > 0, policies.Count == 0 && regs.Count > 0, policies.Count > 0, policies);
    }

    private async Task<string> BuildConfirmationHtmlAsync(string jobPath, string? template, string familyUserId, Guid? registrationId)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        try
        {
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
            return await _subs.SubstituteAsync(jobPath, ccPaymentMethodId, registrationId, familyUserId, template);
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
