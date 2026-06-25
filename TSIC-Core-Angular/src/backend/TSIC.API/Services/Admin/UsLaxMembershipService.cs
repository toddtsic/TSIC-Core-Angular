using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

public sealed class UsLaxMembershipService : IUsLaxMembershipService
{
    // Mirrors the CC payment-method GUID used across the codebase. Accounting tokens are
    // not expected in USLax templates, but the engine parameter is required.
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

    private readonly IRegistrationRepository _registrations;
    private readonly IUsLaxService _usLax;
    private readonly IJobRepository _jobs;
    private readonly IEmailBatchService _emailBatch;
    private readonly ILogger<UsLaxMembershipService> _logger;

    public UsLaxMembershipService(
        IRegistrationRepository registrations,
        IUsLaxService usLax,
        IJobRepository jobs,
        IEmailBatchService emailBatch,
        ILogger<UsLaxMembershipService> logger)
    {
        _registrations = registrations;
        _usLax = usLax;
        _jobs = jobs;
        _emailBatch = emailBatch;
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

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            pingByMember.TryGetValue(c.SportAssnId, out var ping);
            var row = await BuildRowFromPingAsync(c, ping, request.Role, ct);
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

    private async Task<UsLaxReconciliationRowDto> BuildRowFromPingAsync(
        UsLaxReconciliationCandidateRow c,
        UsLaxMemberPingResult? ping,
        UsLaxMembershipRole role,
        CancellationToken ct)
    {
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

        return BuildRow(c, statusCode: 200, errorMessage: null, newExpiry: newExpiry, updated: updated, output: output);
    }

    public async Task<UsLaxEmailStartResponse> StartEmailAsync(Guid jobId, string? senderUserId, UsLaxEmailRequest request, CancellationToken ct = default)
    {
        var jobInfo = await _jobs.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobName = jobInfo?.JobName ?? string.Empty;
        var jobPath = jobInfo?.JobPath ?? string.Empty;
        var jobValidThrough = jobInfo?.UsLaxNumberValidThroughDate;

        // Up-front partition — pure checks on the request snapshot (no I/O), so the skip rollup is
        // known immediately and returned in the start response:
        //   healthy   → NeedsAction == false (never false-alarm a valid member, even if force-selected)
        //   no-email  → blank Email
        //   actionable→ everything else, becomes the background batch
        var skippedNames = new List<string>();
        var missingEmail = 0;
        var actionable = new List<UsLaxEmailRecipientDto>();
        foreach (var r in request.Recipients)
        {
            if (!NeedsAction(r, jobValidThrough))
            {
                skippedNames.Add($"{r.FirstName} {r.LastName}".Trim());
                continue;
            }
            if (string.IsNullOrWhiteSpace(r.Email))
            {
                missingEmail++;
                continue;
            }
            actionable.Add(r);
        }

        // Opt-out is resolved SERVER-SIDE by registrationId — the FE recipient snapshot can't be
        // trusted for it. One batch load builds the opted-out set the engine's IsOptedOut closes over.
        var optedOut = (await _registrations.GetByIdsAsync(actionable.Select(a => a.RegistrationId).ToList(), ct))
            .Where(reg => reg.BemailOptOut)
            .Select(reg => reg.RegistrationId)
            .ToHashSet();

        var subjectTemplate = request.Subject;
        var bodyTemplate = request.Body;

        var plan = new EmailBatchPlan<UsLaxEmailRecipientDto>
        {
            SeedAsync = (_, _) => Task.FromResult(new EmailBatchSeed<UsLaxEmailRecipientDto> { Items = actionable }),
            IsOptedOut = r => optedOut.Contains(r.RegistrationId),
            DescribeItem = r => $"{r.FirstName} {r.LastName}".Trim(),
            RenderAsync = async (r, sp, _) =>
            {
                var toAddresses = BatchEmailRecipientFilter.BuildSendableSet(new[] { r.Email });
                if (toAddresses.Count == 0) return null;

                // Same TextSubstitution engine as every other email — resolved from the render scope.
                var textSub = sp.GetRequiredService<ITextSubstitutionService>();
                var extras = BuildUsLaxExtras(r);
                var (subject, body) = await textSub.SubstituteSubjectAndBodyAsync(
                    jobPath, jobId, CcPaymentMethodId, r.RegistrationId, string.Empty,
                    subjectTemplate, bodyTemplate, inviteTargetJobPath: null, extraTokens: extras);

                return new EmailBatchRendered
                {
                    Message = new EmailMessageDto
                    {
                        FromName = jobName,
                        Subject = subject,
                        HtmlBody = body,
                        ToAddresses = toAddresses
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
