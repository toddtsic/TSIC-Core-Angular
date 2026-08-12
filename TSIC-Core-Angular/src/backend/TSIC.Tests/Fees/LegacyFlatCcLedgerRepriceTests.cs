using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.Fees;

/// <summary>
/// PROOF TESTS — does a reprice mint phantom money on a legacy-settled team?
///
/// Model ruling (2026-08-11): Registration_Accounting.Payamt is money-in, nothing more. The
/// ledger has NEVER carried a per-row principal/proc split in either era — that split lives
/// only in the entity columns (fee_processing / owed_total / paid_total), maintained at write
/// time when it was actually known. Legacy club-lump allocation rows ("paid by cc: $1,300.00
/// of $5,291.00") book FLAT PRINCIPAL into Payamt; the current decode (PaymentState
/// .CcPrincipalPaid = payamt ÷ (1+ccRate)) reads them as gross-with-proc and invents ~3.4%
/// of "proc collected" that never existed.
///
/// The display half of this bug was fixed in d29a79fb (grid Due columns re-anchored on
/// stored totals). These tests probe the RECOMPUTE half: the reprice paths — LADT
/// agegroup/pool move, fee edit, phase change — run FeeResolutionService
/// .ApplyTeamSwapFeesAsync → StampTeamProcessingAndTotals, which writes FeeProcessing from
/// PaymentState.FeeProcessingTarget (consumes the same decode) and then RecalcTotals()
/// → REAL owed_total. If the decode is wrong there, a settled team develops real,
/// chargeable owed from a no-op move.
///
/// Fee shape mirrors the affected prod population (Top Threat, e.g. ULTIMATE CC 2031
/// PREMIER): proc fees ON, NOT on deposit; Deposit $500 + BalanceDue $1,300 = $1,800 full.
/// Legacy-settled ledger: $500 correction (older) + $1,300 flat-principal CC row; entity
/// fee_processing 0 / fee_total 1,800 / paid 1,800 / owed 0.
///
/// Predicted failure (both flaw tests, worked from the real code):
///   hydration: correction fills the $500 proc-free slice → CC row decodes 1300/1.035
///   = 1256.04 principal, 43.96 invented ProcCollected;
///   stamp: target = 43.96 + (1300 − 1256.04) × 0.035 = 45.50 → FeeProcessing 45.50,
///   OwedTotal 45.50 on a team that owes nothing.
///
/// READ THE RESULTS AS:
///   • AgSwap / PhaseFlip tests RED (owed 45.50)  → flaw CONFIRMED; build the recompute fix;
///     prod moratorium on moves/fee-edits/phase-changes for the affected jobs stands.
///   • All GREEN → flaw disproven; moratorium lifts.
/// The two green-by-design tests pin WHY it hides: new-engine rows decode cleanly
/// (Ann's testing), and the deposit slice walk protects deposit-leg dollars — only the
/// balance leg is exposed.
///
/// NOTE: the flaw tests assert CORRECT behavior, so they stay red until the recompute fix
/// lands — do not commit to master while red.
/// </summary>
public class LegacyFlatCcLedgerRepriceTests
{
    private const decimal Deposit = 500m;
    private const decimal BalanceDue = 1300m;
    private const decimal FullPrice = Deposit + BalanceDue;          // $1,800
    private const decimal NewEngineProc = 45.50m;                    // 1300 × 3.5%
    private const decimal NewEngineGross = FullPrice + NewEngineProc; // $1,845.50 single CC charge

    [Fact(DisplayName = "PROOF: agegroup move of a legacy-settled team (same fee) must not mint owed")]
    public async Task AgegroupMove_LegacySettledTeam_SameFee_MintsNothing()
    {
        // The LADT move: PoolAssignmentService → ApplyTeamSwapFeesAsync(target agegroup).
        // Source and target agegroups carry IDENTICAL fees, so the correct outcome is a
        // byte-identical no-op on the money columns.
        var f = await ArrangeLegacySettledAsync(pifStamp: true, addSecondAgegroup: true);

        await f.Svc.ApplyTeamSwapFeesAsync(f.Team, f.JobId, f.TargetAgegroupId, ProcOnBalanceOnly());

        f.Team.FeeBase.Should().Be(FullPrice, "same fee at the target scope — base must not move");
        f.Team.FeeProcessing.Should().Be(0m,
            "the entity says no proc was ever charged (fee_processing 0); the stamp must not invent " +
            "ProcCollected by decoding a flat-principal legacy CC row as gross (predicted flaw value: 45.50)");
        f.Team.FeeTotal.Should().Be(FullPrice);
        f.Team.OwedTotal.Should().Be(0m,
            "a settled team repriced to the SAME fee must stay settled — anything else is minted money " +
            "that the charge engine would then really collect (predicted flaw value: 45.50)");
    }

    [Fact(DisplayName = "PROOF: phase re-stamp of a legacy-settled team must not mint owed")]
    public async Task PhaseRestamp_LegacySettledTeam_MintsNothing()
    {
        // The phase-change trigger: the scope carries a PIF stamp (director set it in LADT;
        // the save engine then re-runs the swap applier over every team in scope — same
        // agegroup, no move). The team already paid the full $1,800, so the re-stamp must
        // change nothing.
        var f = await ArrangeLegacySettledAsync(pifStamp: true, addSecondAgegroup: false);

        await f.Svc.ApplyTeamSwapFeesAsync(f.Team, f.JobId, f.SourceAgegroupId, ProcOnBalanceOnly());

        f.Team.FeeBase.Should().Be(FullPrice, "already at full price — the stamp is a no-op on base");
        f.Team.FeeProcessing.Should().Be(0m,
            "re-stamping the phase must not invent proc from the ledger decode (predicted flaw value: 45.50)");
        f.Team.OwedTotal.Should().Be(0m,
            "fully paid before the re-stamp, fully paid after (predicted flaw value: 45.50)");
    }

    [Fact(DisplayName = "CONTROL: new-engine-written ledger reprices clean (why routine testing never sees this)")]
    public async Task AgegroupMove_NewEngineSettledTeam_SameFee_StaysSettled()
    {
        // Same job, same fee shape — but the ledger row is the one the CURRENT charge engine
        // writes: one CC row at gross $1,845.50 (1800 principal + 45.50 proc on the balance
        // leg), entity fee_processing 45.50. The decode is self-consistent by construction
        // (the engine computed the gross with the same math that now inverts it), so the
        // reprice is a genuine no-op. This is Ann's data shape — green here + red above is
        // the whole explanation for "reprice tests great in QA".
        var f = await ArrangeAsync(
            teamFeeProcessing: NewEngineProc,
            teamPaidTotal: NewEngineGross,
            pifStamp: true,
            addSecondAgegroup: true,
            ledger: (b, teamId) => b.AddPayment(
                registrationId: null, teamId: teamId, amount: NewEngineGross,
                paymentMethodId: AccountingDataBuilder.CcPaymentMethodId));

        await f.Svc.ApplyTeamSwapFeesAsync(f.Team, f.JobId, f.TargetAgegroupId, ProcOnBalanceOnly());

        f.Team.FeeBase.Should().Be(FullPrice);
        f.Team.FeeProcessing.Should().Be(NewEngineProc, "grossed row decodes back to exactly the booked proc");
        f.Team.FeeTotal.Should().Be(NewEngineGross);
        f.Team.OwedTotal.Should().Be(0m, "self-consistent data reprices to a no-op");
    }

    [Fact(DisplayName = "CONTROL: deposit-leg legacy CC dollars are slice-protected — flip to full owes exactly the balance")]
    public async Task PhaseFlip_LegacyDepositPaidTeam_OwesExactlyTheBalance()
    {
        // Legacy team that paid ONLY its $500 deposit as a flat CC row, still deposit-stamped
        // (fee_base 500, proc 0 — the job never charges proc on deposits). Director flips the
        // scope to full payment. Correct: base 1800, proc 45.50 (on the not-yet-paid balance),
        // owed 1,345.50. This passes TODAY because the slice walk attributes the $500 CC row
        // to the proc-free deposit slice (decoded at face value) — pinning that the flaw is
        // confined to the BALANCE leg, where no slice protects flat rows.
        var f = await ArrangeAsync(
            teamFeeBase: Deposit,
            teamPaidTotal: Deposit,
            pifStamp: true,
            addSecondAgegroup: false,
            ledger: (b, teamId) => b.AddPayment(
                registrationId: null, teamId: teamId, amount: Deposit,
                paymentMethodId: AccountingDataBuilder.CcPaymentMethodId));

        await f.Svc.ApplyTeamSwapFeesAsync(f.Team, f.JobId, f.SourceAgegroupId, ProcOnBalanceOnly());

        f.Team.FeeBase.Should().Be(FullPrice, "PIF stamp re-prices the deposit-phase team to full");
        f.Team.FeeProcessing.Should().Be(NewEngineProc, "proc rides only the unpaid balance leg");
        f.Team.OwedTotal.Should().Be(BalanceDue + NewEngineProc, "1800 + 45.50 − 500 already paid");
    }

    [Fact(DisplayName = "CONTROL: deposit-less fee (BalanceDue only) — settled team reprices clean")]
    public async Task PhaseRestamp_DepositLessSettledTeam_StaysSettled()
    {
        // Population-sweep catch (2026-08-11): Deposit NULL → EffectiveDeposit falls back to
        // BalanceDue. The proc-free slice must be built on the RAW deposit — none here — not
        // the fallback: a fallback-sized slice covers the whole bill, drops the paid
        // principal out of the billable base, and re-projects full proc on top of collected
        // ($59.50 minted on each of 54 settled Big Dawgs College Club teams).
        var f = await ArrangeAsync(
            feeDeposit: null, feeBalance: 1700m,
            teamFeeBase: 1700m, teamFeeProcessing: 59.50m, teamPaidTotal: 1759.50m,
            pifStamp: true, addSecondAgegroup: false,
            ledger: (b, teamId) => b.AddPayment(
                registrationId: null, teamId: teamId, amount: 1759.50m,
                paymentMethodId: AccountingDataBuilder.CcPaymentMethodId));

        await f.Svc.ApplyTeamSwapFeesAsync(f.Team, f.JobId, f.SourceAgegroupId, ProcOnBalanceOnly());

        f.Team.FeeBase.Should().Be(1700m);
        f.Team.FeeProcessing.Should().Be(59.50m, "collected proc reproduced — no re-projection on paid principal");
        f.Team.OwedTotal.Should().Be(0m, "settled stays settled on a same-fee re-stamp");
    }

    // ── Harness ─────────────────────────────────────────────────────
    //
    // Real FeeResolutionService + real PaymentStateService over real ledger rows — the exact
    // production path (only IJobRepository is mocked, returning the job's own settings).
    // Mirrors TeamPaidPastDepositPromotionTests.ArrangeAsync with processing ON.

    private sealed record Fixture(
        FeeResolutionService Svc,
        Domain.Entities.Teams Team,
        Guid JobId,
        Guid SourceAgegroupId,
        Guid TargetAgegroupId);

    private static TeamFeeApplicationContext ProcOnBalanceOnly() => new()
    {
        AddProcessingFees = true,
        ApplyProcessingFeesToDeposit = false,
        ProcessingFeePercent = 0.035m,
    };

    /// <summary>The legacy-settled shape: $500 correction (older) + $1,300 flat-principal CC row.</summary>
    private static Task<Fixture> ArrangeLegacySettledAsync(bool pifStamp, bool addSecondAgegroup) =>
        ArrangeAsync(
            teamPaidTotal: FullPrice,
            pifStamp: pifStamp,
            addSecondAgegroup: addSecondAgegroup,
            ledger: (b, teamId) =>
            {
                // Billing order is deposit-first and the slice walk is oldest-first: the
                // correction ("credit from dropped team" era) predates the club-lump CC
                // allocation, so it consumes the proc-free deposit slice and the flat CC
                // row lands entirely on the balance leg — the prod shape.
                var corr = b.AddPayment(
                    registrationId: null, teamId: teamId, amount: Deposit,
                    paymentMethodId: AccountingDataBuilder.CorrectionMethodId);
                corr.Createdate = new DateTime(2026, 5, 1);

                var cc = b.AddPayment(
                    registrationId: null, teamId: teamId, amount: BalanceDue,
                    paymentMethodId: AccountingDataBuilder.CcPaymentMethodId);
                cc.Createdate = new DateTime(2026, 6, 1);
            });

    private static async Task<Fixture> ArrangeAsync(
        decimal teamFeeBase = FullPrice,
        decimal teamFeeProcessing = 0m,
        decimal teamPaidTotal = 0m,
        bool pifStamp = true,
        bool addSecondAgegroup = true,
        decimal? feeDeposit = Deposit,
        decimal feeBalance = BalanceDue,
        Action<AccountingDataBuilder, Guid>? ledger = null)
    {
        var ctx = DbContextFactory.Create();
        var acct = new AccountingDataBuilder(ctx);   // ctor seeds the standard payment methods
        var fees = new FeeDataBuilder(ctx);

        var job = acct.AddJob(
            processingFeePercent: 3.5m,
            bAddProcessingFees: true,
            bApplyProcessingFeesToTeamDeposit: false);
        var league = acct.AddLeague(job.JobId);
        var agA = acct.AddAgegroup(league.LeagueId, "2031 ELITE");
        var agB = addSecondAgegroup ? acct.AddAgegroup(league.LeagueId, "2031 PREMIER") : agA;

        var team = acct.AddTeam(
            job.JobId, agA.AgegroupId, teamName: "ULTIMATE CC 2031",
            feeBase: teamFeeBase, feeProcessing: teamFeeProcessing, paidTotal: teamPaidTotal);

        // Identical ag-scoped ClubRep fee on source (and target, when moving) — a same-fee
        // reprice, so any money delta the applier writes is minted, not repriced.
        fees.AddJobFee(job.JobId, RoleConstants.ClubRep, agegroupId: agA.AgegroupId,
            deposit: feeDeposit, balanceDue: feeBalance, bFullPaymentRequired: pifStamp ? true : null);
        if (addSecondAgegroup)
            fees.AddJobFee(job.JobId, RoleConstants.ClubRep, agegroupId: agB.AgegroupId,
                deposit: feeDeposit, balanceDue: feeBalance, bFullPaymentRequired: pifStamp ? true : null);

        ledger?.Invoke(acct, team.TeamId);

        await acct.SaveAsync();

        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetJobFeeSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings
            {
                BAddProcessingFees = true,
                BApplyProcessingFeesToTeamDeposit = false,
                PaymentMethodsAllowedCode = 1,
            });
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        jobRepo.Setup(j => j.GetEcprocessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1.5m);

        var paymentState = new PaymentStateService(
            new RegistrationAccountingRepository(ctx), jobRepo.Object, new FeeRepository(ctx), new TeamRepository(ctx));
        var svc = new FeeResolutionService(new FeeRepository(ctx), jobRepo.Object, paymentState);

        return new Fixture(svc, team, job.JobId, agA.AgegroupId, agB.AgegroupId);
    }
}
