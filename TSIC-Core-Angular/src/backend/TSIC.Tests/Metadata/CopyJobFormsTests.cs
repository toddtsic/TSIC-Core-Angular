using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TSIC.API.Services.Metadata;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.Tests.Metadata;

/// <summary>
/// Copy-forms service: copies another job's player and/or adult (coach) form JSON onto the current job.
/// The repository is mocked — the service only orchestrates existing read/write calls, so we assert on
/// which writes fire, that the target jobId is the current job's, and that a missing source form aborts
/// before ANY write (never a partial copy).
/// </summary>
public class CopyJobFormsTests
{
    private const string PlayerJson = "{\"fields\":[{\"name\":\"jerseyNumber\"}]}";
    private const string AdultJson =
        "{\"UnassignedAdult\":{\"fields\":[{\"name\":\"jerseySize\"}]},\"Referee\":{\"fields\":[]},\"Recruiter\":{\"fields\":[]}}";

    private static ProfileMetadataMigrationService BuildService(
        Guid regId, Guid targetJobId, Guid sourceJobId, out Mock<IProfileMetadataRepository> repo,
        string? sourcePlayerJson = PlayerJson, string? sourceAdultJson = AdultJson)
    {
        repo = new Mock<IProfileMetadataRepository>();
        repo.Setup(r => r.GetJobDataForRegistrationAsync(regId))
            .ReturnsAsync(new RegistrationJobProjection { JobId = targetJobId });
        repo.Setup(r => r.GetJobBasicInfoAsync(sourceJobId))
            .ReturnsAsync(new JobBasicInfo
            {
                JobName = "Source Job",
                PlayerProfileMetadataJson = sourcePlayerJson,
                AdultProfileMetadataJson = sourceAdultJson
            });
        repo.Setup(r => r.UpdateJobPlayerMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateJobAdultMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        return new ProfileMetadataMigrationService(
            repo.Object,
            Mock.Of<IGitHubProfileFetcher>(),
            new CSharpToMetadataParser(NullLogger<CSharpToMetadataParser>.Instance),
            NullLogger<ProfileMetadataMigrationService>.Instance);
    }

    private static CopyJobFormsRequest Req(Guid sourceJobId, bool player) =>
        new() { SourceJobId = sourceJobId, IncludePlayer = player };

    /// <summary>
    /// Adult/coach forms are NOT copyable: a job's adult form is its RegformName_Coach identity, and
    /// Configure → Job → Adult is the single writer (it sets the identity and the blob together).
    /// Copy Forms only ever wrote the blob, so it was a second, half-complete writer. These tests lock
    /// in that the adult write never fires from this path.
    /// </summary>
    [Fact]
    public async Task Copy_Player_WritesPlayerFormAndNeverTouchesAdult()
    {
        var regId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        var svc = BuildService(regId, targetJobId, sourceJobId, out var repo);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(sourceJobId, player: true));

        result.Success.Should().BeTrue();
        result.PlayerCopied.Should().BeTrue();
        result.SourceJobName.Should().Be("Source Job");
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(targetJobId, PlayerJson), Times.Once);
        repo.Verify(r => r.UpdateJobAdultMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_PlayerFormMissingOnSource_FailsWithoutWriting()
    {
        var regId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        var svc = BuildService(regId, targetJobId, sourceJobId, out var repo, sourcePlayerJson: null);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(sourceJobId, player: true));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("player");
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        repo.Verify(r => r.UpdateJobAdultMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_SourceIsCurrentJob_Fails()
    {
        var regId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var svc = BuildService(regId, jobId, jobId, out var repo); // source == target

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(jobId, player: true));

        result.Success.Should().BeFalse();
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_NothingSelected_FailsBeforeAnyRepoCall()
    {
        var regId = Guid.NewGuid();
        var svc = BuildService(regId, Guid.NewGuid(), Guid.NewGuid(), out var repo);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(Guid.NewGuid(), player: false));

        result.Success.Should().BeFalse();
        repo.Verify(r => r.GetJobDataForRegistrationAsync(It.IsAny<Guid>()), Times.Never);
    }

    private static ProfileMetadataMigrationService BuildSourcesService(
        Guid regId, Guid currentJobId,
        List<JobForProfileSummary> playerJobs,
        List<JobForAdultProfileSummary> adultJobs)
    {
        var repo = new Mock<IProfileMetadataRepository>();
        repo.Setup(r => r.GetJobDataForRegistrationAsync(regId))
            .ReturnsAsync(new RegistrationJobProjection { JobId = currentJobId });
        repo.Setup(r => r.GetJobsForProfileSummaryAsync()).ReturnsAsync(playerJobs);
        repo.Setup(r => r.GetJobsForAdultProfileSummaryAsync()).ReturnsAsync(adultJobs);

        return new ProfileMetadataMigrationService(
            repo.Object,
            Mock.Of<IGitHubProfileFetcher>(),
            new CSharpToMetadataParser(NullLogger<CSharpToMetadataParser>.Instance),
            NullLogger<ProfileMetadataMigrationService>.Instance);
    }

    [Fact]
    public async Task CopySources_ExcludesCurrentAndFormlessJobs_OrdersByName()
    {
        var regId = Guid.NewGuid();
        var current = Guid.NewGuid();
        var jobA = Guid.NewGuid();
        var jobB = Guid.NewGuid();
        var jobC = Guid.NewGuid();
        var jobD = Guid.NewGuid();

        var players = new List<JobForProfileSummary>
        {
            new() { JobId = jobB, JobName = "Bravo", PlayerProfileMetadataJson = PlayerJson },
            new() { JobId = jobA, JobName = "Alpha", PlayerProfileMetadataJson = PlayerJson },
            new() { JobId = jobD, JobName = "Delta", PlayerProfileMetadataJson = null },      // no form → excluded
            new() { JobId = current, JobName = "Current", PlayerProfileMetadataJson = PlayerJson }, // self → excluded
        };
        var adults = new List<JobForAdultProfileSummary>
        {
            new() { JobId = jobA, JobName = "Alpha", Year = "2025", AdultProfileMetadataJson = AdultJson },
            new() { JobId = jobC, JobName = "Charlie", Year = "2024", AdultProfileMetadataJson = AdultJson },
        };

        var svc = BuildSourcesService(regId, current, players, adults);

        var result = await svc.GetCopyFormSourcesAsync(regId);

        // Only player forms are copyable now, so a source must carry one: Alpha and Bravo. Delta has no
        // player form, Current is self, and Charlie is adult-only — all dropped.
        result.Select(r => r.JobName).Should().Equal("Alpha", "Bravo");

        var a = result.Single(r => r.JobId == jobA);
        a.HasPlayerForm.Should().BeTrue();
        a.Year.Should().Be("2025");   // display year still comes from the adult summary read

        result.Should().NotContain(r => r.JobId == jobC);
    }
}
