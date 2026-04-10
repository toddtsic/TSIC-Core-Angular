using TSIC.Contracts.Dtos.ClubRoster;

namespace TSIC.Contracts.Services;

public interface IClubRosterService
{
    Task<List<ClubRosterTeamDto>> GetTeamsAsync(Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default);

    Task<List<ClubRosterPlayerDto>> GetRosterAsync(Guid teamId, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default);

    Task<ClubRosterMutationResultDto> MovePlayersAsync(MovePlayersRequest request, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default);

    Task<ClubRosterMutationResultDto> DeletePlayersAsync(DeletePlayersRequest request, Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default);
}
