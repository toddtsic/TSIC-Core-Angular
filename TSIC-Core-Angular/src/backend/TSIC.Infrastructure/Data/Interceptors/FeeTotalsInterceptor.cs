using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using TSIC.Contracts.Payments;
using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Data.Interceptors;

/// <summary>
/// The fee-totals write-chokepoint — currently running in <b>Stage A (OBSERVE / shadow)</b>.
///
/// On every save it computes what <see cref="FeeMath"/> would produce for each changed
/// <see cref="Registrations"/> / <see cref="Teams"/> and compares it to the value the code
/// actually wrote — <b>logging any drift, mutating nothing</b>. This proves FeeMath agrees
/// with every existing write path on real traffic BEFORE the interceptor is ever granted
/// authority to derive the values. (FeeDiscountMp is excluded from FeeMath as a retired stub;
/// drift attributable purely to it on legacy data is flagged via legacyMpWouldMatch.)
///
/// Stage B will flip this to derive FeeTotal/OwedTotal from the components on save; until the
/// drift log reads clean it derives nothing, so it cannot corrupt money during the migration.
/// </summary>
public sealed class FeeTotalsInterceptor : SaveChangesInterceptor
{
    private const decimal Tolerance = 0.005m;

    // Property names are identical on Registrations and Teams.
    private static readonly HashSet<string> FeeProps = new()
    {
        nameof(Registrations.FeeBase), nameof(Registrations.FeeProcessing),
        nameof(Registrations.FeeDiscount), nameof(Registrations.FeeDiscountMp),
        nameof(Registrations.FeeDonation), nameof(Registrations.FeeLatefee),
        nameof(Registrations.FeeTotal), nameof(Registrations.OwedTotal),
        nameof(Registrations.PaidTotal),
    };

    private readonly ILogger<FeeTotalsInterceptor> _logger;

    public FeeTotalsInterceptor(ILogger<FeeTotalsInterceptor> logger) => _logger = logger;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Observe(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Observe(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Observe(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified) continue;
            // On a plain update that didn't touch fees, skip — don't flag pre-existing legacy
            // row drift that this operation didn't produce.
            if (entry.State == EntityState.Modified && !TouchesFee(entry)) continue;

            switch (entry.Entity)
            {
                case Registrations r:
                    Check("Registration", r.RegistrationId,
                        r.FeeBase, r.FeeProcessing, r.FeeDiscount, r.FeeDiscountMp,
                        r.FeeDonation, r.FeeLatefee, r.PaidTotal, r.FeeTotal, r.OwedTotal);
                    break;
                case Teams t:
                    Check("Team", t.TeamId,
                        t.FeeBase ?? 0m, t.FeeProcessing ?? 0m, t.FeeDiscount ?? 0m, t.FeeDiscountMp ?? 0m,
                        t.FeeDonation ?? 0m, t.FeeLatefee ?? 0m, t.PaidTotal ?? 0m, t.FeeTotal ?? 0m, t.OwedTotal ?? 0m);
                    break;
            }
        }
    }

    private static bool TouchesFee(EntityEntry entry)
        => entry.Properties.Any(p => p.IsModified && FeeProps.Contains(p.Metadata.Name));

    private void Check(
        string kind, Guid id,
        decimal feeBase, decimal feeProcessing, decimal feeDiscount, decimal feeDiscountMp,
        decimal feeDonation, decimal feeLatefee, decimal paidTotal,
        decimal storedFeeTotal, decimal storedOwed)
    {
        var computedFeeTotal = FeeMath.ComputeFeeTotal(
            feeBase, feeProcessing, feeDiscount, feeDonation, feeLatefee);
        var computedOwed = FeeMath.ComputeOwed(computedFeeTotal, paidTotal);

        var feeDrift = Math.Abs(computedFeeTotal - storedFeeTotal) > Tolerance;
        var owedDrift = Math.Abs(computedOwed - storedOwed) > Tolerance;
        if (!feeDrift && !owedDrift) return;

        // FeeDiscountMp is a retired discount excluded from FeeMath (kept only as a stub).
        // If the stored value matches the OLD subtract-Mp formula, this drift is purely that
        // dead concept on legacy/club-rep data — expected, not a real inconsistency.
        var legacyMpWouldMatch = feeDiscountMp != 0m
            && Math.Abs((computedFeeTotal - feeDiscountMp) - storedFeeTotal) <= Tolerance;

        _logger.LogWarning(
            "FeeTotalsShadowDrift {Kind} {Id}: storedFeeTotal={StoredFeeTotal} feeMathFeeTotal={ComputedFeeTotal} legacyMpWouldMatch={LegacyMpWouldMatch} storedOwed={StoredOwed} feeMathOwed={ComputedOwed} | base={FeeBase} proc={FeeProcessing} disc={FeeDiscount} discMp={FeeDiscountMp} don={FeeDonation} late={FeeLatefee} paid={PaidTotal}",
            kind, id, storedFeeTotal, computedFeeTotal, legacyMpWouldMatch, storedOwed, computedOwed,
            feeBase, feeProcessing, feeDiscount, feeDiscountMp, feeDonation, feeLatefee, paidTotal);
    }
}
