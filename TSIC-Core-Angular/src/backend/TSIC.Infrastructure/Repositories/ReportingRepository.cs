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
    private const string KindStoredProcedure = "StoredProcedure";
    private const string KindBoldReport = "BoldReport";

    private readonly SqlDbContext _context;

    public ReportingRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<JobReportEntryDto>> GetJobReportsAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds == null || roleIds.Count == 0) return new List<JobReportEntryDto>();

        return await _context.JobReports
            .AsNoTracking()
            .Where(jr => jr.JobId == jobId
                         && jr.Active
                         && roleIds.Contains(jr.RoleId))
            .OrderBy(jr => jr.GroupLabel)
            .ThenBy(jr => jr.SortOrder)
            .ThenBy(jr => jr.Title)
            .Select(jr => new JobReportEntryDto
            {
                JobReportId = jr.JobReportId,
                Title = jr.Title,
                IconName = jr.IconName,
                Controller = jr.Controller,
                Action = jr.Action,
                Kind = jr.Kind,
                GroupLabel = jr.GroupLabel,
                SortOrder = jr.SortOrder,
                Active = jr.Active,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<JobReportEntryDto>> GetAllActiveJobReportsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // SuperUser all-roles view: no role filter, joined to AspNetRoles so each row
        // carries its assigned RoleName. UI dedups by report identity into role chips.
        return await (from jr in _context.JobReports.AsNoTracking()
                      join r in _context.AspNetRoles.AsNoTracking() on jr.RoleId equals r.Id
                      where jr.JobId == jobId && jr.Active
                      orderby jr.GroupLabel, jr.SortOrder, jr.Title
                      select new JobReportEntryDto
                      {
                          JobReportId = jr.JobReportId,
                          Title = jr.Title,
                          IconName = jr.IconName,
                          Controller = jr.Controller,
                          Action = jr.Action,
                          Kind = jr.Kind,
                          GroupLabel = jr.GroupLabel,
                          SortOrder = jr.SortOrder,
                          Active = jr.Active,
                          RoleName = r.Name ?? jr.RoleId,
                      })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasStoredProcedureEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string spName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spName) || roleIds == null || roleIds.Count == 0)
            return false;

        // Action format for stored-proc rows:
        //   ExportStoredProcedureResults?spName=<X>&bUseJobId=true
        // where <X> may be raw ('Foo') or schema-bracketed ('[reporting].[Foo]').
        // Match the spName segment terminated by '&' (current legacy data) or end-of-string
        // (defensive — guards against a future Action where spName is the trailing param,
        // and prevents 'Foo' from matching a row whose spName is 'FooBar').
        var token = "spName=" + spName;
        var tokenWithDelim = token + "&";

        return await _context.JobReports
            .AsNoTracking()
            .AnyAsync(jr => jr.JobId == jobId
                            && jr.Active
                            && jr.Kind == KindStoredProcedure
                            && roleIds.Contains(jr.RoleId)
                            && (jr.Action.Contains(tokenWithDelim) || jr.Action.EndsWith(token)),
                cancellationToken);
    }

    public async Task<bool> HasStoredProcedureEntitlementAnyRoleAsync(
        Guid jobId,
        string spName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spName)) return false;

        // Same spName-token match as the role-scoped check, minus the role filter:
        // SuperUser runs any role's configured report from the all-roles catalogue.
        var token = "spName=" + spName;
        var tokenWithDelim = token + "&";

        return await _context.JobReports
            .AsNoTracking()
            .AnyAsync(jr => jr.JobId == jobId
                            && jr.Active
                            && jr.Kind == KindStoredProcedure
                            && (jr.Action.Contains(tokenWithDelim) || jr.Action.EndsWith(token)),
                cancellationToken);
    }

    public async Task<bool> HasBoldReportEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string reportName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reportName) || roleIds == null || roleIds.Count == 0)
            return false;

        // Action format for Bold rows:
        //   ExportBoldReport?reportName=<X>
        // <X> is the bare RDL stem (no path, no extension). Same delimiter/end-of-string
        // match as the SP variant — guards against prefix collisions.
        var token = "reportName=" + reportName;
        var tokenWithDelim = token + "&";

        return await _context.JobReports
            .AsNoTracking()
            .AnyAsync(jr => jr.JobId == jobId
                            && jr.Active
                            && jr.Kind == KindBoldReport
                            && roleIds.Contains(jr.RoleId)
                            && (jr.Action.Contains(tokenWithDelim) || jr.Action.EndsWith(token)),
                cancellationToken);
    }

    public async Task<bool> HasBoldReportEntitlementAnyRoleAsync(
        Guid jobId,
        string reportName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reportName)) return false;

        var token = "reportName=" + reportName;
        var tokenWithDelim = token + "&";

        return await _context.JobReports
            .AsNoTracking()
            .AnyAsync(jr => jr.JobId == jobId
                            && jr.Active
                            && jr.Kind == KindBoldReport
                            && (jr.Action.Contains(tokenWithDelim) || jr.Action.EndsWith(token)),
                cancellationToken);
    }

    // ══════════════════════════════════════
    // SuperUser editor
    // ══════════════════════════════════════

    public async Task<List<JobReportEditorRoleDto>> GetEditorRolesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        return await (from jr in _context.JobReports.AsNoTracking()
                      join r in _context.AspNetRoles.AsNoTracking() on jr.RoleId equals r.Id
                      where jr.JobId == jobId
                      group new { jr, r } by new { jr.RoleId, r.Name } into g
                      orderby g.Key.Name
                      select new JobReportEditorRoleDto
                      {
                          RoleId = g.Key.RoleId,
                          RoleName = g.Key.Name ?? g.Key.RoleId,
                          RowCount = g.Count(),
                      })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<JobReportEditorRowDto>> GetEditorRowsAsync(
        Guid jobId,
        string roleId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobReports
            .AsNoTracking()
            .Where(jr => jr.JobId == jobId && jr.RoleId == roleId)
            .OrderBy(jr => jr.GroupLabel)
            .ThenBy(jr => jr.SortOrder)
            .ThenBy(jr => jr.Title)
            .Select(jr => new JobReportEditorRowDto
            {
                JobReportId = jr.JobReportId,
                Title = jr.Title,
                IconName = jr.IconName,
                Controller = jr.Controller,
                Action = jr.Action,
                Kind = jr.Kind,
                GroupLabel = jr.GroupLabel,
                SortOrder = jr.SortOrder,
                Active = jr.Active,
                Modified = jr.Modified,
                LebUserId = jr.LebUserId,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<JobReports?> GetJobReportForUpdateAsync(
        Guid jobReportId,
        CancellationToken cancellationToken = default)
    {
        // Tracked (no AsNoTracking) — caller mutates + SaveChanges.
        return await _context.JobReports
            .FirstOrDefaultAsync(jr => jr.JobReportId == jobReportId, cancellationToken);
    }

    public async Task<JobReports> AddJobReportAsync(
        JobReports entity,
        CancellationToken cancellationToken = default)
    {
        _context.JobReports.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);

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
            ExportDate = DateTime.Now,
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

    public async Task<List<TournamentRosterRowDto>> GetTournamentRosterRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Per-registrant raw rows for the tournament roster family. Mirrors the team set of
        // reporting_migrate.TournamentRosterPacked_Flat: active Staff/Player whose assigned
        // team is active, appears in the job's schedule, and is not in a WAITLIST/DROPPED
        // agegroup. The `from t in Teams.Where(...)` (no DefaultIfEmpty) is an INNER JOIN that
        // yields a non-null team without tripping the Guid?/Guid join-key mismatch; the
        // optional club-rep + family-user + family fallbacks ride navigation properties that
        // EF emits as LEFT JOINs. The proc's window columns (divTeamRow, isLastRow) are
        // intentionally dropped — the PDF layer owns team ordering and last-row detection.
        var query =
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
            from uF in _context.AspNetUsers.AsNoTracking()
                .Where(x => x.Id == r.FamilyUserId).DefaultIfEmpty()
            where r.BActive == true
                && (roles.Name == "Staff" || roles.Name == "Player")
                && t.Active == true
                && t.DivId != null
                && t.Agegroup.AgegroupName != null
                && !t.Agegroup.AgegroupName.Contains("WAITLIST")
                && !t.Agegroup.AgegroupName.Contains("DROPPED")
                && _context.Schedule.Any(s => s.JobId == jobId
                    && (s.T1Id == t.TeamId || s.T2Id == t.TeamId))
            select new TournamentRosterRowDto
            {
                TeamId = t.TeamId,
                AgegroupName = t.Agegroup.AgegroupName ?? "",
                DivName = t.Div != null ? (t.Div.DivName ?? "") : "",
                TeamName = t.TeamName ?? "",

                ClubName = t.ClubrepRegistration != null ? t.ClubrepRegistration.ClubName : null,
                ClubRepFirstName = t.ClubrepRegistration != null && t.ClubrepRegistration.User != null
                    ? t.ClubrepRegistration.User.FirstName : null,
                ClubRepLastName = t.ClubrepRegistration != null && t.ClubrepRegistration.User != null
                    ? t.ClubrepRegistration.User.LastName : null,
                ClubRepEmail = t.ClubrepRegistration != null && t.ClubrepRegistration.User != null
                    ? t.ClubrepRegistration.User.Email : null,
                ClubRepCellphone = t.ClubrepRegistration != null && t.ClubrepRegistration.User != null
                    ? t.ClubrepRegistration.User.Cellphone : null,

                RoleName = roles.Name,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UniformNo = r.UniformNo,
                Position = r.Position,
                SchoolName = r.SchoolName,
                GradYear = r.GradYear,
                Gpa = r.Gpa,
                BCollegeCommit = r.BCollegeCommit,
                CollegeCommit = r.CollegeCommit,
                Cellphone = u.Cellphone,

                PlayerEmail = u.Email,
                PlayerStreet = u.StreetAddress,
                PlayerCity = u.City,
                PlayerState = u.State,
                PlayerZip = u.PostalCode,

                FamilyEmail = uF != null ? uF.Email : null,
                FamilyStreet = uF != null ? uF.StreetAddress : null,
                FamilyCity = uF != null ? uF.City : null,
                FamilyState = uF != null ? uF.State : null,
                FamilyZip = uF != null ? uF.PostalCode : null,

                // Optional nav → EF emits a LEFT JOIN, yielding null when there's no family.
                MomCellphone = r.FamilyUser!.MomCellphone,

                SatMath = r.SatMath,
                SatVerbal = r.SatVerbal,
                SatWriting = r.SatWriting,
            };

        return await query.ToListAsync(cancellationToken);
    }
}
