using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Shared;

/// <summary>
/// The single source of truth for "seat-gone → WAITLIST twin" reconciliation, shared by
/// PreSubmit (review→payment) and the charge itself (its pre-charge backstop). Mirrors the seat
/// rule in <see cref="IRegistrationRepository.IsSeatAvailableAsync"/> (confirmed members vs
/// MaxCount). Dependencies are passed in rather than injected because every caller already holds
/// them — this keeps the helper allocation-free and avoids threading a new DI dependency (and its
/// constructor churn) through PaymentService and PlayerRegistrationService.
/// </summary>
public static class SeatReconciliation
{
    /// <summary>
    /// Split a set of registrations BEFORE charging/committing: keep the players who still have a
    /// seat, and move the players whose team filled up to that team's WAITLIST twin at $0 — those
    /// are NOT charged. Only NEW player seats are gated: an already-confirmed reg (BActive=true)
    /// owns its seat, and non-players / team-less regs are untouched. A bounced player is re-priced
    /// onto the twin's $0 fee through the existing swap engine
    /// (<see cref="IFeeResolutionService.ApplySwapFeesAsync"/>, the same path the roster swapper
    /// uses), dropped from <paramref name="registrations"/> so the charge never sees it, and
    /// returned for the caller's "moved to waitlist" bucket. Mutates <paramref name="registrations"/>
    /// in place.
    /// </summary>
    public static async Task<List<PaymentWaitlistedDto>> ReconcileSeatsAsync(
        Guid jobId,
        string familyUserId,
        List<Registrations> registrations,
        IRegistrationRepository registrationsRepo,
        ITeamPlacementService placement,
        ITeamRepository teams,
        IFeeResolutionService feeService,
        CancellationToken ct = default)
    {
        var bounced = new List<PaymentWaitlistedDto>();
        var drop = new List<Registrations>();
        // Seats this pass has already handed out, per real team. IsSeatAvailableAsync counts only
        // confirmed (BActive=1) members, so two siblings in ONE submission both see the last seat as
        // free (neither is active yet) and both would stay → overfill. This running tally makes the
        // 2nd sibling's check see the seat the 1st just took, routing the overflow to the twin.
        var claimedThisBatch = new Dictionary<Guid, int>();
        // Hand out the scarce seat in CREATION order: the earliest-registered sibling keeps the
        // real seat and any later sibling on the same full team is the one bounced to the twin.
        // GetByJobAndFamilyWithUsersAsync returns no explicit order, so without this the seat went
        // to whichever reg SQL happened to surface first (e.g. the 2nd-created child), which read
        // as "the wrong kid got waitlisted". RegistrationTs is stamped at create (DateTime.Now).
        foreach (var reg in registrations.OrderBy(r => r.RegistrationTs))
        {
            if (reg.BActive == true || reg.RoleId != RoleConstants.Player || reg.AssignedTeamId is not { } teamId)
                continue;
            var reserved = claimedThisBatch.TryGetValue(teamId, out var already) ? already : 0;
            if (await registrationsRepo.IsSeatAvailableAsync(reg, reserved, ct))
            {
                claimedThisBatch[teamId] = reserved + 1; // this reg takes the seat; later siblings see it gone
                continue; // seat still there — stays in the charge set
            }

            var result = await placement.ResolveRosterPlacementAsync(jobId, teamId, familyUserId, ct);
            if (result is { IsWaitlisted: true })
            {
                var twin = await teams.GetTeamFromTeamId(result.TeamId, ct);
                reg.AssignedTeamId = result.TeamId;
                reg.Assignment = $"Player: {result.WaitlistTeamName}";
                if (twin != null)
                {
                    await feeService.ApplySwapFeesAsync(
                        reg, jobId, twin.AgegroupId, result.TeamId,
                        new FeeApplicationContext { IsFullPaymentRequired = false }, ct);
                }
                // A waitlist placement is a COMPLETED registration, not a pending-payment hold: the
                // player is confirmed on the $0 WAITLIST twin (nothing to pay). Activate it here —
                // this reg is dropped from the charge set / familyRegs below, so the post-reconcile
                // ActivateIfFree pass never sees it; without this it would sit BActive=false (pending),
                // inconsistent with a twin reg the registrant picked directly (which ActivateIfFree
                // does activate at $0). The twin is unlimited (MaxCount huge), so this never overfills.
                reg.BActive = true;
                reg.Modified = DateTime.Now;
                await registrationsRepo.SaveChangesAsync(ct);
                bounced.Add(new PaymentWaitlistedDto
                {
                    RegistrationId = reg.RegistrationId,
                    TeamName = result.WaitlistTeamName ?? string.Empty,
                    // Both callers load the reg with its User (GetByJobAndFamilyWithUsersAsync), so
                    // the player's name is available for the "X is on the waitlist" notice.
                    PlayerName = $"{reg.User?.FirstName} {reg.User?.LastName}".Trim()
                });
                drop.Add(reg); // never charge a player we just moved to the waitlist
            }
            // else: placement found a seat after all — leave the reg in the charge set.
        }
        foreach (var d in drop) registrations.Remove(d);
        return bounced;
    }
}
