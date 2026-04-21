using FluentAssertions;
using TSIC.API.Services.Admin;

namespace TSIC.Tests.JobClone;

/// <summary>
/// Unit tests for pure transform helpers used by JobCloneService.
/// Covers year-delta computation, date shifts (incl. Feb-29 edge case),
/// agegroup name year-bump, and league-name pattern inference.
/// </summary>
public class JobCloneTransformsTests
{
    // ── ComputeYearDelta ─────────────────────────────────────────

    [Fact]
    public void ComputeYearDelta_StandardForward_Returns_Delta()
    {
        JobCloneTransforms.ComputeYearDelta("2025", "2026").Should().Be(1);
        JobCloneTransforms.ComputeYearDelta("2024", "2027").Should().Be(3);
    }

    [Fact]
    public void ComputeYearDelta_Same_Returns_Zero()
    {
        JobCloneTransforms.ComputeYearDelta("2025", "2025").Should().Be(0);
    }

    [Fact]
    public void ComputeYearDelta_Backward_Returns_Negative()
    {
        JobCloneTransforms.ComputeYearDelta("2026", "2024").Should().Be(-2);
    }

    [Fact]
    public void ComputeYearDelta_NullOrUnparseable_Returns_Zero()
    {
        JobCloneTransforms.ComputeYearDelta(null, "2026").Should().Be(0);
        JobCloneTransforms.ComputeYearDelta("2025", null).Should().Be(0);
        JobCloneTransforms.ComputeYearDelta("not-a-year", "2026").Should().Be(0);
        JobCloneTransforms.ComputeYearDelta("", "").Should().Be(0);
    }

    // ── ShiftByYears (DateTime) ─────────────────────────────────

    [Fact]
    public void ShiftByYears_DateTime_Zero_Returns_Same()
    {
        var d = new DateTime(2025, 5, 15, 10, 30, 0);
        JobCloneTransforms.ShiftByYears(d, 0).Should().Be(d);
    }

    [Fact]
    public void ShiftByYears_DateTime_ForwardOneYear()
    {
        var d = new DateTime(2025, 5, 15);
        JobCloneTransforms.ShiftByYears(d, 1).Should().Be(new DateTime(2026, 5, 15));
    }

    [Fact]
    public void ShiftByYears_DateTime_Feb29_ClampsToFeb28InNonLeapYear()
    {
        var d = new DateTime(2024, 2, 29); // 2024 leap
        JobCloneTransforms.ShiftByYears(d, 1).Should().Be(new DateTime(2025, 2, 28));
    }

    [Fact]
    public void ShiftByYears_DateTime_Feb29_ToLeapYear_Preserved()
    {
        var d = new DateTime(2024, 2, 29);
        JobCloneTransforms.ShiftByYears(d, 4).Should().Be(new DateTime(2028, 2, 29));
    }

    [Fact]
    public void ShiftByYears_NullableDateTime_Null_ReturnsNull()
    {
        DateTime? d = null;
        JobCloneTransforms.ShiftByYears(d, 1).Should().BeNull();
    }

    [Fact]
    public void ShiftByYears_NullableDateTime_Valued_Shifts()
    {
        DateTime? d = new DateTime(2025, 5, 15);
        JobCloneTransforms.ShiftByYears(d, 1).Should().Be(new DateTime(2026, 5, 15));
    }

    // ── ShiftByYears (DateOnly) ─────────────────────────────────

    [Fact]
    public void ShiftByYears_DateOnly_ForwardOneYear()
    {
        var d = new DateOnly(2025, 8, 1);
        JobCloneTransforms.ShiftByYears(d, 1).Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public void ShiftByYears_DateOnly_Feb29_ClampsToFeb28()
    {
        var d = new DateOnly(2024, 2, 29);
        JobCloneTransforms.ShiftByYears(d, 1).Should().Be(new DateOnly(2025, 2, 28));
    }

    [Fact]
    public void ShiftByYears_NullableDateOnly_Null_ReturnsNull()
    {
        DateOnly? d = null;
        JobCloneTransforms.ShiftByYears(d, 1).Should().BeNull();
    }

    // ── IncrementYearsInName ────────────────────────────────────

    [Fact]
    public void IncrementYearsInName_BumpsYearToken()
    {
        JobCloneTransforms.IncrementYearsInName("2025 Boys").Should().Be("2026 Boys");
        JobCloneTransforms.IncrementYearsInName("Class of 2027").Should().Be("Class of 2028");
    }

    [Fact]
    public void IncrementYearsInName_NoYearToken_IsNoOp()
    {
        JobCloneTransforms.IncrementYearsInName("Boys Advanced").Should().Be("Boys Advanced");
        JobCloneTransforms.IncrementYearsInName("U12").Should().Be("U12");
    }

    [Fact]
    public void IncrementYearsInName_MultipleYears_BumpsAll()
    {
        JobCloneTransforms.IncrementYearsInName("2025-2026 Season").Should().Be("2026-2027 Season");
    }

    [Fact]
    public void IncrementYearsInName_TypicalAgegroupPattern_BumpsBothEnds()
    {
        JobCloneTransforms.IncrementYearsInName("Girls Elite Players 2025-2026")
            .Should().Be("Girls Elite Players 2026-2027");
    }

    [Fact]
    public void IncrementYearsInName_YearRangeWithSpaces_BumpsBothEnds()
    {
        JobCloneTransforms.IncrementYearsInName("Boys 2025 - 2026")
            .Should().Be("Boys 2026 - 2027");
    }

    [Fact]
    public void IncrementYearsInName_YearRangeWithSlash_BumpsBothEnds()
    {
        JobCloneTransforms.IncrementYearsInName("Boys 2025/2026")
            .Should().Be("Boys 2026/2027");
    }

    [Fact]
    public void IncrementYearsInName_NonYearDigits_LeftAlone()
    {
        // 4-digit numbers outside 2020-2039 are not mangled
        JobCloneTransforms.IncrementYearsInName("Game 1500").Should().Be("Game 1500");
        JobCloneTransforms.IncrementYearsInName("Room 1234").Should().Be("Room 1234");
    }

    // ── InferLeagueName ─────────────────────────────────────────

    [Fact]
    public void InferLeagueName_HyphenSeparator_SubstitutesAll()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "STEPS-Spring-2025",
            newLeagueName: "STEPS",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "unused");
        result.Should().Be("STEPS-Fall-2026");
    }

    [Fact]
    public void InferLeagueName_SpaceSeparator_SubstitutesAll()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "STEPS Spring 2025",
            newLeagueName: "STEPS",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "unused");
        result.Should().Be("STEPS Fall 2026");
    }

    [Fact]
    public void InferLeagueName_UnderscoreSeparator_SubstitutesAll()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "Steps_Spring_2025",
            newLeagueName: "Steps",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "unused");
        result.Should().Be("Steps_Fall_2026");
    }

    [Fact]
    public void InferLeagueName_DifferentLeagueName_UsesNewValue()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "STEPS-Spring-2025",
            newLeagueName: "CASA",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "unused");
        result.Should().Be("CASA-Fall-2026");
    }

    [Fact]
    public void InferLeagueName_YearFirstOrder_PreservesOrder()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "2025_Spring_STEPS",
            newLeagueName: "STEPS",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "unused");
        result.Should().Be("2026_Fall_STEPS");
    }

    [Fact]
    public void InferLeagueName_SingleToken_FallsBack()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "MyLeague",
            newLeagueName: "ignored",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "FALLBACK");
        result.Should().Be("FALLBACK");
    }

    [Fact]
    public void InferLeagueName_NoYearToken_FallsBack()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "STEPS-Youth-Program",
            newLeagueName: "ignored",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "FALLBACK");
        result.Should().Be("FALLBACK");
    }

    [Fact]
    public void InferLeagueName_NullSource_FallsBack()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: null,
            newLeagueName: "ignored",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "FALLBACK");
        result.Should().Be("FALLBACK");
    }

    [Fact]
    public void InferLeagueName_FourTokens_FallsBack()
    {
        // Beyond the 3-token pattern we handle
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "STEPS-Youth-Spring-2025",
            newLeagueName: "ignored",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "FALLBACK");
        result.Should().Be("FALLBACK");
    }

    [Fact]
    public void InferLeagueName_CaseInsensitiveSeason_Detected()
    {
        var result = JobCloneTransforms.InferLeagueName(
            sourceLeagueName: "STEPS-SPRING-2025",
            newLeagueName: "STEPS",
            seasonTarget: "Fall",
            yearTarget: "2026",
            fallback: "unused");
        result.Should().Be("STEPS-Fall-2026");
    }
}
