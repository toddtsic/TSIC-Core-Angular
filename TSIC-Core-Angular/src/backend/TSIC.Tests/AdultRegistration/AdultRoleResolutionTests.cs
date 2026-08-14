using System.Reflection;
using FluentAssertions;
using TSIC.API.Services.Adults;
using TSIC.Contracts.Dtos.AdultRegistration;
using TSIC.Domain.Constants;

namespace TSIC.Tests.AdultRegistration;

/// <summary>
/// Adult registration role firewall + release gate.
///
/// Three guarantees this locks down:
///  1. Each adult role key resolves to its OWN RoleId — Referee→Referee, Recruiter→Recruiter,
///     never collapsed into UnassignedAdult. A collapse would leak referees/recruiters into
///     the coach approval queue (which selects UnassignedAdult rows).
///  2. The coach key resolves by WHO CAN VOUCH (ruling 2026-08-14): Club (player-registration
///     site) → UnassignedAdult, the minor-PII firewall — only the director can vet a coach.
///     Tournament/League (team-registration sites) → Staff DIRECT placement — the roster
///     arrived with the club's own team, so the club vouches; the privacy control there is
///     the consent-gated BAllowRosterViewAdult toggle, not a vetting queue. A regression to
///     UA on Tournament/League breaks clients (directors rubber-stamping hundreds of visiting
///     coaches); a regression to Staff on Club is a minor-PII leak.
///  3. Each role's director release gate (BRegistrationAllow{Staff,Referee,Recruiter}) blocks
///     registration when off — null/false = closed.
///
/// ResolveAdultRole is private static; invoked via reflection so the guarantee is locked at the
/// resolution layer without standing up the full service + DbContext.
/// </summary>
public class AdultRoleResolutionTests
{
    private static readonly MethodInfo Resolve =
        typeof(AdultRegistrationService).GetMethod(
            "ResolveAdultRole", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static AdultRegJobData Job(
        int jobTypeId = JobConstants.JobTypeClub,
        bool staff = true, bool referee = true, bool recruiter = true) => new()
    {
        JobId = Guid.NewGuid(),
        JobName = "Test Job",
        JobAi = 1,
        JobTypeId = jobTypeId,
        BAllowRosterViewAdult = false,
        BAddProcessingFees = false,
        BRegistrationAllowStaff = staff,
        BRegistrationAllowReferee = referee,
        BRegistrationAllowRecruiter = recruiter,
    };

    private static string RoleIdOf(object resolution) =>
        (string)resolution.GetType().GetProperty("RoleId")!.GetValue(resolution)!;

    private static bool NeedsTeamOf(object resolution) =>
        (bool)resolution.GetType().GetProperty("NeedsTeamSelection")!.GetValue(resolution)!;

    private static object Invoke(AdultRegJobData job, string roleKey)
    {
        try { return Resolve.Invoke(null, new object?[] { job, roleKey })!; }
        catch (TargetInvocationException ex) { throw ex.InnerException!; }
    }

    [Fact(DisplayName = "Referee resolves to Referee role, not UnassignedAdult")]
    public void Referee_ResolvesToOwnRole()
    {
        var r = Invoke(Job(), AdultRegRoleKeys.Referee);
        RoleIdOf(r).Should().Be(RoleConstants.Referee);
        RoleIdOf(r).Should().NotBe(RoleConstants.UnassignedAdult);
    }

    [Fact(DisplayName = "Recruiter resolves to Recruiter role, not UnassignedAdult")]
    public void Recruiter_ResolvesToOwnRole()
    {
        var r = Invoke(Job(), AdultRegRoleKeys.Recruiter);
        RoleIdOf(r).Should().Be(RoleConstants.Recruiter);
        RoleIdOf(r).Should().NotBe(RoleConstants.UnassignedAdult);
    }

    [Fact(DisplayName = "Coach on a Club (player-registration) site resolves to UnassignedAdult (minor-PII firewall)")]
    public void Coach_Club_ResolvesToUnassignedAdult()
    {
        var r = Invoke(Job(jobTypeId: JobConstants.JobTypeClub), AdultRegRoleKeys.Coach);
        RoleIdOf(r).Should().Be(RoleConstants.UnassignedAdult);
        RoleIdOf(r).Should().NotBe(RoleConstants.Staff);
    }

    [Theory(DisplayName = "Coach on a team-registration site resolves to Staff (direct placement — the club vouches)")]
    [InlineData(JobConstants.JobTypeTournament)]
    [InlineData(JobConstants.JobTypeLeague)]
    public void Coach_TeamRegistrationSites_ResolveToStaff(int jobTypeId)
    {
        var r = Invoke(Job(jobTypeId: jobTypeId), AdultRegRoleKeys.Coach);
        RoleIdOf(r).Should().Be(RoleConstants.Staff);
    }

    [Theory(DisplayName = "Coach must select ≥1 team on EVERY team job type (Club: no no-request queue rows; 2/3: placement needs a target)")]
    [InlineData(JobConstants.JobTypeClub)]
    [InlineData(JobConstants.JobTypeLeague)]
    [InlineData(JobConstants.JobTypeTournament)]
    public void Coach_RequiresTeamSelection_AllJobTypes(int jobTypeId)
    {
        var r = Invoke(Job(jobTypeId: jobTypeId), AdultRegRoleKeys.Coach);
        NeedsTeamOf(r).Should().BeTrue();
    }

    [Fact(DisplayName = "Coach registration throws when the staff release gate is off")]
    public void Coach_Gate_Off_Throws()
    {
        var act = () => Invoke(Job(staff: false), AdultRegRoleKeys.Coach);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not currently open*");
    }

    [Fact(DisplayName = "Referee registration throws when the referee release gate is off")]
    public void Referee_Gate_Off_Throws()
    {
        var act = () => Invoke(Job(referee: false), AdultRegRoleKeys.Referee);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not currently open*");
    }

    [Fact(DisplayName = "Recruiter registration throws when the recruiter release gate is off")]
    public void Recruiter_Gate_Off_Throws()
    {
        var act = () => Invoke(Job(recruiter: false), AdultRegRoleKeys.Recruiter);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not currently open*");
    }
}
