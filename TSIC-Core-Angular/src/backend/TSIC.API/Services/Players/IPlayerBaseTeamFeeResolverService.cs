namespace TSIC.API.Services.Players;

public interface IPlayerBaseTeamFeeResolverService
{
    Task<decimal> ResolveBaseFeeForTeamAsync(Guid teamId);
}
