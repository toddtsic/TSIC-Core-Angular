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
        foreach (var reg in registrations)
        {
            if (reg.BActive == true || reg.RoleId != RoleConstants.Player || reg.AssignedTeamId is not { } teamId)
                continue;
            if (await registrationsRepo.IsSeatAvailableAsync(reg, ct))
                continue; // seat still there — stays in the charge set

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
                reg.Modified = DateTime.Now;
                await registrationsRepo.SaveChangesAsync(ct);
                bounced.Add(new PaymentWaitlistedDto
                {
                    RegistrationId = reg.RegistrationId,
                    TeamName = result.WaitlistTeamName ?? string.Empty
                });
                drop.Add(reg); // never charge a player we just moved to the waitlist
            }
            // else: placement found a seat after all — leave the reg in the charge set.
        }
        foreach (var d in drop) registrations.Remove(d);
        return bounced;
    }
}
