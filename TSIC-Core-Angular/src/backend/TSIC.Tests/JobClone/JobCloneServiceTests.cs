using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.JobClone;

/// <summary>
/// Behavior tests for JobCloneService against the InMemory EF provider.
///
/// Coverage:
///   - Choice validation rejects invalid scope/choice strings + out-of-range custom fees
///   - CC + eCheck processing-fee resolution honors source / current / custom
///   - eCheck enable + Store choices applied to new Job
///   - LadtScope: none / lad / ladt produce expected entity sets
///   - LADT team filter: ClubRep-paid + WAITLIST/DROPPED excluded
///   - Cloned teams: ClubRep refs nulled, financials zeroed, lineage flagged
///   - Team-level JobFees remap via teamIdMap
///
/// **NOT covered** (InMemory limitation, accepted gap): transactional rollback. EF InMemory
/// has no real transactions — BeginTransaction/Commit/Rollback are silent no-ops here.
/// FK + unique-constraint enforcement and SQL-translation semantics also diverge from prod
/// SQL Server. Treat these as shape/behavior tests, not integrity tests. Mid-clone failure
/// scenarios + constraint-violation paths must be verified against real SQL Server.
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
        var svc = new JobCloneService(repo, feeRepo, NullLogger<JobCloneService>.Instance);
        return (svc, ctx);
    }

    /// <summary>
    /// Seeds a source Job + League + JobLeague + Agegroup + Division. Returns IDs for
    /// follow-up assertions / team seeding.
    /// </summary>
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
        var customerId = Guid.NewGuid();
        var leagueId = Guid.NewGuid();
        var agegroupId = Guid.NewGuid();
        var divId = Guid.NewGuid();
        var sportId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        ctx.Jobs.Add(new Jobs
        {
            JobId = jobId,
            JobPath = $"src-{Guid.NewGuid():N}"[..16],
            JobName = "Source Job",
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
        decimal feeBase = 100m, decimal paidTotal = 50m)
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
            Active = true,
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

    private static JobCloneRequest BaseRequest(Guid sourceJobId, string ladtScope = "lad",
        string processingFeeChoice = "current",
        decimal? customCcFee = null,
        string echeckProcessingFeeChoice = "current",
        decimal? customEcheckFee = null,
        string enableEcheckChoice = "off",
        string storeChoice = "disable")
    {
        return new JobCloneRequest
        {
            SourceJobId = sourceJobId,
            JobPathTarget = $"new-{Guid.NewGuid():N}"[..16],
            JobNameTarget = $"New {Guid.NewGuid():N}"[..16],
            YearTarget = "2026",
            SeasonTarget = "Spring",
            DisplayName = "New",
            LeagueNameTarget = "New League",
            ExpiryAdmin = DateTime.UtcNow.AddYears(1),
            ExpiryUsers = DateTime.UtcNow.AddYears(1),
            UpAgegroupNamesByOne = false,
            LadtScope = ladtScope,
            ProcessingFeeChoice = processingFeeChoice,
            CustomProcessingFeePercent = customCcFee,
            EcheckProcessingFeeChoice = echeckProcessingFeeChoice,
            CustomEcheckProcessingFeePercent = customEcheckFee,
            EnableEcheckChoice = enableEcheckChoice,
            StoreChoice = storeChoice,
        };
    }

    // ══════════════════════════════════════════════════════════
    // Choice validation
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Validation_InvalidLadtScope_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        var act = () => svc.CloneJobAsync(BaseRequest(jobId, ladtScope: "all"), SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*LadtScope*");
    }

    [Fact]
    public async Task Validation_InvalidProcessingFeeChoice_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        var act = () => svc.CloneJobAsync(
            BaseRequest(jobId, processingFeeChoice: "average"), SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ProcessingFeeChoice*");
    }

    [Fact]
    public async Task Validation_CustomCcFee_BelowMin_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        var act = () => svc.CloneJobAsync(
            BaseRequest(jobId, processingFeeChoice: "custom", customCcFee: 1.0m), SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*CustomProcessingFeePercent*");
    }

    [Fact]
    public async Task Validation_CustomCcFee_AboveMax_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        var act = () => svc.CloneJobAsync(
            BaseRequest(jobId, processingFeeChoice: "custom", customCcFee: 5.0m), SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*CustomProcessingFeePercent*");
    }

    [Fact]
    public async Task Validation_CustomEcheckFee_OutOfRange_Throws()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        var act = () => svc.CloneJobAsync(
            BaseRequest(jobId, echeckProcessingFeeChoice: "custom", customEcheckFee: 5.0m),
            SuperUserId);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*CustomEcheckProcessingFeePercent*");
    }

    // ══════════════════════════════════════════════════════════
    // Processing fee resolution
    // ══════════════════════════════════════════════════════════

    // The clone carries the source job's stored rate forward, floored at the current
    // new-job rate (CC 3.8 / eCheck 1.5). The request's fee-choice fields are not consulted.

    [Fact]
    public async Task ProcessingFee_SourceAboveFloor_CarriedForward()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, processingFeePercent: 3.9m);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.ProcessingFeePercent.Should().Be(3.9m);
    }

    [Fact]
    public async Task ProcessingFee_SourceBelowFloor_RaisedToNewJobRate()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, processingFeePercent: 3.5m);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.ProcessingFeePercent.Should().Be(3.8m); // FeeConstants.NewJobProcessingFeePercent
    }

    [Fact]
    public async Task ProcessingFee_SourceNull_UsesNewJobRate()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, processingFeePercent: null);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.ProcessingFeePercent.Should().Be(3.8m); // FeeConstants.NewJobProcessingFeePercent
    }

    [Fact]
    public async Task EcheckProcessingFee_SourceAboveFloor_CarriedForward()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, ecprocessingFeePercent: 1.85m);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.EcprocessingFeePercent.Should().Be(1.85m);
    }

    [Fact]
    public async Task EcheckProcessingFee_SourceNull_UsesNewJobRate()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, ecprocessingFeePercent: null);

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.EcprocessingFeePercent.Should().Be(1.5m); // FeeConstants.NewJobEcprocessingFeePercent
    }

    // ══════════════════════════════════════════════════════════
    // EnableEcheck + Store
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task EnableEcheckChoice_Off_DisablesOnNewJob_RegardlessOfSource()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, bEnableEcheck: true);

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, enableEcheckChoice: "off"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableEcheck.Should().BeFalse();
    }

    [Fact]
    public async Task EnableEcheckChoice_Source_CopiesSourceFlag()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, bEnableEcheck: true);

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, enableEcheckChoice: "source"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableEcheck.Should().BeTrue();
    }

    [Fact]
    public async Task StoreChoice_Disable_DisablesOnNewJob_RegardlessOfSource()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, bEnableStore: true);

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, storeChoice: "disable"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableStore.Should().BeFalse();
    }

    [Fact]
    public async Task StoreChoice_Keep_CopiesSourceFlag()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, bEnableStore: true);

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, storeChoice: "keep"), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BEnableStore.Should().Be(true);
    }

    // ══════════════════════════════════════════════════════════
    // Profile metadata carry-forward
    // ══════════════════════════════════════════════════════════

    // Regression guard: the clone must carry BOTH materialized profile forms. Player was
    // always copied; adult (AdultProfileMetadataJson) was dropped until the copy block was
    // fixed, silently collapsing every adult role on a cloned job to the SpecialRequests
    // fallback even though its ListSizes_* apparel lists rode along in JsonOptions.
    [Fact]
    public async Task Clone_CarriesPlayerAndAdultProfileMetadataJson()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        const string playerJson = "{\"fields\":[{\"name\":\"jerseyNumber\"}]}";
        const string adultJson =
            "{\"UnassignedAdult\":{\"fields\":[{\"name\":\"jerseySize\"}]}," +
            "\"Referee\":{\"fields\":[]},\"Recruiter\":{\"fields\":[]}}";

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.PlayerProfileMetadataJson = playerJson;
        src.AdultProfileMetadataJson = adultJson;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.PlayerProfileMetadataJson.Should().Be(playerJson);
        newJob.AdultProfileMetadataJson.Should().Be(adultJson);
    }

    // Regression guard: BRegistrationAllow{Staff,Referee,Recruiter} were added to Jobs after the
    // clone copy-block was written, so clone carried Player/Team but silently dropped the three
    // adult-role enablement switches — a cloned multi-role job came out coach-only (it kept the
    // ref/recruiter confirmation emails but lost the flags that actually turn those regs on).
    [Fact]
    public async Task Clone_CarriesAllRegistrationAllowFlags()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx);

        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BRegistrationAllowPlayer = true;
        src.BRegistrationAllowTeam = true;
        src.BRegistrationAllowStaff = true;
        src.BRegistrationAllowReferee = true;
        src.BRegistrationAllowRecruiter = true;
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(BaseRequest(jobId), SuperUserId);

        var newJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == resp.NewJobId);
        newJob.BRegistrationAllowPlayer.Should().BeTrue();
        newJob.BRegistrationAllowTeam.Should().BeTrue();
        newJob.BRegistrationAllowStaff.Should().BeTrue();
        newJob.BRegistrationAllowReferee.Should().BeTrue();
        newJob.BRegistrationAllowRecruiter.Should().BeTrue();
    }

    // Regression guard: auditing the copy-block after the role flags found 8 more scalar columns
    // silently dropped — registration token-gating, public-roster privacy, ADN trial (partial ARB
    // copy), donations, and store walk-up. Each had copied siblings; none was intentional. Two
    // refinements verified here: AdnStartDateAfterTrial year-shifts like AdnArbstartDate, and
    // BAllowStoreWalkup follows the resolved store state (can't leak back on under "disable store").
    [Fact]
    public async Task Clone_CarriesRecoveredRegistrationBillingAndStoreConfig()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx, year: "2025", bEnableStore: true);

        var trialStart = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var src = await ctx.Jobs.FirstAsync(j => j.JobId == jobId);
        src.BplayerRegRequiresToken = true;
        src.BteamRegRequiresToken = true;
        src.BRestrictPublicRosters = true;
        src.BIncludePlayerDonation = true;
        src.BIncludeTeamDonation = true;
        src.AdnArbtrial = true;
        src.AdnStartDateAfterTrial = trialStart;
        src.BAllowStoreWalkup = true;
        await ctx.SaveChangesAsync();

        // storeChoice "keep" → store stays enabled, so walk-up rides along.
        var kept = await svc.CloneJobAsync(BaseRequest(jobId, storeChoice: "keep"), SuperUserId);
        var keptJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == kept.NewJobId);

        keptJob.BplayerRegRequiresToken.Should().BeTrue();
        keptJob.BteamRegRequiresToken.Should().BeTrue();
        keptJob.BRestrictPublicRosters.Should().BeTrue();
        keptJob.BIncludePlayerDonation.Should().BeTrue();
        keptJob.BIncludeTeamDonation.Should().BeTrue();
        keptJob.AdnArbtrial.Should().BeTrue();
        keptJob.BAllowStoreWalkup.Should().BeTrue();
        // 2025 source → 2026 target = +1yr, mirroring AdnArbstartDate's shift (not copied raw).
        keptJob.AdnStartDateAfterTrial.Should().NotBeNull();
        keptJob.AdnStartDateAfterTrial!.Value.Year.Should().Be(2026);

        // storeChoice "disable" → store off, so walk-up is forced off regardless of source.
        var disabled = await svc.CloneJobAsync(BaseRequest(jobId, storeChoice: "disable"), SuperUserId);
        var disabledJob = await ctx.Jobs.AsNoTracking().FirstAsync(j => j.JobId == disabled.NewJobId);
        disabledJob.BAllowStoreWalkup.Should().BeFalse();
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
            BaseRequest(jobId, ladtScope: "none"), SuperUserId);

        resp.Summary.LeaguesCloned.Should().Be(0);
        resp.Summary.AgegroupsCloned.Should().Be(0);
        resp.Summary.DivisionsCloned.Should().Be(0);
        resp.Summary.TeamsCloned.Should().Be(0);

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
            BaseRequest(jobId, ladtScope: "lad"), SuperUserId);

        resp.Summary.LeaguesCloned.Should().Be(1);
        resp.Summary.AgegroupsCloned.Should().Be(1);
        resp.Summary.DivisionsCloned.Should().Be(1);
        resp.Summary.TeamsCloned.Should().Be(0);

        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId).ToListAsync();
        newTeams.Should().BeEmpty();
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
            BaseRequest(jobId, ladtScope: "ladt"), SuperUserId);

        resp.Summary.TeamsCloned.Should().Be(1);

        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId).ToListAsync();
        newTeams.Should().HaveCount(1);
        newTeams[0].TeamName.Should().Be("Eagles");
        newTeams[0].PrevTeamId.Should().Be(sourceTeam.TeamId);
        newTeams[0].BnewTeam.Should().BeTrue();
    }

    [Fact]
    public async Task LadtScope_Ladt_ExcludesClubRepPaidTeam()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Paid Team",
            clubRepRegistrationId: Guid.NewGuid());
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Open Team",
            clubRepRegistrationId: null);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, ladtScope: "ladt"), SuperUserId);

        resp.Summary.TeamsCloned.Should().Be(1);

        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId).ToListAsync();
        newTeams.Should().HaveCount(1);
        newTeams[0].TeamName.Should().Be("Open Team");
    }

    [Fact]
    public async Task LadtScope_Ladt_ExcludesWaitlistAndDroppedAgegroupTeams()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, _, divId) = await SeedSourceJobAsync(ctx,
            agegroupName: "Boys U10");

        // Seed two extra agegroups: one WAITLIST, one DROPPED. Same league.
        var waitlistAg = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = "Boys U10 - WAITLIST",
            Season = "Spring",
            Modified = DateTime.UtcNow,
        };
        var droppedAg = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = "DROPPED",
            Season = "Spring",
            Modified = DateTime.UtcNow,
        };
        ctx.Agegroups.AddRange(waitlistAg, droppedAg);

        // Source teams: one in normal agegroup (eligible), one in WAITLIST, one in DROPPED.
        var normalAgegroupId = await ctx.Agegroups.AsNoTracking()
            .Where(a => a.AgegroupName == "Boys U10").Select(a => a.AgegroupId).FirstAsync();
        SeedTeam(ctx, jobId, leagueId, waitlistAg.AgegroupId, divId: null, name: "Waitlisted");
        SeedTeam(ctx, jobId, leagueId, droppedAg.AgegroupId, divId: null, name: "Dropped");
        SeedTeam(ctx, jobId, leagueId, normalAgegroupId, divId, name: "Eligible");

        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, ladtScope: "ladt"), SuperUserId);

        resp.Summary.TeamsCloned.Should().Be(1);
        var newTeams = await ctx.Teams.AsNoTracking()
            .Where(t => t.JobId == resp.NewJobId).ToListAsync();
        newTeams.Should().ContainSingle().Which.TeamName.Should().Be("Eligible");
    }

    [Fact]
    public async Task LadtScope_Ladt_ResetsClubRepAndFinancialsOnClone()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles",
            clubRepRegistrationId: null, // eligible
            feeBase: 500m, paidTotal: 250m);
        await ctx.SaveChangesAsync();

        var resp = await svc.CloneJobAsync(
            BaseRequest(jobId, ladtScope: "ladt"), SuperUserId);

        var newTeam = await ctx.Teams.AsNoTracking()
            .FirstAsync(t => t.JobId == resp.NewJobId);

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
        newTeam.AdnSubscriptionStatus.Should().BeNull();
        newTeam.ViPolicyId.Should().BeNull();
    }

    [Fact]
    public async Task LadtScope_Ladt_RemapsTeamLevelJobFees()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);
        var team = SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eagles");

        // Team-level fee row (TeamId set) for ClubRep role.
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
            BaseRequest(jobId, ladtScope: "ladt"), SuperUserId);

        var newTeam = await ctx.Teams.AsNoTracking()
            .FirstAsync(t => t.JobId == resp.NewJobId);
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
        // Confirms LAD scope skips team-level fees (no team to point at).
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
            BaseRequest(jobId, ladtScope: "lad"), SuperUserId);

        var newTeamFees = await ctx.JobFees.AsNoTracking()
            .Where(f => f.JobId == resp.NewJobId && f.TeamId != null).ToListAsync();
        newTeamFees.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════
    // Preview team counts
    // ══════════════════════════════════════════════════════════

    [Fact]
    public async Task Preview_PopulatesTeamCounts()
    {
        var (svc, ctx) = BuildService();
        var (jobId, leagueId, agegroupId, divId) = await SeedSourceJobAsync(ctx);

        var waitlistAg = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = "WAITLIST",
            Season = "Spring",
            Modified = DateTime.UtcNow,
        };
        ctx.Agegroups.Add(waitlistAg);

        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eligible1",
            clubRepRegistrationId: null);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Eligible2",
            clubRepRegistrationId: null);
        SeedTeam(ctx, jobId, leagueId, agegroupId, divId, "Paid",
            clubRepRegistrationId: Guid.NewGuid());
        SeedTeam(ctx, jobId, leagueId, waitlistAg.AgegroupId, null, "Waitlisted",
            clubRepRegistrationId: null);
        await ctx.SaveChangesAsync();

        var preview = await svc.PreviewCloneAsync(BaseRequest(jobId, ladtScope: "ladt"));

        preview.TeamsToClone.Should().Be(2);
        preview.TeamsExcludedPaid.Should().Be(1);
        preview.TeamsExcludedWaitlistDropped.Should().Be(1);
    }

    [Fact]
    public async Task Preview_SourceFlags_PopulatedFromSourceJob()
    {
        var (svc, ctx) = BuildService();
        var (jobId, _, _, _) = await SeedSourceJobAsync(ctx,
            processingFeePercent: 3.85m,
            ecprocessingFeePercent: 1.95m,
            bEnableEcheck: true,
            bEnableStore: true);

        var preview = await svc.PreviewCloneAsync(BaseRequest(jobId));

        preview.SourceProcessingFeePercent.Should().Be(3.85m);
        preview.SourceEcheckProcessingFeePercent.Should().Be(1.95m);
        preview.SourceBEnableEcheck.Should().BeTrue();
        preview.SourceBEnableStore.Should().BeTrue();
        preview.CurrentProcessingFeePercent.Should().Be(3.8m); // FeeConstants.NewJobProcessingFeePercent
        preview.CurrentEcheckProcessingFeePercent.Should().Be(1.5m);
    }
}
