using TSIC.Contracts.Dtos;
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
    /// Get a queryable for advanced LINQ queries.
    /// Use sparingly - prefer adding specific methods to the repository instead.
    /// </summary>
    /// <returns>IQueryable for Registrations</returns>
    IQueryable<Registrations> Query();

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
}

public record RegistrationWithInvoiceData(
    int CustomerAi,
    int JobAi,
    int RegistrationAi);

public record EligibleInsuranceRegistration(
    Guid RegistrationId,
    Guid AssignedTeamId,
    string? Assignment,
    string? FirstName,
    string? LastName,
    decimal? PerRegistrantFee,
    decimal? TeamFee,
    decimal FeeTotal);

public record DirectorContactInfo(
    string? Email,
    string? FirstName,
    string? LastName,
    string? Cellphone,
    string? OrgName,
    bool PaymentPlan);

