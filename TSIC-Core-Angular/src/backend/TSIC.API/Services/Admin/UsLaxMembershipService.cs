using System.Globalization;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

public sealed class UsLaxMembershipService : IUsLaxMembershipService
{
    private readonly IRegistrationRepository _registrations;
    private readonly IUsLaxService _usLax;
    private readonly IJobRepository _jobs;
    private readonly IEmailService _emailService;
    private readonly IEmailLogRepository _emailLogs;
    private readonly ILogger<UsLaxMembershipService> _logger;

    public UsLaxMembershipService(
        IRegistrationRepository registrations,
        IUsLaxService usLax,
        IJobRepository jobs,
        IEmailService emailService,
        IEmailLogRepository emailLogs,
        ILogger<UsLaxMembershipService> logger)
    {
        _registrations = registrations;
        _usLax = usLax;
        _jobs = jobs;
        _emailService = emailService;
        _emailLogs = emailLogs;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UsLaxReconciliationCandidateDto>> GetCandidatesAsync(Guid jobId, UsLaxMembershipRole role, CancellationToken ct = default)
    {
        var rows = await _registrations.GetUsLaxReconciliationCandidatesAsync(jobId, role, ct);
        return rows.Select(r => new UsLaxReconciliationCandidateDto
        {
            RegistrationId = r.RegistrationId,
            FirstName = r.FirstName,
            LastName = r.LastName,
            Email = r.Email,
            Dob = r.Dob,
            MembershipId = r.SportAssnId,
            CurrentExpiryDate = r.SportAssnIdexpDate,
            TeamName = r.TeamName
        }).ToList();
    }

    public async Task<UsLaxReconciliationResponse> ReconcileAsync(Guid jobId, UsLaxReconciliationRequest request, CancellationToken ct = default)
    {
        var candidates = await _registrations.GetUsLaxReconciliationCandidatesAsync(jobId, request.Role, ct);

        if (request.RegistrationIds is { Count: > 0 })
        {
            var filter = request.RegistrationIds.ToHashSet();
            candidates = candidates.Where(c => filter.Contains(c.RegistrationId)).ToList();
        }

        var rows = new List<UsLaxReconciliationRowDto>(candidates.Count);
        var datesUpdated = 0;
        var failed = 0;

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var row = await ReconcileOneAsync(c, ct);
            rows.Add(row);
            if (row.ExpiryDateUpdated) datesUpdated++;
            if (row.StatusCode != 200) failed++;
        }

        return new UsLaxReconciliationResponse
        {
            TotalPinged = rows.Count,
            DatesUpdated = datesUpdated,
            Failed = failed,
            Rows = rows
        };
    }

    private async Task<UsLaxReconciliationRowDto> ReconcileOneAsync(UsLaxReconciliationCandidateRow c, CancellationToken ct)
    {
        UsLaxMemberPingResult? ping;
        try
        {
            ping = await _usLax.GetMemberAsync(c.SportAssnId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USLax ping failed for registration {RegistrationId} / member {MembershipId}", c.RegistrationId, c.SportAssnId);
            ping = null;
        }

        if (ping == null)
        {
            return BuildRow(c, statusCode: 0, errorMessage: "Network or parse failure", newExpiry: null, updated: false);
        }

        if (ping.StatusCode != 200 || ping.Output is null)
        {
            return BuildRow(c, statusCode: ping.StatusCode, errorMessage: ping.ErrorMessage, newExpiry: null, updated: false, output: ping.Output);
        }

        var output = ping.Output;
        DateTime? newExpiry = null;
        var updated = false;

        // Legacy rule: write back when USALax returns an exp_date AND involvement includes "Player".
        // Skip no-op writes when the new date matches what's already on file.
        var isPlayer = output.Involvement?.Any(s => s.Equals("Player", StringComparison.OrdinalIgnoreCase)) == true;
        if (isPlayer && DateTime.TryParse(output.ExpDate, out var parsed))
        {
            newExpiry = parsed.Date;
            if (c.SportAssnIdexpDate?.Date != newExpiry)
            {
                try
                {
                    await _registrations.UpdateSportAssnIdExpDateAsync(c.RegistrationId, newExpiry.Value, ct);
                    updated = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write SportAssnIdexpDate for registration {RegistrationId}", c.RegistrationId);
                }
            }
        }

        return BuildRow(c, statusCode: 200, errorMessage: null, newExpiry: newExpiry, updated: updated, output: output);
    }

    public async Task<UsLaxEmailResponse> SendEmailAsync(Guid jobId, string? senderUserId, UsLaxEmailRequest request, CancellationToken ct = default)
    {
        var jobInfo = await _jobs.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobName = jobInfo?.JobName ?? string.Empty;
        var jobPath = jobInfo?.JobPath ?? string.Empty;
        var jobLinkHtml = BuildJobLinkHtml(jobPath, jobName);

        var sent = 0;
        var failed = 0;
        var missingEmail = 0;
        var failedAddresses = new List<string>();
        var sentAddresses = new List<string>();

        foreach (var r in request.Recipients)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(r.Email))
            {
                missingEmail++;
                continue;
            }

            var subject = SubstituteRowTokens(request.Subject, r, jobName, jobLinkHtml);
            var body = SubstituteRowTokens(request.Body, r, jobName, jobLinkHtml);

            var message = new EmailMessageDto
            {
                FromName = jobName,
                Subject = subject,
                HtmlBody = body,
                ToAddresses = new List<string> { r.Email }
            };

            try
            {
                var ok = await _emailService.SendAsync(message, cancellationToken: ct);
                if (ok) { sent++; sentAddresses.Add(r.Email); }
                else { failed++; failedAddresses.Add(r.Email); }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "USLax email send failed for registration {RegistrationId}", r.RegistrationId);
                failed++;
                failedAddresses.Add(r.Email);
            }
        }

        // Audit: one EmailLogs row for the batch (matches legacy USLaxMembershipController pattern).
        try
        {
            await _emailLogs.LogAsync(new EmailLogs
            {
                JobId = jobId,
                Count = sent,
                Subject = request.Subject,
                Msg = request.Body,
                SendTo = string.Join(";", sentAddresses),
                SendFrom = null,
                SenderUserId = senderUserId,
                SendTs = DateTime.UtcNow
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USLax email log write failed for job {JobId}", jobId);
        }

        return new UsLaxEmailResponse
        {
            Sent = sent,
            Failed = failed,
            MissingEmail = missingEmail,
            FailedAddresses = failedAddresses
        };
    }

    /// <summary>
    /// Per-recipient token substitution matching the legacy USLaxMembershipController.
    /// Token names (incl. the doubled <c>!USLAXMEMBERSTATUSSTATUS</c>) are kept verbatim
    /// so admin muscle memory still works.
    /// </summary>
    private static string SubstituteRowTokens(string template, UsLaxEmailRecipientDto r, string jobName, string jobLinkHtml)
    {
        var person = $"{r.FirstName} {r.LastName}".Trim();
        var dob = r.Dob?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        var expiry = r.ExpiryDate?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        var paddedId = string.IsNullOrWhiteSpace(r.MembershipId)
            ? string.Empty
            : new string(r.MembershipId.Where(char.IsDigit).ToArray()).PadLeft(12, '0');

        return template
            .Replace("!PLAYER", person, StringComparison.Ordinal)
            .Replace("!PLAYERDOB", dob, StringComparison.Ordinal)
            .Replace("!USLAXMEMBERID", paddedId, StringComparison.Ordinal)
            .Replace("!USLAXMEMBERSTATUSSTATUS", r.MemStatus ?? string.Empty, StringComparison.Ordinal)
            .Replace("!USLAXMEMBERSTATUS", r.MemStatus ?? string.Empty, StringComparison.Ordinal)
            .Replace("!USLAXAGEVERIFIED", r.AgeVerified ?? string.Empty, StringComparison.Ordinal)
            .Replace("!USLAXEXPIRY", expiry, StringComparison.Ordinal)
            .Replace("!JOBLINK", jobLinkHtml, StringComparison.Ordinal)
            .Replace("!JOBNAME", jobName, StringComparison.Ordinal);
    }

    private static string BuildJobLinkHtml(string jobPath, string jobName)
    {
        if (string.IsNullOrWhiteSpace(jobPath)) return System.Net.WebUtility.HtmlEncode(jobName);
        var url = $"https://www.teamsportsinfo.com/{jobPath}";
        var label = string.IsNullOrWhiteSpace(jobName) ? url : jobName;
        return $"<a href=\"{url}\">{System.Net.WebUtility.HtmlEncode(label)}</a>";
    }

    private static UsLaxReconciliationRowDto BuildRow(
        UsLaxReconciliationCandidateRow c,
        int statusCode,
        string? errorMessage,
        DateTime? newExpiry,
        bool updated,
        UsLaxMemberPingOutput? output = null)
    {
        return new UsLaxReconciliationRowDto
        {
            RegistrationId = c.RegistrationId,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Email = c.Email,
            MembershipId = c.SportAssnId,
            TeamName = c.TeamName,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            MemStatus = output?.MemStatus,
            AgeVerified = output?.AgeVerified,
            Involvement = output?.Involvement,
            PreviousExpiryDate = c.SportAssnIdexpDate,
            NewExpiryDate = newExpiry ?? c.SportAssnIdexpDate,
            ExpiryDateUpdated = updated
        };
    }
}
