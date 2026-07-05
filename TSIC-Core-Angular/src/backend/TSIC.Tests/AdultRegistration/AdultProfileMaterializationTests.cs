using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TSIC.API.Services.Metadata;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.Tests.AdultRegistration;

/// <summary>
/// Materialization + idempotency for the adult migrate path (<c>MigrateAdultProfileAsync</c>). The repository
/// is mocked — no DbContext: the service mutates the tracked <see cref="Jobs"/> entities in place, so we assert
/// directly on them. Locks four behaviors: writes all three roles, seeds <c>ListSizes_*</c> for AC2, leaves
/// other-profile jobs untouched, and is idempotent (skip-already-migrated unless force; dry-run writes nothing).
/// </summary>
public class AdultProfileMaterializationTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private static ProfileMetadataMigrationService BuildService(List<Jobs> jobs, out Mock<IProfileMetadataRepository> repo)
    {
        repo = new Mock<IProfileMetadataRepository>();
        repo.Setup(r => r.GetJobsForAdultMigrationAsync()).ReturnsAsync(jobs);
        repo.Setup(r => r.UpdateMultipleJobsAdultMetadataAsync(It.IsAny<List<Jobs>>())).Returns(Task.CompletedTask);
        return new ProfileMetadataMigrationService(
            repo.Object,
            Mock.Of<IGitHubProfileFetcher>(),
            new CSharpToMetadataParser(NullLogger<CSharpToMetadataParser>.Instance),
            NullLogger<ProfileMetadataMigrationService>.Instance);
    }

    private static Jobs Job(string regform, string? existing = null) => new()
    {
        JobId = Guid.NewGuid(),
        JobName = regform + " Job",
        Year = "2026",
        RegformNameCoach = regform,
        AdultProfileMetadataJson = existing
    };

    [Fact(DisplayName = "Migrating AC2 writes all three roles + seeds ListSizes_* into JsonOptions; other-profile jobs untouched")]
    public async Task Migrate_AC2_WritesRolesAndSeedsApparel()
    {
        var ac2 = Job("StaffSTEPS");    // → AC2, no USLax
        var ac1 = Job("Default_Form");  // → AC1
        var jobs = new List<Jobs> { ac2, ac1 };
        var svc = BuildService(jobs, out var repo);

        var result = await svc.MigrateAdultProfileAsync("AC2", dryRun: false, force: false);

        result.Success.Should().BeTrue();
        result.JobsAffected.Should().Be(1);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(ac2.AdultProfileMetadataJson!, CaseInsensitive)!;
        set.UnassignedAdult.Fields.Should().Contain(f => f.Name == "jerseySize");
        set.UnassignedAdult.Fields.Should().Contain(f => f.Name == "specialRequests");
        set.Referee.Fields.Should().ContainSingle();
        set.Recruiter.Fields.Should().ContainSingle();

        ac2.JsonOptions.Should().NotBeNull();
        ac2.JsonOptions!.Should().Contain("ListSizes_CoachJersey");

        ac1.AdultProfileMetadataJson.Should().BeNull(); // AC1 job is not part of an AC2 migration
        repo.Verify(r => r.UpdateMultipleJobsAdultMetadataAsync(It.IsAny<List<Jobs>>()), Times.Once);
    }

    [Fact(DisplayName = "Migrating AC3 (StaffASL) writes shirt+shoe ONLY and seeds just those two size sets — no shorts/waist")]
    public async Task Migrate_AC3_ShirtAndShoeOnly()
    {
        var ac3 = Job("StaffASL");      // → AC3 (legacy StaffASL: jersey + shoe only)
        var ac2 = Job("StaffSTEPS");    // → AC2, must stay out of an AC3 migration
        var svc = BuildService(new List<Jobs> { ac3, ac2 }, out _);

        var result = await svc.MigrateAdultProfileAsync("AC3", dryRun: false, force: false);

        result.Success.Should().BeTrue();
        result.JobsAffected.Should().Be(1);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(ac3.AdultProfileMetadataJson!, CaseInsensitive)!;
        var names = set.UnassignedAdult.Fields.Select(f => f.Name).ToList();
        names.Should().Contain(new[] { "jerseySize", "shoes", "specialRequests" });
        names.Should().NotContain(new[] { "shortsSize", "sweatpants" });   // the over-collection bug this fixes

        // Only the two referenced size sets are seeded — not the full apparel four.
        ac3.JsonOptions.Should().NotBeNull();
        ac3.JsonOptions!.Should().Contain("ListSizes_CoachJersey");
        ac3.JsonOptions!.Should().Contain("ListSizes_CoachShoes");
        ac3.JsonOptions!.Should().NotContain("ListSizes_CoachShorts");
        ac3.JsonOptions!.Should().NotContain("ListSizes_CoachWaist");

        ac2.AdultProfileMetadataJson.Should().BeNull(); // AC2 job untouched by an AC3 migration
    }

    [Fact(DisplayName = "A USLax job gets a required sportAssnId in the materialized coach block")]
    public async Task Migrate_UsLaxJob_PrependsSportAssnId()
    {
        var job = Job("StaffLaxValidate"); // → AC1 + USLax
        var svc = BuildService(new List<Jobs> { job }, out _);

        var result = await svc.MigrateAdultProfileAsync("AC1", dryRun: false, force: false);

        result.JobsAffected.Should().Be(1);
        result.UsLaxJobsAffected.Should().Be(1);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(job.AdultProfileMetadataJson!, CaseInsensitive)!;
        var sportAssn = set.UnassignedAdult.Fields.SingleOrDefault(f => f.Name == "sportAssnId");
        sportAssn.Should().NotBeNull();
        sportAssn!.Validation!.Required.Should().BeTrue();
    }

    [Fact(DisplayName = "Migrate is idempotent: an already-materialized job is skipped unless force")]
    public async Task Migrate_SkipsAlreadyMaterialized_UnlessForce()
    {
        const string existing = "{\"UnassignedAdult\":{\"fields\":[]}}";
        var job = Job("StaffSTEPS", existing);
        var svc = BuildService(new List<Jobs> { job }, out var repo);

        var skip = await svc.MigrateAdultProfileAsync("AC2", dryRun: false, force: false);
        skip.JobsAffected.Should().Be(0);
        job.AdultProfileMetadataJson.Should().Be(existing); // untouched
        repo.Verify(r => r.UpdateMultipleJobsAdultMetadataAsync(It.IsAny<List<Jobs>>()), Times.Never);

        var forced = await svc.MigrateAdultProfileAsync("AC2", dryRun: false, force: true);
        forced.JobsAffected.Should().Be(1);
        job.AdultProfileMetadataJson.Should().NotBe(existing);        // rewritten
        job.AdultProfileMetadataJson.Should().Contain("jerseySize");
    }

    [Fact(DisplayName = "Dry-run reports affected jobs but writes nothing")]
    public async Task Migrate_DryRun_WritesNothing()
    {
        var job = Job("StaffSTEPS");
        var svc = BuildService(new List<Jobs> { job }, out var repo);

        var result = await svc.MigrateAdultProfileAsync("AC2", dryRun: true, force: true);

        result.JobsAffected.Should().Be(1);              // reported
        job.AdultProfileMetadataJson.Should().BeNull();  // but not written
        repo.Verify(r => r.UpdateMultipleJobsAdultMetadataAsync(It.IsAny<List<Jobs>>()), Times.Never);
    }
}
