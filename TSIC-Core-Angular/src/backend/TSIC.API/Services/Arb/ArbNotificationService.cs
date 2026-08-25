using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Email;
using TSIC.Contracts.Dtos.Arb;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Arb;

/// <summary>
/// Emails the families behind ARB drafts that failed, from the daily sweep.
///
/// Until this existed the sweep imported every declined draft correctly and told nobody. The digest's
/// alert list filters on subscription status != active, and ADN keeps a subscription active while it
/// retries a declined card, so the great majority of failures were invisible there by construction.
///
/// WHY THE TEXT IS FIXED HERE and not shared with the ARB Health screen: that screen's copy is a
/// DRAFT a director edits before sending, which is its whole purpose. This copy is an unattended
/// contract. One shared source would let a director's wording change silently alter what goes to
/// thousands of families at 4 AM. The wording below is deliberately IDENTICAL to the screen's
/// defaults in arb-health.component.ts — separately owned, not separately worded.
///
/// If you change the menu label in the step list below, change it in arb-health.component.ts too.
/// That step list is the line that rots: it names a menu item the family has to find.
/// </summary>
public sealed class ArbNotificationService : IArbNotificationService
{
    /// <summary>Subject for a failure on a plan that is still alive. Stable: emailLogs is queried on it.</summary>
    public const string SubjectPlanAlive = "Action Required: Update Your Payment Information";

    /// <summary>Subject for a failure on a plan that has already ended. Stable: emailLogs is queried on it.</summary>
    public const string SubjectPlanDead = "Action Required: Pay Balance Due";

    /// <summary>Subject for the expiring-card notice. Stable: emailLogs is queried on it.</summary>
    public const string SubjectExpiringCard = "TeamSportsInfo.com Credit Card Expiring This Month";

    /// <summary>Prefix on the emailLogs subject of a message that was rendered but never transmitted.</summary>
    public const string DryRunSubjectPrefix = "[DRY RUN] ";

    private readonly IArbSubscriptionRepository _arbRepo;
    private readonly IArbDefensiveService _defensive;
    private readonly IEmailService _email;
    private readonly IEmailLogRepository _emailLogs;
    private readonly ILogger<ArbNotificationService> _logger;

    /// <summary>
    /// Whether this host renders instead of sends. Derived from the environment IN THE CONSTRUCTOR and
    /// deliberately NOT a parameter: a caller-supplied flag is a flag a caller can get wrong, and the
    /// way to get it wrong on Production is to mail nobody while reporting that thousands were mailed.
    /// There is nothing to pass, so there is nothing to pass incorrectly. Production always sends;
    /// every other environment never does.
    /// </summary>
    private readonly bool _dryRun;

    public ArbNotificationService(
        IArbSubscriptionRepository arbRepo,
        IArbDefensiveService defensive,
        IEmailService email,
        IEmailLogRepository emailLogs,
        IHostEnvironment env,
        ILogger<ArbNotificationService> logger)
    {
        _arbRepo = arbRepo;
        _defensive = defensive;
        _email = email;
        _emailLogs = emailLogs;
        _logger = logger;
        _dryRun = env.IsSandbox();
    }

    /// <summary>
    /// One family's message: transmitted on Production, rendered only on a dry run. Deliberately does
    /// NOT write to emailLogs — the audit is one row per job per email type, batched and flushed by
    /// <see cref="FlushAuditAsync"/> once the whole pass is done. Both notices route through here so the
    /// two paths cannot drift: what a dry run shows on screen is what a live run puts on the wire.
    /// </summary>
    private async Task<ArbRenderedEmailDto> SendOrRenderAsync(
        string registrant, string jobName, List<string> recipients,
        string subject, string body, ArbDirectorProjection? director, CancellationToken ct)
    {
        if (!_dryRun)
        {
            await _email.SendAsync(new EmailMessageDto
            {
                FromName = director?.Name ?? jobName,
                ReplyToName = director?.Name,
                ReplyToAddress = director?.Email,
                ToAddresses = recipients,
                Subject = subject,
                HtmlBody = body
            }, cancellationToken: ct);
        }

        return new ArbRenderedEmailDto
        {
            Registrant = registrant,
            JobName = jobName,
            ToAddresses = recipients,
            Subject = subject,
            ReplyToName = director?.Name,
            ReplyToAddress = director?.Email,
            HtmlBody = body
        };
    }

    /// <summary>
    /// One accumulating emailLogs row: a job, an email type, and everyone who got that type today.
    /// </summary>
    private sealed class AuditBucket
    {
        public required Guid JobId { get; init; }
        public required string Subject { get; init; }
        /// <summary>The TEMPLATE, tokens unreplaced — see <see cref="FlushAuditAsync"/>.</summary>
        public required string BodyTemplate { get; init; }
        public string? SendFrom { get; set; }
        public List<string> Recipients { get; } = [];
    }

    /// <summary>
    /// Writes ONE emailLogs row per job per email type, matching how a Search Registrations batch email
    /// audits itself (<c>EmailBatchService.CreateAuditRowAsync</c>): Count is the recipient tally, SendTo
    /// is the ';'-joined address list, and Msg holds the BODY TEMPLATE rather than any one family's
    /// rendered copy.
    ///
    /// Storing the template is the deliberate part. Every family's rendered body differs — their player,
    /// their username, their balance — so no single row could hold "the" body, and picking one family's
    /// copy to stand for the rest would be worse than storing none. The template is what the batch
    /// actually was. Per-family rendered text is not lost: on a dry run every message comes back on
    /// ArbNotifyResultDto.Rendered for review.
    ///
    /// The ';'-joined SendTo is what makes a batched row still answer "was this family written to?" —
    /// GetSentToAddressesAsync matches "%;addr;%" against it, so the player panel and the family's own
    /// sent-mail list resolve exactly as they do for a per-family row.
    /// </summary>
    private async Task FlushAuditAsync(IEnumerable<AuditBucket> buckets, CancellationToken ct)
    {
        foreach (var b in buckets.Where(b => b.Recipients.Count > 0))
        {
            try
            {
                await _emailLogs.LogAsync(new EmailLogs
                {
                    JobId = b.JobId,
                    Count = b.Recipients.Count,
                    // A dry-run row is marked so nobody reading the log screen, the player panel, or the
                    // family's own sent-mail list mistakes rendered-only mail for mail that actually went.
                    Subject = _dryRun ? DryRunSubjectPrefix + b.Subject : b.Subject,
                    Msg = b.BodyTemplate,
                    SendFrom = b.SendFrom ?? TsicConstants.SupportEmail,
                    SendTo = string.Join(";", b.Recipients),
                    // System actor, matching the accounting rows the same sweep writes.
                    SenderUserId = TsicConstants.SuperUserId,
                    SendTs = DateTime.Now
                }, ct);
            }
            catch (Exception ex)
            {
                // The families got their email. Losing the audit row is bad, but failing the pass over it
                // would be worse — and on a live run there is no way to un-send what already went.
                _logger.LogError(ex, "ARB notification audit write failed for job {JobId} / {Subject}",
                    b.JobId, b.Subject);
            }
        }
    }

    /// <summary>Find or start the bucket for this job + email type.</summary>
    private static AuditBucket Bucket(
        Dictionary<(Guid, string), AuditBucket> buckets, Guid jobId, string subject, string template)
    {
        if (!buckets.TryGetValue((jobId, subject), out var b))
        {
            b = new AuditBucket { JobId = jobId, Subject = subject, BodyTemplate = template };
            buckets[(jobId, subject)] = b;
        }
        return b;
    }

    public async Task<ArbNotifyResultDto> NotifyFailedDraftsAsync(
        IReadOnlyList<ArbFailedDraftDto> failures, CancellationToken ct = default)
    {
        if (failures.Count == 0) return ArbNotifyResultDto.Empty;

        var skips = new List<ArbNotifySkipDto>();
        var rendered = new List<ArbRenderedEmailDto>();
        var buckets = new Dictionary<(Guid, string), AuditBucket>();
        var emailed = 0;

        // One estate-wide projection read for the whole morning's failures. jobIdFilter is null on
        // purpose: the sweep spans every job, and the registration carries its own job identity.
        var invoices = failures.Select(f => f.InvoiceNumber).Distinct().ToList();
        var projections = (await _arbRepo.GetRegistrationsByInvoiceNumbersAsync(invoices, null, ct))
            .GroupBy(p => p.RegistrationId)
            .ToDictionary(g => g.Key, g => g.First());

        // The From ADDRESS is forced to the SES-verified identity at the send chokepoint, so the
        // director rides Reply-To. Without one, a family's reply about their own payment lands in
        // TSIC support's inbox instead of their club's.
        var jobIds = projections.Values.Select(p => p.JobId).Distinct().ToList();
        var directors = (await _arbRepo.GetDefaultDirectorsForJobsAsync(jobIds, ct))
            .GroupBy(d => d.JobId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var failure in failures)
        {
            ct.ThrowIfCancellationRequested();

            var who = failure.Registrant ?? failure.RegistrationId.ToString();
            try
            {
                if (!projections.TryGetValue(failure.RegistrationId, out var reg))
                {
                    skips.Add(Skip(who, failure.JobName, "no ARB projection for this registration"));
                    continue;
                }

                if (reg.BemailOptOut)
                {
                    skips.Add(Skip(who, reg.JobName, "registrant has opted out of email"));
                    continue;
                }

                // Unresolved-token guard. The login line is the entire point of this email; sending
                // it with a blank username produces a support call, not a payment. Skip and name
                // them in the digest so a human picks it up. Adult/self registrations legitimately
                // have no family user and land here.
                if (string.IsNullOrWhiteSpace(reg.FamilyUsername))
                {
                    skips.Add(Skip(who, reg.JobName, "no family username on file - cannot give login instructions"));
                    continue;
                }

                // Same candidate set as the director-clicked path in ArbDefensiveService.
                var recipients = BatchEmailRecipientFilter.BuildSendableSet(
                    new[] { reg.MomEmail, reg.DadEmail, reg.RegistrantEmail });
                if (recipients.Count == 0)
                {
                    skips.Add(Skip(who, reg.JobName, "no sendable email address on the account"));
                    continue;
                }

                var alive = PlanIsAlive(failure.SubscriptionStatus);
                var subject = alive ? SubjectPlanAlive : SubjectPlanDead;
                var template = alive ? BodyPlanAlive : BodyPlanDead;
                var body = ReplaceTokens(template, reg, failure);

                directors.TryGetValue(reg.JobId, out var director);

                rendered.Add(await SendOrRenderAsync(
                    who, reg.JobName, recipients, subject, body, director, ct));

                // Alive and dead are separate email types, so a job with both kinds of failure this
                // morning audits as two rows — which is correct: they are two different messages.
                var bucket = Bucket(buckets, reg.JobId, subject, template);
                bucket.SendFrom ??= director?.Email;
                bucket.Recipients.AddRange(recipients);
                emailed++;
            }
            catch (Exception ex)
            {
                // Per-registration containment: one bad address must not cost the other families
                // their notice, and must not surface as a sweep error.
                _logger.LogError(ex, "ARB failure notification failed for registration {RegId} (tx {TxId})",
                    failure.RegistrationId, failure.TransId);
                skips.Add(Skip(who, failure.JobName, $"send failed: {ex.Message}"));
            }
        }

        await FlushAuditAsync(buckets.Values, ct);

        return new ArbNotifyResultDto
        {
            Found = failures.Count,
            Emailed = emailed,
            Skipped = skips.Count,
            Skips = skips,
            DryRun = _dryRun,
            Rendered = rendered
        };
    }

    public async Task<ArbNotifyResultDto> NotifyExpiringCardsAsync(CancellationToken ct = default)
    {
        var skips = new List<ArbNotifySkipDto>();
        var rendered = new List<ArbRenderedEmailDto>();
        var buckets = new Dictionary<(Guid, string), AuditBucket>();
        var found = 0;
        var emailed = 0;

        // Per-job, not estate-wide: the expiring-card list comes from ADN, which is queried with the
        // job's own credentials. Only jobs still holding a live subscription are asked.
        var jobIds = await _arbRepo.GetJobIdsWithLiveSubscriptionsAsync(ct);
        var directors = (await _arbRepo.GetDefaultDirectorsForJobsAsync(jobIds, ct))
            .GroupBy(d => d.JobId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var jobId in jobIds)
        {
            ct.ThrowIfCancellationRequested();

            List<ArbFlaggedRegistrantDto> flagged;
            try
            {
                flagged = await _defensive.GetFlaggedSubscriptionsAsync(jobId, ArbFlagType.ExpiringCard, ct);
            }
            catch (Exception ex)
            {
                // One job's ADN call failing must not cost every other job its notices. Recorded as a
                // skip so the summary shows a job was NOT checked rather than silently reporting zero.
                _logger.LogError(ex, "Expiring-card lookup failed for job {JobId}", jobId);
                skips.Add(Skip("(whole job)", jobId.ToString(), $"expiring-card lookup failed: {ex.Message}"));
                continue;
            }

            directors.TryGetValue(jobId, out var director);

            foreach (var reg in flagged)
            {
                ct.ThrowIfCancellationRequested();
                found++;

                var who = reg.RegistrantName;
                try
                {
                    if (reg.BemailOptOut)
                    {
                        skips.Add(Skip(who, reg.JobName, "registrant has opted out of email"));
                        continue;
                    }

                    // Same unresolved-token guard as the failure path: the login line is the
                    // instruction, and a blank one wastes the notice.
                    if (string.IsNullOrWhiteSpace(reg.FamilyUsername))
                    {
                        skips.Add(Skip(who, reg.JobName, "no family username on file - cannot give login instructions"));
                        continue;
                    }

                    var recipients = BatchEmailRecipientFilter.BuildSendableSet(
                        new[] { reg.MomEmail, reg.DadEmail, reg.RegistrantEmail });
                    if (recipients.Count == 0)
                    {
                        skips.Add(Skip(who, reg.JobName, "no sendable email address on the account"));
                        continue;
                    }

                    var body = ReplaceFlaggedTokens(BodyExpiringCard, reg);

                    rendered.Add(await SendOrRenderAsync(
                        who, reg.JobName, recipients, SubjectExpiringCard, body, director, ct));

                    var bucket = Bucket(buckets, jobId, SubjectExpiringCard, BodyExpiringCard);
                    bucket.SendFrom ??= director?.Email;
                    bucket.Recipients.AddRange(recipients);
                    emailed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Expiring-card notification failed for registration {RegId}", reg.RegistrationId);
                    skips.Add(Skip(who, reg.JobName, $"send failed: {ex.Message}"));
                }
            }
        }

        await FlushAuditAsync(buckets.Values, ct);

        var result = new ArbNotifyResultDto
        {
            Found = found,
            Emailed = emailed,
            Skipped = skips.Count,
            Skips = skips,
            DryRun = _dryRun,
            Rendered = rendered
        };

        return result with { SummaryHtml = await SendExpiringSummaryAsync(result, jobIds.Count, ct) };
    }

    /// <summary>
    /// Paired counts to support, the same shape the sweep digest reports: how many cards expire this
    /// month, how many families were reached, and by name the ones that need a person.
    /// </summary>
    private async Task<string?> SendExpiringSummaryAsync(ArbNotifyResultDto result, int jobCount, CancellationToken ct)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<h3 style='margin-bottom:4px;'>ARB Expiring Cards — {DateTime.Now:dddd, dd MMMM yyyy HH:mm}</h3>");
            sb.Append($"<p style='font-size:10px;'>Jobs checked: {jobCount} · Cards expiring this month: {result.Found} · "
                + $"Families emailed: {result.Emailed} · NOT emailed: {result.Skipped}</p>");

            if (result.Skips.Count > 0)
            {
                sb.Append("<p style='font-size:10px;color:#b00;font-weight:bold;'>&#9888; NOT emailed — contact these by hand:</p>");
                sb.Append("<ul style='font-size:9px;margin-top:0;'>");
                foreach (var s in result.Skips)
                {
                    sb.Append($"<li>{s.JobName} · {s.Registrant} — {s.Reason}</li>");
                }
                sb.Append("</ul>");
            }

            var html = sb.ToString();

            // On a dry run the summary is handed back to the screen instead of mailed. It previously
            // passed sendInDevelopment:true — the one message on this path that DID transmit off
            // Production — and leaving that in would break the rule the dry run exists to keep:
            // off Production, nothing leaves the box.
            if (_dryRun) return html;

            await _email.SendAsync(new EmailMessageDto
            {
                FromName = "",
                ToAddresses = [TsicConstants.SupportEmail],
                Subject = $"ARB Expiring Cards {DateTime.Now:dddd, dd MMMM yyyy} — {result.Emailed} emailed"
                    + (result.Skipped > 0 ? $", {result.Skipped} NOT" : ""),
                HtmlBody = html
            }, sendInDevelopment: true, cancellationToken: ct);

            return html;
        }
        catch (Exception ex)
        {
            // The families were already emailed; losing the summary must not undo or re-run that.
            _logger.LogError(ex, "ARB expiring-card summary send failed");
            return null;
        }
    }

    /// <summary>
    /// Blank or unknown counts as ALIVE. The sweep only sees a failure because a draft was attempted,
    /// and a terminated subscription cannot attempt one — so the live-plan instructions are the safe
    /// default. Sending a live-plan family to "Pay Balance Due" would strand their remaining
    /// installments; the reverse is the AR-013 shape.
    /// </summary>
    private static bool PlanIsAlive(string? status) =>
        string.IsNullOrWhiteSpace(status)
        || status.Equals("active", StringComparison.OrdinalIgnoreCase)
        || status.Equals("suspended", StringComparison.OrdinalIgnoreCase);

    private static ArbNotifySkipDto Skip(string who, string jobName, string reason) =>
        new() { Registrant = who, JobName = jobName, Reason = reason };

    private static string ReplaceTokens(
        string template, ArbRegistrationProjection reg, ArbFailedDraftDto failure)
    {
        var natural = $"{reg.FirstName} {reg.LastName}".Trim();
        var display = string.IsNullOrWhiteSpace(natural) ? reg.RegistrantName : natural;
        var person = $"<strong>{System.Net.WebUtility.HtmlEncode(display)}</strong>";
        var jobName = System.Net.WebUtility.HtmlEncode(reg.JobName ?? string.Empty);

        return template
            .Replace("!PLAYER", person)
            .Replace("!PERSON", person)
            .Replace("!SUBSCRIPTIONID", $"<strong>{reg.SubscriptionId}</strong>")
            .Replace("!SUBSCRIPTIONSTATUS", $"<strong>{reg.SubscriptionStatus}</strong>")
            .Replace("!FEETOTAL", $"<strong>{reg.FeeTotal:C}</strong>")
            .Replace("!PAIDTOTAL", $"<strong>{reg.PaidTotal:C}</strong>")
            // The sweep's own ComputeInstallmentMath figure, carried across so the family's email and
            // the morning digest never quote two different numbers for the same registration.
            .Replace("!OWEDNOW", $"<strong>{failure.OwedNow:C}</strong>")
            .Replace("!OWEDTOTAL", $"<strong>{reg.OwedTotal:C}</strong>")
            .Replace("!FAMILYUSERNAME", $"<strong>{System.Net.WebUtility.HtmlEncode(reg.FamilyUsername ?? string.Empty)}</strong>")
            .Replace("!JOBLINK", $"<a href='https://www.teamsportsinfo.com/{reg.JobPath}' target='_blank'>{jobName}</a>")
            .Replace("!JOBNAME", $"<strong>{jobName}</strong>");
    }

    /// <summary>
    /// Same token set, filled from the flagged-registrant DTO the ARB Health query produces. The
    /// expiring-card path has no failed transaction behind it, so !OWEDNOW comes from the DTO's own
    /// CurrentlyOwes (zero on this flag type by construction) rather than from a draft.
    /// </summary>
    private static string ReplaceFlaggedTokens(string template, ArbFlaggedRegistrantDto reg)
    {
        var natural = $"{reg.FirstName} {reg.LastName}".Trim();
        var display = string.IsNullOrWhiteSpace(natural) ? reg.RegistrantName : natural;
        var person = $"<strong>{System.Net.WebUtility.HtmlEncode(display)}</strong>";
        var jobName = System.Net.WebUtility.HtmlEncode(reg.JobName ?? string.Empty);

        return template
            .Replace("!PLAYER", person)
            .Replace("!PERSON", person)
            .Replace("!SUBSCRIPTIONID", $"<strong>{reg.SubscriptionId}</strong>")
            .Replace("!SUBSCRIPTIONSTATUS", $"<strong>{reg.SubscriptionStatus}</strong>")
            .Replace("!FEETOTAL", $"<strong>{reg.FeeTotal:C}</strong>")
            .Replace("!PAIDTOTAL", $"<strong>{reg.PaidTotal:C}</strong>")
            .Replace("!OWEDNOW", $"<strong>{reg.CurrentlyOwes:C}</strong>")
            .Replace("!OWEDTOTAL", $"<strong>{reg.OwedTotal:C}</strong>")
            .Replace("!FAMILYUSERNAME", $"<strong>{System.Net.WebUtility.HtmlEncode(reg.FamilyUsername ?? string.Empty)}</strong>")
            .Replace("!JOBLINK", $"<a href='https://www.teamsportsinfo.com/{reg.JobPath}' target='_blank'>{jobName}</a>")
            .Replace("!JOBNAME", $"<strong>{jobName}</strong>");
    }

    // ── Fixed text ────────────────────────────────────────────────────
    // Word-for-word the ARB Health screen's defaults. See the class summary for why it is a separate
    // copy rather than a shared one.

    private const string BodyPlanAlive =
        "<p>One or more of your automatic payments for !JOBNAME for !PLAYER was declined.</p>" +
        "<p>You can contact your credit card issuer to determine the reason if you need to.</p>" +
        "<p>Then you can update your credit card information and process the current balance due (!OWEDNOW) all in one step.</p>" +
        "<p>To fix this, visit !JOBLINK, then:</p>" +
        "<ol>" +
        "<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>" +
        "<li>Select your Player's role</li>" +
        "<li>Under 'Player' in the upper right, select 'Update CC Info (will also pay for failed auto-payments)'</li>" +
        "<li>Enter your credit card information and you will see the amount due at the bottom of the screen.</li>" +
        "<li>Click Submit to make the payment and reactivate your future automatic payments.</li>" +
        "</ol>";

    private const string BodyPlanDead =
        "<p>One or more of your automatic payments for !JOBNAME for !PLAYER was declined.</p>" +
        "<p>You can contact your credit card issuer to determine the reason if you need to.</p>" +
        "<p>Then you can update your credit card information and process the current balance due (!OWEDNOW) all in one step.</p>" +
        "<p>To fix this, visit !JOBLINK, then:</p>" +
        "<ol>" +
        "<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>" +
        "<li>Select your Player's role</li>" +
        "<li>Under 'Player' in the upper right, select 'Pay Balance Due'</li>" +
        "</ol>";

    private const string BodyExpiringCard =
        "<h2>Credit Card Expiration Notice</h2>" +
        "<p>The credit card on file for <strong>Automatic Recurrent Billing</strong> for !PLAYER is expiring this month.</p>" +
        "<p>Please visit !JOBLINK to update your credit card information TO PREVENT YOUR NEXT PAYMENT FROM FAILING.</p>" +
        "<ol>" +
        "<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>" +
        "<li>Select your Player's role</li>" +
        "<li>Under 'Player' in the upper right, select 'Update CC Info (will also pay for failed auto-payments)'</li>" +
        "<li>Enter your credit card information and you will see the amount due at the bottom of the screen</li>" +
        "<li>Click Submit to make the payment and reactivate your future automatic payments</li>" +
        "</ol>";
}
