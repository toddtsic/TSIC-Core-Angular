using TSIC.Contracts.Dtos.Ladt;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Repository for managing Leagues entity data access (LADT admin).
/// </summary>
public interface ILeagueRepository
{
    /// <summary>
    /// Get all leagues for a job as projected DTOs (AsNoTracking). Flattens Sport.SportName.
    /// </summary>
    Task<List<LeagueDetailDto>> GetLeaguesByJobIdAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single league by ID (tracked for updates).
    /// </summary>
    Task<Leagues?> GetByIdAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a single league by ID as projected DTO (AsNoTracking). Flattens Sport.SportName.
    /// </summary>
    Task<LeagueDetailDto?> GetByIdWithSportAsync(Guid leagueId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get JobLeagues entries for a job.
    /// </summary>
    Task<List<JobLeagues>> GetJobLeaguesAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a league belongs to a job.
    /// </summary>
    Task<bool> BelongsToJobAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all sports for dropdown selection.
    /// </summary>
    Task<List<Sports>> GetAllSportsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all standings sort profiles with their ordered rule chains, for dropdown selection.
    /// </summary>
    Task<List<StandingsSortProfileOptionDto>> GetStandingsSortProfilesAsync(CancellationToken cancellationToken = default);

    void Add(Leagues league);
    void Remove(Leagues league);

    /// <summary>Adds a Jobs.JobLeagues row (the league → job link).</summary>
    void AddJobLeague(JobLeagues jobLeague);

    /// <summary>
    /// Opens an explicit transaction on the shared context so a multi-step build can roll
    /// back as a unit. Needed because the LADT stub helpers each SaveChanges internally —
    /// without this, a first-league create commits in pieces and a mid-way failure strands
    /// a half-built league whose empty-state form is already gone.
    /// </summary>
    Task<IAsyncDisposable> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Begins a transaction ONLY if none is already open on this context, and returns null when
    /// one is. A null result means the caller is running inside someone else's transaction and
    /// must neither commit nor dispose it — its work simply joins theirs and lands when they
    /// commit. Lets a service guarantee its own atomicity standalone without breaking when a
    /// larger operation (a pool transfer, with its fee and club-rep writes) wraps it.
    /// </summary>
    Task<IAsyncDisposable?> BeginTransactionIfNoneAsync(CancellationToken cancellationToken = default);

    /// <summary>Commits the transaction opened by <see cref="BeginTransactionAsync"/>.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
