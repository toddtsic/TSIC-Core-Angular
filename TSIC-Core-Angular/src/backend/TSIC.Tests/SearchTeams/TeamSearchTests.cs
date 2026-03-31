using FluentAssertions;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchTeams;

/// <summary>
/// TEAM SEARCH TESTS
///
/// These tests validate the SearchTeamsAsync method in TeamRepository —
/// the query behind the Search/Teams admin grid.
///
/// Each test seeds an in-memory database, runs a search with specific filters,
/// and verifies the correct teams are returned with accurate data.
///
/// Filters tested:
///   - No filters (returns all)
///   - Active status
///   - Club name (via club rep registration)
///   - Pay status (PAID IN FULL, UNDER PAID)
///   - Level of play
///   - Agegroup ID
///   - LADT tree (LeagueId, TeamId)
///   - Waitlist status
///   - Sort order (ClubName, AgegroupName, DivName, TeamName)
///   - Club rep info projected correctly
/// </summary>
public class TeamSearchTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  HELPER: build a standard test scenario with league + agegroup
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<(TeamRepository repo, Guid jobId, Guid leagueId, Guid agegroupId,
        SearchDataBuilder b, Infrastructure.Data.SqlDbContext.SqlDbContext ctx)>
        CreateScenarioAsync(string agegroupName = "U14 Boys")
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, agegroupName);
        var repo = new TeamRepository(ctx);
        return (repo, job.JobId, league.LeagueId, ag.AgegroupId, b, ctx);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  1. NO FILTERS — returns all teams for the job
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "No filters →returns all teams for the job")]
    public async Task NoFilters_ReturnsAllTeams()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder");
        b.AddTeam(jobId, leagueId, agId, "Lightning");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest());

        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "No filters →does not return teams from other jobs")]
    public async Task NoFilters_ExcludesOtherJobs()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder");

        var otherJob = b.AddJob();
        var otherLeague = b.AddLeague(otherJob.JobId);
        var otherAg = b.AddAgegroup(otherLeague.LeagueId, "U16 Boys");
        b.AddTeam(otherJob.JobId, otherLeague.LeagueId, otherAg.AgegroupId, "Other Team");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest());

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Thunder");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  2. ACTIVE STATUS FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Active status 'True' →returns only active teams")]
    public async Task ActiveStatus_True_ReturnsActiveOnly()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder", active: true);
        b.AddTeam(jobId, leagueId, agId, "Lightning", active: false);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            ActiveStatuses = ["True"]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Thunder");
    }

    [Fact(DisplayName = "Active status 'False' →returns only inactive teams")]
    public async Task ActiveStatus_False_ReturnsInactiveOnly()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder", active: true);
        b.AddTeam(jobId, leagueId, agId, "Lightning", active: false);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            ActiveStatuses = ["False"]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Lightning");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  3. CLUB NAME FILTER (via club rep registration)
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Club name filter →returns teams belonging to that club")]
    public async Task ClubNameFilter_ReturnsMatchingClub()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var u1 = b.AddUser("Rep", "Thunder");
        var u2 = b.AddUser("Rep", "Lightning");
        var clubRep1 = b.AddRegistration(jobId, u1.Id, clubRepRole.Id, clubName: "Thunder FC");
        var clubRep2 = b.AddRegistration(jobId, u2.Id, clubRepRole.Id, clubName: "Lightning SC");
        b.AddTeam(jobId, leagueId, agId, "Thunder U14", clubRepRegistrationId: clubRep1.RegistrationId);
        b.AddTeam(jobId, leagueId, agId, "Lightning U14", clubRepRegistrationId: clubRep2.RegistrationId);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            ClubNames = ["Thunder FC"]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Thunder U14");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  4. PAY STATUS FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Pay status 'PAID IN FULL' →returns teams with OwedTotal = 0")]
    public async Task PayStatus_PaidInFull_ReturnsZeroOwed()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Paid Team", feeBase: 500m, paidTotal: 500m);
        b.AddTeam(jobId, leagueId, agId, "Unpaid Team", feeBase: 500m, paidTotal: 200m);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            PayStatuses = ["PAID IN FULL"]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Paid Team");
    }

    [Fact(DisplayName = "Pay status 'UNDER PAID' →returns teams with OwedTotal > 0")]
    public async Task PayStatus_UnderPaid_ReturnsPositiveOwed()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Paid Team", feeBase: 500m, paidTotal: 500m);
        b.AddTeam(jobId, leagueId, agId, "Unpaid Team", feeBase: 500m, paidTotal: 200m);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            PayStatuses = ["UNDER PAID"]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Unpaid Team");
        result.Single().OwedTotal.Should().Be(300m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  5. LEVEL OF PLAY FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Level of play filter →returns matching teams")]
    public async Task LevelOfPlayFilter_ReturnsMatching()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder AA", levelOfPlay: "AA");
        b.AddTeam(jobId, leagueId, agId, "Lightning A", levelOfPlay: "A");
        b.AddTeam(jobId, leagueId, agId, "Storm AA", levelOfPlay: "AA");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            LevelOfPlays = ["AA"]
        });

        result.Should().HaveCount(2);
        result.Select(t => t.TeamName).Should().BeEquivalentTo(["Thunder AA", "Storm AA"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  6. AGEGROUP ID FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Agegroup filter →returns teams in that agegroup")]
    public async Task AgegroupFilter_ReturnsMatching()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var ag2 = b.AddAgegroup(leagueId, "U16 Boys");
        b.AddTeam(jobId, leagueId, agId, "Thunder U14");
        b.AddTeam(jobId, leagueId, ag2.AgegroupId, "Thunder U16");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            AgegroupIds = [agId]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Thunder U14");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  7. LADT TREE FILTER (LeagueId, TeamId)
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "LADT tree team ID filter →returns specific team")]
    public async Task LadtTreeTeamId_ReturnsSpecificTeam()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var team1 = b.AddTeam(jobId, leagueId, agId, "Thunder");
        var team2 = b.AddTeam(jobId, leagueId, agId, "Lightning");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            TeamIds = [team1.TeamId]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Thunder");
    }

    [Fact(DisplayName = "LADT tree league ID filter →returns all teams in league")]
    public async Task LadtTreeLeagueId_ReturnsAllInLeague()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var league2 = b.AddLeague(jobId);
        var ag2 = b.AddAgegroup(league2.LeagueId, "U18 Girls");
        b.AddTeam(jobId, leagueId, agId, "Thunder");
        b.AddTeam(jobId, leagueId, agId, "Lightning");
        b.AddTeam(jobId, league2.LeagueId, ag2.AgegroupId, "Storm");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            LeagueIds = [leagueId]
        });

        result.Should().HaveCount(2);
        result.Select(t => t.TeamName).Should().BeEquivalentTo(["Thunder", "Lightning"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  8. WAITLIST STATUS FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Waitlist status 'WAITLISTED' →returns teams in WAITLIST agegroup")]
    public async Task WaitlistStatus_Waitlisted_ReturnsWaitlistTeams()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);

        var normalAg = b.AddAgegroup(league.LeagueId, "U14 Boys");
        var waitlistAg = b.AddAgegroup(league.LeagueId, "WAITLIST - U14 Boys");

        b.AddTeam(job.JobId, league.LeagueId, normalAg.AgegroupId, "Normal Team");
        b.AddTeam(job.JobId, league.LeagueId, waitlistAg.AgegroupId, "Waitlisted Team");
        await b.SaveAsync();

        var repo = new TeamRepository(ctx);
        var result = await repo.SearchTeamsAsync(job.JobId, new TeamSearchRequest
        {
            WaitlistScheduledStatus = "WAITLISTED"
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Waitlisted Team");
    }

    [Fact(DisplayName = "Waitlist status 'NOT_WAITLISTED' →excludes WAITLIST agegroup teams")]
    public async Task WaitlistStatus_NotWaitlisted_ExcludesWaitlistTeams()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        var league = b.AddLeague(job.JobId);

        var normalAg = b.AddAgegroup(league.LeagueId, "U14 Boys");
        var waitlistAg = b.AddAgegroup(league.LeagueId, "WAITLIST - U14 Boys");

        b.AddTeam(job.JobId, league.LeagueId, normalAg.AgegroupId, "Normal Team");
        b.AddTeam(job.JobId, league.LeagueId, waitlistAg.AgegroupId, "Waitlisted Team");
        await b.SaveAsync();

        var repo = new TeamRepository(ctx);
        var result = await repo.SearchTeamsAsync(job.JobId, new TeamSearchRequest
        {
            WaitlistScheduledStatus = "NOT_WAITLISTED"
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Normal Team");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  9. SORT ORDER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Sort order →results sorted by ClubName, AgegroupName, TeamName")]
    public async Task SortOrder_ByClubAgegroupTeam()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var uB = b.AddUser("Rep", "Beta");
        var uA = b.AddUser("Rep", "Alpha");
        var clubRepB = b.AddRegistration(jobId, uB.Id, clubRepRole.Id, clubName: "Beta FC");
        var clubRepA = b.AddRegistration(jobId, uA.Id, clubRepRole.Id, clubName: "Alpha SC");

        b.AddTeam(jobId, leagueId, agId, "Zulu", clubRepRegistrationId: clubRepB.RegistrationId);
        b.AddTeam(jobId, leagueId, agId, "Alpha", clubRepRegistrationId: clubRepA.RegistrationId);
        b.AddTeam(jobId, leagueId, agId, "Bravo", clubRepRegistrationId: clubRepA.RegistrationId);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest());

        var teamNames = result.Select(t => t.TeamName).ToList();
        // Alpha SC teams first (sorted by name), then Beta FC
        teamNames.Should().ContainInOrder("Alpha", "Bravo", "Zulu");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  10. CLUB REP INFO PROJECTED
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Club rep info →name and email projected correctly")]
    public async Task ClubRepInfo_ProjectedCorrectly()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var clubRepRole = b.AddRole(RoleConstants.ClubRep, "Club Rep");
        var user = b.AddUser("Jane", "Director", email: "jane@club.com");
        var clubRep = b.AddRegistration(jobId, user.Id, clubRepRole.Id, clubName: "Thunder FC");
        b.AddTeam(jobId, leagueId, agId, "Thunder U14", clubRepRegistrationId: clubRep.RegistrationId);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest());

        var team = result.Single();
        team.ClubName.Should().Be("Thunder FC");
        team.ClubRepName.Should().Be("Director, Jane");
        team.ClubRepEmail.Should().Be("jane@club.com");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  11. FINANCIAL TOTALS
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Financial totals →PaidTotal and OwedTotal projected correctly")]
    public async Task FinancialTotals_ProjectedCorrectly()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder", feeBase: 500m, paidTotal: 200m);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest());

        var team = result.Single();
        team.PaidTotal.Should().Be(200m);
        team.OwedTotal.Should().Be(300m);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  12. COMBINED FILTERS
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Combined filters →active + pay status uses AND logic")]
    public async Task CombinedFilters_ActiveAndPayStatus()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Active Paid", active: true, feeBase: 500m, paidTotal: 500m);
        b.AddTeam(jobId, leagueId, agId, "Active Unpaid", active: true, feeBase: 500m, paidTotal: 200m);
        b.AddTeam(jobId, leagueId, agId, "Inactive Paid", active: false, feeBase: 500m, paidTotal: 500m);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            ActiveStatuses = ["True"],
            PayStatuses = ["UNDER PAID"]
        });

        result.Should().HaveCount(1);
        result.Single().TeamName.Should().Be("Active Unpaid");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  13. EMPTY RESULT
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "No matching teams →returns empty list")]
    public async Task NoMatches_ReturnsEmptyList()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        b.AddTeam(jobId, leagueId, agId, "Thunder", active: true);
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            ActiveStatuses = ["False"]
        });

        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  14. CADT TREE FILTER
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "CADT tree filter →returns only specified team IDs")]
    public async Task CadtTreeFilter_ReturnsSpecifiedTeams()
    {
        var (repo, jobId, leagueId, agId, b, ctx) = await CreateScenarioAsync();

        var team1 = b.AddTeam(jobId, leagueId, agId, "Thunder");
        var team2 = b.AddTeam(jobId, leagueId, agId, "Lightning");
        var team3 = b.AddTeam(jobId, leagueId, agId, "Storm");
        await b.SaveAsync();

        var result = await repo.SearchTeamsAsync(jobId, new TeamSearchRequest
        {
            CadtTeamIds = [team1.TeamId, team3.TeamId]
        });

        result.Should().HaveCount(2);
        result.Select(t => t.TeamName).Should().BeEquivalentTo(["Thunder", "Storm"]);
    }
}
