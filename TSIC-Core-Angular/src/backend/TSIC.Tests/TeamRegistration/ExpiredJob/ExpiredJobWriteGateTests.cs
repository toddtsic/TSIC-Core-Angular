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

namespace TSIC.Tests.TeamRegistration.ExpiredJob;

/// <summary>
/// EXPIRED-JOB WRITE GATE TESTS
///
/// A job's ExpiryUsers is the canonical "doors closed for non-admin users" signal
/// (IJobRepository.IsJobExpiredForUsersAsync). Historically it was enforced only on the
/// read side (Phase-1 role list), so a club rep with a straddling session — or a direct
/// API call carrying the jobPath — could still mint a Phase-2 token, add teams, and remove
/// teams on an EXPIRED job.
///
/// These tests pin that every club-rep write path now refuses an expired job. Admins are
/// unaffected: they reach these endpoints via separate role-gated flows bounded by ExpiryAdmin.
/// </summary>
public class ExpiredJobWriteGateTests
{
    private static readonly Guid TestJobId = Guid.NewGuid();
    private static readonly Guid TestRegId = Guid.NewGuid();
    private const string TestUserId = "clubrep-user-1";
    private const string TestClubName = "Test FC";
    private const string TestJobPath = "test-tourney-2026";

    private sealed class Mocks
    {
        public required TeamRegistrationService Svc { get; init; }
        public required Mock<IJobRepository> Jobs { get; init; }
        public required Mock<ITeamRepository> Teams { get; init; }
        public required Mock<IRegistrationRepository> Regs { get; init; }
    }

    /// <param name="expired">What IsJobExpiredForUsersAsync returns for the test job.</param>
    private static Mocks CreateService(bool expired)
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

        // The one fact under test.
        jobs
            .Setup(j => j.IsJobExpiredForUsersAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expired);

        // ── Wiring sufficient to reach each write path's expiry gate ──
        var userStore = new Mock<IUserPasswordStore<ApplicationUser>>();
        userStore.As<IUserStore<ApplicationUser>>()
            .Setup(s => s.FindByIdAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationUser { Id = TestUserId, UserName = TestUserId });
        var userManager = new UserManager<ApplicationUser>(
            userStore.Object, null!, new PasswordHasher<ApplicationUser>(),
            null!, null!, null!, null!, null!, null!);

        // InitializeRegistrationAsync prerequisites
        clubReps
            .Setup(cr => cr.GetClubsForUserAsync(TestUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ClubWithUsageInfo>
            {
                new() { ClubId = 1, ClubName = TestClubName, IsInUse = true },
            });
        jobs
            .Setup(j => j.GetJobIdByPathAsync(TestJobPath))
            .ReturnsAsync(TestJobId);

        // RegisterTeamForEventAsync prerequisites
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
        feeService
            .Setup(f => f.GetEffectiveProcessingRateAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        clubs
            .Setup(c => c.GetByNameAsync(TestClubName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clubs { ClubId = 1, ClubName = TestClubName });
        clubReps
            .Setup(cr => cr.ExistsAsync(TestUserId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // initialize still consults the ExpiryUsers ENTRY door (IsJobExpiredForUsersAsync, set
        // above) — it is the SETTLE/manage gateway, not a create. register-team / unregister-team
        // now consult the create AUTHORITY, so mirror the expired state there too: an expired
        // job is also a concluded one, so the authority denies every create surface.
        var capabilities = expired
            ? TSIC.Tests.Helpers.CapabilityMocks.Closed()
            : TSIC.Tests.Helpers.CapabilityMocks.Open();

        var svc = new TeamRegistrationService(
            logger.Object, clubReps.Object, clubs.Object, jobs.Object,
            jobLeagues.Object, agRepo.Object, teamRepo.Object, regRepo.Object,
            users.Object, tokenService.Object, new Mock<TSIC.API.Services.Invites.IInviteTokenService>().Object, userManager, feeService.Object,
            textSubstitution.Object, emailService.Object, discountCodeRepo.Object,
            clubTeams.Object, placement.Object, paymentState.Object,
            new Mock<IRegisteredTeamShaper>().Object,
            capabilities,
            new Mock<IJobPaymentFeaturesService>().Object);

        return new Mocks { Svc = svc, Jobs = jobs, Teams = teamRepo, Regs = regRepo };
    }

    private static RegisterTeamRequest MakeRequest() => new()
    {
        AgeGroupId = Guid.NewGuid(),
        TeamName = "New Team",
        ClubTeamGradYear = "2030",
        LevelOfPlay = "A",
    };

    [Fact(DisplayName = "InitializeRegistration: expired job → throws, no Phase-2 token minted")]
    public async Task InitializeRegistration_Expired_Throws()
    {
        var m = CreateService(expired: true);

        var act = () => m.Svc.InitializeRegistrationAsync(TestUserId, TestClubName, TestJobPath);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*closed*");
    }

    [Fact(DisplayName = "RegisterTeam: expired job → throws before any team is created")]
    public async Task RegisterTeam_Expired_Throws()
    {
        var m = CreateService(expired: true);

        var act = () => m.Svc.RegisterTeamForEventAsync(MakeRequest(), TestRegId, TestUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*closed*");
        m.Teams.Verify(t => t.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "UnregisterTeam: expired job → throws before the team is removed")]
    public async Task UnregisterTeam_Expired_Throws()
    {
        var m = CreateService(expired: true);

        var teamId = Guid.NewGuid();
        m.Teams
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

        var act = () => m.Svc.UnregisterTeamFromEventAsync(teamId, TestUserId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*closed*");
        m.Regs.Verify(
            r => r.SynchronizeClubRepFinancialsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
