using FluentAssertions;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.TeamRegistration;

/// <summary>
/// SAME-NAME CLUBS (Todd 2026-10-06)
///
/// Sign-up lets a rep create a club whose name another club already uses — typically the rep replacing
/// that club's old rep. Two rules serve that rep:
///   1. sign-up copies the library of the club they picked as theirs into their own club;
///   2. the Teams step lists teams other same-name reps already registered in THIS event, to warn
///      before a double entry.
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
    //  RULE 1 — sign-up copies the picked club's library
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Library copy: the picked club's active teams become new rows of the rep's club")]
    public void LibraryCopy_ActiveTeamsIntoRepsClub()
    {
        var theirs = new[]
        {
            Lib(20, 2, "Fury 2030 Blue", "2030", lop: "5"),
            Lib(21, 2, "Fury 2029 Gold", "2029", lop: null),
        };

        var result = SameNameClubLists.LibraryCopy(theirs, [], targetClubId: 1, userId: "rep-1");

        result.Select(t => (t.ClubId, t.ClubTeamId, t.ClubTeamName, t.ClubTeamGradYear, t.ClubTeamLevelOfPlay, t.Active, t.LebUserId))
            .Should().BeEquivalentTo([
                (1, 0, "Fury 2030 Blue", "2030", (string?)"5", true, (string?)"rep-1"),
                (1, 0, "Fury 2029 Gold", "2029", (string?)null, true, (string?)"rep-1"),
            ]);
    }

    [Fact(DisplayName = "Library copy: archived and blank-named teams are not copied")]
    public void LibraryCopy_SkipsArchivedAndBlank()
    {
        var theirs = new[]
        {
            Lib(20, 2, "Fury 2030 Blue", "2030", active: false),
            Lib(21, 2, "  ", "2030"),
        };

        SameNameClubLists.LibraryCopy(theirs, [], 1, "rep-1").Should().BeEmpty();
    }

    [Fact(DisplayName = "Library copy: one row per name + grad year")]
    public void LibraryCopy_DedupesNamePlusGradYear()
    {
        var theirs = new[]
        {
            Lib(20, 2, "Fury 2030 Blue", "2030"),
            Lib(21, 2, "fury  2030 blue ", "2030"),
            Lib(22, 2, "Fury 2030 Blue", "2031"),   // different grad year = different team
        };

        SameNameClubLists.LibraryCopy(theirs, [], 1, "rep-1").Should().HaveCount(2);
    }

    [Fact(DisplayName = "Library copy: teams the rep's library already holds are skipped — archived own rows too")]
    public void LibraryCopy_SkipsOwnLibrary()
    {
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

        SameNameClubLists.LibraryCopy(theirs, own, 1, "rep-1").Select(t => t.ClubTeamName)
            .Should().BeEquivalentTo(["Fury 2028 Red"]);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SIGN-UP LIST — "Is your club one of these?"
    // ═══════════════════════════════════════════════════════════════════

    private static ClubSearchResult Found(int id, bool sameName = true) => new()
    {
        ClubId = id, ClubName = "Fury Lacrosse", TeamCount = 0, MatchScore = 100, IsExactMatch = sameName
    };

    [Fact(DisplayName = "Sign-up list: each same-name club carries its active team count and last registration")]
    public void ForSignUp_Annotates()
    {
        var libs = new[] { Lib(1, 1, "2030", "2030"), Lib(2, 1, "2029", "2029"), Lib(3, 1, "2027", "2027", active: false) };
        var when = new DateTime(2026, 9, 1);

        var result = SameNameClubLists.ForSignUp([Found(1)], libs, new Dictionary<int, DateTime> { [1] = when });

        result.Single().ActiveTeamCount.Should().Be(2);
        result.Single().LastRegistered.Should().Be(when);
    }

    [Fact(DisplayName = "Sign-up list: an untouched copy shows once — the club that registered teams is kept")]
    public void ForSignUp_FoldsIdenticalLists()
    {
        var libs = new[]
        {
            Lib(1, 1, "2030", "2030"), Lib(2, 1, "2029", "2029"),   // the original, registered last month
            Lib(3, 2, "2029", "2029"), Lib(4, 2, " 2030 ", "2030"), // its fresh copy, never registered
        };

        var result = SameNameClubLists.ForSignUp([Found(2), Found(1)], libs,
            new Dictionary<int, DateTime> { [1] = new DateTime(2026, 9, 1) });

        result.Select(r => r.ClubId).Should().Equal(1);
    }

    [Fact(DisplayName = "Sign-up list: a copy with teams archived stays listed; most recently registered first")]
    public void ForSignUp_EditedCopyStaysListed_RecentFirst()
    {
        var libs = new[]
        {
            Lib(1, 1, "2030", "2030"), Lib(2, 1, "2029", "2029"),                     // the old club
            Lib(3, 2, "2030", "2030"), Lib(4, 2, "2029", "2029", active: false),      // the new rep archived 2029
        };

        var result = SameNameClubLists.ForSignUp([Found(1), Found(2)], libs, new Dictionary<int, DateTime>
        {
            [1] = new DateTime(2024, 5, 1),
            [2] = new DateTime(2026, 9, 1),
        });

        result.Select(r => r.ClubId).Should().Equal(2, 1);
    }

    [Fact(DisplayName = "Sign-up list: clubs with no active teams are never folded; other results follow unchanged")]
    public void ForSignUp_EmptyClubsKept_OthersAfter()
    {
        var result = SameNameClubLists.ForSignUp([Found(9, sameName: false), Found(1), Found(2)], [],
            new Dictionary<int, DateTime>());

        result.Select(r => (r.ClubId, r.ActiveTeamCount)).Should().Equal((1, 0), (2, 0), (9, (int?)null));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RULE 2 — same-name reps' teams already in this event
    // ═══════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "Event list: teams of every other rep under the exact club name, each with its rep's name")]
    public void EventTeams_EveryOtherSameNameRep()
    {
        var others = new[]
        {
            EventTeam("Fury Lacrosse", "Fury 2030 Blue", "Jane Smith"),
            EventTeam("Fury Lacrosse", "Fury 2029 Gold", "Bob Jones"),
            EventTeam("Storm Lacrosse", "Storm 2030", "Ann Lee"),
        };

        var result = SameNameClubLists.EventTeams("Fury Lacrosse", others);

        result.Select(t => t.RepName).Should().BeEquivalentTo(["Jane Smith", "Bob Jones"]);
    }

    [Fact(DisplayName = "Event list: a merely similar club name is a different club — exact name only, as the director's tree groups")]
    public void EventTeams_SimilarNameNotMatched()
    {
        var others = new[]
        {
            EventTeam("Fury Lax", "Fury 2030 Blue", "Bob Jones"),        // normalizes to the same club
            EventTeam("Fury Lacrosse Club", "Fury 2030 Blue", "Ann Lee"), // filler word added
            EventTeam("fury lacrosse", "Fury 2030 Blue", "Cal Diaz"),     // the tree splits on case too
        };

        SameNameClubLists.EventTeams("Fury Lacrosse", others).Should().BeEmpty();
    }

    [Fact(DisplayName = "Event list: a waitlisted team reads its TRUE age group, WAITLIST prefix kept")]
    public void EventTeams_KeepsWaitlistPrefix()
    {
        var others = new[] { EventTeam("Fury Lacrosse", "Fury 2030 Blue", "Jane Smith", ag: "WAITLIST - 2030") };

        SameNameClubLists.EventTeams("Fury Lacrosse", others).Single().AgeGroupName.Should().Be("WAITLIST - 2030");
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
