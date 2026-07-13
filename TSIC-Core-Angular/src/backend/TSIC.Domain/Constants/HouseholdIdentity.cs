using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace TSIC.Domain.Constants;

/// <summary>
/// Decides whether two family accounts are <b>the same household</b>.
///
/// ── THIS IS A SECURITY CONTROL, NOT A MATCHING HEURISTIC ──
///
/// A merge re-points one household's registrations — its children, its history — onto another
/// household's login. If this code says "same" about two accounts that are not, a SuperUser hands one
/// family another family's children, cross-tenant, irreversibly. The SuperUser is clicking a list that
/// THIS CODE produced, so the candidate list *is* the security boundary. Nothing downstream of it can
/// recover from a wrong answer here.
///
/// The cost of the two failure modes is wildly asymmetric, and every rule below follows from that:
///
///   * a MISS costs nothing. The parent is told to use their new account going forward. That is a fine
///     outcome and nobody is harmed.
///   * a FALSE MATCH is a data breach.
///
/// So the key is deliberately narrow, and the recall it gives up is not worth arguing about.
///
/// ── THE KEY ──
///
/// The family account IS the mother's data. Three things, ALL of which must agree:
///
///     Mom email   exact, after normalizing case and whitespace
///     Mom phone   exact, after reducing to the last ten digits
///     Mom name    exact, else Soundex-equal
///
/// Measured against TSICV5 (661,074 registrations):
///
///     email AND phone alone            47,437 linked pairs, of which 592 look like strangers —
///                                      different mother, children with unrelated surnames
///     + name                           deletes ALL 592, at a cost of 2,749 genuine duplicates (7%)
///     + Soundex on the name            recovers 868 of those (Kate/Katherine, Mellisa/Melissa)
///
/// Soundex is safe HERE and only here: it runs on a set that email-and-phone has already gated, so it
/// can only ever NARROW the candidates. It can never widen the blast radius. Do not "improve" the email
/// or phone rules with the same instinct — see below.
/// </summary>
public static class HouseholdIdentity
{
    /// <summary>
    /// Contact values people type to get past a required field. They are FLAGS, not contacts.
    ///
    /// Matching on one links strangers: <c>0000000000</c> sits on 106 different households and
    /// <c>na@gmail.com</c> on 22. An account whose contact data is a placeholder therefore has NO
    /// identity and gets NO merge candidates — absence must never match absence.
    ///
    /// Kept as a literal list rather than inferred by fan-out: a threshold is a knob someone will
    /// eventually turn, and this is not a knob.
    /// </summary>
    private static readonly HashSet<string> PlaceholderEmails = new(StringComparer.OrdinalIgnoreCase)
    {
        EmailAddressRules.NotGiven,
        "na@gmail.com", "na@na.com", "n/a@gmail.com",
        "none@none.com", "none@gmail.com", "no@email.com", "noemail@gmail.com"
    };

    private static readonly HashSet<string> PlaceholderPhones =
    [
        "0000000000", "1111111111", "2222222222", "3333333333", "4444444444",
        "5555555555", "6666666666", "7777777777", "8888888888", "9999999999",
        "1234567890", "1234567891", "0123456789"
    ];

    /// <summary>
    /// An address reduced to an identity, or null if it cannot be one.
    ///
    /// Normalization strips FORMATTING ONLY. It deliberately does NOT strip Gmail dots or <c>+tags</c>,
    /// even though Gmail delivers <c>m.abell+lax@gmail.com</c> and <c>mabell@gmail.com</c> to the same
    /// inbox — every such rule widens what counts as "the same person", and width is the attack surface.
    /// </summary>
    public static string? NormalizeEmail(string? email)
    {
        if (!EmailAddressRules.IsWellFormed(email)) return null;

        var value = email.Trim().ToLowerInvariant();
        return PlaceholderEmails.Contains(value) ? null : value;
    }

    /// <summary>
    /// A phone reduced to an identity: the last ten digits, or null if it cannot be one.
    /// <c>(201) 815-7044</c>, <c>201.815.7044</c> and <c>+1 201 815 7044</c> are one number.
    /// Anything shorter than ten digits is not a US phone number and is not an identity.
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;

        var digits = new StringBuilder(phone.Length);
        foreach (var c in phone)
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
        }

        if (digits.Length < 10) return null;

        // The last ten: a leading country code is formatting, not identity. "+1 201 815 7044" is
        // the same number as "(201) 815-7044".
        var last10 = digits.ToString()[^10..];
        return PlaceholderPhones.Contains(last10) ? null : last10;
    }

    /// <summary>
    /// The two names belong to the same person. Exact first, then Soundex — which is what recovers
    /// <c>Mellisa</c> for <c>Melissa</c> and <c>Kathy</c> for <c>Kathi</c>.
    ///
    /// Only ever called on candidates that already agree on email AND phone.
    /// </summary>
    public static bool SamePerson(string? firstA, string? lastA, string? firstB, string? lastB)
    {
        var fa = Fold(firstA);
        var la = Fold(lastA);
        var fb = Fold(firstB);
        var lb = Fold(lastB);

        // A nameless account has no identity to compare. Refuse rather than match on two blanks.
        if (fa.Length == 0 || la.Length == 0 || fb.Length == 0 || lb.Length == 0) return false;

        if (fa == fb && la == lb) return true;

        return Soundex(fa) == Soundex(fb) && Soundex(la) == Soundex(lb);
    }

    /// <summary>Case, surrounding whitespace and punctuation are not identity. O'Brien is OBrien.</summary>
    private static string Fold(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
        {
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Standard Soundex. Matches SQL Server's <c>SOUNDEX()</c>, so the coverage numbers in this file's
    /// summary — measured with <c>DIFFERENCE(a,b) = 4</c> — are the numbers this produces.
    /// </summary>
    private static string Soundex(string folded)
    {
        if (folded.Length == 0) return "";

        var sb = new StringBuilder(4);
        sb.Append(char.ToUpperInvariant(folded[0]));

        var previous = Code(folded[0]);

        for (var i = 1; i < folded.Length && sb.Length < 4; i++)
        {
            var code = Code(folded[i]);

            // A vowel breaks a run, so "Tymczak" keeps both consonant codes; H and W do not.
            if (code != '0' && code != previous) sb.Append(code);
            if (folded[i] is not ('h' or 'w')) previous = code;
        }

        return sb.Append('0', 4 - sb.Length).ToString();
    }

    private static char Code(char c) => c switch
    {
        'b' or 'f' or 'p' or 'v' => '1',
        'c' or 'g' or 'j' or 'k' or 'q' or 's' or 'x' or 'z' => '2',
        'd' or 't' => '3',
        'l' => '4',
        'm' or 'n' => '5',
        'r' => '6',
        _ => '0'
    };
}

/// <summary>
/// The identity of one account, normalized. An account only HAS one if all three parts are real; see
/// <see cref="TryCreate"/>.
///
/// For a FAMILY login the three parts come from the mother — <c>Families.Mom_Email</c>,
/// <c>Mom_Cellphone</c>, <c>Mom_FirstName</c>/<c>Mom_LastName</c>. The family account IS her data; the
/// login's own <c>AspNetUsers</c> row carries an email but essentially never a phone (64 of 60,895
/// duplicate pairs), so it cannot key anything.
///
/// For an ADULT they come from the adult's own <c>AspNetUsers</c> row. An adult signs in as themselves,
/// so their account and their identity are the same record.
/// </summary>
public sealed record AccountKey
{
    private AccountKey(string email, string phone, string firstName, string lastName)
    {
        Email = email;
        Phone = phone;
        FirstName = firstName;
        LastName = lastName;
    }

    public string Email { get; }
    public string Phone { get; }
    public string FirstName { get; }
    public string LastName { get; }

    /// <summary>
    /// The key for an account, or false if it does not have one — a blank, malformed or placeholder
    /// email or phone, or a missing name. <b>No key means no merge candidates</b>, which is the correct
    /// and safe outcome: we cannot establish who this account is, so we will not claim anyone else is
    /// them.
    /// </summary>
    public static bool TryCreate(
        string? email,
        string? phone,
        string? firstName,
        string? lastName,
        [NotNullWhen(true)] out AccountKey? key)
    {
        key = null;

        var normalizedEmail = HouseholdIdentity.NormalizeEmail(email);
        if (normalizedEmail is null) return false;

        var normalizedPhone = HouseholdIdentity.NormalizePhone(phone);
        if (normalizedPhone is null) return false;

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)) return false;

        key = new AccountKey(normalizedEmail, normalizedPhone, firstName.Trim(), lastName.Trim());
        return true;
    }

    /// <summary>True if that account is the same person. All three parts must agree.</summary>
    public bool Matches(string? email, string? phone, string? firstName, string? lastName)
        => HouseholdIdentity.NormalizeEmail(email) == Email
           && HouseholdIdentity.NormalizePhone(phone) == Phone
           && HouseholdIdentity.SamePerson(FirstName, LastName, firstName, lastName);
}
