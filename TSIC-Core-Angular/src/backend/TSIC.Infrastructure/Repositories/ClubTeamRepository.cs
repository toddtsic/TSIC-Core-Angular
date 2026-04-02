using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for ClubTeams entity using Entity Framework Core.
/// </summary>
public class ClubTeamRepository : IClubTeamRepository
{
    private readonly SqlDbContext _context;

    public ClubTeamRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ClubTeams>> GetByClubIdAsync(
        int clubId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<ClubTeams?> GetByIdAsync(
        int clubTeamId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ClubTeams
            .Where(ct => ct.ClubTeamId == clubTeamId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(ClubTeams clubTeam)
    {
        _context.ClubTeams.Add(clubTeam);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ClubTeamLibraryResponse> GetLibraryWithHistoryAsync(
        int clubId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Get club teams + club name
        var clubTeams = await _context.ClubTeams
            .Where(ct => ct.ClubId == clubId)
            .Include(ct => ct.Club)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var clubName = clubTeams.FirstOrDefault()?.Club.ClubName ?? "";
        var clubTeamIds = clubTeams.Select(ct => ct.ClubTeamId).ToList();

        // Step 2: Get event history for all club teams — project into DTO to avoid pulling 150+ columns
        var eventHistory = await _context.Teams
            .Where(t => t.ClubTeamId != null && clubTeamIds.Contains(t.ClubTeamId.Value))
            .Select(t => new
            {
                ClubTeamId = t.ClubTeamId!.Value,
                t.TeamId,
                t.JobId,
                JobName = t.Job.JobName ?? t.Job.DisplayName ?? "",
                t.Job.JobPath,
                AgegroupName = t.Agegroup.AgegroupName ?? "",
                DivisionName = t.Div != null ? t.Div.DivName : null,
                t.Job.EventStartDate,
                t.Wins,
                t.Losses,
                t.Ties,
                t.GoalsFor,
                t.GoalsVs,
                GamesPlayed = t.Games,
                t.StandingsRank
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var historyByClubTeam = eventHistory
            .GroupBy(h => h.ClubTeamId)
            .ToDictionary(g => g.Key, g => g
                .OrderByDescending(e => e.EventStartDate)
                .Select(e => new ClubTeamEventSummaryDto
                {
                    TeamId = e.TeamId,
                    JobId = e.JobId,
                    JobName = e.JobName,
                    JobPath = e.JobPath,
                    AgegroupName = e.AgegroupName,
                    DivisionName = e.DivisionName,
                    EventStartDate = e.EventStartDate,
                    Wins = e.Wins,
                    Losses = e.Losses,
                    Ties = e.Ties,
                    GoalsFor = e.GoalsFor,
                    GoalsVs = e.GoalsVs,
                    GamesPlayed = e.GamesPlayed,
                    StandingsRank = e.StandingsRank
                })
                .ToList());

        // Step 3: Assemble response
        var teams = clubTeams
            .OrderBy(ct => ct.ClubTeamGradYear)
            .ThenBy(ct => ct.ClubTeamName)
            .Select(ct => new ClubTeamLibraryEntryDto
            {
                ClubTeamId = ct.ClubTeamId,
                ClubTeamName = ct.ClubTeamName,
                ClubTeamGradYear = ct.ClubTeamGradYear,
                ClubTeamLevelOfPlay = ct.ClubTeamLevelOfPlay,
                Active = ct.Active,
                EventHistory = historyByClubTeam.TryGetValue(ct.ClubTeamId, out var history)
                    ? history
                    : []
            })
            .ToList();

        return new ClubTeamLibraryResponse
        {
            ClubId = clubId,
            ClubName = clubName,
            Teams = teams
        };
    }
}
