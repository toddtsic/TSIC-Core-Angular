using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.JobClone;

/// <summary>
/// Behavior tests for the copy-everything JobCloneService against the InMemory EF provider.
///
/// Coverage:
///   - Safe-state reset list (all five BRegistrationAllow* forced off, exposure flags off)
///   - CC + eCheck processing-fee floor (max(source, new-job rate) — no choices)
///   - eCheck enable + Store choices applied to new Job
///   - LadtScope: none / lad / ladt produce expected entity sets
///   - Team eligibility: structure-vs-competing (owner club_name), waitlist/dropped
///     buckets, inactive teams
///   - Cloned teams: ClubRep refs nulled, financials zeroed, lineage flagged
///   - Fee remap incl. multi-league; BFullPaymentRequired reset to null (tri-state
///     silence — league card governs); phase-only rows not cloned
///   - Preview/clone parity (same planner, identical per-step counts)
///   - Data-moved guard (fingerprint mismatch → ClonePlanChangedException)
///   - Dev-undo manifest-reversed cascade delete
///
/// **NOT covered** (InMemory limitation, accepted gap): transactional rollback, FK and
/// unique-constraint enforcement, the D2 raw-SQL ancillary walk (relational-only; skipped
/// on InMemory). Verify those against real SQL Server.
/// </summary>
public class JobCloneServiceTests
{
    private const string SuperUserId = "test-superuser";

    // ══════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════

    private static (JobCloneService svc, SqlDbContext ctx) BuildService()
    {
        var ctx = DbContextFactory.Create();
        var repo = new JobCloneRepository(ctx);
        var feeRepo = new FeeRepository(ctx);
        var planner = new JobClonePlanner(repo, feeRepo);
        var svc = new JobCloneService(repo, feeRepo, planner, NullLogger<JobCloneService>.Instance);
        return (svc, ctx);
    }

    private static int StepCount(JobCloneResponse resp, string stepKey) =>
        resp.Steps.Single(s => s.StepKey == stepKey).Count;

    private static int StepCount(ClonePlanDto plan, string stepKey) =>
        plan.Steps.Single(s => s.StepKey == stepKey).Count;

    /// <summary>
    /// Seeds a source Job + League + JobLeague + Agegroup + Division. Returns IDs for
    /// follow-up assertions / team seeding.
    /// </summary>
    /// <summary>Fixed so BaseRequest can target the same customer the source job belongs to.</summary>
    private static readonly Guid TestCustomerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task<(Guid jobId, Guid leagueId, Guid agegroupId, Guid divId)>
        SeedSourceJobAsync(
            SqlDbContext ctx,
            decimal? processingFeePercent = 3.75m,
            decimal? ecprocessingFeePercent = 1.75m,
            bool bEnableEcheck = true,
            bool bEnableStore = true,
            string season = "Spring",
            string year = "2025",
            string agegroupName = "Boys U10")
    {
        var jobId = Guid.NewGuid();
        var customerId = TestCustomerId;
        var leagueId = Guid.NewGuid();
        var agegroupId = Guid.NewGuid();
        var divId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        ctx.Jobs.Add(new Jobs
        {
            JobId = jobId,
            JobPath = $"src-{Guid.NewGuid():N}"[..16],
            JobName = $"Source {Guid.NewGuid():N}"[..20],
            JobDescription = "Source Job",
            Year = year,
            Season = season,
            DisplayName = "Source",
            CustomerId = customerId,
            BillingTypeId = 1,
            JobTypeId = 1,
            SportId = sportId,
            ProcessingFeePercent = processingFeePercent,
            EcprocessingFeePercent = ecprocessingFeePercent,
            BEnableEcheck = bEnableEcheck,
            BEnableStore = bEnableStore,
            BSuspendPublic = false,
            ExpiryAdmin = now.AddYears(1),
            ExpiryUsers = now.AddYears(1),
            RegformNamePlayer = "Player",
            RegformNameTeam = "Team",
            RegformNameCoach = "Coach",
            RegformNameClubRep = "Club Rep",
            Modified = now,
        });

        ctx.Leagues.Add(new Leagues
        {
            LeagueId = leagueId,
            LeagueName = "Source League",
            SportId = sportId,
            Modified = now,
        });

        ctx.JobLeagues.Add(new JobLeagues
        {
            JobLeagueId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = leagueId,
            BIsPrimary = true,
            Modified = now,
        });

        ctx.Agegroups.Add(new Agegroups
        {
            AgegroupId = agegroupId,
            LeagueId = leagueId,
            AgegroupName = agegroupName,
            Season = season,
            Modified = now,
        });

        ctx.Divisions.Add(new Divisions
        {
            DivId = divId,
            AgegroupId = agegroupId,
            DivName = "A",
            Modified = now,
        });

        await ctx.SaveChangesAsync();
        return (jobId, leagueId, agegroupId, divId);
    }

    private static Teams SeedTeam(
        SqlDbContext ctx, Guid jobId, Guid leagueId, Guid agegroupId, Guid? divId,
        string name, Guid? clubRepRegistrationId = null,
        decimal feeBase = 100m, decimal paidTotal = 50m, bool active = true)
    {
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = leagueId,
            AgegroupId = agegroupId,
            DivId = divId,
            TeamName = name,
            Year = "2025",
            Season = "Spring",
            Active = active,
            ClubrepRegistrationid = clubRepRegistrationId,
            ClubrepId = clubRepRegistrationId.HasValue ? "rep-id" : null,
            FeeBase = feeBase,
            FeeProcessing = 5m,
            FeeTotal = feeBase + 5m,
            PaidTotal = paidTotal,
            OwedTotal = feeBase + 5m - paidTotal,
            Wins = 7, Losses = 3, Points = 21,
            AdnSubscriptionStatus = "active",
            ViPolicyId = "POL123",
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        };
        ctx.Teams.Add(team);
        return team;
    }

    /// <summary>Seeds a ClubRep registration owning teams; clubName drives eligibility.</summary>
    private static Guid SeedClubRep(SqlDbContext ctx, Guid jobId, string? clubName)
    {
        var regId = Guid.NewGuid();
        ctx.Registrations.Add(new Registrations
        {
            RegistrationId = regId,
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            UserId = $"rep-{Guid.NewGuid():N}"[..12],
            ClubName = clubName,
            BActive = true,
            RegistrationTs = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        });
        return regId;
    }

    private static JobCloneRequest BaseRequest(
        Guid sourceJobId,
        Guid? renameLeagueId = null,
        string ladtScope = "lad",
        string enableEcheckChoice = "off",
        string storeChoice = "disable",
        bool advance = false,
        string yearTarget = "2026",
        string? planFingerprint = null)
    {
        return new JobCloneRequest
        {
            SourceJobId = sourceJobId,
            // Same customer as the seeded source — every existing case is a same-customer clone.
            TargetCustomerId = TestCustomerId,
            JobPathTarget = $"new-{Guid.NewGuid():N}"[..16],
            JobNameTarget = $"New {Guid.NewGuid():N}"[..16],
            YearTarget = yearTarget,
            SeasonTarget = "Spring",
            DisplayName = "New",
            Leagues = renameLeagueId.HasValue
                ? [new LeagueRenameDto { SourceLeagueId = renameLeagueId.Value, NameTarget = "New League" }]
                : [],
            ExpiryAdmin = DateTime.UtcNow.AddYears(1),
            ExpiryUsers = DateTime.UtcNow.AddYears(1),
            // Workbench seeds this from the source; the seeded source is CC-only.
            PaymentMethodsAllowedCode = PaymentMethodConstants.CreditCardOnly,
            UpAgegroupNamesByOne = advance,
            LadtScope = ladtScope,
            EnableEcheckChoice = enableEcheckChoice,
            StoreChoice = storeChoice,
            PlanFingerprint = planFingerprint,
        };
    }

    // ══════════════════════════════════════════════════════════
    // Validation
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Validation_InvalidLadtScope_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var act = () => svc.CloneJobAsync(BaseRequest(jobId, leagueId, ladtScope: "all"), SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*LadtScope*");
    }

    [Fact]
    public async Task Validation_InvalidJobPathSlug_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var req = BaseRequest(jobId, leagueId) with { JobPathTarget = "Bad Path!" };
        var act = () => svc.CloneJobAsync(req, SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*URL segment*");
    }

    [Fact]
    public async Task Validation_DuplicateJobName_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);
        var sourceName = (await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == jobId)).JobName!;

        var req = BaseRequest(jobId, leagueId) with { JobNameTarget = sourceName };
        var act = () => svc.CloneJobAsync(req, SuperUserId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Fact]
    public async Task Validation_MissingLeagueRename_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        // lad scope but NO rename row for the source league.
        var act = () => svc.CloneJobAsync(BaseRequest(jobId, renameLeagueId: null), SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*target name*");
    }

    // ══════════════════════════════════════════════════════════
    // Processing fee floors (T1 — no choices; max(source, new-job rate))
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task ProcessingFee_SourceAboveFloor_CarriedForward()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, processingFeePercent: 3.9m);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.ProcessingFeePercent.Should().Be(3.9m);
    }

    [Fact]
    public async Task ProcessingFee_SourceBelowFloor_RaisedToNewJobRate()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, processingFeePercent: 3.5m);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.ProcessingFeePercent.Should().Be(3.8m); // FeeConstants.NewJobProcessingFeePercent
    }

    [Fact]
    public async Task ProcessingFee_SourceNull_UsesNewJobRate()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, processingFeePercent: null);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.ProcessingFeePercent.Should().Be(3.8m);
    }

    [Fact]
    public async Task EcheckProcessingFee_SourceAboveFloor_CarriedForward()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, ecprocessingFeePercent: 1.85m);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.EcprocessingFeePercent.Should().Be(1.85m);
    }

    [Fact]
    public async Task EcheckProcessingFee_SourceNull_UsesNewJobRate()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, ecprocessingFeePercent: null);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.EcprocessingFeePercent.Should().Be(1.5m); // FeeConstants.NewJobEcprocessingFeePercent
    }

    // ══════════════════════════════════════════════════════════
    // EnableEcheck + Store choices
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task EnableEcheckChoice_Off_DisablesOnNewJob_RegardlessOfSource()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, bEnableEcheck: true);

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, enableEcheckChoice: "off"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableEcheck.Should().BeFalse();
    }

    [Fact]
    public async Task EnableEcheckChoice_Source_CopiesSourceFlag()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, bEnableEcheck: true);

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, enableEcheckChoice: "source"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableEcheck.Should().BeTrue();
    }

    [Fact]
    public async Task StoreChoice_Disable_DisablesStoreAndWalkup_RegardlessOfSource()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, bEnableStore: true);
        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BAllowStoreWalkup = true;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, storeChoice: "disable"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableStore.Should().BeFalse();
        newJob.BAllowStoreWalkup.Should().BeFalse();
    }

    [Fact]
    public async Task StoreChoice_Keep_CopiesSourceFlag_AndWalkupRidesAlong()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, bEnableStore: true);
        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BAllowStoreWalkup = true;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, storeChoice: "keep"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableStore.Should().Be(true);
        newJob.BAllowStoreWalkup.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════
    // Copy-everything: config carries; safe-state resets force exposure off
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Clone_CarriesPlayerAndAdultProfileMetadataJson()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        const string playerJson = "{\"fields\":[{\"name\":\"jerseyNumber\"}]}";
        const string adultJson = "{\"UnassignedAdult\":{\"fields\":[{\"name\":\"jerseySize\"}]}}";

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.PlayerProfileMetadataJson = playerJson;
        src.AdultProfileMetadataJson = adultJson;
        src.BplayerRegRequiresToken = true;
        src.BIncludePlayerDonation = true;
        src.JsonOptions = "{\"x\":1}";
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.PlayerProfileMetadataJson.Should().Be(playerJson);
        newJob.AdultProfileMetadataJson.Should().Be(adultJson);
        newJob.BplayerRegRequiresToken.Should().BeTrue();
        newJob.BIncludePlayerDonation.Should().BeTrue();
        newJob.JsonOptions.Should().Be("{\"x\":1}");
    }

    // T2 (Todd-decided 08-02): ALL FIVE BRegistrationAllow* flags force FALSE on clone —
    // a released clone must never instantly open registration. The release page's
    // open-registration panel is the deliberate flip.
    [Fact]
    public async Task Clone_ForcesAllRegistrationAllowFlagsOff()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BRegistrationAllowPlayer = true;
        src.BRegistrationAllowTeam = true;
        src.BRegistrationAllowStaff = true;
        src.BRegistrationAllowReferee = true;
        src.BRegistrationAllowRecruiter = true;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BRegistrationAllowPlayer.Should().BeFalse();
        newJob.BRegistrationAllowTeam.Should().BeFalse();
        newJob.BRegistrationAllowStaff.Should().BeFalse();
        newJob.BRegistrationAllowReferee.Should().BeFalse();
        newJob.BRegistrationAllowRecruiter.Should().BeFalse();
    }

    // The complete safe-state reset list: access/exposure OFF, restrictions ON.
    [Fact]
    public async Task Clone_AppliesSafeStateResets()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BAllowMobileLogin = true;
        src.BAllowMobileRegn = true;
        src.BEnableMobileRsvp = true;
        src.BEnableMobileTeamChat = true;
        src.BEnableTsicteams = true;
        src.BScheduleAllowPublicAccess = true;
        src.BAllowRosterViewPlayer = true;
        src.BAllowRosterViewAdult = true;
        src.BRestrictPublicRosters = false;
        src.BSignalRschedule = true;
        src.BClubRepAllowEdit = false;
        src.BClubRepAllowDelete = false;
        src.BClubRepAllowAdd = false;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var j = await ctx.Jobs.AsNoTracking().FirstAsync(x => x.JobId == resp.NewJobId);
        j.BSuspendPublic.Should().BeTrue();
        j.BAllowMobileLogin.Should().BeFalse();
        j.BAllowMobileRegn.Should().BeFalse();
        j.BEnableMobileRsvp.Should().BeFalse();
        j.BEnableMobileTeamChat.Should().BeFalse();
        j.BEnableTsicteams.Should().BeFalse();
        j.BScheduleAllowPublicAccess.Should().BeFalse();
        j.BAllowRosterViewPlayer.Should().BeFalse();
        j.BAllowRosterViewAdult.Should().BeFalse();
        j.BRestrictPublicRosters.Should().BeTrue();
        j.BSignalRschedule.Should().BeFalse();
        j.BClubRepAllowEdit.Should().BeTrue();
        j.BClubRepAllowDelete.Should().BeTrue();
        j.BClubRepAllowAdd.Should().BeTrue();
    }

    // Retired columns zeroed (Todd-decided 08-02).
    [Fact]
    public async Task Clone_ZeroesRetiredColumns()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BTeamsFullPaymentRequired = true;
        src.BPlayersFullPaymentRequired = true;
        src.PlayerRegMultiPlayerDiscountMin = 2;
        src.PlayerRegMultiPlayerDiscountPercent = 10;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var j = await ctx.Jobs.AsNoTracking().FirstAsync(x => x.JobId == resp.NewJobId);
        j.BTeamsFullPaymentRequired.Should().BeNull();
        j.BPlayersFullPaymentRequired.Should().BeFalse();
        j.PlayerRegMultiPlayerDiscountMin.Should().BeNull();
        j.PlayerRegMultiPlayerDiscountPercent.Should().BeNull();
    }

    // Advance flag: content year-bump incl. JobNameQbp (QuickBooks IIF alias — copied +
    // bumped, superseding legacy's null).
    [Fact]
    public async Task Clone_AdvanceFlag_BumpsContentYearTokens()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.PlayerRegConfirmationEmail = "Welcome to the 2025 season!";
        src.JobNameQbp = "QBP 2025";
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, advance: true), SuperUserId);

        var j = await ctx.Jobs.AsNoTracking().FirstAsync(x => x.JobId == resp.NewJobId);
        j.PlayerRegConfirmationEmail.Should().Be("Welcome to the 2026 season!");
        j.JobNameQbp.Should().Be("QBP 2026");
    }

    // ══════════════════════════════════════════════════════════
    // Admin registrations
    // ══════════════════════════════════════════════════════════

    // RegistrationTs = source + yearDelta (Todd-decided 08-02): the earliest-registered
    // ordering is the default-administrator fallback; "= now" was a bug.
    [Fact]
    public async Task Clone_ShiftsAdminRegistrationTsByYearDelta_AndRemapsPrimaryContact()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, year: "2025");

        var directorRegId = Guid.NewGuid();
        ctx.Registrations.Add(new Registrations
        {
            RegistrationId = directorRegId,
            JobId = jobId,
            RoleId = RoleConstants.Director,
            UserId = "director-1",
            BActive = true,
            RegistrationTs = new DateTime(2025, 3, 1, 8, 0, 0),
            Modified = DateTime.UtcNow,
        });
        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.PrimaryContactRegistrationId = directorRegId;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newRegs = await ctx.Registrations.AsNoTracking()
            .Where(r => r.JobId == resp.NewJobId).ToListAsync();
        var newDirector = newRegs.Single(r => r.UserId == "director-1");

        newDirector.RegistrationTs.Should().Be(new DateTime(2026, 3, 1, 8, 0, 0));
        newDirector.BActive.Should().BeFalse();             // inactive until release panel 3
        newDirector.BConfirmationSent.Should().BeFalse();
        newDirector.FeeTotal.Should().Be(0);
        newDirector.OwedTotal.Should().Be(0);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.PrimaryContactRegistrationId.Should().Be(newDirector.RegistrationId);
        newJob.PrimaryContactRegistrationId.Should().NotBe(directorRegId);
    }

    [Fact]
    public async Task Clone_ActorWithoutSourceReg_GetsFreshActiveSuperuserReg()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        StepCount(resp, JobCloneStepOrder.AdminRegistrations).Should().Be(1);
        var actorReg = await ctx.Registrations.AsNoTracking()
            .FirstAsync(r => r.RegistrationId == resp.NewSuperUserRegistrationId);
        actorReg.JobId.Should().Be(resp.NewJobId);
        actorReg.UserId.Should().Be(SuperUserId);
        actorReg.BActive.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════
    // Bulletins
    // ══════════════════════════════════════════════════════════

    // Forced inactive (legacy 8/2024 rule adopted) + yearDelta date shift.
    [Fact]
    public async Task Clone_Bulletins_ArriveInactive_DatesShifted()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx, year: "2025");

        ctx.Bulletins.Add(new Bulletins
        {
            BulletinId = Guid.NewGuid(),
            JobId = jobId,
            Title = "Welcome 2025",
            Text = "See you in 2025!",
            Active = true,
            CreateDate = new DateTime(2025, 1, 10),
            StartDate = new DateTime(2025, 2, 1),
            EndDate = new DateTime(2025, 6, 1),
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, advance: true), SuperUserId);

        var b = await ctx.Bulletins.AsNoTracking().SingleAsync(x => x.JobId == resp.NewJobId);
        b.Active.Should().BeFalse();
        b.CreateDate.Should().Be(new DateTime(2026, 1, 10));
        b.StartDate.Should().Be(new DateTime(2026, 2, 1));
        b.EndDate.Should().Be(new DateTime(2026, 6, 1));
        b.Title.Should().Be("Welcome 2026");
        b.Text.Should().Be("See you in 2026!");
    }

    // ══════════════════════════════════════════════════════════
    // LADT scope
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task LadtScope_None_SkipsLeagueAgegroupDivisionTeam()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles");
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, renameLeagueId: null, ladtScope: "none"), SuperUserId);

        StepCount(resp, JobCloneStepOrder.Leagues).Should().Be(0);
        StepCount(resp, JobCloneStepOrder.Agegroups).Should().Be(0);
        StepCount(resp, JobCloneStepOrder.Divisions).Should().Be(0);
        StepCount(resp, JobCloneStepOrder.Teams).Should().Be(0);

        var newJobLeagues = await ctx.JobLeagues.AsNoTracking()
            .Where(jl => jl.JobId == resp.NewJobId).ToListAsync();
        newJobLeagues.Should().BeEmpty();
    }

    [Fact]
    public async Task LadtScope_Lad_ClonesLadButSkipsTeams()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles");
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "lad"), SuperUserId);

        StepCount(resp, JobCloneStepOrder.Leagues).Should().Be(1);
        // Boys U10 + the minted Dropped Teams bucket every cloned league is born with.
        StepCount(resp, JobCloneStepOrder.Agegroups).Should().Be(2);
        // Source pool "A" + the Unassigned holding division every cloned agegroup now
        // gets + the minted Dropped Teams division.
        StepCount(resp, JobCloneStepOrder.Divisions).Should().Be(3);
        // No CLONED teams — but the Store Merch anchor is minted regardless of scope.
        StepCount(resp, JobCloneStepOrder.Teams).Should().Be(1);

        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId).ToListAsync();
        var anchor = newTeams.Should().ContainSingle().Which;
        anchor.TeamName.Should().Be("Store Merch");
        anchor.Active.Should().BeFalse();
    }

    // Every cloned league is born with the canonical Dropped Teams bucket, and the job
    // gets one Store Merch anchor under it (Todd-decided 08-12). Shape must match
    // DropTeamAsync's find-or-create exactly — that path FINDS this bucket by name and
    // must never mint a duplicate alongside it.
    [Fact]
    public async Task Clone_MintsDroppedTeamsBucket_AndStoreMerchAnchor()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "lad"), SuperUserId);

        var newLeagueId = (await ctx.JobLeagues.AsNoTracking()
            .SingleAsync(jl => jl.JobId == resp.NewJobId)).LeagueId;

        var bucket = await ctx.Agegroups.AsNoTracking()
            .SingleAsync(a => a.LeagueId == newLeagueId && a.AgegroupName == "Dropped Teams");
        bucket.MaxTeams.Should().Be(999);
        bucket.MaxTeamsPerClub.Should().Be(999);
        bucket.SortAge.Should().Be(254);
        bucket.BAllowApiRosterAccess.Should().BeFalse();

        var bucketDiv = await ctx.Divisions.AsNoTracking()
            .SingleAsync(d => d.AgegroupId == bucket.AgegroupId && d.DivName == "Dropped Teams");

        var anchor = await ctx.Teams.AsNoTracking()
            .SingleAsync(t => t.JobId == resp.NewJobId && t.TeamName == "Store Merch");
        anchor.AgegroupId.Should().Be(bucket.AgegroupId);
        anchor.DivId.Should().Be(bucketDiv.DivId);
        anchor.LeagueId.Should().Be(newLeagueId);
        anchor.Active.Should().BeFalse();
        anchor.ClubrepRegistrationid.Should().BeNull();
        anchor.FeeTotal.Should().Be(0m);
    }

    [Fact]
    public async Task LadtScope_Ladt_ClonesEligibleTeam()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        var sourceTeam = SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles",
            clubRepRegistrationId: null);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);

        // Eagles + the minted Store Merch anchor.
        StepCount(resp, JobCloneStepOrder.Teams).Should().Be(2);

        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId && t.TeamName != "Store Merch").ToListAsync();
        newTeams.Should().HaveCount(1);
        newTeams[0].TeamName.Should().Be("Eagles");
        newTeams[0].PrevTeamId.Should().Be(sourceTeam.TeamId);
        newTeams[0].BnewTeam.Should().BeTrue();
        newTeams[0].LastSeasonYear.Should().Be("2025");
    }

    // CRITICAL eligibility rule (Todd-decided 08-02): clone STRUCTURE teams, never
    // COMPETING teams. Competing = owned AND the owner has a real club name.
    [Fact]
    public async Task LadtScope_Ladt_ExcludesCompetingTeam_ButClonesHouseTeam()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);

        // Competing: owned by a rep with a real club name.
        var competingRep = SeedClubRep(ctx, jobId, "Rockets Lacrosse Club");
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Rockets 2012",
            clubRepRegistrationId: competingRep);

        // House team: owned by a rep with an EMPTY club name (director-created pattern —
        // Main Event / ISP / HHH). This is STRUCTURE and clones, arriving ownerless.
        var houseRep = SeedClubRep(ctx, jobId, "");
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "House Team A",
            clubRepRegistrationId: houseRep);

        // Unowned structure team.
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Open Team",
            clubRepRegistrationId: null);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);

        // House + Open + the minted Store Merch anchor.
        StepCount(resp, JobCloneStepOrder.Teams).Should().Be(3);
        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId).ToListAsync();
        newTeams.Select(t => t.TeamName)
            .Should().BeEquivalentTo("House Team A", "Open Team", "Store Merch");
        // Cloned house team arrives ownerless (clubrep pointers nulled); anchor is too.
        newTeams.Should().OnlyContain(t =>
            t.ClubrepRegistrationid == null && t.ClubrepId == null);
    }

    [Fact]
    public async Task LadtScope_Ladt_ExcludesInactiveTeams()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Active Team");
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Dead Team", active: false);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);

        // Active Team + the minted Store Merch anchor.
        StepCount(resp, JobCloneStepOrder.Teams).Should().Be(2);
        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId && t.TeamName != "Store Merch").ToListAsync();
        newTeams.Should().ContainSingle().Which.TeamName.Should().Be("Active Team");
    }

    // WAITLIST mirrors + Dropped graveyard: neither the AGEGROUPS nor their teams clone.
    [Fact]
    public async Task LadtScope_Ladt_ExcludesWaitlistAndDroppedBuckets_AgegroupsAndTeams()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx,
            agegroupName: "Boys U10");

        var waitlistAg = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = "WAITLIST - Boys U10",
            Season = "Spring",
            Modified = DateTime.UtcNow,
        };
        var droppedAg = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = "Dropped Teams",
            Season = "Spring",
            Modified = DateTime.UtcNow,
        };
        ctx.Agegroups.AddRange(waitlistAg, droppedAg);

        SeedTeam(ctx, jobId, leagueId, waitlistAg.AgegroupId, divId: null, name: "Waitlisted");
        SeedTeam(ctx, jobId, leagueId, droppedAg.AgegroupId, divId: null, name: "Dropped");
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, name: "Eligible");
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);

        // Boys U10 + the MINTED Dropped Teams bucket — the SOURCE's dropped/waitlist
        // agegroups still never clone; the clone births a fresh, empty graveyard.
        StepCount(resp, JobCloneStepOrder.Agegroups).Should().Be(2);
        // Eligible + the minted Store Merch anchor; Waitlisted/Dropped teams excluded.
        StepCount(resp, JobCloneStepOrder.Teams).Should().Be(2);
        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId && t.TeamName != "Store Merch").ToListAsync();
        newTeams.Should().ContainSingle().Which.TeamName.Should().Be("Eligible");
    }

    [Fact]
    public async Task LadtScope_Ladt_ResetsClubRepFinancialsStandingsOnClone()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles",
            clubRepRegistrationId: null, feeBase: 500m, paidTotal: 250m);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);

        var newTeam = await ctx.Teams.AsNoTracking()
            .FirstAsync(t => t.JobId == resp.NewJobId && t.TeamName == "Eagles");

        newTeam.ClubrepRegistrationid.Should().BeNull();
        newTeam.ClubrepId.Should().BeNull();
        newTeam.FeeBase.Should().Be(0m);
        newTeam.FeeProcessing.Should().Be(0m);
        newTeam.FeeTotal.Should().Be(0m);
        newTeam.PaidTotal.Should().Be(0m);
        newTeam.OwedTotal.Should().Be(0m);
        newTeam.Wins.Should().Be(0);
        newTeam.Losses.Should().Be(0);
        newTeam.Points.Should().Be(0);
        newTeam.StandingsRank.Should().BeNull();
        newTeam.LastLeagueRecord.Should().BeNull();
        newTeam.AdnSubscriptionStatus.Should().BeNull();
        newTeam.ViPolicyId.Should().BeNull();
    }

    // Team NAME year-bump under the advance flag (fixes a gap in both stacks).
    [Fact]
    public async Task LadtScope_Ladt_AdvanceFlag_BumpsTeamNameYearTokens()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles 2032");
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt", advance: true), SuperUserId);

        var newTeam = await ctx.Teams.AsNoTracking()
            .FirstAsync(t => t.JobId == resp.NewJobId && t.TeamName != "Store Merch");
        newTeam.TeamName.Should().Be("Eagles 2033");
    }

    // ══════════════════════════════════════════════════════════
    // Fees
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task LadtScope_Ladt_RemapsTeamLevelJobFees()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        var team = SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles");

        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            AgegroupId = agegroupId,
            TeamId = team.TeamId,
            Deposit = 100m,
            BalanceDue = 400m,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);

        var newTeam = await ctx.Teams.AsNoTracking()
            .FirstAsync(t => t.JobId == resp.NewJobId && t.TeamName == "Eagles");
        var newTeamFees = await ctx.JobFees.AsNoTracking()
            .Where(f => f.JobId == resp.NewJobId && f.TeamId != null).ToListAsync();

        newTeamFees.Should().HaveCount(1);
        newTeamFees[0].TeamId.Should().Be(newTeam.TeamId);
        newTeamFees[0].AgegroupId.Should().NotBe(agegroupId); // remapped to new agegroup
        newTeamFees[0].Deposit.Should().Be(100m);
        newTeamFees[0].BalanceDue.Should().Be(400m);
    }

    [Fact]
    public async Task LadtScope_Lad_DropsTeamLevelJobFees()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        var team = SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles");
        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            AgegroupId = agegroupId,
            TeamId = team.TeamId,
            Deposit = 100m,
            BalanceDue = 400m,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "lad"), SuperUserId);

        var newTeamFees = await ctx.JobFees.AsNoTracking()
            .Where(f => f.JobId == resp.NewJobId && f.TeamId != null).ToListAsync();
        newTeamFees.Should().BeEmpty();
    }

    // BFullPaymentRequired is RESET TO NULL, never copied or derived (Todd-decided
    // 08-04). It is tri-state: null = inherit, false = explicit deposit VETO, true =
    // full payment. Any non-null clone output is a per-scope override that beats the
    // league-level phase control — the earlier shape-derive (false when a deposit
    // exists) vetoed the league card on every AG row of every clone. All-null = the
    // new season opens deposit-phase and the league card governs from day one.
    // Phase-only source rows (no amounts, no modifiers) are NOT cloned at all — with
    // the stamp nulled they would be all-null junk rows.
    [Fact]
    public async Task Clone_JobFees_PhaseResetToNull_PhaseOnlyRowsDropped()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, _) = await SeedSourceJobAsync(ctx);

        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            AgegroupId = agegroupId,
            Deposit = 100m,
            BalanceDue = 400m,
            BFullPaymentRequired = false,   // source AG veto — must NOT copy (would
                                            // make the new job's league card inert)
            Modified = DateTime.UtcNow,
        });
        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            Deposit = null,                 // no deposit configured (job-wide row)
            BalanceDue = 300m,
            BFullPaymentRequired = true,    // source in full-pay phase — must NOT copy
            Modified = DateTime.UtcNow,
        });
        // League-scope phase-only stamp (the §8P / league-card SaveFee shape):
        // no amounts, no modifiers — must not clone at all.
        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            LeagueId = leagueId,
            BFullPaymentRequired = true,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId, leagueId), SuperUserId);

        var newFees = await ctx.JobFees.AsNoTracking()
            .Where(f => f.JobId == resp.NewJobId).ToListAsync();
        newFees.Should().HaveCount(2, "phase-only rows are not cloned");
        newFees.Should().OnlyContain(f => f.BFullPaymentRequired == null,
            "a clone carries no phase opinion at any scope — the league card governs");
        StepCount(resp, JobCloneStepOrder.JobFees).Should().Be(2);
    }

    // ══════════════════════════════════════════════════════════
    // Multi-league (T3)
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Clone_MultiLeague_ClonesAllLeagues_AndRemapsLeagueScopedFees()
    {
        var (svc, ctx) = BuildService();
        var (jobId, league1, _, _) = await SeedSourceJobAsync(ctx);

        var league2 = Guid.NewGuid();
        ctx.Leagues.Add(new Leagues
        {
            LeagueId = league2,
            LeagueName = "Second League",
            SportId = Guid.NewGuid(),
            Modified = DateTime.UtcNow,
        });
        ctx.JobLeagues.Add(new JobLeagues
        {
            JobLeagueId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = league2,
            BIsPrimary = false,
            Modified = DateTime.UtcNow,
        });
        // League-scoped fee row on the SECOND league — was silently dropped pre-T3.
        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.ClubRep,
            LeagueId = league2,
            Deposit = 250m,
            BalanceDue = 750m,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var req = BaseRequest(jobId, renameLeagueId: null) with
        {
            Leagues =
            [
                new LeagueRenameDto { SourceLeagueId = league1, NameTarget = "New League One" },
                new LeagueRenameDto { SourceLeagueId = league2, NameTarget = "New League Two" },
            ],
        };
        var resp = await svc.CloneJobAsync(req, SuperUserId);

        StepCount(resp, JobCloneStepOrder.Leagues).Should().Be(2);
        StepCount(resp, JobCloneStepOrder.JobLeagues).Should().Be(2);

        var newLinks = await ctx.JobLeagues.AsNoTracking()
            .Where(jl => jl.JobId == resp.NewJobId).ToListAsync();
        newLinks.Should().HaveCount(2);
        newLinks.Count(jl => jl.BIsPrimary).Should().Be(1);     // BIsPrimary preserved

        var newLeagueIds = newLinks.Select(jl => jl.LeagueId).ToHashSet();
        var newLeagues = await ctx.Leagues.AsNoTracking()
            .Where(l => newLeagueIds.Contains(l.LeagueId)).ToListAsync();
        newLeagues.Select(l => l.LeagueName)
            .Should().BeEquivalentTo("New League One", "New League Two");

        var leagueFee = await ctx.JobFees.AsNoTracking()
            .SingleAsync(f => f.JobId == resp.NewJobId && f.LeagueId != null);
        newLeagueIds.Should().Contain(leagueFee.LeagueId!.Value);
        leagueFee.LeagueId.Should().NotBe(league2);
    }

    // ══════════════════════════════════════════════════════════
    // One plan, two consumers — parity + data-moved guard
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task PreviewAndClone_ProduceIdenticalPerStepCounts()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        var competingRep = SeedClubRep(ctx, jobId, "Real Club");
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Competing", competingRep);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Structure");
        ctx.Bulletins.Add(new Bulletins
        {
            BulletinId = Guid.NewGuid(),
            JobId = jobId,
            Title = "B1",
            Active = true,
            CreateDate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        });
        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.Player,
            AgegroupId = agegroupId,
            Deposit = 50m,
            BalanceDue = 200m,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var req = BaseRequest(jobId, leagueId, ladtScope: "ladt");
        var plan = await svc.PreviewCloneAsync(req, SuperUserId);
        var resp = await svc.CloneJobAsync(
            req with { PlanFingerprint = plan.PlanFingerprint }, SuperUserId);

        // Row-by-row parity — same planner feeds both consumers.
        foreach (var step in plan.Steps)
            StepCount(resp, step.StepKey).Should().Be(step.Count, $"step {step.StepKey}");

        plan.TeamsToClone.Should().Be(1);
        plan.TeamsExcludedCompeting.Should().Be(1);
    }

    [Fact]
    public async Task Clone_StaleFingerprint_ThrowsClonePlanChanged()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx);

        var req = BaseRequest(jobId, leagueId);
        var plan = await svc.PreviewCloneAsync(req, SuperUserId);

        // Source data moves between preview and submit: a bulletin appears.
        ctx.Bulletins.Add(new Bulletins
        {
            BulletinId = Guid.NewGuid(),
            JobId = jobId,
            Title = "Late-breaking",
            Active = true,
            CreateDate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var act = () => svc.CloneJobAsync(
            req with { PlanFingerprint = plan.PlanFingerprint }, SuperUserId);

        var ex = await act.Should().ThrowAsync<ClonePlanChangedException>();
        StepCount(ex.Which.FreshPlan, JobCloneStepOrder.Bulletins).Should().Be(1);
    }

    [Fact]
    public async Task Preview_SourceRatesAndFlags_Populated()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, _) = await SeedSourceJobAsync(ctx,
            processingFeePercent: 3.85m,
            ecprocessingFeePercent: 1.95m,
            bEnableEcheck: true,
            bEnableStore: true);

        var plan = await svc.PreviewCloneAsync(BaseRequest(jobId, leagueId), SuperUserId);

        plan.SourceProcessingFeePercent.Should().Be(3.85m);
        plan.ResolvedProcessingFeePercent.Should().Be(3.85m);   // above floor → carried
        plan.SourceEcheckProcessingFeePercent.Should().Be(1.95m);
        plan.ResolvedEcheckProcessingFeePercent.Should().Be(1.95m);
        plan.SourceBEnableEcheck.Should().BeTrue();
        plan.SourceBEnableStore.Should().BeTrue();
        plan.YearDelta.Should().Be(1);
        plan.AdvanceFlagDefault.Should().BeTrue();
    }

    // OpenRegistration_FlipsOnlyRequestedPersonas removed with the release page: opening
    // registration is the five BRegistrationAllow* flags on Configure → Job, and
    // Clone_ForcesAllRegistrationAllowFlagsOff still pins the clone-side invariant that
    // they all arrive false.

    // ══════════════════════════════════════════════════════════
    // Dev-undo (manifest-reversed cascade delete)
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task DevUndo_FullLadtClone_DeletesEverythingTheCloneCreated()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles");
        ctx.Bulletins.Add(new Bulletins
        {
            BulletinId = Guid.NewGuid(),
            JobId = jobId,
            Title = "B1",
            Active = true,
            CreateDate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
        });
        ctx.JobFees.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.Player,
            AgegroupId = agegroupId,
            Deposit = 50m,
            BalanceDue = 200m,
            Modified = DateTime.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, leagueId, ladtScope: "ladt"), SuperUserId);
        var newJobId = resp.NewJobId;

        var status = await svc.GetDevUndoStatusAsync(newJobId);
        status.CanUndo.Should().BeTrue(string.Join("; ", status.Reasons));

        await svc.DeleteClonedJobAsync(newJobId);

        (await ctx.Jobs.AsNoTracking().AnyAsync(j => j.JobId == newJobId)).Should().BeFalse();
        (await ctx.Teams.AsNoTracking().AnyAsync(t => t.JobId == newJobId)).Should().BeFalse();
        (await ctx.Registrations.AsNoTracking().AnyAsync(r => r.JobId == newJobId)).Should().BeFalse();
        (await ctx.Bulletins.AsNoTracking().AnyAsync(b => b.JobId == newJobId)).Should().BeFalse();
        (await ctx.JobFees.AsNoTracking().AnyAsync(f => f.JobId == newJobId)).Should().BeFalse();
        (await ctx.JobLeagues.AsNoTracking().AnyAsync(jl => jl.JobId == newJobId)).Should().BeFalse();

        // Cloned league gone; SOURCE league untouched.
        var newLeagueNames = await ctx.Leagues.AsNoTracking()
            .Select(l => l.LeagueName).ToListAsync();
        newLeagueNames.Should().NotContain("New League");
        newLeagueNames.Should().Contain("Source League");
    }
}
