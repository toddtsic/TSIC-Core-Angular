using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Payments;

/// <summary>
/// DROP-TEAM FEE ZEROING — LEDGER TRUTH
///
/// ZeroFeesForTeamAsync runs in the drop-team (soft-drop) flow: every player on a team being
/// moved to "Dropped Teams" has their fee components zeroed. The soft-drop branch is taken
/// precisely when the team has players/payments (the clean, payment-free case hard-deletes),
/// so a player who already paid is real.
///
/// Per the signed-owed policy the row must show the truth: with fees zeroed and a payment on
/// record, the org owes that money back, so OwedTotal = FeeTotal - PaidTotal = -PaidTotal
/// (OVER PAID / refund owed) — never a flat 0 that hides the liability.
/// </summary>
public class ZeroFeesForTeamTests
{
    private static readonly Guid JobId = Guid.Parse("CCCCCCCC-0000-0000-0000-000000000001");

    private static Registrations Player(Guid teamId, decimal feeBase, decimal feeProcessing, decimal paidTotal)
    {
        var feeTotal = feeBase + feeProcessing;
        return new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = JobId,
            AssignedTeamId = teamId,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeDiscount = 0m,
            FeeDonation = 0m,
            FeeLatefee = 0m,
            FeeTotal = feeTotal,
            PaidTotal = paidTotal,
            OwedTotal = feeTotal - paidTotal,
            BActive = true,
            Modified = DateTime.Now
        };
    }

    [Fact(DisplayName = "Drop-team zero fees: a player who already paid is left owed a refund (OwedTotal = -PaidTotal)")]
    public async Task ZeroFees_PaidPlayer_LeavesNegativeOwedRefund()
    {
        var ctx = DbContextFactory.Create();
        var teamId = Guid.NewGuid();
        var reg = Player(teamId, feeBase: 500m, feeProcessing: 17.50m, paidTotal: 517.50m); // paid in full
        ctx.Registrations.Add(reg);
        await ctx.SaveChangesAsync();

        var affected = await new RegistrationRepository(ctx).ZeroFeesForTeamAsync(teamId, JobId);

        affected.Should().Be(1);
        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeBase.Should().Be(0m);
        dbReg.FeeProcessing.Should().Be(0m);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.PaidTotal.Should().Be(517.50m);    // payment record preserved
        dbReg.OwedTotal.Should().Be(-517.50m);   // refund owed surfaced (pre-fix this was hidden as 0)
    }

    [Fact(DisplayName = "Drop-team zero fees: an unpaid player nets to zero owed")]
    public async Task ZeroFees_UnpaidPlayer_NetsToZero()
    {
        var ctx = DbContextFactory.Create();
        var teamId = Guid.NewGuid();
        var reg = Player(teamId, feeBase: 500m, feeProcessing: 17.50m, paidTotal: 0m);
        ctx.Registrations.Add(reg);
        await ctx.SaveChangesAsync();

        await new RegistrationRepository(ctx).ZeroFeesForTeamAsync(teamId, JobId);

        var dbReg = await ctx.Registrations.FirstAsync(r => r.RegistrationId == reg.RegistrationId);
        dbReg.FeeTotal.Should().Be(0m);
        dbReg.OwedTotal.Should().Be(0m);   // nothing paid, nothing owed
    }
}
