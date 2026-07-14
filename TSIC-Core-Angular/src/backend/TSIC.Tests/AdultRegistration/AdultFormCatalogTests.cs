using FluentAssertions;
using TSIC.API.Services.Adults;

namespace TSIC.Tests.AdultRegistration;

/// <summary>
/// Locks the adult "legacy source" — the parallel to the player C#→metadata parser.
///
/// Two guarantees:
///  1. <see cref="AdultFormCatalog.MapLegacy"/> collapses every legacy <c>RegformName_Coach</c> value into
///     the right (canonical AC1/AC2, requiresUsLax) pair — USLax is a per-job capability, never a profile.
///  2. <see cref="AdultFormCatalog.BuildRoleSet"/> composes the three role blocks correctly: the coach block
///     carries the profile's substantive fields with <c>sportAssnId</c> prepended IFF USLax; Referee/Recruiter
///     are uniform; every field has a sequential 1-based Order; AC2 apparel SELECTs carry inline options whose
///     DataSource matches the seeded <c>ListSizes_*</c> keys.
/// </summary>
public class AdultFormCatalogTests
{
    // ── (a) MapLegacy ────────────────────────────────────────────────────────

    [Theory(DisplayName = "MapLegacy collapses each known RegformName_Coach to the right (profile, USLax)")]
    [InlineData("StaffSTEPS", "AC2", false)]          // full apparel
    [InlineData("StaffLaxValidatePlus", "AC2", true)] // full apparel + USLax
    [InlineData("StaffLaxValidate", "AC1", true)]     // base + USLax
    [InlineData("StaffASL", "AC3", false)]            // shirt + shoe ONLY (distinct subset)
    [InlineData("CP-STEPS", "AC1", false)]            // "STEPS" in name but NOT StaffSTEPS → base
    [InlineData("RegAdult_WANTTOCOACH_RegForm", "AC1", false)]
    [InlineData("Defalt_Form", "AC1", false)]         // legacy misspelling
    [InlineData("Default_Form", "AC1", false)]
    public void MapLegacy_MapsKnownValues(string legacy, string expectedProfile, bool expectedUsLax)
    {
        var (profile, requiresUsLax) = AdultFormCatalog.MapLegacy(legacy);
        profile.Should().Be(expectedProfile);
        requiresUsLax.Should().Be(expectedUsLax);
    }

    [Theory(DisplayName = "MapLegacy falls to AC1/no-USLax for null, empty, whitespace, and unknown values")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomethingNobodyRecognizes")]
    public void MapLegacy_FallsToBase(string? legacy)
    {
        var (profile, requiresUsLax) = AdultFormCatalog.MapLegacy(legacy);
        profile.Should().Be("AC1");
        requiresUsLax.Should().BeFalse();
    }

    [Theory(DisplayName = "MapLegacy is case-insensitive and trims surrounding whitespace")]
    [InlineData("staffsteps", "AC2", false)]
    [InlineData("  StaffLaxValidate  ", "AC1", true)]
    [InlineData("STAFFLAXVALIDATEPLUS", "AC2", true)]
    public void MapLegacy_CaseInsensitiveAndTrimmed(string legacy, string expectedProfile, bool expectedUsLax)
    {
        var (profile, requiresUsLax) = AdultFormCatalog.MapLegacy(legacy);
        profile.Should().Be(expectedProfile);
        requiresUsLax.Should().Be(expectedUsLax);
    }

    // ── (b) BuildRoleSet — coach block ───────────────────────────────────────

    [Fact(DisplayName = "AC1 without USLax: coach is a single optional Special Requests, no sportAssnId")]
    public void BuildRoleSet_AC1_NoUsLax()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC1", requiresUsLax: false).UnassignedAdult.Fields;

        coach.Should().ContainSingle();
        coach[0].Name.Should().Be("specialRequests");
        coach[0].InputType.Should().Be("TEXTAREA");
        (coach[0].Validation?.Required ?? false).Should().BeFalse();
        coach.Should().NotContain(f => f.Name == "sportAssnId");
    }

    [Fact(DisplayName = "AC1 with USLax: required sportAssnId (7-12) prepended, then Special Requests")]
    public void BuildRoleSet_AC1_UsLax()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC1", requiresUsLax: true).UnassignedAdult.Fields;

        coach.Select(f => f.Name).Should().Equal("sportAssnId", "specialRequests");
        coach[0].Order.Should().Be(1);
        coach[0].InputType.Should().Be("TEXT");
        coach[0].Validation!.Required.Should().BeTrue();
        coach[0].Validation!.MinLength.Should().Be(7);
        coach[0].Validation!.MaxLength.Should().Be(12);
    }

    [Fact(DisplayName = "AC2 without USLax: four apparel SELECTs (inline options + ListSizes_* DataSource) then Special Requests")]
    public void BuildRoleSet_AC2_NoUsLax()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC2", requiresUsLax: false).UnassignedAdult.Fields;

        coach.Select(f => f.Name).Should().Equal("jerseySize", "shortsSize", "sweatpants", "shoes", "specialRequests");

        var apparel = coach.Where(f => f.Name != "specialRequests").ToList();
        apparel.Should().OnlyContain(f => f.InputType == "SELECT");
        apparel.Should().OnlyContain(f => f.Options != null && f.Options.Count > 0);
        apparel.Should().OnlyContain(f => f.Validation != null && f.Validation.Required);
        apparel.Should().OnlyContain(f => f.DataSource != null && f.DataSource!.StartsWith("ListSizes_"));
        coach.Should().NotContain(f => f.Name == "sportAssnId");
    }

    [Fact(DisplayName = "AC2 with USLax: sportAssnId first, then the four apparel sizes, then Special Requests")]
    public void BuildRoleSet_AC2_UsLax()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC2", requiresUsLax: true).UnassignedAdult.Fields;
        coach.Select(f => f.Name).Should().Equal("sportAssnId", "jerseySize", "shortsSize", "sweatpants", "shoes", "specialRequests");
    }

    [Fact(DisplayName = "AC3 without USLax: shirt + shoe SELECTs ONLY (no shorts/waist) then Special Requests")]
    public void BuildRoleSet_AC3_NoUsLax()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC3", requiresUsLax: false).UnassignedAdult.Fields;

        // The whole point of AC3: jersey + shoe only — NOT shorts/waist (that would be AC2 over-collecting).
        coach.Select(f => f.Name).Should().Equal("jerseySize", "shoes", "specialRequests");

        var apparel = coach.Where(f => f.Name != "specialRequests").ToList();
        apparel.Should().OnlyContain(f => f.InputType == "SELECT");
        apparel.Should().OnlyContain(f => f.Options != null && f.Options.Count > 0);
        apparel.Should().OnlyContain(f => f.Validation != null && f.Validation.Required);
        apparel.Select(f => f.DataSource).Should().Equal("ListSizes_CoachJersey", "ListSizes_CoachShoes");
        coach.Should().NotContain(f => f.Name == "shortsSize" || f.Name == "sweatpants");
        coach.Should().NotContain(f => f.Name == "sportAssnId");
    }

    [Fact(DisplayName = "AC3 with USLax: sportAssnId first, then shirt + shoe, then Special Requests")]
    public void BuildRoleSet_AC3_UsLax()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC3", requiresUsLax: true).UnassignedAdult.Fields;
        coach.Select(f => f.Name).Should().Equal("sportAssnId", "jerseySize", "shoes", "specialRequests");
    }

    [Theory(DisplayName = "sportAssnId appears in the coach block IFF USLax is required")]
    [InlineData("AC1", false, false)]
    [InlineData("AC1", true, true)]
    [InlineData("AC2", false, false)]
    [InlineData("AC2", true, true)]
    [InlineData("AC3", false, false)]
    [InlineData("AC3", true, true)]
    public void BuildRoleSet_SportAssnId_PresentIffUsLax(string profile, bool requiresUsLax, bool expectPresent)
    {
        var coach = AdultFormCatalog.BuildRoleSet(profile, requiresUsLax).UnassignedAdult.Fields;
        coach.Any(f => string.Equals(f.Name, "sportAssnId", StringComparison.OrdinalIgnoreCase))
            .Should().Be(expectPresent);
    }

    [Theory(DisplayName = "Every generated coach field carries a sequential 1-based Order")]
    [InlineData("AC1", false)]
    [InlineData("AC1", true)]
    [InlineData("AC2", false)]
    [InlineData("AC2", true)]
    [InlineData("AC3", false)]
    [InlineData("AC3", true)]
    public void BuildRoleSet_OrdersSequential(string profile, bool requiresUsLax)
    {
        var coach = AdultFormCatalog.BuildRoleSet(profile, requiresUsLax).UnassignedAdult.Fields;
        coach.Select(f => f.Order).Should().Equal(Enumerable.Range(1, coach.Count));
    }

    // ── (b) BuildRoleSet — Referee / Recruiter (uniform) ─────────────────────

    [Theory(DisplayName = "Referee and Recruiter are uniform single-required-field blocks, independent of profile/USLax")]
    [InlineData("AC1", false)]
    [InlineData("AC1", true)]
    [InlineData("AC2", false)]
    [InlineData("AC2", true)]
    [InlineData("AC3", false)]
    [InlineData("AC3", true)]
    public void BuildRoleSet_RefereeRecruiter_Uniform(string profile, bool requiresUsLax)
    {
        var set = AdultFormCatalog.BuildRoleSet(profile, requiresUsLax);

        set.Referee.Fields.Should().ContainSingle();
        set.Referee.Fields[0].Name.Should().Be("specialRequests");
        set.Referee.Fields[0].InputType.Should().Be("TEXTAREA");
        set.Referee.Fields[0].Validation!.Required.Should().BeTrue();

        set.Recruiter.Fields.Should().ContainSingle();
        set.Recruiter.Fields[0].Name.Should().Be("specialRequests");
        set.Recruiter.Fields[0].InputType.Should().Be("TEXT");
        set.Recruiter.Fields[0].DisplayName.Should().Be("College / University");
        set.Recruiter.Fields[0].Validation!.Required.Should().BeTrue();
    }

    // ── Nomenclature helpers ─────────────────────────────────────────────────

    [Theory(DisplayName = "Canonical normalizes casing and passes unknown values through unchanged")]
    [InlineData("ac1", "AC1")]
    [InlineData("Ac2", "AC2")]
    [InlineData("ac3", "AC3")]
    [InlineData("AC1", "AC1")]
    [InlineData("weird", "weird")]
    public void Canonical_Normalizes(string input, string expected)
        => AdultFormCatalog.Canonical(input).Should().Be(expected);

    [Theory(DisplayName = "DisplayName gives the three canonical coach labels")]
    [InlineData("AC1", "Adult Coach (Standard)")]
    [InlineData("ac2", "Adult Coach (Apparel)")]
    [InlineData("AC3", "Adult Coach (Shirt + Shoe)")]
    public void DisplayName_Labels(string profile, string expected)
        => AdultFormCatalog.DisplayName(profile).Should().Be(expected);

    [Theory(DisplayName = "IsKnownProfile recognizes AC1/AC2/AC3 in any case and nothing else")]
    [InlineData("AC1", true)]
    [InlineData("AC2", true)]
    [InlineData("AC3", true)]
    [InlineData("ac1", true)]
    [InlineData("ac3", true)]
    [InlineData("AC9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnownProfile_Recognizes(string? profile, bool expected)
        => AdultFormCatalog.IsKnownProfile(profile).Should().Be(expected);

    // ── Apparel option sets + USLax capability ───────────────────────────────

    [Fact(DisplayName = "ApparelOptionSets exposes the four adult-namespaced ListSizes_Coach* sets with the legacy size lists")]
    public void ApparelOptionSets_FourSets()
    {
        var sets = AdultFormCatalog.ApparelOptionSets;
        sets.Keys.Should().BeEquivalentTo("ListSizes_CoachJersey", "ListSizes_CoachShorts", "ListSizes_CoachWaist", "ListSizes_CoachShoes");
        sets["ListSizes_CoachJersey"].Select(o => o.Value).Should().Equal("SM", "MD", "LG", "XL", "XXL", "XXXL");
        sets["ListSizes_CoachWaist"].Should().HaveCount(10);  // 28..46
        sets["ListSizes_CoachShoes"].Should().HaveCount(23);  // 5..16 half-steps
        sets["ListSizes_CoachShoes"].Should().OnlyContain(o => o.Value == o.Label);
    }

    [Fact(DisplayName = "The AC2 apparel DataSources are a subset of the seeded ApparelOptionSets keys (exact-match enrichment)")]
    public void Ac2_DataSources_MatchOptionSetKeys()
    {
        var coach = AdultFormCatalog.BuildRoleSet("AC2", requiresUsLax: false).UnassignedAdult.Fields;
        var dataSources = coach.Where(f => f.DataSource != null).Select(f => f.DataSource!);
        dataSources.Should().BeSubsetOf(AdultFormCatalog.ApparelOptionSets.Keys);
    }

    [Fact(DisplayName = "UsLaxField is the required 7-12 USA Lacrosse number on the SportAssnId column")]
    public void UsLaxField_Shape()
    {
        var f = AdultFormCatalog.UsLaxField(3);
        f.Name.Should().Be("sportAssnId");
        f.DbColumn.Should().Be("SportAssnId");
        f.InputType.Should().Be("TEXT");
        f.Order.Should().Be(3);
        f.Validation!.Required.Should().BeTrue();
        f.Validation!.MinLength.Should().Be(7);
        f.Validation!.MaxLength.Should().Be(12);
    }

    [Fact(DisplayName = "UsLaxCapabilityFieldNames covers sportAssnId + its exp date, case-insensitively")]
    public void UsLaxCapabilityFieldNames_Contents()
    {
        var names = AdultFormCatalog.UsLaxCapabilityFieldNames;
        names.Should().Contain("sportAssnId");
        names.Should().Contain("sportAssnIdexpDate");
        names.Contains("SPORTASSNID").Should().BeTrue(); // case-insensitive set
    }
}
