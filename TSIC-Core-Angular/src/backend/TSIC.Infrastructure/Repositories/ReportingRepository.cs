using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ReportingRepository : IReportingRepository
{
    private readonly SqlDbContext _context;

    public ReportingRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<ReportCatalogueEntryDto>> GetActiveCatalogueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ReportCatalogue
            .AsNoTracking()
            .Where(r => r.Active)
            .OrderBy(r => r.SortOrder)
            .Select(r => new ReportCatalogueEntryDto
            {
                ReportId = r.ReportId,
                Title = r.Title,
                Description = r.Description,
                IconName = r.IconName,
                StoredProcName = r.StoredProcName,
                ParametersJson = r.ParametersJson,
                VisibilityRules = r.VisibilityRules,
                SortOrder = r.SortOrder,
                Active = r.Active
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<ReportCatalogueEntryDto>> GetAllCatalogueEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ReportCatalogue
            .AsNoTracking()
            .OrderBy(r => r.SortOrder)
            .Select(r => new ReportCatalogueEntryDto
            {
                ReportId = r.ReportId,
                Title = r.Title,
                Description = r.Description,
                IconName = r.IconName,
                StoredProcName = r.StoredProcName,
                ParametersJson = r.ParametersJson,
                VisibilityRules = r.VisibilityRules,
                SortOrder = r.SortOrder,
                Active = r.Active
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ReportCatalogueEntryDto> CreateCatalogueEntryAsync(
        ReportCatalogueWriteDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = new ReportCatalogue
        {
            ReportId = Guid.NewGuid(),
            Title = dto.Title,
            Description = dto.Description,
            IconName = dto.IconName,
            StoredProcName = dto.StoredProcName,
            ParametersJson = dto.ParametersJson,
            VisibilityRules = dto.VisibilityRules,
            SortOrder = dto.SortOrder,
            Active = dto.Active,
            Modified = DateTime.Now,
            LebUserId = lebUserId
        };

        _context.ReportCatalogue.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return ProjectToDto(entity);
    }

    public async Task<ReportCatalogueEntryDto?> UpdateCatalogueEntryAsync(
        Guid reportId,
        ReportCatalogueWriteDto dto,
        string lebUserId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.ReportCatalogue
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);

        if (entity == null) return null;

        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.IconName = dto.IconName;
        entity.StoredProcName = dto.StoredProcName;
        entity.ParametersJson = dto.ParametersJson;
        entity.VisibilityRules = dto.VisibilityRules;
        entity.SortOrder = dto.SortOrder;
        entity.Active = dto.Active;
        entity.Modified = DateTime.Now;
        entity.LebUserId = lebUserId;

        await _context.SaveChangesAsync(cancellationToken);

        return ProjectToDto(entity);
    }

    public async Task<bool> DeleteCatalogueEntryAsync(
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var entity = await _context.ReportCatalogue
            .FirstOrDefaultAsync(r => r.ReportId == reportId, cancellationToken);

        if (entity == null) return false;

        _context.ReportCatalogue.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> StoredProcedureExistsAsync(
        string spName,
        CancellationToken cancellationToken = default)
    {
        // Parameterized OBJECT_ID lookup — accepts schema-qualified or bare names.
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@name, 'P') IS NULL THEN 0 ELSE 1 END";
        cmd.CommandType = CommandType.Text;
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 200) { Value = spName });

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }

    private static ReportCatalogueEntryDto ProjectToDto(ReportCatalogue r) => new()
    {
        ReportId = r.ReportId,
        Title = r.Title,
        Description = r.Description,
        IconName = r.IconName,
        StoredProcName = r.StoredProcName,
        ParametersJson = r.ParametersJson,
        VisibilityRules = r.VisibilityRules,
        SortOrder = r.SortOrder,
        Active = r.Active
    };

    public async Task<(DbDataReader Reader, DbConnection Connection)> ExecuteStoredProcedureAsync(
        string spName,
        Guid jobId,
        bool useJobId,
        bool useDateUnscheduled = false,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        var cmd = connection.CreateCommand();

        cmd.CommandText = spName;
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add(new SqlParameter("@jobID", SqlDbType.UniqueIdentifier)
        {
            Value = useJobId ? jobId : Guid.Empty
        });

        if (useDateUnscheduled)
        {
            cmd.Parameters.Add(new SqlParameter("@gDate_Unscheduled", SqlDbType.DateTime)
            {
                Value = new DateTime(2017, 12, 30)
            });
        }

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return (reader, connection);
    }

    public async Task<(DbDataReader Reader, DbConnection Connection)> ExecuteMonthlyReconciliationAsync(
        int settlementMonth,
        int settlementYear,
        bool isMerchandise,
        CancellationToken cancellationToken = default)
    {
        var connection = _context.Database.GetDbConnection();
        var cmd = connection.CreateCommand();

        cmd.CommandText = isMerchandise
            ? "[adn].[MonthyQBPExport_Automated_Merch]"
            : "[adn].[MonthyQBPExport_Automated]";
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add(new SqlParameter("@settlementMonth", SqlDbType.Int) { Value = settlementMonth });
        cmd.Parameters.Add(new SqlParameter("@settlementYear", SqlDbType.Int) { Value = settlementYear });

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return (reader, connection);
    }

    public async Task RecordExportHistoryAsync(
        Guid registrationId,
        string? storedProcedureName,
        string? reportName,
        CancellationToken cancellationToken = default)
    {
        var record = new JobReportExportHistory
        {
            ExportDate = DateTime.UtcNow,
            RegistrationId = registrationId,
            ReportName = reportName,
            StoredProcedureName = storedProcedureName
        };

        _context.JobReportExportHistory.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ScheduleGameForICalDto>> GetScheduleGamesForICalAsync(
        List<int> gameIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => gameIds.Contains(s.Gid))
            .Select(s => new ScheduleGameForICalDto
            {
                Gid = s.Gid,
                GDate = s.GDate,
                T1Name = s.T1Name,
                T2Name = s.T2Name,
                FieldName = s.Field != null ? s.Field.FName : null,
                Address = s.Field != null ? s.Field.Address : null,
                City = s.Field != null ? s.Field.City : null,
                State = s.Field != null ? s.Field.State : null,
                Zip = s.Field != null ? s.Field.Zip : null
            })
            .ToListAsync(cancellationToken);
    }
}
