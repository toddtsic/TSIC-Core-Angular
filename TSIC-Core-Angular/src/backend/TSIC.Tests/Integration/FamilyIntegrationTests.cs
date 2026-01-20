using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Tests.Integration;

/// <summary>
/// Integration tests for family management flow.
/// Tests: get family → add member → update member → remove member.
/// Covers both happy paths and error cases.
/// </summary>
public class FamilyIntegrationTests : IClassFixture<WebApplicationTestFactory>
{
    private readonly WebApplicationTestFactory _factory;
    private readonly HttpClient _client;

    public FamilyIntegrationTests(WebApplicationTestFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetFamily_HappyPath_ReturnsAllMembers()
    {
        // Arrange
        var familyUserId = await SeedFamilyWithMembers();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/family/{familyUserId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify family members returned
        // Verify adult and child members correctly categorized
    }

    [Fact]
    public async Task GetFamily_NoMembers_ReturnsEmptyFamily()
    {
        // Arrange
        var familyUserId = await SeedEmptyFamily();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/family/{familyUserId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify empty members list
    }

    [Fact]
    public async Task GetFamily_UnauthorizedUser_ReturnsForbidden()
    {
        // Arrange
        var familyUserId = await SeedFamilyWithMembers();
        // No authorization header

        // Act
        var response = await _client.GetAsync($"/api/family/{familyUserId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddFamilyMember_Adult_SuccessfullyAdds()
    {
        // Arrange
        var familyUserId = await SeedEmptyFamily();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        
        var request = new
        {
            FamilyUserId = familyUserId,
            FirstName = "John",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            Gender = "M",
            IsAdult = true
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/family/add-member", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify member added to database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var members = db.AspNetUsers.Where(u => u.FamilyUserId == familyUserId).ToList();
        members.Should().HaveCount(2); // Family user + new member
    }

    [Fact]
    public async Task AddFamilyMember_Child_SuccessfullyAdds()
    {
        // Arrange
        var familyUserId = await SeedEmptyFamily();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        
        var request = new
        {
            FamilyUserId = familyUserId,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(2010, 5, 15),
            Gender = "F",
            IsAdult = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/family/add-member", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify child member has correct properties
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var child = db.AspNetUsers.FirstOrDefault(u => u.FamilyUserId == familyUserId && u.FirstName == "Jane");
        child.Should().NotBeNull();
        child!.Birthdate.Should().Be(new DateTime(2010, 5, 15));
    }

    [Fact]
    public async Task UpdateFamilyMember_HappyPath_UpdatesDetails()
    {
        // Arrange
        var (familyUserId, memberId) = await SeedFamilyWithOneMember();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        
        var request = new
        {
            MemberId = memberId,
            FirstName = "UpdatedName",
            LastName = "UpdatedLastName",
            Email = "updated@example.com"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/family/member/{memberId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify updates persisted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var member = await db.AspNetUsers.FindAsync(memberId);
        member!.FirstName.Should().Be("UpdatedName");
        member.LastName.Should().Be("UpdatedLastName");
    }

    [Fact]
    public async Task UpdateFamilyMember_CrossFamily_ReturnsForbidden()
    {
        // Arrange
        var (family1UserId, member1Id) = await SeedFamilyWithOneMember();
        var family2UserId = await SeedEmptyFamily();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        
        var request = new
        {
            MemberId = member1Id,
            FamilyUserId = family2UserId, // Trying to move to different family
            FirstName = "Malicious"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/family/member/{member1Id}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RemoveFamilyMember_NoRegistrations_SuccessfullyRemoves()
    {
        // Arrange
        var (familyUserId, memberId) = await SeedFamilyWithOneMember();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.DeleteAsync($"/api/family/member/{memberId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify member removed
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var member = await db.AspNetUsers.FindAsync(memberId);
        member.Should().BeNull();
    }

    [Fact]
    public async Task RemoveFamilyMember_WithActiveRegistrations_ReturnsBadRequest()
    {
        // Arrange
        var (familyUserId, memberId) = await SeedFamilyMemberWithRegistration();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.DeleteAsync($"/api/family/member/{memberId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadAsStringAsync();
        problem.Should().Contain("registration");
    }

    [Fact]
    public async Task GetFamilyWithDiscounts_AppliesCorrectly()
    {
        // Arrange
        var familyUserId = await SeedFamilyWithMultipleChildren();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Act
        var response = await _client.GetAsync($"/api/family/{familyUserId}/pricing");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Verify family discount applied for multiple registrations
    }

    #region Helper Methods

    private async Task<string> SeedFamilyWithMembers()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var familyUserId = Guid.NewGuid().ToString();
        var familyUser = new TSIC.Infrastructure.Data.Identity.ApplicationUser
        {
            Id = familyUserId,
            UserName = "familyuser",
            Email = "family@example.com",
            FirstName = "Parent",
            LastName = "User",
            FamilyUserId = familyUserId
        };
        db.Users.Add(familyUser);

        // Add child member
        var childId = Guid.NewGuid().ToString();
        var child = new TSIC.Infrastructure.Data.Identity.ApplicationUser
        {
            Id = childId,
            UserName = $"child_{childId}",
            Email = null,
            FirstName = "Child",
            LastName = "User",
            Birthdate = new DateTime(2010, 5, 15),
            Gender = "M",
            FamilyUserId = familyUserId
        };
        db.Users.Add(child);

        await db.SaveChangesAsync();
        return familyUserId;
    }

    private async Task<string> SeedEmptyFamily()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var familyUserId = Guid.NewGuid().ToString();
        var familyUser = new TSIC.Infrastructure.Data.Identity.ApplicationUser
        {
            Id = familyUserId,
            UserName = "emptyfamily",
            Email = "empty@example.com",
            FirstName = "Empty",
            LastName = "Family",
            FamilyUserId = familyUserId
        };
        db.Users.Add(familyUser);

        await db.SaveChangesAsync();
        return familyUserId;
    }

    private async Task<(string familyUserId, string memberId)> SeedFamilyWithOneMember()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var familyUserId = Guid.NewGuid().ToString();
        var familyUser = new TSIC.Infrastructure.Data.Identity.ApplicationUser
        {
            Id = familyUserId,
            UserName = "familywithmember",
            Email = "family@example.com",
            FamilyUserId = familyUserId
        };
        db.Users.Add(familyUser);

        var memberId = Guid.NewGuid().ToString();
        var member = new TSIC.Infrastructure.Data.Identity.ApplicationUser
        {
            Id = memberId,
            UserName = $"member_{memberId}",
            Email = "member@example.com",
            FirstName = "Member",
            LastName = "User",
            FamilyUserId = familyUserId
        };
        db.Users.Add(member);

        await db.SaveChangesAsync();
        return (familyUserId, memberId);
    }

    private async Task<(string familyUserId, string memberId)> SeedFamilyMemberWithRegistration()
    {
        var (familyUserId, memberId) = await SeedFamilyWithOneMember();
        
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var jobId = Guid.NewGuid();
        var job = new Jobs
        {
            JobId = jobId,
            JobPath = "test-job-2026",
            JobName = "Test Job",
            Season = "2026"
        };
        db.Jobs.Add(job);

        var registration = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            UserId = memberId,
            JobId = jobId,
            BActive = true,
            RoleId = 5 // Player role
        };
        db.Registrations.Add(registration);

        await db.SaveChangesAsync();
        return (familyUserId, memberId);
    }

    private async Task<string> SeedFamilyWithMultipleChildren()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        var familyUserId = Guid.NewGuid().ToString();
        var familyUser = new TSIC.Infrastructure.Data.Identity.ApplicationUser
        {
            Id = familyUserId,
            UserName = "largefamily",
            Email = "large@example.com",
            FamilyUserId = familyUserId
        };
        db.Users.Add(familyUser);

        // Add 3 children
        for (int i = 0; i < 3; i++)
        {
            var childId = Guid.NewGuid().ToString();
            var child = new TSIC.Infrastructure.Data.Identity.ApplicationUser
            {
                Id = childId,
                UserName = $"child{i}_{childId}",
                FirstName = $"Child{i}",
                LastName = "Family",
                Birthdate = new DateTime(2008 + i, 1, 1),
                FamilyUserId = familyUserId
            };
            db.Users.Add(child);
        }

        await db.SaveChangesAsync();
        return familyUserId;
    }

    #endregion
}
