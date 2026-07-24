using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.Tests.TeamRegistration.ClubRepSync;

/// <summary>
/// CLUB REP FINANCIAL SYNC TESTS — register/unregister
///
/// Pins the contract that TeamRegistrationService re-aggregates the rep
/// registration row whenever a team is added or removed. Without this,
/// clubRep.OwedTotal drifts from the sum of its teams and downstream
/// guards (e.g. admin check-record owed cap) reject valid payments.
///
/// Regression: a rep registered 3 teams in separate sessions; the
/// second-and-third register paths never synced, leaving the rep's
/// OwedTotal frozen at a prior partial-sum. Admin's $3,100 check was
/// rejected with "exceeds amount owed ($1,401.30)" even though the
/// teams summed to $3,217.80.
/// </summary>
public class RegisterTeamSyncTests
{
    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly Guid TestRegId = Guid.NewGuid();
    private static readonly Guid TestAgegroupId = Guid.NewGuid();
    private static readonly Guid TestLeagueId = Guid.NewGuid();
    private const string TestUserId = "clubrep-user-1";
    private const string TestClubName = "Test FC";

    private static (TeamRegistrationService svc, Mock<IRegistrationRepository> regRepo, Mock<ITeamRepository> teamRepo)
        CreateService()
    {
        var logger = new Mock<ILogger<TeamRegistrationService>>();
        var clubReps = new Mock<IClubRepRepository>();
        var clubs = new Mock<IClubRepository>();
        var jobs = new Mock<IJobRepository>();
        var jobLeagues = new Mock<IJobLeagueRepository>();
        var agRepo = new Mock<IAgeGroupRepository>();
        var teamRepo = new Mock<ITeamRepository>();
        var regRepo = new Mock<IRegistrationRepository>();
        var users = new Mock<IUserRepository>();
        var tokenService = new Mock<ITokenService>();
        var feeService = new Mock<IFeeResolutionService>();
        var textSubstitution = new Mock<ITextSubstitutionService>();
        var emailService = new Mock<IEmailService>();
        var discountCodeRepo = new Mock<IJobDiscountCodeRepository>();
        var clubTeams = new Mock<IClubTeamRepository>();
        var placement = new Mock<ITeamPlacementService>();
        var paymentState = new Mock<IPaymentStateService>();

        var userStore = new Mock<IUserPasswordStore<ApplicationUser>>();
        userStore.As<IUserStore<ApplicationUser>>();
        var userManager = new UserManager<ApplicationUser>(
            userStore.Object, null!, new PasswordHasher<ApplicationUser>(),
            null!, null!, null!, null!, null!, null!);

        regRepo
            .Setup(r => r.GetByIdAsync(TestRegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Registrations
            {
                RegistrationId = TestRegId,
                JobId = TestJobId,
                UserId = TestUserId,
                RoleId = RoleConstants.ClubRep,
                ClubName = TestClubName,
                Modified = DateTime.UtcNow,
            });

        jobs
            .Setup(j => j.GetJobFeeSettingsAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobFeeSettings { PaymentMethodsAllowedCode = 1 });

        // Team add/remove capability now comes from the create authority (CapabilityMocks.Open
        // is passed to the service ctor below), not the retired GetTeamCapabilitiesAsync.

        feeService
            .Setup(f => f.GetEffectiveProcessingRateAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        clubs
            .Setup(c => c.GetByNameAsync(TestClubName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clubs { ClubId = 1, ClubName = TestClubName });

        clubReps
            .Setup(cr => cr.ExistsAsync(TestUserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        jobLeagues
            .Setup(jl => jl.GetPrimaryLeagueForJobAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestLeagueId);

        teamRepo
            .Setup(t => t.GetTeamsByClubExcludingRegistrationAsync(
                TestJobId, 1, TestRegId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TeamWithRegistrationInfo>());

        agRepo
            .Setup(a => a.GetByIdAsync(TestAgegroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Agegroups
            {
                AgegroupId = TestAgegroupId,
                LeagueId = TestLeagueId,
                AgegroupName = "Boys U14",
                MaxTeams = 100,
                MaxTeamsPerClub = 0,
                Modified = DateTime.UtcNow,
            });

        teamRepo
            .Setup(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        placement
            .Setup(p => p.ResolvePlacementAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TeamPlacementResult
            {
                AgegroupId = TestAgegroupId,
                LeagueId = TestLeagueId,
                IsWaitlisted = false,
            });

        var svc = new TeamRegistrationService(
            logger.Object, clubReps.Object, clubs.Object, jobs.Object,
            jobLeagues.Object, agRepo.Object, teamRepo.Object, regRepo.Object,
            users.Object, tokenService.Object, new Mock<TSIC.API.Services.Invites.IInviteTokenService>().Object, userManager, feeService.Object,
            textSubstitution.Object, emailService.Object, discountCodeRepo.Object,
            clubTeams.Object, placement.Object, paymentState.Object,
            new Mock<IRegisteredTeamShaper>().Object,
            TSIC.Tests.Helpers.CapabilityMocks.Open(),
            new Mock<IJobPaymentFeaturesService>().Object,
            new Mock<TSIC.API.Services.Teams.ITeamRenameService>().Object);

        return (svc, regRepo, teamRepo);
    }

    private static RegisterTeamRequest MakeRequest() => new()
    {
        AgeGroupId = TestAgegroupId,
        TeamName = "New Team",
        ClubTeamGradYear = "2030",
        LevelOfPlay = "A",
    };

    [Fact(DisplayName = "RegisterTeam: success → SynchronizeClubRepFinancialsAsync called exactly once with rep id + user id")]
    public async Task RegisterTeam_Success_SyncsClubRep()
    {
        var (svc, regRepo, _) = CreateService();

        var result = await svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        result.Success.Should().BeTrue();
        regRepo.Verify(
            r => r.SynchronizeClubRepFinancialsAsync(TestRegId, TestUserId, It.IsAny<CancellationToken>()),
            Times.Once,
            "register-team must re-aggregate the rep row so clubRep.OwedTotal reflects the new team");
    }

    [Fact(DisplayName = "UnregisterTeam: success → SynchronizeClubRepFinancialsAsync called exactly once")]
    public async Task UnregisterTeam_Success_SyncsClubRep()
    {
        var (svc, regRepo, teamRepo) = CreateService();

        var teamId = Guid.NewGuid();
        teamRepo
            .Setup(t => t.GetTeamFromTeamId(teamId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Teams
            {
                TeamId = teamId,
                JobId = TestJobId,
                ClubrepRegistrationid = TestRegId,
                PaidTotal = 0m,
                Active = true,
                Modified = DateTime.UtcNow,
            });

        await svc.UnregisterTeamFromEventAsync(teamId, TestUserId);

        regRepo.Verify(
            r => r.SynchronizeClubRepFinancialsAsync(TestRegId, TestUserId, It.IsAny<CancellationToken>()),
            Times.Once,
            "unregister-team must re-aggregate so clubRep.OwedTotal drops the removed team's contribution");
    }
}
