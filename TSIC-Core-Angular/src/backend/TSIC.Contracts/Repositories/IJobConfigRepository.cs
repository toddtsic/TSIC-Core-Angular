using TSIC.Contracts.Dtos.JobConfig;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

/// <summary>
/// Dedicated repository for Job Configuration Editor data access.
/// </summary>
public interface IJobConfigRepository
{
    // ── Read ─────────────────────────────────────────────
    Task<Jobs?> GetJobByIdAsync(Guid jobId, CancellationToken ct = default);
    Task<GameClockParams?> GetGameClockParamsAsync(Guid jobId, CancellationToken ct = default);
    Task<List<JobAdminCharges>> GetAdminChargesAsync(Guid jobId, CancellationToken ct = default);

    // ── Reference data ───────────────────────────────────
    Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default);
    Task<List<SportRefDto>> GetSportsAsync(CancellationToken ct = default);
    Task<List<CustomerRefDto>> GetCustomersAsync(CancellationToken ct = default);
    Task<List<BillingTypeRefDto>> GetBillingTypesAsync(CancellationToken ct = default);
    Task<List<ChargeTypeRefDto>> GetChargeTypesAsync(CancellationToken ct = default);

    // ── Write ────────────────────────────────────────────
    Task<Jobs?> GetJobTrackedAsync(Guid jobId, CancellationToken ct = default);
    Task<GameClockParams?> GetGameClockParamsTrackedAsync(Guid jobId, CancellationToken ct = default);
    void AddGameClockParams(GameClockParams gcp);
    void AddAdminCharge(JobAdminCharges charge);
    void RemoveAdminCharge(JobAdminCharges charge);
    Task<JobAdminCharges?> GetAdminChargeByIdAsync(int chargeId, Guid jobId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
