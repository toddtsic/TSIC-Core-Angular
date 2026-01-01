using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Shared.TextSubstitution;

namespace TSIC.API.Services.Players;

public sealed class PlayerRegConfirmationService : IPlayerRegConfirmationService
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _regRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly ITextSubstitutionService _subs;
    private readonly ILogger<PlayerRegConfirmationService> _logger;

    public PlayerRegConfirmationService(
        IJobRepository jobRepo,
        IRegistrationRepository regRepo,
        IRegistrationAccountingRepository accountingRepo,
        ITextSubstitutionService subs,
        ILogger<PlayerRegConfirmationService> logger)
    {
        _jobRepo = jobRepo;
        _regRepo = regRepo;
        _accountingRepo = accountingRepo;
        _subs = subs;
        _logger = logger;
    }

    public async Task<PlayerRegConfirmationDto> BuildAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        var job = await _jobRepo.GetConfirmationInfoAsync(jobId, ct);
        if (job == null)
        {
            _logger.LogWarning("Confirmation build: job {JobId} not found", jobId);
            return EmptyDto();
        }

        var regs = await _regRepo.GetConfirmationDataAsync(jobId, familyUserId, ct);
        var tsic = await BuildTsicFinancialAsync(regs, ct);
        var insurance = BuildInsuranceStatus(regs);
        Guid? firstRegistrationId = regs.FirstOrDefault()?.RegistrationId;
        var html = await BuildConfirmationHtmlAsync(job.JobPath, job.PlayerRegConfirmationOnScreen, familyUserId, firstRegistrationId);
        return new PlayerRegConfirmationDto(tsic, insurance, html);
    }

    public async Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        // For email, use the Job.PlayerRegConfirmationEmail template (not the on-screen variant)
        var job = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, ct);
        if (job == null)
        {
            _logger.LogWarning("Email confirmation build: job {JobId} not found", jobId);
            return (string.Empty, string.Empty);
        }

        var regs = await _regRepo.GetConfirmationDataAsync(jobId, familyUserId, ct);
        Guid? firstRegistrationId = regs.FirstOrDefault()?.RegistrationId;
        string subject = string.IsNullOrWhiteSpace(job.JobName) ? "Registration Confirmation" : $"{job.JobName} Registration Confirmation";
        string? template = job.PlayerRegConfirmationEmail;
        if (string.IsNullOrWhiteSpace(template)) return (subject, string.Empty);
        // Ensure email mode token present for inline-styled email output
        if (!template.Contains("!EMAILMODE", StringComparison.OrdinalIgnoreCase))
        {
            template = "!EMAILMODE " + template;
        }
        try
        {
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
            var html = await _subs.SubstituteAsync(job.JobPath, ccPaymentMethodId, firstRegistrationId, familyUserId, template);
            return (subject, html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email substitution failed for jobPath {JobPath}", job.JobPath);
            return (subject, string.Empty);
        }
    }

    private async Task<PlayerRegTsicFinancialDto> BuildTsicFinancialAsync(List<RegistrationConfirmationData> regs, CancellationToken ct)
    {
        var totalOriginal = regs.Sum(r => r.FeeTotal);
        var totalDiscounts = 0m;
        var totalNet = totalOriginal - totalDiscounts;
        bool wasArb = regs.Exists(r => !string.IsNullOrWhiteSpace(r.AdnSubscriptionId) && string.Equals(r.AdnSubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase));
        decimal amountCharged = regs.Sum(r => r.PaidTotal);
        bool wasImmediateCharge = amountCharged > 0m && !wasArb;
        var regIds = regs.Select(r => r.RegistrationId).ToList();
        string? transactionId = await _accountingRepo.GetLatestAdnTransactionIdAsync(regIds, ct);
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

    private static PlayerRegInsuranceStatusDto BuildInsuranceStatus(List<RegistrationConfirmationData> regs)
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
