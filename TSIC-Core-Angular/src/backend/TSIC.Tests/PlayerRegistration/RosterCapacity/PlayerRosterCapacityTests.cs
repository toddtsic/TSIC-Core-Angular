using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.VerticalInsure;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.VerticalInsure;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.PlayerRegistration.RosterCapacity;

/// <summary>
/// PLAYER ROSTER CAPACITY TESTS
///
/// Validates that the player registration wizard correctly enforces
/// team roster limits (MaxCount) at the "reserve teams" step.
///
/// What these tests prove:
///   - A team with room accepts the registration (count goes up)
///   - A full team blocks the registration with IsFull = true
///   - A full team with waitlists redirects to the waitlist team
///   - MaxCount = 0 means unlimited (never full)
///
/// Service under test: PlayerRegistrationService.ReserveTeamsAsync()
/// All 9 dependencies are mocked. No database involved.
/// </summary>
public class PlayerRosterCapacityTests
{
    // ── Shared test IDs ──────────────────────────────────────────────

    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly string TestFamilyUserId = "family-user-test";
    private static readonly string TestPlayerId = "player-test-1";

    // ── Factory: builds service with all mocks ───────────────────────

    private static (
        PlayerRegistrationService svc,
        Mock<IRegistrationRepository> regRepo,
        Mock<ITeamRepository> teamRepo,
        Mock<ITeamPlacementService> placement,
        Mock<IFeeResolutionService> feeService)
        CreateService()
    {
        var logger = new Mock<ILogger<PlayerRegistrationService>>();
        var feeService = new Mock<IFeeResolutionService>();
        var verticalInsure = new Mock<IVerticalInsureService>();
        var teamLookup = new Mock<ITeamLookupService>();
        var validation = new Mock<IPlayerFormValidationService>();
        var regRepo = new Mock<IRegistrationRepository>();
        var teamRepo = new Mock<ITeamRepository>();
        var jobRepo = new Mock<IJobRepository>();
        var placement = new Mock<ITeamPlacementService>();

        // Default: validation returns no errors
        validation
            .Setup(v => v.ValidatePlayerFormValues(It.IsAny<string?>(), It.IsAny<List<PreSubmitTeamSelectionDto>>()))
            .Returns(new List<PreSubmitValidationErrorDto>());

        // Default: insurance returns unavailable
        verticalInsure
            .Setup(v => v.BuildOfferAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(new PreSubmitInsuranceDto { Available = false });

        // Default: job metadata returns PP mode (standard player registration)
        jobRepo
            .Setup(j => j.GetPreSubmitMetadataAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobPreSubmitMetadata
            {
                PlayerProfileMetadataJson = null,
                JsonOptions = null,
                CoreRegformPlayer = "PP10",
            });

        // Default: no existing registrations for any player in this family
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());
        regRepo
            .Setup(r => r.GetFamilyRegistrationsForPlayersTrackedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Registrations>());

        // Default: SaveChangesAsync succeeds
        regRepo
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var svc = new PlayerRegistrationService(
            logger.Object,
            feeService.Object,
            verticalInsure.Object,
            teamLookup.Object,
            validation.Object,
            regRepo.Object,
            teamRepo.Object,
            jobRepo.Object,
            placement.Object);

        return (svc, regRepo, teamRepo, placement, feeService);
    }

    // ── Helper: configure a team with a specific roster count ─────────

    private static Teams SetupTeamWithRoster(
        Mock<ITeamRepository> teamRepo,
        Mock<IRegistrationRepository> regRepo,
        int maxCount,
        int currentRosterCount)
    {
        var team = RegistrationDataBuilder.BuildTeam(TestJobId, Guid.NewGuid(), maxCount);

        teamRepo
            .Setup(t => t.GetTeamsForJobAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Teams> { team });

        regRepo
            .Setup(r => r.GetActiveTeamRosterCountsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { team.TeamId, currentRosterCount } });

        return team;
    }

    // ── Helper: build a ReserveTeams request ─────────────────────────

    private static ReserveTeamsRequestDto MakeReserveRequest(Guid teamId)
    {
        return new ReserveTeamsRequestDto
        {
            JobPath = "test-job",
            TeamSelections = new List<ReserveTeamSelectionDto>
            {
                new() { PlayerId = TestPlayerId, TeamId = teamId }
            }
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "Reserve: team has room (9/10) → registration created, IsFull = false")]
    public async Task Reserve_TeamHasRoom_CreatesRegistration()
    {
        // Arrange — team allows 10 players, currently has 9
        var (svc, regRepo, teamRepo, placement, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 9);

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — registration was created, team is not full
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse("team has room for 1 more player");
        result.TeamResults[0].IsWaitlisted.Should().BeFalse("direct placement, not waitlisted");
        result.HasFullTeams.Should().BeFalse();

        // Verify a registration entity was added
        regRepo.Verify(r => r.Add(It.Is<Registrations>(reg =>
            reg.AssignedTeamId == team.TeamId &&
            reg.UserId == TestPlayerId)),
            Times.Once, "should create a pending registration on the selected team");
    }

    [Fact(DisplayName = "Reserve: team full (10/10), no waitlist → IsFull = true, no registration")]
    public async Task Reserve_TeamFull_NoWaitlist_ReturnsIsFull()
    {
        // Arrange — team is at capacity, placement throws (no waitlist)
        var (svc, regRepo, teamRepo, placement, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);

        placement
            .Setup(p => p.ResolveRosterPlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Team roster is full"));

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — user is told the team is full
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeTrue("roster is at capacity");
        result.TeamResults[0].Message.Should().Contain("full");
        result.HasFullTeams.Should().BeTrue();

        // Verify NO registration was created
        regRepo.Verify(r => r.Add(It.IsAny<Registrations>()), Times.Never,
            "should not create a registration when the team is full");
    }

    [Fact(DisplayName = "Reserve: team full (10/10), waitlist enabled → redirected to waitlist team")]
    public async Task Reserve_TeamFull_WithWaitlist_RedirectsToWaitlistTeam()
    {
        // Arrange — team is full, but waitlist exists
        var (svc, regRepo, teamRepo, placement, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);
        var waitlistTeam = RegistrationDataBuilder.BuildTeam(TestJobId, team.AgegroupId, maxCount: 10000);

        // Placement service redirects to the waitlist team
        placement
            .Setup(p => p.ResolveRosterPlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RosterPlacementResult
            {
                TeamId = waitlistTeam.TeamId,
                IsWaitlisted = true,
                WaitlistTeamName = "WAITLIST - Test Team"
            });

        // The service will look up the waitlist team by ID
        teamRepo
            .Setup(t => t.GetTeamFromTeamId(waitlistTeam.TeamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(waitlistTeam);

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — registration was created on the WAITLIST team, not the original
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse("waitlist absorbed the registration");
        result.TeamResults[0].IsWaitlisted.Should().BeTrue("registrant must be told they are on a waitlist");
        result.TeamResults[0].WaitlistTeamName.Should().Be("WAITLIST - Test Team");

        regRepo.Verify(r => r.Add(It.Is<Registrations>(reg =>
            reg.AssignedTeamId == waitlistTeam.TeamId)),
            Times.Once, "registration should be on the waitlist team");
    }

    [Fact(DisplayName = "Reserve: MaxCount = 0 (unlimited) → never full, placement never called")]
    public async Task Reserve_MaxCountZero_NeverFull()
    {
        // Arrange — team has MaxCount = 0 (unlimited), even with 999 players
        var (svc, regRepo, teamRepo, placement, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 0, currentRosterCount: 999);

        // Act
        var result = await svc.ReserveTeamsAsync(TestJobId, TestFamilyUserId, MakeReserveRequest(team.TeamId), TestFamilyUserId);

        // Assert — unlimited teams are never full
        result.TeamResults.Should().HaveCount(1);
        result.TeamResults[0].IsFull.Should().BeFalse("MaxCount = 0 means unlimited");
        result.HasFullTeams.Should().BeFalse();

        // Placement service should never be called (capacity check is skipped entirely)
        placement.Verify(p => p.ResolveRosterPlacementAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "capacity check should be skipped for unlimited teams");
    }

    [Fact(DisplayName = "PreSubmit: team full → NextTab = 'Team' (sends user back to team selection)")]
    public async Task PreSubmit_TeamFull_NextTabIsTeam()
    {
        // Arrange — full team, no waitlist
        var (svc, regRepo, teamRepo, placement, _) = CreateService();
        var team = SetupTeamWithRoster(teamRepo, regRepo, maxCount: 10, currentRosterCount: 10);

        placement
            .Setup(p => p.ResolveRosterPlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Team roster is full"));

        var request = new PreSubmitPlayerRegistrationRequestDto
        {
            JobPath = "test-job",
            TeamSelections = new List<PreSubmitTeamSelectionDto>
            {
                new() { PlayerId = TestPlayerId, TeamId = team.TeamId }
            }
        };

        // Act
        var result = await svc.PreSubmitAsync(TestJobId, TestFamilyUserId, request, TestFamilyUserId);

        // Assert — NextTab directs user back to team selection
        result.NextTab.Should().Be("Team",
            "when a team is full, the wizard should send the user back to choose a different team");
        result.HasFullTeams.Should().BeTrue();
    }
}
