namespace TSIC.API.Services;

public interface IPlayerBaseTeamFeeResolverService
{
    Task<decimal> ResolveBaseFeeForTeamAsync(Guid teamId);
}
