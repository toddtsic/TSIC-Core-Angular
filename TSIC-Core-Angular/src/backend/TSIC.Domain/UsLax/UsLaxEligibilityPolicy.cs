using System.Globalization;

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
///
/// ROLE: legacy ran two validators — <c>ValidationRemoteController</c> for players and
/// <c>ValidationCoachRemoteController</c> for coaches — whose rules are character-for-character
/// identical except for the involvement string they require, so that is the one parameter here
/// (<see cref="UsLaxEligibilityInput.RequiredInvolvement"/>) rather than a second policy. The
/// team bypass is the lone rule legacy applied to players only; see below.
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

        // PLAYERS ONLY. Legacy's coach validator never read bDoNotValidateUSLaxNumber — only the
        // player one did — so a team's opt-out excuses its players, not the adults coaching them.
        if (input.TeamValidationDisabled && !RequiresCoach(input))
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

        if (!IsActive(input))
            return Fail(UsLaxEligibilityReason.NotActive);

        // A coach's number is a real active membership — it just isn't a player's, and vice versa.
        // Nothing in the migrated code checked this, so any active number passed either gate.
        if (!HasRequiredInvolvement(input))
        {
            return Fail(RequiresCoach(input)
                ? UsLaxEligibilityReason.NotACoach
                : UsLaxEligibilityReason.NotAPlayer);
        }

        if (!LastNameMatches(input))
            return Fail(UsLaxEligibilityReason.LastNameMismatch);

        if (!DobMatches(input, vendorDob))
            return Fail(UsLaxEligibilityReason.DobMismatch);

        if (!ExpiryCoversCutoff(input, expDate))
            return Fail(UsLaxEligibilityReason.ExpiresBeforeCutoff, expDate);

        return Pass(UsLaxEligibilityReason.Eligible, expDate);
    }

    // ── The rules, one definition each ──
    // Extracted so Evaluate (ordered, short-circuits on the first failure) and Describe (evaluates
    // every row for the checklist UI) cannot drift. Adding a rule means adding it in both callers,
    // never re-implementing it.

    private static bool IsActive(UsLaxEligibilityInput input) =>
        string.Equals(input.VendorMemStatus?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);

    private static bool HasRequiredInvolvement(UsLaxEligibilityInput input) =>
        input.VendorInvolvement is not null
        && input.VendorInvolvement.Any(i =>
            string.Equals(i?.Trim(), input.RequiredInvolvement, StringComparison.OrdinalIgnoreCase));

    private static bool LastNameMatches(UsLaxEligibilityInput input) =>
        !string.IsNullOrWhiteSpace(input.VendorLastName)
        && string.Equals(NormalizeName(input.RegistrantLastName), NormalizeName(input.VendorLastName),
            StringComparison.OrdinalIgnoreCase);

    private static bool DobMatches(UsLaxEligibilityInput input, DateTime vendorDob) =>
        input.RegistrantDob is not null && input.RegistrantDob.Value.Date == vendorDob.Date;

    /// <summary>Legacy: <c>lastGameDay &lt;= exp_date</c>. A membership expiring ON the cutoff is valid
    /// THROUGH it. Compared date-only: the vendor sends a bare ISO date (UTC midnight when parsed) and
    /// the cutoff is a local DateTime, so a raw comparison would reject an exact match by the offset.</summary>
    private static bool ExpiryCoversCutoff(UsLaxEligibilityInput input, DateTime expDate) =>
        input.ValidThrough is not null && expDate.Date >= input.ValidThrough.Value.Date;

    /// <summary>Message for a verdict — the checklist for anything the registrant can act on,
    /// the transient notice when the failure was ours.</summary>
    public static string? MessageFor(UsLaxEligibilityVerdict verdict) => verdict.Reason switch
    {
        UsLaxEligibilityReason.VendorUnavailable => VendorUnavailableMessage,
        _ => verdict.Valid ? null : FailureMessageHtml
    };

    /// <summary>
    /// Every criterion, each judged INDEPENDENTLY — for a UI that shows a checklist rather than a
    /// single verdict. <see cref="Evaluate"/> stops at the first failure because that is what a gate
    /// needs; an admin looking at one registrant needs to see that the last name AND the birthdate
    /// both disagree, not just whichever the ordering happened to reach first.
    ///
    /// Both call the same private predicates above, so a row can never claim something the gate
    /// does not enforce. <see cref="UsLaxCheckRow.Passed"/> is nullable: null means NOT ASSESSABLE
    /// (vendor unreachable, no cutoff configured, validation bypassed) — never "passed".
    /// </summary>
    public static IReadOnlyList<UsLaxCheckRow> Describe(UsLaxEligibilityInput input)
    {
        var rows = new List<UsLaxCheckRow>();
        var involvementLabel = RequiresCoach(input) ? UsLaxInvolvement.Coach : UsLaxInvolvement.Player;

        // Bypasses answer the whole question, so they replace the checklist rather than decorate it.
        if (string.Equals(input.MembershipNumber?.Trim(), TestMembershipNumber, StringComparison.Ordinal))
        {
            rows.Add(new UsLaxCheckRow
            {
                Key = nameof(UsLaxEligibilityReason.TestNumber),
                Label = "Validation bypassed",
                Passed = null,
                Detail = "This is the reserved test membership number — USA Lacrosse was not checked."
            });
            return rows;
        }

        if (input.TeamValidationDisabled && !RequiresCoach(input))
        {
            rows.Add(new UsLaxCheckRow
            {
                Key = nameof(UsLaxEligibilityReason.TeamBypass),
                Label = "Validation bypassed",
                Passed = null,
                Detail = "USA Lacrosse validation is turned off for this player's team."
            });
            return rows;
        }

        // Our own failure — say so on every row instead of reporting six false negatives.
        if (input.VendorStatusCode == 0)
        {
            rows.Add(new UsLaxCheckRow
            {
                Key = nameof(UsLaxEligibilityReason.VendorUnavailable),
                Label = "USA Lacrosse reachable",
                Passed = false,
                Detail = "Couldn't reach USA Lacrosse, so nothing below could be checked. Try again in a moment."
            });
            return rows;
        }

        var found = input.VendorStatusCode == 200;
        rows.Add(new UsLaxCheckRow
        {
            Key = nameof(UsLaxEligibilityReason.NotFound),
            Label = "USA Lacrosse has a record for this number",
            Passed = found,
            Detail = found ? null : "No membership record was returned for this number."
        });

        // Nothing below is answerable without a record.
        if (!found) return rows;

        rows.Add(new UsLaxCheckRow
        {
            Key = nameof(UsLaxEligibilityReason.NotActive),
            Label = "Membership is Active",
            Passed = IsActive(input),
            Detail = IsActive(input) ? null
                : $"USA Lacrosse reports status \"{Blank(input.VendorMemStatus)}\", not Active."
        });

        var involvementOk = HasRequiredInvolvement(input);
        rows.Add(new UsLaxCheckRow
        {
            Key = RequiresCoach(input)
                ? nameof(UsLaxEligibilityReason.NotACoach)
                : nameof(UsLaxEligibilityReason.NotAPlayer),
            Label = $"Registered as a {involvementLabel} at USA Lacrosse",
            Passed = involvementOk,
            Detail = involvementOk ? null
                : $"USA Lacrosse lists {DescribeInvolvement(input.VendorInvolvement)} — not {involvementLabel}."
        });

        var lastNameOk = LastNameMatches(input);
        rows.Add(new UsLaxCheckRow
        {
            Key = nameof(UsLaxEligibilityReason.LastNameMismatch),
            Label = "Last name matches USA Lacrosse",
            Passed = lastNameOk,
            Detail = lastNameOk ? null
                : $"We have \"{Blank(input.RegistrantLastName)}\", USA Lacrosse has \"{Blank(input.VendorLastName)}\"."
        });

        var dobParsed = TryParseDate(input.VendorBirthdate, out var vendorDob);
        var dobOk = dobParsed && DobMatches(input, vendorDob);
        rows.Add(new UsLaxCheckRow
        {
            Key = nameof(UsLaxEligibilityReason.DobMismatch),
            Label = "Date of birth matches USA Lacrosse",
            Passed = dobOk,
            Detail = dobOk ? null
                : $"We have {Date(input.RegistrantDob)}, USA Lacrosse has {(dobParsed ? Date(vendorDob) : Blank(input.VendorBirthdate))}."
        });

        // The one row that depends on the director's own configuration, so an unset cutoff reads as
        // "we can't tell" rather than a failure the registrant could act on.
        var expParsed = TryParseDate(input.VendorExpDate, out var expDate);
        var haveCutoff = input.ValidThrough is not null && input.ValidThrough != DateTime.MinValue;
        rows.Add(new UsLaxCheckRow
        {
            Key = haveCutoff
                ? nameof(UsLaxEligibilityReason.ExpiresBeforeCutoff)
                : nameof(UsLaxEligibilityReason.NoCutoffConfigured),
            Label = haveCutoff
                ? $"Valid through this event's {Date(input.ValidThrough)} cutoff"
                : "Valid through this event's cutoff",
            Passed = !haveCutoff ? null : expParsed && ExpiryCoversCutoff(input, expDate),
            Detail = !haveCutoff
                ? "No USA Lacrosse valid-through date is set for this event, so expiry can't be checked."
                : !expParsed
                    ? "USA Lacrosse returned no usable expiration date."
                    : ExpiryCoversCutoff(input, expDate)
                        ? $"Expires {Date(expDate)}."
                        : $"Expires {Date(expDate)} — before the {Date(input.ValidThrough)} cutoff."
        });

        return rows;
    }

    private static string Blank(string? s) => string.IsNullOrWhiteSpace(s) ? "nothing" : s.Trim();

    private static string Date(DateTime? d) =>
        d?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "nothing";

    private static string DescribeInvolvement(IReadOnlyList<string>? involvement)
    {
        if (involvement is null || involvement.Count == 0) return "no involvement";
        return string.Join(", ", involvement.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()));
    }

    private static bool RequiresCoach(UsLaxEligibilityInput input) =>
        string.Equals(input.RequiredInvolvement?.Trim(), UsLaxInvolvement.Coach, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One plain-English line naming the SPECIFIC discrepancy, with the real values in it — for an
    /// ADMIN audience (the reconcile grid's Details column, the registration-search detail panel).
    ///
    /// Deliberately not <see cref="MessageFor"/>: that is the parent-facing HTML checklist of
    /// remediation steps. A director needs to know which field disagrees, not how a family fixes it.
    /// Lives here so every admin surface reads the same sentence for the same verdict.
    /// </summary>
    public static string? DetailFor(
        UsLaxEligibilityVerdict verdict,
        DateTime? validThrough,
        string? registrantLastName,
        DateTime? registrantDob,
        string? vendorMemStatus,
        string? vendorLastName,
        string? vendorBirthdate)
    {
        static string D(DateTime? d) => d?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "—";

        return verdict.Reason switch
        {
            UsLaxEligibilityReason.Eligible => null,
            UsLaxEligibilityReason.TestNumber => "Test membership number — validation bypassed.",
            UsLaxEligibilityReason.TeamBypass => "USA Lacrosse validation is turned off for this team.",
            UsLaxEligibilityReason.NoCutoffConfigured =>
                "No USA Lacrosse valid-through date is set for this event, so memberships can't be checked against a cutoff.",
            UsLaxEligibilityReason.VendorUnavailable => "Couldn't reach USA Lacrosse — try again.",
            UsLaxEligibilityReason.NotFound => "USA Lacrosse has no membership record for this number.",
            UsLaxEligibilityReason.NotActive =>
                $"USA Lacrosse membership status is {vendorMemStatus ?? "unknown"}, not Active.",
            UsLaxEligibilityReason.NotAPlayer =>
                "Membership is not registered as a Player at USA Lacrosse.",
            UsLaxEligibilityReason.NotACoach =>
                "Membership is not registered as a Coach at USA Lacrosse.",
            UsLaxEligibilityReason.ExpiresBeforeCutoff =>
                $"Expires {D(verdict.ExpDate)} — before this event's {D(validThrough)} cutoff.",
            UsLaxEligibilityReason.LastNameMismatch =>
                $"Last name doesn't match USA Lacrosse (we have \"{registrantLastName}\", they have \"{vendorLastName ?? "nothing"}\").",
            UsLaxEligibilityReason.DobMismatch =>
                $"Date of birth doesn't match USA Lacrosse (we have {D(registrantDob)}, they have {vendorBirthdate ?? "nothing"}).",
            _ => null
        };
    }

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

/// <summary>One criterion's result, for a checklist UI — see <see cref="UsLaxEligibilityPolicy.Describe"/>.</summary>
public sealed record UsLaxCheckRow
{
    /// <summary>Stable identifier, matching the <see cref="UsLaxEligibilityReason"/> this row would
    /// produce. For styling/telemetry — never render it.</summary>
    public required string Key { get; init; }

    /// <summary>What is being checked, phrased as the passing condition.</summary>
    public required string Label { get; init; }

    /// <summary>true = passed, false = failed, null = NOT ASSESSABLE (bypassed, no cutoff set,
    /// vendor unreachable). Null must never be rendered as a pass.</summary>
    public bool? Passed { get; init; }

    /// <summary>The specific discrepancy with the real values in it. Null when the row passed.</summary>
    public string? Detail { get; init; }
}

/// <summary>The involvement values USA Lacrosse returns that we gate on. Strings, not an enum, so
/// the policy stays primitive-in and TSIC.Domain keeps its zero project references.</summary>
public static class UsLaxInvolvement
{
    public const string Player = "Player";
    public const string Coach = "Coach";
}

/// <summary>Everything the decision needs, as primitives — see <see cref="UsLaxEligibilityPolicy"/>.</summary>
public sealed record UsLaxEligibilityInput
{
    public required string? MembershipNumber { get; init; }

    /// <summary>Which involvement USA Lacrosse must list for this registration — <c>Player</c> or
    /// <c>Coach</c> (<see cref="UsLaxInvolvement"/>). Defaults to Player: every pre-existing caller
    /// is a player gate, so the default keeps them unchanged. This is the ONLY rule that differed
    /// between legacy's two validators, plus the player-only team bypass it drives.</summary>
    public string RequiredInvolvement { get; init; } = UsLaxInvolvement.Player;

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
    /// <summary>Active membership that USA Lacrosse does not list a Coach involvement for. The
    /// coach-side counterpart of <see cref="NotAPlayer"/> — kept separate so the reason string a
    /// director reads names the involvement that was actually required.</summary>
    NotACoach,
    ExpiresBeforeCutoff,
    LastNameMismatch,
    DobMismatch,
}
