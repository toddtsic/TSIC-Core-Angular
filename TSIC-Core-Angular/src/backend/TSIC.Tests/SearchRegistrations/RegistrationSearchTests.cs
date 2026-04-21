using FluentAssertions;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// REGISTRATION SEARCH TESTS
///
/// These tests validate the SearchAsync method in RegistrationRepository —
/// the query behind the Search/Registrations admin grid.
///
/// Each test seeds an in-memory database, runs a search with specific filters,
/// and verifies the correct registrations are returned with accurate aggregates.
///
/// Filters tested:
///   - No filters (returns all)
///   - Name (single term and "first last" split)
///   - Email
///   - Active status
///   - Pay status (PAID IN FULL, UNDER PAID)
///   - Role ID
///   - Club name
///   - Position
///   - Date range
///   - LADT tree (TeamId, AgegroupId)
///   - Aggregates (TotalFees, TotalPaid, TotalOwed)
///   - Sort order (LastName, FirstName)
/// </summary>
public class RegistrationSearchTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  HELPER: build a standard test scenario
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<(RegistrationRepository repo, Guid jobId, SearchDataBuilder b,
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx)>
        CreateScenarioAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        var repo = new RegistrationRepository(ctx);
        return (repo, job.JobId, b, ctx);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  1. NO FILTERS — returns all registrations for the job
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "No filters →returns all registrations for the job")]
    public async Task NoFilters_ReturnsAllRegistrations()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var playerRole = b.AddRole(RoleConstants.Player, "Player");
        var user1 = b.AddUser("Alice", "Smith");
        var user2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, user1.Id, playerRole.Id, feeTotal: 100m);
        b.AddRegistration(jobId, user2.Id, playerRole.Id, feeTotal: 200m);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest());

        result.Count.Should().Be(2);
        result.Result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "No filters →does not return registrations from other jobs")]
    public async Task NoFilters_ExcludesOtherJobs()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var otherJob = b.AddJob();
        var role = b.AddRole(RoleConstants.Player, "Player");
        var user1 = b.AddUser("Alice", "Smith");
        var user2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, user1.Id, role.Id);
        b.AddRegistration(otherJob.JobId, user2.Id, role.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest());

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  2. NAME FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Name filter (single term) →matches first or last name")]
    public async Task NameFilter_SingleTerm_MatchesFirstOrLast()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        var u3 = b.AddUser("Charlie", "Smithson");
        b.AddRegistration(jobId, u1.Id, role.Id);
        b.AddRegistration(jobId, u2.Id, role.Id);
        b.AddRegistration(jobId, u3.Id, role.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest { Name = "Smith" });

        result.Count.Should().Be(2);
        result.Result.Select(r => r.FirstName).Should().BeEquivalentTo("Alice", "Charlie");
    }

    [Fact(DisplayName = "Name filter (first + last) →matches both parts")]
    public async Task NameFilter_FirstAndLast_MatchesBoth()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Alice", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id);
        b.AddRegistration(jobId, u2.Id, role.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest { Name = "Alice Smith" });

        result.Count.Should().Be(1);
        result.Result.Single().LastName.Should().Be("Smith");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  3. EMAIL FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Email filter →matches partial email")]
    public async Task EmailFilter_MatchesPartial()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith", email: "alice@example.com");
        var u2 = b.AddUser("Bob", "Jones", email: "bob@other.com");
        b.AddRegistration(jobId, u1.Id, role.Id);
        b.AddRegistration(jobId, u2.Id, role.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest { Email = "example.com" });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  4. ACTIVE STATUS FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Active status 'True' →returns only active registrations")]
    public async Task ActiveStatus_True_ReturnsActiveOnly()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, active: true);
        b.AddRegistration(jobId, u2.Id, role.Id, active: false);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            ActiveStatuses = ["True"]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    [Fact(DisplayName = "Active status 'False' →returns only inactive registrations")]
    public async Task ActiveStatus_False_ReturnsInactiveOnly()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, active: true);
        b.AddRegistration(jobId, u2.Id, role.Id, active: false);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            ActiveStatuses = ["False"]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Bob");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  5. PAY STATUS FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Pay status 'PAID IN FULL' →returns registrations with OwedTotal = 0")]
    public async Task PayStatus_PaidInFull_ReturnsZeroOwed()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, feeTotal: 100m, paidTotal: 100m);  // OwedTotal = 0
        b.AddRegistration(jobId, u2.Id, role.Id, feeTotal: 100m, paidTotal: 50m);   // OwedTotal = 50
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            PayStatuses = ["PAID IN FULL"]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    [Fact(DisplayName = "Pay status 'UNDER PAID' →returns registrations with OwedTotal > 0")]
    public async Task PayStatus_UnderPaid_ReturnsPositiveOwed()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, feeTotal: 100m, paidTotal: 100m);
        b.AddRegistration(jobId, u2.Id, role.Id, feeTotal: 100m, paidTotal: 50m);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            PayStatuses = ["UNDER PAID"]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Bob");
        result.Result.Single().OwedTotal.Should().Be(50m);
    }

    [Fact(DisplayName = "Pay status multi-select →OR logic (PAID IN FULL + UNDER PAID)")]
    public async Task PayStatus_MultiSelect_UsesOrLogic()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, feeTotal: 100m, paidTotal: 100m);
        b.AddRegistration(jobId, u2.Id, role.Id, feeTotal: 100m, paidTotal: 50m);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            PayStatuses = ["PAID IN FULL", "UNDER PAID"]
        });

        result.Count.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  6. ROLE ID FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Role filter →returns only matching role")]
    public async Task RoleFilter_ReturnsMatchingRole()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var playerRole = b.AddRole(RoleConstants.Player, "Player");
        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, playerRole.Id);
        b.AddRegistration(jobId, u2.Id, clubRepRole.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            RoleIds = [RoleConstants.Player]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  7. CLUB NAME FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Club name filter →returns matching club registrations")]
    public async Task ClubNameFilter_ReturnsMatching()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, clubName: "Thunder FC");
        b.AddRegistration(jobId, u2.Id, role.Id, clubName: "Lightning SC");
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            ClubNames = ["Thunder FC"]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  8. POSITION FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Position filter →returns matching positions")]
    public async Task PositionFilter_ReturnsMatching()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        var u3 = b.AddUser("Charlie", "Brown");
        b.AddRegistration(jobId, u1.Id, role.Id, position: "Forward");
        b.AddRegistration(jobId, u2.Id, role.Id, position: "Goalie");
        b.AddRegistration(jobId, u3.Id, role.Id, position: "Forward");
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            Positions = ["Forward"]
        });

        result.Count.Should().Be(2);
        result.Result.Select(r => r.FirstName).Should().BeEquivalentTo("Alice", "Charlie");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  9. DATE RANGE FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Date range filter →returns registrations within range")]
    public async Task DateRange_ReturnsWithinRange()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        var u3 = b.AddUser("Charlie", "Brown");
        b.AddRegistration(jobId, u1.Id, role.Id, registrationTs: new DateTime(2025, 1, 15));
        b.AddRegistration(jobId, u2.Id, role.Id, registrationTs: new DateTime(2025, 3, 10));
        b.AddRegistration(jobId, u3.Id, role.Id, registrationTs: new DateTime(2025, 6, 1));
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            RegDateFrom = new DateTime(2025, 2, 1),
            RegDateTo = new DateTime(2025, 4, 30)
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Bob");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  10. LADT TREE FILTER (Team / Agegroup)
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Team ID filter →returns registrations assigned to that team")]
    public async Task TeamIdFilter_ReturnsAssignedRegistrations()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var league = b.AddLeague(jobId);
        var ag = b.AddAgegroup(league.LeagueId, "U14 Boys");
        var team1 = b.AddTeam(jobId, league.LeagueId, ag.AgegroupId, "Thunder");
        var team2 = b.AddTeam(jobId, league.LeagueId, ag.AgegroupId, "Lightning");

        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, assignedTeamId: team1.TeamId);
        b.AddRegistration(jobId, u2.Id, role.Id, assignedTeamId: team2.TeamId);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            TeamIds = [team1.TeamId]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    [Fact(DisplayName = "Agegroup ID filter →returns registrations in that agegroup")]
    public async Task AgegroupIdFilter_ReturnsMatching()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var league = b.AddLeague(jobId);
        var ag1 = b.AddAgegroup(league.LeagueId, "U14 Boys");
        var ag2 = b.AddAgegroup(league.LeagueId, "U16 Boys");

        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, assignedAgegroupId: ag1.AgegroupId);
        b.AddRegistration(jobId, u2.Id, role.Id, assignedAgegroupId: ag2.AgegroupId);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            AgegroupIds = [ag1.AgegroupId]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  11. AGGREGATES
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Aggregates →TotalFees, TotalPaid, TotalOwed computed correctly")]
    public async Task Aggregates_ComputedCorrectly()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        var u3 = b.AddUser("Charlie", "Brown");
        b.AddRegistration(jobId, u1.Id, role.Id, feeTotal: 100m, paidTotal: 100m);  // owed 0
        b.AddRegistration(jobId, u2.Id, role.Id, feeTotal: 200m, paidTotal: 50m);   // owed 150
        b.AddRegistration(jobId, u3.Id, role.Id, feeTotal: 150m, paidTotal: 0m);    // owed 150
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest());

        result.TotalFees.Should().Be(450m);
        result.TotalPaid.Should().Be(150m);
        result.TotalOwed.Should().Be(300m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  12. SORT ORDER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Sort order →results sorted by LastName then FirstName")]
    public async Task SortOrder_ByLastNameThenFirstName()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Charlie", "Adams");
        var u2 = b.AddUser("Alice", "Adams");
        var u3 = b.AddUser("Bob", "Zimmerman");
        b.AddRegistration(jobId, u1.Id, role.Id);
        b.AddRegistration(jobId, u2.Id, role.Id);
        b.AddRegistration(jobId, u3.Id, role.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest());

        var names = result.Result.Select(r => $"{r.FirstName} {r.LastName}").ToList();
        names.Should().ContainInOrder("Alice Adams", "Charlie Adams", "Bob Zimmerman");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  13. SCHOOL NAME FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "School name filter →matches partial school name")]
    public async Task SchoolNameFilter_MatchesPartial()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        b.AddRegistration(jobId, u1.Id, role.Id, schoolName: "Lincoln High School");
        b.AddRegistration(jobId, u2.Id, role.Id, schoolName: "Washington Middle School");
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest { SchoolName = "Lincoln" });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Alice");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  14. COMBINED FILTERS (AND logic between different filter types)
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Combined filters →AND logic between active status + pay status")]
    public async Task CombinedFilters_ActiveAndPayStatus()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        var u2 = b.AddUser("Bob", "Jones");
        var u3 = b.AddUser("Charlie", "Brown");
        b.AddRegistration(jobId, u1.Id, role.Id, active: true, feeTotal: 100m, paidTotal: 100m);   // active + paid
        b.AddRegistration(jobId, u2.Id, role.Id, active: true, feeTotal: 100m, paidTotal: 50m);    // active + underpaid
        b.AddRegistration(jobId, u3.Id, role.Id, active: false, feeTotal: 100m, paidTotal: 100m);  // inactive + paid
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest
        {
            ActiveStatuses = ["True"],
            PayStatuses = ["UNDER PAID"]
        });

        result.Count.Should().Be(1);
        result.Result.Single().FirstName.Should().Be("Bob");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  15. EMPTY RESULT
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "No matching registrations →returns empty with zero aggregates")]
    public async Task NoMatches_ReturnsEmptyWithZeroAggregates()
    {
        var (repo, jobId, b, _) = await CreateScenarioAsync();

        var role = b.AddRole(RoleConstants.Player, "Player");
        var u1 = b.AddUser("Alice", "Smith");
        b.AddRegistration(jobId, u1.Id, role.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(jobId, new RegistrationSearchRequest { Name = "zzz_nomatch" });

        result.Count.Should().Be(0);
        result.Result.Should().BeEmpty();
        result.TotalFees.Should().Be(0);
        result.TotalPaid.Should().Be(0);
        result.TotalOwed.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  16. VERTICAL INSURE — PLAYER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "HasVIPlayerInsurance=false →returns only active Players with null RegsaverPolicyId")]
    public async Task VIPlayer_NotAccepted_FiltersCorrectly()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        job.BOfferPlayerRegsaverInsurance = true;
        var repo = new RegistrationRepository(ctx);

        var playerRole = b.AddRole(RoleConstants.Player, "Player");
        var coachRole = b.AddRole("RoleCoach123", "Coach");

        var uncovered = b.AddRegistration(job.JobId, b.AddUser("Alice", "A").Id, playerRole.Id);
        var covered = b.AddRegistration(job.JobId, b.AddUser("Bob", "B").Id, playerRole.Id);
        covered.RegsaverPolicyId = "POL-123";
        b.AddRegistration(job.JobId, b.AddUser("Carol", "C").Id, playerRole.Id, active: false);
        b.AddRegistration(job.JobId, b.AddUser("Dan", "D").Id, coachRole.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(job.JobId, new RegistrationSearchRequest
        {
            HasVIPlayerInsurance = false
        });

        result.Count.Should().Be(1);
        result.Result[0].RegistrationId.Should().Be(uncovered.RegistrationId);
    }

    [Fact(DisplayName = "HasVIPlayerInsurance=false on non-VI job →filter is ignored (no-op)")]
    public async Task VIPlayer_JobDoesNotOfferVI_FilterIgnored()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        // NOTE: BOfferPlayerRegsaverInsurance NOT set → defaults null/false
        var repo = new RegistrationRepository(ctx);

        var playerRole = b.AddRole(RoleConstants.Player, "Player");
        b.AddRegistration(job.JobId, b.AddUser("Alice", "A").Id, playerRole.Id);
        b.AddRegistration(job.JobId, b.AddUser("Bob", "B").Id, playerRole.Id);
        await b.SaveAsync();

        var result = await repo.SearchAsync(job.JobId, new RegistrationSearchRequest
        {
            HasVIPlayerInsurance = false
        });

        // Filter is a no-op on non-VI jobs — all registrations returned
        result.Count.Should().Be(2);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  17. VERTICAL INSURE — TEAM
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "HasVITeamInsurance=false →returns ClubReps with >=1 uncovered active team")]
    public async Task VITeam_NotAccepted_FiltersCorrectly()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        job.BOfferTeamRegsaverInsurance = true;
        var repo = new RegistrationRepository(ctx);

        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var playerRole = b.AddRole(RoleConstants.Player, "Player");

        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);

        var repUncovered = b.AddRegistration(job.JobId, b.AddUser("Alice", "A").Id, clubRepRole.Id);
        var repFullyCovered = b.AddRegistration(job.JobId, b.AddUser("Bob", "B").Id, clubRepRole.Id);
        b.AddRegistration(job.JobId, b.AddUser("Carl", "C").Id, playerRole.Id);

        // repUncovered: one team covered, one team NOT covered → should match
        var teamCovered1 = b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "T1", clubRepRegistrationId: repUncovered.RegistrationId);
        teamCovered1.ViPolicyClubRepRegId = repUncovered.RegistrationId;
        b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "T2", clubRepRegistrationId: repUncovered.RegistrationId);
        // (ViPolicyClubRepRegId left null on T2)

        // repFullyCovered: all teams covered → should NOT match
        var teamCovered2 = b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "T3", clubRepRegistrationId: repFullyCovered.RegistrationId);
        teamCovered2.ViPolicyClubRepRegId = repFullyCovered.RegistrationId;

        await b.SaveAsync();

        var result = await repo.SearchAsync(job.JobId, new RegistrationSearchRequest
        {
            HasVITeamInsurance = false
        });

        result.Count.Should().Be(1);
        result.Result[0].RegistrationId.Should().Be(repUncovered.RegistrationId);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  18. ARB HEALTH
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "ArbHealthStatus='behind-active' →returns active/suspended subs with balance owed")]
    public async Task ArbHealth_BehindActive_FiltersCorrectly()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        job.AdnArb = true;
        var repo = new RegistrationRepository(ctx);

        var role = b.AddRole(RoleConstants.Player, "Player");

        var behindActive = b.AddRegistration(job.JobId, b.AddUser("Alice", "A").Id, role.Id, feeTotal: 100m, paidTotal: 20m);
        behindActive.AdnSubscriptionStatus = "active";

        var behindSuspended = b.AddRegistration(job.JobId, b.AddUser("Bob", "B").Id, role.Id, feeTotal: 100m, paidTotal: 30m);
        behindSuspended.AdnSubscriptionStatus = "suspended";

        var behindExpired = b.AddRegistration(job.JobId, b.AddUser("Carol", "C").Id, role.Id, feeTotal: 100m, paidTotal: 10m);
        behindExpired.AdnSubscriptionStatus = "expired";

        var paidActive = b.AddRegistration(job.JobId, b.AddUser("Dan", "D").Id, role.Id, feeTotal: 100m, paidTotal: 100m);
        paidActive.AdnSubscriptionStatus = "active";

        var activeButInactive = b.AddRegistration(job.JobId, b.AddUser("Eve", "E").Id, role.Id, feeTotal: 100m, paidTotal: 20m, active: false);
        activeButInactive.AdnSubscriptionStatus = "active";

        await b.SaveAsync();

        var result = await repo.SearchAsync(job.JobId, new RegistrationSearchRequest
        {
            ArbHealthStatus = "behind-active"
        });

        result.Count.Should().Be(2);
        result.Result.Select(r => r.FirstName).Should().BeEquivalentTo("Alice", "Bob");
    }

    [Fact(DisplayName = "ArbHealthStatus='behind-expired' →returns expired/terminated subs with balance owed")]
    public async Task ArbHealth_BehindExpired_FiltersCorrectly()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        job.AdnArb = true;
        var repo = new RegistrationRepository(ctx);

        var role = b.AddRole(RoleConstants.Player, "Player");

        var expired = b.AddRegistration(job.JobId, b.AddUser("Alice", "A").Id, role.Id, feeTotal: 100m, paidTotal: 20m);
        expired.AdnSubscriptionStatus = "expired";

        var terminated = b.AddRegistration(job.JobId, b.AddUser("Bob", "B").Id, role.Id, feeTotal: 100m, paidTotal: 30m);
        terminated.AdnSubscriptionStatus = "terminated";

        var active = b.AddRegistration(job.JobId, b.AddUser("Carol", "C").Id, role.Id, feeTotal: 100m, paidTotal: 10m);
        active.AdnSubscriptionStatus = "active";

        await b.SaveAsync();

        var result = await repo.SearchAsync(job.JobId, new RegistrationSearchRequest
        {
            ArbHealthStatus = "behind-expired"
        });

        result.Count.Should().Be(2);
        result.Result.Select(r => r.FirstName).Should().BeEquivalentTo("Alice", "Bob");
    }

    [Fact(DisplayName = "ArbHealthStatus on non-ARB job →filter ignored (no-op)")]
    public async Task ArbHealth_JobHasNoArb_FilterIgnored()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        // NOTE: AdnArb NOT set → defaults null/false
        var repo = new RegistrationRepository(ctx);

        var role = b.AddRole(RoleConstants.Player, "Player");
        b.AddRegistration(job.JobId, b.AddUser("Alice", "A").Id, role.Id, feeTotal: 100m, paidTotal: 20m);
        b.AddRegistration(job.JobId, b.AddUser("Bob", "B").Id, role.Id, feeTotal: 100m, paidTotal: 100m);
        await b.SaveAsync();

        var result = await repo.SearchAsync(job.JobId, new RegistrationSearchRequest
        {
            ArbHealthStatus = "behind-active"
        });

        // Filter is a no-op → all registrations returned
        result.Count.Should().Be(2);
    }
}
