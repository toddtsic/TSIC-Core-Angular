using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.LastMonthsJobStats;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class LastMonthsJobStatsRepository : ILastMonthsJobStatsRepository
{
    private readonly SqlDbContext _context;

    public LastMonthsJobStatsRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<LastMonthsJobStatRowDto>> GetLastMonthsAsync(
        CancellationToken cancellationToken = default)
    {
        var lastMonth = DateTime.Now.AddMonths(-1);
        var month = lastMonth.Month;
        var year = lastMonth.Year;

        return await (
            from mjs in _context.MonthlyJobStats
            join j in _context.Jobs on mjs.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            where mjs.Month == month && mjs.Year == year
            orderby c.CustomerName, j.JobName
            select new LastMonthsJobStatRowDto
            {
                Aid = mjs.Aid,
                CustomerName = c.CustomerName,
                JobName = j.JobName,
                CountActivePlayersToDate = mjs.CountActivePlayersToDate ?? 0,
                CountActivePlayersToDateLastMonth = mjs.CountActivePlayersToDateLastMonth ?? 0,
                CountNewPlayersThisMonth = mjs.CountNewPlayersThisMonth ?? 0,
                CountActiveTeamsToDate = mjs.CountActiveTeamsToDate ?? 0,
                CountActiveTeamsToDateLastMonth = mjs.CountActiveTeamsToDateLastMonth ?? 0,
                CountNewTeamsThisMonth = mjs.CountNewTeamsThisMonth ?? 0,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> UpdateCountsAsync(
        int aid,
        UpdateLastMonthsJobStatRequest request,
        string lebUserId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.MonthlyJobStats
            .SingleOrDefaultAsync(m => m.Aid == aid, cancellationToken);

        if (record == null) return false;

        record.CountActivePlayersToDate = request.CountActivePlayersToDate;
        record.CountActivePlayersToDateLastMonth = request.CountActivePlayersToDateLastMonth;
        record.CountNewPlayersThisMonth = request.CountNewPlayersThisMonth;
        record.CountActiveTeamsToDate = request.CountActiveTeamsToDate;
        record.CountActiveTeamsToDateLastMonth = request.CountActiveTeamsToDateLastMonth;
        record.CountNewTeamsThisMonth = request.CountNewTeamsThisMonth;
        record.LebUserId = lebUserId;
        record.Modified = DateTime.Now;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
