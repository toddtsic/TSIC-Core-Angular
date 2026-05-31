using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// CLUB REP DELETE GUARDS
///
/// A Superuser may delete a Club Rep registration, but only when:
///   - the caller is Superuser (Directors/SuperDirectors are rejected), and
///   - no team references the rep via Teams.ClubrepRegistrationid (the FK that would
///     otherwise break referential integrity) — active OR inactive.
/// </summary>
public class ClubRepDeleteTests
{
    private static (RegistrationSearchService svc, SearchDataBuilder b, Guid jobId)
        CreateService()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();

        var registrationRepo = new RegistrationRepository(ctx);
        var accountingRepo = new RegistrationAccountingRepository(ctx);
        var familiesRepo = new FamiliesRepository(ctx);
        var teamRepo = new TeamRepository(ctx);

        var deviceRepo = new Mock<IDeviceRepository>();
        deviceRepo.Setup(d => d.GetDeviceRegistrationIdsByRegistrationAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceRegistrationIds>());

        var svc = new RegistrationSearchService(
            registrationRepo, accountingRepo,
            new Mock<IJobRepository>().Object, familiesRepo, deviceRepo.Object,
            teamRepo, new Mock<IAdnApiService>().Object, new Mock<IArbSubscriptionRepository>().Object,
            new Mock<ITextSubstitutionService>().Object, new Mock<IEmailService>().Object,
            new Mock<IRegistrationFeeAdjustmentService>().Object,
            new Mock<IPaymentService>().Object,
            new Mock<IPaymentStateService>().Object,
            new Mock<IFeeResolutionService>().Object,
            new Mock<ILogger<RegistrationSearchService>>().Object);

        return (svc, b, job.JobId);
    }

    [Fact(DisplayName = "Club Rep with no teams → Superuser delete succeeds")]
    public async Task ClubRep_NoTeams_Superuser_Succeeds()
    {
        var (svc, b, jobId) = CreateService();
        var role = b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        var user = b.AddUser("Cara", "Rep");
        var reg = b.AddRegistration(jobId, user.Id, role.Id, feeTotal: 0m);
        await b.SaveAsync();

        var result = await svc.DeleteRegistrationAsync(
            jobId, userId: "admin-1", callerRole: RoleConstants.Names.SuperuserName,
            registrationId: reg.RegistrationId);

        result.Success.Should().BeTrue();
    }

    [Fact(DisplayName = "Club Rep with an attached team → delete blocked")]
    public async Task ClubRep_WithTeam_Blocked()
    {
        var (svc, b, jobId) = CreateService();
        var role = b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        var user = b.AddUser("Cara", "Rep");
        var reg = b.AddRegistration(jobId, user.Id, role.Id, feeTotal: 0m);
        b.AddTeam(jobId, Guid.NewGuid(), Guid.NewGuid(), clubRepRegistrationId: reg.RegistrationId);
        await b.SaveAsync();

        var result = await svc.DeleteRegistrationAsync(
            jobId, userId: "admin-1", callerRole: RoleConstants.Names.SuperuserName,
            registrationId: reg.RegistrationId);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("team");
    }

    [Fact(DisplayName = "Club Rep with only an inactive team → delete still blocked (FK scope)")]
    public async Task ClubRep_WithInactiveTeam_Blocked()
    {
        var (svc, b, jobId) = CreateService();
        var role = b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        var user = b.AddUser("Cara", "Rep");
        var reg = b.AddRegistration(jobId, user.Id, role.Id, feeTotal: 0m);
        b.AddTeam(jobId, Guid.NewGuid(), Guid.NewGuid(), active: false, clubRepRegistrationId: reg.RegistrationId);
        await b.SaveAsync();

        var result = await svc.DeleteRegistrationAsync(
            jobId, userId: "admin-1", callerRole: RoleConstants.Names.SuperuserName,
            registrationId: reg.RegistrationId);

        result.Success.Should().BeFalse();
    }

    [Fact(DisplayName = "Club Rep delete by non-Superuser → rejected")]
    public async Task ClubRep_NonSuperuser_Rejected()
    {
        var (svc, b, jobId) = CreateService();
        var role = b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        var user = b.AddUser("Cara", "Rep");
        var reg = b.AddRegistration(jobId, user.Id, role.Id, feeTotal: 0m);
        await b.SaveAsync();

        var result = await svc.DeleteRegistrationAsync(
            jobId, userId: "dir-1", callerRole: RoleConstants.Names.DirectorName,
            registrationId: reg.RegistrationId);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Superuser");
    }
}
