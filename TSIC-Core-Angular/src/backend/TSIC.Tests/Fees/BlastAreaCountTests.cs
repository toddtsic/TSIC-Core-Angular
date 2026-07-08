using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Players;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Fees;

/// <summary>
/// The "blast area" counts surfaced to the admin BEFORE a fee/phase save. These guard that
/// the count equals what the canonical reprice engines would actually touch — same selection,
/// scoped the same way:
///   • players: active player registrations narrowed by team / agegroup-set / whole job,
///   • teams: eligible (non-WAITLIST/DROPPED) teams narrowed by team / agegroup-set / whole job.
/// A drift here would misinform the admin about how many registrations a money change hits.
/// </summary>
public class BlastAreaCountTests
{
    private static readonly Guid JobId = Guid.NewGuid();
    private static readonly Guid TeamA = Guid.NewGuid();
    private static readonly Guid TeamB = Guid.NewGuid();
    private static readonly Guid TeamC = Guid.NewGuid();
    private static readonly Guid TeamD = Guid.NewGuid();
    private static readonly Guid AgX = Guid.NewGuid();
    private static readonly Guid AgY = Guid.NewGuid();
    private static readonly Guid AgZ = Guid.NewGuid();

    // ── Players ──

    // Agegroup is resolved THROUGH the team — a player carries only AssignedTeamId and the
    // team's AgegroupId is the source of truth (Registrations.AssignedAgegroupId is obsolete).
    private static List<Teams> PlayerTeams() => new()
    {
        Team(TeamA, AgX, "U10"),
        Team(TeamB, AgX, "U10"),
        Team(TeamC, AgY, "U12"),
        Team(TeamD, AgZ, "U14"),
    };

    private static PlayerRegistrationService BuildPlayerService(List<Registrations> regs)
    {
        var regRepo = new Mock<IRegistrationRepository>();
        regRepo.Setup(r => r.GetActivePlayerRegistrationsByJobAsync(JobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(regs);

        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerTeams());

        return new PlayerRegistrationService(
            new Mock<ILogger<PlayerRegistrationService>>().Object,
            new Mock<IFeeResolutionService>().Object,
            new Mock<TSIC.API.Services.Shared.VerticalInsure.IVerticalInsureService>().Object,
            new Mock<ITeamLookupService>().Object,
            new Mock<IPlayerFormValidationService>().Object,
            regRepo.Object,
            teamRepo.Object,
            new Mock<IJobRepository>().Object,
            new Mock<ITeamPlacementService>().Object,
            new Mock<IMedFormService>().Object,
            new Mock<TSIC.API.Services.Shared.UsLax.IUsLaxService>().Object);
    }

    private static Registrations PlayerReg(Guid teamId) => new()
    {
        RegistrationId = Guid.NewGuid(),
        JobId = JobId,
        AssignedTeamId = teamId,
        BActive = true
    };

    // TeamA/AgX: 2 · TeamB/AgX: 1 · TeamC/AgY: 1 · TeamD/AgZ: 1  → 5 total
    private static List<Registrations> PlayerFixture() => new()
    {
        PlayerReg(TeamA),
        PlayerReg(TeamA),
        PlayerReg(TeamB),
        PlayerReg(TeamC),
        PlayerReg(TeamD),
    };

    [Fact(DisplayName = "Player blast: team scope counts only that team's active registrations")]
    public async Task PlayerCount_TeamScope_CountsThatTeam()
    {
        var svc = BuildPlayerService(PlayerFixture());
        // teamId wins even if an agegroup set is also supplied
        (await svc.CountActivePlayersInScopeAsync(JobId, new[] { AgX, AgY }, TeamA)).Should().Be(2);
        (await svc.CountActivePlayersInScopeAsync(JobId, null, TeamB)).Should().Be(1);
    }

    [Fact(DisplayName = "Player blast: agegroup-set scope counts across teams in those agegroups")]
    public async Task PlayerCount_AgegroupScope_CountsAcrossTeams()
    {
        var svc = BuildPlayerService(PlayerFixture());
        (await svc.CountActivePlayersInScopeAsync(JobId, new[] { AgX }, null)).Should().Be(3); // 2×TeamA + 1×TeamB
        (await svc.CountActivePlayersInScopeAsync(JobId, new[] { AgX, AgY }, null)).Should().Be(4); // + TeamC/AgY
    }

    [Fact(DisplayName = "Player blast: no scope = whole job")]
    public async Task PlayerCount_WholeJob_CountsAll()
    {
        var svc = BuildPlayerService(PlayerFixture());
        (await svc.CountActivePlayersInScopeAsync(JobId, null, null)).Should().Be(5);
    }

    // ── Teams ──

    private static TeamRegistrationService BuildTeamService(List<Teams> teams)
    {
        var teamRepo = new Mock<ITeamRepository>();
        teamRepo.Setup(t => t.GetTeamsWithDetailsForJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(teams);

        return new TeamRegistrationService(
            new Mock<ILogger<TeamRegistrationService>>().Object,
            new Mock<IClubRepRepository>().Object,
            new Mock<IClubRepository>().Object,
            new Mock<IJobRepository>().Object,
            new Mock<IJobLeagueRepository>().Object,
            new Mock<IAgeGroupRepository>().Object,
            teamRepo.Object,
            new Mock<IRegistrationRepository>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<ITokenService>().Object,
            new Mock<TSIC.API.Services.Invites.IInviteTokenService>().Object,
            MockUserManager(),
            new Mock<IFeeResolutionService>().Object,
            new Mock<TSIC.API.Services.Shared.TextSubstitution.ITextSubstitutionService>().Object,
            new Mock<IEmailService>().Object,
            new Mock<IJobDiscountCodeRepository>().Object,
            new Mock<IClubTeamRepository>().Object,
            new Mock<ITeamPlacementService>().Object,
            new Mock<IPaymentStateService>().Object,
            new Mock<IRegisteredTeamShaper>().Object,
            TSIC.Tests.Helpers.CapabilityMocks.Open(),
            new Mock<IJobPaymentFeaturesService>().Object);
    }

    private static Microsoft.AspNetCore.Identity.UserManager<TSIC.Infrastructure.Data.Identity.ApplicationUser> MockUserManager()
    {
        var store = new Mock<Microsoft.AspNetCore.Identity.IUserStore<TSIC.Infrastructure.Data.Identity.ApplicationUser>>();
        return new Mock<Microsoft.AspNetCore.Identity.UserManager<TSIC.Infrastructure.Data.Identity.ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!).Object;
    }

    private static Teams Team(Guid teamId, Guid agId, string agName) => new()
    {
        TeamId = teamId,
        AgegroupId = agId,
        Agegroup = new Agegroups { AgegroupId = agId, AgegroupName = agName }
    };

    // AgX "U10": TeamA, TeamB · AgY "U12": TeamC · plus a WAITLIST and a DROPPED team (excluded)
    private static List<Teams> TeamFixture() => new()
    {
        Team(TeamA, AgX, "U10"),
        Team(TeamB, AgX, "U10"),
        Team(TeamC, AgY, "U12"),
        Team(Guid.NewGuid(), Guid.NewGuid(), "WAITLIST"),
        Team(Guid.NewGuid(), Guid.NewGuid(), "DROPPED Teams"),
    };

    [Fact(DisplayName = "Team blast: whole job counts eligible teams only (WAITLIST/DROPPED excluded)")]
    public async Task TeamCount_WholeJob_ExcludesWaitlistAndDropped()
    {
        var svc = BuildTeamService(TeamFixture());
        (await svc.CountEligibleTeamsInScopeAsync(JobId, null, null)).Should().Be(3);
    }

    [Fact(DisplayName = "Team blast: agegroup-set scope counts eligible teams in those agegroups")]
    public async Task TeamCount_AgegroupScope_CountsInScope()
    {
        var svc = BuildTeamService(TeamFixture());
        (await svc.CountEligibleTeamsInScopeAsync(JobId, new[] { AgX }, null)).Should().Be(2);
        (await svc.CountEligibleTeamsInScopeAsync(JobId, new[] { AgY }, null)).Should().Be(1);
    }

    [Fact(DisplayName = "Team blast: team scope counts the single team")]
    public async Task TeamCount_TeamScope_CountsOne()
    {
        var svc = BuildTeamService(TeamFixture());
        (await svc.CountEligibleTeamsInScopeAsync(JobId, null, TeamA)).Should().Be(1);
    }
}
