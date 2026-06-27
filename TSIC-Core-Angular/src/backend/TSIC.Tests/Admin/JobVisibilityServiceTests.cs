using FluentAssertions;
using Moq;
using TSIC.API.Services.Admin;
using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.Tests.Admin;

/// <summary>
/// QUICK-LINKS VISIBILITY WRITE GATE TESTS
///
/// The Quick Links editor (api/job-visibility) is AdminOnly (Director/SuperDirector/
/// SuperUser). Three of its flags — EnableStore, OfferPlayerInsurance, OfferTeamInsurance —
/// are SuperUser-only everywhere else (gated in JobConfigService). JobVisibilityService.UpdateAsync
/// must apply those three ONLY when isSuperUser is true, so opening the endpoint to the admin
/// tier does not escalate insurance/store control to Directors. The other seven flags are
/// admin-editable and must apply regardless.
/// </summary>
public class JobVisibilityServiceTests
{
    private static readonly Guid TestJobId = Guid.NewGuid();

    private static (JobVisibilityService Svc, Jobs Job) CreateService()
    {
        // Every gated flag starts false so an applied write is unambiguous.
        var job = new Jobs
        {
            JobId = TestJobId,
            BRegistrationAllowPlayer = false,
            BEnableStore = false,
            BOfferPlayerRegsaverInsurance = false,
            BOfferTeamRegsaverInsurance = false,
        };

        var repo = new Mock<IJobConfigRepository>();
        repo.Setup(r => r.GetJobTrackedAsync(TestJobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return (new JobVisibilityService(repo.Object), job);
    }

    /// <summary>
    /// A request that turns ON one admin flag AND all three SuperUser-only flags.
    /// </summary>
    private static UpdateJobVisibilityRequest AllOnRequest() => new()
    {
        AllowPlayerRegistration = true,
        EnableStore = true,
        OfferPlayerInsurance = true,
        OfferTeamInsurance = true,
    };

    [Fact]
    public async Task UpdateAsync_NonSuperUser_IgnoresSuperUserOnlyFlags_ButAppliesAdminFlags()
    {
        var (svc, job) = CreateService();

        await svc.UpdateAsync(TestJobId, AllOnRequest(), isSuperUser: false);

        // Admin flag applied...
        job.BRegistrationAllowPlayer.Should().BeTrue("AllowPlayerRegistration is admin-editable");
        // ...but the three SuperUser-only flags are ignored for a non-super caller.
        job.BEnableStore.Should().BeFalse("EnableStore is SuperUser-only");
        job.BOfferPlayerRegsaverInsurance.Should().BeFalse("OfferPlayerInsurance is SuperUser-only");
        job.BOfferTeamRegsaverInsurance.Should().BeFalse("OfferTeamInsurance is SuperUser-only");
    }

    [Fact]
    public async Task UpdateAsync_SuperUser_AppliesSuperUserOnlyFlags()
    {
        var (svc, job) = CreateService();

        await svc.UpdateAsync(TestJobId, AllOnRequest(), isSuperUser: true);

        job.BRegistrationAllowPlayer.Should().BeTrue();
        job.BEnableStore.Should().BeTrue("EnableStore applies for a SuperUser");
        job.BOfferPlayerRegsaverInsurance.Should().BeTrue("OfferPlayerInsurance applies for a SuperUser");
        job.BOfferTeamRegsaverInsurance.Should().BeTrue("OfferTeamInsurance applies for a SuperUser");
    }
}
