using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.CheckIn;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICheckinRepository"/>. Roster reads project
/// straight to DTOs (one query, one shape) and LEFT JOIN the check-in row via the
/// scaffolded 1:1 navs. Writes are tracked upserts — one row per target.
/// Check-in timestamps use local time (Arizona, MST/no-DST) to match the DB default.
/// </summary>
public class CheckinRepository : ICheckinRepository
{
    private readonly SqlDbContext _context;

    public CheckinRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<List<TeamCheckinRowDto>> GetTeamRosterByJobAsync(Guid jobId, CancellationToken ct = default)
    {
        return await (
            from t in _context.Teams
            where t.JobId == jobId && t.Active == true
            join crReg in _context.Registrations
                on t.ClubrepRegistrationid equals crReg.RegistrationId into crj
            from cr in crj.DefaultIfEmpty()
            join crUsr in _context.AspNetUsers on cr.UserId equals crUsr.Id into cuj
            from cu in cuj.DefaultIfEmpty()
            orderby t.Agegroup.AgegroupName, t.TeamName
            select new TeamCheckinRowDto
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName ?? string.Empty,
                ClubRepRegistrationId = t.ClubrepRegistrationid,
                AgegroupName = t.Agegroup.AgegroupName,
                Gender = t.Agegroup.Gender,
                DivName = t.Div != null ? t.Div!.DivName : null,
                ClubName = cr != null ? cr.ClubName : null,
                ClubRepName = cu != null
                    ? (((cu.FirstName ?? string.Empty) + " " + (cu.LastName ?? string.Empty)).Trim())
                    : null,
                OwedTotal = cr != null ? cr.OwedTotal : 0m,
                PaidTotal = cr != null ? cr.PaidTotal : 0m,
                StartDate = t.Startdate,
                EndDate = t.Enddate,
                EffectiveDate = t.Effectiveasofdate,
                ExpiryDate = t.Expireondate,
                CheckedInTs = t.TeamCheckIns != null ? t.TeamCheckIns!.CheckedInTs : (DateTime?)null,
                CheckedInByRegId = t.TeamCheckIns != null ? t.TeamCheckIns!.CheckedInByRegId : null,
            })
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<PlayerCheckinRowDto>> GetPlayerRosterByTeamAsync(Guid teamId, CancellationToken ct = default)
    {
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.AssignedTeamId == teamId
                  && r.BActive == true
                  && r.RoleId == RoleConstants.Player
            orderby u.LastName, u.FirstName
            select new PlayerCheckinRowDto
            {
                RegistrationId = r.RegistrationId,
                PlayerUserId = u.Id,
                FirstName = u.FirstName ?? string.Empty,
                LastName = u.LastName ?? string.Empty,
                Email = u.Email,
                ClubName = r.ClubName,
                SchoolName = r.SchoolName,
                GradYear = r.GradYear,
                Position = r.Position,
                DayGroup = r.DayGroup,
                NightGroup = r.NightGroup,
                RoommatePref = r.RoommatePref,
                MomName = r.FamilyUser != null
                    ? (((r.FamilyUser.MomFirstName ?? string.Empty) + " " + (r.FamilyUser.MomLastName ?? string.Empty)).Trim())
                    : null,
                MomCellphone = r.FamilyUser != null ? r.FamilyUser.MomCellphone : null,
                MomEmail = r.FamilyUser != null ? r.FamilyUser.MomEmail : null,
                DadName = r.FamilyUser != null
                    ? (((r.FamilyUser.DadFirstName ?? string.Empty) + " " + (r.FamilyUser.DadLastName ?? string.Empty)).Trim())
                    : null,
                DadCellphone = r.FamilyUser != null ? r.FamilyUser.DadCellphone : null,
                DadEmail = r.FamilyUser != null ? r.FamilyUser.DadEmail : null,
                OwedTotal = r.OwedTotal,
                PaidTotal = r.PaidTotal,
                HasMedForm = r.BUploadedMedForm ?? false,
                CheckedInTs = r.PlayerCheckInsRegistration != null
                    ? r.PlayerCheckInsRegistration.CheckedInTs
                    : (DateTime?)null,
                CheckedInByRegId = r.PlayerCheckInsRegistration != null
                    ? r.PlayerCheckInsRegistration.CheckedInByRegId
                    : null,
            })
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<CheckinStateDto?> UpsertPlayerCheckinAsync(
        Guid jobId, Guid registrationId, Guid byRegId, string? userId, CancellationToken ct = default)
    {
        var inJob = await _context.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.RegistrationId == registrationId && r.JobId == jobId, ct);
        if (!inJob) return null;

        var now = DateTime.Now; // local AZ — matches sysdatetime() column default
        var row = await _context.PlayerCheckIns
            .FirstOrDefaultAsync(p => p.RegistrationId == registrationId, ct);

        if (row is null)
        {
            row = new PlayerCheckIns
            {
                RegistrationId = registrationId,
                CheckedInTs = now,
                CheckedInByRegId = byRegId,
                Modified = now,
                LebUserId = userId,
            };
            _context.PlayerCheckIns.Add(row);
        }
        else
        {
            row.CheckedInTs = now;
            row.CheckedInByRegId = byRegId;
            row.Modified = now;
            row.LebUserId = userId;
        }

        await _context.SaveChangesAsync(ct);
        return new CheckinStateDto { Id = registrationId, CheckedInTs = now, CheckedInByRegId = byRegId };
    }

    public async Task<bool> UndoPlayerCheckinAsync(Guid jobId, Guid registrationId, CancellationToken ct = default)
    {
        var inJob = await _context.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.RegistrationId == registrationId && r.JobId == jobId, ct);
        if (!inJob) return false;

        var row = await _context.PlayerCheckIns
            .FirstOrDefaultAsync(p => p.RegistrationId == registrationId, ct);
        if (row is null) return false;

        _context.PlayerCheckIns.Remove(row);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CheckinStateDto?> UpsertTeamCheckinAsync(
        Guid jobId, Guid teamId, Guid byRegId, string? userId, CancellationToken ct = default)
    {
        var inJob = await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.TeamId == teamId && t.JobId == jobId, ct);
        if (!inJob) return null;

        var now = DateTime.Now; // local AZ — matches sysdatetime() column default
        var row = await _context.TeamCheckIns
            .FirstOrDefaultAsync(t => t.TeamId == teamId, ct);

        if (row is null)
        {
            row = new TeamCheckIns
            {
                TeamId = teamId,
                CheckedInTs = now,
                CheckedInByRegId = byRegId,
                Modified = now,
                LebUserId = userId,
            };
            _context.TeamCheckIns.Add(row);
        }
        else
        {
            row.CheckedInTs = now;
            row.CheckedInByRegId = byRegId;
            row.Modified = now;
            row.LebUserId = userId;
        }

        await _context.SaveChangesAsync(ct);
        return new CheckinStateDto { Id = teamId, CheckedInTs = now, CheckedInByRegId = byRegId };
    }

    public async Task<bool> UndoTeamCheckinAsync(Guid jobId, Guid teamId, CancellationToken ct = default)
    {
        var inJob = await _context.Teams
            .AsNoTracking()
            .AnyAsync(t => t.TeamId == teamId && t.JobId == jobId, ct);
        if (!inJob) return false;

        var row = await _context.TeamCheckIns
            .FirstOrDefaultAsync(t => t.TeamId == teamId, ct);
        if (row is null) return false;

        _context.TeamCheckIns.Remove(row);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
