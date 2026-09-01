using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.UsLax;

namespace TSIC.API.Services.Admin;

public sealed class UsLaxMembershipService : IUsLaxMembershipService
{
    // Mirrors the CC payment-method GUID used across the codebase. Accounting tokens are
    // not expected in USLax templates, but the engine parameter is required.
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

    private readonly IRegistrationRepository _registrations;
    private readonly IUsLaxService _usLax;
    private readonly IJobRepository _jobs;
    private readonly IFamiliesRepository _families;
    private readonly IEmailBatchService _emailBatch;
    private readonly ITextSubstitutionService _textSubstitution;
    private readonly IEmailTestSendService _testSend;
    private readonly ILogger<UsLaxMembershipService> _logger;

    public UsLaxMembershipService(
        IRegistrationRepository registrations,
        IUsLaxService usLax,
        IJobRepository jobs,
        IFamiliesRepository families,
        IEmailBatchService emailBatch,
        ITextSubstitutionService textSubstitution,
        IEmailTestSendService testSend,
        ILogger<UsLaxMembershipService> logger)
    {
        _registrations = registrations;
        _usLax = usLax;
        _jobs = jobs;
        _families = families;
        _emailBatch = emailBatch;
        _textSubstitution = textSubstitution;
        _testSend = testSend;
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

        // ONE batch ping across all candidates (chunked to ≤499 internally), instead of
        // N round-trips. Per-chunk failures isolate inside the service; the dict has a
        // result for every input id (synthesized for invalid format / not in AMMS / chunk
        // failure) so the loop below never gets a null lookup.
        var ids = candidates.Select(c => c.SportAssnId).ToList();
        IReadOnlyDictionary<string, UsLaxMemberPingResult> pingByMember;
        try
        {
            pingByMember = await _usLax.GetMembersAsync(ids, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USLax batch ping threw for job {JobId} ({Count} ids).", jobId, ids.Count);
            pingByMember = new Dictionary<string, UsLaxMemberPingResult>();
        }

        var rows = new List<UsLaxReconciliationRowDto>(candidates.Count);
        var datesUpdated = 0;
        var failed = 0;
        var eligible = 0;

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            pingByMember.TryGetValue(c.SportAssnId, out var ping);
            var row = await BuildRowFromPingAsync(c, ping, request.Role, ct);
            rows.Add(row);
            if (row.ExpiryDateUpdated) datesUpdated++;
            if (row.StatusCode != 200) failed++;
            if (row.Eligible) eligible++;
        }

        // The one success-path record that a reconcile ran and what it did. Before this, a run
        // that silently updated nothing left NOTHING in Seq — the only log lines on this path
        // were failure warnings, so the endpoint was invisible unless you filtered EF command
        // logs by ActionName and counted UPDATE statements by hand.
        //
        // Counts only, no member details — the same PII rule ValidationController's rejection
        // log follows. Unlike the batch shape telemetry in UsLaxService this is NOT gated to
        // non-Production: "did the director's reconcile do anything" is an operational question
        // and prod is where it gets asked.
        _logger.LogInformation(
            "USLax reconcile: job {JobId} role {Role} — {Candidates} candidates, {Pinged} pinged, "
            + "{Eligible} eligible, {Failed} failed, {DatesUpdated} dates written",
            jobId, request.Role, candidates.Count, rows.Count, eligible, failed, datesUpdated);

        return new UsLaxReconciliationResponse
        {
            TotalPinged = rows.Count,
            DatesUpdated = datesUpdated,
            Failed = failed,
            Rows = rows
        };
    }

    private async Task<UsLaxReconciliationRowDto> BuildRowFromPingAsync(
        UsLaxReconciliationCandidateRow c,
        UsLaxMemberPingResult? ping,
        UsLaxMembershipRole role,
        CancellationToken ct)
    {
        if (ping == null)
        {
            return BuildRow(c, statusCode: 0, errorMessage: "Network or parse failure", newExpiry: null, updated: false, role: role);
        }

        if (ping.StatusCode != 200 || ping.Output is null)
        {
            return BuildRow(c, statusCode: ping.StatusCode, errorMessage: ping.ErrorMessage, newExpiry: null, updated: false, role: role, output: ping.Output);
        }

        var output = ping.Output;
        DateTime? newExpiry = null;
        var updated = false;

        // Gate the DB write on involvement so we don't cross-contaminate roles:
        //   - Player mode: only write if USLax says this member plays as a Player (legacy rule).
        //   - Coach  mode: write if USLax says they're staff in any capacity (Coach/Official/Referee).
        // A USLax member has one expiry per membership regardless of involvement, but the gate
        // protects against a registration mis-keyed to a membership that belongs to someone else.
        // Skip no-op writes when the new date matches what's already on file.
        var involvement = output.Involvement;
        var eligibleForWrite = role switch
        {
            UsLaxMembershipRole.Coach => involvement?.Any(s =>
                s.Equals("Coach", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Official", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Referee", StringComparison.OrdinalIgnoreCase)) == true,
            _ /* Player */ => involvement?.Any(s => s.Equals("Player", StringComparison.OrdinalIgnoreCase)) == true
        };
        if (eligibleForWrite && DateTime.TryParse(output.ExpDate, out var parsed))
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

        return BuildRow(c, statusCode: 200, errorMessage: null, newExpiry: newExpiry, updated: updated, role: role, output: output);
    }

    public async Task<UsLaxEmailStartResponse> StartEmailAsync(Guid jobId, string? senderUserId, UsLaxEmailRequest request, CancellationToken ct = default)
    {
        var jobInfo = await _jobs.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobName = jobInfo?.JobName ?? string.Empty;
        var jobPath = jobInfo?.JobPath ?? string.Empty;
        var jobValidThrough = jobInfo?.UsLaxNumberValidThroughDate;

        // SECURITY — the recipient snapshot is CLIENT-BUILT, so nothing in it may decide where mail
        // goes. Every posted registrationId is confirmed to belong to the CALLER'S job before anything
        // else happens. Without this an admin of any job could post another job's ids (or ids paired
        // with an arbitrary Email) and have the batch deliver arbitrary HTML under our SES identity —
        // and the per-recipient render, which keys on registrationId ALONE, would substitute the other
        // job's registrant into the tokens. Same guard, same shape, as
        // RegistrationSearchService.StartBatchEmailAsync.
        var postedIds = request.Recipients.Select(r => r.RegistrationId).Distinct().ToList();
        var regs = await _registrations.GetByIdsAsync(postedIds, ct);
        if (regs.Any(reg => reg.JobId != jobId))
            throw new InvalidOperationException("Some registrations do not belong to this job.");

        var regById = regs.ToDictionary(reg => reg.RegistrationId);

        // Addresses are re-derived from the database, never taken from the snapshot. ResolveRecipients
        // is the shared rule every other batch path uses: a Player fans out to mom + dad + the player's
        // own address; every other role — including the Coach audience, which registers as
        // UnassignedAdult — resolves to its own address only, which is the coach behaviour unchanged.
        var emailByRegId = (await _registrations.GetRecipientEmailsByIdsAsync(postedIds, ct))
            .GroupBy(e => e.RegistrationId)
            .ToDictionary(g => g.Key, g => g.First().Email);
        var playerFamilyIds = regs
            .Where(reg => reg.RoleId == RoleConstants.Player && !string.IsNullOrWhiteSpace(reg.FamilyUserId))
            .Select(reg => reg.FamilyUserId!)
            .ToList();
        var familyEmailsById = (await _families.GetByFamilyUserIdsAsync(playerFamilyIds, ct))
            .GroupBy(f => f.FamilyUserId)
            .ToDictionary(g => g.Key, g => g.First());

        // Up-front partition, so the skip rollup is known immediately and returned in the start
        // response. Address resolution is pure dictionary work against the maps loaded above:
        //   healthy   → NeedsAction == false (never false-alarm a valid member, even if force-selected)
        //   no-email  → no sendable address resolved (also covers an id that no longer exists)
        //   actionable→ everything else, becomes the background batch
        var skippedNames = new List<string>();
        var missingEmail = 0;
        var actionable = new List<UsLaxSendItem>();
        foreach (var r in request.Recipients)
        {
            if (!NeedsAction(r, jobValidThrough))
            {
                skippedNames.Add($"{r.FirstName} {r.LastName}".Trim());
                continue;
            }

            regById.TryGetValue(r.RegistrationId, out var reg);
            var toAddresses = reg is null
                ? new List<string>()
                : BatchEmailRecipientFilter.ResolveRecipients(
                    reg.RoleId, reg.FamilyUserId, reg.RegistrationId, emailByRegId, familyEmailsById);

            if (toAddresses.Count == 0)
            {
                missingEmail++;
                continue;
            }
            actionable.Add(new UsLaxSendItem(r, toAddresses, reg!.BemailOptOut));
        }

        var subjectTemplate = request.Subject;
        var bodyTemplate = request.Body;

        var plan = new EmailBatchPlan<UsLaxSendItem>
        {
            SeedAsync = (_, _) => Task.FromResult(new EmailBatchSeed<UsLaxSendItem> { Items = actionable }),
            // Opt-out comes off the loaded registration, not the snapshot.
            IsOptedOut = i => i.OptedOut,
            DescribeItem = i => $"{i.Snapshot.FirstName} {i.Snapshot.LastName}".Trim(),
            RenderAsync = async (i, sp, _) =>
            {
                var r = i.Snapshot;

                // Same TextSubstitution engine as every other email — resolved from the render scope.
                var textSub = sp.GetRequiredService<ITextSubstitutionService>();
                var extras = BuildUsLaxExtras(r);
                var (subject, body) = await textSub.SubstituteSubjectAndBodyAsync(
                    jobPath, jobId, CcPaymentMethodId, r.RegistrationId, string.Empty,
                    subjectTemplate, bodyTemplate, inviteTargetJobPath: null, extraTokens: extras, emailMode: true);

                return new EmailBatchRendered
                {
                    Message = new EmailMessageDto
                    {
                        FromName = jobName,
                        Subject = subject,
                        HtmlBody = body,
                        ToAddresses = i.ToAddresses
                    },
                    UnsubscribeRegId = r.RegistrationId // engine appends the unsubscribe footer
                };
            },
            // Engine writes the EmailLogs audit row from this (replaces USLax's manual log). No
            // sender-summary / director-notify for USLax, so no completion hook.
            Audit = new EmailBatchAudit
            {
                JobId = jobId,
                SenderUserId = senderUserId,
                Subject = subjectTemplate,
                BodyTemplate = bodyTemplate,
                SendFrom = null
            }
        };

        var handle = await _emailBatch.StartAsync(plan, new EmailBatchOptions(), ct);

        return new UsLaxEmailStartResponse
        {
            BatchJobId = handle.JobId,
            TotalRecipients = handle.TotalRecipients,
            MissingEmail = missingEmail,
            SkippedHealthy = skippedNames.Count,
            SkippedNames = skippedNames
        };
    }

    public async Task<EmailTestSendResponse> SendTestEmailAsync(
        Guid jobId, UsLaxTestSendRequest request, CancellationToken ct = default)
    {
        var jobInfo = await _jobs.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobPath = jobInfo?.JobPath ?? string.Empty;

        // Same render as StartEmailAsync's per-recipient step: shared TextSubstitution engine
        // plus the row-level USLax extras from the recipient snapshot.
        var extras = BuildUsLaxExtras(request.Recipient);
        var (subject, body) = await _textSubstitution.SubstituteSubjectAndBodyAsync(
            jobPath, jobId, CcPaymentMethodId, request.Recipient.RegistrationId, string.Empty,
            request.Subject, request.Body, inviteTargetJobPath: null, extraTokens: extras, emailMode: true);

        var renderedFor = $"{request.Recipient.FirstName} {request.Recipient.LastName}".Trim();
        return await _testSend.SendRenderedAsync(subject, body, renderedFor, request.TestRecipient, ct);
    }

    /// <summary>
    /// One queued send: the client's row snapshot (token data only — it decides NOTHING about
    /// delivery), plus the two facts resolved server-side from the registration — where it goes
    /// and whether that registrant has opted out.
    /// </summary>
    private sealed record UsLaxSendItem(
        UsLaxEmailRecipientDto Snapshot,
        List<string> ToAddresses,
        bool OptedOut);

    /// <summary>
    /// A recipient needs action (and therefore warrants the email) when:
    ///   - USLax did not return a membership status (no ping / API error), OR
    ///   - status is anything other than "Active" (PENDING / SUSPENDED / INACTIVE / …), OR
    ///   - no expiry date on file, OR
    ///   - expiry is before the job's USLax-valid-through date (when the job has one).
    /// When the job has no valid-through date configured we skip the date comparison —
    /// there's no cutoff to fail against.
    /// </summary>
    private static bool NeedsAction(UsLaxEmailRecipientDto r, DateTime? jobValidThrough)
    {
        var status = r.MemStatus?.Trim();
        if (string.IsNullOrEmpty(status)) return true;
        if (!string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase)) return true;
        if (r.ExpiryDate is null) return true;
        if (jobValidThrough.HasValue && r.ExpiryDate.Value.Date < jobValidThrough.Value.Date) return true;
        return false;
    }

    /// <summary>
    /// Per-recipient tokens specific to USLax reconciliation — data that comes from the
    /// USA Lacrosse ping response (DOB, padded membership ID, status, age-verified, expiry),
    /// not from the database. These are merged into the global TextSubstitutionService
    /// dictionary so shared tokens (<c>!PERSON</c>, <c>!JOBNAME</c>, <c>!JOBLINK</c>,
    /// <c>!USLAXVALIDTHROUGHDATE</c>, <c>!UNSUBSCRIBE</c>, etc.) resolve through the same
    /// engine every other email in the system uses — one dialect, one ordering discipline
    /// (TokenReplacer sorts by descending key length before replacing, so e.g.
    /// <c>!PLAYERDOB</c> wins over <c>!PLAYER</c> automatically).
    ///
    /// <c>!PLAYER</c> is kept as a legacy alias for <c>!PERSON</c> so already-saved
    /// bodies keep working. <c>!USLAXMEMBERSTATUSSTATUS</c> (doubled by the legacy
    /// controller) is preserved verbatim.
    /// </summary>
    private static IReadOnlyDictionary<string, string> BuildUsLaxExtras(UsLaxEmailRecipientDto r)
    {
        var person = $"{r.FirstName} {r.LastName}".Trim();
        var dob = r.Dob?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        var expiry = r.ExpiryDate?.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
        var paddedId = string.IsNullOrWhiteSpace(r.MembershipId)
            ? string.Empty
            : new string(r.MembershipId.Where(char.IsDigit).ToArray()).PadLeft(12, '0');
        var status = r.MemStatus ?? string.Empty;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["!PLAYERDOB"] = dob,
            ["!USLAXMEMBERSTATUSSTATUS"] = status,
            ["!USLAXMEMBERSTATUS"] = status,
            ["!USLAXMEMBERID"] = paddedId,
            ["!USLAXAGEVERIFIED"] = r.AgeVerified ?? string.Empty,
            ["!USLAXEXPIRY"] = expiry,
            ["!PLAYER"] = person
        };
    }

    private static UsLaxReconciliationRowDto BuildRow(
        UsLaxReconciliationCandidateRow c,
        int statusCode,
        string? errorMessage,
        DateTime? newExpiry,
        bool updated,
        UsLaxMembershipRole role,
        UsLaxMemberPingOutput? output = null)
    {
        var (eligible, reason, detail) = EvaluateForDisplay(c, statusCode, role, output);

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
            ExpiryDateUpdated = updated,
            Eligible = eligible,
            EligibilityReason = reason,
            EligibilityDetail = detail
        };
    }

    /// <summary>
    /// Runs the SAME <see cref="UsLaxEligibilityPolicy"/> the registration wizard runs, so the
    /// director sees the verdict the front door would actually give — status, involvement,
    /// lastname, DOB and the job's cutoff — rather than the involvement-only check this grid
    /// reported before. The batch MemberPing carries `lastname` and `birthdate`, so the identity
    /// half of that policy is answerable here (confirmed from live traffic 2026-09-01).
    ///
    /// REPORTING ONLY. Nothing here gates the expiry write, which is unchanged.
    ///
    /// Player role only. The policy is the player gate — it requires Player involvement — so
    /// running it over coaches would flag every one of them NotAPlayer. Coach rows are reported
    /// as-was until there is a ruling on what a coach's eligibility even means.
    /// </summary>
    private static (bool Eligible, string Reason, string? Detail) EvaluateForDisplay(
        UsLaxReconciliationCandidateRow c,
        int statusCode,
        UsLaxMembershipRole role,
        UsLaxMemberPingOutput? output)
    {
        if (role != UsLaxMembershipRole.Player)
        {
            return (true, nameof(UsLaxEligibilityReason.Eligible), null);
        }

        var verdict = UsLaxEligibilityPolicy.Evaluate(new UsLaxEligibilityInput
        {
            MembershipNumber = c.SportAssnId,
            ValidThrough = c.ValidThrough,
            TeamValidationDisabled = c.TeamValidationDisabled,
            VendorStatusCode = statusCode,
            VendorMemStatus = output?.MemStatus,
            VendorExpDate = output?.ExpDate,
            VendorLastName = output?.LastName,
            VendorBirthdate = output?.Birthdate,
            VendorInvolvement = output?.Involvement,
            RegistrantLastName = c.LastName,
            RegistrantDob = c.Dob
        });

        return (verdict.Valid, verdict.Reason.ToString(), DescribeVerdict(c, verdict, output));
    }

    /// <summary>
    /// One plain-English line for the grid's Details column, with the real values in it so the
    /// director can act without cross-referencing another screen. The policy's own MessageFor()
    /// is the parent-facing HTML checklist — deliberately not reused here; this audience is the
    /// director, who needs the specific discrepancy, not the remediation steps.
    /// </summary>
    private static string? DescribeVerdict(
        UsLaxReconciliationCandidateRow c,
        UsLaxEligibilityVerdict verdict,
        UsLaxMemberPingOutput? output)
    {
        static string D(DateTime? d) => d?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "—";

        return verdict.Reason switch
        {
            UsLaxEligibilityReason.Eligible => null,
            UsLaxEligibilityReason.TestNumber => "Test membership number — validation bypassed.",
            UsLaxEligibilityReason.TeamBypass => "USA Lacrosse validation is turned off for this team.",
            UsLaxEligibilityReason.NoCutoffConfigured =>
                "No USA Lacrosse valid-through date is set for this event, so memberships can't be checked against a cutoff.",
            UsLaxEligibilityReason.VendorUnavailable => "Couldn't reach USA Lacrosse — try again.",
            UsLaxEligibilityReason.NotFound => "USA Lacrosse has no membership record for this number.",
            UsLaxEligibilityReason.NotActive =>
                $"USA Lacrosse membership status is {output?.MemStatus ?? "unknown"}, not Active.",
            UsLaxEligibilityReason.NotAPlayer =>
                "Membership is not registered as a Player at USA Lacrosse.",
            UsLaxEligibilityReason.ExpiresBeforeCutoff =>
                $"Expires {D(verdict.ExpDate)} — before this event's {D(c.ValidThrough)} cutoff.",
            UsLaxEligibilityReason.LastNameMismatch =>
                $"Last name doesn't match USA Lacrosse (we have \"{c.LastName}\", they have \"{output?.LastName ?? "nothing"}\").",
            UsLaxEligibilityReason.DobMismatch =>
                $"Date of birth doesn't match USA Lacrosse (we have {D(c.Dob)}, they have {output?.Birthdate ?? "nothing"}).",
            _ => null
        };
    }
}
