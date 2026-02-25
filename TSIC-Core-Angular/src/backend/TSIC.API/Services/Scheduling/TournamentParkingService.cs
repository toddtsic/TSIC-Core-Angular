using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Scheduling;

/// <summary>
/// Computes tournament parking/teams-on-site report.
/// Ports logic from legacy stored procedure [utility].[TournamentTeamsOnSiteReporting].
/// Bug fix: running totals are computed per (complex, day) — never crossing boundaries.
/// </summary>
public sealed class TournamentParkingService : ITournamentParkingService
{
    private readonly ITournamentParkingRepository _repo;

    public TournamentParkingService(ITournamentParkingRepository repo)
    {
        _repo = repo;
    }

    public async Task<TournamentParkingResponse> GetParkingReportAsync(
        Guid jobId,
        TournamentParkingRequest request,
        CancellationToken ct = default)
    {
        // 1. Fetch raw data from repository (sequential — DbContext is not thread-safe)
        var presence = await _repo.GetTeamGamePresenceAsync(jobId, ct);
        var intervals = await _repo.GetGameStartIntervalsAsync(jobId, ct);

        if (presence.Count == 0)
        {
            return new TournamentParkingResponse
            {
                Rollup = [],
                ComplexDays = [],
                Summary = new ParkingSummaryDto
                {
                    TotalComplexes = 0,
                    TotalDays = 0,
                    PeakTeamsOnSite = 0,
                    PeakTeamsComplex = "",
                    PeakCarsOnSite = 0,
                    PeakCarsComplex = ""
                }
            };
        }

        // 2. Group by (FieldComplex, TeamId, Day) to find each team's arrival/departure window
        var teamWindows = presence
            .GroupBy(p => (p.FieldComplex, p.TeamId, Day: p.GameDate.Date))
            .Select(g =>
            {
                var firstGame = g.Min(p => p.GameDate);
                var lastGame = g.Max(p => p.GameDate);
                var agegroupId = g.First().AgegroupId;

                // Look up game duration; fallback to 60 minutes
                var gameInterval = intervals.TryGetValue(agegroupId, out var interval)
                    ? interval
                    : 60;

                return new
                {
                    g.Key.FieldComplex,
                    g.Key.Day,
                    ArrivalTime = firstGame.AddMinutes(-request.ArrivalBufferMinutes),
                    DepartureTime = lastGame.AddMinutes(gameInterval + request.DepartureBufferMinutes)
                };
            })
            .ToList();

        // 3. Emit arrival (+) and departure (-) events
        var events = new List<(string Complex, DateTime Day, DateTime Time, int Teams, int Cars, bool IsArrival)>();

        // Group arrivals by (Complex, Time) to count distinct teams
        var arrivalGroups = teamWindows
            .GroupBy(w => (w.FieldComplex, w.Day, w.ArrivalTime))
            .Select(g => (
                Complex: g.Key.FieldComplex,
                Day: g.Key.Day,
                Time: g.Key.ArrivalTime,
                Teams: g.Count(),
                Cars: g.Count() * request.CarMultiplier,
                IsArrival: true
            ));

        var departureGroups = teamWindows
            .GroupBy(w => (w.FieldComplex, w.Day, w.DepartureTime))
            .Select(g => (
                Complex: g.Key.FieldComplex,
                Day: g.Key.Day,
                Time: g.Key.DepartureTime,
                Teams: g.Count(),
                Cars: g.Count() * request.CarMultiplier,
                IsArrival: false
            ));

        events.AddRange(arrivalGroups);
        events.AddRange(departureGroups);

        // 4. Merge events at the same (Complex, Day, Time), computing net arrivals/departures
        var mergedSlots = events
            .GroupBy(e => (e.Complex, e.Day, e.Time))
            .Select(g =>
            {
                int? teamsArriving = null;
                int? teamsDeparting = null;
                int? carsArriving = null;
                int? carsDeparting = null;

                foreach (var e in g)
                {
                    if (e.IsArrival)
                    {
                        teamsArriving = (teamsArriving ?? 0) + e.Teams;
                        carsArriving = (carsArriving ?? 0) + e.Cars;
                    }
                    else
                    {
                        teamsDeparting = (teamsDeparting ?? 0) + e.Teams;
                        carsDeparting = (carsDeparting ?? 0) + e.Cars;
                    }
                }

                var teamsNet = (teamsArriving ?? 0) - (teamsDeparting ?? 0);
                var carsNet = (carsArriving ?? 0) - (carsDeparting ?? 0);

                return new
                {
                    g.Key.Complex,
                    g.Key.Day,
                    g.Key.Time,
                    TeamsArriving = teamsArriving,
                    TeamsDeparting = teamsDeparting,
                    TeamsNet = teamsNet,
                    CarsArriving = carsArriving,
                    CarsDeparting = carsDeparting,
                    CarsNet = carsNet
                };
            })
            .OrderBy(s => s.Complex)
            .ThenBy(s => s.Day)
            .ThenBy(s => s.Time)
            .ToList();

        // 5. Compute running totals PER (Complex, Day) — the key bug fix vs legacy
        var rollup = new List<ParkingTimeslotDto>();
        string? prevComplex = null;
        DateTime? prevDay = null;
        int runningTeams = 0;
        int runningCars = 0;

        foreach (var slot in mergedSlots)
        {
            // Reset running totals at complex/day boundary
            if (slot.Complex != prevComplex || slot.Day != prevDay)
            {
                runningTeams = 0;
                runningCars = 0;
                prevComplex = slot.Complex;
                prevDay = slot.Day;
            }

            runningTeams += slot.TeamsNet;
            runningCars += slot.CarsNet;

            rollup.Add(new ParkingTimeslotDto
            {
                FieldComplex = slot.Complex,
                Day = slot.Day,
                Time = slot.Time,
                TeamsArriving = slot.TeamsArriving,
                TeamsDeparting = slot.TeamsDeparting,
                TeamsNet = slot.TeamsNet,
                TeamsOnSite = runningTeams,
                CarsArriving = slot.CarsArriving,
                CarsDeparting = slot.CarsDeparting,
                CarsNet = slot.CarsNet,
                CarsOnSite = runningCars
            });
        }

        // 6. Build per-complex/day slices
        var complexDays = rollup
            .GroupBy(r => (r.FieldComplex, r.Day))
            .OrderBy(g => g.Key.FieldComplex)
            .ThenBy(g => g.Key.Day)
            .Select(g => new ParkingComplexDayDto
            {
                FieldComplex = g.Key.FieldComplex,
                Day = g.Key.Day,
                Label = $"{g.Key.FieldComplex} — {g.Key.Day:ddd M/d}",
                Timeslots = g.OrderBy(t => t.Time).ToList()
            })
            .ToList();

        // 7. Summary KPIs
        var peakTeamsSlot = rollup.Count > 0
            ? rollup.MaxBy(r => r.TeamsOnSite)
            : null;
        var peakCarsSlot = rollup.Count > 0
            ? rollup.MaxBy(r => r.CarsOnSite)
            : null;

        var summary = new ParkingSummaryDto
        {
            TotalComplexes = rollup.Select(r => r.FieldComplex).Distinct().Count(),
            TotalDays = rollup.Select(r => r.Day).Distinct().Count(),
            PeakTeamsOnSite = peakTeamsSlot?.TeamsOnSite ?? 0,
            PeakTeamsComplex = peakTeamsSlot?.FieldComplex ?? "",
            PeakCarsOnSite = peakCarsSlot?.CarsOnSite ?? 0,
            PeakCarsComplex = peakCarsSlot?.FieldComplex ?? ""
        };

        return new TournamentParkingResponse
        {
            Rollup = rollup,
            ComplexDays = complexDays,
            Summary = summary
        };
    }
}
