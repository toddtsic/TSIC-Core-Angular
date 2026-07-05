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

    private static CopyJobFormsRequest Req(Guid sourceJobId, bool player, bool coach) =>
        new() { SourceJobId = sourceJobId, IncludePlayer = player, IncludeCoach = coach };

    [Fact]
    public async Task Copy_Both_WritesBothFormsToCurrentJob()
    {
        var regId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        var svc = BuildService(regId, targetJobId, sourceJobId, out var repo);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(sourceJobId, player: true, coach: true));

        result.Success.Should().BeTrue();
        result.PlayerCopied.Should().BeTrue();
        result.CoachCopied.Should().BeTrue();
        result.SourceJobName.Should().Be("Source Job");
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(targetJobId, PlayerJson), Times.Once);
        repo.Verify(r => r.UpdateJobAdultMetadataAsync(targetJobId, AdultJson), Times.Once);
    }

    [Fact]
    public async Task Copy_PlayerOnly_DoesNotTouchAdult()
    {
        var regId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        var svc = BuildService(regId, targetJobId, sourceJobId, out var repo);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(sourceJobId, player: true, coach: false));

        result.Success.Should().BeTrue();
        result.PlayerCopied.Should().BeTrue();
        result.CoachCopied.Should().BeFalse();
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(targetJobId, PlayerJson), Times.Once);
        repo.Verify(r => r.UpdateJobAdultMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_CoachOnly_DoesNotTouchPlayer()
    {
        var regId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        var svc = BuildService(regId, targetJobId, sourceJobId, out var repo);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(sourceJobId, player: false, coach: true));

        result.Success.Should().BeTrue();
        result.CoachCopied.Should().BeTrue();
        result.PlayerCopied.Should().BeFalse();
        repo.Verify(r => r.UpdateJobAdultMetadataAsync(targetJobId, AdultJson), Times.Once);
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_RequestedFormMissingOnSource_FailsWithoutWriting()
    {
        var regId = Guid.NewGuid();
        var targetJobId = Guid.NewGuid();
        var sourceJobId = Guid.NewGuid();
        // Source has a player form but NO adult form; the request asks for both.
        var svc = BuildService(regId, targetJobId, sourceJobId, out var repo, sourceAdultJson: null);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(sourceJobId, player: true, coach: true));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("coach");
        // No partial copy — neither write fired.
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
        repo.Verify(r => r.UpdateJobAdultMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_SourceIsCurrentJob_Fails()
    {
        var regId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var svc = BuildService(regId, jobId, jobId, out var repo); // source == target

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(jobId, player: true, coach: false));

        result.Success.Should().BeFalse();
        repo.Verify(r => r.UpdateJobPlayerMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Copy_NothingSelected_FailsBeforeAnyRepoCall()
    {
        var regId = Guid.NewGuid();
        var svc = BuildService(regId, Guid.NewGuid(), Guid.NewGuid(), out var repo);

        var result = await svc.CopyFormsToCurrentJobAsync(regId, Req(Guid.NewGuid(), player: false, coach: false));

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
    public async Task CopySources_MergesFormFlags_ExcludesCurrentAndFormlessJobs_OrdersByName()
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

        // Alpha (both forms), Bravo (player only), Charlie (coach only) — ordered by name; Delta & self dropped.
        result.Select(r => r.JobName).Should().Equal("Alpha", "Bravo", "Charlie");

        var a = result.Single(r => r.JobId == jobA);
        a.HasPlayerForm.Should().BeTrue();
        a.HasCoachForm.Should().BeTrue();
        a.Year.Should().Be("2025");

        var b = result.Single(r => r.JobId == jobB);
        b.HasPlayerForm.Should().BeTrue();
        b.HasCoachForm.Should().BeFalse();

        var c = result.Single(r => r.JobId == jobC);
        c.HasPlayerForm.Should().BeFalse();
        c.HasCoachForm.Should().BeTrue();
    }
}
