using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.CampGroups;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.UsLax;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Registrations entity data access.
/// Abstracts EF Core implementation details from domain/application services.
/// All role-type queries encapsulated here to keep data access logic in one place.
/// </summary>
public interface IRegistrationRepository
{
    /// <summary>
    /// Get registrations by user ID with specified filters and includes
    /// </summary>
    /// <param name="userId">The user ID to filter by</param>
    /// <param name="includeJob">Whether to include Job navigation property</param>
    /// <param name="includeJobDisplayOptions">Whether to include JobDisplayOptions navigation property</param>
    /// <param name="includeRole">Whether to include AspNetRole navigation property</param>
    /// <param name="roleIdFilter">Optional role ID to filter by</param>
    /// <param name="roleNameFilter">Optional role name to filter by</param>
    /// <param name="activeOnly">Whether to filter for active registrations only (BActive = true)</param>
    /// <param name="nonExpiredOnly">Whether to filter for non-expired jobs only (ExpiryAdmin > now)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching Registrations</returns>
    Task<List<Registrations>> GetByUserIdAsync(
        string userId,
        bool includeJob = false,
        bool includeJobDisplayOptions = false,
        bool includeRole = false,
        string? roleIdFilter = null,
        string? roleNameFilter = null,
        bool activeOnly = false,
        bool nonExpiredOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations for a family user within a specific job
    /// </summary>
    /// <param name="jobId">The job ID</param>
    /// <param name="familyUserId">The family user ID</param>
    /// <param name="activePlayersOnly">Whether to include only registrations with non-null UserId</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching Registrations</returns>
    Task<List<Registrations>> GetByFamilyAndJobAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations by specific registration IDs
    /// </summary>
    /// <param name="registrationIds">List of registration IDs to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of matching Registrations</returns>
    Task<List<Registrations>> GetByIdsAsync(
        List<Guid> registrationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-load each registration's own contact email (the registrant's User.Email) for a set of
    /// registration IDs in a single AsNoTracking projection. Used by the batch-email engine to resolve
    /// recipients from memory rather than one tracked Include query per recipient.
    /// </summary>
    Task<List<BatchRegistrantEmailDto>> GetRecipientEmailsByIdsAsync(
        IEnumerable<Guid> registrationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Superuser role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetSuperUserRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get SuperDirector role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetSuperDirectorRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Director role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetDirectorRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Player (Family) role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetPlayerRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the distinct CustomerIds of every Job the user has prior Family
    /// registrations with (active OR inactive — prior history is the signal).
    /// Used to find sibling Jobs to suggest on role-selection.
    /// </summary>
    Task<List<Guid>> GetCustomerIdsForFamilyUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the JobIds where the user already has an active Family registration.
    /// Used to exclude already-registered Jobs from suggestions.
    /// </summary>
    Task<List<Guid>> GetActiveFamilyJobIdsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the distinct CustomerIds of every Job the user has prior ClubRep
    /// registrations with (active OR inactive — prior history is the signal).
    /// ClubRep parallel of <see cref="GetCustomerIdsForFamilyUserAsync"/>.
    /// </summary>
    Task<List<Guid>> GetCustomerIdsForClubRepUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the JobIds where the user already has an active ClubRep registration.
    /// Used to exclude already-registered Jobs from suggestions.
    /// </summary>
    Task<List<Guid>> GetActiveClubRepJobIdsForUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Club Rep role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetClubRepRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Staff role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetStaffRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Store Admin role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetStoreAdminRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Ref Assignor role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetRefAssignorRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Referee role registrations for a user
    /// </summary>
    Task<List<RegistrationDto>> GetRefereeRegistrationsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new registration to the database
    /// </summary>
    /// <param name="registration">The registration to add</param>
    void Add(Registrations registration);

    /// <summary>
    /// Remove a registration from the database
    /// </summary>
    /// <param name="registration">The registration to remove</param>
    void Remove(Registrations registration);

    /// <summary>
    /// Get club rep registration for a user and job.
    /// Returns first active ClubRep registration matching userId, jobId, and ClubRep role.
    /// </summary>
    Task<Registrations?> GetClubRepRegistrationAsync(string userId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registration basic info (ClubName, JobId) by registration ID and user ID.
    /// Used for authorization checks.
    /// </summary>
    Task<RegistrationBasicInfo?> GetRegistrationBasicInfoAsync(Guid registrationId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registration by ID.
    /// </summary>
    Task<Registrations?> GetByIdAsync(Guid registrationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get tracked registration by ADN ARB subscription ID. Used by the daily sweep
    /// to resolve subscription transactions back to the registration that owns them.
    /// </summary>
    Task<Registrations?> GetByAdnSubscriptionIdAsync(string adnSubscriptionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a registration from an invoice's composite AIs (customer_job_registration —
    /// the AdnInvoiceNo format). All three must match. Read-only (AsNoTracking); used by the
    /// daily sweep to attribute a settled-but-unbooked ("orphan") ADN charge to a registrant
    /// for the digest report. Reporting only — the sweep never writes off this lookup.
    /// </summary>
    Task<Registrations?> GetByInvoiceAisAsync(int customerAi, int jobAi, int registrationAi, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations by job ID and user IDs.
    /// Used for family registration queries.
    /// </summary>
    Task<List<Registrations>> GetByJobAndUserIdsAsync(Guid jobId, List<string> userIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations by job ID and family user ID, optionally filtered by RegSaver policy.
    /// </summary>
    Task<List<Registrations>> GetByJobAndFamilyUserIdAsync(
        Guid jobId,
        string familyUserId,
        string? regsaverPolicyId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist changes to the database.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get roster counts for active registrations across teams within a job.
    /// </summary>
    Task<Dictionary<Guid, int>> GetActiveTeamRosterCountsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations for a family for specific players within a job.
    /// </summary>
    Task<List<Registrations>> GetFamilyRegistrationsForPlayersAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<string> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations for a family for specific players within a job (change-tracked for writes).
    /// </summary>
    Task<List<Registrations>> GetFamilyRegistrationsForPlayersTrackedAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<string> playerIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registrations by job and family with User navigation included.
    /// </summary>
    Task<List<Registrations>> GetByJobAndFamilyWithUsersAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only per-child summary for the family-accounting view: every player a parent
    /// registered for the job (keyed by JobId + FamilyUserId), with name and financial totals.
    /// The parent-side analog of GetRegisteredTeamsForClubRepAndJobAsync.
    /// </summary>
    Task<List<RegisteredPlayerInfo>> GetFamilyPlayersForAccountingAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registration with Job and Customer data for invoice building.
    /// </summary>
    Task<RegistrationWithInvoiceData?> GetRegistrationWithInvoiceDataAsync(
        Guid registrationId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the Authorize.Net charge description for a registration, matching the legacy
    /// format: "{JobName}:{First} {Last}:{Agegroup}:{Team}" when the player has an assigned
    /// team, otherwise "{RoleName}:{First} {Last}". Returns null if the registration is
    /// not found for the job.
    /// </summary>
    Task<string?> GetChargeDescriptionAsync(
        Guid registrationId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get job ID for a registration (lightweight lookup).
    /// </summary>
    Task<Guid?> GetRegistrationJobIdAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get roster counts by team ID for a collection of teams.
    /// </summary>
    Task<Dictionary<Guid, int>> GetRosterCountsByTeamAsync(
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically commit a player registration to a confirmed roster seat (BActive=true) IF the
    /// team has room — "hold a roster spot only if one is available." The gate counts CONFIRMED
    /// members only (BActive=1), excluding this registration; an unlimited team (MaxCount &lt;= 0)
    /// always has room. Guarantees confirmed members never exceed MaxCount; two in-flight holds on
    /// the last seat do not block each other (first to commit wins, second gets false → waitlist).
    /// Returns true when committed (or already active), false when full. Serializable + one
    /// deadlock retry; the tracked reg should be the only dirty entity when called.
    /// </summary>
    Task<bool> TryCommitSeatAsync(
        Registrations reg,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only sibling of <see cref="TryCommitSeatAsync"/> answering "is there a confirmed seat
    /// for this registration?" — used to PARTITION a payment cart BEFORE charging so seatable
    /// players are charged and seat-gone players are routed to the waitlist (never charged). Mirrors
    /// the guard's decision exactly: counts CONFIRMED members only (BActive=1), excluding self; an
    /// unlimited team (MaxCount &lt;= 0) and an already-active reg always return true. No write, no
    /// transaction — a point-in-time read; the authoritative overfill check stays in the commit path.
    /// <paramref name="reservedInBatch"/> = seats already handed out to EARLIER registrations in the
    /// same reconcile pass; added to the confirmed count so two siblings competing for the last seat
    /// in one submission don't both pass (neither is BActive=1 yet).
    /// </summary>
    Task<bool> IsSeatAvailableAsync(
        Registrations reg,
        int reservedInBatch = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get eligible registrations for insurance (positive fee, no existing policy, active team).
    /// </summary>
    Task<List<EligibleInsuranceRegistration>> GetEligibleInsuranceRegistrationsAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get Director contact info for insurance purposes.
    /// </summary>
    Task<DirectorContactInfo?> GetDirectorContactForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate and load registrations for insurance purchase.
    /// </summary>
    Task<List<Registrations>> ValidateRegistrationsForInsuranceAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all registrations for a family within a job (with UserId not null).
    /// </summary>
    Task<List<Registrations>> GetByJobAndFamilyUserIdAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registration confirmation data for a family within a job.
    /// </summary>
    Task<List<RegistrationConfirmationData>> GetConfirmationDataAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all registrations for a set of users across all jobs (for family defaults).
    /// </summary>
    Task<List<Registrations>> GetRegistrationsByUserIdsAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get latest RegSaver policy for a family within a job.
    /// </summary>
    Task<List<RegSaverPolicyInfo>> GetRegSaverPoliciesAsync(
        Guid jobId,
        string familyUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set notification sent flag for club rep registration.
    /// Used after sending confirmation email.
    /// </summary>
    Task SetNotificationSentAsync(Guid registrationId, bool sent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronize club rep's Registration financial fields with SUM of all active Teams.
    /// Updates FeeBase, FeeDiscount, FeeDiscountMp, FeeProcessing, FeeDonation, FeeLatefee,
    /// FeeTotal, OwedTotal, PaidTotal from active teams only (Active = 1).
    /// </summary>
    /// <param name="clubRepRegistrationId">Club rep's Registration ID</param>
    /// <param name="userId">User performing the sync (for audit trail)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SynchronizeClubRepFinancialsAsync(
        Guid clubRepRegistrationId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active registrations assigned to any of the given teams.
    /// Returns tracked entities for in-place fee recalculation.
    /// </summary>
    Task<List<Registrations>> GetActivePlayerRegistrationsByTeamIdsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> teamIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get every active player registration in a job. Returns tracked entities
    /// for in-place fee recalculation when the job-level phase flag flips.
    /// Filters by RoleId = Player; excludes registrations without an AssignedTeamId.
    /// </summary>
    Task<List<Registrations>> GetActivePlayerRegistrationsByJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Zero out all fee fields for registrations assigned to a team.
    /// Used when dropping a team to the "Dropped Teams" agegroup.
    /// Returns the number of registrations affected.
    /// </summary>
    Task<int> ZeroFeesForTeamAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get distinct club registrations for a job (club reps with RoleId = ClubRep).
    /// Returns RegistrationId + ClubName + UserId for each club rep.
    /// Used to populate the "target club" dropdown in LADT move-team.
    /// </summary>
    Task<List<ClubRegistrationInfo>> GetClubRegistrationsForJobAsync(Guid jobId, CancellationToken ct = default);

    // ── Registration Search methods ──

    /// <summary>
    /// Returns the CADT tree (Club → Agegroup → Division → Team) for a job.
    /// Built from active teams that have a ClubRepRegistrationId assigned.
    /// Club name is resolved from the club rep's registration ClubName.
    /// Returns empty list when no teams have club reps (no CADT data).
    /// </summary>
    Task<List<CadtClubNode>> GetCadtTreeForJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Server-side paged search with multi-criteria filtering and aggregates.
    /// Joins Registrations → AspNetUsers, Teams, Roles, Agegroups, Divisions.
    /// Computes Count + Aggregates (TotalFees, TotalPaid, TotalOwed) BEFORE paging.
    /// AsNoTracking.
    /// </summary>
    Task<RegistrationSearchResponse> SearchAsync(Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns distinct roles, teams, agegroups, divisions, club names for this job's registrations.
    /// Used to populate filter dropdowns. AsNoTracking.
    /// </summary>
    Task<RegistrationFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Full registration detail with user data, profile values, accounting records, and metadata schema.
    /// Validates registrationId belongs to jobId. AsNoTracking.
    /// </summary>
    Task<RegistrationDetailDto?> GetRegistrationDetailAsync(Guid registrationId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Update dynamic profile fields on a registration entity.
    /// Validates registration belongs to job. Maps key (dbColumn) to entity property via reflection.
    /// </summary>
    Task UpdateRegistrationProfileAsync(Guid jobId, string userId, UpdateRegistrationProfileRequest request, CancellationToken ct = default);

    /// <summary>
    /// Update family contact info for a registration's linked Families entity.
    /// </summary>
    Task UpdateFamilyContactAsync(Guid jobId, string userId, UpdateFamilyContactRequest request, CancellationToken ct = default);

    /// <summary>
    /// Update user demographics on the AspNetUsers entity linked to a registration.
    /// </summary>
    Task UpdateUserDemographicsAsync(Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Check whether a registration has any RegistrationAccounting records.
    /// </summary>
    Task<bool> HasAccountingRecordsAsync(Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// Check whether a registration has any StoreCartBatchSkus linked via DirectToRegId.
    /// </summary>
    Task<bool> HasStoreCartBatchRecordsAsync(Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// Get the role name for a registration (from AspNetRoles via RoleId).
    /// </summary>
    Task<string?> GetRegistrationRoleNameAsync(Guid registrationId, CancellationToken ct = default);

    /// <summary>
    /// Find a matching "Registration" agegroup team in the target job for a player registration.
    /// Matches on season, year, agegroup name ("Registration"), division name, and GradYear in team name.
    /// Returns the matching team ID or null if no match found.
    /// </summary>
    Task<Guid?> FindMatchingRegistrationTeamAsync(Guid registrationId, Guid newJobId, CancellationToken ct = default);

    // ── Roster Swapper methods ──

    /// <summary>
    /// Get roster for a specific team, joined with User + Role for display names.
    /// Returns all registrations (active + inactive). AsNoTracking.
    /// </summary>
    Task<List<SwapperPlayerDto>> GetRosterByTeamIdAsync(Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get a team's roster projected for the Player/Staff "View Rosters" screen.
    /// Includes Email + Cellphone (needed for the batch-email feature). AsNoTracking.
    /// </summary>
    Task<List<TSIC.Contracts.Dtos.MyRoster.MyRosterPlayerDto>> GetMyRosterByTeamIdAsync(Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Reads the two roster-visibility flags on a Job. Returns null if the job does not exist.
    /// </summary>
    Task<(bool AllowPlayer, bool AllowAdult)?> GetRosterViewFlagsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Returns the TeamName for a given team, scoped to a job. Null if not found.
    /// </summary>
    Task<string?> GetTeamNameAsync(Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get all Unassigned Adult registrations for a job.
    /// These are master coach/adult records in the unassigned pool. AsNoTracking.
    /// </summary>
    Task<List<SwapperPlayerDto>> GetUnassignedAdultsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Director approval queue: every ACTIVE UnassignedAdult coach with their recognition
    /// context (prior Staff in other jobs, family linkage), their append-only team record
    /// (asks ∪ grants, tagged self/admin) and their live grants. Nothing auto-retires; a
    /// coach leaves only via Deny. AsNoTracking.
    /// </summary>
    Task<List<UnassignedAdultQueueRowDto>> GetUnassignedAdultQueueAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Build Rule (one-time seed): for active UnassignedAdult coaches with ≥1 Staff assignment
    /// but no codified JSON yet, snapshot those grants into the record as <c>admin</c>. Idempotent.
    /// </summary>
    Task SeedAdultRequestRecordsAsync(Guid jobId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Append-on-grant: add <paramref name="teamId"/> to the coach's append-only record as
    /// <c>admin</c> (no-op if already recorded). Returns false if the registration isn't a valid
    /// UnassignedAdult for the job.
    /// </summary>
    Task<bool> AppendGrantedTeamToRecordAsync(
        Guid registrationId, Guid jobId, Guid teamId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Deny a coach outright: delete ALL their Staff rows (and device links) and deactivate the
    /// UnassignedAdult anchor (<c>bActive=0</c>). The immutable team record is left untouched.
    /// Returns false if the anchor isn't a valid UnassignedAdult for the job.
    /// </summary>
    Task<bool> DenyCoachAsync(Guid registrationId, Guid jobId, string adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Get tracked registrations for bulk transfer operations.
    /// Validates each belongs to sourcePoolId (team) or has Unassigned Adult role (if sourcePoolId = Guid.Empty).
    /// </summary>
    Task<List<Registrations>> GetRegistrationsForTransferAsync(List<Guid> registrationIds, Guid sourcePoolId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Check if a Staff registration already exists for this user on this team.
    /// Used to prevent duplicate staff assignments. AsNoTracking.
    /// </summary>
    Task<Registrations?> GetExistingStaffAssignmentAsync(string userId, Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Set the BemailOptOut flag on a registration. Used by both admin toggle and public unsubscribe endpoint.
    /// </summary>
    Task SetEmailOptOutAsync(Guid registrationId, bool optOut, CancellationToken ct = default);

    /// <summary>
    /// Set the BActive flag on a registration.
    /// </summary>
    Task SetActiveAsync(Guid registrationId, bool active, CancellationToken ct = default);

    /// <summary>
    /// Update the family account user's demographics (email, phone, address). Does NOT touch DOB/Gender.
    /// </summary>
    Task UpdateFamilyAccountDemographicsAsync(Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get player roster data for uniform number template export.
    /// Joins Registrations → Users → Teams, filtered to Player role for the given job. AsNoTracking.
    /// </summary>
    Task<List<UniformTemplateRow>> GetPlayerRosterForTemplateAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// USA Lacrosse reconciliation candidate set for a job: active Player registrations with a
    /// non-null SportAssnId and a user DOB on file (USALax API needs DOB for age-verification).
    /// Restricted to Lacrosse sport to match legacy.
    /// </summary>
    Task<List<UsLaxReconciliationCandidateRow>> GetUsLaxReconciliationCandidatesAsync(Guid jobId, UsLaxMembershipRole role, CancellationToken ct = default);

    /// <summary>
    /// Write a new SportAssnIdexpDate to a single registration. Used by USLax reconciliation
    /// when a MemberPing returns an exp_date and the member is involved as a Player.
    /// </summary>
    Task UpdateSportAssnIdExpDateAsync(Guid registrationId, DateTime newExpiryDate, CancellationToken ct = default);

    // ── Camp Day/Night Groups admin ──

    /// <summary>
    /// Get all active Player registrations for a team with the fields the camp-groups
    /// admin screen needs (identity, school, grad year, position, club, current Day/Night group).
    /// AsNoTracking. Ordered by GradYear, LastName, FirstName.
    /// </summary>
    Task<List<CampPlayerDto>> GetCampersByTeamAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Update DayGroup and/or NightGroup on a single registration, guarded by jobId
    /// so callers can't mutate registrations in a different job. Caller chooses which
    /// fields to write via the `updateDayGroup` / `updateNightGroup` flags so this can
    /// PATCH one field without clobbering the other. Empty string is stored as null.
    /// Returns true if the registration existed, belonged to the job, and was touched.
    /// </summary>
    Task<bool> UpdateCampGroupsAsync(
        Guid jobId,
        Guid registrationId,
        string? dayGroup,
        string? nightGroup,
        bool updateDayGroup,
        bool updateNightGroup,
        CancellationToken ct = default);

    /// <summary>
    /// Bulk variant of <see cref="UpdateCampGroupsAsync"/> — same field-update semantics
    /// applied across a list of registration ids, with the same jobId guard. Silently
    /// skips any ids not belonging to the job. Returns the count of rows touched.
    /// </summary>
    Task<int> BulkUpdateCampGroupsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> registrationIds,
        string? dayGroup,
        string? nightGroup,
        bool updateDayGroup,
        bool updateNightGroup,
        CancellationToken ct = default);
}

public record UsLaxReconciliationCandidateRow
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Email { get; init; }
    public DateTime? Dob { get; init; }
    public required string SportAssnId { get; init; }
    public DateTime? SportAssnIdexpDate { get; init; }
    public string? TeamName { get; init; }
}

public record RegistrationWithInvoiceData
{
    public required int CustomerAi { get; init; }
    public required int JobAi { get; init; }
    public required int RegistrationAi { get; init; }
}

public record EligibleInsuranceRegistration
{
    public required Guid RegistrationId { get; init; }
    public required Guid AssignedTeamId { get; init; }
    public string? Assignment { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public decimal? PerRegistrantFee { get; init; }
    public required decimal FeeTotal { get; init; }
    // Stamped per-registration fee modifiers, so the insurable amount can reflect them
    // (early bird + discount codes → FeeDiscount; late fees → FeeLatefee).
    public decimal FeeDiscount { get; init; }
    public decimal FeeLatefee { get; init; }
}

public record RegSaverPolicyInfo
{
    public required string PolicyId { get; init; }
    public DateTime? PolicyCreateDate { get; init; }
    public string? PlayerName { get; init; }
    public string? TeamName { get; init; }
}

public record DirectorContactInfo
{
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Cellphone { get; init; }
    public string? OrgName { get; init; }
    public string? StreetAddress { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }
    public required bool PaymentPlan { get; init; }
}

public record RegistrationConfirmationData
{
    public required Guid RegistrationId { get; init; }
    public required string PlayerFirst { get; init; }
    public required string PlayerLast { get; init; }
    public required string TeamName { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal PaidTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public string? RegsaverPolicyId { get; init; }
    public DateTime? RegsaverPolicyIdCreateDate { get; init; }
    public string? AdnSubscriptionId { get; init; }
    public string? AdnSubscriptionStatus { get; init; }
    public DateTime? AdnSubscriptionStartDate { get; init; }
    public int? AdnSubscriptionIntervalLength { get; init; }
    public int? AdnSubscriptionBillingOccurences { get; init; }
    public decimal? AdnSubscriptionAmountPerOccurence { get; init; }
}

public record RegistrationBasicInfo
{
    public string? ClubName { get; init; }
    public required Guid JobId { get; init; }
    public bool BWaiverSigned3 { get; init; }
}

public record ClubRepFinancialTotals
{
    public required decimal FeeBase { get; init; }
    public required decimal FeeDiscount { get; init; }
    public required decimal FeeDiscountMp { get; init; }
    public required decimal FeeProcessing { get; init; }
    public required decimal FeeDonation { get; init; }
    public required decimal FeeLatefee { get; init; }
    public required decimal FeeTotal { get; init; }
    public required decimal OwedTotal { get; init; }
    public required decimal PaidTotal { get; init; }
}

public record ClubRegistrationInfo
{
    public required Guid RegistrationId { get; init; }
    public required string ClubName { get; init; }
    public required string UserId { get; init; }
}

public record UniformTemplateRow
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string TeamName { get; init; }
    public string? UniformNo { get; init; }
    public string? DayGroup { get; init; }
}
