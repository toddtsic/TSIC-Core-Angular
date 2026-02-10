using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.RosterSwapper;
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
    /// Update an existing registration
    /// </summary>
    /// <param name="registration">The registration to update</param>
    void Update(Registrations registration);

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
    /// Get registrations by job and family with User navigation included.
    /// </summary>
    Task<List<Registrations>> GetByJobAndFamilyWithUsersAsync(
        Guid jobId,
        string familyUserId,
        bool activePlayersOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get registration with Job and Customer data for invoice building.
    /// </summary>
    Task<RegistrationWithInvoiceData?> GetRegistrationWithInvoiceDataAsync(
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
    Task<RegSaverPolicyInfo?> GetLatestRegSaverPolicyAsync(
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

    // ── Roster Swapper methods ──

    /// <summary>
    /// Get roster for a specific team, joined with User + Role for display names.
    /// Returns all registrations (active + inactive). AsNoTracking.
    /// </summary>
    Task<List<SwapperPlayerDto>> GetRosterByTeamIdAsync(Guid teamId, Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Get all Unassigned Adult registrations for a job.
    /// These are master coach/adult records in the unassigned pool. AsNoTracking.
    /// </summary>
    Task<List<SwapperPlayerDto>> GetUnassignedAdultsAsync(Guid jobId, CancellationToken ct = default);

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
    public decimal? TeamFee { get; init; }
    public required decimal FeeTotal { get; init; }
}

public record RegSaverPolicyInfo
{
    public required string PolicyId { get; init; }
    public DateTime? PolicyCreateDate { get; init; }
}

public record DirectorContactInfo
{
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Cellphone { get; init; }
    public string? OrgName { get; init; }
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
