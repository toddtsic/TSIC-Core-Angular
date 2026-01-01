using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Players;

public class PlayerBaseTeamFeeResolverService : IPlayerBaseTeamFeeResolverService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IAgeGroupRepository _ageGroupRepo;

    public PlayerBaseTeamFeeResolverService(ITeamRepository teamRepo, IAgeGroupRepository ageGroupRepo)
    {
        _teamRepo = teamRepo;
        _ageGroupRepo = ageGroupRepo;
    }

    public async Task<decimal> ResolveBaseFeeForTeamAsync(Guid teamId)
    {
        // Pull only the minimal fields needed for fee resolution
        var feeInfo = await _teamRepo.GetTeamFeeInfoAsync(teamId);

        // 1) Team-level fee
        var teamBase = feeInfo.FeeBase ?? feeInfo.PerRegistrantFee ?? 0m;
        if (teamBase > 0) return teamBase;

        // 2) Need AgegroupId to look up age group fees - must query Team entity again
        var teamWithAgeGroup = await _teamRepo.Query()
            .Where(t => t.TeamId == teamId)
            .Select(t => new { t.AgegroupId })
            .FirstOrDefaultAsync();

        if (teamWithAgeGroup != null)
        {
            var ageFees = await _ageGroupRepo.GetFeeInfoAsync(teamWithAgeGroup.AgegroupId);
            if (ageFees != null)
            {
                var agBase = (ageFees.Value.TeamFee ?? ageFees.Value.RosterFee) ?? 0m;
                if (agBase > 0) return agBase;
            }
        }

        // 3) League-level fee: no explicit fee fields currently modeled; return 0 for now
        return 0m;
    }
}
