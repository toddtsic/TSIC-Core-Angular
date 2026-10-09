using FluentAssertions;
using TSIC.Domain.UsLax;

namespace TSIC.Tests.UsLax;

/// <summary>
/// AR-145: a number on file that can't be a USA Lacrosse number is the family's to fix — NotFound —
/// never VendorUnavailable. The vendor client refuses to forward it, so it reaches the policy as
/// status 0; judged as an outage, the reconcile grid withheld the email and the submit gate told the
/// family to "try again". These pin both the verdict and the checklist the grid reads.
/// </summary>
public class UsLaxEligibilityPolicyMalformedNumberTests
{
    private static readonly DateTime Cutoff = new(2027, 6, 30);

    private static UsLaxEligibilityInput Input(string? number, int statusCode = 0, DateTime? validThrough = null, bool teamBypass = false) => new()
    {
        MembershipNumber = number,
        ValidThrough = validThrough ?? Cutoff,
        TeamValidationDisabled = teamBypass,
        VendorStatusCode = statusCode,
        RegistrantLastName = "Rockhold",
        RegistrantDob = new DateTime(2012, 3, 4)
    };

    [Theory]
    [InlineData("123")]
    [InlineData("NA")]
    [InlineData("N/A")]
    [InlineData("12345")]
    [InlineData("1234567890123")]
    public void Malformed_Number_Is_NotFound_Not_VendorUnavailable(string number)
    {
        var verdict = UsLaxEligibilityPolicy.Evaluate(Input(number));

        verdict.Valid.Should().BeFalse();
        verdict.Reason.Should().Be(UsLaxEligibilityReason.NotFound);
    }

    [Fact]
    public void Malformed_Number_Fails_Even_With_No_Cutoff_Configured()
    {
        // A missing cutoff is the director's to fix and withholds the email; a number that isn't one
        // can never pass whatever the cutoff, so it must not hide behind that.
        var verdict = UsLaxEligibilityPolicy.Evaluate(Input("123", validThrough: DateTime.MinValue));

        verdict.Reason.Should().Be(UsLaxEligibilityReason.NotFound);
    }

    [Fact]
    public void Malformed_Number_Checklist_Is_A_Single_Failed_NotFound_Row()
    {
        var rows = UsLaxEligibilityPolicy.Describe(Input("123"));

        rows.Should().ContainSingle();
        rows[0].Key.Should().Be(nameof(UsLaxEligibilityReason.NotFound));
        rows[0].Passed.Should().BeFalse();
        rows[0].Detail.Should().Contain("\"123\"");
    }

    [Fact]
    public void Team_Bypass_Still_Wins_Over_A_Malformed_Number()
    {
        var verdict = UsLaxEligibilityPolicy.Evaluate(Input("NA", teamBypass: true));

        verdict.Valid.Should().BeTrue();
        verdict.Reason.Should().Be(UsLaxEligibilityReason.TeamBypass);
    }

    [Fact]
    public void WellFormed_Number_With_Status_Zero_Is_Still_VendorUnavailable()
    {
        // The real outage stays our failure — this fix must not turn it into a family problem.
        var verdict = UsLaxEligibilityPolicy.Evaluate(Input("010030451570", statusCode: 0));

        verdict.Reason.Should().Be(UsLaxEligibilityReason.VendorUnavailable);
    }

    [Fact]
    public void Leading_Hash_Is_WellFormed()
    {
        var verdict = UsLaxEligibilityPolicy.Evaluate(Input("#01003045157", statusCode: 0));

        verdict.Reason.Should().Be(UsLaxEligibilityReason.VendorUnavailable);
    }

    [Theory]
    [InlineData("#01003045157", "01003045157")]
    [InlineData(" 123456 ", "123456")]
    [InlineData("# 123456", "123456")]
    [InlineData("123", null)]
    [InlineData("NA", null)]
    [InlineData("##123456", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeMembershipNumber(string? raw, string? expected)
    {
        UsLaxEligibilityPolicy.NormalizeMembershipNumber(raw).Should().Be(expected);
    }
}
