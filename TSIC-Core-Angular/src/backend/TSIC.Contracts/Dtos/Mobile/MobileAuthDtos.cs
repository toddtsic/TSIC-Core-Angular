namespace TSIC.Contracts.Dtos.Mobile;

/// <summary>
/// Why a context exists but cannot be opened. Precedence is deliberate and defined in
/// one place: TeamsAppDisabled outranks NotPlaced, because "assignment pending" implies
/// the row will become usable — and for a club that has not enabled the Teams app, it
/// never will. Null means the context is openable.
/// </summary>
public static class MobileContextUnavailableReason
{
    /// <summary>Jobs.bEnableTSICTeams is not true for this registration's job.</summary>
    public const string TeamsAppDisabled = "TeamsAppDisabled";

    /// <summary>Registration is active but Registrations.AssignedTeamId is still null.</summary>
    public const string NotPlaced = "NotPlaced";
}

// --- Request DTOs ---

public record MobileLoginRequest
{
    public required string Username { get; init; }
    public required string Password { get; init; }
}

public record MobileSelectRegistrationRequest
{
    public required string RegId { get; init; }
}

// --- Response DTOs ---

/// <summary>
/// A roster seat — Player or Staff. Strictly one team per row; a parent with two
/// children on two teams each has four of these. The row is returned whenever the
/// registration is active and its job unexpired, EVEN IF it cannot be opened: absence
/// is reserved for "this account has no registrations at all", so the client can tell
/// "not placed yet" and "club is not on the app" apart from "wrong account".
/// </summary>
public record MobileContextDto
{
    public required string RegId { get; init; }

    /// <summary>DB role name — "Player" or "Staff". Matches the value minted into the JWT role claim.</summary>
    public required string RoleName { get; init; }

    public required string JobName { get; init; }

    /// <summary>Bare path as stored in Jobs.jobPath — no leading slash (e.g. "tsic", "CG2026").</summary>
    public required string JobPath { get; init; }

    public string? JobLogo { get; init; }

    /// <summary>
    /// The person holding the seat, and the client's grouping key for the picker.
    /// For a Player row this is the child (Registrations.UserId), NOT the parent who
    /// logged in. For a Staff row it is the staff member themselves.
    /// </summary>
    public required string PlayerUserId { get; init; }

    /// <summary>First + last name of PlayerUserId. Supplied as its own field, never concatenated.</summary>
    public required string PlayerName { get; init; }

    /// <summary>Null when the registration is not yet placed on a team.</summary>
    public Guid? TeamId { get; init; }

    /// <summary>Null when the registration is not yet placed on a team.</summary>
    public string? TeamName { get; init; }

    /// <summary>Resolved via the team, never Registrations.AssignedAgegroupId. Null when unplaced.</summary>
    public string? Agegroup { get; init; }

    /// <summary>
    /// The team's Google Calendar id, or null when it has none. Sourced from Teams.KeywordPairs,
    /// which is DUAL-PURPOSE: most rows hold scheduling keyword pairs ("Team:Class of 2033") and
    /// only some hold a calendar id, so the column is never surfaced raw — a keyword pair would
    /// point the client's embed at nonsense.
    ///
    /// Normalized to the literal "@" form. The column stores BOTH encodings — 82 rows hold "@"
    /// and 74 hold "%40" — and a client that has to guess which form it was handed cannot build
    /// the embed URL: re-encoding a "%40" id yields "%2540" and a dead iframe.
    /// </summary>
    public string? CalendarId { get; init; }

    /// <summary>Jobs.bEnableTSICTeams. Null in the column is reported as false.</summary>
    public required bool TeamsAppEnabled { get; init; }

    /// <summary>Registrations.AssignedTeamId is non-null.</summary>
    public required bool IsPlaced { get; init; }

    /// <summary>IsPlaced AND TeamsAppEnabled. The single flag the client gates navigation on.</summary>
    public required bool IsOpenable { get; init; }

    /// <summary>Null when IsOpenable. Otherwise a MobileContextUnavailableReason value.</summary>
    public string? UnavailableReason { get; init; }
}

/// <summary>
/// Authority over many teams in a job — Director or Superuser. The holder is on none of
/// those rosters. Carries a COUNT and never a list: a superuser across 50 jobs would
/// otherwise inline roughly 10,000 teams into the login response.
/// </summary>
public record MobileOwnershipDto
{
    public required string RegId { get; init; }

    /// <summary>DB role name — "Director" or "Superuser".</summary>
    public required string RoleName { get; init; }

    public required string JobName { get; init; }

    /// <summary>Bare path as stored in Jobs.jobPath — no leading slash.</summary>
    public required string JobPath { get; init; }

    public string? JobLogo { get; init; }

    /// <summary>Active teams in this job. Fetch the list via ownerships/{regId}/teams.</summary>
    public required int TeamCount { get; init; }

    /// <summary>Jobs.bEnableTSICTeams. Null in the column is reported as false.</summary>
    public required bool TeamsAppEnabled { get; init; }
}

/// <summary>One team under an ownership registration.</summary>
public record MobileOwnershipTeamDto
{
    public required Guid TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string Agegroup { get; init; }
}

/// <summary>
/// Login result. Both arrays may legitimately be empty — a Referee or Store Admin
/// authenticates successfully and has nothing in this app. That is a 200, not a 403.
/// </summary>
public record MobileLoginResponse
{
    /// <summary>
    /// Minimal token, unless auto-resolve applied — see AutoResolved. Never a role-bearing
    /// token when the user still has a choice to make.
    /// </summary>
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }
    public required int ExpiresIn { get; init; }
    public required bool RequiresTosSignature { get; init; }

    /// <summary>
    /// True when there was exactly one OPENABLE context and no ownerships, so the server
    /// minted the enriched token directly and the client may skip select-registration.
    /// </summary>
    public required bool AutoResolved { get; init; }

    /// <summary>
    /// True when this account holds registrations that fall outside their job's expiry
    /// window. Those rows are NOT returned — expired is terminal, and six seasons of dead
    /// rows would bloat every login. This flag exists purely so an empty response can say
    /// "your season has ended" instead of implying the account is empty or wrong.
    /// </summary>
    public required bool HasExpiredRegistrations { get; init; }

    public required IReadOnlyList<MobileContextDto> Contexts { get; init; }
    public required IReadOnlyList<MobileOwnershipDto> Ownerships { get; init; }
}
