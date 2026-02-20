using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using TSIC.Contracts.Dtos.Logs;
using TSIC.Contracts.Repositories;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Reads from logs.AppLog via ADO.NET (not EF Core).
/// Serilog writes to the table; this repo only reads + purges.
/// </summary>
public class LogRepository : ILogRepository
{
    private readonly string _connectionString;

    public LogRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection not configured");
    }

    public async Task<(List<LogEntryDto> Items, int TotalCount)> QueryAsync(
        LogQueryParams query, CancellationToken ct = default)
    {
        var items = new List<LogEntryDto>();
        int totalCount = 0;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Build WHERE clause
        var conditions = new List<string>();
        var parameters = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            conditions.Add("Level = @Level");
            parameters.Add(new SqlParameter("@Level", query.Level));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(Message LIKE @Search OR Exception LIKE @Search OR SourceContext LIKE @Search OR RequestPath LIKE @Search)");
            parameters.Add(new SqlParameter("@Search", $"%{query.Search}%"));
        }

        if (query.From.HasValue)
        {
            conditions.Add("TimeStamp >= @From");
            parameters.Add(new SqlParameter("@From", query.From.Value));
        }

        if (query.To.HasValue)
        {
            conditions.Add("TimeStamp <= @To");
            parameters.Add(new SqlParameter("@To", query.To.Value));
        }

        var whereClause = conditions.Count > 0
            ? "WHERE " + string.Join(" AND ", conditions)
            : "";

        // Count query
        var countSql = $"SELECT COUNT(*) FROM logs.AppLog {whereClause}";
        await using (var countCmd = new SqlCommand(countSql, conn))
        {
            countCmd.Parameters.AddRange(parameters.Select(p => CloneParameter(p)).ToArray());
            totalCount = (int)(await countCmd.ExecuteScalarAsync(ct) ?? 0);
        }

        // Data query with pagination
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        var dataSql = $@"
            SELECT Id, TimeStamp, Level, Message, Exception,
                   SourceContext, RequestPath, StatusCode, Elapsed
            FROM logs.AppLog
            {whereClause}
            ORDER BY TimeStamp DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        await using var dataCmd = new SqlCommand(dataSql, conn);
        dataCmd.Parameters.AddRange(parameters.Select(p => CloneParameter(p)).ToArray());
        dataCmd.Parameters.Add(new SqlParameter("@Offset", offset));
        dataCmd.Parameters.Add(new SqlParameter("@PageSize", pageSize));

        await using var reader = await dataCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(new LogEntryDto
            {
                Id = reader.GetInt64(0),
                TimeStamp = reader.GetDateTimeOffset(1),
                Level = reader.GetString(2),
                Message = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Exception = reader.IsDBNull(4) ? null : reader.GetString(4),
                SourceContext = reader.IsDBNull(5) ? null : reader.GetString(5),
                RequestPath = reader.IsDBNull(6) ? null : reader.GetString(6),
                StatusCode = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Elapsed = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            });
        }

        return (items, totalCount);
    }

    public async Task<LogStatsDto> GetStatsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // 1) Counts by hour + level
        var countsByHour = new List<LogCountByHour>();
        var hourSql = @"
            SELECT DATEADD(hour, DATEDIFF(hour, 0, TimeStamp), 0) AS Hour,
                   Level, COUNT(*) AS Cnt
            FROM logs.AppLog
            WHERE TimeStamp >= @From AND TimeStamp <= @To
            GROUP BY DATEADD(hour, DATEDIFF(hour, 0, TimeStamp), 0), Level
            ORDER BY Hour";

        await using (var cmd = new SqlCommand(hourSql, conn))
        {
            cmd.Parameters.Add(new SqlParameter("@From", from));
            cmd.Parameters.Add(new SqlParameter("@To", to));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                countsByHour.Add(new LogCountByHour
                {
                    Hour = new DateTimeOffset(reader.GetDateTime(0), TimeSpan.Zero),
                    Level = reader.GetString(1),
                    Count = reader.GetInt32(2),
                });
            }
        }

        // 2) Counts by hour + status range (2XX/3XX/4XX/5XX)
        var countsByHourByStatus = new List<LogCountByHourByStatus>();
        var statusHourSql = @"
            SELECT DATEADD(hour, DATEDIFF(hour, 0, TimeStamp), 0) AS Hour,
                   CASE
                       WHEN StatusCode >= 200 AND StatusCode < 300 THEN '2XX'
                       WHEN StatusCode >= 300 AND StatusCode < 400 THEN '3XX'
                       WHEN StatusCode >= 400 AND StatusCode < 500 THEN '4XX'
                       WHEN StatusCode >= 500 THEN '5XX'
                       ELSE 'Other'
                   END AS StatusRange,
                   COUNT(*) AS Cnt
            FROM logs.AppLog
            WHERE TimeStamp >= @From AND TimeStamp <= @To
              AND StatusCode IS NOT NULL
            GROUP BY DATEADD(hour, DATEDIFF(hour, 0, TimeStamp), 0),
                     CASE
                         WHEN StatusCode >= 200 AND StatusCode < 300 THEN '2XX'
                         WHEN StatusCode >= 300 AND StatusCode < 400 THEN '3XX'
                         WHEN StatusCode >= 400 AND StatusCode < 500 THEN '4XX'
                         WHEN StatusCode >= 500 THEN '5XX'
                         ELSE 'Other'
                     END
            ORDER BY Hour";

        await using (var cmd = new SqlCommand(statusHourSql, conn))
        {
            cmd.Parameters.Add(new SqlParameter("@From", from));
            cmd.Parameters.Add(new SqlParameter("@To", to));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                countsByHourByStatus.Add(new LogCountByHourByStatus
                {
                    Hour = new DateTimeOffset(reader.GetDateTime(0), TimeSpan.Zero),
                    StatusRange = reader.GetString(1),
                    Count = reader.GetInt32(2),
                });
            }
        }

        // 3) Counts by level (renumbered after adding status query)
        var countsByLevel = new Dictionary<string, int>();
        var levelSql = @"
            SELECT Level, COUNT(*) AS Cnt
            FROM logs.AppLog
            WHERE TimeStamp >= @From AND TimeStamp <= @To
            GROUP BY Level";

        await using (var cmd = new SqlCommand(levelSql, conn))
        {
            cmd.Parameters.Add(new SqlParameter("@From", from));
            cmd.Parameters.Add(new SqlParameter("@To", to));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                countsByLevel[reader.GetString(0)] = reader.GetInt32(1);
            }
        }

        // 3) Top 10 errors
        var topErrors = new List<TopErrorDto>();
        var errorSql = @"
            SELECT TOP 10 LEFT(Message, 200) AS Msg, COUNT(*) AS Cnt, MAX(TimeStamp) AS LastSeen
            FROM logs.AppLog
            WHERE TimeStamp >= @From AND TimeStamp <= @To
              AND Level IN ('Error', 'Fatal')
            GROUP BY LEFT(Message, 200)
            ORDER BY Cnt DESC";

        await using (var cmd = new SqlCommand(errorSql, conn))
        {
            cmd.Parameters.Add(new SqlParameter("@From", from));
            cmd.Parameters.Add(new SqlParameter("@To", to));

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                topErrors.Add(new TopErrorDto
                {
                    Message = reader.IsDBNull(0) ? "" : reader.GetString(0),
                    Count = reader.GetInt32(1),
                    LastSeen = reader.GetDateTimeOffset(2),
                });
            }
        }

        // 4) Total count
        var totalCount = countsByLevel.Values.Sum();

        return new LogStatsDto
        {
            CountsByHour = countsByHour,
            CountsByHourByStatus = countsByHourByStatus,
            CountsByLevel = countsByLevel,
            TopErrors = topErrors,
            TotalCount = totalCount,
        };
    }

    public async Task<int> PurgeBeforeAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(
            "DELETE FROM logs.AppLog WHERE TimeStamp < @Cutoff", conn);
        cmd.Parameters.Add(new SqlParameter("@Cutoff", cutoff));
        cmd.CommandTimeout = 120; // purge can be slow on large tables

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter CloneParameter(SqlParameter source)
    {
        return new SqlParameter(source.ParameterName, source.SqlDbType)
        {
            Value = source.Value,
            Size = source.Size,
        };
    }
}
