using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

public interface IMasterScheduleService
{
    Task<MasterScheduleResponse> GetMasterScheduleAsync(
        Guid jobId, bool includeReferees, CancellationToken ct = default);

    Task<byte[]> ExportExcelAsync(
        Guid jobId, bool includeReferees, int? dayIndex, CancellationToken ct = default);

    /// <summary>Collegiate Coach Master Schedule — print-ready PDF, one 18"×12" page per day.</summary>
    Task<byte[]> ExportCoachPdfAsync(Guid jobId, CancellationToken ct = default);
}
