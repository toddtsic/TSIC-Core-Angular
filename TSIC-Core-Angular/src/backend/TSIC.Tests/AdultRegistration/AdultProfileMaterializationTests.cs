using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TSIC.API.Services.Metadata;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Adults;
using TSIC.Domain.Entities;

namespace TSIC.Tests.AdultRegistration;

/// <summary>
/// The coach-form build, exercised through <c>ComputeCoachFormSwap</c> — the compute step behind
/// Configure → Job → Adult, and now the only writer of <c>Jobs.AdultProfileMetadataJson</c>.
///
/// <para>These assertions previously ran through the bulk adult migration. That was removed once adult
/// forms became DERIVED from <c>RegformName_Coach</c> (an empty blob means "use the catalog", so
/// materializing ~1,034 AC1 jobs would have written a copy of the catalog). The behavior they cover —
/// per-profile apparel sets and the USLax field — lives on in <c>MaterializeAdultForJob</c>, which
/// <c>ComputeCoachFormSwap</c> still calls, so the coverage moved with it rather than being deleted.</para>
///
/// <para>Pure compute: no DbContext, nothing persisted. The service mutates the passed
/// <see cref="Jobs"/> entity's <c>JsonOptions</c> and returns the new blob.</para>
/// </summary>
public class AdultProfileMaterializationTests
{
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private static ProfileMetadataMigrationService BuildService() => new(
        Mock.Of<IProfileMetadataRepository>(),
        Mock.Of<IGitHubProfileFetcher>(),
        new CSharpToMetadataParser(NullLogger<CSharpToMetadataParser>.Instance),
        NullLogger<ProfileMetadataMigrationService>.Instance);

    private static Jobs Job(string regform) => new()
    {
        JobId = Guid.NewGuid(),
        JobName = regform + " Job",
        Year = "2026",
        RegformNameCoach = regform
    };

    [Fact(DisplayName = "AC2 builds all three roles and seeds ListSizes_* into the job's JsonOptions")]
    public void AC2_WritesRolesAndSeedsApparel()
    {
        var job = Job("StaffSTEPS");
        var svc = BuildService();

        var json = svc.ComputeCoachFormSwap(job, "AC2", AdultUsLaxMode.None);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(json, CaseInsensitive)!;
        set.UnassignedAdult.Fields.Should().Contain(f => f.Name == "jerseySize");
        set.UnassignedAdult.Fields.Should().Contain(f => f.Name == "specialRequests");
        set.Referee.Fields.Should().NotBeEmpty();
        set.Recruiter.Fields.Should().NotBeEmpty();

        job.JsonOptions.Should().NotBeNull();
        job.JsonOptions!.Should().Contain("ListSizes_CoachJersey");

        // Seeded items MUST use the legacy { "Text", "Value" } PascalCase shape so the Configure Job
        // Dropdowns editor (DdlOptionsService, case-sensitive) can read them — not { "value", "label" }.
        using var opts = JsonDocument.Parse(job.JsonOptions!);
        var firstCoachJersey = opts.RootElement.GetProperty("ListSizes_CoachJersey")[0];
        firstCoachJersey.GetProperty("Value").GetString().Should().Be("SM");
        firstCoachJersey.GetProperty("Text").GetString().Should().Be("SM");
    }

    [Fact(DisplayName = "AC3 builds shirt+shoe ONLY and seeds just those two size sets — no shorts/waist")]
    public void AC3_ShirtAndShoeOnly()
    {
        var job = Job("StaffASL");
        var svc = BuildService();

        var json = svc.ComputeCoachFormSwap(job, "AC3", AdultUsLaxMode.None);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(json, CaseInsensitive)!;
        var names = set.UnassignedAdult.Fields.Select(f => f.Name).ToList();
        names.Should().Contain(new[] { "jerseySize", "shoes", "specialRequests" });
        names.Should().NotContain(new[] { "shortsSize", "sweatpants" });   // the over-collection bug this fixes

        // Only the two referenced size sets are seeded — not the full apparel four.
        job.JsonOptions.Should().NotBeNull();
        job.JsonOptions!.Should().Contain("ListSizes_CoachJersey");
        job.JsonOptions!.Should().Contain("ListSizes_CoachShoes");
        job.JsonOptions!.Should().NotContain("ListSizes_CoachShorts");
        job.JsonOptions!.Should().NotContain("ListSizes_CoachWaist");
    }

    [Theory(DisplayName = "USLax rides on the coach block independently of profile — required only when Required")]
    [InlineData("AC1", AdultUsLaxMode.Required, true)]
    [InlineData("AC3", AdultUsLaxMode.Required, true)]    // impossible under the legacy form names
    [InlineData("AC2", AdultUsLaxMode.Optional, false)]   // collected, hard-validated when supplied, never blocking
    public void UsLax_PrependsSportAssnId(string profile, AdultUsLaxMode usLax, bool required)
    {
        var job = Job("Default_Form");
        var svc = BuildService();

        var json = svc.ComputeCoachFormSwap(job, profile, usLax);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(json, CaseInsensitive)!;
        var sportAssn = set.UnassignedAdult.Fields.SingleOrDefault(f => f.Name == "sportAssnId");
        sportAssn.Should().NotBeNull();
        sportAssn!.Validation!.Required.Should().Be(required);
    }

    [Fact(DisplayName = "No USLax on the job ⇒ no sportAssnId field on the coach block")]
    public void NoUsLax_OmitsSportAssnId()
    {
        var job = Job("Default_Form");
        var svc = BuildService();

        var json = svc.ComputeCoachFormSwap(job, "AC1", AdultUsLaxMode.None);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(json, CaseInsensitive)!;
        set.UnassignedAdult.Fields.Should().NotContain(f => f.Name == "sportAssnId");
    }

    [Fact(DisplayName = "An existing blob keeps its Referee/Recruiter blocks — only the coach role is rebuilt")]
    public void ExistingBlob_RebuildsCoachOnly()
    {
        var job = Job("StaffSTEPS");
        job.AdultProfileMetadataJson =
            """{"UnassignedAdult":{"fields":[]},"Referee":{"fields":[{"name":"customRefField","order":1}]},"Recruiter":{"fields":[]}}""";
        var svc = BuildService();

        var json = svc.ComputeCoachFormSwap(job, "AC2", AdultUsLaxMode.None);

        var set = JsonSerializer.Deserialize<AdultRoleMetadataSet>(json, CaseInsensitive)!;
        set.UnassignedAdult.Fields.Should().Contain(f => f.Name == "jerseySize");   // rebuilt
        set.Referee.Fields.Should().ContainSingle(f => f.Name == "customRefField"); // left verbatim
    }
}
