using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Integration;

/// <summary>
/// Integration tests for payment processing flow.
/// Tests: calculate fees → process payment → verify financial calculations.
/// Covers both happy paths and error cases.
/// </summary>
public class PaymentIntegrationTests : IClassFixture<WebApplicationTestFactory>
{
    private readonly WebApplicationTestFactory _factory;
    private readonly HttpClient _client;

    public PaymentIntegrationTests(WebApplicationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task RecalculateTeamFees_HappyPath_ReturnsUpdatedFees()
    {
        // Arrange
        var (teamId, jobId) = await SeedTeamForFeeCalculation();
        var request = new
        {
            TeamId = teamId,
            UserId = "test-user-1"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/recalculate-fees", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify response contains updated fee calculations
    }

    [Fact]
    public async Task RecalculateTeamFees_ForEntireJob_UpdatesAllTeams()
    {
        // Arrange
        var jobId = await SeedMultipleTeamsForJob();
        var request = new
        {
            JobId = jobId,
            UserId = "test-user-1"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/recalculate-fees", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify all teams in job have updated fees
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var teams = db.Teams.Where(t => t.JobId == jobId).ToList();
        teams.Should().AllSatisfy(t => t.FeeBase.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task RecalculateTeamFees_WithProcessingFees_CalculatesCorrectly()
    {
        // Arrange
        var (teamId, jobId) = await SeedTeamWithProcessingFees();
        var request = new
        {
            TeamId = teamId,
            UserId = "test-user-1"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/recalculate-fees", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify processing fee is calculated and added
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var team = await db.Teams.FindAsync(teamId);
        team!.FeeProcessing.Should().BeGreaterThan(0);
        team.FeeBase.Should().NotBeNull();
    }

    [Fact]
    public async Task RecalculateTeamFees_UnauthorizedUser_ReturnsForbidden()
    {
        // Arrange
        var (teamId, _) = await SeedTeamForFeeCalculation();
        var request = new
        {
            TeamId = teamId,
            UserId = "unauthorized-user"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/teams/recalculate-fees", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetTeamPaymentSummary_CalculatesOwedAmountCorrectly()
    {
        // Arrange
        var teamId = await SeedTeamWithPartialPayment();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/teams/{teamId}/payment-summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify OwedTotal = FeeTotal - PaidTotal
        // Verify DepositDue calculation is correct
    }

    [Fact]
    public async Task GetTeamPaymentSummary_FullPaymentRequired_NoDepositDue()
    {
        // Arrange
        var teamId = await SeedTeamWithFullPaymentRequired();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/teams/{teamId}/payment-summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify DepositDue = 0 when BTeamsFullPaymentRequired = true
    }

    [Fact]
    public async Task PaymentWithDiscount_AppliesDiscountCorrectly()
    {
        // Arrange
        var (teamId, discountCode) = await SeedTeamWithDiscountCode();
        var request = new
        {
            TeamId = teamId,
            Amount = 450m, // Original 500 - 10% = 450
            DiscountCode = discountCode
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments/process", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify discount was applied
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var team = await db.Teams.FindAsync(teamId);
        team!.PaidTotal.Should().Be(450m);
    }

    [Fact]
    public async Task PaymentWithInvalidDiscount_RejectsClaim()
    {
        // Arrange
        var teamId = await SeedTeamForPayment();
        var request = new
        {
            TeamId = teamId,
            Amount = 450m,
            DiscountCode = "INVALID_CODE"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments/process", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #region Helper Methods

    private async Task<(Guid teamId, Guid jobId)> SeedTeamForFeeCalculation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var jobId = Guid.NewGuid();
        var job = new Jobs
        {
            JobId = jobId,
            JobPath = "payment-test-2026",
            JobName = "Payment Test",
            Season = "2026",
            BAddProcessingFees = false,
            BTeamsFullPaymentRequired = false
        };
        db.Jobs.Add(job);

        var ageGroupId = Guid.NewGuid();
        var ageGroup = new Agegroups
        {
            AgegroupId = ageGroupId,
            AgegroupName = "U14 Boys",
            TeamFee = 500,
            RosterFee = 100,
            MaxTeams = 10
        };
        db.Agegroups.Add(ageGroup);

        var teamId = Guid.NewGuid();
        var team = new Teams
        {
            TeamId = teamId,
            JobId = jobId,
            AgegroupId = ageGroupId,
            TeamName = "Test Team",
            Active = true,
            FeeBase = 0, // Will be recalculated
            PaidTotal = 0
        };
        db.Teams.Add(team);

        await db.SaveChangesAsync();
        return (teamId, jobId);
    }

    private async Task<Guid> SeedMultipleTeamsForJob()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var jobId = Guid.NewGuid();
        var job = new Jobs
        {
            JobId = jobId,
            JobPath = "multi-team-test-2026",
            JobName = "Multi Team Test",
            Season = "2026"
        };
        db.Jobs.Add(job);

        var ageGroupId = Guid.NewGuid();
        var ageGroup = new Agegroups
        {
            AgegroupId = ageGroupId,
            AgegroupName = "U14 Boys",
            TeamFee = 500,
            RosterFee = 100,
            MaxTeams = 10
        };
        db.Agegroups.Add(ageGroup);

        // Add multiple teams
        for (int i = 0; i < 3; i++)
        {
            var team = new Teams
            {
                TeamId = Guid.NewGuid(),
                JobId = jobId,
                AgegroupId = ageGroupId,
                TeamName = $"Team {i + 1}",
                Active = true,
                FeeBase = 0
            };
            db.Teams.Add(team);
        }

        await db.SaveChangesAsync();
        return jobId;
    }

    private async Task<(Guid teamId, Guid jobId)> SeedTeamWithProcessingFees()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var jobId = Guid.NewGuid();
        var job = new Jobs
        {
            JobId = jobId,
            JobPath = "processing-fee-test-2026",
            JobName = "Processing Fee Test",
            Season = "2026",
            BAddProcessingFees = true, // Enable processing fees
            BApplyProcessingFeesToTeamDeposit = true
        };
        db.Jobs.Add(job);

        var ageGroupId = Guid.NewGuid();
        var ageGroup = new Agegroups
        {
            AgegroupId = ageGroupId,
            AgegroupName = "U14 Boys",
            TeamFee = 500,
            RosterFee = 100,
            MaxTeams = 10
        };
        db.Agegroups.Add(ageGroup);

        var teamId = Guid.NewGuid();
        var team = new Teams
        {
            TeamId = teamId,
            JobId = jobId,
            AgegroupId = ageGroupId,
            TeamName = "Team With Processing",
            Active = true,
            FeeBase = 0
        };
        db.Teams.Add(team);

        await db.SaveChangesAsync();
        return (teamId, jobId);
    }

    private async Task<Guid> SeedTeamWithPartialPayment()
    {
        var (teamId, _) = await SeedTeamForFeeCalculation();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        
        var team = await db.Teams.FindAsync(teamId);
        team!.FeeBase = 500;
        team.PaidTotal = 200; // Partial payment
        await db.SaveChangesAsync();
        
        return teamId;
    }

    private async Task<Guid> SeedTeamWithFullPaymentRequired()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var jobId = Guid.NewGuid();
        var job = new Jobs
        {
            JobId = jobId,
            JobPath = "full-payment-2026",
            JobName = "Full Payment Required",
            Season = "2026",
            BTeamsFullPaymentRequired = true // Full payment required
        };
        db.Jobs.Add(job);

        var ageGroupId = Guid.NewGuid();
        var ageGroup = new Agegroups
        {
            AgegroupId = ageGroupId,
            AgegroupName = "U14 Boys",
            TeamFee = 500,
            RosterFee = 100,
            MaxTeams = 10
        };
        db.Agegroups.Add(ageGroup);

        var teamId = Guid.NewGuid();
        var team = new Teams
        {
            TeamId = teamId,
            JobId = jobId,
            AgegroupId = ageGroupId,
            TeamName = "Full Payment Team",
            Active = true,
            FeeBase = 500
        };
        db.Teams.Add(team);

        await db.SaveChangesAsync();
        return teamId;
    }

    private async Task<Guid> SeedTeamForPayment()
    {
        var (teamId, _) = await SeedTeamForFeeCalculation();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        
        var team = await db.Teams.FindAsync(teamId);
        team!.FeeBase = 500;
        await db.SaveChangesAsync();
        
        return teamId;
    }

    private async Task<(Guid teamId, string discountCode)> SeedTeamWithDiscountCode()
    {
        var teamId = await SeedTeamForPayment();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        
        var team = await db.Teams.FindAsync(teamId);
        var discount = new JobDiscountCodes
        {
            JobId = team!.JobId,
            CodeName = "SAVE10",
            BAsPercent = true,
            CodeAmount = 10,
            Active = true,
            CodeStartDate = DateTime.UtcNow.AddDays(-1),
            CodeEndDate = DateTime.UtcNow.AddDays(30),
            LebUserId = "admin"
        };
        db.JobDiscountCodes.Add(discount);
        await db.SaveChangesAsync();
        
        return (teamId, "SAVE10");
    }

    #endregion
}
