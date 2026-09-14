using FluentAssertions;
using TSIC.API.Services.Admin;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// DIRECTOR LOCAL CLUB RENAME (Search Registrations → Club Rep detail)
///
/// A Director renames the club on one Club Rep registration for THIS event only. Exactly two writes:
///   1. the registration's club name (+ its Assignment / RegistrationCategory copies), and
///   2. this job's round-robin ("T") schedule slots for the teams that rep owns
///      (Teams.ClubrepRegistrationid = the registration).
/// Nothing else moves: another rep's slots, bracket slots, the other job, the Clubs row.
/// </summary>
public class ClubRepLocalRenameTests
{
    private const string Director = RoleConstants.Names.DirectorName;
    private const string OldName = "Old Club";
    private const string NewName = "New Club";

    private sealed class World
    {
        public required SqlDbContext Ctx { get; init; }
        public required ClubRepLocalRenameService Svc { get; init; }
        public required Guid JobId { get; init; }
        public required Guid OtherJobId { get; init; }
        public required Registrations Rep { get; init; }
        public required Registrations OtherRep { get; init; }
        public required Registrations SameUserOtherJobRep { get; init; }
        public required Registrations Player { get; init; }
        public required AspNetUsers RepUser { get; init; }
    }

    // Gids, so assertions read as the scenario rather than as ids.
    private const int GameRepVsOther = 1;       // T1 = rep's "2030 Blue", T2 = other rep's "2030 Red"
    private const int GameOtherVsRep = 2;       // T1 = other rep's "2030 Red", T2 = rep's "2031 White"
    private const int GameBracket = 3;          // bracket (Q) row seated with the rep's team
    private const int GameOtherJob = 4;         // the same club, same user, a DIFFERENT job

    private static async Task<World> Seed(bool showTeamNameOnly = false)
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);

        var job = b.AddJob();
        job.BShowTeamNameOnlyInSchedules = showTeamNameOnly;
        var otherJob = b.AddJob();
        var league = b.AddLeague(job.JobId);
        var ag = b.AddAgegroup(league.LeagueId);
        var div = b.AddDivision(ag.AgegroupId);

        b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        b.AddRole(RoleConstants.Player, "Player");
        var repUser = b.AddUser("Cara", "Rep");
        var otherRepUser = b.AddUser("Omar", "Rep");
        var playerUser = b.AddUser("Pat", "Player");

        var rep = b.AddRegistration(job.JobId, repUser.Id, RoleConstants.ClubRep, feeTotal: 0m, clubName: OldName);
        rep.Assignment = OldName;
        rep.RegistrationCategory = $"Club Rep: {OldName}";
        var otherRep = b.AddRegistration(job.JobId, otherRepUser.Id, RoleConstants.ClubRep, feeTotal: 0m, clubName: "Other Club");
        var sameUserOtherJobRep = b.AddRegistration(otherJob.JobId, repUser.Id, RoleConstants.ClubRep, feeTotal: 0m, clubName: OldName);
        var player = b.AddRegistration(job.JobId, playerUser.Id, RoleConstants.Player, clubName: OldName);

        var blue = b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "2030 Blue", clubRepRegistrationId: rep.RegistrationId, divId: div.DivId);
        var white = b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "2031 White", clubRepRegistrationId: rep.RegistrationId, divId: div.DivId);
        var red = b.AddTeam(job.JobId, league.LeagueId, ag.AgegroupId, "2030 Red", clubRepRegistrationId: otherRep.RegistrationId, divId: div.DivId);
        var otherJobBlue = b.AddTeam(otherJob.JobId, league.LeagueId, ag.AgegroupId, "2030 Blue", clubRepRegistrationId: sameUserOtherJobRep.RegistrationId);

        ctx.Schedule.AddRange(
            Game(GameRepVsOther, job.JobId, div.DivId, "T", blue.TeamId, $"{OldName}:2030 Blue", "T", red.TeamId, "Other Club:2030 Red"),
            Game(GameOtherVsRep, job.JobId, div.DivId, "T", red.TeamId, "Other Club:2030 Red", "T", white.TeamId, $"{OldName}:2031 White"),
            Game(GameBracket, job.JobId, div.DivId, "Q", blue.TeamId, $"{OldName}:2030 Blue", "Q", red.TeamId, "Other Club:2030 Red"),
            Game(GameOtherJob, otherJob.JobId, null, "T", otherJobBlue.TeamId, $"{OldName}:2030 Blue", "T", null, null));

        await b.SaveAsync();

        var svc = new ClubRepLocalRenameService(
            new RegistrationRepository(ctx), new ScheduleRepository(ctx),
            new ClubRepository(ctx), new ClubRepRepository(ctx));

        return new World
        {
            Ctx = ctx, Svc = svc, JobId = job.JobId, OtherJobId = otherJob.JobId,
            Rep = rep, OtherRep = otherRep, SameUserOtherJobRep = sameUserOtherJobRep,
            Player = player, RepUser = repUser
        };
    }

    private static Schedule Game(
        int gid, Guid jobId, Guid? divId,
        string t1Type, Guid? t1Id, string? t1Name,
        string t2Type, Guid? t2Id, string? t2Name) => new()
    {
        Gid = gid,
        JobId = jobId,
        DivId = divId,
        GDate = new DateTime(2026, 11, 22, 12, 0, 0),
        Season = "Fall",
        Year = "2026",
        LebUserId = "seed",
        T1Type = t1Type, T1Id = t1Id, T1Name = t1Name,
        T2Type = t2Type, T2Id = t2Id, T2Name = t2Name
    };

    /// <summary>Re-read from the store, not the tracker, so an assertion proves the write was SAVED.</summary>
    private static Schedule Row(World w, int gid)
    {
        w.Ctx.ChangeTracker.Clear();
        return w.Ctx.Schedule.Single(s => s.Gid == gid);
    }

    private static Registrations Reg(World w, Guid registrationId)
    {
        w.Ctx.ChangeTracker.Clear();
        return w.Ctx.Registrations.Single(r => r.RegistrationId == registrationId);
    }

    [Fact(DisplayName = "Director rename writes the registration's club name and its two copies")]
    public async Task Rename_WritesRegistration()
    {
        var w = await Seed();

        var result = await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, NewName);

        result.ClubName.Should().Be(NewName);
        var reg = Reg(w, w.Rep.RegistrationId);
        reg.ClubName.Should().Be(NewName);
        reg.Assignment.Should().Be(NewName);
        reg.RegistrationCategory.Should().Be($"Club Rep: {NewName}");
        reg.LebUserId.Should().Be("dir-1");
    }

    [Fact(DisplayName = "Director rename restamps the rep's round-robin slots — T1 and T2 alike")]
    public async Task Rename_RestampsRepsTeamSlots()
    {
        var w = await Seed();

        var result = await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, NewName);

        result.ScheduleSlotsUpdated.Should().Be(2);
        Row(w, GameRepVsOther).T1Name.Should().Be($"{NewName}:2030 Blue");
        Row(w, GameOtherVsRep).T2Name.Should().Be($"{NewName}:2031 White");
    }

    [Fact(DisplayName = "Another rep's slots in the same games are not touched")]
    public async Task Rename_LeavesOtherRepsSlots()
    {
        var w = await Seed();

        await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, NewName);

        Row(w, GameRepVsOther).T2Name.Should().Be("Other Club:2030 Red");
        Row(w, GameOtherVsRep).T1Name.Should().Be("Other Club:2030 Red");
        Reg(w, w.OtherRep.RegistrationId).ClubName.Should().Be("Other Club");
    }

    [Fact(DisplayName = "Bracket slots are not touched, even when seated with the rep's team")]
    public async Task Rename_LeavesBracketSlots()
    {
        var w = await Seed();

        await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, NewName);

        var bracket = Row(w, GameBracket);
        bracket.T1Name.Should().Be($"{OldName}:2030 Blue");
        bracket.T2Name.Should().Be("Other Club:2030 Red");
    }

    [Fact(DisplayName = "The same user's registration and schedule in ANOTHER job are not touched")]
    public async Task Rename_LeavesOtherJob()
    {
        var w = await Seed();

        await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, NewName);

        Reg(w, w.SameUserOtherJobRep.RegistrationId).ClubName.Should().Be(OldName);
        Row(w, GameOtherJob).T1Name.Should().Be($"{OldName}:2030 Blue");
    }

    [Fact(DisplayName = "A job that shows team names only gets the bare team name in its slots")]
    public async Task Rename_ShowTeamNameOnly_WritesBareTeamName()
    {
        var w = await Seed(showTeamNameOnly: true);

        await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, NewName);

        Row(w, GameRepVsOther).T1Name.Should().Be("2030 Blue");
        Row(w, GameOtherVsRep).T2Name.Should().Be("2031 White");
        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be(NewName);
    }

    [Fact(DisplayName = "The name is trimmed before it is written")]
    public async Task Rename_TrimsName()
    {
        var w = await Seed();

        await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, $"  {NewName}  ");

        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be(NewName);
        Row(w, GameRepVsOther).T1Name.Should().Be($"{NewName}:2030 Blue");
    }

    [Theory(DisplayName = "Only a Director may rename — Superuser and SuperDirector are refused and nothing is written")]
    [InlineData(RoleConstants.Names.SuperuserName)]
    [InlineData(RoleConstants.Names.SuperDirectorName)]
    [InlineData("")]
    public async Task Rename_NonDirector_Refused(string callerRole)
    {
        var w = await Seed();

        var act = () => w.Svc.RenameAsync(w.JobId, "u-1", callerRole, w.Rep.RegistrationId, NewName);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Director*");
        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be(OldName);
        Row(w, GameRepVsOther).T1Name.Should().Be($"{OldName}:2030 Blue");
    }

    [Fact(DisplayName = "A registration from another job is refused and nothing is written")]
    public async Task Rename_OtherJobsRegistration_Refused()
    {
        var w = await Seed();

        var act = () => w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.SameUserOtherJobRep.RegistrationId, NewName);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not belong to this job*");
        Reg(w, w.SameUserOtherJobRep.RegistrationId).ClubName.Should().Be(OldName);
        Row(w, GameOtherJob).T1Name.Should().Be($"{OldName}:2030 Blue");
    }

    [Fact(DisplayName = "A non-Club-Rep registration is refused and nothing is written")]
    public async Task Rename_NonClubRep_Refused()
    {
        var w = await Seed();

        var act = () => w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Player.RegistrationId, NewName);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Club Rep*");
        Reg(w, w.Player.RegistrationId).ClubName.Should().Be(OldName);
    }

    [Fact(DisplayName = "An unknown registration is not found")]
    public async Task Rename_UnknownRegistration_NotFound()
    {
        var w = await Seed();

        var act = () => w.Svc.RenameAsync(w.JobId, "dir-1", Director, Guid.NewGuid(), NewName);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory(DisplayName = "A blank name is refused and nothing is written")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Rename_BlankName_Refused(string? name)
    {
        var w = await Seed();

        var act = () => w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, name!);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*required*");
        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be(OldName);
    }

    [Fact(DisplayName = "A name longer than a club name may be is refused")]
    public async Task Rename_TooLong_Refused()
    {
        var w = await Seed();
        var tooLong = new string('x', ClubRepLocalRenameService.MaxClubNameLength + 1);

        var act = () => w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, tooLong);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot exceed*");
        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be(OldName);
    }

    [Fact(DisplayName = "Naming the rep after a DIFFERENT club is refused and nothing is written")]
    public async Task Rename_ToAnotherClubsName_Refused()
    {
        var w = await Seed();
        w.Ctx.Clubs.Add(new Clubs { ClubId = 900, ClubName = "True Lacrosse" });
        await w.Ctx.SaveChangesAsync();

        var act = () => w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, "True Lacrosse");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different club*");
        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be(OldName);
        Row(w, GameRepVsOther).T1Name.Should().Be($"{OldName}:2030 Blue");
    }

    [Fact(DisplayName = "Naming the rep after a club they DO represent is allowed")]
    public async Task Rename_ToRepsOwnClubName_Allowed()
    {
        var w = await Seed();
        w.Ctx.Clubs.Add(new Clubs { ClubId = 901, ClubName = "Old Club Lacrosse" });
        w.Ctx.ClubReps.Add(new ClubReps { Aid = 1, ClubId = 901, ClubRepUserId = w.RepUser.Id });
        await w.Ctx.SaveChangesAsync();

        await w.Svc.RenameAsync(w.JobId, "dir-1", Director, w.Rep.RegistrationId, "Old Club Lacrosse");

        Reg(w, w.Rep.RegistrationId).ClubName.Should().Be("Old Club Lacrosse");
        Row(w, GameRepVsOther).T1Name.Should().Be("Old Club Lacrosse:2030 Blue");
    }
}
