using TSIC.Domain.Constants;
using Xunit;

namespace TSIC.Tests.Admin;

/// <summary>
/// The merge key — <c>docs/Domain/change-password-contract.md</c>.
///
/// This is the control that decides whether two accounts are the same household. If it says yes about
/// two accounts that are not, a SuperUser hands one family another family's children, across customers,
/// irreversibly. Every string below is a real value from TSICV5.
///
/// The asymmetry that shapes every test here: a MISS costs nothing — the parent is told to use their
/// new account. A FALSE MATCH is a breach. So the cases that matter most are the ones asserting the key
/// REFUSES.
/// </summary>
public class HouseholdIdentityTests
{
    // ── phones: formatting is not identity, but a placeholder is not a phone ──

    [Theory]
    [InlineData("(201) 815-7044")]
    [InlineData("201-815-7044")]
    [InlineData("201.815.7044")]
    [InlineData("2018157044")]
    [InlineData("+1 201 815 7044")]
    [InlineData("1-201-815-7044")]
    [InlineData("  201 815 7044  ")]
    public void Phone_formatting_is_stripped_to_the_same_identity(string typed)
    {
        Assert.Equal("2018157044", HouseholdIdentity.NormalizePhone(typed));
    }

    [Theory]
    [InlineData("0000000000")]   // 106 households share this one
    [InlineData("5555555555")]
    [InlineData("1111111111")]
    [InlineData("9999999999")]
    [InlineData("1234567890")]
    [InlineData("(000) 000-0000")]
    public void A_placeholder_phone_is_not_an_identity(string junk)
    {
        Assert.Null(HouseholdIdentity.NormalizePhone(junk));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("555-1234")]     // seven digits is not a phone number
    [InlineData("n/a")]
    public void An_absent_or_short_phone_is_not_an_identity(string? value)
    {
        Assert.Null(HouseholdIdentity.NormalizePhone(value));
    }

    // ── emails ──

    [Theory]
    [InlineData("Melissa.Abell@Gmail.com", "melissa.abell@gmail.com")]
    [InlineData("  melissa.abell@gmail.com  ", "melissa.abell@gmail.com")]
    public void Email_case_and_whitespace_are_not_identity(string typed, string expected)
    {
        Assert.Equal(expected, HouseholdIdentity.NormalizeEmail(typed));
    }

    /// <summary>
    /// Gmail delivers both of these to one inbox — and we still treat them as different people.
    /// Every "smart" equivalence widens what counts as the same person, and width is the attack
    /// surface. The recall it would buy is not worth a single wrong merge.
    /// </summary>
    [Fact]
    public void Gmail_dots_and_plus_tags_are_NOT_collapsed()
    {
        var a = HouseholdIdentity.NormalizeEmail("melissa.abell+lax@gmail.com");
        var b = HouseholdIdentity.NormalizeEmail("melissaabell@gmail.com");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("not@given.com")]   // the recorded "no email" flag — a well-formed address, so only a name check catches it
    [InlineData("na@gmail.com")]
    [InlineData("none@none.com")]
    [InlineData("na@na.com")]
    [InlineData("n/a@gmail.com")]
    [InlineData("NONE@GMAIL.COM")]
    public void A_placeholder_email_is_not_an_identity(string junk)
    {
        Assert.Null(HouseholdIdentity.NormalizeEmail(junk));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("meghankcn@hotmail")]   // truncated real address; no dot in the domain
    [InlineData("na@na")]
    [InlineData("not a email")]
    public void A_blank_or_malformed_email_is_not_an_identity(string? value)
    {
        Assert.Null(HouseholdIdentity.NormalizeEmail(value));
    }

    // ── names: typo tolerance, but only after email and phone have already gated ──

    [Theory]
    [InlineData("Melissa", "Abell", "melissa", "ABELL")]
    [InlineData("Melissa", "O'Brien", "Melissa", "OBrien")]
    [InlineData("Melissa", "Abell", "  Melissa  ", "Abell")]
    [InlineData("Melissa", "Abell", "Mellisa", "Abell")]     // Soundex
    [InlineData("Kathy", "Schlatter", "Kathi", "Schlatter")] // Soundex
    public void Same_person_despite_typing(string fa, string la, string fb, string lb)
    {
        Assert.True(HouseholdIdentity.SamePerson(fa, la, fb, lb));
    }

    [Theory]
    [InlineData("Melissa", "Abell", "Michael", "Abell")]     // mom vs dad
    [InlineData("Melissa", "Abell", "Melissa", "Shimizu")]   // different surname
    [InlineData("Melissa", "Abell", "Jennifer", "Nguyen")]
    public void Different_people_are_not_the_same_person(string fa, string la, string fb, string lb)
    {
        Assert.False(HouseholdIdentity.SamePerson(fa, la, fb, lb));
    }

    /// <summary>Two blanks are not a match. Absence must never match absence.</summary>
    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("", "", "", "")]
    [InlineData("Melissa", "Abell", "", "")]
    public void A_missing_name_never_matches(string? fa, string? la, string? fb, string? lb)
    {
        Assert.False(HouseholdIdentity.SamePerson(fa, la, fb, lb));
    }

    // ── the key: all three, or nothing ──

    [Fact]
    public void An_account_with_all_three_parts_has_a_key()
    {
        Assert.True(AccountKey.TryCreate(
            "melissa.abell@gmail.com", "(201) 815-7044", "Melissa", "Abell", out var key));

        Assert.Equal("melissa.abell@gmail.com", key.Email);
        Assert.Equal("2018157044", key.Phone);
    }

    [Theory]
    [InlineData(null, "2018157044", "Melissa", "Abell")]
    [InlineData("melissa.abell@gmail.com", null, "Melissa", "Abell")]
    [InlineData("melissa.abell@gmail.com", "2018157044", null, "Abell")]
    [InlineData("melissa.abell@gmail.com", "2018157044", "Melissa", null)]
    [InlineData("not@given.com", "2018157044", "Melissa", "Abell")]
    [InlineData("melissa.abell@gmail.com", "0000000000", "Melissa", "Abell")]
    public void An_account_missing_any_part_has_NO_key(string? email, string? phone, string? first, string? last)
    {
        // No key means no merge candidates — which is the correct, safe outcome. We cannot establish
        // who this household is, so we will not claim anyone else is them.
        Assert.False(AccountKey.TryCreate(email, phone, first, last, out _));
    }

    /// <summary>The scenario the tool exists for: mom forgot her password and rebuilt her account.</summary>
    [Fact]
    public void The_same_mother_rebuilding_her_account_matches()
    {
        Assert.True(AccountKey.TryCreate(
            "melissa.abell@gmail.com", "(201) 815-7044", "Melissa", "Abell", out var key));

        Assert.True(key.Matches("Melissa.Abell@gmail.com", "201-815-7044", "melissa", "abell"));
        Assert.True(key.Matches("melissa.abell@gmail.com", "2018157044", "Mellisa", "Abell"));
    }

    // ── the refusals. these are the ones that matter. ──

    /// <summary>
    /// Email and phone agree; the mother does not. Measured: 592 pairs in TSICV5 look exactly like this
    /// — a different mother, children with unrelated surnames — and the name is what deletes all of
    /// them. Two families sharing a contact point is not two families being one family.
    /// </summary>
    [Fact]
    public void Same_contact_details_but_a_different_mother_is_REFUSED()
    {
        Assert.True(AccountKey.TryCreate(
            "info@bronxlacrosse.org", "9738515961", "Melissa", "Abell", out var key));

        Assert.False(key.Matches("info@bronxlacrosse.org", "9738515961", "Jennifer", "Nguyen"));
    }

    /// <summary>A club admin who registered other people's families using his own contact block.</summary>
    [Fact]
    public void A_club_admins_contact_block_on_someone_elses_household_is_REFUSED()
    {
        Assert.True(AccountKey.TryCreate(
            "cshoulberg@stepslacrosse.com", "9738515961", "Chris", "Shoulberg", out var key));

        Assert.False(key.Matches("cshoulberg@stepslacrosse.com", "9738515961", "Susan", "Kang"));
    }

    [Fact]
    public void The_same_mother_at_a_different_number_is_REFUSED()
    {
        Assert.True(AccountKey.TryCreate(
            "melissa.abell@gmail.com", "2018157044", "Melissa", "Abell", out var key));

        // A miss. She uses her new account, and nobody is harmed.
        Assert.False(key.Matches("melissa.abell@gmail.com", "9145257119", "Melissa", "Abell"));
    }

    /// <summary>
    /// Two households that both wrote "no email" and "0000000000" are NOT one household. Without the
    /// placeholder rule this is the merge that fuses 106 unrelated families.
    /// </summary>
    [Fact]
    public void Two_households_with_placeholder_contacts_are_REFUSED()
    {
        Assert.False(AccountKey.TryCreate("not@given.com", "0000000000", "Melissa", "Abell", out _));
    }
}
