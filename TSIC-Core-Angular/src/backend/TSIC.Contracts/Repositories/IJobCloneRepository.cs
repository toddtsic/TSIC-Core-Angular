using TSIC.Contracts.Dtos.JobClone;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IJobCloneRepository
{
    // ── Source data loading (AsNoTracking) ──
    Task<Jobs?> GetSourceJobAsync(Guid jobId, CancellationToken ct = default);
    Task<JobDisplayOptions?> GetSourceDisplayOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<JobOwlImages?> GetSourceOwlImagesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Bulletins>> GetSourceBulletinsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobAgeRanges>> GetSourceAgeRangesAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobMenus>> GetSourceMenusWithItemsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Registrations>> GetSourceAdminRegistrationsAsync(Guid jobId, CancellationToken ct = default);
    Task<Leagues?> GetSourceLeagueAsync(Guid jobId, CancellationToken ct = default);
    Task<List<Agegroups>> GetSourceAgegroupsAsync(Guid leagueId, string? season, CancellationToken ct = default);
    Task<List<Divisions>> GetSourceDivisionsAsync(List<Guid> agegroupIds, CancellationToken ct = default);

    // ── Validation ──
    Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default);

    // ── Source picker list ──
    Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default);

    // ── Write operations (queue in change tracker) ──
    void AddJob(Jobs job);
    void AddDisplayOptions(JobDisplayOptions options);
    void AddOwlImages(JobOwlImages images);
    void AddBulletins(IEnumerable<Bulletins> bulletins);
    void AddAgeRanges(IEnumerable<JobAgeRanges> ranges);
    void AddMenu(JobMenus menu);
    void AddMenuItems(IEnumerable<JobMenuItems> items);
    void AddRegistrations(IEnumerable<Registrations> registrations);
    void AddLeague(Leagues league);
    void AddJobLeague(JobLeagues jobLeague);
    void AddAgegroups(IEnumerable<Agegroups> agegroups);
    void AddDivisions(IEnumerable<Divisions> divisions);

    // ── Transaction + commit ──
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
