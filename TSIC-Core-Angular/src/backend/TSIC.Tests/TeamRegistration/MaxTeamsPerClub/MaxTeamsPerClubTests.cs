using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.Tests.TeamRegistration.MaxTeamsPerClub;

/// <summary>
/// MAX TEAMS PER CLUB TESTS
///
/// Validates that TeamRegistrationService enforces the MaxTeamsPerClub
/// limit on the Agegroups entity. This prevents a single club from
/// dominating an age group by registering too many teams.
///
/// What these tests prove:
///   - Club under limit → registration proceeds to placement
///   - Club at limit → returns Success = false with friendly message
///   - MaxTeamsPerClub = 0 → unlimited (check skipped entirely)
///
/// Service under test: TeamRegistrationService.RegisterTeamForEventAsync()
/// All 17 dependencies are mocked. The mock chain ensures the service
/// passes all earlier validation checks before reaching MaxTeamsPerClub.
/// </summary>
public class MaxTeamsPerClubTests
{
    // ── Shared test data ─────────────────────────────────────────────

    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly Guid TestRegId = Guid.NewGuid();
    private static readonly Guid TestAgegroupId = Guid.NewGuid();
    private static readonly Guid TestLeagueId = Guid.NewGuid();
    private static readonly string TestUserId = "clubrep-user-1";
    private static readonly string TestClubName = "Test FC";

    // ── Factory: builds service with full mock chain ─────────────────

    private static (
        TeamRegistrationService svc,
        Mock<ITeamRepository> teamRepo,
        Mock<ITeamPlacementService> placement,
        Mock<IAgeGroupRepository> agRepo)
        CreateService(int maxTeamsPerClub, int currentClubTeamCount)
    {
        var logger = new Mock<ILogger<TeamRegistrationService>>();
        var clubReps = new Mock<IClubRepRepository>();
        var clubs = new Mock<IClubRepository>();
        var jobs = new Mock<IJobRepository>();
        var jobLeagues = new Mock<IJobLeagueRepository>();
        var agRepo = new Mock<IAgeGroupRepository>();
        var teamRepo = new Mock<ITeamRepository>();
        var regRepo = new Mock<IRegistrationRepository>();
        var users = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var feeService = new Mock<IFeeResolutionService>();
        var textSubstitution = new Mock<ITextSubstitutionService>();
        var emailService = new Mock<IEmailService>();
        var discountCodeRepo = new Mock<IJobDiscountCodeRepository>();
        var clubTeams = new Mock<IClubTeamRepository>();
        var placement = new Mock<ITeamPlacementService>();

        // UserManager needs a store mock
        var userStore = new Mock<IUserPasswordStore<ApplicationUser>>();
        userStore.As<IUserStore<ApplicationUser>>();
        var userManager = new UserManager<ApplicationUser>(
            userStore.Object, null!, new PasswordHasher<ApplicationUser>(),
            null!, null!, null!, null!, null!, null!);

        // ── Mock chain: each setup satisfies a validation check that runs
        // BEFORE the MaxTeamsPerClub check (lines 486-558 of the service) ──

        // 1. GetByIdAsync → returns valid club rep registration
        regRepo
            .Setup(r => r.GetByIdAsync(TestRegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Registrations
            {
                RegistrationId = TestRegId,
                JobId = TestJobId,
                UserId = TestUserId,
                RoleId = RoleConstants.ClubRep,
                ClubName = TestClubName,
                Modified = DateTime.UtcNow,
            });

        // 2. GetJobFeeSettingsAsync → returns valid job settings
        jobs
            .Setup(j => j.GetJobFeeSettingsAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings { PaymentMethodsAllowedCode = 1 });

        // 3. GetEffectiveProcessingRateAsync → return 0 (no processing fees)
        feeService
            .Setup(f => f.GetEffectiveProcessingRateAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        // 4. GetByNameAsync → returns a club
        clubs
            .Setup(c => c.GetByNameAsync(TestClubName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clubs { ClubId = 1, ClubName = TestClubName });

        // 5. ExistsAsync → user has access to this club
        clubReps
            .Setup(cr => cr.ExistsAsync(TestUserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // 6. GetPrimaryLeagueForJobAsync → returns a league
        jobLeagues
            .Setup(jl => jl.GetPrimaryLeagueForJobAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestLeagueId);

        // 7. GetTeamsByClubExcludingRegistrationAsync → no conflicts (one-rep-per-event passes)
        teamRepo
            .Setup(t => t.GetTeamsByClubExcludingRegistrationAsync(
                TestJobId, 1, TestRegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamWithRegistrationInfo>());

        // 8. GetByIdAsync → agegroup with the specified MaxTeamsPerClub
        agRepo
            .Setup(a => a.GetByIdAsync(TestAgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agegroups
            {
                AgegroupId = TestAgegroupId,
                LeagueId = TestLeagueId,
                AgegroupName = "Boys U14",
                MaxTeams = 100,
                MaxTeamsPerClub = maxTeamsPerClub,
                Modified = DateTime.UtcNow,
            });

        // 9. KEY MOCK — GetRegisteredCountForClubRepAndAgegroupAsync
        teamRepo
            .Setup(t => t.GetRegisteredCountForClubRepAndAgegroupAsync(
                TestJobId, TestAgegroupId, TestRegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentClubTeamCount);

        // Fee resolution (needed if we reach placement)
        feeService
            .Setup(f => f.ResolveClubRepFeeAsync(
                TestJobId, TestAgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResolvedFee?)null);
        feeService
            .Setup(f => f.EvaluateClubRepModifiersAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedModifiers());

        // Team save (needed if we reach team creation)
        teamRepo
            .Setup(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Placement (needed if we pass the MaxTeamsPerClub check)
        placement
            .Setup(p => p.ResolvePlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamPlacementResult
            {
                AgegroupId = TestAgegroupId,
                LeagueId = TestLeagueId,
                IsWaitlisted = false,
            });

        var svc = new TeamRegistrationService(
            logger.Object, clubReps.Object, clubs.Object, jobs.Object,
            jobLeagues.Object, agRepo.Object, teamRepo.Object, regRepo.Object,
            users.Object, tokenService.Object, userManager, feeService.Object,
            textSubstitution.Object, emailService.Object, discountCodeRepo.Object,
            clubTeams.Object, placement.Object);

        return (svc, teamRepo, placement, agRepo);
    }

    // ── Helper: build a RegisterTeamRequest ──────────────────────────

    private static RegisterTeamRequest MakeRequest() => new()
    {
        AgeGroupId = TestAgegroupId,
        TeamName = "New Team",
        ClubTeamGradYear = "2030",
        LevelOfPlay = "A",
    };

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Register: club under limit (2/3) → reaches placement")]
    public async Task Register_UnderClubLimit_ReachesPlacement()
    {
        // Arrange — club has 2 teams, limit is 3
        var (svc, _, placement, _) = CreateService(maxTeamsPerClub: 3, currentClubTeamCount: 2);

        // Act
        var result = await svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        // Assert — registration proceeded past the MaxTeamsPerClub check
        result.Success.Should().BeTrue("club is under the per-club limit");

        // Verify placement was called (meaning the check passed)
        placement.Verify(p => p.ResolvePlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once, "placement should be called when under the club limit");
    }

    [Fact(DisplayName = "Register: club at limit (3/3) → returns Success = false with message")]
    public async Task Register_AtClubLimit_ReturnsFailure()
    {
        // Arrange — club already has 3 teams, limit is 3
        var (svc, teamRepo, placement, _) = CreateService(maxTeamsPerClub: 3, currentClubTeamCount: 3);

        // Act
        var result = await svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        // Assert — blocked with a friendly message
        result.Success.Should().BeFalse("club has reached the per-club limit");
        result.Message.Should().Contain("maximum of 3", "message should tell the user the limit");
        result.Message.Should().Contain("Boys U14", "message should name the agegroup");
        result.TeamId.Should().Be(Guid.Empty);

        // Verify placement was NEVER called (blocked before reaching placement)
        placement.Verify(p => p.ResolvePlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "placement should not be called when the club limit is reached");
    }

    [Fact(DisplayName = "Register: MaxTeamsPerClub = 0 (unlimited) → check skipped, reaches placement")]
    public async Task Register_MaxTeamsPerClubZero_SkipsCheck()
    {
        // Arrange — no per-club limit (0 = unlimited), even with 100 teams
        var (svc, teamRepo, placement, _) = CreateService(maxTeamsPerClub: 0, currentClubTeamCount: 100);

        // Act
        var result = await svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        // Assert — registration proceeded (unlimited means no check)
        result.Success.Should().BeTrue("MaxTeamsPerClub = 0 means unlimited");

        // Verify the count query was NEVER called (check is skipped entirely)
        teamRepo.Verify(t => t.GetRegisteredCountForClubRepAndAgegroupAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "should not query club team count when MaxTeamsPerClub = 0");
    }

    // ── Agegroup MaxTeams tests (full service flow) ──────────────────

    [Fact(DisplayName = "Register: agegroup full, no waitlists → returns Success = false with message")]
    public async Task Register_AgegroupFull_NoWaitlists_ReturnsFailure()
    {
        // Arrange — bypass per-club check, placement throws (agegroup full, no waitlists)
        var (svc, teamRepo, placement, _) = CreateService(maxTeamsPerClub: 0, currentClubTeamCount: 0);

        placement.Reset();
        placement
            .Setup(p => p.ResolvePlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("This age group is full"));

        // Act
        var result = await svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        // Assert — club rep gets a clear message, no team created
        result.Success.Should().BeFalse("agegroup is at capacity with no waitlists");
        result.Message.Should().Contain("age group is full");
        result.TeamId.Should().Be(Guid.Empty);

        // Verify no team was persisted
        teamRepo.Verify(t => t.Add(It.IsAny<Teams>()), Times.Never,
            "should not create a team when the agegroup is full");
    }

    [Fact(DisplayName = "Register: agegroup full, waitlisted → Success = true, IsWaitlisted = true")]
    public async Task Register_AgegroupFull_Waitlisted_ReturnsSuccessWithWaitlist()
    {
        // Arrange — bypass per-club check, placement redirects to waitlist agegroup
        var waitlistAgegroupId = Guid.NewGuid();
        var (svc, teamRepo, placement, _) = CreateService(maxTeamsPerClub: 0, currentClubTeamCount: 0);

        placement.Reset();
        placement
            .Setup(p => p.ResolvePlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamPlacementResult
            {
                AgegroupId = waitlistAgegroupId,
                LeagueId = TestLeagueId,
                IsWaitlisted = true,
                WaitlistAgegroupName = "WAITLIST - Boys U14",
            });

        // Act
        var result = await svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        // Assert — club rep is told they're waitlisted, team IS created
        result.Success.Should().BeTrue("registration should succeed on the waitlist");
        result.IsWaitlisted.Should().BeTrue("team was redirected to waitlist agegroup");
        result.WaitlistAgegroupName.Should().Be("WAITLIST - Boys U14");

        // Verify team was created on the waitlist agegroup
        teamRepo.Verify(t => t.Add(It.Is<Teams>(team =>
            team.AgegroupId == waitlistAgegroupId)),
            Times.Once, "team should be created on the waitlist agegroup");
    }
}
