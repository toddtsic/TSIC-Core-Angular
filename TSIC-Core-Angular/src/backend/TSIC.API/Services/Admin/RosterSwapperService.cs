using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the Roster Swapper admin tool.
/// Implements four distinct transfer flows based on registration role:
/// 1. Player → Team: standard swap (UPDATE AssignedTeamId + fee recalc)
/// 2. Unassigned Adult → Team: CREATE new Staff registration
/// 3. Staff → Unassigned pool: DELETE Staff registration
/// 4. Staff → Team: UPDATE AssignedTeamId (no fee recalc)
/// </summary>
public sealed class RosterSwapperService : IRosterSwapperService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IFeeResolutionService _feeService;
    private readonly IJobRepository _jobRepo;
    private readonly ITeamPlacementService _placement;
    private readonly IUsLaxService _usLax;

    public RosterSwapperService(
        IRegistrationRepository registrationRepo,
        ITeamRepository teamRepo,
        IDeviceRepository deviceRepo,
        IFeeResolutionService feeService,
        IJobRepository jobRepo,
        ITeamPlacementService placement,
        IUsLaxService usLax)
    {
        _registrationRepo = registrationRepo;
        _teamRepo = teamRepo;
        _deviceRepo = deviceRepo;
        _feeService = feeService;
        _jobRepo = jobRepo;
        _placement = placement;
        _usLax = usLax;
    }

    public async Task<List<SwapperPoolOptionDto>> GetPoolOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _teamRepo.GetSwapperPoolOptionsAsync(jobId, ct);
    }

    public async Task<List<SwapperPlayerDto>> GetRosterAsync(Guid poolId, Guid jobId, CancellationToken ct = default)
    {
        if (poolId == Guid.Empty)
            return await _registrationRepo.GetUnassignedAdultsAsync(jobId, ct);

        // Validate team belongs to job
        if (!await _teamRepo.BelongsToJobAsync(poolId, jobId, ct))
            throw new ArgumentException("Team does not belong to this job.");

        return await _registrationRepo.GetRosterByTeamIdAsync(poolId, jobId, ct);
    }

    public async Task<List<RosterTransferFeePreviewDto>> PreviewTransferAsync(
        Guid jobId, RosterTransferPreviewRequest request, CancellationToken ct = default)
    {
        if (request.SourcePoolId == request.TargetPoolId)
            throw new ArgumentException("Source and target pools must be different.");

        var registrations = await _registrationRepo.GetRegistrationsForTransferAsync(
            request.RegistrationIds, request.SourcePoolId, jobId, ct);

        if (registrations.Count == 0)
            throw new ArgumentException("No valid registrations found for transfer.");

        var previews = new List<RosterTransferFeePreviewDto>();
        var isSourceUnassigned = request.SourcePoolId == Guid.Empty;
        var isTargetUnassigned = request.TargetPoolId == Guid.Empty;

        // FLOW 2: Unassigned Adult → Team (staff creation)
        if (isSourceUnassigned && !isTargetUnassigned)
        {
            foreach (var reg in registrations)
            {
                string? warning = null;
                var existing = await _registrationRepo.GetExistingStaffAssignmentAsync(
                    reg.UserId!, request.TargetPoolId, jobId, ct);
                if (existing != null)
                    warning = "Already assigned to this team — transfer will be skipped.";

                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "staff-create",
                    CurrentFeeBase = 0,
                    CurrentFeeTotal = 0,
                    NewFeeBase = 0,
                    NewFeeTotal = 0,
                    FeeDelta = 0,
                    Warning = warning
                });
            }
            return previews;
        }

        // FLOW 3: Staff → Unassigned pool (staff removal)
        if (!isSourceUnassigned && isTargetUnassigned)
        {
            foreach (var reg in registrations)
            {
                var roleName = reg.Role?.Name ?? "";
                if (roleName != RoleConstants.Names.StaffName)
                {
                    previews.Add(new RosterTransferFeePreviewDto
                    {
                        RegistrationId = reg.RegistrationId,
                        PlayerName = GetPlayerName(reg),
                        TransferType = "invalid",
                        CurrentFeeBase = reg.FeeBase,
                        CurrentFeeTotal = reg.FeeTotal,
                        NewFeeBase = reg.FeeBase,
                        NewFeeTotal = reg.FeeTotal,
                        FeeDelta = 0,
                        Warning = "Only Staff registrations can be moved to the Unassigned Adults pool."
                    });
                    continue;
                }

                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "staff-delete",
                    CurrentFeeBase = reg.FeeBase,
                    CurrentFeeTotal = reg.FeeTotal,
                    NewFeeBase = 0,
                    NewFeeTotal = 0,
                    FeeDelta = -reg.FeeTotal
                });
            }
            return previews;
        }

        // FLOW 1 or 4: Team → Team (player swap or staff move)
        var targetContext = await _teamRepo.GetTeamWithFeeContextAsync(request.TargetPoolId, ct);
        if (targetContext == null)
            throw new ArgumentException("Target team not found.");

        var (targetTeam, _) = targetContext.Value;

        foreach (var reg in registrations)
        {
            var roleName = reg.Role?.Name ?? "";
            if (roleName == RoleConstants.Names.StaffName)
            {
                // FLOW 4: Staff → Different Team (no fee recalc)
                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "staff-move",
                    CurrentFeeBase = reg.FeeBase,
                    CurrentFeeTotal = reg.FeeTotal,
                    NewFeeBase = 0,
                    NewFeeTotal = 0,
                    FeeDelta = 0
                });
            }
            else
            {
                // FLOW 1: Player → Team (fee recalc via unified service)
                var resolved = await _feeService.ResolveFeeAsync(
                    targetTeam.JobId, RoleConstants.Player, targetTeam.AgegroupId, targetTeam.TeamId, ct);
                var newFeeBase = resolved?.EffectiveBalanceDue ?? 0m;
                // Preview estimate: modifiers preserved from the original registration (the execute
                // path freezes discount/donation and only re-derives the late fee when asked).
                // Derived through FeeMath — the SAME formula RecalcTotals stamps on execute — so the
                // number the director approves is the number the swap produces. Both discount buckets
                // net off; FeeProcessing carries forward (the execute path re-derives it from the new
                // base, so proc is the one term this preview still estimates).
                var isFree = newFeeBase <= 0m;
                var previewDiscount = isFree ? 0m : reg.FeeDiscount;
                var previewDiscountMp = isFree ? 0m : reg.FeeDiscountMp;
                var previewLatefee = isFree ? 0m : reg.FeeLatefee;
                var newTotal = FeeMath.ComputeFeeTotal(
                    newFeeBase, reg.FeeProcessing, previewDiscount, previewDiscountMp,
                    reg.FeeDonation, previewLatefee);

                previews.Add(new RosterTransferFeePreviewDto
                {
                    RegistrationId = reg.RegistrationId,
                    PlayerName = GetPlayerName(reg),
                    TransferType = "player-swap",
                    CurrentFeeBase = reg.FeeBase,
                    CurrentFeeTotal = reg.FeeTotal,
                    NewFeeBase = newFeeBase,
                    NewFeeTotal = newTotal,
                    FeeDelta = newTotal - reg.FeeTotal
                });
            }
        }

        return previews;
    }

    public async Task<RosterTransferResultDto> ExecuteTransferAsync(
        Guid jobId, string adminUserId, RosterTransferRequest request, CancellationToken ct = default)
    {
        if (request.SourcePoolId == request.TargetPoolId)
            throw new ArgumentException("Source and target pools must be different.");

        var registrations = await _registrationRepo.GetRegistrationsForTransferAsync(
            request.RegistrationIds, request.SourcePoolId, jobId, ct);

        if (registrations.Count == 0)
            throw new ArgumentException("No valid registrations found for transfer.");

        var isSourceUnassigned = request.SourcePoolId == Guid.Empty;
        var isTargetUnassigned = request.TargetPoolId == Guid.Empty;
        var now = DateTime.Now;

        int playersTransferred = 0;
        int staffCreated = 0;
        int staffDeleted = 0;
        int feesRecalculated = 0;

        // Who actually moved, and who was deliberately left behind. Reported per registrant rather
        // than as counts: the UI highlights exactly the movers and raises one alert per refusal.
        var movedIds = new List<Guid>();
        // Moved, but carrying a consequence the operator must act on — today only the ARB plan
        // that cannot follow the player. Every registrant in here DID move.
        var warnings = new List<RosterTransferWarningDto>();

        // FLOW 2: Unassigned Adult → Team (staff creation)
        if (isSourceUnassigned && !isTargetUnassigned)
        {
            var targetContext = await _teamRepo.GetTeamWithFeeContextAsync(request.TargetPoolId, ct)
                ?? throw new ArgumentException("Target team not found.");
            var (targetTeam, _) = targetContext;

            // Defense-in-depth: the target team GUID is client-supplied; confirm it belongs to
            // the caller's job before minting a Staff row (which grants roster/PII access).
            // The endpoint is already AdminOnly + job-scoped, so this guards against an
            // accidental cross-job approval rather than an anonymous attacker.
            if (targetTeam.JobId != jobId)
                throw new ArgumentException("Target team does not belong to this job.");

            // No capacity check: roster MaxCount gates self-rostering registrants, NOT admins.
            // The Roster Swapper is an admin-discretion tool and may overfill a team intentionally.

            foreach (var reg in registrations)
            {
                // Check for duplicate
                var existing = await _registrationRepo.GetExistingStaffAssignmentAsync(
                    reg.UserId!, request.TargetPoolId, jobId, ct);
                if (existing != null) continue;

                // Create new Staff registration
                var staffReg = new Registrations
                {
                    RegistrationId = Guid.NewGuid(),
                    UserId = reg.UserId,
                    FamilyUserId = reg.FamilyUserId,
                    JobId = reg.JobId,
                    RoleId = RoleConstants.Staff,
                    AssignedTeamId = request.TargetPoolId,
                    AssignedAgegroupId = targetTeam.AgegroupId,
                    AssignedDivId = targetTeam.DivId,
                    AssignedLeagueId = targetTeam.LeagueId,
                    // Carry the coach's USLax membership onto the minted Staff row so rosters
                    // and reports read it without resolving back to the anchor.
                    SportAssnId = reg.SportAssnId,
                    SportAssnIdexpDate = reg.SportAssnIdexpDate,
                    BActive = true,
                    FeeBase = 0,
                    FeeProcessing = 0,
                    FeeDiscount = 0,
                    FeeDiscountMp = 0,
                    FeeDonation = 0,
                    FeeLatefee = 0,
                    PaidTotal = 0,
                    LebUserId = adminUserId,
                    Modified = now,
                    RegistrationTs = now,
                    BConfirmationSent = false
                };
                _registrationRepo.Add(staffReg);

                // Device sync: mirror source's device links for new Staff reg
                var deviceIds = await _deviceRepo.GetDeviceIdsByRegistrationAsync(reg.RegistrationId, ct);
                foreach (var deviceId in deviceIds)
                {
                    _deviceRepo.AddDeviceTeam(new DeviceTeams
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = deviceId,
                        TeamId = request.TargetPoolId,
                        RegistrationId = staffReg.RegistrationId,
                        Modified = now
                    });
                    _deviceRepo.AddDeviceRegistrationId(new DeviceRegistrationIds
                    {
                        Id = Guid.NewGuid(),
                        DeviceId = deviceId,
                        RegistrationId = staffReg.RegistrationId,
                        Active = true,
                        Modified = now
                    });
                }

                staffCreated++;
            }

            await _registrationRepo.SaveChangesAsync(ct);
            return new RosterTransferResultDto
            {
                PlayersTransferred = 0,
                StaffCreated = staffCreated,
                StaffDeleted = 0,
                FeesRecalculated = 0,
                Message = $"{staffCreated} staff registration(s) created.",
                // Staff creation mints rows; it never moves a paying registrant, so the ARB warning
                // cannot fire here. Empty, not absent — the client always has a list to iterate.
                MovedRegistrationIds = new List<Guid>(),
                Warnings = new List<RosterTransferWarningDto>()
            };
        }

        // FLOW 3: Staff → Unassigned pool (staff removal)
        if (!isSourceUnassigned && isTargetUnassigned)
        {
            foreach (var reg in registrations)
            {
                var roleName = reg.Role?.Name ?? "";
                if (roleName != RoleConstants.Names.StaffName) continue;

                // Device cleanup
                var deviceTeams = await _deviceRepo.GetDeviceTeamsByRegistrationAndTeamAsync(
                    reg.RegistrationId, request.SourcePoolId, ct);
                if (deviceTeams.Count > 0)
                    _deviceRepo.RemoveDeviceTeams(deviceTeams);

                var deviceRegIds = await _deviceRepo.GetDeviceRegistrationIdsByRegistrationAsync(
                    reg.RegistrationId, ct);
                if (deviceRegIds.Count > 0)
                    _deviceRepo.RemoveDeviceRegistrationIds(deviceRegIds);

                // Delete the Staff registration
                _registrationRepo.Remove(reg);
                staffDeleted++;
            }

            await _registrationRepo.SaveChangesAsync(ct);
            return new RosterTransferResultDto
            {
                PlayersTransferred = 0,
                StaffCreated = 0,
                StaffDeleted = staffDeleted,
                FeesRecalculated = 0,
                Message = $"{staffDeleted} staff registration(s) removed.",
                // Staff removal deletes rows; no paying registrant moves, so the ARB warning cannot
                // fire here. Empty, not absent — the client always has a list to iterate.
                MovedRegistrationIds = new List<Guid>(),
                Warnings = new List<RosterTransferWarningDto>()
            };
        }

        // FLOW 1 & 4: Team → Team
        {
            var targetContext = await _teamRepo.GetTeamWithFeeContextAsync(request.TargetPoolId, ct)
                ?? throw new ArgumentException("Target team not found.");
            var (targetTeam, _) = targetContext;

            // Defense-in-depth: the target team GUID is client-supplied; confirm it belongs to
            // the caller's job before reassigning a registration onto it. The endpoint is already
            // AdminOnly + job-scoped, so this guards against an accidental cross-job move.
            if (targetTeam.JobId != jobId)
                throw new ArgumentException("Target team does not belong to this job.");

            // No capacity check: roster MaxCount gates self-rostering registrants, NOT admins.
            // The Roster Swapper is an admin-discretion tool (e.g. moving a waitlisted player
            // onto a full active team) and may overfill a team intentionally.

            foreach (var reg in registrations)
            {
                var roleName = reg.Role?.Name ?? "";

                // ARB plan check — WARN, do not refuse. Ruled 09-04 (Todd, with Ann): moving a
                // player who holds a subscription is routine work, and refusing it stopped that
                // work dead. This used to `continue` and report the registrant as blocked.
                //
                // What is still true, and why the warning survives the guard: a player mid-plan
                // has paid past the deposit, which forces the full-payment phase in
                // StampSwapFeeBase, so FeeBase snaps to the NEW team's price and any rate
                // difference opens a gap. NOTHING REPRICES THE PLAN — the subscription keeps
                // drafting the old amount against the new bill until an operator cancels or
                // adjusts it. Computed BEFORE the mutation below, because the conflict is a
                // comparison against the fees the move is about to overwrite.
                if (roleName != RoleConstants.Names.StaffName)
                {
                    var conflict = await _feeService.DetectArbPlanConflictAsync(
                        reg, targetTeam.JobId, targetTeam.AgegroupId, targetTeam.TeamId, ct);

                    if (conflict != null)
                    {
                        var who = GetPlayerName(reg);
                        warnings.Add(new RosterTransferWarningDto
                        {
                            RegistrationId = reg.RegistrationId,
                            PlayerName = who,
                            Reason = BuildArbWarningReason(who, targetTeam.TeamName, conflict)
                        });
                    }
                }

                var oldTeamId = reg.AssignedTeamId ?? Guid.Empty;

                // Update team assignment
                reg.AssignedTeamId = request.TargetPoolId;
                reg.AssignedAgegroupId = targetTeam.AgegroupId;
                reg.AssignedDivId = targetTeam.DivId;
                reg.AssignedLeagueId = targetTeam.LeagueId;
                reg.Modified = now;
                reg.LebUserId = adminUserId;

                if (roleName == RoleConstants.Names.StaffName)
                {
                    // FLOW 4: Staff → Different Team (no fee recalc)
                    // Entity is already tracked — EF detects property changes automatically
                    playersTransferred++;
                }
                else
                {
                    // FLOW 1: Player → Team (fee recalc via new fee resolution service)
                    await _feeService.ApplySwapFeesAsync(
                        reg, targetTeam.JobId, targetTeam.AgegroupId, targetTeam.TeamId,
                        new FeeApplicationContext(), ct);

                    // Entity is already tracked — EF detects property changes automatically
                    playersTransferred++;
                    feesRecalculated++;
                }

                movedIds.Add(reg.RegistrationId);

                // Device sync: update DeviceTeams for old team → new team
                if (oldTeamId != Guid.Empty)
                {
                    var deviceTeams = await _deviceRepo.GetDeviceTeamsByRegistrationAndTeamAsync(
                        reg.RegistrationId, oldTeamId, ct);
                    foreach (var dt in deviceTeams)
                    {
                        dt.TeamId = request.TargetPoolId;
                        dt.Modified = now;
                    }
                }
            }

            await _registrationRepo.SaveChangesAsync(ct);

            // Mint-on-fill (parity with the registration submit path): if this admin transfer
            // brought the target team to its roster max, proactively create its WAITLIST mirror
            // so the picker can surface the twin. Player-count-based (GetAssignedPlayerCountAsync
            // ignores the staff FLOW 4 moves), idempotent, and a no-op for non-waitlist jobs.
            // Admins may overfill — this never blocks the transfer.
            if (playersTransferred > 0 && targetTeam.MaxCount > 0)
            {
                var committed = await _teamRepo.GetAssignedPlayerCountAsync(targetTeam.TeamId, ct);
                if (committed >= targetTeam.MaxCount)
                    await _placement.EnsureWaitlistMirrorAsync(jobId, targetTeam.TeamId, adminUserId, ct);
            }

            var parts = new List<string>();
            if (playersTransferred > 0) parts.Add($"{playersTransferred} transferred");
            if (feesRecalculated > 0) parts.Add($"{feesRecalculated} fees recalculated");

            // A summary only — each consequence rides in Warnings, one alert per registrant.
            // Counting alone here also keeps the empty-parts case ("." on its own) from reaching
            // the director when nothing moved.
            var summary = parts.Count > 0 ? string.Join(", ", parts) + "." : "No players were moved.";
            if (warnings.Count > 0)
                summary += $" {warnings.Count} moved with a payment plan that did NOT follow — see the alert{(warnings.Count == 1 ? "" : "s")}.";

            return new RosterTransferResultDto
            {
                PlayersTransferred = playersTransferred,
                StaffCreated = 0,
                StaffDeleted = 0,
                FeesRecalculated = feesRecalculated,
                Message = summary,
                MovedRegistrationIds = movedIds,
                Warnings = warnings
            };
        }
    }

    public async Task TogglePlayerActiveAsync(
        Guid registrationId, Guid jobId, bool active, string adminUserId, CancellationToken ct = default)
    {
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct)
            ?? throw new KeyNotFoundException("Registration not found.");

        if (reg.JobId != jobId)
            throw new ArgumentException("Registration does not belong to this job.");

        // The method is named TogglePLAYERActive and the roster UI only ever lists players, but
        // nothing enforced it: the endpoint is AdminOnly, so a Director could pass an
        // administrator's registrationId here and grant or revoke admin access through the
        // roster route, bypassing the Superuser gate on the registration-search path.
        if (!string.Equals(reg.RoleId, RoleConstants.Player, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only player registrations can be toggled here.");

        reg.BActive = active;
        reg.Modified = DateTime.Now;
        reg.LebUserId = adminUserId;
        // Entity is already tracked via FindAsync — EF detects property changes automatically
        await _registrationRepo.SaveChangesAsync(ct);
    }

    public async Task<List<UnassignedAdultQueueRowDto>> GetUnassignedAdultQueueAsync(
        Guid jobId, string adminUserId, CancellationToken ct = default)
    {
        // Build Rule: codify any pre-existing grants into a tagged record before reading, so
        // legacy coaches show with a record. Idempotent — only fires for not-yet-codified coaches.
        await _registrationRepo.SeedAdultRequestRecordsAsync(jobId, adminUserId, ct);
        return await _registrationRepo.GetUnassignedAdultQueueAsync(jobId, ct);
    }

    public async Task<RosterTransferResultDto> ApproveTeamRequestAsync(
        Guid jobId, string adminUserId, Guid registrationId, Guid teamId, CancellationToken ct = default)
    {
        var result = await ExecuteTransferAsync(jobId, adminUserId, new RosterTransferRequest
        {
            RegistrationIds = new List<Guid> { registrationId },
            SourcePoolId = Guid.Empty,   // Unassigned Adults pool ⇒ FLOW 2 mints the Staff row
            TargetPoolId = teamId
        }, ct);

        // Append-on-grant: record the granted team as admin in the coach's append-only record.
        await _registrationRepo.AppendGrantedTeamToRecordAsync(registrationId, jobId, teamId, adminUserId, ct);
        return result;
    }

    public async Task<bool> DenyCoachAsync(
        Guid jobId, string adminUserId, Guid registrationId, CancellationToken ct = default)
    {
        var denied = await _registrationRepo.DenyCoachAsync(registrationId, jobId, adminUserId, ct);
        if (!denied)
            throw new ArgumentException("Coach registration not found for this job.");
        return true;
    }

    public async Task<RevalidateUsLaxResultDto> RevalidateUsLaxAsync(
        Guid jobId, Guid registrationId, CancellationToken ct = default)
    {
        var reference = await _registrationRepo.GetUnassignedAdultUsLaxRefAsync(registrationId, jobId, ct)
            ?? throw new ArgumentException("Coach registration not found for this job.");

        if (string.IsNullOrWhiteSpace(reference.SportAssnId))
            return new RevalidateUsLaxResultDto { Found = false, Message = "No USA Lacrosse number on file." };

        var member = await _usLax.GetMemberAsync(reference.SportAssnId, ct);

        // Vendor unreachable / transient → leave the stored value untouched, just report.
        if (member is null || member.StatusCode == 0)
            return new RevalidateUsLaxResultDto { Found = false, Message = "USA Lacrosse is unreachable right now. Try again shortly." };

        var expDate = DateTime.TryParse(member.Output?.ExpDate, out var dt) ? dt : (DateTime?)null;

        // Definitive response → refresh stored expiry on the anchor + every Staff grant.
        if (!string.IsNullOrWhiteSpace(reference.UserId))
            await _registrationRepo.UpdateUsLaxExpiryForUserInJobAsync(reference.UserId!, jobId, expDate, ct);

        return new RevalidateUsLaxResultDto
        {
            Found = member.StatusCode == 200,
            MemStatus = member.Output?.MemStatus ?? (member.StatusCode == 404 ? "Not found" : null),
            ExpDate = expDate?.ToString("yyyy-MM-dd"),
            Message = member.StatusCode == 200 ? null : (member.ErrorMessage ?? "Membership not found.")
        };
    }

    private static string GetPlayerName(Registrations reg)
    {
        if (reg.User != null)
            return $"{reg.User.LastName ?? ""}, {reg.User.FirstName ?? ""}".Trim().TrimEnd(',').Trim();
        return "Unknown";
    }

    /// <summary>
    /// The director-facing consequence of a move that DID happen. Composed here, in ONE place,
    /// because it must say the same thing wherever it surfaces and because every figure in it is
    /// live money the client has no business re-deriving.
    /// <para>
    /// AR-089 (Ann, 09-08) SUPERSEDES the AR-077 wording below. She withdrew her own text because
    /// it offered Correction Records, "which the client can't use here". Both remedies are GONE by
    /// her ruling — Correction Records AND cancel-and-resubscribe — and neither is to be
    /// reinstated as a helpful addition. The toast now names WHO TO ASK rather than WHAT TO DO.
    /// Her four confirmations, all answered 09-08: it stays PAST tense and after-the-fact (so
    /// AR-076's behaviour is untouched — this is not a pre-move prompt), the plan figures stay,
    /// the name stays in the client-side header, and "Support" is spelled out as an address.
    /// <para>
    /// The address is a TSIC one, and that is a DELIBERATE divergence from AR-068, where the
    /// expiring-card email's reply-to stays the club director because a family asking about their
    /// card is a club money conversation. The audience HERE is the DIRECTOR, not a family — a
    /// director stuck on a subscription they cannot adjust is asking TSIC, not themselves.
    /// </para>
    /// <para>
    /// AR-077 (Ann, 09-06), now superseded: wording supplied by her and built verbatim. The
    /// reprice arithmetic came OUT — it explained what went wrong instead of saying what is true —
    /// and the third sentence went IN, because correcting the player's accounting on the new team
    /// is the move an operator makes next and it does not touch the subscription.
    /// </para>
    /// <para>
    /// Every figure is read off the SUBSCRIPTION mirror (<c>AdnSubscription*</c>), per Ann's
    /// ruling — never off either registration total, and never recomputed as amount × count.
    /// No plan total is printed because none exists on <see cref="ArbPlanConflict"/>.
    /// </para>
    /// <para>
    /// Asserts NO past draft and NO future draft. The guard fires on schedule POSITION, not
    /// <c>AdnSubscriptionStatus</c>, so it also fires for an already-CANCELED plan. Under AR-089
    /// the text makes no forward-looking claim at all — the "future installments" clause that
    /// needed that scoping went out with Correction Records.
    /// </para>
    /// <para>
    /// It renders through
    /// <c>{{ t.message }}</c> interpolation, so it is flowing prose — newlines and markup would
    /// collapse into a run-on line. The header ("Last, First — payment plan needs attention") is
    /// built client-side from <c>PlayerName</c>, which is why this string does not repeat it.
    /// </para>
    /// </summary>
    private static string BuildArbWarningReason(string playerName, string? targetTeamName, ArbPlanConflict c)
    {
        return "This player has an active installment subscription plan and has been moved to a "
             + "team with a different balance due. The plan is unchanged — "
             + $"{c.AmountPerOccurrence:C} per installment, {c.TotalOccurrences} installments, on "
             + "its original schedule. Adjusting this player's accounting will not change the "
             + $"installment plan. Please contact {TsicConstants.SupportEmail} if you have "
             + "questions on how to adjust this account.";
    }
}
