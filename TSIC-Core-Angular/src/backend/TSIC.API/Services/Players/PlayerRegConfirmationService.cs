using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Players;

public sealed class PlayerRegConfirmationService : IPlayerRegConfirmationService
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _regRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IFamilyRepository _familyRepo;
    private readonly ITextSubstitutionService _subs;
    private readonly IEmailService _email;
    private readonly ILogger<PlayerRegConfirmationService> _logger;

    public PlayerRegConfirmationService(
        IJobRepository jobRepo,
        IRegistrationRepository regRepo,
        IRegistrationAccountingRepository accountingRepo,
        IFamilyRepository familyRepo,
        ITextSubstitutionService subs,
        IEmailService email,
        ILogger<PlayerRegConfirmationService> logger)
    {
        _jobRepo = jobRepo;
        _regRepo = regRepo;
        _accountingRepo = accountingRepo;
        _familyRepo = familyRepo;
        _subs = subs;
        _email = email;
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

    public async Task<PlayerRegConfirmationDto> BuildAsync(string jobPath, string familyUserId, CancellationToken ct)
    {
        var jobId = await _jobRepo.GetJobIdByPathAsync(jobPath, ct);
        if (jobId == null)
        {
            _logger.LogWarning("Confirmation build: job {JobPath} not found", jobPath);
            return EmptyDto();
        }
        return await BuildAsync(jobId.Value, familyUserId, ct);
    }

    // Inline-styled banner prepended to the confirmation email body when the customer paid by
    // eCheck. Inline styling only — email clients ignore <style> blocks. The payment is booked
    // at submit (optimistic); this note sets the bank-draft expectation the standard
    // receipt-style template doesn't carry, and makes no contact promises (no per-incident
    // emails exist — NSF handling surfaces in the support digest).
    // KEEP IDENTICAL to the literal in TeamRegistrationService.
    private const string EcheckPendingBanner =
        "<div style=\"background:#fff7e6;border:1px solid #ffd591;border-left:4px solid #fa8c16;" +
        "padding:14px 16px;margin:0 0 18px;border-radius:4px;font-family:Arial,sans-serif;color:#612500;\">" +
        "<div style=\"font-weight:bold;margin-bottom:6px;font-size:14px;\">Paid by eCheck</div>" +
        "<div style=\"font-size:13px;line-height:1.5;\">" +
        "Your payment was made by eCheck (bank draft). " +
        "<strong>Bank drafts typically finalize within 3 to 5 business days.</strong> " +
        "In the rare case your bank returns the draft (insufficient funds or other), your balance is restored automatically." +
        "</div></div>";

    public async Task<(string Subject, string Html)> BuildEmailAsync(Guid jobId, string familyUserId, CancellationToken ct, bool isEcheckPending = false)
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
            var html = await _subs.SubstituteAsync(job.JobPath, jobId, ccPaymentMethodId, firstRegistrationId, familyUserId, template);
            if (isEcheckPending && !string.IsNullOrEmpty(html))
            {
                html = EcheckPendingBanner + html;
            }
            return (subject, html);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email substitution failed for jobPath {JobPath}", job.JobPath);
            return (subject, string.Empty);
        }
    }

    public async Task<(string Subject, string Html)> BuildEmailAsync(string jobPath, string familyUserId, CancellationToken ct, bool isEcheckPending = false)
    {
        var jobId = await _jobRepo.GetJobIdByPathAsync(jobPath, ct);
        if (jobId == null)
        {
            _logger.LogWarning("Email confirmation build: job {JobPath} not found", jobPath);
            return (string.Empty, string.Empty);
        }
        return await BuildEmailAsync(jobId.Value, familyUserId, ct, isEcheckPending);
    }

    public async Task<PlayerConfirmationSendResult> SendConfirmationAsync(Guid jobId, string familyUserId, CancellationToken ct)
    {
        // Distinct recipient set: mom/dad from Families ∪ every family player email in the job.
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fam = await _familyRepo.GetByFamilyUserIdAsync(familyUserId, ct);
        if (fam != null)
        {
            if (!string.IsNullOrWhiteSpace(fam.MomEmail)) recipients.Add(fam.MomEmail.Trim());
            if (!string.IsNullOrWhiteSpace(fam.DadEmail)) recipients.Add(fam.DadEmail.Trim());
        }
        var playerEmails = await _familyRepo.GetFamilyPlayerEmailsForJobAsync(jobId, familyUserId, ct);
        foreach (var email in playerEmails)
        {
            var e = email.Trim();
            if (!string.IsNullOrWhiteSpace(e)) recipients.Add(e);
        }
        // One address rule for every send path — see EmailAddressRules.
        var toList = recipients
            .Select(x => x.Trim())
            .Where(EmailAddressRules.IsSendable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (toList.Count == 0) return Failure(PlayerConfirmationSendFailure.NoRecipients);

        var (subject, html) = await BuildEmailAsync(jobId, familyUserId, ct);
        if (string.IsNullOrWhiteSpace(html)) return Failure(PlayerConfirmationSendFailure.NoContent);

        var dto = new EmailMessageDto
        {
            Subject = string.IsNullOrWhiteSpace(subject) ? "Registration Confirmation" : subject,
            HtmlBody = html
        };
        dto.ToAddresses.AddRange(toList);

        // Redelivery: Reply-To still routes replies to the club, but the job's CC/BCC audience got
        // the original — a resend is not a new confirmation event.
        var jobInfo = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, ct);
        if (jobInfo != null) JobConfirmationCopies.Apply(dto, jobInfo, includeCopies: false);

        var ok = await _email.SendAsync(dto, sendInDevelopment: false, cancellationToken: ct);
        return ok
            ? new PlayerConfirmationSendResult { Sent = true, Failure = PlayerConfirmationSendFailure.None, Recipients = toList }
            : Failure(PlayerConfirmationSendFailure.SendFailed);
    }

    public async Task<PlayerConfirmationSendResult> SendConfirmationAsync(string jobPath, string familyUserId, CancellationToken ct)
    {
        var jobId = await _jobRepo.GetJobIdByPathAsync(jobPath, ct);
        if (jobId == null)
        {
            _logger.LogWarning("Confirmation resend: job {JobPath} not found", jobPath);
            return Failure(PlayerConfirmationSendFailure.JobNotFound);
        }
        return await SendConfirmationAsync(jobId.Value, familyUserId, ct);
    }

    private static PlayerConfirmationSendResult Failure(PlayerConfirmationSendFailure failure)
        => new() { Sent = false, Failure = failure };

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
            .Select(r => new PlayerRegPolicyDto(r.RegistrationId, r.RegsaverPolicyId!, r.RegsaverPolicyIdCreateDate ?? DateTime.Now, 0))
            .ToList();
        return new PlayerRegInsuranceStatusDto(regs.Count > 0, policies.Count > 0, policies.Count == 0 && regs.Count > 0, policies.Count > 0, policies);
    }

    private async Task<string> BuildConfirmationHtmlAsync(string jobPath, string? template, string familyUserId, Guid? registrationId)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        try
        {
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

            // Get jobId from jobPath for substitution service
            var jobId = await _jobRepo.GetJobIdByPathAsync(jobPath);
            if (!jobId.HasValue) return string.Empty;

            return await _subs.SubstituteAsync(jobPath, jobId.Value, ccPaymentMethodId, registrationId, familyUserId, template);
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
