namespace TSIC.Contracts.Dtos.RegistrationSearch;

/// <summary>
/// Full registration detail for the slide-over panel.
/// Includes profile values, metadata schema, and accounting records.
/// </summary>
public record RegistrationDetailDto
{
    // Identity
    public required Guid RegistrationId { get; init; }
    public required int RegistrationAi { get; init; }

    // Person (from AspNetUsers)
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Email { get; init; }
    public string? Phone { get; init; }

    // Context
    public required string RoleName { get; init; }
    public required bool Active { get; init; }
    public string? TeamName { get; init; }
    /// <summary>Computed: "ClubRepClub AgegroupName TeamName" from live join chain.</summary>
    public string? Assignment { get; init; }

    // Financials (summary)
    public required decimal FeeBase { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }

    // Dynamic profile fields (key = metadata field name, value = current value as string)
    public required Dictionary<string, string?> ProfileValues { get; init; }

    // Metadata schema for form rendering. Role-resolved: player roles get the flat
    // Job.PlayerProfileMetadataJson; adult roles (coach/Staff/Referee/Recruiter) get the flat
    // sub-slice of Job.AdultProfileMetadataJson for their role; roles with no template get null.
    public string? ProfileMetadataJson { get; init; }

    // Coach/Staff only: the decoded human NOTE from the codified SpecialRequests team-request blob
    // (AdultTeamRequestData). Never the raw JSON — surfaced read-only in the panel. Null otherwise.
    public string? CoachRequestNote { get; init; }

    // Coach/Staff only: the coach's REQUESTED teams, each resolved to its current
    // "Club: Age Group: Team" label (rename-proof) from the codified SpecialRequests blob.
    // Read-only, pending director approval — NOT roster assignments. Empty/null otherwise.
    public IReadOnlyList<string>? CoachRequestedTeams { get; init; }

    // Account username: for players = family account username, for non-players = registrant username
    public string? AccountUsername { get; init; }

    // Family account id (AspNetUsers) linking sibling player registrations. Non-null for
    // players registered under a family account — drives the family-accounting view in the
    // detail panel. Null for adult/self registrations.
    public string? FamilyUserId { get; init; }

    // Job sport (for sport-aware label rendering, e.g. "USA Lax Number" for Lacrosse)
    public string? SportName { get; init; }

    // Job option sets (for resolving dataSource-driven select field options)
    public string? JsonOptions { get; init; }

    // Parent/guardian labels (from Jobs entity, defaults "Mom"/"Dad")
    public string MomLabel { get; init; } = "Mom";
    public string DadLabel { get; init; } = "Dad";

    // Family contact info (from Families entity, null if no family link)
    public FamilyContactDto? FamilyContact { get; init; }

    // User demographics (from AspNetUsers — the registrant/player)
    public UserDemographicsDto? UserDemographics { get; init; }

    // Family account demographics (from AspNetUsers via FamilyUserId — email, phone, address)
    public UserDemographicsDto? FamilyAccountDemographics { get; init; }

    // Registration timestamps
    public DateTime? RegistrationDate { get; init; }
    public DateTime? ModifiedDate { get; init; }

    // ARB subscription (true when registration has an AdnSubscriptionId)
    public bool HasSubscription { get; init; }

    // Stored ARB snapshot, projected from the Registrations.AdnSubscription* columns. Unlike the
    // live GetSubscription endpoint (which queries the production Authorize.Net gateway and cannot
    // resolve a prod subscription id from a sandbox environment), this is available in EVERY
    // environment. Kept fresh in Production by the ADN sweep; may lag live status off-Production.
    public SubscriptionDetailDto? StoredSubscription { get; init; }

    // Accounting records
    public required List<AccountingRecordDto> AccountingRecords { get; init; }

    // Club rep detection (true when this registration has ACTIVE teams assigned to it — drives payment panel)
    public bool IsClubRep { get; init; }

    // Count of ALL teams (active or inactive) referencing this registration via ClubrepRegistrationid.
    // This is the foreign-key scope: a Club Rep registration can only be deleted when this is zero.
    // Distinct from IsClubRep, which is active-only and used for display.
    public int ClubRepTeamCount { get; init; }

    // The Club this rep registration was made under, resolved from the rep link + the stored ClubName
    // copy (Clubs is the canonical name). Populated only for a Club Rep role registration whose copy
    // still resolves to a live club; null otherwise (drift). Drives the club-rep card + admin rename.
    public int? ClubId { get; init; }
    public string? ClubName { get; init; }
}

/// <summary>
/// Family contact information from the Families entity.
/// </summary>
public record FamilyContactDto
{
    public string? MomFirstName { get; init; }
    public string? MomLastName { get; init; }
    public string? MomCellphone { get; init; }
    public string? MomEmail { get; init; }
    public string? DadFirstName { get; init; }
    public string? DadLastName { get; init; }
    public string? DadCellphone { get; init; }
    public string? DadEmail { get; init; }
}

/// <summary>
/// User demographics from AspNetUsers.
/// </summary>
public record UserDemographicsDto
{
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public string? Gender { get; init; }
    public DateTime? DateOfBirth { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
}

/// <summary>
/// Update family contact info for a registration's linked family.
/// </summary>
public record UpdateFamilyContactRequest
{
    public required Guid RegistrationId { get; init; }
    public required FamilyContactDto FamilyContact { get; init; }
}

/// <summary>
/// Update user demographics for a registration's linked user.
/// </summary>
public record UpdateUserDemographicsRequest
{
    public required Guid RegistrationId { get; init; }
    public required UserDemographicsDto Demographics { get; init; }
}

/// <summary>
/// Update registration profile request.
/// Key = dbColumn name, Value = new value as string.
/// </summary>
public record UpdateRegistrationProfileRequest
{
    public required Guid RegistrationId { get; init; }
    public required Dictionary<string, string?> ProfileValues { get; init; }
}

/// <summary>
/// Batch email request — sends to multiple registrations with token substitution.
/// </summary>
public record BatchEmailRequest
{
    /// <summary>
    /// Explicit recipient set. When non-empty it is used SOLELY (any <see cref="Criteria"/> is
    /// ignored). When empty, recipients are resolved server-side by re-running <see cref="Criteria"/>
    /// — this is how "Email All" targets the whole result set without the client enumerating ids.
    /// </summary>
    public required List<Guid> RegistrationIds { get; init; }

    /// <summary>
    /// Search criteria used to resolve recipients when <see cref="RegistrationIds"/> is empty.
    /// The same filter the grid ran; resolved unpaged at send time ("who matches now").
    /// </summary>
    public RegistrationSearchRequest? Criteria { get; init; }

    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
    /// <summary>
    /// When !INVITE_LINK / !CLUBREP_INVITE_LINK is used, this is the target job the invite links point to.
    /// </summary>
    public Guid? InviteLinkTargetJobId { get; init; }

    /// <summary>
    /// Lifetime (hours) of the per-recipient signed invite token, from send time. Drives both the
    /// token's expiry and the !INVITE_EXPIRES text. Defaults to 24 when unset. Only meaningful with
    /// <see cref="InviteLinkTargetJobId"/>.
    /// </summary>
    public int? InviteExpiryHours { get; init; }

    /// <summary>
    /// DEV/SANDBOX ONLY. When set, the engine simulates each send with this delay instead of
    /// transmitting via SES — drives the "TEST BATCH PROCESSING" preview. Ignored outside sandbox.
    /// </summary>
    public int? SimulatedPerUnitDelayMs { get; init; }

    /// <summary>
    /// SANDBOX ONLY. Test inbox: when set (and the host is sandboxed), the engine forces a REAL SES
    /// send from the otherwise-suppressed sandbox and delivers every message to this single address
    /// instead of its real recipients, so an invite's token link can be received and clicked.
    /// Set by the Staging-only "To (test inbox)" field on the invite modal. Ignored in Production.
    /// </summary>
    public string? SandboxTestRecipient { get; init; }
}

/// <summary>
/// Request to set or clear the email opt-out flag on a registration.
/// </summary>
public record SetEmailOptOutRequest
{
    public required bool OptOut { get; init; }
}

/// <summary>
/// Request to set the active (bActive) flag on a registration.
/// </summary>
public record SetActiveRequest
{
    public required bool Active { get; init; }
}

/// <summary>
/// Email preview request — renders tokens for N recipients without sending.
/// </summary>
public record EmailPreviewRequest
{
    public required List<Guid> RegistrationIds { get; init; }
    /// <summary>Same role as <see cref="BatchEmailRequest.Criteria"/> — resolves a representative
    /// recipient for the token preview when no explicit ids are supplied (Email-All mode).</summary>
    public RegistrationSearchRequest? Criteria { get; init; }
    public required string Subject { get; init; }
    public required string BodyTemplate { get; init; }
}

/// <summary>
/// Email preview response with rendered previews.
/// </summary>
public record EmailPreviewResponse
{
    public required List<RenderedEmailPreview> Previews { get; init; }
}

public record RenderedEmailPreview
{
    public required string RecipientName { get; init; }
    public required string RecipientEmail { get; init; }
    public required string RenderedSubject { get; init; }
    public required string RenderedBody { get; init; }
}

/// <summary>
/// Request to change a registration's job.
/// </summary>
public record ChangeJobRequest
{
    public required Guid NewJobId { get; init; }
}

/// <summary>
/// Response from a change-job operation.
/// </summary>
public record ChangeJobResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
    public string? NewJobName { get; init; }
}

/// <summary>
/// Lightweight job info for the "Change Job" dropdown.
/// </summary>
public record JobOptionDto
{
    public required Guid JobId { get; init; }
    public required string JobName { get; init; }
}

/// <summary>
/// Which registration type an invite targets. Selects the "accepts this registration type"
/// filter (<c>BRegistrationAllowPlayer</c> vs <c>BRegistrationAllowTeam</c>) when building the
/// invite-link target-event dropdown. Not gated on the requires-token flag — an invite is valid
/// into an open-enrollment event too.
/// </summary>
public enum InviteRegistrationKind
{
    Player,
    Team
}

/// <summary>
/// Response from a delete-registration operation.
/// </summary>
public record DeleteRegistrationResponse
{
    public required bool Success { get; init; }
    public string? Message { get; init; }
}
