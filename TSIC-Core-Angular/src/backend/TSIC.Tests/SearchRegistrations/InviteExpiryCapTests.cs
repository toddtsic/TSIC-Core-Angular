using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Repositories;
using TSIC.Tests.Helpers;

namespace TSIC.Tests.SearchRegistrations;

/// <summary>
/// INVITE EXPIRY CAP
///
/// An invite's lifetime is its exposure window. The batch-email modal offers at most 72 hours, but the
/// request field is a bare int — StartBatchEmailAsync refuses anything longer so a hand-built request
/// cannot mint a months-long invite.
/// </summary>
public class InviteExpiryCapTests
{
    private const string CapMessage = "at most 72 hours";

    private static async Task<(RegistrationSearchService svc, Guid jobId, Guid regId)> CreateAsync()
    {
        var ctx = DbContextFactory.Create();
        var b = new SearchDataBuilder(ctx);
        var job = b.AddJob();
        var role = b.AddRole(RoleConstants.ClubRep, RoleConstants.Names.ClubRepName);
        var user = b.AddUser("Cara", "Rep");
        var reg = b.AddRegistration(job.JobId, user.Id, role.Id, feeTotal: 0m);
        await b.SaveAsync();

        return (BuildService(ctx, new Mock<IJobRepository>().Object), job.JobId, reg.RegistrationId);
    }

    /// <summary>The batch-send service over <paramref name="ctx"/>; everything the invite checks don't read is mocked.</summary>
    internal static RegistrationSearchService BuildService(
        Infrastructure.Data.SqlDbContext.SqlDbContext ctx, IJobRepository jobRepo)
    {
        var deviceRepo = new Mock<IDeviceRepository>();
        deviceRepo.Setup(d => d.GetDeviceRegistrationIdsByRegistrationAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DeviceRegistrationIds>());

        return new RegistrationSearchService(
            new RegistrationRepository(ctx), new RegistrationAccountingRepository(ctx),
            jobRepo, new FamiliesRepository(ctx), deviceRepo.Object,
            new TeamRepository(ctx), new Mock<IAdnApiService>().Object,
            new Mock<IAdnReversalService>().Object, new Mock<IArbSubscriptionRepository>().Object,
            new Mock<ITextSubstitutionService>().Object,
            new Mock<IEmailBatchService>().Object,
            new Mock<IRegistrationFeeAdjustmentService>().Object,
            new Mock<IPaymentService>().Object,
            new Mock<IPaymentStateService>().Object,
            new Mock<IRegisteredPlayerShaper>().Object,
            new Mock<IUserRepository>().Object,
            new Mock<TSIC.API.Services.Shared.UsLax.IUsLaxService>().Object,
            new Mock<IPlayerRegConfirmationService>().Object,
            new Mock<IAdultRegistrationService>().Object,
            new Mock<TSIC.API.Services.Teams.ITeamRegistrationService>().Object,
            new Mock<IInvitationRepository>().Object,
            new Mock<ILogger<RegistrationSearchService>>().Object);
    }

    private static BatchEmailRequest InviteRequest(Guid regId, int? hours) => new()
    {
        RegistrationIds = [regId],
        Subject = "Pre-register",
        BodyTemplate = "!CLUBREP_INVITE_LINK",
        InviteLinkTargetJobId = Guid.NewGuid(),
        InviteExpiryHours = hours
    };

    [Theory(DisplayName = "Invite lifetime over 72 hours is refused")]
    [InlineData(73)]
    [InlineData(24 * 30)]
    [InlineData(int.MaxValue)]
    public async Task OverCap_Refused(int hours)
    {
        var (svc, jobId, regId) = await CreateAsync();

        var act = () => svc.StartBatchEmailAsync(jobId, "admin-1", InviteRequest(regId, hours));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{CapMessage}*");
    }

    [Theory(DisplayName = "Every lifetime the modal offers (and unset → 24h) passes the cap")]
    [InlineData(6)]
    [InlineData(72)]
    [InlineData(null)]
    public async Task WithinCap_NotRefusedByCap(int? hours)
    {
        var (svc, jobId, regId) = await CreateAsync();

        // The send continues past the cap into mocked collaborators, which may fail for unrelated
        // reasons. The assertion is only that the cap did not refuse it.
        Exception? thrown = null;
        try { await svc.StartBatchEmailAsync(jobId, "admin-1", InviteRequest(regId, hours)); }
        catch (Exception ex) { thrown = ex; }

        (thrown?.Message ?? "").Should().NotContain(CapMessage);
    }

    [Fact(DisplayName = "The cap applies only to invite sends: a plain batch email ignores InviteExpiryHours")]
    public async Task NonInvite_IgnoresExpiryHours()
    {
        var (svc, jobId, regId) = await CreateAsync();
        var request = InviteRequest(regId, 9999) with { InviteLinkTargetJobId = null };

        Exception? thrown = null;
        try { await svc.StartBatchEmailAsync(jobId, "admin-1", request); }
        catch (Exception ex) { thrown = ex; }

        (thrown?.Message ?? "").Should().NotContain(CapMessage);
    }
}
