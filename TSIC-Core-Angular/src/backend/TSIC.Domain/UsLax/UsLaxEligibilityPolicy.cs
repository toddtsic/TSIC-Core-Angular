namespace TSIC.Domain.UsLax;

/// <summary>
/// The single decision point for "may this registrant use this USA Lacrosse number on this job."
///
/// Restores the ruleset legacy ran in <c>ValidationRemoteController.IsUSLaxNumberValid</c> — active
/// membership, Player involvement, expiry at or beyond the director's cutoff, and an exact
/// lastname + DOB match against USA Lacrosse's record. The migration carried over only the first
/// rule and the expiry comparison, and pointed the expiry comparison at a stale JsonOptions key, so
/// the cutoff a director set was never the cutoff that got checked.
///
/// Pure and primitive-in on purpose: the vendor DTO lives in TSIC.API, so keeping this in Domain
/// lets the live field check (ValidationController) and the submit gate (PlayerRegistrationService)
/// share one implementation instead of drifting the way the client and server copies did.
///
/// Identity is an AND, never an OR: an active number proves a membership exists, not that it
/// belongs to the child being registered.
/// </summary>
public static class UsLaxEligibilityPolicy
{
    /// <summary>Well-known number that bypasses vendor validation for testing. Legacy honored it
    /// as the first statement in the action; the wizard already short-circuits on it client-side.</summary>
    public const string TestMembershipNumber = "424242424242";

    /// <summary>
    /// Legacy's <c>[Remote]</c> failure text, verbatim from the ~20 PP/CAC form models that carried
    /// it (e.g. PP20ViewModel). It enumerates every way this check can fail and routes the family to
    /// USA Lacrosse to fix it themselves, which is why it stays a single message rather than being
    /// split per reason — the reason code is for us, this text is for the parent.
    /// The wizard renders it through its existing HTML-error popup.
    /// </summary>
    public const string FailureMessageHtml =
        "<strong>We encountered an issue validating the USA Lacrosse Number you entered. To successfully pass validation, please confirm the following:</strong>"
        + "<ol><li>The USA Lacrosse Number is entered correctly</li>"
        + "<li>The membership is Valid and Active</li>"
        + "<li>The membership <strong>does not expire before the date required </strong>by the event or club director</li>"
        + "<li>The <strong>Date of Birth and Last Name</strong> of the player entered above exactly match what USA Lacrosse has on file</li>"
        + "<li>The member has completed the USA Lacrosse <strong>age verification process</strong>*</li></ol>"
        + "*Beginning July 1, 2025, all USA Lacrosse player members are required to complete a one-time age verification process to maintain an active membership. "
        + "(<a href='https://www.usalacrosse.com/age-verification' target='_blank'>Learn more</a>)<br><br>"
        + "<strong>Helpful Links:</strong><ul>"
        + "<li>Look up your USA Lacrosse Number - <a href='https://account.usalacrosse.com/login/lookup' target='_blank'>CLICK HERE</a></li>"
        + "<li>Register for a USA Lacrosse Number - <a href='https://www.usalacrosse.com/membership' target='_blank'>CLICK HERE</a></li></ul>"
        + "For assistance please contact <a href='mailto:membership@usalacrosse.com'>membership@usalacrosse.com</a> or call 410-235-6882";

    /// <summary>Transient-vendor message. Separate from the checklist above because "try again in a
    /// moment" is the only useful instruction, and telling a parent to fix their membership when our
    /// call failed sends them to USA Lacrosse's support line for nothing.</summary>
    public const string VendorUnavailableMessage =
        "We couldn't reach USA Lacrosse to verify this number. Please try again in a moment.";

    public static UsLaxEligibilityVerdict Evaluate(UsLaxEligibilityInput input)
    {
        // Bypasses first, exactly as legacy ordered them: the test number short-circuits before any
        // vendor traffic, and a team flagged bDoNotValidateUSLaxNumber opts out of the check entirely.
        if (string.Equals(input.MembershipNumber?.Trim(), TestMembershipNumber, StringComparison.Ordinal))
            return Pass(UsLaxEligibilityReason.TestNumber, null);

        if (input.TeamValidationDisabled)
            return Pass(UsLaxEligibilityReason.TeamBypass, null);

        // No cutoff configured → reject. Legacy's `if (lastGameDay != null && ...)` fell through to
        // `return Ok(false)`, and every live job that collects a number has a cutoff set, so this
        // stays fail-closed rather than becoming a silent hole on a misconfigured job.
        if (input.ValidThrough is null || input.ValidThrough == DateTime.MinValue)
            return Fail(UsLaxEligibilityReason.NoCutoffConfigured);

        // StatusCode 0 is our transport/parse failure, not a vendor verdict — distinguish it so the
        // registrant gets "try again" instead of "your membership is bad."
        if (input.VendorStatusCode == 0)
            return Fail(UsLaxEligibilityReason.VendorUnavailable);

        if (input.VendorStatusCode != 200)
            return Fail(UsLaxEligibilityReason.NotFound);

        // Legacy required all three source fields to be present and parseable before comparing.
        if (!TryParseDate(input.VendorExpDate, out var expDate))
            return Fail(UsLaxEligibilityReason.NotFound);
        if (!TryParseDate(input.VendorBirthdate, out var vendorDob))
            return Fail(UsLaxEligibilityReason.DobMismatch);
        if (string.IsNullOrWhiteSpace(input.VendorLastName))
            return Fail(UsLaxEligibilityReason.LastNameMismatch);

        if (!string.Equals(input.VendorMemStatus?.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
            return Fail(UsLaxEligibilityReason.NotActive);

        // A coach's or official's number is a real active membership — it just isn't a player's.
        // Nothing in the migrated code checked this, so any active number passed.
        if (input.VendorInvolvement is null
            || !input.VendorInvolvement.Any(i => string.Equals(i?.Trim(), "Player", StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(UsLaxEligibilityReason.NotAPlayer);
        }

        if (!string.Equals(NormalizeName(input.RegistrantLastName), NormalizeName(input.VendorLastName),
                StringComparison.OrdinalIgnoreCase))
        {
            return Fail(UsLaxEligibilityReason.LastNameMismatch);
        }

        if (input.RegistrantDob is null || input.RegistrantDob.Value.Date != vendorDob.Date)
            return Fail(UsLaxEligibilityReason.DobMismatch);

        // Legacy: `lastGameDay <= exp_date`. A membership expiring ON the cutoff is valid THROUGH it.
        // Compared date-only: the vendor sends a bare ISO date (UTC midnight when parsed) and the
        // cutoff is a local DateTime, so a raw comparison would reject an exact match by the offset.
        if (expDate.Date < input.ValidThrough.Value.Date)
            return Fail(UsLaxEligibilityReason.ExpiresBeforeCutoff, expDate);

        return Pass(UsLaxEligibilityReason.Eligible, expDate);
    }

    /// <summary>Message for a verdict — the checklist for anything the registrant can act on,
    /// the transient notice when the failure was ours.</summary>
    public static string? MessageFor(UsLaxEligibilityVerdict verdict) => verdict.Reason switch
    {
        UsLaxEligibilityReason.VendorUnavailable => VendorUnavailableMessage,
        _ => verdict.Valid ? null : FailureMessageHtml
    };

    /// <summary>Legacy stripped the three apostrophe variants before comparing, so O'Brien typed with
    /// a smart quote still matches USA Lacrosse's plain one.</summary>
    private static string NormalizeName(string? raw) =>
        (raw ?? string.Empty).Replace("’", "").Replace("'", "").Replace("`", "").Trim();

    private static bool TryParseDate(string? raw, out DateTime value) =>
        DateTime.TryParse(raw, out value) && value != DateTime.MinValue;

    private static UsLaxEligibilityVerdict Pass(UsLaxEligibilityReason reason, DateTime? expDate) =>
        new() { Valid = true, Reason = reason, ExpDate = expDate };

    private static UsLaxEligibilityVerdict Fail(UsLaxEligibilityReason reason, DateTime? expDate = null) =>
        new() { Valid = false, Reason = reason, ExpDate = expDate };
}

/// <summary>Everything the decision needs, as primitives — see <see cref="UsLaxEligibilityPolicy"/>.</summary>
public sealed record UsLaxEligibilityInput
{
    public required string? MembershipNumber { get; init; }

    /// <summary>Jobs.USLaxNumberValidThroughDate — the column the director edits, NOT the stale
    /// JsonOptions key the wizard used to read.</summary>
    public required DateTime? ValidThrough { get; init; }

    /// <summary>Leagues.teams.bDoNotValidateUSLaxNumber for the team being registered for.</summary>
    public bool TeamValidationDisabled { get; init; }

    public required int VendorStatusCode { get; init; }
    public string? VendorMemStatus { get; init; }
    public string? VendorExpDate { get; init; }
    public string? VendorLastName { get; init; }
    public string? VendorBirthdate { get; init; }
    public IReadOnlyList<string>? VendorInvolvement { get; init; }

    public string? RegistrantLastName { get; init; }
    public DateTime? RegistrantDob { get; init; }
}

public sealed record UsLaxEligibilityVerdict
{
    public required bool Valid { get; init; }
    public required UsLaxEligibilityReason Reason { get; init; }

    /// <summary>Parsed vendor expiry when we got one — the value callers stamp onto
    /// Registrations.SportAssnIdexpDate.</summary>
    public DateTime? ExpDate { get; init; }
}

public enum UsLaxEligibilityReason
{
    Eligible,
    TestNumber,
    TeamBypass,
    NoCutoffConfigured,
    VendorUnavailable,
    NotFound,
    NotActive,
    NotAPlayer,
    ExpiresBeforeCutoff,
    LastNameMismatch,
    DobMismatch,
}
