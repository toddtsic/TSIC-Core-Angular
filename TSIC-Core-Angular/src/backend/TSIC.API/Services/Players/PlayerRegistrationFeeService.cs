using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Players;

/// <summary>
/// Single source of truth for player fee resolution and application.
/// Consolidates all fee cascades (formerly in TeamLookupService, LadtService, RosterSwapperService)
/// into one authoritative path.
/// </summary>
public class PlayerRegistrationFeeService : IPlayerRegistrationFeeService
{
    private const int TournamentJobType = 2;

    private readonly ITeamRepository _teamRepo;
    private readonly IPlayerFeeCalculator _feeCalc;

    public PlayerRegistrationFeeService(
        ITeamRepository teamRepo,
        IPlayerFeeCalculator feeCalc)
    {
        _teamRepo = teamRepo;
        _feeCalc = feeCalc;
    }

    /// <inheritdoc />
    public decimal ResolveBaseFee(TeamFeeData data)
    {
        // Tournament guard: team-level fees (PerRegistrantFee, TeamFee, RosterFee)
        // represent club rep costs in tournaments, not player costs.
        // Only charge players if an explicit PlayerFeeOverride is set.
        var prFee = data.PerRegistrantFee ?? 0m;
        var agOverride = data.AgegroupPlayerFeeOverride ?? 0m;
        var leagueOverride = data.LeaguePlayerFeeOverride ?? 0m;

        if (data.JobTypeId == TournamentJobType && prFee <= 0m && agOverride <= 0m && leagueOverride <= 0m)
            return 0m;

        // Cascade: most specific → most general
        // Priority 1: Team per-registrant fee (this team charges this amount)
        if (prFee > 0m)
            return prFee;

        // Priority 2: Agegroup-level override (all teams in this agegroup)
        if (agOverride > 0m)
            return agOverride;

        // Priority 3: League-level override (all teams in this league)
        if (leagueOverride > 0m)
            return leagueOverride;

        // No player fee configured — AG.TeamFee and AG.RosterFee are club rep fees, not player fees
        return 0m;
    }

    /// <inheritdoc />
    public async Task<decimal> ResolveBaseFeeAsync(Guid teamId, CancellationToken ct = default)
    {
        var data = await _teamRepo.GetTeamFeeDataAsync(teamId, ct);
        if (data == null)
            return 0m;

        return ResolveBaseFee(data);
    }

    /// <inheritdoc />
    public async Task<Dictionary<Guid, decimal>> ResolveBaseFeesByTeamIdsAsync(
        IReadOnlyList<Guid> teamIds, CancellationToken ct = default)
    {
        var dataMap = await _teamRepo.GetTeamFeeDataByTeamIdsAsync(teamIds, ct);
        var result = new Dictionary<Guid, decimal>(dataMap.Count);
        foreach (var (teamId, data) in dataMap)
        {
            result[teamId] = ResolveBaseFee(data);
        }
        return result;
    }

    /// <inheritdoc />
    public void ApplyFees(Registrations reg, decimal baseFee, PlayerFeeContext ctx)
    {
        // Initial apply: don't overwrite existing fee if player already has one
        if (!ctx.IsRecalculation && reg.FeeBase > 0m)
            return;

        if (baseFee <= 0m)
        {
            // Zero fee: clear fee-dependent fields, preserve FeeDonation
            reg.FeeBase = 0m;
            reg.FeeProcessing = 0m;
            reg.FeeDiscount = 0m;
            reg.FeeLatefee = 0m;
            // FeeDonation intentionally preserved — player's voluntary choice
        }
        else
        {
            reg.FeeBase = baseFee;

            if (ctx.AddProcessingFees)
            {
                // Reduce processing fee basis by non-CC payments already made
                var adjustedBase = Math.Max(baseFee - ctx.NonCcPayments, 0m);
                reg.FeeProcessing = _feeCalc.GetDefaultProcessing(adjustedBase);
            }
            else
            {
                reg.FeeProcessing = 0m;
            }

            // FeeDiscount, FeeDonation, FeeLatefee all preserved
        }

        // Always recalculate totals
        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
    }
}
