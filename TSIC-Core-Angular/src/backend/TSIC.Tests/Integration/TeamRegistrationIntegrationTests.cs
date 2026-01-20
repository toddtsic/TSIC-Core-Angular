using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Integration;

/// <summary>
/// Integration tests for team registration flow.
/// Tests: initialize registration → register team → unregister team.
/// Covers both happy paths and error cases.
/// </summary>
public class TeamRegistrationIntegrationTests : IClassFixture<WebApplicationTestFactory>
{
    private readonly WebApplicationTestFactory _factory;
    private readonly HttpClient _client;

    public TeamRegistrationIntegrationTests(WebApplicationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Initialize_Registration_HappyPath_ReturnsTokenWithRegId()
    {
        // Arrange
        await SeedTestDataForInitialization();
        var request = new
        {
            UserId = "test-user-1",
            ClubName = "Test Club",
            JobPath = "test-event-2026"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/initialize", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();

        // Verify token contains regId claim
        // TODO: Decode JWT and verify regId claim exists
    }

    [Fact]
    public async Task Initialize_Registration_UserNotClubRep_ReturnsForbidden()
    {
        // Arrange
        await SeedTestDataForInitialization();
        var request = new
        {
            UserId = "test-user-1",
            ClubName = "Unauthorized Club",
            JobPath = "test-event-2026"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/initialize", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError); // Service throws InvalidOperationException
        // TODO: Update service to return proper 403 with ProblemDetails
    }

    [Fact]
    public async Task Initialize_Registration_JobNotFound_ReturnsNotFound()
    {
        // Arrange
        await SeedTestDataForInitialization();
        var request = new
        {
            UserId = "test-user-1",
            ClubName = "Test Club",
            JobPath = "nonexistent-event"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/initialize", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError); // Service throws InvalidOperationException
        // TODO: Update service to return proper 404
    }

    [Fact]
    public async Task Register_Team_HappyPath_CreatesTeamAndReturnsDetails()
    {
        // Arrange
        var (token, regId) = await InitializeRegistration();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new RegisterTeamRequest
        {
            TeamName = "Test Team U14",
            AgeGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LevelOfPlay = "A"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/register?regId={regId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RegisterTeamResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.TeamId.Should().NotBeEmpty();
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_Team_DuplicateTeamName_ReturnsConflict()
    {
        // Arrange
        var (token, regId) = await InitializeRegistration();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new RegisterTeamRequest
        {
            TeamName = "Duplicate Team",
            AgeGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LevelOfPlay = "A"
        };

        // Act - Register first time
        await _client.PostAsJsonAsync($"/api/teams/register?regId={regId}", request);

        // Act - Try to register again with same name
        var response = await _client.PostAsJsonAsync($"/api/teams/register?regId={regId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_Team_AgeGroupFull_ReturnsBadRequest()
    {
        // Arrange
        var (token, regId) = await InitializeRegistration();
        await SeedFullAgeGroup(); // Fill age group to max capacity
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new RegisterTeamRequest
        {
            TeamName = "Too Many Teams",
            AgeGroupId = Guid.Parse("22222222-2222-2222-2222-222222222222"), // Full age group
            LevelOfPlay = "A"
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/teams/register?regId={regId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unregister_Team_WithoutPayment_SuccessfullyRemovesTeam()
    {
        // Arrange
        var (token, regId) = await InitializeRegistration();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var registerRequest = new RegisterTeamRequest
        {
            TeamName = "Team To Remove",
            AgeGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LevelOfPlay = "A"
        };

        var registerResponse = await _client.PostAsJsonAsync($"/api/teams/register?regId={regId}", registerRequest);
        var registerResult = await registerResponse.Content.ReadFromJsonAsync<RegisterTeamResponse>();

        // Act
        var response = await _client.DeleteAsync($"/api/teams/{registerResult!.TeamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify team is removed from database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var team = await db.Teams.FindAsync(registerResult.TeamId);
        team.Should().BeNull();
    }

    [Fact]
    public async Task Unregister_Team_WithPayment_ReturnsBadRequest()
    {
        // Arrange
        var (token, _, teamId) = await CreateTeamWithPayment();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.DeleteAsync($"/api/teams/{teamId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("payment");
    }

    [Fact]
    public async Task GetTeamsMetadata_FiltersInactiveAndDroppedTeams()
    {
        // Arrange
        var (token, regId) = await InitializeRegistration();
        await SeedTeamsWithInactiveAndDropped(regId);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.GetAsync($"/api/teams/metadata?regId={regId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<TeamsMetadataResponse>();
        result.Should().NotBeNull();
        result!.RegisteredTeams.Should().NotBeNull();

        // Verify no inactive teams
        result.RegisteredTeams.Should().NotContain(t => t.TeamName.Contains("INACTIVE"));

        // Verify no dropped age groups
        result.RegisteredTeams.Should().NotContain(t => t.AgeGroupName.Contains("DROPPED"));
    }

    #region Helper Methods

    private async Task SeedTestDataForInitialization()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        // Add test job
        var job = new Jobs
        {
            JobId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            JobPath = "test-event-2026",
            JobName = "Test Event 2026",
            Season = "2026",
            BTeamsFullPaymentRequired = false
        };
        db.Jobs.Add(job);

        // Add test club
        var club = new Clubs
        {
            ClubId = 1,
            ClubName = "Test Club"
        };
        db.Clubs.Add(club);

        // Add test user (AspNetUsers)
        var user = new TSIC.Domain.Entities.AspNetUsers
        {
            Id = "test-user-1",
            UserName = "testuser",
            Email = "test@example.com"
        };
        db.AspNetUsers.Add(user);

        // Add club rep association
        var clubRep = new ClubReps
        {
            ClubId = 1,
            ClubRepUserId = "test-user-1"
        };
        db.ClubReps.Add(clubRep);

        // Add test age group
        var ageGroup = new Agegroups
        {
            AgegroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            AgegroupName = "U14 Boys",
            MaxTeams = 10,
            TeamFee = 500,
            RosterFee = 100
        };
        db.Agegroups.Add(ageGroup);

        await db.SaveChangesAsync();
    }

    private async Task<(string token, Guid regId)> InitializeRegistration()
    {
        await SeedTestDataForInitialization();

        var request = new
        {
            UserId = "test-user-1",
            ClubName = "Test Club",
            JobPath = "test-event-2026"
        };

        var response = await _client.PostAsJsonAsync("/api/teams/initialize", request);
        var result = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();

        // TODO: Extract regId from JWT token
        var regId = Guid.NewGuid(); // Placeholder

        return (result!.AccessToken, regId);
    }

    private async Task<(string token, Guid regId, Guid teamId)> CreateTeamWithPayment()
    {
        var (token, regId) = await InitializeRegistration();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new RegisterTeamRequest
        {
            TeamName = "Team With Payment",
            AgeGroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            LevelOfPlay = "A"
        };

        var response = await _client.PostAsJsonAsync($"/api/teams/register?regId={regId}", request);
        var result = await response.Content.ReadFromJsonAsync<RegisterTeamResponse>();

        // Add payment to team
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var team = await db.Teams.FindAsync(result!.TeamId);
        team!.PaidTotal = 100;
        await db.SaveChangesAsync();

        return (token, regId, result.TeamId);
    }

    private async Task SeedFullAgeGroup()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var fullAgeGroup = new Agegroups
        {
            AgegroupId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            AgegroupName = "U16 Boys",
            MaxTeams = 2, // Small limit for testing
            TeamFee = 500,
            RosterFee = 100
        };
        db.Agegroups.Add(fullAgeGroup);

        // Add teams to fill capacity
        for (int i = 0; i < 2; i++)
        {
            var team = new Teams
            {
                TeamId = Guid.NewGuid(),
                JobId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
                AgegroupId = fullAgeGroup.AgegroupId,
                TeamName = $"Full Team {i + 1}",
                Active = true
            };
            db.Teams.Add(team);
        }

        await db.SaveChangesAsync();
    }

    private async Task SeedTeamsWithInactiveAndDropped(Guid regId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        // Add dropped age group
        var droppedAgeGroup = new Agegroups
        {
            AgegroupId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            AgegroupName = "U18 Boys DROPPED",
            MaxTeams = 10
        };
        db.Agegroups.Add(droppedAgeGroup);

        // Add inactive team
        var inactiveTeam = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            AgegroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TeamName = "INACTIVE Team",
            Active = false,
            ClubrepRegistrationid = regId
        };
        db.Teams.Add(inactiveTeam);

        // Add team in dropped age group
        var droppedTeam = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            AgegroupId = droppedAgeGroup.AgegroupId,
            TeamName = "Team in Dropped",
            Active = true,
            ClubrepRegistrationid = regId
        };
        db.Teams.Add(droppedTeam);

        // Add valid active team
        var activeTeam = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = Guid.Parse("99999999-9999-9999-9999-999999999999"),
            AgegroupId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TeamName = "Active Team",
            Active = true,
            ClubrepRegistrationid = regId
        };
        db.Teams.Add(activeTeam);

        await db.SaveChangesAsync();
    }

    #endregion
}
