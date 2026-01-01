using Microsoft.EntityFrameworkCore;
using TSIC.Application.Services.Players;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.API.Services.Teams;

namespace TSIC.API.Services.Players;

public class PlayerRegistrationFeeService : IPlayerRegistrationFeeService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IPlayerBaseTeamFeeResolverService _feeResolver;
    private readonly IPlayerFeeCalculator _feeCalculator;
    private readonly ITeamLookupService _teamLookupService;

    public PlayerRegistrationFeeService(
        ITeamRepository teamRepo,
        IPlayerBaseTeamFeeResolverService feeResolver,
        IPlayerFeeCalculator feeCalculator,
        ITeamLookupService teamLookupService)
    {
        _teamRepo = teamRepo;
        _feeResolver = feeResolver;
        _feeCalculator = feeCalculator;
        _teamLookupService = teamLookupService;
    }

    public async Task<decimal> ResolveTeamBaseFeeAsync(Guid teamId)
    {
        // Prefer centralized TeamLookupService resolver for consistency with team listings.
        var (fee, _) = await _teamLookupService.ResolvePerRegistrantAsync(teamId);
        if (fee > 0m) return fee;

        // Fallback to legacy resolver if centralized logic yields zero, for backward compatibility.
        var feeInfo = await _teamRepo.GetTeamFeeInfoAsync(teamId);
        var v = feeInfo.FeeBase ?? feeInfo.PerRegistrantFee ?? 0m;
        if (v > 0m) return v;
        
        return await _feeResolver.ResolveBaseFeeForTeamAsync(teamId);
    }

    public async Task ApplyInitialFeesAsync(Registrations reg, Guid teamId, decimal? teamFeeBase, decimal? teamPerRegistrantFee)
    {
        var paid = reg.PaidTotal;
        if (paid > 0m) return;

        // Centralized fee resolution: prefer provided team values, else resolve via TeamLookupService
        var baseFee = teamFeeBase ?? teamPerRegistrantFee ?? 0m;
        if (baseFee <= 0m)
        {
            baseFee = await ResolveTeamBaseFeeAsync(teamId);
        }
        if (baseFee > 0m)
        {
            if (reg.FeeBase <= 0m) reg.FeeBase = baseFee;
            var (processing, total) = _feeCalculator.ComputeTotals(reg.FeeBase, reg.FeeDiscount, reg.FeeDonation,
                (reg.FeeProcessing > 0m) ? reg.FeeProcessing : null);
            if (reg.FeeProcessing <= 0m) reg.FeeProcessing = processing;
            reg.FeeTotal = total;
            reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
        }
    }
}


