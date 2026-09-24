using TSIC.Contracts.Repositories;

namespace TSIC.Contracts.Dtos;

/// <summary>
/// The landing card's money phase for a club rep, composed from the SAME per-scope fee
/// resolution the Teams step grid uses (<see cref="ResolvedFee.ResolveFullPaymentPhase"/>),
/// so the card and the grid footer agree by construction.
///
/// Phase is per TEAM, never per job: a converted age group can be in balance phase while its
/// siblings are still in deposit phase. The verdict here is over the rep's PAYABLE teams -
/// not on ARB (its balance drips), not waitlisted (no fee until placed).
/// </summary>
public static class ClubRepMoneyPhase
{
    public const string Single = "single";
    public const string Deposit = "deposit";
    public const string Balance = "balance";
    public const string Mixed = "mixed";

    public readonly record struct Verdict(string? Phase, decimal DueLater, decimal DepositPaid);

    /// <param name="teams">The rep's registered teams as the pulse read them.</param>
    /// <param name="feesByTeamId">Resolved fee per team (team → agegroup → league cascade).</param>
    public static Verdict Compose(
        IReadOnlyList<JobPulseClubRepTeam> teams,
        IReadOnlyDictionary<Guid, ResolvedFee> feesByTeamId)
    {
        var payable = teams.Where(t => !t.OnArb && !t.IsWaitlisted).ToList();
        if (payable.Count == 0) return new Verdict(null, 0m, 0m);

        var depositPhase = 0;
        var balancePhase = 0;
        var dueLater = 0m;
        var depositPaid = 0m;
        foreach (var t in payable)
        {
            var fee = feesByTeamId.GetValueOrDefault(t.TeamId);
            // A deposit STRUCTURE exists only when a deposit slice is configured beside a balance
            // slice. Deposit-less fees are single-phase whatever the flag says: the whole fee is the
            // bill, and the grid's isDepositPhaseRow makes the same test.
            var twoPhase = fee is not null && (fee.Deposit ?? 0m) > 0m && fee.EffectiveBalanceDue > 0m;
            if (!twoPhase) continue;

            if (ResolvedFee.ResolveFullPaymentPhase(fee))
            {
                balancePhase++;
            }
            else
            {
                depositPhase++;
                dueLater += fee!.EffectiveBalanceDue;
                depositPaid += t.PaidTotal;
            }
        }

        var phase = (depositPhase, balancePhase) switch
        {
            (0, 0) => Single,
            (> 0, 0) => Deposit,
            (0, > 0) => Balance,
            _ => Mixed,
        };
        return new Verdict(phase, dueLater, depositPaid);
    }
}
