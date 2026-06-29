using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Teams;

public interface ITeamLookupService
{
    Task<IReadOnlyList<AvailableTeamDto>> GetAvailableTeamsForJobAsync(Guid jobId);

    /// <summary>
    /// Distinct club names present at the job (active team in a real agegroup) for the
    /// "Choose Player Club" picker. Independent of the team registration window.
    /// </summary>
    Task<IReadOnlyList<string>> GetClubNamesForJobAsync(Guid jobId);
    Task<(decimal Fee, decimal Deposit)> ResolvePerRegistrantAsync(Guid teamId);

    /// <summary>
    /// Resolves the team's CONFIGURED full price (deposit + balance) for the given role,
    /// independent of payment phase — i.e. <see cref="TSIC.Contracts.Repositories.ResolvedFee.FullPrice"/>.
    /// Returns 0 when the team is missing or the cascade resolves to an unconfigured fee.
    /// Used by the insurance offer build, which must insure the whole forfeitable
    /// registration cost, not just the current-phase (deposit) base.
    /// </summary>
    Task<decimal> ResolveFullPriceAsync(Guid teamId, string roleId);
}
