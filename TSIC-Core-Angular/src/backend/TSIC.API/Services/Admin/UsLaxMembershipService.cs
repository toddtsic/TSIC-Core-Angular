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
using TSIC.Domain.Entities;
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
    private readonly IUserRepository _users;
    private readonly IEmailBatchService _emailBatch;
    private readonly ITextSubstitutionService _textSubstitution;
    private readonly IEmailTestSendService _testSend;
    private readonly ILogger<UsLaxMembershipService> _logger;

    public UsLaxMembershipService(
        IRegistrationRepository registrations,
        IUsLaxService usLax,
        IJobRepository jobs,
        IFamiliesRepository families,
        IUserRepository users,
        IEmailBatchService emailBatch,
        ITextSubstitutionService textSubstitution,
        IEmailTestSendService testSend,
        ILogger<UsLaxMembershipService> logger)
    {
        _registrations = registrations;
        _usLax = usLax;
        _jobs = jobs;
        _families = families;
        _users = users;
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
        var jobPath = jobInfo?.JobPath ?? string.Empty;
        var jobValidThrough = jobInfo?.UsLaxNumberValidThroughDate;

        // From display = the public job/org label, matching every other batch surface. The From
        // ADDRESS is forced to the SES-verified identity downstream; this is only what recipients see.
        var fromName = jobInfo?.DisplayName ?? jobInfo?.JobName ?? string.Empty;

        // Reply-To = the admin who sent it. SES rewrites From to support@, which delivers nothing, so
        // Reply-To is the ONLY place the sending human can live — without it a parent who hits Reply
        // to ask about their kid's membership is talking to a mailbox no one reads. Legacy put the
        // sender on From and Reply-To both; this is that intent, carried onto the SES chokepoint.
        var sender = string.IsNullOrWhiteSpace(senderUserId) ? null : await _users.GetByIdAsync(senderUserId, ct);
        var senderEmail = sender?.Email;
        var senderName = $"{sender?.FirstName} {sender?.LastName}".Trim();

        // SECURITY — the recipient snapshot is CLIENT-BUILT, so nothing in it may decide where mail
        // goes. Every posted registrationId is confirmed to belong to the CALLER'S job before anything
        // else happens. Without this an admin of any job could post another job's ids (or ids paired
        // with an arbitrary Email) and have the batch deliver arbitrary HTML under our SES identity —
        // and the per-recipient render, which keys on registrationId ALONE, would substitute the other
        // job's registrant into the tokens. Same guard, same shape, as
        // RegistrationSearchService.StartBatchEmailAsync.
        var postedIds = request.Recipients.Select(r => r.RegistrationId).Distinct().ToList();
        var regs = await _registrations.GetByIdsAsync(postedIds, ct);
        var foreign = regs.Where(reg => reg.JobId != jobId).Select(reg => reg.RegistrationId).ToList();
        if (foreign.Count > 0)
        {
            // Should be unreachable from our own UI, which only ever posts rows it loaded for this
            // job. If it fires it is either an FE defect or someone hand-crafting the request, and
            // both are worth seeing — so it is a named Warning rather than an anonymous 500.
            _logger.LogWarning(
                "USLax email REJECTED: job {JobId} sender {SenderUserId} posted {ForeignCount} of "
                + "{PostedCount} registrationIds belonging to another job.",
                jobId, senderUserId, foreign.Count, postedIds.Count);
            throw new InvalidOperationException("Some registrations do not belong to this job.");
        }

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

        // Who actually needs the email is decided by UsLaxEligibilityPolicy — the SAME rule the
        // registration form runs — against data we fetch ourselves. It previously ran a second,
        // weaker copy of that rule over the browser's snapshot, which checked only status and expiry.
        // That copy called an identity mismatch "healthy" and silently skipped exactly the families
        // the front door is blocking, while nagging the test-number and team-bypass exemptions.
        //
        // The candidate rows carry the director's cutoff, the team bypass, and the registrant's real
        // lastname/DOB; the ping supplies the vendor half. Both roles are loaded because the request
        // does not carry an audience — each registration's own RoleId decides which rule it gets.
        // Sequential awaits, never Task.WhenAll: these share the scoped DbContext.
        var candidates = (await _registrations.GetUsLaxReconciliationCandidatesAsync(jobId, UsLaxMembershipRole.Player, ct))
            .Concat(await _registrations.GetUsLaxReconciliationCandidatesAsync(jobId, UsLaxMembershipRole.Coach, ct))
            .GroupBy(c => c.RegistrationId)
            .ToDictionary(g => g.Key, g => g.First());

        var pingIds = request.Recipients
            .Select(r => candidates.TryGetValue(r.RegistrationId, out var c) ? c.SportAssnId : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct()
            .ToList();

        IReadOnlyDictionary<string, UsLaxMemberPingResult> pingByMember;
        try
        {
            pingByMember = await _usLax.GetMembersAsync(pingIds, ct);
        }
        catch (Exception ex)
        {
            // Every recipient then lands in the unverifiable bucket below and NOBODY is emailed,
            // which is the point: an outage on our side must not tell a whole event their
            // memberships are broken.
            _logger.LogWarning(ex, "USLax batch ping threw while composing the email audience for job {JobId}.", jobId);
            pingByMember = new Dictionary<string, UsLaxMemberPingResult>();
        }

        // Up-front partition, so the rollup is known immediately and returned in the start response:
        //   healthy      → the policy passes them; never false-alarm a valid member
        //   unverifiable → the failure is OURS (vendor unreachable / no cutoff set), so stay quiet
        //   no-email     → no sendable address resolved (also covers an id that no longer exists)
        //   actionable   → everything else, becomes the background batch
        var skippedNames = new List<string>();
        var unverifiableNames = new List<string>();
        var missingEmail = 0;
        var noCutoffConfigured = false;
        var actionable = new List<UsLaxSendItem>();
        // Why each recipient landed where it did, counts only — never names. A tally like
        // "DobMismatch: 47" on a 60-person event says the comparison is broken, not that 47 families
        // are. That is the fastest read there is on whether the stricter audience is behaving.
        var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in request.Recipients)
        {
            regById.TryGetValue(r.RegistrationId, out var reg);
            candidates.TryGetValue(r.RegistrationId, out var candidate);
            var ping = candidate is not null && pingByMember.TryGetValue(candidate.SportAssnId, out var p) ? p : null;

            var (disposition, reason) = Decide(reg, candidate, ping, jobValidThrough, ref noCutoffConfigured);
            reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason) + 1;

            if (disposition == UsLaxEmailDisposition.Healthy)
            {
                skippedNames.Add($"{r.FirstName} {r.LastName}".Trim());
                continue;
            }
            if (disposition == UsLaxEmailDisposition.Unverifiable)
            {
                unverifiableNames.Add($"{r.FirstName} {r.LastName}".Trim());
                continue;
            }

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
                        FromName = fromName,
                        ReplyToName = senderName,
                        ReplyToAddress = senderEmail,
                        Subject = subject,
                        HtmlBody = body,
                        ToAddresses = i.ToAddresses
                    },
                    UnsubscribeRegId = r.RegistrationId // engine appends the unsubscribe footer
                };
            },
            // Engine writes the EmailLogs audit row from this (replaces USLax's manual log).
            Audit = new EmailBatchAudit
            {
                JobId = jobId,
                SenderUserId = senderUserId,
                Subject = subjectTemplate,
                BodyTemplate = bodyTemplate,
                // What the family actually saw in their inbox — the Email Troubleshooter reads this
                // column, and it was writing NULL for every USLax send.
                SendFrom = senderEmail
            },
            // The sender's receipt, copied to the job's always-copy oversight list — the same hook
            // Search Reg uses. Legacy USLax mailed the admin a completion summary; the port dropped it.
            OnCompleteAsync = (status, sp, token) => BatchCompletionReceipt.SendAsync(
                status, sp, jobId, senderEmail, fromName, subjectTemplate, bodyTemplate, token)
        };

        var handle = await _emailBatch.StartAsync(plan, new EmailBatchOptions(), ct);

        // One line per send, mirroring the reconcile line. The engine logs only failures and the
        // EmailLogs row records what went out, so sent/failed is already answerable in SSMS — what
        // is NOT recorded anywhere is the audience decision. That partition lived only in the HTTP
        // response to the browser and then evaporated, which is precisely what you need after the
        // fact when asking "why did only nine of the forty I selected get an email?"
        // Counts only, no member details — the same PII rule the reconcile line follows.
        _logger.LogInformation(
            "USLax email: job {JobId} sender {SenderUserId} — {Selected} selected, {Queued} queued, "
            + "{SkippedHealthy} already eligible, {Unverifiable} unverifiable, {MissingEmail} no address, "
            + "noCutoff={NoCutoffConfigured}, reasons {@Reasons}",
            jobId, senderUserId, request.Recipients.Count, handle.TotalRecipients,
            skippedNames.Count, unverifiableNames.Count, missingEmail, noCutoffConfigured, reasonCounts);

        return new UsLaxEmailStartResponse
        {
            BatchJobId = handle.JobId,
            TotalRecipients = handle.TotalRecipients,
            MissingEmail = missingEmail,
            SkippedHealthy = skippedNames.Count,
            SkippedNames = skippedNames,
            Unverifiable = unverifiableNames.Count,
            UnverifiableNames = unverifiableNames,
            NoCutoffConfigured = noCutoffConfigured
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

    /// <summary>What to do with one selected recipient.</summary>
    private enum UsLaxEmailDisposition
    {
        /// <summary>Queue the email — there is something the family can act on.</summary>
        Send,
        /// <summary>Passes the same check the registration form applies. Say nothing.</summary>
        Healthy,
        /// <summary>
        /// No verdict is possible because the failure is OURS — USA Lacrosse unreachable, or no
        /// cutoff date configured on the job. Say nothing and report it: telling a family their
        /// membership is broken because our own call failed is a false alarm, and on an outage it
        /// would be a false alarm to every family on the list at once.
        /// </summary>
        Unverifiable
    }

    /// <summary>
    /// Decides one recipient's disposition from data WE fetched — never from the request snapshot.
    ///
    /// Players run <see cref="UsLaxEligibilityPolicy"/>, the same rule the registration form and the
    /// reconcile grid run, so the tool can no longer stay silent about a family the front door is
    /// blocking. Coaches keep the older status-and-expiry rule until there is a ruling on what a
    /// coach's eligibility means — but they now run it against the fresh ping rather than the
    /// browser's copy of it, which is the same rule on trustworthy inputs.
    /// </summary>
    private static (UsLaxEmailDisposition Disposition, string Reason) Decide(
        Registrations? reg,
        UsLaxReconciliationCandidateRow? candidate,
        UsLaxMemberPingResult? ping,
        DateTime? jobValidThrough,
        ref bool noCutoffConfigured)
    {
        // Not a reconciliation candidate at all (no number on file, inactive, or gone). Nothing to
        // judge, so nothing to claim.
        if (reg is null || candidate is null) return (UsLaxEmailDisposition.Unverifiable, "NotACandidate");

        // Coaches run the SAME policy with Coach involvement, not the weaker status-and-expiry rule
        // they used to. The grid judges them with the policy now, so anything less here would let
        // the batch call a coach the grid flagged "healthy" and silently skip them — precisely the
        // false-negative that was fixed for players and must not be reintroduced on the coach side.
        var isCoach = reg.RoleId != RoleConstants.Player;

        var verdict = UsLaxEligibilityPolicy.Evaluate(new UsLaxEligibilityInput
        {
            MembershipNumber = candidate.SportAssnId,
            RequiredInvolvement = isCoach ? UsLaxInvolvement.Coach : UsLaxInvolvement.Player,
            ValidThrough = candidate.ValidThrough,
            TeamValidationDisabled = candidate.TeamValidationDisabled,
            VendorStatusCode = ping?.StatusCode ?? 0,
            VendorMemStatus = ping?.Output?.MemStatus,
            VendorExpDate = ping?.Output?.ExpDate,
            VendorLastName = ping?.Output?.LastName,
            VendorBirthdate = ping?.Output?.Birthdate,
            VendorInvolvement = ping?.Output?.Involvement,
            RegistrantLastName = candidate.LastName,
            RegistrantDob = candidate.Dob
        });

        var reason = verdict.Reason.ToString();
        if (verdict.Valid) return (UsLaxEmailDisposition.Healthy, reason);

        // The two verdicts that are our problem, not the family's. NoCutoffConfigured is reported
        // separately because it is one blank field on the director's own setup screen, and without
        // saying so a wholesale skip looks like a malfunction.
        if (verdict.Reason == UsLaxEligibilityReason.NoCutoffConfigured)
        {
            noCutoffConfigured = true;
            return (UsLaxEmailDisposition.Unverifiable, reason);
        }
        if (verdict.Reason == UsLaxEligibilityReason.VendorUnavailable)
            return (UsLaxEmailDisposition.Unverifiable, reason);

        return (UsLaxEmailDisposition.Send, reason);
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
    /// BOTH ROLES. The policy takes the required involvement as a parameter — Player for players,
    /// Coach for the adult audience — because legacy ran two validators (ValidationRemoteController
    /// and ValidationCoachRemoteController) whose rules differ in nothing else. Coach rows were
    /// previously hardcoded Eligible, which meant this grid could not report a coach whose
    /// membership had lapsed, was registered under a different name, or was never a coach
    /// membership at all.
    /// </summary>
    private static (bool Eligible, string Reason, string? Detail) EvaluateForDisplay(
        UsLaxReconciliationCandidateRow c,
        int statusCode,
        UsLaxMembershipRole role,
        UsLaxMemberPingOutput? output)
    {
        var verdict = UsLaxEligibilityPolicy.Evaluate(new UsLaxEligibilityInput
        {
            MembershipNumber = c.SportAssnId,
            RequiredInvolvement = role == UsLaxMembershipRole.Coach
                ? UsLaxInvolvement.Coach
                : UsLaxInvolvement.Player,
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

        return (verdict.Valid, verdict.Reason.ToString(), UsLaxEligibilityPolicy.DetailFor(
            verdict, c.ValidThrough, c.LastName, c.Dob,
            output?.MemStatus, output?.LastName, output?.Birthdate));
    }
}
