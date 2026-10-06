using FluentAssertions;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.TeamRegistration;

/// <summary>
/// TEAM WIZARD — SAME-NAME CLUB LISTS (Todd 2026-10-06)
///
/// Sign-up lets a rep create a club whose name another club already uses — typically the rep replacing
/// that club's old rep. The Teams step then carries two lists:
///   1. the other same-name clubs' saved teams, to pick instead of retyping;
///   2. teams other same-name reps already registered in THIS event, to warn before a double entry.
/// "Same name" is normalized ("Fury Lax" = "Fury Lacrosse"); a filler-only name matches nothing.
/// </summary>
public class TeamsMetadataSameNameTests
{
    private static ClubTeams Lib(int id, int clubId, string name, string grad, bool active = true, string? lop = "A") => new()
    {
        ClubTeamId = id,
        ClubId = clubId,
        ClubTeamName = name,
        ClubTeamGradYear = grad,
        ClubTeamLevelOfPlay = lop,
        Active = active
    };

    private static ClubIdName Club(int id, string name) => new() { ClubId = id, ClubName = name };

    private static OtherClubRepTeamInfo EventTeam(string club, string team, string rep, string? grad = "2030",
        string ag = "2030") => new()
    {
        ClubName = club,
        TeamName = team,
        GradYear = grad,
        AgegroupName = ag,
        RepFirstName = rep.Split(' ')[0],
        RepLastName = rep.Split(' ')[1]
    };

    // ═══════════════════════════════════════════════════════════════════
    //  LIST 1 — other same-name clubs' saved teams
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Same-name clubs: every OTHER club of that name, normalized; never the rep's own")]
    public void SameNameClubs_AllOthersNormalized()
    {
        var clubs = new[]
        {
            Club(1, "Fury Lacrosse"),   // own
            Club(2, "Fury Lacrosse"),   // exact
            Club(3, "Fury Lax"),        // normalizes the same
            Club(4, "Fury Lacrosse NJ"),// a different club
            Club(5, "Storm Lacrosse"),
        };

        var result = SameNameClubLists.SameNameClubs(1, clubs);

        result.Keys.Should().BeEquivalentTo([2, 3]);
    }

    [Fact(DisplayName = "Same-name clubs: a filler-only name matches nothing")]
    public void SameNameClubs_FillerOnlyMatchesNothing()
    {
        var clubs = new[] { Club(1, "Lacrosse Club"), Club(2, "The Lacrosse Club") };

        SameNameClubLists.SameNameClubs(1, clubs).Should().BeEmpty();
    }

    [Fact(DisplayName = "Library list: every same-name club's active teams, each tagged with its source club")]
    public void LibraryTeams_FromEverySameNameClub()
    {
        var sameName = new Dictionary<int, string> { [2] = "Fury Lacrosse", [3] = "Fury Lax" };
        var theirs = new[]
        {
            Lib(20, 2, "Fury 2030 Blue", "2030"),
            Lib(30, 3, "Fury 2029 Gold", "2029"),
        };

        var result = SameNameClubLists.LibraryTeams(sameName, theirs, []);

        result.Select(t => (t.ClubTeamName, t.SourceClubName)).Should().BeEquivalentTo(
            [("Fury 2029 Gold", "Fury Lax"), ("Fury 2030 Blue", "Fury Lacrosse")]);
    }

    [Fact(DisplayName = "Library list: archived teams are left out")]
    public void LibraryTeams_ExcludesArchived()
    {
        var sameName = new Dictionary<int, string> { [2] = "Fury Lacrosse" };
        var theirs = new[] { Lib(20, 2, "Fury 2030 Blue", "2030", active: false) };

        SameNameClubLists.LibraryTeams(sameName, theirs, []).Should().BeEmpty();
    }

    [Fact(DisplayName = "Library list: the same team on two lists appears once")]
    public void LibraryTeams_DedupesNamePlusGradYear()
    {
        var sameName = new Dictionary<int, string> { [2] = "Fury Lacrosse", [3] = "Fury Lax" };
        var theirs = new[]
        {
            Lib(20, 2, "Fury 2030 Blue", "2030"),
            Lib(30, 3, "fury  2030 blue ", "2030"),
            Lib(31, 3, "Fury 2030 Blue", "2031"),   // different grad year = different team
        };

        var result = SameNameClubLists.LibraryTeams(sameName, theirs, []);

        result.Should().HaveCount(2);
    }

    [Fact(DisplayName = "Library list: teams the rep's own library holds are left out — archived own rows too")]
    public void LibraryTeams_ExcludesOwnLibrary()
    {
        var sameName = new Dictionary<int, string> { [2] = "Fury Lacrosse" };
        var theirs = new[]
        {
            Lib(20, 2, "Fury 2030 Blue", "2030"),
            Lib(21, 2, "Fury 2029 Gold", "2029"),
            Lib(22, 2, "Fury 2028 Red", "2028"),
        };
        var own = new[]
        {
            Lib(10, 1, "FURY 2030 BLUE", "2030"),
            Lib(11, 1, "Fury 2029 Gold", "2029", active: false),
        };

        var result = SameNameClubLists.LibraryTeams(sameName, theirs, own);

        result.Select(t => t.ClubTeamName).Should().BeEquivalentTo(["Fury 2028 Red"]);
    }

    [Fact(DisplayName = "Library list: a team of a club that is not a same-name club is ignored")]
    public void LibraryTeams_IgnoresOtherClubs()
    {
        var sameName = new Dictionary<int, string> { [2] = "Fury Lacrosse" };
        var theirs = new[] { Lib(90, 9, "Storm 2030", "2030") };

        SameNameClubLists.LibraryTeams(sameName, theirs, []).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  LIST 2 — same-name reps' teams already in this event
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Event list: teams of every other same-name rep, each with its rep's name")]
    public void EventTeams_EveryOtherSameNameRep()
    {
        var others = new[]
        {
            EventTeam("Fury Lacrosse", "Fury 2030 Blue", "Jane Smith"),
            EventTeam("Fury Lax", "Fury 2030 Blue", "Bob Jones"),
            EventTeam("Storm Lacrosse", "Storm 2030", "Ann Lee"),
        };

        var result = SameNameClubLists.EventTeams("Fury Lacrosse", others);

        result.Select(t => t.RepName).Should().BeEquivalentTo(["Jane Smith", "Bob Jones"]);
    }

    [Fact(DisplayName = "Event list: a waitlisted team reads its age group without the WAITLIST prefix")]
    public void EventTeams_StripsWaitlistPrefix()
    {
        var others = new[] { EventTeam("Fury Lacrosse", "Fury 2030 Blue", "Jane Smith", ag: "WAITLIST - 2030") };

        SameNameClubLists.EventTeams("Fury Lacrosse", others).Single().AgeGroupName.Should().Be("2030");
    }

    [Fact(DisplayName = "Event list: no club name on the rep's registration means no list")]
    public void EventTeams_NoClubName_Empty()
    {
        var others = new[] { EventTeam("Fury Lacrosse", "Fury 2030 Blue", "Jane Smith") };

        SameNameClubLists.EventTeams(null, others).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  The event query — which teams count as "already registered here"
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Event query: other reps' on-the-books teams in THIS job only, never the rep's own")]
    public async Task OtherClubRepTeamsInJob_Scope()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        var otherJob = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId, "2030");
        var waitlist = b.AddAgegroup(league.LeagueId, "WAITLIST - 2030");
        var dropped = b.AddAgegroup(league.LeagueId, "DROPPED Teams");
        b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        var me = b.AddUser("Cara", "Rep");
        var jane = b.AddUser("Jane", "Smith");
        var bob = b.AddUser("Bob", "Jones");

        var myReg = b.AddRegistration(job.JobId, me.Id, RoleConstants.ClubRep, clubName: "Fury Lacrosse");
        var janeReg = b.AddRegistration(job.JobId, jane.Id, RoleConstants.ClubRep, clubName: "Fury Lacrosse");
        var bobInactiveReg = b.AddRegistration(job.JobId, bob.Id, RoleConstants.ClubRep, active: false, clubName: "Fury Lacrosse");
        var janeOtherJobReg = b.AddRegistration(otherJob.JobId, jane.Id, RoleConstants.ClubRep, clubName: "Fury Lacrosse");

        b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "Mine 2030", clubRepRegistrationId: myReg.RegistrationId);
        b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "Jane 2030 Blue", clubRepRegistrationId: janeReg.RegistrationId);
        b.AddTeam(job.JobId, league.LeagueId, waitlist.AgegroupId, "Jane Waitlisted", clubRepRegistrationId: janeReg.RegistrationId);
        b.AddTeam(job.JobId, league.LeagueId, dropped.AgegroupId, "Jane Dropped", clubRepRegistrationId: janeReg.RegistrationId);
        b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "Jane Inactive", active: false, clubRepRegistrationId: janeReg.RegistrationId);
        b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "Bob Inactive Reg", clubRepRegistrationId: bobInactiveReg.RegistrationId);
        b.AddTeam(otherJob.JobId, league.LeagueId, ag.AgegroupId, "Jane Other Job", clubRepRegistrationId: janeOtherJobReg.RegistrationId);
        await b.SaveAsync();

        var rows = await new TeamRepository(ctx).GetOtherClubRepTeamsInJobAsync(job.JobId, myReg.RegistrationId);

        rows.Select(r => r.TeamName).Should().BeEquivalentTo(["Jane 2030 Blue", "Jane Waitlisted"]);
        rows.Should().OnlyContain(r => r.ClubName == "Fury Lacrosse" && r.RepFirstName == "Jane");
    }
}
