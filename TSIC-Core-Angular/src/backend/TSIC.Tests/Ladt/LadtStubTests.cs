using FluentAssertions;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Ladt;

/// <summary>
/// Tests for LADT stub-creation methods (AddStubAgegroup, AddStubDivision, AddStubTeam).
/// Verifies each method creates the correct entity type at the correct tree level.
///
/// Naming convention: {MethodName}_{Scenario}_{ExpectedOutcome}
/// </summary>
public class LadtStubTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid LeagueId = Guid.NewGuid();
    private static readonly Guid AgegroupId = Guid.NewGuid();
    private static readonly Guid DivId = Guid.NewGuid();
    private const string UserId = "test-user";

    private readonly Mock<ILeagueRepository> _leagueRepo = new();
    private readonly Mock<IAgeGroupRepository> _agegroupRepo = new();
    private readonly Mock<IDivisionRepository> _divisionRepo = new();
    private readonly Mock<ITeamRepository> _teamRepo = new();
    private readonly Mock<IRegistrationRepository> _registrationRepo = new();
    private readonly Mock<IRegistrationAccountingRepository> _regAcctRepo = new();
    private readonly Mock<IJobRepository> _jobRepo = new();
    private readonly Mock<IFeeResolutionService> _feeService = new();
    private readonly Mock<IClubTeamRepository> _clubTeamRepo = new();
    private readonly Mock<IClubRepository> _clubRepo = new();
    private readonly Mock<IScheduleRepository> _scheduleRepo = new();
    private readonly Mock<ITeamPlacementService> _placement = new();

    private LadtService CreateService() => new(
        _leagueRepo.Object,
        _agegroupRepo.Object,
        _divisionRepo.Object,
        _teamRepo.Object,
        _registrationRepo.Object,
        _regAcctRepo.Object,
        _jobRepo.Object,
        _feeService.Object,
        _clubTeamRepo.Object,
        _clubRepo.Object,
        _scheduleRepo.Object,
        _placement.Object
    );

    // ─── AddStubAgegroup ──────────────────────────────────────────────────

    [Fact]
    public async Task AddStubAgegroup_ValidLeague_CreatesAgegroupEntity()
    {
        // Arrange
        _leagueRepo.Setup(r => r.BelongsToJobAsync(LeagueId, JobId, default)).ReturnsAsync(true);
        _jobRepo.Setup(r => r.GetJobSeasonYearAsync(JobId, default))
            .ReturnsAsync(new JobSeasonYear { Season = "Spring", Year = "2026" });

        Agegroups? capturedAgegroup = null;
        _agegroupRepo.Setup(r => r.Add(It.IsAny<Agegroups>()))
            .Callback<Agegroups>(ag => capturedAgegroup = ag);

        var svc = CreateService();

        // Act
        var result = await svc.AddStubAgegroupAsync(LeagueId, JobId, UserId, "U10 Boys");

        // Assert
        result.Should().NotBeEmpty("should return the new agegroup ID");
        capturedAgegroup.Should().NotBeNull("an Agegroups entity must be added");
        capturedAgegroup!.LeagueId.Should().Be(LeagueId);
        capturedAgegroup.AgegroupName.Should().Be("U10 Boys");
        capturedAgegroup.Season.Should().Be("Spring");
    }

    [Fact]
    public async Task AddStubAgegroup_NoName_DefaultsToNewAgeGroup()
    {
        // Arrange
        _leagueRepo.Setup(r => r.BelongsToJobAsync(LeagueId, JobId, default)).ReturnsAsync(true);
        _jobRepo.Setup(r => r.GetJobSeasonYearAsync(JobId, default))
            .ReturnsAsync(new JobSeasonYear { Season = "Fall", Year = "2026" });

        Agegroups? capturedAgegroup = null;
        _agegroupRepo.Setup(r => r.Add(It.IsAny<Agegroups>()))
            .Callback<Agegroups>(ag => capturedAgegroup = ag);

        var svc = CreateService();

        // Act
        await svc.AddStubAgegroupAsync(LeagueId, JobId, UserId);

        // Assert
        capturedAgegroup.Should().NotBeNull();
        capturedAgegroup!.AgegroupName.Should().Be("New Age Group");
    }

    [Fact]
    public async Task AddStubAgegroup_AutoCreatesUnassignedDivision()
    {
        // Arrange
        _leagueRepo.Setup(r => r.BelongsToJobAsync(LeagueId, JobId, default)).ReturnsAsync(true);
        _jobRepo.Setup(r => r.GetJobSeasonYearAsync(JobId, default))
            .ReturnsAsync(new JobSeasonYear { Season = "Spring", Year = "2026" });

        Agegroups? capturedAgegroup = null;
        _agegroupRepo.Setup(r => r.Add(It.IsAny<Agegroups>()))
            .Callback<Agegroups>(ag => capturedAgegroup = ag);

        Divisions? capturedDivision = null;
        _divisionRepo.Setup(r => r.Add(It.IsAny<Divisions>()))
            .Callback<Divisions>(d => capturedDivision = d);

        var svc = CreateService();

        // Act
        await svc.AddStubAgegroupAsync(LeagueId, JobId, UserId, "U12 Girls");

        // Assert
        capturedDivision.Should().NotBeNull("an Unassigned division must be auto-created");
        capturedDivision!.DivName.Should().Be("Unassigned");
        capturedDivision.AgegroupId.Should().Be(capturedAgegroup!.AgegroupId,
            "the stub division must belong to the newly created agegroup");
    }

    [Fact]
    public async Task AddStubAgegroup_WrongJob_ThrowsInvalidOperation()
    {
        // Arrange — league does NOT belong to job
        _leagueRepo.Setup(r => r.BelongsToJobAsync(LeagueId, JobId, default)).ReturnsAsync(false);

        var svc = CreateService();

        // Act & Assert
        var act = () => svc.AddStubAgegroupAsync(LeagueId, JobId, UserId, "U10");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*League*does not belong*");
    }

    // ─── AddStubDivision ──────────────────────────────────────────────────

    [Fact]
    public async Task AddStubDivision_ValidAgegroup_CreatesDivisionEntity()
    {
        // Arrange
        _agegroupRepo.Setup(r => r.BelongsToJobAsync(AgegroupId, JobId, default)).ReturnsAsync(true);

        Divisions? capturedDivision = null;
        _divisionRepo.Setup(r => r.Add(It.IsAny<Divisions>()))
            .Callback<Divisions>(d => capturedDivision = d);

        var svc = CreateService();

        // Act
        var result = await svc.AddStubDivisionAsync(AgegroupId, JobId, UserId, "Gold");

        // Assert
        result.Should().NotBeEmpty("should return the new division ID");
        capturedDivision.Should().NotBeNull("a Divisions entity must be added");
        capturedDivision!.AgegroupId.Should().Be(AgegroupId);
        capturedDivision.DivName.Should().Be("Gold");
    }

    [Fact]
    public async Task AddStubDivision_NoName_AutoNamesPoolA()
    {
        // Arrange
        _agegroupRepo.Setup(r => r.BelongsToJobAsync(AgegroupId, JobId, default)).ReturnsAsync(true);
        _divisionRepo.Setup(r => r.GetByAgegroupIdAsync(AgegroupId, default))
            .ReturnsAsync(new List<Divisions>()); // no existing divisions

        Divisions? capturedDivision = null;
        _divisionRepo.Setup(r => r.Add(It.IsAny<Divisions>()))
            .Callback<Divisions>(d => capturedDivision = d);

        var svc = CreateService();

        // Act
        await svc.AddStubDivisionAsync(AgegroupId, JobId, UserId);

        // Assert
        capturedDivision.Should().NotBeNull();
        capturedDivision!.DivName.Should().Be("Pool A");
    }

    [Fact]
    public async Task AddStubDivision_PoolAExists_AutoNamesPoolB()
    {
        // Arrange
        _agegroupRepo.Setup(r => r.BelongsToJobAsync(AgegroupId, JobId, default)).ReturnsAsync(true);
        _divisionRepo.Setup(r => r.GetByAgegroupIdAsync(AgegroupId, default))
            .ReturnsAsync(new List<Divisions>
            {
                new() { DivId = Guid.NewGuid(), AgegroupId = AgegroupId, DivName = "Pool A" }
            });

        Divisions? capturedDivision = null;
        _divisionRepo.Setup(r => r.Add(It.IsAny<Divisions>()))
            .Callback<Divisions>(d => capturedDivision = d);

        var svc = CreateService();

        // Act
        await svc.AddStubDivisionAsync(AgegroupId, JobId, UserId);

        // Assert
        capturedDivision.Should().NotBeNull();
        capturedDivision!.DivName.Should().Be("Pool B",
            "should skip 'Pool A' since it already exists and use 'Pool B'");
    }

    [Fact]
    public async Task AddStubDivision_WrongJob_ThrowsInvalidOperation()
    {
        // Arrange — agegroup does NOT belong to job
        _agegroupRepo.Setup(r => r.BelongsToJobAsync(AgegroupId, JobId, default)).ReturnsAsync(false);

        var svc = CreateService();

        // Act & Assert
        var act = () => svc.AddStubDivisionAsync(AgegroupId, JobId, UserId, "Gold");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Agegroup*does not belong*");
    }

    // ─── AddStubTeam ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddStubTeam_ValidDivision_CreatesTeamEntity()
    {
        // Arrange
        _divisionRepo.Setup(r => r.BelongsToJobAsync(DivId, JobId, default)).ReturnsAsync(true);
        _divisionRepo.Setup(r => r.GetByIdReadOnlyAsync(DivId, default))
            .ReturnsAsync(new Divisions { DivId = DivId, AgegroupId = AgegroupId, DivName = "Pool A" });
        _agegroupRepo.Setup(r => r.GetByIdAsync(AgegroupId, default))
            .ReturnsAsync(new Agegroups { AgegroupId = AgegroupId, LeagueId = LeagueId });
        _placement.Setup(p => p.ResolvePlacementAsync(
                JobId, AgegroupId, "Thunder", "Pool A", UserId, true, default))
            .ReturnsAsync(new TeamPlacementResult
            {
                AgegroupId = AgegroupId,
                LeagueId = LeagueId,
                DivisionId = DivId,
                IsWaitlisted = false
            });
        _teamRepo.Setup(r => r.GetNextDivRankAsync(DivId, default)).ReturnsAsync(1);
        _jobRepo.Setup(r => r.GetJobSeasonYearAsync(JobId, default))
            .ReturnsAsync(new JobSeasonYear { Season = "Spring", Year = "2026" });

        TSIC.Domain.Entities.Teams? capturedTeam = null;
        _teamRepo.Setup(r => r.Add(It.IsAny<TSIC.Domain.Entities.Teams>()))
            .Callback<TSIC.Domain.Entities.Teams>(t => capturedTeam = t);

        var svc = CreateService();

        // Act
        var result = await svc.AddStubTeamAsync(DivId, JobId, UserId, "Thunder");

        // Assert
        result.Should().NotBeEmpty("should return the new team ID");
        capturedTeam.Should().NotBeNull("a Teams entity must be added");
        capturedTeam!.DivId.Should().Be(DivId);
        capturedTeam.AgegroupId.Should().Be(AgegroupId);
        capturedTeam.TeamName.Should().Be("Thunder");
        capturedTeam.Active.Should().BeTrue();
        capturedTeam.Season.Should().Be("Spring");
        capturedTeam.Year.Should().Be("2026");
    }

    [Fact]
    public async Task AddStubTeam_NoName_DefaultsToNewTeam()
    {
        // Arrange
        _divisionRepo.Setup(r => r.BelongsToJobAsync(DivId, JobId, default)).ReturnsAsync(true);
        _divisionRepo.Setup(r => r.GetByIdReadOnlyAsync(DivId, default))
            .ReturnsAsync(new Divisions { DivId = DivId, AgegroupId = AgegroupId, DivName = "Pool A" });
        _agegroupRepo.Setup(r => r.GetByIdAsync(AgegroupId, default))
            .ReturnsAsync(new Agegroups { AgegroupId = AgegroupId, LeagueId = LeagueId });
        _placement.Setup(p => p.ResolvePlacementAsync(
                JobId, AgegroupId, "New Team", "Pool A", UserId, true, default))
            .ReturnsAsync(new TeamPlacementResult
            {
                AgegroupId = AgegroupId,
                LeagueId = LeagueId,
                DivisionId = DivId,
                IsWaitlisted = false
            });
        _teamRepo.Setup(r => r.GetNextDivRankAsync(DivId, default)).ReturnsAsync(1);
        _jobRepo.Setup(r => r.GetJobSeasonYearAsync(JobId, default))
            .ReturnsAsync(new JobSeasonYear { Season = "Fall", Year = "2026" });

        TSIC.Domain.Entities.Teams? capturedTeam = null;
        _teamRepo.Setup(r => r.Add(It.IsAny<TSIC.Domain.Entities.Teams>()))
            .Callback<TSIC.Domain.Entities.Teams>(t => capturedTeam = t);

        var svc = CreateService();

        // Act
        await svc.AddStubTeamAsync(DivId, JobId, UserId);

        // Assert
        capturedTeam.Should().NotBeNull();
        capturedTeam!.TeamName.Should().Be("New Team");
    }

    [Fact]
    public async Task AddStubTeam_WrongJob_ThrowsInvalidOperation()
    {
        // Arrange — division does NOT belong to job
        _divisionRepo.Setup(r => r.BelongsToJobAsync(DivId, JobId, default)).ReturnsAsync(false);

        var svc = CreateService();

        // Act & Assert
        var act = () => svc.AddStubTeamAsync(DivId, JobId, UserId, "Thunder");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Division*does not belong*");
    }
}
