using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Camp Day/Night Groups admin service. Thin wrapper over repository calls;
/// guards team ↔ job pairing on roster fetches and delegates jobId enforcement
/// for writes to the registration repository.
/// </summary>
public class CampGroupsService : ICampGroupsService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IDdlOptionsService _ddlOptions;

    public CampGroupsService(
        ITeamRepository teamRepo,
        IRegistrationRepository registrationRepo,
        IDdlOptionsService ddlOptions)
    {
        _teamRepo = teamRepo;
        _registrationRepo = registrationRepo;
        _ddlOptions = ddlOptions;
    }

    public Task<List<TeamRosterCountDto>> GetTeamsAsync(Guid jobId, CancellationToken ct = default)
        => _teamRepo.GetTeamsWithRosterCountForJobAsync(jobId, ct);

    public async Task<CampGroupOptionsDto> GetGroupOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        var opts = await _ddlOptions.GetOptionsAsync(jobId, ct);
        return new CampGroupOptionsDto
        {
            DayGroups = opts.DayGroups,
            NightGroups = opts.NightGroups,
        };
    }

    public async Task<List<CampPlayerDto>> GetCampersAsync(Guid teamId, CancellationToken ct = default)
    {
        return await _registrationRepo.GetCampersByTeamAsync(teamId, ct);
    }

    public Task<bool> UpdateGroupsAsync(
        Guid jobId,
        Guid registrationId,
        UpdateCampGroupsRequest request,
        CancellationToken ct = default)
    {
        return _registrationRepo.UpdateCampGroupsAsync(
            jobId,
            registrationId,
            request.DayGroup,
            request.NightGroup,
            request.UpdateDayGroup,
            request.UpdateNightGroup,
            ct);
    }

    public Task<int> BulkUpdateGroupsAsync(
        Guid jobId,
        BulkUpdateCampGroupsRequest request,
        CancellationToken ct = default)
    {
        return _registrationRepo.BulkUpdateCampGroupsAsync(
            jobId,
            request.RegistrationIds,
            request.DayGroup,
            request.NightGroup,
            request.UpdateDayGroup,
            request.UpdateNightGroup,
            ct);
    }
}
