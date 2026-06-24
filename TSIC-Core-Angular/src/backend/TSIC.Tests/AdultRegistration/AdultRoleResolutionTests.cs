using System.Reflection;
using FluentAssertions;
using TSIC.API.Services.Adults;
using TSIC.Contracts.Dtos.AdultRegistration;
using TSIC.Domain.Constants;

namespace TSIC.Tests.AdultRegistration;

/// <summary>
/// Adult registration role firewall + release gate.
///
/// Two guarantees this locks down:
///  1. Each adult role key resolves to its OWN RoleId — Referee→Referee, Recruiter→Recruiter,
///     never collapsed into UnassignedAdult. A collapse would leak referees/recruiters into
///     the coach approval queue (which selects UnassignedAdult rows). Coach intentionally
///     IS UnassignedAdult (the minor-PII firewall).
///  2. Each role's director release gate (BRegistrationAllow{Staff,Referee,Recruiter}) blocks
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

    [Fact(DisplayName = "Coach resolves to UnassignedAdult (minor-PII firewall)")]
    public void Coach_ResolvesToUnassignedAdult()
    {
        var r = Invoke(Job(), AdultRegRoleKeys.Coach);
        RoleIdOf(r).Should().Be(RoleConstants.UnassignedAdult);
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
