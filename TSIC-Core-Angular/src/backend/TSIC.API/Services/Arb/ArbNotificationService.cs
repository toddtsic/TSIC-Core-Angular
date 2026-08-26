using TSIC.API.Extensions;
using TSIC.API.Services.Shared.Email;
using TSIC.Contracts.Dtos.Arb;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Arb;

/// <summary>
/// Unattended family-facing ARB mail: the expiring-card notice, on the 2nd and the 15th.
///
/// It used to carry a second job — mailing every family behind a failed draft, from the 4 AM sweep.
/// That was removed on 2026-08-26. A dunning notice now goes only when a director sends it from the
/// ARB Health screen; the sweep reports failed drafts in its digest and contacts nobody. What remains
/// here is the pre-emptive notice, which fires BEFORE a card fails and asks for nothing owed.
///
/// WHY THE TEXT IS FIXED HERE and not shared with the ARB Health screen: that screen's copy is a
/// DRAFT a director edits before sending, which is its whole purpose. This copy is an unattended
/// contract. One shared source would let a director's wording change silently alter what goes to
/// thousands of families. The wording below is deliberately IDENTICAL to the screen's defaults in
/// arb-health.component.ts — separately owned, not separately worded.
///
/// If you change the menu label in the step list below, change it in arb-health.component.ts too.
/// That step list is the line that rots: it names a menu item the family has to find.
/// </summary>
public sealed class ArbNotificationService : IArbNotificationService
{
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
            var accepted = await _email.SendAsync(new EmailMessageDto
            {
                FromName = director?.Name ?? jobName,
                ReplyToName = director?.Name,
                ReplyToAddress = director?.Email,
                ToAddresses = recipients,
                Subject = subject,
                HtmlBody = body
            }, cancellationToken: ct);

            // This is a family being told their payment failed. A send that quietly returned false left
            // no trace anywhere — the audit row still recorded them as a recipient.
            if (!accepted)
            {
                _logger.LogError(
                    "ARB family email NOT accepted: {Registrant} ({Job}) to={Recipients} subject={Subject}",
                    registrant, jobName, string.Join(",", recipients), subject);
            }
        }
        else
        {
            _logger.LogInformation(
                "ARB family email RENDERED ONLY (dry run): {Registrant} ({Job}) to={Recipients}",
                registrant, jobName, string.Join(",", recipients));
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
        public required string JobName { get; init; }
        public required string Subject { get; init; }
        /// <summary>The TEMPLATE, tokens unreplaced — see <see cref="FlushAuditAsync"/>.</summary>
        public required string BodyTemplate { get; init; }
        public string? SendFrom { get; set; }
        public List<string> Recipients { get; } = [];
        /// <summary>Registrations covered. Not Recipients.Count — an account can carry both parents.</summary>
        public int Families { get; set; }
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
    private async Task<List<ArbAuditRowDto>> FlushAuditAsync(
        IEnumerable<AuditBucket> buckets, CancellationToken ct)
    {
        var written = new List<ArbAuditRowDto>();

        foreach (var b in buckets.Where(b => b.Recipients.Count > 0))
        {
            written.Add(new ArbAuditRowDto
            {
                JobName = b.JobName,
                Subject = b.Subject,
                Families = b.Families,
                Recipients = b.Recipients.Count
            });

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

        return written;
    }

    /// <summary>Find or start the bucket for this job + email type.</summary>
    private static AuditBucket Bucket(
        Dictionary<(Guid, string), AuditBucket> buckets,
        Guid jobId, string jobName, string subject, string template)
    {
        if (!buckets.TryGetValue((jobId, subject), out var b))
        {
            b = new AuditBucket
            {
                JobId = jobId,
                JobName = jobName,
                Subject = subject,
                BodyTemplate = template
            };
            buckets[(jobId, subject)] = b;
        }
        return b;
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

        // This pass runs unattended on the 2nd and the 15th with no manual trigger on production, so
        // the log is the only place its scope is ever visible.
        _logger.LogInformation(
            "ARB expiring-card pass START: jobsWithLiveSubscriptions={JobCount} dryRun={DryRun}",
            jobIds.Count, _dryRun);

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

                    var sent = await SendOrRenderAsync(
                        who, reg.JobName, recipients, SubjectExpiringCard, body, director, ct);
                    // Dry run only — same reason as the failed-draft path.
                    if (_dryRun) rendered.Add(sent);

                    var bucket = Bucket(buckets, jobId, reg.JobName, SubjectExpiringCard, BodyExpiringCard);
                    bucket.SendFrom ??= director?.Email;
                    bucket.Recipients.AddRange(recipients);
                    bucket.Families++;
                    emailed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Expiring-card notification failed for registration {RegId}", reg.RegistrationId);
                    skips.Add(Skip(who, reg.JobName, $"send failed: {ex.Message}"));
                }
            }
        }

        var auditRows = await FlushAuditAsync(buckets.Values, ct);

        var result = new ArbNotifyResultDto
        {
            Found = found,
            Emailed = emailed,
            Skipped = skips.Count,
            Skips = skips,
            DryRun = _dryRun,
            Rendered = rendered,
            AuditRows = auditRows
        };

        // No ct. See SendExpiringSummaryAsync.
        return result with { SummaryHtml = await SendExpiringSummaryAsync(result, jobIds.Count) };
    }

    /// <summary>
    /// Paired counts to support, the same shape the sweep digest reports: how many cards expire this
    /// month, how many families were reached, and by name the ones that need a person.
    ///
    /// TAKES NO CancellationToken, for the same reason as AdnSweepService.SendDigestAsync — and here
    /// it matters more. This runs LAST, after families have already been mailed. If an abort could
    /// cancel this send, the families would have their notices and support would have no record that
    /// the pass ran at all, including the by-name list of the ones who could NOT be reached. That
    /// list is the only place those families surface.
    /// </summary>
    private async Task<string?> SendExpiringSummaryAsync(ArbNotifyResultDto result, int jobCount)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"<h3 style='margin-bottom:4px;'>ARB Expiring Cards — {DateTime.Now:dddd, dd MMMM yyyy HH:mm}</h3>");
            if (_dryRun)
            {
                sb.Append("<p style='font-size:12px;font-weight:bold;color:#0b5;margin:8px 0 2px 0;'>"
                    + "DRY RUN — no family was emailed. This summary is the only message that left the box.</p>");
            }
            sb.Append($"<p style='font-size:10px;'>Jobs checked: {jobCount} · Cards expiring this month: {result.Found} · "
                + $"Families {(_dryRun ? "that would be emailed" : "emailed")}: {result.Emailed} · NOT emailed: {result.Skipped}</p>");

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

            // Plain-text half, so the message is multipart/alternative rather than text/html only.
            // support@ routes through a mail security gateway and HTML-only mail was being quarantined
            // after SES accepted it — see AdnSweepService.BuildDigestText. The skip list is the part
            // that must survive: those families are the ones a human has to contact by hand.
            var textSb = new System.Text.StringBuilder();
            textSb.AppendLine($"ARB Expiring Cards - {DateTime.Now:dddd, dd MMMM yyyy HH:mm}");
            textSb.AppendLine();
            if (_dryRun) { textSb.AppendLine("DRY RUN - no family was emailed."); textSb.AppendLine(); }
            textSb.AppendLine($"Jobs checked:              {jobCount}");
            textSb.AppendLine($"Cards expiring this month: {result.Found}");
            textSb.AppendLine($"Families {(_dryRun ? "that would be emailed" : "emailed"),-17} {result.Emailed}");
            textSb.AppendLine($"NOT emailed:               {result.Skipped}");
            if (result.Skips.Count > 0)
            {
                textSb.AppendLine();
                textSb.AppendLine("NOT emailed - contact these by hand:");
                foreach (var s in result.Skips)
                {
                    textSb.AppendLine($"  - {s.JobName} / {s.Registrant} - {s.Reason}");
                }
            }
            textSb.AppendLine();
            textSb.AppendLine("Full detail is in the HTML version of this message.");

            // The summary mails on a dry run too, to support only. The rule the dry run keeps is that
            // nothing reaches a FAMILY off Production — not that nothing leaves the box. Suppressing
            // this left the whole delivery path untested: SES, the transport hop, and how a mail client
            // renders the HTML are only exercised by actually sending, and the transport is where the
            // digest was being corrupted. Subject carries the DRY RUN marker so it can never be mistaken
            // for the 4am one.
            var accepted = await _email.SendAsync(new EmailMessageDto
            {
                FromName = "",
                ToAddresses = [TsicConstants.SupportEmail],
                Subject = (_dryRun ? "[DRY RUN] " : "")
                    + $"ARB Expiring Cards {DateTime.Now:dddd, dd MMMM yyyy} — {result.Emailed} {(_dryRun ? "would be emailed" : "emailed")}"
                    + (result.Skipped > 0 ? $", {result.Skipped} NOT" : ""),
                HtmlBody = html,
                TextBody = textSb.ToString()
            }, sendInDevelopment: true, cancellationToken: CancellationToken.None);

            if (accepted)
            {
                _logger.LogInformation("ARB expiring-card summary: accepted by the mail service (dryRun={DryRun})", _dryRun);
            }
            else
            {
                _logger.LogError("ARB expiring-card summary: NOT accepted by the mail service (dryRun={DryRun})", _dryRun);
            }

            return html;
        }
        catch (Exception ex)
        {
            // The families were already emailed; losing the summary must not undo or re-run that.
            _logger.LogError(ex, "ARB expiring-card summary send failed");
            return null;
        }
    }

    private static ArbNotifySkipDto Skip(string who, string jobName, string reason) =>
        new() { Registrant = who, JobName = jobName, Reason = reason };

    /// <summary>
    /// Fills the template from the flagged-registrant DTO the ARB Health query produces. The
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

    private const string BodyExpiringCard =
        "<h2>Credit Card Expiration Notice</h2>" +
        "<p>The credit card on file for <strong>Automatic Recurrent Billing</strong> for !PLAYER is expiring this month.</p>" +
        "<p>Please visit !JOBLINK to update your credit card information TO PREVENT YOUR NEXT PAYMENT FROM FAILING.</p>" +
        "<ol>" +
        "<li>Login in the upper right corner using the username you used to register initially: !FAMILYUSERNAME</li>" +
        "<li>Select your Player's role</li>" +
        "<li>Under 'Player' in the upper right, select <b>Update CC Info</b> — this also pays any auto-payment that has failed</li>" +
        "<li>Your <b>Balance Due</b> is shown near the top of the page. Enter your credit card information below it.</li>" +
        "<li>Click <b>Update Card &amp; Pay Balance</b> to save the new card and keep your automatic payments running.</li>" +
        "</ol>";
}
