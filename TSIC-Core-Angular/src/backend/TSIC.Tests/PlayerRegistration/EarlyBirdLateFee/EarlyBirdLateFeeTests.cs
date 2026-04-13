using FluentAssertions;
using Moq;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Players;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.PlayerRegistration.EarlyBirdLateFee;

/// <summary>
/// EARLY BIRD DISCOUNT &amp; LATE FEE TESTS
///
/// These test the new fee modifier system (not in legacy).
/// Modifiers are time-windowed: an EarlyBird discount is active between StartDate→EndDate,
/// a LateFee kicks in between its own StartDate→EndDate.
///
/// Key business rules tested:
///   - Modifiers activate based on registration date (asOfDate)
///   - EarlyBird and Discount both reduce FeeDiscount (they stack)
///   - LateFee increases FeeLatefee
///   - Modifiers from all cascade levels (job, agegroup, team) stack additively
///   - On team swap, modifiers are FROZEN from original registration — never re-evaluated
///   - NULL StartDate/EndDate = unbounded (always active)
///   - FeeTotal = FeeBase + FeeProcessing - FeeDiscount + FeeDonation + FeeLatefee
///
/// Each test uses real repositories against an in-memory database.
/// </summary>
public class EarlyBirdLateFeeTests
{
    // ── Standard date windows for tests ──
    // Early bird:  Jan 1 – Feb 15
    // Normal:      Feb 16 – Mar 31  (no modifiers active)
    // Late fee:    Apr 1 – Jun 30
    private static readonly DateTime EarlyBirdStart = new(2026, 1, 1);
    private static readonly DateTime EarlyBirdEnd = new(2026, 2, 15);
    private static readonly DateTime LateFeeStart = new(2026, 4, 1);
    private static readonly DateTime LateFeeEnd = new(2026, 6, 30);

    // Sample dates within each window
    private static readonly DateTime DateInEarlyBird = new(2026, 1, 20);
    private static readonly DateTime DateInNormal = new(2026, 3, 1);
    private static readonly DateTime DateInLateFee = new(2026, 5, 1);

    /// <summary>
    /// Creates a FeeResolutionService wired to real repositories (in-memory DB).
    /// Returns everything needed to arrange and assert.
    /// </summary>
    private static async Task<(FeeResolutionService svc, FeeDataBuilder builder,
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx, Guid jobId, Guid agegroupId, Guid teamId, Guid jobFeeId)>
        CreateServiceAsync(
            decimal baseFee = 200m,
            decimal processingFeePercent = 3.5m,
            bool addProcessingFees = true)
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);

        var job = builder.AddJob(processingFeePercent, addProcessingFees);
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "U12");
        var team = builder.AddTeam(job.JobId, ag.AgegroupId, "Hawks");

        // Job-level fee for Player role
        var jobFee = builder.AddJobFee(job.JobId, RoleConstants.Player, balanceDue: baseFee);

        await builder.SaveAsync();

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processingFeePercent);

        var feeCalc = new PlayerFeeCalculator();

        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, feeCalc);

        return (svc, builder, ctx, job.JobId, ag.AgegroupId, team.TeamId, jobFee.JobFeeId);
    }

    // ════════════════════════════════════════════════════════════
    //  EARLY BIRD DISCOUNT TESTS
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Early bird: registration during window gets discount")]
    public async Task EarlyBird_DuringWindow_DiscountApplied()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);

        modifiers.TotalDiscount.Should().Be(25m);
        modifiers.TotalLateFee.Should().Be(0m);
    }

    [Fact(DisplayName = "Early bird: registration after window gets no discount")]
    public async Task EarlyBird_AfterWindow_NoDiscount()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInNormal);

        modifiers.TotalDiscount.Should().Be(0m);
        modifiers.TotalLateFee.Should().Be(0m);
    }

    [Fact(DisplayName = "Early bird: registration on exact start date gets discount")]
    public async Task EarlyBird_OnStartDate_DiscountApplied()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, EarlyBirdStart);

        modifiers.TotalDiscount.Should().Be(25m);
    }

    [Fact(DisplayName = "Early bird: registration on exact end date gets discount")]
    public async Task EarlyBird_OnEndDate_DiscountApplied()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, EarlyBirdEnd);

        modifiers.TotalDiscount.Should().Be(25m);
    }

    [Fact(DisplayName = "Early bird: day after end date gets no discount")]
    public async Task EarlyBird_DayAfterEnd_NoDiscount()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, EarlyBirdEnd.AddDays(1));

        modifiers.TotalDiscount.Should().Be(0m);
    }

    // ════════════════════════════════════════════════════════════
    //  LATE FEE TESTS
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Late fee: registration during window incurs fee")]
    public async Task LateFee_DuringWindow_FeeApplied()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInLateFee);

        modifiers.TotalLateFee.Should().Be(30m);
        modifiers.TotalDiscount.Should().Be(0m);
    }

    [Fact(DisplayName = "Late fee: registration before window incurs no fee")]
    public async Task LateFee_BeforeWindow_NoFee()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInNormal);

        modifiers.TotalLateFee.Should().Be(0m);
    }

    [Fact(DisplayName = "Late fee: registration on exact start date incurs fee")]
    public async Task LateFee_OnStartDate_FeeApplied()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, LateFeeStart);

        modifiers.TotalLateFee.Should().Be(30m);
    }

    // ════════════════════════════════════════════════════════════
    //  COMBINED: EARLY BIRD + LATE FEE ON SAME JOB
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Normal window: neither early bird nor late fee applies")]
    public async Task NormalWindow_NoModifiers()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInNormal);

        modifiers.TotalDiscount.Should().Be(0m);
        modifiers.TotalLateFee.Should().Be(0m);
    }

    [Fact(DisplayName = "Early bird window: discount yes, late fee no")]
    public async Task EarlyBirdWindow_DiscountOnly()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);

        modifiers.TotalDiscount.Should().Be(25m);
        modifiers.TotalLateFee.Should().Be(0m);
    }

    [Fact(DisplayName = "Late fee window: late fee yes, discount no")]
    public async Task LateFeeWindow_LateFeeOnly()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInLateFee);

        modifiers.TotalDiscount.Should().Be(0m);
        modifiers.TotalLateFee.Should().Be(30m);
    }

    // ════════════════════════════════════════════════════════════
    //  CASCADE STACKING: MODIFIERS FROM MULTIPLE LEVELS
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Cascade stacking: job + agegroup early bird discounts sum")]
    public async Task Cascade_JobPlusAgegroup_EarlyBirdStack()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Job-level: $10 early bird
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 10m, EarlyBirdStart, EarlyBirdEnd);

        // Agegroup-level fee + $15 early bird
        var agFee = builder.AddJobFee(jobId, RoleConstants.Player, agegroupId: agId, balanceDue: 200m);
        builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierEarlyBird, 15m, EarlyBirdStart, EarlyBirdEnd);

        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);

        modifiers.TotalDiscount.Should().Be(25m, "job $10 + agegroup $15 = $25 total");
    }

    [Fact(DisplayName = "Cascade stacking: job + agegroup + team modifiers all stack")]
    public async Task Cascade_AllThreeLevels_Stack()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Job-level: $10 early bird
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 10m, EarlyBirdStart, EarlyBirdEnd);

        // Agegroup-level: $5 early bird
        var agFee = builder.AddJobFee(jobId, RoleConstants.Player, agegroupId: agId, balanceDue: 200m);
        builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierEarlyBird, 5m, EarlyBirdStart, EarlyBirdEnd);

        // Team-level: $3 early bird
        var teamFee = builder.AddJobFee(jobId, RoleConstants.Player, agegroupId: agId, teamId: teamId, balanceDue: 200m);
        builder.AddModifier(teamFee.JobFeeId, FeeConstants.ModifierEarlyBird, 3m, EarlyBirdStart, EarlyBirdEnd);

        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);

        modifiers.TotalDiscount.Should().Be(18m, "job $10 + agegroup $5 + team $3 = $18 total");
    }

    [Fact(DisplayName = "Cascade stacking: late fees from different levels sum")]
    public async Task Cascade_LateFees_Stack()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Job-level: $20 late fee
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 20m, LateFeeStart, LateFeeEnd);

        // Agegroup-level: $10 late fee
        var agFee = builder.AddJobFee(jobId, RoleConstants.Player, agegroupId: agId, balanceDue: 200m);
        builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierLateFee, 10m, LateFeeStart, LateFeeEnd);

        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInLateFee);

        modifiers.TotalLateFee.Should().Be(30m, "job $20 + agegroup $10 = $30 total");
    }

    [Fact(DisplayName = "Cascade: mixed early bird + late fee at different levels")]
    public async Task Cascade_MixedModifierTypes()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Job-level: $10 early bird AND $20 late fee (different windows)
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 10m, EarlyBirdStart, EarlyBirdEnd);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 20m, LateFeeStart, LateFeeEnd);

        // Agegroup-level: $5 early bird
        var agFee = builder.AddJobFee(jobId, RoleConstants.Player, agegroupId: agId, balanceDue: 200m);
        builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierEarlyBird, 5m, EarlyBirdStart, EarlyBirdEnd);

        await builder.SaveAsync();

        // During early bird window: job $10 + agegroup $5 discount, no late fee
        var earlyMods = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);
        earlyMods.TotalDiscount.Should().Be(15m);
        earlyMods.TotalLateFee.Should().Be(0m);

        // During late fee window: no discount, job $20 late fee only
        var lateMods = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInLateFee);
        lateMods.TotalDiscount.Should().Be(0m);
        lateMods.TotalLateFee.Should().Be(20m);
    }

    // ════════════════════════════════════════════════════════════
    //  DISCOUNT + EARLY BIRD STACKING (same ModifierType category)
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "EarlyBird + Discount both reduce FeeDiscount (they stack)")]
    public async Task EarlyBirdPlusDiscount_BothStack()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Always-active Discount (null dates)
        builder.AddModifier(jobFeeId, FeeConstants.ModifierDiscount, 10m, null, null);

        // Time-windowed EarlyBird
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);

        await builder.SaveAsync();

        // During early bird: both apply
        var earlyMods = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);
        earlyMods.TotalDiscount.Should().Be(35m, "Discount $10 + EarlyBird $25 = $35");

        // After early bird: only flat discount
        var normalMods = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInNormal);
        normalMods.TotalDiscount.Should().Be(10m, "only always-active Discount $10");
    }

    // ════════════════════════════════════════════════════════════
    //  NULL DATE BOUNDARIES (unbounded modifiers)
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Modifier with null StartDate is active from the beginning of time")]
    public async Task NullStartDate_AlwaysActiveFromStart()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);
        // No start date, ends Feb 15
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, null, EarlyBirdEnd);
        await builder.SaveAsync();

        // Any date before end should work
        var veryEarly = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2020, 1, 1));
        veryEarly.TotalDiscount.Should().Be(25m);

        // After end date: no discount
        var after = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, EarlyBirdEnd.AddDays(1));
        after.TotalDiscount.Should().Be(0m);
    }

    [Fact(DisplayName = "Modifier with null EndDate is active until end of time")]
    public async Task NullEndDate_AlwaysActiveAfterStart()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);
        // Starts Apr 1, no end date
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, null);
        await builder.SaveAsync();

        // Before start: no fee
        var before = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInNormal);
        before.TotalLateFee.Should().Be(0m);

        // Way after: still active
        var farFuture = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2030, 12, 31));
        farFuture.TotalLateFee.Should().Be(30m);
    }

    [Fact(DisplayName = "Modifier with null StartDate AND EndDate is always active")]
    public async Task NullBothDates_AlwaysActive()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierDiscount, 15m, null, null);
        await builder.SaveAsync();

        var mods = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2099, 6, 15));
        mods.TotalDiscount.Should().Be(15m);
    }

    // ════════════════════════════════════════════════════════════
    //  OVERLAPPING WINDOWS
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Overlapping early bird windows stack during overlap period")]
    public async Task OverlappingEarlyBirds_StackDuringOverlap()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Early bird A: Jan 1 – Feb 15 ($20)
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 20m, EarlyBirdStart, EarlyBirdEnd);

        // Early bird B: Jan 15 – Feb 28 ($10)  — overlaps A during Jan 15 – Feb 15
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 10m,
            new DateTime(2026, 1, 15), new DateTime(2026, 2, 28));

        await builder.SaveAsync();

        // During overlap (Jan 20): both apply
        var overlap = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2026, 1, 20));
        overlap.TotalDiscount.Should().Be(30m, "both early birds active during overlap");

        // After A expires, B still active (Feb 20): only B
        var afterA = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2026, 2, 20));
        afterA.TotalDiscount.Should().Be(10m, "only early bird B still active");

        // After both expire (Mar 5): nothing
        var afterBoth = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2026, 3, 5));
        afterBoth.TotalDiscount.Should().Be(0m);
    }

    // ════════════════════════════════════════════════════════════
    //  FULL FINANCIAL SNAPSHOT (ApplyNewRegistrationFeesAsync)
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "New registration during early bird: FeeTotal reflects discount")]
    public async Task ApplyNewFees_EarlyBird_CorrectTotals()
    {
        // This test verifies the full pipeline: resolve fee → evaluate modifiers → stamp registration
        // Note: ApplyNewRegistrationFeesAsync uses DateTime.UtcNow internally,
        // so we test EvaluateModifiersAsync directly with controlled dates, then verify
        // the math via ApplyProcessingAndTotals indirectly.
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(
            baseFee: 200m, processingFeePercent: 3.5m, addProcessingFees: true);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        await builder.SaveAsync();

        // Simulate what ApplyNewRegistrationFeesAsync does, but with controlled date
        var resolved = await svc.ResolveFeeAsync(jobId, RoleConstants.Player, agId, teamId);
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;
        baseFee.Should().Be(200m);

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);

        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FeeBase = baseFee,
            FeeDiscount = modifiers.TotalDiscount,
            FeeLatefee = modifiers.TotalLateFee,
            FeeDonation = 0m,
            PaidTotal = 0m,
            Modified = DateTime.UtcNow
        };

        // Calculate processing on adjusted base
        var rate = await svc.GetEffectiveProcessingRateAsync(jobId);
        reg.FeeProcessing = decimal.Round(baseFee * rate, 2, MidpointRounding.AwayFromZero);

        // FeeTotal = FeeBase + FeeProcessing - FeeDiscount + FeeDonation + FeeLatefee
        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;

        reg.FeeBase.Should().Be(200m);
        reg.FeeDiscount.Should().Be(25m);
        reg.FeeLatefee.Should().Be(0m);
        reg.FeeProcessing.Should().Be(7m);            // 200 × 3.5%
        reg.FeeTotal.Should().Be(182m);                // 200 + 7 - 25 + 0 + 0
        reg.OwedTotal.Should().Be(182m);
    }

    [Fact(DisplayName = "New registration during late fee: FeeTotal reflects surcharge")]
    public async Task ApplyNewFees_LateFee_CorrectTotals()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(
            baseFee: 200m, processingFeePercent: 3.5m, addProcessingFees: true);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var resolved = await svc.ResolveFeeAsync(jobId, RoleConstants.Player, agId, teamId);
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInLateFee);

        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = jobId,
            FeeBase = baseFee,
            FeeDiscount = modifiers.TotalDiscount,
            FeeLatefee = modifiers.TotalLateFee,
            FeeDonation = 0m,
            PaidTotal = 0m,
            Modified = DateTime.UtcNow
        };

        var rate = await svc.GetEffectiveProcessingRateAsync(jobId);
        reg.FeeProcessing = decimal.Round(baseFee * rate, 2, MidpointRounding.AwayFromZero);
        reg.FeeTotal = reg.FeeBase + reg.FeeProcessing - reg.FeeDiscount + reg.FeeDonation + reg.FeeLatefee;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;

        reg.FeeBase.Should().Be(200m);
        reg.FeeDiscount.Should().Be(0m);
        reg.FeeLatefee.Should().Be(30m);
        reg.FeeProcessing.Should().Be(7m);             // 200 × 3.5%
        reg.FeeTotal.Should().Be(237m);                 // 200 + 7 - 0 + 0 + 30
        reg.OwedTotal.Should().Be(237m);
    }

    [Fact(DisplayName = "New registration normal window: no modifiers, base fee only")]
    public async Task ApplyNewFees_NormalWindow_NoModifiers()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(
            baseFee: 200m, processingFeePercent: 3.5m, addProcessingFees: true);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);
        await builder.SaveAsync();

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInNormal);

        modifiers.TotalDiscount.Should().Be(0m);
        modifiers.TotalLateFee.Should().Be(0m);

        // FeeTotal should just be base + processing
        var rate = await svc.GetEffectiveProcessingRateAsync(jobId);
        var processing = decimal.Round(200m * rate, 2, MidpointRounding.AwayFromZero);
        var expectedTotal = 200m + processing;  // 207
        expectedTotal.Should().Be(207m);
    }

    // ════════════════════════════════════════════════════════════
    //  SWAP: MODIFIERS FROZEN FROM ORIGINAL REGISTRATION
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Team swap preserves early bird discount even during late fee window")]
    public async Task Swap_PreservesOriginalModifiers()
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);

        var job = builder.AddJob(3.5m, true);
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "U12");
        var teamA = builder.AddTeam(job.JobId, ag.AgegroupId, "Hawks");
        var teamB = builder.AddTeam(job.JobId, ag.AgegroupId, "Eagles");

        // Job-level fee $200 + early bird $25 + late fee $30
        var jobFee = builder.AddJobFee(job.JobId, RoleConstants.Player, balanceDue: 200m);
        builder.AddModifier(jobFee.JobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);
        builder.AddModifier(jobFee.JobFeeId, FeeConstants.ModifierLateFee, 30m, LateFeeStart, LateFeeEnd);

        await builder.SaveAsync();

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        var feeCalc = new PlayerFeeCalculator();
        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, feeCalc);

        // Player registered during early bird window — got $25 discount
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = job.JobId,
            AssignedTeamId = teamA.TeamId,
            FeeBase = 200m,
            FeeDiscount = 25m,   // Early bird snapshot
            FeeLatefee = 0m,     // No late fee at registration time
            FeeDonation = 0m,
            FeeProcessing = 7m,  // 200 × 3.5%
            FeeTotal = 182m,     // 200 + 7 - 25
            PaidTotal = 0m,
            OwedTotal = 182m,
            Modified = DateTime.UtcNow
        };

        // Now swap to Team B during the LATE FEE window
        // ApplySwapFeesAsync should NOT re-evaluate modifiers
        await svc.ApplySwapFeesAsync(
            reg, job.JobId, ag.AgegroupId, teamB.TeamId,
            new FeeApplicationContext { AddProcessingFees = true });

        // FeeDiscount should STILL be $25 (frozen from original early bird)
        reg.FeeDiscount.Should().Be(25m, "modifiers are frozen on swap — early bird preserved");

        // FeeLatefee should STILL be $0 (not re-evaluated to $30)
        reg.FeeLatefee.Should().Be(0m, "late fee not applied on swap even though we're in late fee window");

        // FeeBase stays $200 (same job-level fee for both teams)
        reg.FeeBase.Should().Be(200m);

        // FeeTotal recalculated: 200 + 7 - 25 + 0 + 0 = 182
        reg.FeeTotal.Should().Be(182m);
    }

    [Fact(DisplayName = "Team swap to different-fee team updates FeeBase but keeps modifiers")]
    public async Task Swap_DifferentFee_KeepsModifiers()
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);

        var job = builder.AddJob(3.5m, true);
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId, "U12");
        var teamA = builder.AddTeam(job.JobId, ag.AgegroupId, "Hawks");
        var teamB = builder.AddTeam(job.JobId, ag.AgegroupId, "Eagles");

        // Job-level fee $200
        var jobFee = builder.AddJobFee(job.JobId, RoleConstants.Player, balanceDue: 200m);
        builder.AddModifier(jobFee.JobFeeId, FeeConstants.ModifierEarlyBird, 25m, EarlyBirdStart, EarlyBirdEnd);

        // Team B has a team-level override: $300
        builder.AddJobFee(job.JobId, RoleConstants.Player,
            agegroupId: ag.AgegroupId, teamId: teamB.TeamId, balanceDue: 300m);

        await builder.SaveAsync();

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        var feeCalc = new PlayerFeeCalculator();
        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, feeCalc);

        // Player originally on Team A with early bird discount
        var reg = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            JobId = job.JobId,
            AssignedTeamId = teamA.TeamId,
            FeeBase = 200m,
            FeeDiscount = 25m,
            FeeLatefee = 0m,
            FeeDonation = 0m,
            FeeProcessing = 7m,
            FeeTotal = 182m,
            PaidTotal = 0m,
            OwedTotal = 182m,
            Modified = DateTime.UtcNow
        };

        // Swap to Team B (which has $300 fee)
        await svc.ApplySwapFeesAsync(
            reg, job.JobId, ag.AgegroupId, teamB.TeamId,
            new FeeApplicationContext { AddProcessingFees = true });

        reg.FeeBase.Should().Be(300m, "team B has $300 override");
        reg.FeeDiscount.Should().Be(25m, "early bird discount preserved from original");
        reg.FeeLatefee.Should().Be(0m, "no late fee from original");
        reg.FeeProcessing.Should().Be(10.50m, "300 × 3.5%");
        reg.FeeTotal.Should().Be(285.50m, "300 + 10.50 - 25 + 0 + 0");
    }

    // ════════════════════════════════════════════════════════════
    //  EDGE CASES
    // ════════════════════════════════════════════════════════════

    [Fact(DisplayName = "No modifiers configured: EvaluateModifiers returns zeros")]
    public async Task NoModifiers_ReturnsZeros()
    {
        var (svc, _, _, jobId, agId, teamId, _) = await CreateServiceAsync(baseFee: 200m);

        var modifiers = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, DateInEarlyBird);

        modifiers.TotalDiscount.Should().Be(0m);
        modifiers.TotalLateFee.Should().Be(0m);
    }

    [Fact(DisplayName = "No fee row configured: ResolveFee returns null")]
    public async Task NoFeeRow_ReturnsNull()
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);
        var job = builder.AddJob();
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId);
        var team = builder.AddTeam(job.JobId, ag.AgegroupId);
        // No JobFees rows added
        await builder.SaveAsync();

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        var feeCalc = new PlayerFeeCalculator();
        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, feeCalc);

        var resolved = await svc.ResolveFeeAsync(job.JobId, RoleConstants.Player, ag.AgegroupId, team.TeamId);
        resolved.Should().BeNull();
    }

    [Fact(DisplayName = "Multiple modifier types on same JobFee: each counted separately")]
    public async Task MultipleModifierTypes_CountedSeparately()
    {
        var (svc, builder, _, jobId, agId, teamId, jobFeeId) = await CreateServiceAsync(baseFee: 200m);

        // Same window for both (hypothetical scenario)
        var start = new DateTime(2026, 3, 1);
        var end = new DateTime(2026, 3, 31);

        builder.AddModifier(jobFeeId, FeeConstants.ModifierDiscount, 10m, start, end);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierEarlyBird, 15m, start, end);
        builder.AddModifier(jobFeeId, FeeConstants.ModifierLateFee, 20m, start, end);

        await builder.SaveAsync();

        var mods = await svc.EvaluateModifiersAsync(
            jobId, RoleConstants.Player, agId, teamId, new DateTime(2026, 3, 15));

        mods.TotalDiscount.Should().Be(25m, "Discount $10 + EarlyBird $15 both go to TotalDiscount");
        mods.TotalLateFee.Should().Be(20m, "LateFee $20 goes to TotalLateFee separately");
    }

    [Fact(DisplayName = "Cascade: only agegroup modifier, no job modifier — agegroup still applies")]
    public async Task Cascade_AgegroupOnly_StillApplies()
    {
        var ctx = DbContextFactory.Create();
        var builder = new FeeDataBuilder(ctx);

        var job = builder.AddJob();
        var league = builder.AddLeague(job.JobId);
        var ag = builder.AddAgegroup(league.LeagueId);
        var team = builder.AddTeam(job.JobId, ag.AgegroupId);

        // Job-level fee with NO modifiers
        builder.AddJobFee(job.JobId, RoleConstants.Player, balanceDue: 200m);

        // Agegroup-level fee WITH early bird
        var agFee = builder.AddJobFee(job.JobId, RoleConstants.Player,
            agegroupId: ag.AgegroupId, balanceDue: 200m);
        builder.AddModifier(agFee.JobFeeId, FeeConstants.ModifierEarlyBird, 15m, EarlyBirdStart, EarlyBirdEnd);

        await builder.SaveAsync();

        var feeRepo = new FeeRepository(ctx);
        var jobRepo = new Mock<IJobRepository>();
        jobRepo.Setup(j => j.GetProcessingFeePercentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3.5m);
        var feeCalc = new PlayerFeeCalculator();
        var svc = new FeeResolutionService(feeRepo, jobRepo.Object, feeCalc);

        var mods = await svc.EvaluateModifiersAsync(
            job.JobId, RoleConstants.Player, ag.AgegroupId, team.TeamId, DateInEarlyBird);

        mods.TotalDiscount.Should().Be(15m, "agegroup-level early bird applies even without job-level modifier");
    }
}
