using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.CustomerJobRevenue;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class CustomerJobRevenueRepository : ICustomerJobRevenueRepository
{
    private readonly SqlDbContext _context;

    public CustomerJobRevenueRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<JobRevenueDataDto> GetRevenueDataAsync(
        Guid jobId, DateTime startDate, DateTime endDate,
        string listJobsString, bool isTsicAdn,
        CancellationToken ct = default)
    {
        var connection = _context.Database.GetDbConnection();
        var cmd = connection.CreateCommand();

        cmd.CommandText = isTsicAdn
            ? "[reporting].[CustomerJobRevenueRollups]"
            : "[reporting].[CustomerJobRevenueRollups_NotTSICADN]";
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add(new SqlParameter("@jobId", SqlDbType.UniqueIdentifier) { Value = jobId });
        cmd.Parameters.Add(new SqlParameter("@startDate", SqlDbType.DateTime) { Value = startDate });
        cmd.Parameters.Add(new SqlParameter("@endDate", SqlDbType.DateTime) { Value = endDate });
        cmd.Parameters.Add(new SqlParameter("@listJobsString", SqlDbType.VarChar) { Value = listJobsString });

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        // Result set 1: Revenue rollup records
        var revenueRecords = new List<JobRevenueRecordDto>();
        while (await reader.ReadAsync(ct))
        {
            revenueRecords.Add(new JobRevenueRecordDto
            {
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                PayMethod = reader.GetString(reader.GetOrdinal("PayMethod")),
                PayAmount = isTsicAdn
                    ? reader.GetDecimal(reader.GetOrdinal("PayAmount"))
                    : (decimal)reader.GetDouble(reader.GetOrdinal("PayAmount"))
            });
        }

        // Result set 2: Monthly counts
        await reader.NextResultAsync(ct);
        var monthlyCounts = new List<JobMonthlyCountDto>();
        while (await reader.ReadAsync(ct))
        {
            monthlyCounts.Add(new JobMonthlyCountDto
            {
                Aid = reader.GetInt32(reader.GetOrdinal("aid")),
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                CountActivePlayersToDate = reader.GetInt32(reader.GetOrdinal("Count_ActivePlayersToDate")),
                CountActivePlayersToDateLastMonth = reader.GetInt32(reader.GetOrdinal("Count_ActivePlayersToDate_LastMonth")),
                CountNewPlayersThisMonth = reader.GetInt32(reader.GetOrdinal("Count_NewPlayers_ThisMonth")),
                CountActiveTeamsToDate = reader.GetInt32(reader.GetOrdinal("Count_ActiveTeamsToDate")),
                CountActiveTeamsToDateLastMonth = reader.GetInt32(reader.GetOrdinal("Count_ActiveTeamsToDate_LastMonth")),
                CountNewTeamsThisMonth = reader.GetInt32(reader.GetOrdinal("Count_NewTeams_ThisMonth"))
            });
        }

        // Result set 3: Admin fees
        await reader.NextResultAsync(ct);
        var adminFees = new List<JobAdminFeeDto>();
        while (await reader.ReadAsync(ct))
        {
            adminFees.Add(new JobAdminFeeDto
            {
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                ChargeType = reader.GetString(reader.GetOrdinal("ChargeType")),
                ChargeAmount = reader.GetDecimal(reader.GetOrdinal("ChargeAmount")),
                Comment = reader.GetString(reader.GetOrdinal("Comment"))
            });
        }

        // Result set 4: Credit card records
        await reader.NextResultAsync(ct);
        var ccRecords = await ReadPaymentRecords(reader, ct);

        // Result set 5: Check records
        await reader.NextResultAsync(ct);
        var checkRecords = await ReadPaymentRecords(reader, ct);

        // Result set 6: E-Check records
        await reader.NextResultAsync(ct);
        var echeckRecords = await ReadPaymentRecords(reader, ct);

        // Result set 7: Available jobs
        await reader.NextResultAsync(ct);
        var availableJobs = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            availableJobs.Add(reader.GetString(reader.GetOrdinal("JobName")));
        }

        return new JobRevenueDataDto
        {
            RevenueRecords = revenueRecords,
            MonthlyCounts = monthlyCounts,
            AdminFees = adminFees,
            CreditCardRecords = ccRecords,
            CheckRecords = checkRecords,
            EcheckRecords = echeckRecords,
            AvailableJobs = availableJobs
        };
    }

    public async Task UpdateMonthlyCountAsync(
        int aid, UpdateMonthlyCountRequest request, string userId,
        CancellationToken ct = default)
    {
        var record = await _context.MonthlyJobStats
            .FirstOrDefaultAsync(m => m.Aid == aid, ct)
            ?? throw new KeyNotFoundException($"MonthlyJobStats record with aid {aid} not found.");

        record.CountActivePlayersToDate = request.CountActivePlayersToDate;
        record.CountActivePlayersToDateLastMonth = request.CountActivePlayersToDateLastMonth;
        record.CountNewPlayersThisMonth = request.CountNewPlayersThisMonth;
        record.CountActiveTeamsToDate = request.CountActiveTeamsToDate;
        record.CountActiveTeamsToDateLastMonth = request.CountActiveTeamsToDateLastMonth;
        record.CountNewTeamsThisMonth = request.CountNewTeamsThisMonth;
        record.LebUserId = userId;
        record.Modified = DateTime.Now;

        await _context.SaveChangesAsync(ct);
    }

    private static async Task<List<JobPaymentRecordDto>> ReadPaymentRecords(
        System.Data.Common.DbDataReader reader, CancellationToken ct)
    {
        var records = new List<JobPaymentRecordDto>();
        while (await reader.ReadAsync(ct))
        {
            records.Add(new JobPaymentRecordDto
            {
                JobName = reader.GetString(reader.GetOrdinal("JobName")),
                Year = reader.GetInt32(reader.GetOrdinal("Year")),
                Month = reader.GetInt32(reader.GetOrdinal("Month")),
                Registrant = reader.GetString(reader.GetOrdinal("Registrant")),
                PaymentMethod = reader.GetString(reader.GetOrdinal("PaymentMethod")),
                PaymentDate = reader.GetDateTime(reader.GetOrdinal("PaymentDate")),
                PaymentAmount = reader.GetDecimal(reader.GetOrdinal("PaymentAmount"))
            });
        }
        return records;
    }
}
