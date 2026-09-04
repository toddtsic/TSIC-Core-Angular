namespace TSIC.Contracts.Dtos.RosterSwapper;

/// <summary>
/// Pool dropdown option (teams grouped by agegroup/division + special Unassigned Adults pool).
/// </summary>
public record SwapperPoolOptionDto
{
    public required Guid PoolId { get; init; }
    public required string PoolName { get; init; }
    public required bool IsUnassignedAdultsPool { get; init; }
    /// <summary>Owning club (via the clubrep route), when the team has one — for club→agegroup grouping.</summary>
    public string? ClubName { get; init; }
    public string? AgegroupName { get; init; }
    public string? AgegroupColor { get; init; }
    public string? DivName { get; init; }
    public Guid? AgegroupId { get; init; }
    public Guid? DivId { get; init; }
    public required int RosterCount { get; init; }
    public required int MaxCount { get; init; }
    public required bool Active { get; init; }
}

/// <summary>
/// Player row in roster table.
/// </summary>
public record SwapperPlayerDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string RoleName { get; init; }
    public required bool BActive { get; init; }
    public string? School { get; init; }
    public short? Grade { get; init; }
    public int? GradYear { get; init; }
    public string? Position { get; init; }
    public DateTime? Dob { get; init; }
    public string? Gender { get; init; }
    public string? SkillLevel { get; init; }
    public int? YrsExp { get; init; }
    public string? Requests { get; init; }
    public string? PrevCoach { get; init; }
    public required decimal FeeBase { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public DateTime? RegistrationTs { get; init; }
    public string? UniformNo { get; init; }
}

/// <summary>
/// Transfer request (supports multi-select).
/// Backend detects transfer type from source/target pool IDs and registration roles.
/// </summary>
public record RosterTransferRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid SourcePoolId { get; init; }
    public required Guid TargetPoolId { get; init; }
}

/// <summary>
/// Fee impact preview per player.
/// </summary>
public record RosterTransferFeePreviewDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string TransferType { get; init; }
    public required decimal CurrentFeeBase { get; init; }
    public required decimal CurrentFeeTotal { get; init; }
    public required decimal NewFeeBase { get; init; }
    public required decimal NewFeeTotal { get; init; }
    public required decimal FeeDelta { get; init; }
    public string? Warning { get; init; }
}

/// <summary>
/// Fee preview request.
/// </summary>
public record RosterTransferPreviewRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    public required Guid SourcePoolId { get; init; }
    public required Guid TargetPoolId { get; init; }
}

/// <summary>
/// Transfer result — describes what happened (role-dependent).
/// </summary>
public record RosterTransferResultDto
{
    public required int PlayersTransferred { get; init; }
    public required int StaffCreated { get; init; }
    public required int StaffDeleted { get; init; }
    public required int FeesRecalculated { get; init; }
    public required string Message { get; init; }

    /// <summary>
    /// The registrations that ACTUALLY moved. Never assume this equals the requested id list — a
    /// registrant whose recurring-billing plan the move would break is skipped (see <see cref="Blocked"/>).
    /// The UI keys its "just moved" highlight and scroll off THIS list, never off the request:
    /// highlighting a player who stayed put is a lie the roster panel tells until the next reload.
    /// </summary>
    public required List<Guid> MovedRegistrationIds { get; init; }

    /// <summary>
    /// Registrants deliberately NOT moved, each with a director-readable reason. Same
    /// skip-with-reason shape as <c>ArbNotifyResultDto.Skips</c>: serve everyone the operation
    /// can, then report precisely who it could not serve and why.
    /// </summary>
    public required List<RosterTransferBlockedDto> Blocked { get; init; }

    /// <summary>
    /// Registrants that DID move but carry a consequence the operator has to act on — today, a
    /// live recurring-billing plan whose per-draft amount no longer matches the new fee total.
    /// <para>
    /// This used to be a refusal (the registrant landed in <see cref="Blocked"/> and stayed put).
    /// Ruled 09-04: the move proceeds and the operator is warned instead — moving a subscribed
    /// player is routine, and refusing it stopped ordinary work. NOTHING REPRICES THE PLAN: the
    /// subscription keeps drafting the old amount until someone cancels or adjusts it, which is
    /// exactly what the warning says.
    /// </para>
    /// </summary>
    public required List<RosterTransferBlockedDto> Warnings { get; init; }
}

/// <summary>
/// One registrant the transfer refused to move, and a reason the director can act on.
/// <para>
/// The reason is composed SERVER-SIDE and passed through verbatim. It carries live money figures
/// (plan position, per-draft amount, both fee totals) that only fee + plan resolution has in hand,
/// and it must read identically wherever it surfaces — so the client formats nothing and
/// re-derives nothing.
/// </para>
/// </summary>
public record RosterTransferBlockedDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Active status toggle.
/// </summary>
public record UpdatePlayerActiveRequest
{
    public required bool BActive { get; init; }
}

/// <summary>
/// One team in the coach's append-only association record (their <c>SpecialRequests</c> JSON),
/// tagged with its origin. Shown as a chip on the card so the director sees what the coach
/// asked for (<c>self</c>) versus what a director added (<c>admin</c>); whether it's granted
/// right now is told separately by <see cref="UnassignedAdultQueueRowDto.AssignedTeams"/>.
/// </summary>
public record UnassignedAdultRecordedTeamDto
{
    public required Guid TeamId { get; init; }
    public required string DisplayText { get; init; }
    /// <summary><c>"self"</c> = the coach requested it; <c>"admin"</c> = a director added/granted it.</summary>
    public required string Source { get; init; }
}

/// <summary>
/// A team the coach is CURRENTLY assigned to in this job (a live Staff row). Pre-checks the
/// approval-queue dropdown; carries the Staff registration id so an un-check deletes exactly
/// that row (FLOW 3) without a lookup.
/// </summary>
public record UnassignedAdultAssignedTeamDto
{
    public required Guid TeamId { get; init; }
    public required string DisplayText { get; init; }
    public required Guid StaffRegistrationId { get; init; }
}

/// <summary>
/// A prior Staff assignment for the coach (across any job/season) — the lead recognition
/// signal on the director card ("was Staff on {team} in {job}").
/// </summary>
public record PriorStaffAssignmentDto
{
    public required string TeamName { get; init; }
    public required string JobName { get; init; }
}

/// <summary>
/// A director-queue row: one UnassignedAdult coach with recognition context, their immutable
/// team record (requested ∪ granted, tagged) and their live grants. The queue lists every
/// active coach — nothing auto-retires; a coach leaves only when a director denies them.
/// </summary>
public record UnassignedAdultQueueRowDto
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerName { get; init; }
    public string? ClubName { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public required DateTime RegistrationTs { get; init; }
    public string? Note { get; init; }
    /// <summary>The coach's USA Lacrosse membership number (lacrosse jobs that collect it).</summary>
    public string? SportAssnId { get; init; }
    /// <summary>USLax membership expiry on record — refreshed by the queue's re-validate action.</summary>
    public DateTime? SportAssnIdexpDate { get; init; }
    /// <summary>True once the coach proved ownership of the membership via the on-file email code.
    /// False ⇒ "identity unverified" badge (registered before/without email verification).</summary>
    public required bool IdVerified { get; init; }
    /// <summary>Prior Staff assignments in OTHER jobs — the lead recognition signal.</summary>
    public required List<PriorStaffAssignmentDto> PriorStaff { get; init; }
    /// <summary>Names of the coach's own players registered in THIS job (minor secondary signal).</summary>
    public required List<string> LinkedPlayerNames { get; init; }
    /// <summary>The append-only team record (coach asks ∪ director grants), each tagged self/admin.
    /// Immutable history; cross-reference with <see cref="AssignedTeams"/> for current grant state.</summary>
    public required List<UnassignedAdultRecordedTeamDto> RecordedTeams { get; init; }
    /// <summary>Teams the coach is CURRENTLY assigned to (live Staff rows) — pre-checks the
    /// dropdown. Checking a new team places (FLOW 2); un-checking one of these removes it (FLOW 3).</summary>
    public required List<UnassignedAdultAssignedTeamDto> AssignedTeams { get; init; }
}

/// <summary>
/// Approve (grant) a single (coach, team) from the director queue — mints the Staff row and
/// appends the team to the coach's record as <c>admin</c>.
/// </summary>
public record ApproveTeamRequestDto
{
    public required Guid RegistrationId { get; init; }
    public required Guid TeamId { get; init; }
}

/// <summary>Internal read: the inputs the queue's USLax re-validate needs for one coach.</summary>
public record CoachUsLaxRefDto
{
    public string? UserId { get; init; }
    public string? SportAssnId { get; init; }
}

/// <summary>Re-validate a coach's USLax membership currency from the director queue.</summary>
public record RevalidateUsLaxRequestDto
{
    public required Guid RegistrationId { get; init; }
}

/// <summary>Result of a re-validate: current USA Lacrosse status + refreshed expiry. The stored
/// <c>SportAssnIdexpDate</c> (anchor + Staff rows) is updated to match when a definitive response
/// is returned. Identity verification is unaffected — this refreshes currency only.</summary>
public record RevalidateUsLaxResultDto
{
    public required bool Found { get; init; }
    public string? MemStatus { get; init; }
    public string? ExpDate { get; init; }
    public string? Message { get; init; }

    /// <summary>Verdict from <c>UsLaxEligibilityPolicy</c> — the SAME rule the registration form
    /// applies, run with the involvement this registration's role requires. Null when the caller
    /// refreshed currency without judging eligibility (the coach-approval-queue path), so a null
    /// here means "not assessed", never "passed".</summary>
    public bool? Eligible { get; init; }

    /// <summary>The policy's reason name, e.g. <c>ExpiresBeforeCutoff</c>. Null when not assessed.</summary>
    public string? EligibilityReason { get; init; }

    /// <summary>Admin-facing one-liner naming the specific discrepancy, with the real values in it.
    /// Null when eligible or not assessed.</summary>
    public string? EligibilityDetail { get; init; }

    /// <summary>Every criterion judged independently, for the checklist panel. Empty when the caller
    /// refreshed currency without assessing eligibility.</summary>
    public List<UsLaxCheckRowDto> Checks { get; init; } = new();
}

/// <summary>One row of the eligibility checklist — mirrors <c>UsLaxCheckRow</c>.</summary>
public record UsLaxCheckRowDto
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    /// <summary>true = passed, false = failed, null = not assessable. Never render null as a pass.</summary>
    public bool? Passed { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// Deny a coach outright from the director queue — deletes ALL their Staff rows and deactivates
/// the UnassignedAdult anchor (<c>bActive=0</c>). The immutable team record is left untouched.
/// </summary>
public record DenyCoachDto
{
    public required Guid RegistrationId { get; init; }
}
