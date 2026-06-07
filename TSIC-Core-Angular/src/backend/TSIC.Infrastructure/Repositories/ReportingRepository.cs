using System.Data;
using System.Data.Common;
using System.Globalization;
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
                LeagueName = l.LeagueName,
                FieldName = f.FName,
                Color = ag.Color,
                GDate = s.GDate,

                T1Id = s.T1Id,
                T1Name = s.T1Name,
                T1Type = s.T1Type,
                T1Ann = s.T1Ann,
                T1Score = s.T1Score,

                T2Id = s.T2Id,
                T2Name = s.T2Name,
                T2Type = s.T2Type,
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

    public async Task<List<AmericanSelectMainEventRosterRowDto>> GetAmericanSelectMainEventRosterRowsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Flattened EF replacement for the master-detail proc pair _Teams + _TeamRoster. All active
        // Players on non-"Registration" agegroups; INNER Families + family-user (Hometown = the family
        // user's city). One flat query; the PDF layer groups by agegroup → team into per-team cards.
        return await (
            from r in _context.Registrations.AsNoTracking()
            join roles in _context.AspNetRoles.AsNoTracking() on r.RoleId equals roles.Id
            join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
            join j in _context.Jobs.AsNoTracking() on r.JobId equals j.JobId
            from f in _context.Families.AsNoTracking().Where(x => x.FamilyUserId == r.FamilyUserId)
            from uF in _context.AspNetUsers.AsNoTracking().Where(x => x.Id == r.FamilyUserId)
            from t in _context.Teams.AsNoTracking().Where(x => x.TeamId == r.AssignedTeamId)
            where r.JobId == jobId && r.BActive == true && roles.Name == "Player"
                && t.Agegroup.AgegroupName != "Registration"
            orderby t.Agegroup.AgegroupName, t.TeamName, u.LastName, u.FirstName
            select new AmericanSelectMainEventRosterRowDto
            {
                RegistrationId = r.RegistrationId,
                TeamId = t.TeamId,
                JobName = j.JobName,
                AgegroupName = t.Agegroup.AgegroupName,
                TeamName = t.TeamName,
                UniformNo = r.UniformNo,
                Position = r.Position,
                FirstName = u.FirstName,
                LastName = u.LastName,
                SchoolName = r.SchoolName,
                Hometown = uF.City,
            }).ToListAsync(cancellationToken);
    }
}
