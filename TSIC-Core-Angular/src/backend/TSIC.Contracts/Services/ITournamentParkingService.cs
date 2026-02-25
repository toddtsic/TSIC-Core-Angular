using TSIC.Contracts.Dtos.Scheduling;

namespace TSIC.Contracts.Services;

public interface ITournamentParkingService
{
    Task<TournamentParkingResponse> GetParkingReportAsync(
        Guid jobId,
        TournamentParkingRequest request,
        CancellationToken ct = default);
}
