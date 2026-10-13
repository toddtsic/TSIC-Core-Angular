using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ReportingRepository : IReportingRepository
{
    private const string KindStoredProcedure = "StoredProcedure";
    private const string KindBoldReport = "BoldReport";
    private const string KindCrystalReport = "CrystalReport";

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
                ReportLibraryId = jr.ReportLibraryId,
                Description = jr.ReportLibrary != null ? jr.ReportLibrary.Description : null,
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
                          ReportLibraryId = jr.ReportLibraryId,
                          Description = jr.ReportLibrary != null ? jr.ReportLibrary.Description : null,
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

    // ── Reports LIBRARY ──────────────────────────────────────────────────────

    public async Task<ShelfContextDto?> GetShelfContextAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new ShelfContextDto { CustomerId = j.CustomerId, JobTypeId = j.JobTypeId })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ReportLibraryEntryDto>> GetReportLibraryForShelfAsync(
        Guid jobId,
        string roleId,
        IReadOnlyCollection<string> allowedMinRoleIds,
        Guid customerId,
        int jobTypeId,
        bool bypassApplicability,
        CancellationToken cancellationToken = default)
    {
        if (allowedMinRoleIds.Count == 0) return new List<ReportLibraryEntryDto>();

        // Inner join on the role drops retired entries (MinRoleId NULL) before the rank
        // filter even runs. Applicability: no job-type rows = every job type.
        return await (from l in _context.ReportLibrary.AsNoTracking()
                      join r in _context.AspNetRoles.AsNoTracking() on l.MinRoleId equals r.Id
                      where allowedMinRoleIds.Contains(l.MinRoleId!)
                            && (l.OwnerCustomerId == null || l.OwnerCustomerId == customerId)
                            && (bypassApplicability
                                || !l.JobType.Any()
                                || l.JobType.Any(jt => jt.JobTypeId == jobTypeId))
                      orderby l.CategoryCode, l.SortOrder, l.Title
                      select new ReportLibraryEntryDto
                      {
                          ReportLibraryId = l.ReportLibraryId,
                          ReportKey = l.ReportKey,
                          Title = l.Title,
                          Description = l.Description,
                          Tags = l.Tags,
                          CategoryCode = l.CategoryCode,
                          IconName = l.IconName,
                          Kind = l.Kind,
                          Controller = l.Controller,
                          Action = l.Action,
                          Scope = l.Scope,
                          MinRoleName = r.Name ?? l.MinRoleId!,
                          ShelfJobReportId = _context.JobReports
                              .Where(jr => jr.JobId == jobId
                                           && jr.RoleId == roleId
                                           && jr.ReportLibraryId == l.ReportLibraryId)
                              .Select(jr => (Guid?)jr.JobReportId)
                              .FirstOrDefault(),
                      })
            .ToListAsync(cancellationToken);
    }

    public async Task<ReportLibrary?> GetReportLibraryEntryAsync(Guid reportLibraryId, CancellationToken cancellationToken = default)
    {
        return await _context.ReportLibrary
            .AsNoTracking()
            .Include(l => l.JobType)
            .FirstOrDefaultAsync(l => l.ReportLibraryId == reportLibraryId, cancellationToken);
    }

    public async Task<Guid?> GetShelfRowIdAsync(Guid jobId, string roleId, Guid reportLibraryId, CancellationToken cancellationToken = default)
    {
        return await _context.JobReports
            .AsNoTracking()
            .Where(jr => jr.JobId == jobId && jr.RoleId == roleId && jr.ReportLibraryId == reportLibraryId)
            .Select(jr => (Guid?)jr.JobReportId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<int> GetMaxShelfSortOrderAsync(Guid jobId, string roleId, CancellationToken cancellationToken = default)
    {
        return await _context.JobReports
            .AsNoTracking()
            .Where(jr => jr.JobId == jobId && jr.RoleId == roleId)
            .Select(jr => (int?)jr.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
    }

    public async Task<bool> DeleteShelfRowAsync(Guid jobReportId, Guid jobId, string roleId, CancellationToken cancellationToken = default)
    {
        // The (job, role) predicate IS the ownership check — a row outside the caller's own
        // shelf matches nothing and nothing is deleted.
        var deleted = await _context.JobReports
            .Where(jr => jr.JobReportId == jobReportId && jr.JobId == jobId && jr.RoleId == roleId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 1;
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

    public async Task<List<ThirdPartyExportLogEntryDto>> GetThirdPartyExportHistoryAsync(
        IReadOnlyList<Guid> jobIds,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
            return [];

        // Shaped in memory after the join so the name fallback is one rule in one place:
        // vendor aliases routinely carry no first or last name, and an empty lead line on a
        // report about who took minors' data out is the one thing it must never render.
        var raw = await (
            from h in _context.JobReportExportHistory
            join r in _context.Registrations on h.RegistrationId equals r.RegistrationId
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            where h.ReportName == ReportActionConstants.ThirdPartyRosterExport
                  && h.ExportDate != null
                  && h.ExportDate >= since
                  && jobIds.Contains(r.JobId)
            select new
            {
                r.JobId,
                r.Job.JobName,
                UserName = r.User!.UserName,
                r.User.FirstName,
                r.User.LastName,
                RoleName = role.Name,
                r.RoleId,
                ExportedAt = h.ExportDate!.Value,
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return raw
            .OrderByDescending(x => x.ExportedAt)
            .Select(x =>
            {
                var login = x.UserName ?? string.Empty;
                var name = $"{x.FirstName} {x.LastName}".Trim();
                return new ThirdPartyExportLogEntryDto
                {
                    JobId = x.JobId,
                    JobName = x.JobName ?? string.Empty,
                    ExporterName = name.Length > 0 ? name : login,
                    ExporterLogin = login,
                    RoleName = x.RoleName ?? string.Empty,
                    IsThirdParty = string.Equals(x.RoleId, RoleConstants.ApiAuthorized, StringComparison.OrdinalIgnoreCase),
                    ExportedAt = x.ExportedAt,
                };
            })
            .ToList();
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
        bool requiresSchedule = true,
        CancellationToken cancellationToken = default)
    {
        // Per-registrant raw rows for the tournament roster family. Mirrors the team set of
        // reporting_migrate.TournamentRosterPacked_Flat: active Staff/Player whose assigned
        // team is active and is not in a WAITLIST/DROPPED agegroup. The `from t in Teams.Where(...)`
        // (no DefaultIfEmpty) is an INNER JOIN that yields a non-null team without tripping the
        // Guid?/Guid join-key mismatch; the optional club-rep + family-user + family fallbacks
        // ride navigation properties that EF emits as LEFT JOINs. The proc's window columns
        // (divTeamRow, isLastRow) are intentionally dropped — the PDF layer owns team ordering
        // and last-row detection.
        //
        // Job scope is conditional on requiresSchedule (default true):
        //   true  → tournament scope: team must appear in this job's Schedule (the schedule gate
        //           IS the job filter — a scheduled team belongs to the job).
        //   false → showcase/offer scope (e.g. American Select offer rosters, which play no
        //           games): the schedule gate is dropped, so scope the job explicitly by JobId
        //           and exclude the holding-pen "Registration" agegroup (tryout teams). Existing
        //           callers pass true → identical SQL/behavior (the false-branch predicate is
        //           short-circuited away).
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
                && (!requiresSchedule || _context.Schedule.Any(s => s.JobId == jobId
                    && (s.T1Id == t.TeamId || s.T2Id == t.TeamId)))
                && (requiresSchedule || (r.JobId == jobId
                    && t.Agegroup.AgegroupName != "Registration"))
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
                PlayerClubName = r.ClubName,
                PlayerClubTeamName = r.ClubTeamName,
                DayGroup = r.DayGroup,
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

    public async Task<List<ScheduleListGameDto>> GetScheduleListGamesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.ScheduleList_Flat. The inner joins on agegroup
        // (color), league (name), and field (name) reproduce the proc's existence filter:
        // agegroup/field ride a correlated Where (the FKs are Guid? — this avoids the Guid?/Guid
        // join-key mismatch and yields an INNER JOIN with no DefaultIfEmpty), league a plain join
        // (LeagueId is non-null). A null FieldId therefore drops the row, exactly as the proc's
        // `inner join reference.Fields` did. Each side's club rep contact rides the optional
        // Schedule.T1/T2 → ClubrepRegistration → User chain, which EF emits as LEFT JOINs.
        // Denormalized AgegroupName/DivName/team names are taken straight off Schedule, as in
        // the proc; the proc's bracket-label CASE and rep-name concat move to the PDF layer.
        var query =
            from s in _context.Schedule.AsNoTracking()
            from ag in _context.Agegroups.AsNoTracking().Where(x => x.AgegroupId == s.AgegroupId)
            join l in _context.Leagues.AsNoTracking() on s.LeagueId equals l.LeagueId
            from f in _context.Fields.AsNoTracking().Where(x => x.FieldId == s.FieldId)
            where s.JobId == jobId
            orderby s.GDate
            select new ScheduleListGameDto
            {
                Gid = s.Gid,
                AgegroupName = s.AgegroupName,
                DivName = s.DivName,
                DivId = s.DivId,
                LeagueName = l.LeagueName,
                FieldName = f.FName,
                Color = ag.Color,
                GDate = s.GDate,

                T1Id = s.T1Id,
                T1Name = s.T1Name,
                T1Type = s.T1Type,
                T1No = s.T1No,
                T1Ann = s.T1Ann,
                T1Score = s.T1Score,

                T2Id = s.T2Id,
                T2Name = s.T2Name,
                T2Type = s.T2Type,
                T2No = (int?)s.T2No,
                T2Ann = s.T2Ann,
                T2Score = s.T2Score,

                // Optional nav chains → EF emits LEFT JOINs, yielding null when a side has no
                // team / clubrep / user (same idiom as r.FamilyUser! above). The ! is compile-
                // time only; this is an expression tree, never dereferenced in C#.
                ClubRep1First = s.T1!.ClubrepRegistration!.User!.FirstName,
                ClubRep1Last = s.T1!.ClubrepRegistration!.User!.LastName,
                ClubRep2First = s.T2!.ClubrepRegistration!.User!.FirstName,
                ClubRep2Last = s.T2!.ClubrepRegistration!.User!.LastName,
            };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<GameBoardStandingTeamDto>> GetScheduleStandingsTeamsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Standings roster per division for the Game Boards report (Schedule_ByAgegroup). The printed
        // box is blank write-in (Wins/Losses/Ties/Goals Against), so only the team list per division is
        // needed. Mirrors reporting.Schedule_Get_DivTeamsAndStandings: active teams, excluding the
        // Unassigned holding division, name = club-rep ClubName ':' TeamName (else TeamName) — the same
        // shape the schedule denormalizes into T1Name/T2Name — ordered by DivRank. Scoped by JobId (a job
        // is one season/year, standing in for the proc's year+divID args). The club-rep registration
        // rides an optional nav → LEFT JOIN; Div is an inner relationship (DivId is filtered non-null).
        var query =
            from t in _context.Teams.AsNoTracking()
            where t.JobId == jobId
                && t.Active == true
                && t.DivId != null
                && t.Div!.DivName != "Unassigned"
            orderby t.DivRank
            select new GameBoardStandingTeamDto
            {
                DivId = t.DivId,
                TeamFullName =
                    t.ClubrepRegistration != null && t.ClubrepRegistration.ClubName != null
                        ? t.ClubrepRegistration.ClubName + ":" + (t.TeamName ?? string.Empty)
                        : (t.TeamName ?? string.Empty),
                DivRank = t.DivRank,
            };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<RosterTableRowDto>> GetRosterTableRowsAsync(
        Guid jobId,
        bool playersOnly,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the wide-roster Crystal family. All active, team-assigned registrants
        // on active teams (NOT schedule-gated — club/camp/showcase rosters aren't on a tournament
        // schedule). Role filter: Player-only, or Staff+Player. Roles/user are inner joins; the
        // assigned team rides a correlated Where (Guid? FK → INNER, no DefaultIfEmpty); family
        // (Mom/Dad) and the per-team club rep ride optional navs that EF emits as LEFT JOINs.
        var query =
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
            where r.JobId == jobId
                && r.BActive == true
                && t.Active == true
                && (roles.Name == "Player" || (!playersOnly && roles.Name == "Staff"))
            orderby t.Agegroup.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new RosterTableRowDto
            {
                RegistrationId = r.RegistrationId,
                RoleName = roles.Name,

                LeagueName = t.League.LeagueName,
                AgegroupName = t.Agegroup.AgegroupName,
                DivName = t.Div != null ? t.Div.DivName : null,
                TeamName = t.TeamName,
                ClubName = r.ClubName,
                ClubTeamName = r.ClubTeamName,
                Color = t.Agegroup.Color,

                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Dob = u.Dob,

                UniformNo = r.UniformNo,
                Position = r.Position,
                SchoolName = r.SchoolName,
                SchoolGrade = r.SchoolGrade,
                GradYear = r.GradYear,
                Gpa = r.Gpa,
                SatMath = r.SatMath,
                SatVerbal = r.SatVerbal,
                SatWriting = r.SatWriting,
                Act = r.Act,
                DayGroup = r.DayGroup,
                NightGroup = r.NightGroup,
                Roommate = r.RoommatePref,

                Email = u.Email,
                Cellphone = u.Cellphone,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,

                // Optional FamilyUser nav → LEFT JOIN, null when the registrant has no family record.
                MomFirstName = r.FamilyUser!.MomFirstName,
                MomLastName = r.FamilyUser!.MomLastName,
                MomEmail = r.FamilyUser!.MomEmail,
                MomCellphone = r.FamilyUser!.MomCellphone,
                DadFirstName = r.FamilyUser!.DadFirstName,
                DadLastName = r.FamilyUser!.DadLastName,
                DadEmail = r.FamilyUser!.DadEmail,
                DadCellphone = r.FamilyUser!.DadCellphone,

                MedicalNote = r.MedicalNote,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,

                JerseySize = r.JerseySize,
                ShortsSize = r.ShortsSize,
                Kilt = r.Kilt,
                TShirt = r.TShirt,
                Reversible = r.Reversible,
                Gloves = r.Gloves,
                Shoes = r.Shoes,

                SportAssnId = r.SportAssnId,
                SportAssnIdexpDate = r.SportAssnIdexpDate,

                // Optional ClubrepRegistration→User nav chain → LEFT JOINs.
                ClubRepFirstName = t.ClubrepRegistration!.User!.FirstName,
                ClubRepLastName = t.ClubrepRegistration!.User!.LastName,
                ClubRepEmail = t.ClubrepRegistration!.User!.Email,
                ClubRepCellphone = t.ClubrepRegistration!.User!.Cellphone,
            };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<ClubRosterRowDto>> GetClubRosterRowsAsync(
        Guid jobId,
        bool allCustomerJobs,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the "Coaches Eyes Only" club roster Crystal family. Active Player rows
        // on active teams, grouped one boxed block per assigned team. Two scopes share this one query:
        //   * per-job  (allCustomerJobs=false): just this job.
        //   * all-jobs (allCustomerJobs=true):  every job of this job's customer — mirrors
        //     reporting.Job_Club_Rosters_AllClubJobs, which derives @customerID from the job, then
        //     filters j.customerID = @customerID. We resolve the customer up front and widen the WHERE.
        // Roles/user are inner joins; the assigned team rides a correlated Where (Guid? FK → INNER);
        // family (Mom/Dad) is an optional nav EF emits as a LEFT JOIN.
        Guid? customerId = allCustomerJobs
            ? await _context.Jobs.AsNoTracking()
                .Where(j => j.JobId == jobId)
                .Select(j => (Guid?)j.CustomerId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var query =
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
                // Home town is the FAMILY user's city, not the player's: AspNetUsers.City is populated on
                // the family login (389/389 on STEPS Girls) and effectively empty on the player row
                // (9/389). Registrations.FamilyUser navigates to Families, which has no address, so the
                // family's AspNetUsers row needs an explicit LEFT JOIN (DefaultIfEmpty) on FamilyUserId.
            from fu in _context.AspNetUsers.AsNoTracking()
                .Where(x => x.Id == r.FamilyUserId).DefaultIfEmpty()
            where (allCustomerJobs ? r.Job.CustomerId == customerId : r.JobId == jobId)
                && r.BActive == true
                && t.Active == true
                && roles.Name == "Player"
            orderby r.Job.JobName, t.League.LeagueName, t.Agegroup.AgegroupName,
                    t.Div!.DivName, t.TeamName, u.LastName, u.FirstName
            select new ClubRosterRowDto
            {
                RegistrationId = r.RegistrationId,
                TeamId = t.TeamId,

                CustomerName = r.Job.Customer.CustomerName,
                JobName = r.Job.JobName,
                LeagueName = t.League.LeagueName,
                AgegroupName = t.Agegroup.AgegroupName,
                DivName = t.Div != null ? t.Div.DivName : null,
                TeamName = t.TeamName,
                ClubName = r.ClubName,

                UniformNo = r.UniformNo,

                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,

                Dob = u.Dob,
                Position = r.Position,

                Cellphone = u.Cellphone,
                SchoolName = r.SchoolName,
                City = fu != null ? fu.City : null,

                OwedTotal = r.OwedTotal,
                PaidTotal = r.PaidTotal,

                // Optional FamilyUser nav → LEFT JOIN, null when the registrant has no family record.
                MomFirstName = r.FamilyUser!.MomFirstName,
                MomLastName = r.FamilyUser!.MomLastName,
                MomCellphone = r.FamilyUser!.MomCellphone,
                MomEmail = r.FamilyUser!.MomEmail,
                DadFirstName = r.FamilyUser!.DadFirstName,
                DadLastName = r.FamilyUser!.DadLastName,
                DadCellphone = r.FamilyUser!.DadCellphone,
                DadEmail = r.FamilyUser!.DadEmail,

                MedicalNote = r.MedicalNote,
            };

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<List<DailyRegCountRowDto>> GetDailyRegCountsAsync(
        DateTime asOfLocal,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.Get_Registrations_TSIC_Today. The proc is two passes:
        // (1) today's active reg counts grouped by (customer, job, role), then (2) the same combos
        // joined back to ALL active regs for the to-date total. We reproduce that as two grouped
        // queries keyed on (JobId, RoleId) — equivalent to (customer, job, role) since job→customer
        // and role↔roleId are 1:1 — and stitch them in C#. The AspNetUsers inner join is an
        // existence filter that matches the proc (a reg with no user is dropped); names ride
        // Job/Customer. Sargable date range (>= dayStart && < dayEnd) instead of CONVERT(char,..)
        // so it stays index-friendly and Postgres-portable.
        var dayStart = asOfLocal.Date;
        var dayEnd = dayStart.AddDays(1);

        var todayGroups = await (
            from r in _context.Registrations.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.BActive == true
                && r.RegistrationTs >= dayStart
                && r.RegistrationTs < dayEnd
            group r by new { r.JobId, c.CustomerName, j.JobName, RoleId = roles.Id, RoleName = roles.Name } into grp
            select new
            {
                grp.Key.JobId,
                grp.Key.CustomerName,
                grp.Key.JobName,
                grp.Key.RoleId,
                grp.Key.RoleName,
                Daily = grp.Count(),
            })
            .ToListAsync(cancellationToken);

        if (todayGroups.Count == 0)
        {
            return new List<DailyRegCountRowDto>();
        }

        var jobIds = todayGroups.Select(x => x.JobId).Distinct().ToList();

        var toDateGroups = await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.BActive == true && jobIds.Contains(r.JobId)
            group r by new { r.JobId, RoleId = roles.Id } into grp
            select new { grp.Key.JobId, grp.Key.RoleId, ToDate = grp.Count() })
            .ToListAsync(cancellationToken);

        var toDate = toDateGroups.ToDictionary(x => (x.JobId, x.RoleId), x => x.ToDate);

        return todayGroups
            .Select(x => new DailyRegCountRowDto
            {
                CustomerName = x.CustomerName ?? string.Empty,
                JobName = x.JobName ?? string.Empty,
                RoleName = x.RoleName ?? string.Empty,
                CountDaily = x.Daily,
                CountToDate = toDate.TryGetValue((x.JobId, x.RoleId), out var td) ? td : x.Daily,
            })
            .OrderBy(x => x.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.JobName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RoleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // adn.rpt_invoice hardcodes this payment-method GUID = "Credit Card Payment"; CC-payment lines
    // are only billable once their settlement carries an invoice number (the proc's gate).
    private static readonly Guid CreditCardPaymentMethodId = new("30ECA575-A268-E111-9D56-F04DA202060D");

    public async Task<List<InvoiceLineRawDto>> GetInvoiceLinesAsync(
        int settlementYear,
        int settlementMonth,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for adn.rpt_invoice. Two flat queries (player + team), concatenated =
        // the proc's UNION ALL. We read the BASE adn.Txs table (NOT the adn.vTxs view) and keep the
        // settlement date/amount as RAW TEXT: year/month for the month filter come from fixed
        // substring positions in "DD-Mon-YYYY ..." (SQL chars 8-11 = year, 4-6 = month abbr — i.e.
        // C# Substring(7,4) / Substring(3,3)), exactly as the view's own SettlementYear/Month do, so
        // there is no datetime coercion and no schema change. Amount text→decimal and the credit
        // negation happen in the service. Each line carries its job fee rates + that month's
        // adn.Monthly_Job_Stats counts (LEFT JOIN), denormalized — the report needs no subreport.
        var yearStr = settlementYear.ToString(CultureInfo.InvariantCulture);
        var monthAbbr = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(settlementMonth); // "May"
        var monthStart = new DateTime(settlementYear, settlementMonth, 1);
        var monthEnd = monthStart.AddMonths(1);
        var ccPaymentId = CreditCardPaymentMethodId;

        // ── Player branch: Registration_Accounting.teamID IS NULL ──
        var playerLines = await (
            from ata in _context.RegistrationAccounting.AsNoTracking()
            where ata.Active == true && ata.TeamId == null && (ata.Payamt ?? 0m) != 0m
            from at in _context.Registrations.AsNoTracking().Where(x => x.RegistrationId == ata.RegistrationId)
            join j in _context.Jobs.AsNoTracking() on at.JobId equals j.JobId
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            join pm in _context.AccountingPaymentMethods.AsNoTracking() on ata.PaymentMethodId equals pm.PaymentMethodId
            from u in _context.AspNetUsers.AsNoTracking().Where(x => x.Id == at.UserId)
            from txs in _context.Txs.AsNoTracking().Where(t =>
                    t.TransactionId == ata.AdnTransactionId
                    && t.TransactionStatus != "Declined" && t.TransactionStatus != "Voided")
                .DefaultIfEmpty()
            from mjs in _context.MonthlyJobStats.AsNoTracking().Where(m =>
                    m.JobId == j.JobId && m.Year == settlementYear && m.Month == settlementMonth)
                .DefaultIfEmpty()
            where
                // Effective settlement month = settlement-text-month when settled, else ata.Modified.
                ((txs != null && txs.SettlementDateTime != null
                    && txs.SettlementDateTime.Substring(7, 4) == yearStr
                    && txs.SettlementDateTime.Substring(3, 3) == monthAbbr)
                 || ((txs == null || txs.SettlementDateTime == null)
                    && ata.Modified >= monthStart && ata.Modified < monthEnd))
                // CC-payment gate: a Credit Card Payment line needs a settled invoice number.
                && (ata.PaymentMethodId != ccPaymentId || (txs != null && txs.InvoiceNumber != null))
            select new InvoiceLineRawDto
            {
                JobId = j.JobId,
                CustomerName = c.CustomerName,
                JobName = j.JobName,
                JobTypeId = j.JobTypeId,
                PerPlayerCharge = j.PerPlayerCharge,
                PerTeamCharge = j.PerTeamCharge,
                ProcessingFeePercent = j.ProcessingFeePercent,
                CountActivePlayersToDate = mjs != null ? mjs.CountActivePlayersToDate : null,
                CountActivePlayersToDateLastMonth = mjs != null ? mjs.CountActivePlayersToDateLastMonth : null,
                CountNewPlayersThisMonth = mjs != null ? mjs.CountNewPlayersThisMonth : null,
                CountActiveTeamsToDate = mjs != null ? mjs.CountActiveTeamsToDate : null,
                CountActiveTeamsToDateLastMonth = mjs != null ? mjs.CountActiveTeamsToDateLastMonth : null,
                CountNewTeamsThisMonth = mjs != null ? mjs.CountNewTeamsThisMonth : null,
                IsTeam = false,
                PaymentMethodName = pm.PaymentMethod,
                PaymentMethodId = pm.PaymentMethodId,
                SettlementDateTimeText = txs != null ? txs.SettlementDateTime : null,
                SettlementAmountText = txs != null ? txs.SettlementAmount : null,
                TransactionStatus = txs != null ? txs.TransactionStatus : null,
                TxnInvoiceNumber = txs != null ? txs.InvoiceNumber : null,
                Payamt = ata.Payamt,
                CheckNo = ata.CheckNo,
                AcctModified = ata.Modified,
                AcctCreatedate = ata.Createdate,
                AcctAId = ata.AId,
                UserFirstName = u.FirstName,
                UserLastName = u.LastName,
                UserName = u.UserName,
                RegistrationTs = at.RegistrationTs,
                AgegroupName = null,
                TeamName = null,
                ClubCustomerName = null,
            }).ToListAsync(cancellationToken);

        // ── Team branch: Registration_Accounting.teamID IS NOT NULL ──
        var teamLines = await (
            from ja in _context.RegistrationAccounting.AsNoTracking()
            where ja.Active == true && ja.TeamId != null
            from r in _context.Registrations.AsNoTracking().Where(x => x.RegistrationId == ja.RegistrationId)
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == ja.TeamId)
            from tClub in _context.Customers.AsNoTracking().Where(x => x.CustomerId == t.CustomerId).DefaultIfEmpty()
            from ru in _context.AspNetUsers.AsNoTracking().Where(x => x.Id == r.UserId).DefaultIfEmpty()
            from pm in _context.AccountingPaymentMethods.AsNoTracking().Where(p => p.PaymentMethodId == ja.PaymentMethodId).DefaultIfEmpty()
            from txs in _context.Txs.AsNoTracking().Where(tx =>
                    tx.TransactionId == ja.AdnTransactionId
                    && tx.TransactionStatus != "Declined" && tx.TransactionStatus != "Voided")
                .DefaultIfEmpty()
            from mjs in _context.MonthlyJobStats.AsNoTracking().Where(m =>
                    m.JobId == j.JobId && m.Year == settlementYear && m.Month == settlementMonth)
                .DefaultIfEmpty()
            where
                // Team effective month = settlement-text-month when settled, else ja.Createdate.
                ((txs != null && txs.SettlementDateTime != null
                    && txs.SettlementDateTime.Substring(7, 4) == yearStr
                    && txs.SettlementDateTime.Substring(3, 3) == monthAbbr)
                 || ((txs == null || txs.SettlementDateTime == null)
                    && ja.Createdate >= monthStart && ja.Createdate < monthEnd))
                && (ja.PaymentMethodId != ccPaymentId || (txs != null && txs.InvoiceNumber != null))
            select new InvoiceLineRawDto
            {
                JobId = j.JobId,
                CustomerName = c.CustomerName,
                JobName = j.JobName,
                JobTypeId = j.JobTypeId,
                PerPlayerCharge = j.PerPlayerCharge,
                PerTeamCharge = j.PerTeamCharge,
                ProcessingFeePercent = j.ProcessingFeePercent,
                CountActivePlayersToDate = mjs != null ? mjs.CountActivePlayersToDate : null,
                CountActivePlayersToDateLastMonth = mjs != null ? mjs.CountActivePlayersToDateLastMonth : null,
                CountNewPlayersThisMonth = mjs != null ? mjs.CountNewPlayersThisMonth : null,
                CountActiveTeamsToDate = mjs != null ? mjs.CountActiveTeamsToDate : null,
                CountActiveTeamsToDateLastMonth = mjs != null ? mjs.CountActiveTeamsToDateLastMonth : null,
                CountNewTeamsThisMonth = mjs != null ? mjs.CountNewTeamsThisMonth : null,
                IsTeam = true,
                PaymentMethodName = pm != null ? pm.PaymentMethod : null,
                PaymentMethodId = ja.PaymentMethodId,
                SettlementDateTimeText = txs != null ? txs.SettlementDateTime : null,
                SettlementAmountText = txs != null ? txs.SettlementAmount : null,
                TransactionStatus = txs != null ? txs.TransactionStatus : null,
                TxnInvoiceNumber = txs != null ? txs.InvoiceNumber : null,
                Payamt = ja.Payamt,
                CheckNo = ja.CheckNo,
                AcctModified = ja.Modified,
                AcctCreatedate = ja.Createdate,
                AcctAId = ja.AId,
                UserFirstName = ru != null ? ru.FirstName : null,
                UserLastName = ru != null ? ru.LastName : null,
                UserName = ru != null ? ru.UserName : null,
                RegistrationTs = r.RegistrationTs,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                ClubCustomerName = tClub != null ? tClub.CustomerName : null,
            }).ToListAsync(cancellationToken);

        return playerLines.Concat(teamLines).ToList();
    }

    public async Task<List<FeeYtdRowDto>> GetFeeYtdRowsAsync(
        DateTime asOfLocal,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for adn.tsicFeesYTDAndLastYear. Window = months 1..maxMonth for BOTH
        // this year (maxYear) and last year (minYear), where max = the last completed month — an
        // apples-to-apples YTD comparison. Per-row fee = NewPlayers×perPlayerCharge +
        // NewTeams×perTeamCharge. The proc's isnumeric(Jobs.year)=1 guard (drop jobs whose text
        // year isn't a number) is applied in C# — ISNUMERIC has no portable LINQ translation.
        var prev = asOfLocal.AddMonths(-1);
        var maxMonth = prev.Month;
        var maxYear = prev.Year;
        var minYear = maxYear - 1;

        var rows = await (
            from j in _context.Jobs.AsNoTracking()
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            join mjs in _context.MonthlyJobStats.AsNoTracking() on j.JobId equals mjs.JobId
            where mjs.Year >= minYear && mjs.Year <= maxYear && mjs.Month >= 1 && mjs.Month <= maxMonth
            select new
            {
                StatYear = mjs.Year,
                StatMonth = mjs.Month,
                c.CustomerName,
                j.JobName,
                JobYear = j.Year,
                Fees = ((mjs.CountNewPlayersThisMonth ?? 0) * (j.PerPlayerCharge ?? 0m))
                     + ((mjs.CountNewTeamsThisMonth ?? 0) * (j.PerTeamCharge ?? 0m)),
            }).ToListAsync(cancellationToken);

        return rows
            .Where(r => int.TryParse(r.JobYear, out _))
            .Select(r => new FeeYtdRowDto
            {
                Year = r.StatYear,
                Month = r.StatMonth,
                CustomerName = r.CustomerName ?? string.Empty,
                JobName = r.JobName ?? string.Empty,
                TsicFees = r.Fees,
            })
            .ToList();
    }

    public async Task<List<PlayerStatsE120RowDto>> GetPlayerStatsE120RowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.PlayerStats_E120. Active Players in the job (NO team-active
        // filter — the proc has none), joined role/user/team/agegroup; the four combine stats ride
        // straight off Registrations (null until tested → blank entry-form cells).
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player"
            orderby t.Agegroup.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new PlayerStatsE120RowDto
            {
                RegistrationId = r.RegistrationId,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                UniformNo = r.UniformNo,
                Fastestshot = r.Fastestshot,
                FiveTenFive = r.FiveTenFive,
                Fourtyyarddash = r.Fourtyyarddash,
                Threehundredshuttle = r.Threehundredshuttle,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<AmericanSelectEvaluationRowDto>> GetAmericanSelectEvaluationRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.AmericanSelectPlayerData. Active tryout Players (team name
        // contains "Tryout"), INNER-joined to Families (mom contact → familyless registrants drop).
        // City is the PLAYER's city (u.City). Tryout match is CI under the DB's default collation.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            from f in _context.Families.AsNoTracking().Where(x => x.FamilyUserId == r.FamilyUserId)
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player"
                && t.TeamName != null && t.TeamName.Contains("Tryout")
            orderby t.Agegroup.AgegroupName, u.LastName, u.FirstName
            select new AmericanSelectEvaluationRowDto
            {
                RegistrationId = r.RegistrationId,
                JobName = j.JobName,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                UniformNo = r.UniformNo,
                GradYear = r.GradYear,
                Position = r.Position,
                FirstName = u.FirstName,
                LastName = u.LastName,
                ClubTeamName = r.ClubTeamName,
                SchoolName = r.SchoolName,
                City = u.City,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomCellphone = f.MomCellphone,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<JobPlayersExcelRowDto>> GetJobPlayersExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the reporting_migrate.*JobPlayers*_Excel and camp_* procs. They INNER-join the
        // player user, the family user, Families, the assigned team and that team's division, so
        // those rows drop here too (DivId != null stands in for the divisions join; league and
        // agegroup are non-null FKs). Club name falls back to the team's club rep, a LEFT join.
        // Dates keep their time part here; the procs' convert(date, …) is applied by the renderer.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player"
                && t.DivId != null
            orderby t.Agegroup.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new JobPlayersExcelRowDto
            {
                RegistrationId = r.RegistrationId,
                RegistrationTs = r.RegistrationTs,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Dob = u.Dob,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomCellphone = f.MomCellphone,
                MomEmail = f.MomEmail,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadCellphone = f.DadCellphone,
                DadEmail = f.DadEmail,
                AgegroupName = t.Agegroup.AgegroupName,
                ClubName = r.ClubTeamName ?? t.ClubrepRegistration!.ClubName,
                ClubTeamName = r.ClubTeamName,
                TeamName = t.TeamName,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
                GradYear = r.GradYear,
                SchoolName = r.SchoolName,
                SchoolGrade = r.SchoolGrade,
                Position = r.Position,
                SportAssnId = r.SportAssnId,
                SportAssnIdExpDate = r.SportAssnIdexpDate,
                JerseySize = r.JerseySize,
                ShortsSize = r.ShortsSize,
                Kilt = r.Kilt,
                TShirt = r.TShirt,
                Sweatshirt = r.Sweatshirt,
                Reversible = r.Reversible,
                Gloves = r.Gloves,
                Gpa = r.Gpa,
                Sat = r.Sat,
                Act = r.Act,
                HeightInches = r.HeightInches,
                WeightLbs = r.WeightLbs,
                UniformNo = r.UniformNo,
                StreetAddress = uF.StreetAddress,
                City = uF.City,
                State = uF.State,
                PostalCode = uF.PostalCode,
                Email = u.Email,
                Cellphone = u.Cellphone,
                SpecialRequests = r.SpecialRequests,
                MedicalNote = r.MedicalNote,
                Shoes = r.Shoes,
                RoommatePref = r.RoommatePref,
                DayGroup = r.DayGroup,
                NightGroup = r.NightGroup,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<JobStaffExcelRowDto>> GetJobStaffExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.Get_Staff_ForExcelExport. No bActive filter: the
        // sheet shows the flag and sorts on it. Team and agegroup come through the optional
        // AssignedTeam navigation, i.e. LEFT joins, so unassigned staff still list.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.JobId == jobId && (roles.Name == "Staff" || roles.Name == "Referee")
            orderby r.BActive, u.LastName, u.FirstName
            select new JobStaffExcelRowDto
            {
                RegistrationId = r.RegistrationId,
                Active = r.BActive,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                UserName = u.UserName,
                AgegroupName = r.AssignedTeam!.Agegroup.AgegroupName,
                TeamName = r.AssignedTeam!.TeamName,
                Cellphone = u.Cellphone,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<PlayerTransactionExcelRowDto>> GetPlayerTransactionExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.Get_Player_Transactions_ForExcelExport. The proc
        // INNER-joins the family user and Families though it selects nothing from them, so those
        // joins are kept as filters. No divisions join here, unlike the player-data family.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join ra in _context.RegistrationAccounting.AsNoTracking() on (Guid?)r.RegistrationId equals ra.RegistrationId
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player" && ra.Active == true
            orderby u.LastName, u.FirstName, ra.Createdate
            select new PlayerTransactionExcelRowDto
            {
                RegistrationId = r.RegistrationId,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Dob = u.Dob,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
                PaymentMethod = ra.PaymentMethod.PaymentMethod,
                AccountingId = ra.AId,
                DueAmount = ra.Dueamt,
                PayAmount = ra.Payamt,
                AdnCc4 = ra.AdnCc4,
                AdnCcExpDate = ra.AdnCcexpDate,
                AdnInvoiceNo = ra.AdnInvoiceNo,
                AdnTransactionId = ra.AdnTransactionId,
                CheckNo = ra.CheckNo,
                Comment = ra.Comment,
                CreateDate = ra.Createdate,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<DiscountedPlayerExcelRowDto>> GetDiscountedPlayerExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.Get_DiscountedPlayers. Families is reached through
        // the family user, as in the proc. The discount code is a LEFT join (optional navigation):
        // a discount entered by hand has no code row. fee_discount is non-null, so the proc's
        // "not null and != 0" reduces to != 0.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join f in _context.Families.AsNoTracking() on uF.Id equals f.FamilyUserId
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player" && r.FeeDiscount != 0
            orderby u.LastName, u.FirstName
            select new DiscountedPlayerExcelRowDto
            {
                LastName = u.LastName,
                FirstName = u.FirstName,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                MomFirstName = f.MomFirstName,
                MomEmail = f.MomEmail,
                MomCellphone = f.MomCellphone,
                MomLastName = f.MomLastName,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadEmail = f.DadEmail,
                DadCellphone = f.DadCellphone,
                FeeDiscount = r.FeeDiscount,
                CodeName = r.DiscountCode!.CodeName,
                CodeActive = (bool?)r.DiscountCode!.Active,
                CodeStartDate = (DateTime?)r.DiscountCode!.CodeStartDate,
                CodeEndDate = (DateTime?)r.DiscountCode!.CodeEndDate,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<PlayerStatsParisiExcelRowDto>> GetPlayerStatsParisiExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.PlayerStats_ParisiResults. The division is an
        // explicit INNER join because the proc filters on its name; SQL's "!= 'Unassigned'" also
        // drops a null name, hence the explicit null test.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join d in _context.Divisions.AsNoTracking() on t.DivId equals d.DivId
            where r.JobId == jobId && roles.Name == "Player" && r.BActive == true
                && d.DivName != null && d.DivName != "Unassigned"
            orderby t.Agegroup.AgegroupName, u.LastName, u.FirstName
            select new PlayerStatsParisiExcelRowDto
            {
                AgegroupName = t.Agegroup.AgegroupName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                TeamName = t.TeamName,
                UniformNo = r.UniformNo,
                SchoolName = r.SchoolName,
                ClubName = r.ClubName,
                FastestShot = r.Fastestshot,
                FiveTenFive = r.FiveTenFive,
                FortyYardDash = r.Fourtyyarddash,
                ThreeHundredShuttle = r.Threehundredshuttle,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<MobileJobUserExcelRowDto>> GetMobileJobUserExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.UsageByJobAndRegistrant. The proc's LEFT join to
        // the assigned team selects and filters nothing, so it is not carried over.
        return await (
            from dr in _context.DeviceRegistrationIds.AsNoTracking()
            join d in _context.Devices.AsNoTracking() on dr.DeviceId equals d.Id
            join r in _context.Registrations.AsNoTracking() on dr.RegistrationId equals r.RegistrationId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            where r.JobId == jobId
            orderby c.CustomerName, j.JobName, roles.Name, u.LastName, u.FirstName
            select new MobileJobUserExcelRowDto
            {
                CustomerName = c.CustomerName,
                JobName = j.JobName,
                Role = roles.Name,
                LastName = u.LastName,
                FirstName = u.FirstName,
                DeviceType = d.Type,
                Modified = dr.Modified,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<LeagueTeamExcelRowDto>> GetLeagueTeamExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.League_Teams. The division and the club rep's
        // registration are INNER joins on nullable keys, so teams without either drop; the home
        // field is a LEFT join (optional navigation).
        return await (
            from t in _context.Teams.AsNoTracking()
            join d in _context.Divisions.AsNoTracking() on t.DivId equals (Guid?)d.DivId
            join r in _context.Registrations.AsNoTracking() on t.ClubrepRegistrationid equals (Guid?)r.RegistrationId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where t.JobId == jobId && t.Active == true && roles.Name == "Club Rep" && r.BActive == true
            orderby t.Agegroup.AgegroupName, d.DivName, r.ClubName, t.TeamName
            select new LeagueTeamExcelRowDto
            {
                AgegroupName = t.Agegroup.AgegroupName,
                DivName = d.DivName,
                TeamName = t.TeamName,
                ClubName = r.ClubName,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Email = u.Email,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,
                Cellphone = u.Cellphone,
                TeamComments = t.TeamComments,
                HomeField = t.FieldId1Navigation!.FName,
                TeamId = t.TeamId,
                LevelOfPlay = t.LevelOfPlay,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<CustomerPlayersExcelRowDto>> GetCustomerPlayersExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.Get_Players_ForExcelExport_AllCustomers. The proc
        // resolves the customer from the job, then lists every job of that customer whose
        // ExpiryAdmin is still ahead. No divisions join here, unlike the job-players family, and
        // club_name is the registration's own.
        var customerId = await _context.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (Guid?)j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerId == null)
        {
            return [];
        }

        var now = DateTime.Now;
        return await (
            from r in _context.Registrations.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            where j.CustomerId == customerId && j.ExpiryAdmin > now
                && r.BActive == true && roles.Name == "Player"
            orderby c.CustomerName, j.JobName, u.LastName, u.FirstName, t.Agegroup.AgegroupName, t.TeamName
            select new CustomerPlayersExcelRowDto
            {
                CustomerName = c.CustomerName,
                JobName = j.JobName,
                RegistrationId = r.RegistrationId,
                RegistrationTs = r.RegistrationTs,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Dob = u.Dob,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomCellphone = f.MomCellphone,
                MomEmail = f.MomEmail,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadCellphone = f.DadCellphone,
                DadEmail = f.DadEmail,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                ClubName = r.ClubName,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
                GradYear = r.GradYear,
                SchoolName = r.SchoolName,
                SchoolGrade = r.SchoolGrade,
                Position = r.Position,
                SportAssnId = r.SportAssnId,
                SportAssnIdExpDate = r.SportAssnIdexpDate,
                JerseySize = r.JerseySize,
                ShortsSize = r.ShortsSize,
                Kilt = r.Kilt,
                TShirt = r.TShirt,
                Sweatshirt = r.Sweatshirt,
                Reversible = r.Reversible,
                Gloves = r.Gloves,
                Gpa = r.Gpa,
                Sat = r.Sat,
                Act = r.Act,
                HeightInches = r.HeightInches,
                WeightLbs = r.WeightLbs,
                UniformNo = r.UniformNo,
                StreetAddress = uF.StreetAddress,
                City = uF.City,
                State = uF.State,
                PostalCode = uF.PostalCode,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<TeamFieldDistributionExcelRowDto>> GetTeamFieldDistributionExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.TeamsFieldDistribution. A game joins to both of its
        // teams (t.teamID in (T1_ID, T2_ID)); field, club rep registration and division are
        // INNER joins, so games or teams missing any of them drop. The proc groups and sorts on
        // the day as MM/DD/YYYY text: grouping on the date is the same grouping, and month, day,
        // year is the same order as that text.
        return await (
            from s in _context.Schedule.AsNoTracking()
            join f in _context.Fields.AsNoTracking() on s.FieldId equals (Guid?)f.FieldId
            from t in _context.Teams.AsNoTracking().Where(t => t.TeamId == s.T1Id || t.TeamId == s.T2Id)
            join r in _context.Registrations.AsNoTracking() on t.ClubrepRegistrationid equals (Guid?)r.RegistrationId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where s.JobId == jobId && t.DivId != null
            group s by new { r.ClubName, ag.AgegroupName, t.TeamName, f.FName, Day = (DateTime?)s.GDate!.Value.Date } into g
            orderby g.Key.ClubName, g.Key.AgegroupName, g.Key.TeamName,
                g.Key.Day!.Value.Month, g.Key.Day!.Value.Day, g.Key.Day!.Value.Year, g.Key.FName
            select new TeamFieldDistributionExcelRowDto
            {
                ClubName = g.Key.ClubName,
                AgegroupName = g.Key.AgegroupName,
                TeamName = g.Key.TeamName,
                FieldName = g.Key.FName,
                GameDay = g.Key.Day,
                GameCount = g.Count(),
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<TournamentRecruitingExcelRowDto>> GetTournamentRecruitingExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.JobRosters_ExportTournament. The team, its club
        // rep's registration and user, and the division (DivId != null) are INNER joins; the
        // family user and Families are LEFT joins, Families reached through the family user as in
        // the proc. The proc's coalesce() fallbacks are kept here; its text formatting is not.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join rCR in _context.Registrations.AsNoTracking() on t.ClubrepRegistrationid equals (Guid?)rCR.RegistrationId
            join uCR in _context.AspNetUsers.AsNoTracking() on rCR.UserId equals uCR.Id
            join d in _context.Divisions.AsNoTracking() on t.DivId equals (Guid?)d.DivId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uFj in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uFj.Id into uFs
            from uF in uFs.DefaultIfEmpty()
            join fj in _context.Families.AsNoTracking() on uF.Id equals fj.FamilyUserId into fs
            from f in fs.DefaultIfEmpty()
            where r.JobId == jobId && r.BActive == true
                && (roles.Name == "Staff" || roles.Name == "Player")
                && u.FirstName != null
            orderby t.Agegroup.AgegroupName, t.TeamName, roles.Name descending, u.LastName, u.FirstName
            select new TournamentRecruitingExcelRowDto
            {
                Role = roles.Name,
                RegistrationId = r.RegistrationId,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email ?? uF.Email,
                Gender = u.Gender,
                Dob = u.Dob,
                StreetAddress = u.StreetAddress ?? uF.StreetAddress,
                City = u.City ?? uF.City,
                State = u.State ?? uF.State,
                PostalCode = u.PostalCode ?? uF.PostalCode,
                Cellphone = u.Cellphone ?? f.MomCellphone,
                SportAssnId = r.SportAssnId,
                AgegroupName = t.Agegroup.AgegroupName,
                DivName = d.DivName,
                ClubName = rCR.ClubName,
                PlayerClubName = r.ClubName,
                TeamName = t.TeamName,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomEmail = f.MomEmail,
                MomCellphone = f.MomCellphone,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadEmail = f.DadEmail,
                DadCellphone = f.DadCellphone,
                UniformNo = r.UniformNo,
                GradYear = r.GradYear,
                SchoolName = r.SchoolName,
                Position = r.Position,
                ClubTeamName = r.ClubTeamName,
                Gpa = r.Gpa,
                Psat = r.Psat,
                ClassRank = r.ClassRank,
                HeightInches = r.HeightInches,
                WeightLbs = r.WeightLbs,
                Act = r.Act,
                SatMath = r.SatMath,
                SatVerbal = r.SatVerbal,
                SatWriting = r.SatWriting,
                CollegeCommitted = r.BCollegeCommit,
                CollegeCommit = r.CollegeCommit,
                ClubRepFirstName = uCR.FirstName,
                ClubRepLastName = uCR.LastName,
                ClubRepEmail = uCR.Email,
                ClubRepCellphone = uCR.Cellphone,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<RecruitingRosterExcelRowDto>> GetRecruitingRosterExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting_migrate.JobRosters_RecruitingReport_DumpExcel and _Public
        // (same FROM, WHERE and ORDER BY). The club rep's registration and user are LEFT joins;
        // division (DivId != null), Families and the family user are INNER joins and so filter.
        var rows = await (
            from t in _context.Teams.AsNoTracking()
            join rCRj in _context.Registrations.AsNoTracking() on t.ClubrepRegistrationid equals (Guid?)rCRj.RegistrationId into rCRs
            from rCR in rCRs.DefaultIfEmpty()
            join uCRj in _context.AspNetUsers.AsNoTracking() on rCR.UserId equals uCRj.Id into uCRs
            from uCR in uCRs.DefaultIfEmpty()
            join r in _context.Registrations.AsNoTracking() on (Guid?)t.TeamId equals r.AssignedTeamId
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            where t.JobId == jobId && roles.Name == "Player" && t.Active == true && r.BActive == true
                && t.DivId != null
            orderby t.Agegroup.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new RecruitingRosterExcelRowDto
            {
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                LastName = u.LastName,
                FirstName = u.FirstName,
                UniformNo = r.UniformNo,
                Cellphone = u.Cellphone ?? f.MomCellphone,
                Email = u.Email ?? f.MomEmail,
                StreetAddress = uF.StreetAddress,
                City = uF.City,
                State = uF.State,
                PostalCode = uF.PostalCode,
                SchoolName = r.SchoolName,
                GradYear = r.GradYear,
                SatMath = r.SatMath,
                SatVerbal = r.SatVerbal,
                SatWriting = r.SatWriting,
                Act = r.Act,
                Gpa = r.Gpa,
                Position = r.Position,
                ClubName = r.ClubName ?? r.ClubTeamName,
                ClubRepFirstName = uCR.FirstName,
                ClubRepLastName = uCR.LastName,
                ClubRepEmail = uCR.Email,
                ClubRepCellphone = uCR.Cellphone,
                ClubTeamName = r.ClubTeamName,
            }).ToListAsync(cancellationToken);

        return OrderRosterByUniformNumber(rows);
    }

    /// <summary>
    /// The recruiting procs sort each team's players by uniform number read as a number
    /// (<c>ISNUMERIC</c> + <c>CONVERT(int, …)</c> with any '#' removed), non-numeric first, then
    /// last and first name. That conversion is SQL Server-specific, so it is applied here: the
    /// rows arrive ordered agegroup → team → last → first by the database, and each run of rows
    /// for one team is stably re-sorted on the number, leaving name order to break ties.
    /// </summary>
    private static List<RecruitingRosterExcelRowDto> OrderRosterByUniformNumber(List<RecruitingRosterExcelRowDto> rows)
    {
        static bool SameText(string? a, string? b) =>
            a == null || b == null
                ? a == b
                : string.Equals(a.TrimEnd(' '), b.TrimEnd(' '), StringComparison.OrdinalIgnoreCase);

        static int? UniformNumber(string? uniformNo) =>
            int.TryParse(
                uniformNo?.Replace("#", string.Empty),
                NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.InvariantCulture,
                out var n)
                ? n
                : null;

        var ordered = new List<RecruitingRosterExcelRowDto>(rows.Count);
        var start = 0;
        while (start < rows.Count)
        {
            var end = start + 1;
            while (end < rows.Count
                && SameText(rows[end].AgegroupName, rows[start].AgegroupName)
                && SameText(rows[end].TeamName, rows[start].TeamName))
            {
                end++;
            }

            // OrderBy is stable; a null number (non-numeric) sorts before every number, as in SQL.
            ordered.AddRange(rows.GetRange(start, end - start).OrderBy(r => UniformNumber(r.UniformNo)));
            start = end;
        }

        return ordered;
    }

    public async Task<List<ExportHistoryExcelRowDto>> GetExportHistoryExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.ExportReportsHistory. SQL's "!= 'SuperUser'" also drops a
        // null role name, hence the explicit null test.
        return await (
            from hx in _context.JobReportExportHistory.AsNoTracking()
            join r in _context.Registrations.AsNoTracking() on hx.RegistrationId equals r.RegistrationId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            where r.JobId == jobId && roles.Name != null && roles.Name != "SuperUser"
            orderby hx.ExportDate descending
            select new ExportHistoryExcelRowDto
            {
                JobName = j.JobName,
                Export = hx.StoredProcedureName ?? hx.ReportName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                ExportDate = hx.ExportDate,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<PlayerParentMailingExcelRowDto>> GetPlayerParentMailingExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.PlayerParentMailingData. No role filter in the proc.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join uF in _context.AspNetUsers.AsNoTracking() on f.FamilyUserId equals uF.Id
            where r.JobId == jobId && r.BActive == true
            orderby f.MomLastName
            select new PlayerParentMailingExcelRowDto
            {
                MomLastName = f.MomLastName,
                MomFirstName = f.MomFirstName,
                DadLastName = f.DadLastName,
                DadFirstName = f.DadFirstName,
                StreetAddress = uF.StreetAddress,
                City = uF.City,
                State = uF.State,
                PostalCode = uF.PostalCode,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<StaffApparelExcelRowDto>> GetStaffApparelExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.StaffExport.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.JobId == jobId && roles.Name == "Staff" && r.BActive == true
            orderby u.LastName, u.FirstName
            select new StaffApparelExcelRowDto
            {
                LastName = u.LastName,
                FirstName = u.FirstName,
                Assignment = r.Assignment,
                Cellphone = u.Cellphone,
                Email = u.Email,
                JerseySize = r.JerseySize,
                ShortsSize = r.ShortsSize,
                Sweatpants = r.Sweatpants,
                Shoes = r.Shoes,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<StepsCoachExcelRowDto>> GetStepsCoachExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.StepsCoaches_Export. No bActive filter: the sheet shows the
        // flag. The count is the proc's correlated subquery: the same user's Staff registrations
        // on this job, active or not.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.JobId == jobId && roles.Name == "Unassigned Adult"
            orderby u.LastName, u.FirstName
            select new StepsCoachExcelRowDto
            {
                Active = r.BActive,
                WaiverSigned = r.BWaiverSigned1,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Email = u.Email,
                Cellphone = u.Cellphone,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,
                JerseySize = r.JerseySize,
                ShortsSize = r.ShortsSize,
                Sweatshirt = r.Sweatshirt,
                Sweatpants = r.Sweatpants,
                SpecialRequests = r.SpecialRequests,
                TeamsCoaching = (
                    from a in _context.Registrations
                    join b in _context.AspNetRoles on a.RoleId equals b.Id
                    where a.JobId == r.JobId && a.UserId == r.UserId && b.Name == "Staff"
                    select a).Count(),
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<MaxExposureClubCoachExcelRowDto>> GetMaxExposureClubCoachExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for utility.MaxExposure_ClubCoaches. No bActive filter in the proc.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            where r.JobId == jobId && roles.Name == "Player"
            orderby u.LastName, u.FirstName
            select new MaxExposureClubCoachExcelRowDto
            {
                LastName = u.LastName,
                FirstName = u.FirstName,
                Dob = u.Dob,
                SchoolName = r.SchoolName,
                GradYear = r.GradYear,
                Cellphone = u.Cellphone,
                Email = u.Email,
                ClubCoach = r.ClubCoach,
                ClubCoachEmail = r.ClubCoachEmail,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomCellphone = f.MomCellphone,
                MomEmail = f.MomEmail,
                DadFirstName = f.DadFirstName,
                DadCellphone = f.DadCellphone,
                DadEmail = f.DadEmail,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<SiblingExcelRowDto>> GetSiblingExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for utility.sibling_report. The inner set is families with more than one
        // active Player registration on the job; the outer rows are every registration of those
        // families on the job, whatever its role or active flag, as in the proc.
        var siblingFamilies =
            from r in _context.Registrations.AsNoTracking()
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player" && r.FamilyUserId != null
            group r by r.FamilyUserId into g
            where g.Count() > 1
            select g.Key;

        return await (
            from r in _context.Registrations.AsNoTracking()
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.JobId == jobId && siblingFamilies.Contains(r.FamilyUserId)
            orderby u.LastName, u.FirstName
            select new SiblingExcelRowDto
            {
                LastName = u.LastName,
                FirstName = u.FirstName,
                RegistrationId = r.RegistrationId,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<JobClubRepContactExcelRowDto>> GetJobClubRepContactExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.JobClubRepContacts.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where j.JobId == jobId && roles.Name == "Club Rep" && r.BActive == true
            orderby j.Year descending, j.JobName, u.LastName, u.FirstName
            select new JobClubRepContactExcelRowDto
            {
                Year = j.Year,
                JobName = j.JobName,
                ClubName = r.ClubName,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Email = u.Email,
                Cellphone = u.Cellphone,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<CampCheckinExcelRowDto>> GetCampCheckinExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.CathyCampCheckinWithVacAndMedform. The division is an
        // INNER join used only as a filter (DivId != null). "not like '%WAITLIST%'" is
        // case-insensitive under the SQL Server collation and drops a null name; upper-casing
        // the name keeps both behaviours on any provider.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where r.JobId == jobId && r.BActive == true && t.DivId != null
                && ag.AgegroupName != null && !ag.AgegroupName.ToUpper().Contains("WAITLIST")
            orderby t.TeamName, u.LastName
            select new CampCheckinExcelRowDto
            {
                LeagueName = t.League.LeagueName,
                TeamName = t.TeamName,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Position = r.Position,
                UploadedVaccineCard = r.BUploadedVaccineCard,
                UploadedMedForm = r.BUploadedMedForm,
                OwedTotal = r.OwedTotal,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomCellphone = f.MomCellphone,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadCellphone = f.DadCellphone,
            }).ToListAsync(cancellationToken);
    }

    public async Task<JobPushTeamSummaryDto> GetJobPushTeamSummaryAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.JobPushTeamResultsSummary, which returns two result sets.
        // The total counts device-team rows whatever the device; the breakdown INNER-joins the
        // device as the proc does, and LEFT-joins the club rep (optional navigation).
        var total = await (
            from dt in _context.DeviceTeams.AsNoTracking()
            join t in _context.Teams.AsNoTracking() on dt.TeamId equals t.TeamId
            where t.JobId == jobId
            select dt).CountAsync(cancellationToken);

        var teams = await (
            from dt in _context.DeviceTeams.AsNoTracking()
            join d in _context.Devices.AsNoTracking() on dt.DeviceId equals d.Id
            join t in _context.Teams.AsNoTracking() on dt.TeamId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where t.JobId == jobId
            group dt by new { t.ClubrepRegistration!.ClubName, ag.AgegroupName, t.TeamName } into g
            orderby g.Key.ClubName, g.Key.AgegroupName, g.Key.TeamName
            select new JobPushTeamCountExcelRowDto
            {
                ClubName = g.Key.ClubName,
                AgegroupName = g.Key.AgegroupName,
                TeamName = g.Key.TeamName,
                FavoriteCount = g.Count(),
            }).ToListAsync(cancellationToken);

        return new JobPushTeamSummaryDto { TotalFavorites = total, Teams = teams };
    }

    public async Task<List<TeamContactExcelRowDto>> GetTeamContactExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.TournamentExportClubrepsAndCoachesEmail: a UNION of club
        // reps and staff, de-duplicated. Preserved from the proc: its club-rep half joins the
        // registration id to the TEAM id (r.RegistrationId = t.teamId), which matches nothing, so
        // in practice only staff list.
        var clubReps =
            from r in _context.Registrations.AsNoTracking()
            join t in _context.Teams.AsNoTracking() on r.RegistrationId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            where r.JobId == jobId && r.BActive == true && roles.Name == "Club Rep"
            select new { ag.AgegroupName, r.ClubName, t.TeamName, Role = roles.Name, u.LastName, u.FirstName, u.Email };

        var staff =
            from r in _context.Registrations.AsNoTracking()
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            join rCR in _context.Registrations.AsNoTracking() on t.ClubrepRegistrationid equals (Guid?)rCR.RegistrationId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            where r.JobId == jobId && r.BActive == true && roles.Name == "Staff"
            select new { ag.AgegroupName, rCR.ClubName, t.TeamName, Role = roles.Name, u.LastName, u.FirstName, u.Email };

        return await clubReps.Union(staff)
            .OrderBy(x => x.AgegroupName).ThenBy(x => x.ClubName).ThenBy(x => x.TeamName)
            .ThenBy(x => x.Role).ThenBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Select(x => new TeamContactExcelRowDto
            {
                AgegroupName = x.AgegroupName,
                ClubName = x.ClubName,
                TeamName = x.TeamName,
                Role = x.Role,
                LastName = x.LastName,
                FirstName = x.FirstName,
                Email = x.Email,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<CustomerPlayerContactExcelRowDto>> GetCustomerPlayerContactExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.TLCContactsHx: SELECT DISTINCT over every job of the
        // calling job's customer. Division (DivId != null), family user and Families are INNER
        // joins and so filter.
        var customerId = await _context.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (Guid?)j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerId == null)
        {
            return [];
        }

        return await (
            from t in _context.Teams.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on t.JobId equals j.JobId
            join r in _context.Registrations.AsNoTracking() on (Guid?)t.TeamId equals r.AssignedTeamId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join f in _context.Families.AsNoTracking() on uF.Id equals f.FamilyUserId
            where r.BActive == true && j.CustomerId == customerId && roles.Name == "Player" && t.DivId != null
            select new CustomerPlayerContactExcelRowDto
            {
                LastName = u.LastName,
                FirstName = u.FirstName,
                Email = u.Email,
                MomEmail = f.MomEmail,
                DadEmail = f.DadEmail,
                Cellphone = u.Cellphone,
                MomCellphone = f.MomCellphone,
                DadCellphone = f.DadCellphone,
            })
            .Distinct()
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<CampBreakoutPaidExcelRowDto>> GetCampBreakoutPaidExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.CathyCampBreakoutPerJob_AmtPaid: SELECT DISTINCT over the
        // camp-or-clinic jobs of the calling job's customer in the calling job's year. No bActive
        // filter: the sheet shows the flag.
        var scope = await _context.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.CustomerId, j.Year })
            .FirstOrDefaultAsync(cancellationToken);
        if (scope?.Year == null)
        {
            return [];
        }

        return await (
            from j in _context.Jobs.AsNoTracking()
            join r in _context.Registrations.AsNoTracking() on j.JobId equals r.JobId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where j.CustomerId == scope.CustomerId && j.Year == scope.Year
                && j.JobType.JobTypeDesc == "player registration for camp or clinic"
                && roles.Name == "Player"
            select new CampBreakoutPaidExcelRowDto
            {
                JobName = j.JobName,
                AgegroupName = ag.AgegroupName,
                TeamName = t.TeamName,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Active = r.BActive,
                ClubTeamName = r.ClubTeamName,
                GradYear = r.GradYear,
                FeeBase = r.FeeBase,
                FeeTotal = r.FeeTotal,
                FeeDiscount = r.FeeDiscount,
                FeeProcessing = r.FeeProcessing,
                PaidTotal = r.PaidTotal,
                OwedTotal = r.OwedTotal,
            })
            .Distinct()
            .OrderBy(x => x.JobName).ThenBy(x => x.AgegroupName).ThenBy(x => x.TeamName)
            .ThenBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<CustomerTeamCountDto>> GetCustomerTeamCountsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the detail level of
        // reporting.AmericanSelect_CustomerJobRegistrationsByJobAgegroupDivTeam. The proc's
        // WITH ROLLUP subtotals are SQL Server syntax and are produced by the renderer from these
        // rows. The player's user and the division (DivId != null) are INNER joins and so filter.
        var customerId = await _context.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (Guid?)j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerId == null)
        {
            return [];
        }

        return await (
            from r in _context.Registrations.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where j.CustomerId == customerId && r.BActive == true && roles.Name == "Player" && t.DivId != null
            group r by new { j.JobName, j.Year, ag.AgegroupName, t.TeamName } into g
            orderby g.Key.JobName, g.Key.Year, g.Key.AgegroupName, g.Key.TeamName
            select new CustomerTeamCountDto
            {
                JobName = g.Key.JobName,
                Year = g.Key.Year,
                AgegroupName = g.Key.AgegroupName,
                TeamName = g.Key.TeamName,
                PlayerCount = g.Count(),
            }).ToListAsync(cancellationToken);
    }

    // The customer and the camp years the MaxExposure counts proc is hard-wired to.
    private static readonly Guid MaxExposureCustomerId = new("502f0c58-c643-43da-9bfe-00e140f1a19b");

    private static readonly string[] MaxExposureCountYears =
    [
        "2010", "2011", "2012", "2013", "2014", "2015", "2016", "2017", "2018",
        "2019", "2020", "2021", "2022", "2023", "2024", "2025", "2026",
    ];

    public async Task<List<MaxExposureTeamCountExcelRowDto>> GetMaxExposureTeamCountExcelRowsAsync(
        CancellationToken cancellationToken = default)
    {
        // EF replacement for utility.MaxExposure_CountsAtYear_ByTeam. Preserved from the proc: it
        // ignores its job, reads one fixed customer and a fixed list of years, and compares
        // years "as of today": a registration counts when its timestamp, moved forward to the
        // current year, is already in the past. The customer name is the job name up to its colon.
        var now = DateTime.Now;
        var thisYear = now.Year;
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            where j.CustomerId == MaxExposureCustomerId && r.BActive == true && roles.Name == "Player"
                && MaxExposureCountYears.Contains(j.Year)
                && r.RegistrationTs.AddYears(thisYear - Convert.ToInt32(j.Year)) < now
            group r by new
            {
                j.Year,
                CustomerName = j.JobName!.Substring(0, j.JobName.IndexOf(":")),
                t.TeamName,
            }
            into g
            orderby g.Key.CustomerName, g.Key.Year
            select new MaxExposureTeamCountExcelRowDto
            {
                Year = g.Key.Year,
                CustomerName = g.Key.CustomerName,
                TeamName = g.Key.TeamName,
                RegistrantsToDate = g.Count(),
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<ShowcasePlayerExcelRowDto>> GetShowcasePlayerExcelRowsAsync(
        Guid jobId,
        bool activePlayersOnly,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for the three reporting.ThePlayers_PlayerShowCaseExport* procs: the same
        // INNER joins (family user, Families, team, and the division via DivId != null). The two
        // showcase exports keep active Players outside waitlist agegroups; the include-inactive
        // one has no filter beyond the job. The waitlist test is upper-cased so it stays
        // case-insensitive on any provider, and it drops a null agegroup name as SQL does.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join f in _context.Families.AsNoTracking() on r.FamilyUserId equals f.FamilyUserId
            join t in _context.Teams.AsNoTracking() on r.AssignedTeamId equals t.TeamId
            join ag in _context.Agegroups.AsNoTracking() on t.AgegroupId equals ag.AgegroupId
            where r.JobId == jobId && t.DivId != null
                && (!activePlayersOnly
                    || (r.BActive == true && roles.Name == "Player"
                        && ag.AgegroupName != null && !ag.AgegroupName.ToUpper().Contains("WAITLIST")))
            orderby ag.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new ShowcasePlayerExcelRowDto
            {
                RegistrationId = r.RegistrationId,
                RegistrationTs = r.RegistrationTs,
                AgegroupName = ag.AgegroupName,
                TeamName = t.TeamName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                RoleName = roles.Name,
                UniformNo = r.UniformNo,
                Active = r.BActive,
                Cellphone = u.Cellphone,
                Email = u.Email,
                City = uF.City,
                State = uF.State,
                StreetAddress = uF.StreetAddress,
                PostalCode = uF.PostalCode,
                Dob = u.Dob,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomCellphone = f.MomCellphone,
                MomEmail = f.MomEmail,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadCellphone = f.DadCellphone,
                DadEmail = f.DadEmail,
                GradYear = r.GradYear,
                Position = r.Position,
                ClubTeamName = r.ClubTeamName,
                SchoolName = r.SchoolName,
                SportAssnId = r.SportAssnId,
                ShortsSize = r.ShortsSize,
                TShirt = r.TShirt,
                HeightInches = r.HeightInches,
                WeightLbs = r.WeightLbs,
                StrongHand = r.StrongHand,
                ClubCoach = r.ClubCoach,
                ClubCoachEmail = r.ClubCoachEmail,
                SchoolCoach = r.SchoolCoach,
                SchoolCoachEmail = r.SchoolCoachEmail,
                HonorsAcademic = r.HonorsAcademic,
                HonorsAthletic = r.HonorsAthletic,
                Instagram = r.Instagram,
                Twitter = r.Twitter,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<CustomerEndUserExcelRowDto>> GetCustomerEndUserExcelRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.CustomerEndUsersDump: a UNION (so de-duplicated) of each
        // team's active club rep and each team's active rostered registrants, over every job of
        // the calling job's customer. The proc de-duplicates on the date of birth as MM/DD/YYYY
        // text, so the day is taken here and the text is produced by the renderer.
        var customerId = await _context.Jobs.AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (Guid?)j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (customerId == null)
        {
            return [];
        }

        var clubReps =
            from t in _context.Teams.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on t.JobId equals j.JobId
            join r in _context.Registrations.AsNoTracking() on t.ClubrepRegistrationid equals (Guid?)r.RegistrationId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            where r.BActive == true && j.CustomerId == customerId
            select new CustomerEndUserExcelRowDto
            {
                Year = j.Year,
                JobName = j.JobName,
                Role = roles.Name,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Dob = (DateTime?)u.Dob!.Value.Date,
                GradYear = null,
                ClubName = r.ClubName,
                TeamName = null,
                Position = null,
                SchoolName = null,
                Email = u.Email,
                MomEmail = null,
                DadEmail = null,
                Cellphone = u.Cellphone,
                MomCellphone = null,
                DadCellphone = null,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,
            };

        var registrants =
            from t in _context.Teams.AsNoTracking()
            join j in _context.Jobs.AsNoTracking() on t.JobId equals j.JobId
            join r in _context.Registrations.AsNoTracking() on (Guid?)t.TeamId equals r.AssignedTeamId
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join uF in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uF.Id
            join f in _context.Families.AsNoTracking() on uF.Id equals f.FamilyUserId
            where r.BActive == true && j.CustomerId == customerId && t.DivId != null
            select new CustomerEndUserExcelRowDto
            {
                Year = j.Year,
                JobName = j.JobName,
                Role = roles.Name,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Dob = (DateTime?)u.Dob!.Value.Date,
                GradYear = r.GradYear,
                ClubName = r.ClubName,
                TeamName = t.TeamName,
                Position = r.Position,
                SchoolName = r.SchoolName,
                Email = u.Email,
                MomEmail = f.MomEmail,
                DadEmail = f.DadEmail,
                Cellphone = u.Cellphone,
                MomCellphone = f.MomCellphone,
                DadCellphone = f.DadCellphone,
                StreetAddress = uF.StreetAddress,
                City = uF.City,
                State = uF.State,
                PostalCode = uF.PostalCode,
            };

        return await clubReps.Union(registrants)
            .OrderByDescending(x => x.Year).ThenBy(x => x.JobName).ThenBy(x => x.Role)
            .ThenBy(x => x.LastName).ThenBy(x => x.FirstName)
            .ToListAsync(cancellationToken);
    }

    // The job and the customer group the accounting-records proc is hard-wired to: called for
    // this job it reports all of these customers instead of the job's own.
    private static readonly Guid AllStepsAccountingJobId = new("1d7e8de1-ff11-43df-880b-cc99318d1b12");

    private static readonly Guid[] AllStepsAccountingCustomerIds =
    [
        new("f4eaaed0-8eb9-de11-8905-00137250256d"),
        new("150db9a4-0e6d-43d4-a7f8-03dfc740ccd1"),
        new("0d82c667-c09a-dd11-a853-00137250256d"),
        new("122178d9-4bef-4fc7-9cdc-7393efc1966d"),
        new("c75b4c3d-a628-4ad8-b197-435921a5cf03"),
        new("c2416042-7a11-4938-896f-306a13cb6069"),
        new("0ea213ce-d975-4cc7-9312-88562db6d8c5"),
        new("b818d018-8f4b-43a5-98d6-dfd5f52e6e4c"),
        new("1ff58860-b347-4519-8b1b-5b3af3aed8a4"),
    ];

    public async Task<CustomerAccountingRecordsDto> GetCustomerAccountingRecordsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // EF replacement for reporting.Customer_RegistrationAccounting_Records. Two branches, as
        // in the proc: the hard-wired job reports the fixed customer group; any other job reports
        // its own customer. Same joins either way: team is a LEFT join on BOTH the record's team
        // and "this registration is that team's club rep", agegroup hangs off it, and the family
        // user is a LEFT join.
        var allSteps = jobId == AllStepsAccountingJobId;
        var customer = allSteps
            ? null
            : await _context.Jobs.AsNoTracking()
                .Where(j => j.JobId == jobId)
                .Select(j => new { j.CustomerId, j.Customer.CustomerName })
                .FirstOrDefaultAsync(cancellationToken);
        if (!allSteps && customer == null)
        {
            return new CustomerAccountingRecordsDto { AllStepsCustomers = false, CustomerName = null, Rows = [] };
        }

        var customerIds = allSteps ? AllStepsAccountingCustomerIds : [customer!.CustomerId];
        var firstDate = new DateTime(DateTime.Now.Year - 2, 1, 1);

        var rows = await (
            from ra in _context.RegistrationAccounting.AsNoTracking()
            join r in _context.Registrations.AsNoTracking() on ra.RegistrationId equals (Guid?)r.RegistrationId
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            join c in _context.Customers.AsNoTracking() on j.CustomerId equals c.CustomerId
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            from t in _context.Teams.AsNoTracking()
                .Where(t => ra.TeamId == t.TeamId && ra.RegistrationId == t.ClubrepRegistrationid)
                .DefaultIfEmpty()
            join uFj in _context.AspNetUsers.AsNoTracking() on r.FamilyUserId equals uFj.Id into uFs
            from uF in uFs.DefaultIfEmpty()
            where customerIds.Contains(j.CustomerId) && ra.Active == true && ra.Createdate >= firstDate
            orderby ra.Createdate, ra.AId
            select new CustomerAccountingRecordExcelRowDto
            {
                CustomerName = c.CustomerName,
                JobName = j.JobName,
                CreateDate = ra.Createdate,
                PaymentMethod = ra.PaymentMethod.PaymentMethod,
                AdnTransactionId = ra.AdnTransactionId,
                AdnCc4 = ra.AdnCc4,
                AdnCcExpDate = ra.AdnCcexpDate,
                LastName = u.LastName,
                FirstName = u.FirstName,
                Role = roles.Name,
                UserName = u.UserName,
                FamilyUserName = uF.UserName,
                PayAmount = ra.Payamt,
                CardComment = ra.Paymeth,
                Comment = ra.Comment,
                ClubName = r.ClubName,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
            }).ToListAsync(cancellationToken);

        return new CustomerAccountingRecordsDto
        {
            AllStepsCustomers = allSteps,
            CustomerName = customer?.CustomerName,
            Rows = rows,
        };
    }

    // ══════════════════════════════════════
    // Third-Party Roster Export (retired SportsRecruits API replacement)
    // ══════════════════════════════════════

    public async Task<bool> HasCrystalActionEntitlementAsync(
        Guid jobId,
        IReadOnlyCollection<string> roleIds,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action) || roleIds == null || roleIds.Count == 0)
            return false;

        // Crystal-kind rows store the bare endpoint name in Action — exact match,
        // no query-string token parsing (unlike the SP/Bold variants).
        return await _context.JobReports
            .AsNoTracking()
            .AnyAsync(jr => jr.JobId == jobId
                            && jr.Active
                            && jr.Kind == KindCrystalReport
                            && roleIds.Contains(jr.RoleId)
                            && jr.Action == action,
                cancellationToken);
    }

    public async Task<bool> HasCrystalActionEntitlementAnyRoleAsync(
        Guid jobId,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;

        return await _context.JobReports
            .AsNoTracking()
            .AnyAsync(jr => jr.JobId == jobId
                            && jr.Active
                            && jr.Kind == KindCrystalReport
                            && jr.Action == action,
                cancellationToken);
    }

    public async Task<JobReports?> GetCrystalActionRowTrackedAsync(
        Guid jobId,
        string roleId,
        string action,
        CancellationToken cancellationToken = default)
    {
        return await _context.JobReports
            .Where(jr => jr.JobId == jobId
                         && jr.RoleId == roleId
                         && jr.Kind == KindCrystalReport
                         && jr.Action == action)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ThirdPartyRosterContextDto?> GetThirdPartyRosterContextAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Banner context: job name + the agegroups currently opted in. Scope mirrors the
        // legacy API's eligible-agegroup resolution (Job_Leagues → league, Jobs.Season →
        // season), widened to ALL of the job's leagues rather than the legacy
        // FirstOrDefault — multi-league jobs report every flagged agegroup.
        var job = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobName, j.Season })
            .SingleOrDefaultAsync(cancellationToken);
        if (job == null) return null;

        var leagueIds = await _context.JobLeagues
            .AsNoTracking()
            .Where(jl => jl.JobId == jobId)
            .Select(jl => jl.LeagueId)
            .ToListAsync(cancellationToken);

        var allowedNames = leagueIds.Count == 0
            ? new List<string>()
            : await _context.Agegroups
                .AsNoTracking()
                .Where(ag => leagueIds.Contains(ag.LeagueId)
                             && ag.Season == job.Season
                             && ag.BAllowApiRosterAccess == true)
                .OrderBy(ag => ag.AgegroupName)
                .Select(ag => ag.AgegroupName ?? string.Empty)
                .ToListAsync(cancellationToken);

        return new ThirdPartyRosterContextDto
        {
            JobName = job.JobName ?? string.Empty,
            AllowedAgegroupNames = allowedNames,
        };
    }

    public async Task<List<ThirdPartyRosterPlayerDto>> GetThirdPartyRosterPlayersAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Legacy feed scope (ThirdPartyApis/RostersController.GetJobRosterPlayerData):
        // active Player registrations on active teams, WAITLIST/DROPPED agegroups
        // excluded — PLUS the per-agegroup opt-in gate the legacy live endpoint never
        // actually enforced. That flag is the whole point of the in-house replacement:
        // a player row can only leave the building from an explicitly flagged agegroup.
        // No phone numbers by design (the legacy feed shipped none).
        return await (
            from r in _context.Registrations.AsNoTracking()
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
            from f in _context.Families.AsNoTracking().Where(x => x.FamilyUserId == r.FamilyUserId).DefaultIfEmpty()
            where r.JobId == jobId
                && r.BActive == true
                && r.RoleId == RoleConstants.Player
                && t.Active == true
                && t.Agegroup.BAllowApiRosterAccess == true
                && (t.Agegroup.AgegroupName == null
                    || (!t.Agegroup.AgegroupName.Contains("WAITLIST")
                        && !t.Agegroup.AgegroupName.Contains("DROPPED")))
            orderby t.Agegroup.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new ThirdPartyRosterPlayerDto
            {
                RegistrationId = r.RegistrationId,
                RegistrationTimestamp = r.RegistrationTs,
                LastModifiedTimestamp = r.Modified,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                TeamClubName = t.ClubrepRegistration != null ? t.ClubrepRegistration.ClubName : null,
                TeamName = t.TeamName,
                AgegroupName = t.Agegroup.AgegroupName,
                PoolName = t.Div != null ? t.Div.DivName : null,
                MomFirstName = f != null ? f.MomFirstName : null,
                MomLastName = f != null ? f.MomLastName : null,
                MomEmail = f != null ? f.MomEmail : null,
                DadFirstName = f != null ? f.DadFirstName : null,
                DadLastName = f != null ? f.DadLastName : null,
                DadEmail = f != null ? f.DadEmail : null,
            }).ToListAsync(cancellationToken);
    }

    public async Task<List<ThirdPartyScheduleGameDto>> GetThirdPartyScheduleGamesAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Exact legacy feed shape (ThirdPartyApis/SchedulesController.GetJobSchedule):
        // whole-job schedule ordered by GDate then FName, the same 13 columns, no
        // agegroup gate — schedules are public information on the site.
        return await _context.Schedule
            .AsNoTracking()
            .Where(s => s.JobId == jobId)
            .OrderBy(s => s.GDate)
            .ThenBy(s => s.FName)
            .Select(s => new ThirdPartyScheduleGameDto
            {
                Gid = s.Gid,
                GDate = s.GDate,
                AgegroupName = s.AgegroupName,
                DivName = s.DivName,
                FName = s.FName,
                T1Type = s.T1Type,
                T1No = s.T1No,
                T1Name = s.T1Name,
                T1Score = s.T1Score,
                T2Type = s.T2Type,
                T2No = s.T2No,
                T2Name = s.T2Name,
                T2Score = s.T2Score,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Registrations>> GetRegistrationsForFormFieldReadAsync(
        List<Guid> registrationIds,
        CancellationToken cancellationToken = default)
    {
        if (registrationIds == null || registrationIds.Count == 0)
            return new List<Registrations>();

        // Full entities (untracked): dynamic form-field values are read off the entity
        // by reflection (FormValueMapper), so a column projection can't serve this.
        return await _context.Registrations
            .AsNoTracking()
            .Where(r => registrationIds.Contains(r.RegistrationId))
            .ToListAsync(cancellationToken);
    }
}
