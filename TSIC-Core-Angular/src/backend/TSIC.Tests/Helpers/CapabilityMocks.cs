using Moq;
using TSIC.Contracts.Services;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Test doubles for <see cref="IJobRegistrationCapabilities"/> — the registration-create
/// authority that <c>TeamRegistrationService</c> (and others) now consults instead of the
/// bespoke expiry / GetTeamCapabilities gates.
///
/// Most team-registration tests exercise logic DOWNSTREAM of the gate, so they want the
/// authority wide open; expiry/closed-event tests want it to deny. These helpers keep the
/// per-test construction noise-free.
/// </summary>
public static class CapabilityMocks
{
    /// <summary>An authority that allows every create surface (door + toggles + preconditions
    /// all satisfied) — the default for tests that aren't about the gate itself.</summary>
    public static IJobRegistrationCapabilities Open() => For(new JobCapabilitySet
    {
        CanRegisterPlayer = true,
        CanRegisterStaff = true,
        CanRegisterReferee = true,
        CanRegisterRecruiter = true,
        CanAddTeam = true,
        CanRemoveTeam = true,
    });

    /// <summary>An authority that denies every create surface (fail-closed / concluded event).</summary>
    public static IJobRegistrationCapabilities Closed() => For(new JobCapabilitySet
    {
        CanRegisterPlayer = false,
        CanRegisterStaff = false,
        CanRegisterReferee = false,
        CanRegisterRecruiter = false,
        CanAddTeam = false,
        CanRemoveTeam = false,
    });

    /// <summary>An authority that returns the given set for any job/actor.</summary>
    public static IJobRegistrationCapabilities For(JobCapabilitySet set)
    {
        var mock = new Mock<IJobRegistrationCapabilities>();
        mock.Setup(c => c.ResolveAsync(It.IsAny<Guid>(), It.IsAny<CapabilityActor>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(set);
        return mock.Object;
    }
}
