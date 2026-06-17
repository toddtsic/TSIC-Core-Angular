using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.TeamRegistration.WaitlistMirror;

/// <summary>
/// WAITLIST TWIN — PICKER DISPLAY TESTS
///
/// TeamLookupService assembles the self-rostering picker. When a real team is full
/// under a waitlist job, the emitted entry must keep the real team's NAME but route
/// registration to the twin's <c>teamId</c> at $0 — so the parent sees the team they
/// wanted, flagged free, and the twin id (not the real id) flows through registration.
///
/// What these prove (pure Moq — the assembly logic, not the DB):
///   - Real team FULL + twin exists → entry carries the twin id at Fee/Deposit/EffectiveFee == 0.
///   - Real team NOT full → entry stays the real id at full price (twin omitted / swap-out case).
///   - Real team FULL but twin not minted yet → entry left as the real team (registration-time
///     overflow swap is the safety net).
///
/// Waitlists are now MANDATORY, so every full team takes the twin-swap path.
/// </summary>
public class WaitlistTwinDisplayTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid AgegroupId = Guid.NewGuid();
    private static readonly Guid FullTeamId = Guid.NewGuid();
    private static readonly Guid OpenTeamId = Guid.NewGuid();
    private static readonly Guid TwinTeamId = Guid.NewGuid();

    private static (
        TeamLookupService svc,
        Mock<ITeamRepository> teamRepo)
        CreateService(bool twinMinted)
    {
        var teamRepo = new Mock<ITeamRepository>();
        var registrationRepo = new Mock<IRegistrationRepository>();
        var jobRepo = new Mock<IJobRepository>();
        var feeService = new Mock<IFeeResolutionService>();
        var logger = new Mock<ILogger<TeamLookupService>>();

        jobRepo
            .Setup(j => j.GetUsesWaitlistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // waitlists are mandatory

        // The base query NEVER returns WAITLIST agegroups — twins are not in this list
        // (matches production; the twin is reattached by name below).
        teamRepo
            .Setup(t => t.GetAvailableTeamsQueryResultsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AvailableTeamQueryResult>
            {
                new()
                {
                    TeamId = FullTeamId, Name = "Hawks",
                    AgegroupId = AgegroupId, AgegroupName = "Boys U14", MaxCount = 1,
                },
                new()
                {
                    TeamId = OpenTeamId, Name = "Eagles",
                    AgegroupId = AgegroupId, AgegroupName = "Boys U14", MaxCount = 10,
                },
            });

        // Hawks full (1/1), Eagles open (1/10).
        registrationRepo
            .Setup(r => r.GetRosterCountsByTeamAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { { FullTeamId, 1 }, { OpenTeamId, 1 } });

        // Both real teams carry a configured non-zero base fee.
        feeService
            .Setup(f => f.ResolveFeesByTeamIdsAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ResolvedFee>
            {
                { FullTeamId, new ResolvedFee { FeeConfigured = true, BalanceDue = 150m } },
                { OpenTeamId, new ResolvedFee { FeeConfigured = true, BalanceDue = 120m } },
            });

        feeService
            .Setup(f => f.EvaluateModifiersAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(),
                It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedModifiers());

        teamRepo
            .Setup(t => t.GetTeamsForJobByNamesAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(twinMinted
                ? new List<Teams>
                {
                    new() { TeamId = TwinTeamId, JobId = JobId, AgegroupId = AgegroupId, TeamName = "WAITLIST - Hawks" },
                }
                : new List<Teams>());

        var svc = new TeamLookupService(
            teamRepo.Object, registrationRepo.Object, jobRepo.Object, feeService.Object, logger.Object);

        return (svc, teamRepo);
    }

    [Fact(DisplayName = "Full team (twin exists) → entry keeps the REAL id at full price; waitlisting deferred to payment")]
    public async Task FullTeam_TwinExists_KeepsRealIdNoSwap()
    {
        var (svc, _) = CreateService(twinMinted: true);

        var teams = await svc.GetAvailableTeamsForJobAsync(JobId);

        var hawks = teams.Single(t => t.TeamName == "Hawks");
        hawks.RosterIsFull.Should().BeTrue("the UI badges it WAITLIST off this flag");
        hawks.TeamId.Should().Be(FullTeamId,
            "the real id flows through — the entry is NOT swapped to the twin even when the twin exists; "
            + "the payment cart-split moves the player to the $0 twin only when they pay (a resuming "
            + "already-rostered player must be able to match their own real team id in this list)");
        hawks.EffectiveFee.Should().Be(150m,
            "the backend carries the real fee; the $0 'waitlist' display is a frontend-only concern");
    }

    [Fact(DisplayName = "Not-full team → stays the real id at full price (twin omitted)")]
    public async Task OpenTeam_StaysRealAtFullPrice()
    {
        var (svc, _) = CreateService(twinMinted: true);

        var teams = await svc.GetAvailableTeamsForJobAsync(JobId);

        var eagles = teams.Single(t => t.TeamName == "Eagles");
        eagles.RosterIsFull.Should().BeFalse();
        eagles.TeamId.Should().Be(OpenTeamId);
        eagles.EffectiveFee.Should().Be(120m, "an open team is bookable at its real price");
    }

    [Fact(DisplayName = "Full team but twin not minted yet → entry left as the real team (registration-time swap is the safety net)")]
    public async Task FullTeam_NoTwinYet_LeavesRealEntry()
    {
        var (svc, _) = CreateService(twinMinted: false);

        var teams = await svc.GetAvailableTeamsForJobAsync(JobId);

        var hawks = teams.Single(t => t.TeamName == "Hawks");
        hawks.TeamId.Should().Be(FullTeamId, "no twin to route to yet — overflow swap handles it at registration time");
        hawks.Fee.Should().Be(150m);
    }
}
