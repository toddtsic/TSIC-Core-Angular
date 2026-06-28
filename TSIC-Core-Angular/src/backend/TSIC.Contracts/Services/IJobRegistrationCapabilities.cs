namespace TSIC.Contracts.Services;

/// <summary>
/// The actor class a capability is resolved for. NOT a role — a coarse door class.
/// <see cref="Admin"/> iff the JWT role-name claim (<c>ClaimTypes.Role</c>, never raw
/// <c>"role"</c>) ∈ {Superuser, Director, SuperDirector} — literally the <c>AdminOnly</c>
/// policy (Program.cs). Everyone else and anonymous = <see cref="User"/>.
///
/// The JWT carries the SINGLE role the user authenticated as FOR THIS JOB, not their max
/// privilege — so a Director acting in a Club-Rep session is a <see cref="User"/> here
/// (dual-role ≠ escalation). An <see cref="Admin"/> session already proves
/// <c>now &lt; ExpiryAdmin</c> (Phase-1 role-offer filter), which is why admins are exempt
/// from the <c>eventConcluded</c> door (see <c>TSIC.Domain.JobRules.JobLifecycle</c>).
/// </summary>
public enum CapabilityActor
{
    /// <summary>Anonymous or any non-admin authenticated role (Player, ClubRep, Staff, …).
    /// Bound by the eventConcluded + supersession door.</summary>
    User = 0,

    /// <summary>Superuser / Director / SuperDirector — session proves <c>now &lt; ExpiryAdmin</c>.
    /// Exempt from toggles and the eventConcluded door, still bound by data preconditions.</summary>
    Admin = 1,
}

/// <summary>
/// The single authoritative answer to "may this actor CREATE registration data on this job
/// right now?" — CREATE-surface gates only (create-freeze scope). Every gate is the derived
/// composition <c>door(actor) AND toggle(c) AND precondition(c)</c>; the flags here are
/// NEVER persisted (honors never-edit-director-config).
///
/// <c>CanEditTeam</c> gates the club-rep wizard's team edit (library name/grad-year/LOP via the
/// pencil) on the director's per-event <c>BClubRepAllowEdit</c> toggle PLUS the same eventConcluded
/// door as its Add/Delete siblings — a concluded event is a higher-level gate that removes editing
/// regardless of the toggle. Other manage-existing edits (<c>CanEditRoster</c>/<c>CanDrop</c> —
/// roster moves, player drops) stay ungated (not a wrong-year vector; admins own post-event
/// corrections). No SETTLE fields: payment is never gated by this authority.
///
/// NOTE — adult is per-channel (Staff / Referee / Recruiter), a deliberate refinement of the
/// plan's single-<c>CanRegisterAdult</c> sketch: the three channels have distinct toggles
/// (and Staff a distinct "teams exist" precondition) and map 1:1 to three separate pulse
/// fields and three controllers, so one bool could neither re-serve the pulse faithfully nor
/// gate the three writes correctly.
/// </summary>
public sealed record JobCapabilitySet
{
    /// <summary>Player <c>preSubmit</c> create (covers CAC multi-team secondary create).
    /// = door · BRegistrationAllowPlayer · player-fees-configured.</summary>
    public required bool CanRegisterPlayer { get; init; }

    /// <summary>Adult Staff (coach) create. = door · BRegistrationAllowStaff · teams-exist
    /// (a coach can only request a team once teams are in — precondition binds even admins).</summary>
    public required bool CanRegisterStaff { get; init; }

    /// <summary>Adult Referee create. = door · BRegistrationAllowReferee.</summary>
    public required bool CanRegisterReferee { get; init; }

    /// <summary>Adult Recruiter create. = door · BRegistrationAllowRecruiter.</summary>
    public required bool CanRegisterRecruiter { get; init; }

    /// <summary>Team <c>register-team</c> + <c>initialize</c> create.
    /// = door · BRegistrationAllowTeam · BClubRepAllowAdd · clubRep-fees-configured.</summary>
    public required bool CanAddTeam { get; init; }

    /// <summary>Team <c>unregister-team</c> (kept from Phase-1; moved onto eventConcluded).
    /// = door · BRegistrationAllowTeam · BClubRepAllowDelete.</summary>
    public required bool CanRemoveTeam { get; init; }

    /// <summary>Club-rep wizard team EDIT (library team name/grad-year/LOP via the pencil).
    /// = door · BRegistrationAllowTeam · BClubRepAllowEdit. The eventConcluded door is the
    /// higher-level gate — a concluded event removes edit regardless of the toggle (mirrors
    /// Add/Delete). Resolved for the job the rep authenticated under (their jobPath claim).</summary>
    public required bool CanEditTeam { get; init; }
}

/// <summary>
/// The ONE composer of registration-create permission. Reads a job's facts once, folds in
/// the eventConcluded door + the director toggles + the data preconditions, and returns the
/// effective <see cref="JobCapabilitySet"/>. Pure read/compose — never writes a director flag.
///
/// Two consumers share this one answer: the pulse SERIALIZES it for the UI (disabled controls)
/// and the write-gates INVOKE it for enforcement (<c>Require(set.CanAddTeam)</c>), so the
/// disabled button and the refused write can never disagree. Fail-closed: an unknown/missing
/// job resolves to all-false.
/// </summary>
public interface IJobRegistrationCapabilities
{
    Task<JobCapabilitySet> ResolveAsync(Guid jobId, CapabilityActor actor, CancellationToken ct = default);
}
