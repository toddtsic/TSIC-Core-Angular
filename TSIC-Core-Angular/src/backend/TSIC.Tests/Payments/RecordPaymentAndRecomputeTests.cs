using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Payments;

/// <summary>
/// RECORD-PAYMENT-AND-RECOMPUTE TESTS (R3 chokepoint)
///
/// RegistrationAccountingRepository.RecordPaymentAndRecomputeAsync is the single seam through
/// which a payment row is written AND the keyed entity's PaidTotal is re-derived from the
/// ledger, in one transaction. The invariant it guarantees:
///
///     entity.PaidTotal == Σ active Payamt over the five payment buckets
///
/// PaidTotal is therefore a cached projection of the accounting rows, not an independently
/// maintained running balance. These tests pin the properties that matter:
///   - it RE-SUMS the whole ledger (never applies a delta), so it self-heals drift;
///   - a team-tagged row reconciles the Team, never the club-rep registration it also tags;
///   - refunds (negative Payamt), non-bucket methods, and inactive rows are handled exactly
///     as GetPaymentTotalsByEntityAsync defines them, end to end.
///
/// Real RegistrationAccountingRepository against an in-memory database; no mocks.
/// (On in-memory, BeginTransaction is a no-op — the transaction's correctness is a SQL Server
/// property; here we assert the resulting numbers in both calling shapes.)
/// </summary>
public class RecordPaymentAndRecomputeTests
{
    private const string UserId = "test-user-recompute";

    // A payment method that belongs to NO bucket — excluded from PaidTotal by design
    // (mirrors BALANCE DUE / Void / Failed, which never sum into paid).
    private static readonly Guid NonBucketMethodId = Guid.Parse("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");

    /// <summary>Build a detached payment row for the method under test to Add itself.</summary>
    private static RegistrationAccounting NewRow(
        Guid? registrationId, Guid? teamId, decimal amount, Guid paymentMethodId) => new()
        {
            RegistrationId = registrationId,
            TeamId = teamId,
            Payamt = amount,
            Dueamt = amount,
            PaymentMethodId = paymentMethodId,
            Active = true,
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
        };

    [Fact]
    public async Task RegistrationRow_SetsPaidTotalFromLedger_AndDerivesOwed()
    {
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 100m, paidTotal: 0m);
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, 40m, AccountingDataBuilder.CheckMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(40m);
        updated.OwedTotal.Should().Be(60m, "OwedTotal = FeeTotal(100) - PaidTotal(40)");
        updated.LebUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Recompute_SumsEntireLedger_NotJustTheNewRow()
    {
        // A delta-based writer would land on 30 (0 + the new row). A ledger re-sum lands on 80.
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 200m, paidTotal: 0m);
        b.AddPayment(reg.RegistrationId, null, 50m, AccountingDataBuilder.CheckMethodId);
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, 30m, AccountingDataBuilder.CcPaymentMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(80m, "the existing $50 plus the new $30 — the whole ledger");
        updated.OwedTotal.Should().Be(120m);
    }

    [Fact]
    public async Task Recompute_HealsDriftedStoredPaidTotal()
    {
        // Stored PaidTotal is a bogus 999 with no rows backing it. Recording one real $30 payment
        // must REPLACE it with the true ledger total (30), not add to the drifted value (1029).
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 100m, paidTotal: 999m);
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, 30m, AccountingDataBuilder.CcPaymentMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(30m, "the ledger is the source of truth; the drifted 999 is discarded");
        updated.OwedTotal.Should().Be(70m);
    }

    [Fact]
    public async Task TeamRow_RecomputesTeam_LeavesClubRepRegistrationUntouched()
    {
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var clubRep = b.AddClubRepRegistration(job.JobId);
        var team = b.AddTeam(job.JobId, ag.AgegroupId, clubRepRegistrationId: clubRep.RegistrationId, feeBase: 500m);
        await b.SaveAsync();

        // Team payment rows are double-tagged (RegistrationId = clubRep AND TeamId = team).
        // Because TeamId is set, the method reconciles the TEAM and leaves the club rep alone;
        // the club rep's rolled-up financials are the caller's SynchronizeClubRepFinancials job.
        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(clubRep.RegistrationId, team.TeamId, 200m, AccountingDataBuilder.CcPaymentMethodId), UserId);

        var updatedTeam = await GetTeamAsync(ctx, team.TeamId);
        updatedTeam.PaidTotal.Should().Be(200m);
        updatedTeam.OwedTotal.Should().Be(300m, "OwedTotal = FeeTotal(500) - PaidTotal(200)");

        var updatedClubRep = await GetRegAsync(ctx, clubRep.RegistrationId);
        updatedClubRep.PaidTotal.Should().Be(0m, "club-rep aggregate is out of scope for a team-keyed write");
    }

    [Fact]
    public async Task Refund_NegativePayamt_NetsPaidTotalDown()
    {
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 100m, paidTotal: 0m);
        b.AddPayment(reg.RegistrationId, null, 80m, AccountingDataBuilder.CcPaymentMethodId);
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, -30m, AccountingDataBuilder.CcCreditMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(50m, "$80 paid less a $30 refund row");
        updated.OwedTotal.Should().Be(50m);
    }

    [Fact]
    public async Task NonBucketMethod_ExcludedFromPaidTotal()
    {
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 100m, paidTotal: 0m);
        b.AddPayment(reg.RegistrationId, null, 80m, AccountingDataBuilder.CheckMethodId);
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, 25m, NonBucketMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(80m, "the $25 non-bucket row never counts toward paid");
        updated.OwedTotal.Should().Be(20m);
    }

    [Fact]
    public async Task InactiveRows_ExcludedFromPaidTotal()
    {
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 100m, paidTotal: 0m);
        var voided = b.AddPayment(reg.RegistrationId, null, 80m, AccountingDataBuilder.CcPaymentMethodId);
        voided.Active = false; // a voided/reversed row is excluded from the re-sum
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, 10m, AccountingDataBuilder.CheckMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(10m, "only the active $10 row counts; the inactive $80 is ignored");
        updated.OwedTotal.Should().Be(90m);
    }

    [Fact]
    public async Task AllPaymentBuckets_ContributeToPaidTotal()
    {
        var ctx = DbContextFactory.Create();
        var b = new AccountingDataBuilder(ctx);
        var job = b.AddJob();
        var reg = b.AddPlayerRegistration(job.JobId, feeBase: 200m, paidTotal: 0m);
        b.AddPayment(reg.RegistrationId, null, 10m, AccountingDataBuilder.CcPaymentMethodId);
        b.AddPayment(reg.RegistrationId, null, 20m, AccountingDataBuilder.EcheckMethodId);
        b.AddPayment(reg.RegistrationId, null, 30m, AccountingDataBuilder.CheckMethodId);
        b.AddPayment(reg.RegistrationId, null, 40m, AccountingDataBuilder.CorrectionMethodId);
        await b.SaveAsync();

        var repo = new RegistrationAccountingRepository(ctx);
        await repo.RecordPaymentAndRecomputeAsync(
            NewRow(reg.RegistrationId, null, 5m, AccountingDataBuilder.CheckMethodId), UserId);

        var updated = await GetRegAsync(ctx, reg.RegistrationId);
        updated.PaidTotal.Should().Be(105m, "CC 10 + eCheck 20 + Check 30 + Correction 40 + new Check 5");
        updated.OwedTotal.Should().Be(95m);
    }

    private static async Task<Registrations> GetRegAsync(SqlDbContext ctx, Guid registrationId) =>
        await ctx.Registrations.FirstAsync(r => r.RegistrationId == registrationId);

    private static async Task<Teams> GetTeamAsync(SqlDbContext ctx, Guid teamId) =>
        await ctx.Teams.FirstAsync(t => t.TeamId == teamId);
}
