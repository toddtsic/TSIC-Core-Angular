using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TSIC.API.Services.Metadata;
using TSIC.Contracts.Dtos;

namespace TSIC.Tests.Metadata;

/// <summary>
/// Regression pin for the legacy DataAnnotation shapes the parser previously dropped.
/// Ann reported player forms migrating without their SAT/GPA/ACT ranges: every legacy
/// [Range]/[StringLength]/[RegularExpression] attribute carries named args and/or a
/// trailing ErrorMessage, which the old strict `)]`-anchored regexes failed to match —
/// so 349 validators silently vanished. These assert the exact PP20-shaped attribute
/// strings now transit. Pure logic (source string in → validation object out), no EF.
/// </summary>
public class CSharpToMetadataParserValidationTests
{
    // POCO mirrors the real PP20 attribute shapes (named args + ErrorMessage on nearly everything).
    private const string ProfileSource = @"
public class TestFormViewModel
{
    [Display(Name = ""SAT (Math)"")]
    [Range(minimum: 200, maximum: 800, ErrorMessage = ""SAT (Math) must be between 200 and 800"")]
    public string SatMath { get; set; }

    [Display(Name = ""GPA"")]
    [Required(AllowEmptyStrings = true)]
    [Range(minimum: 0, maximum: 5.0, ErrorMessage = ""GPA must be between 0 and 5.0"")]
    public string Gpa { get; set; }

    [Display(Name = ""Uniform #"")]
    [Required(ErrorMessage = ""UNIFORM # is required"")]
    [RegularExpression(pattern: ""([0-9]+)"", ErrorMessage = ""UNIFORM # must be digits only, no letters"")]
    public string UniformNo { get; set; }

    [Display(Name = ""USA Lacrosse Number"")]
    [StringLength(12, MinimumLength = 7, ErrorMessage = ""USA LACROSSE NUMBER must be between 7 and 12 digits long"")]
    public string SportAssnId { get; set; }

    [Display(Name = ""I agree"")]
    [Range(typeof(bool), ""true"", ""true"")]
    public bool BAgree { get; set; }
}";

    private const string View = @"
<input asp-for=""SatMath"" />
<input asp-for=""Gpa"" />
<input asp-for=""UniformNo"" />
<input asp-for=""SportAssnId"" />
<input asp-for=""BAgree"" type=""checkbox"" />
";

    private readonly ProfileMetadata _md;

    public CSharpToMetadataParserValidationTests()
    {
        var parser = new CSharpToMetadataParser(NullLogger<CSharpToMetadataParser>.Instance);
        _md = parser.ParseProfileAsync(ProfileSource, baseClassSourceCode: "", profileType: "TEST", commitSha: "sha", viewContent: View)
                    .GetAwaiter().GetResult();
    }

    private FieldValidation Validation(string dbColumn) =>
        _md.Fields.Single(f => f.DbColumn == dbColumn).Validation
            ?? throw new Xunit.Sdk.XunitException($"No validation parsed for '{dbColumn}'");

    [Fact]
    public void Range_named_args_with_error_message_captures_min_max_and_message()
    {
        var v = Validation("SatMath");
        v.Min.Should().Be(200);
        v.Max.Should().Be(800);
        v.Message.Should().Be("SAT (Math) must be between 200 and 800");
        // No plain [Required] on this field → not required.
        v.Required.Should().BeFalse();
    }

    [Fact]
    public void Range_with_decimal_bounds_coexists_with_allow_empty_strings()
    {
        var v = Validation("Gpa");
        v.Min.Should().Be(0);
        v.Max.Should().Be(5.0);
        // [Required(AllowEmptyStrings = true)] passes on an empty post → legacy treated it as
        // OPTIONAL. Must NOT migrate as hard-required or blank academics block submission.
        v.Required.Should().BeFalse();
    }

    [Fact]
    public void RegularExpression_named_pattern_with_trailing_error_message_captures_pattern()
    {
        var v = Validation("UniformNo");
        v.Pattern.Should().Be("([0-9]+)");
        // This field DOES carry a plain [Required] → still required.
        v.Required.Should().BeTrue();
    }

    [Fact]
    public void StringLength_with_trailing_error_message_captures_min_and_max_length()
    {
        var v = Validation("SportAssnId");
        v.MaxLength.Should().Be(12);
        v.MinLength.Should().Be(7);
    }

    [Fact]
    public void Range_typeof_bool_maps_to_required_true_not_a_numeric_range()
    {
        var v = Validation("BAgree");
        v.RequiredTrue.Should().BeTrue("the typeof(bool) Range is legacy's must-be-checked device");
        v.Min.Should().BeNull();
        v.Max.Should().BeNull();
    }
}
