using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Concrete implementation of IUserRepository using Entity Framework Core.
/// Encapsulates all EF-specific query logic for AspNetUsers entity.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly SqlDbContext _context;

    public UserRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<AspNetUsers?> GetByIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.AspNetUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public async Task<bool> RequiresTosSignatureAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.UserName == username)
            .Select(u => new { u.BTsicwaiverSigned, u.TsicwaiverSignedTs })
            .SingleOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return true; // Require signature if user not found
        }

        // Require signature if never signed or if signature is more than 1 year old
        return !user.BTsicwaiverSigned ||
               user.TsicwaiverSignedTs == null ||
               user.TsicwaiverSignedTs.Value.AddYears(1) < DateTime.Now;
    }

    public async Task UpdateTosAcceptanceAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .SingleOrDefaultAsync(u => u.UserName == username, cancellationToken);

        if (user != null)
        {
            user.BTsicwaiverSigned = true;
            user.TsicwaiverSignedTs = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateTosAcceptanceByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user != null)
        {
            user.BTsicwaiverSigned = true;
            user.TsicwaiverSignedTs = DateTime.Now;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<List<UserBasicInfo>> GetUsersByIdsAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new UserBasicInfo
            {
                UserId = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Birthdate = u.Dob
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, UserNameInfo>> GetUserNameMapAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var data = await _context.AspNetUsers
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(cancellationToken);

        return data.ToDictionary(x => x.Id, x => new UserNameInfo
        {
            FirstName = x.FirstName,
            LastName = x.LastName
        });
    }

    public async Task<UserContactInfo?> GetUserContactInfoAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _context.AspNetUsers
            .Where(u => u.Id == userId)
            .Select(u => new UserContactInfo
            {
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode,
                Cellphone = u.Cellphone,
                Phone = u.Phone
            })
            .FirstOrDefaultAsync(cancellationToken);

        return user;
    }

    public async Task<List<AspNetUsers>> GetUsersForFamilyAsync(
        List<string> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new List<AspNetUsers>();
        }

        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new AspNetUsers
            {
                Id = u.Id,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Gender = u.Gender,
                Dob = u.Dob,
                Email = u.Email,
                Cellphone = u.Cellphone,
                Phone = u.Phone,
                StreetAddress = u.StreetAddress,
                City = u.City,
                State = u.State,
                PostalCode = u.PostalCode
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<UserSearchResult>> SearchAdminCandidatesAsync(
        string query,
        Guid jobId,
        string requestedRoleId,
        IReadOnlyCollection<string> laneRoleIds,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;

        // Name match relies on the database's case-insensitive collation — no LOWER() wrappers,
        // which force a per-row scalar computation across the whole scan (measured cost on
        // 314k users; the collation already matches case-insensitively).
        // Two qualifying shapes (ruling 2026-08-13; endpoints are SuperUserOnly):
        //  - Lane-pure admin: every LIVE registration (bActive, and the job unexpired for the
        //    role type — admin roles ride Jobs.ExpiryAdmin, every other role rides
        //    Jobs.ExpiryUsers, the legacy login role-picker predicate) lies within the lane's
        //    role set (Director+SuperDirector share one lane; every other admin type is its
        //    own lane).
        //  - Pending coach: EXACTLY ONE active Unassigned Adult registration, on any job of
        //    any customer, with NO expiry gate — the add path deletes that row, so a closed
        //    source event is irrelevant. Other roles on the account (Staff from a roster-swapper
        //    approval, player history, …) do NOT disqualify this shape.
        // The family-credential exclusion stays GLOBAL (not liveness-gated): a shared household
        // login is structural — accepting one would hand admin access to the whole household.
        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u =>
                u.UserName!.Contains(query) ||
                (u.FirstName != null && u.FirstName.Contains(query)) ||
                (u.LastName != null && u.LastName.Contains(query)))
            // Never a family credential holder — global, not liveness-gated
            .Where(u => !_context.Registrations.Any(r => r.FamilyUserId == u.Id))
            .Select(u => new
            {
                User = u,
                // Third shape: an account with NO registration footprint at all. Nothing to be
                // impure about and nothing on this job to collide with, so it is offered
                // directly (Todd 2026-08-23 — returning admins whose history has aged out).
                HasAnyRegs = _context.Registrations.Any(r => r.UserId == u.Id),
                // Lane purity needs ≥1 live reg (All() over an empty set is vacuously true).
                HasLiveRegs = _context.Registrations.Any(r => r.UserId == u.Id
                    && r.BActive == true
                    && (RoleConstants.AdminRoleIds.Contains(r.RoleId!)
                        ? r.Job.ExpiryAdmin > now
                        : r.Job.ExpiryUsers > now)),
                IsLanePure = _context.Registrations
                    .Where(r => r.UserId == u.Id
                        && r.BActive == true
                        && (RoleConstants.AdminRoleIds.Contains(r.RoleId!)
                            ? r.Job.ExpiryAdmin > now
                            : r.Job.ExpiryUsers > now))
                    .All(r => laneRoleIds.Contains(r.RoleId!)),
                ActiveUaCount = _context.Registrations.Count(r => r.UserId == u.Id
                    && r.BActive == true
                    && r.RoleId == RoleConstants.UnassignedAdult),
                // A registration on this job blocks a LANE-PURE candidate if it carries the
                // REQUESTED role (active or not — reactivate via the grid, never stack a
                // duplicate) or a role outside the lane. Within the D/SD lane the other role
                // does NOT block: dual-hat accounts hold Director AND SuperDirector on one job
                // as two registrations. Mirrors the add-side duplicate guard, so search never
                // offers someone the add would then reject.
                HasBlockingRegOnThisJob = _context.Registrations.Any(r => r.UserId == u.Id
                    && r.JobId == jobId
                    && (r.RoleId == requestedRoleId || !laneRoleIds.Contains(r.RoleId!))),
                // A pending coach blocks only on the requested role itself — their Staff/UA
                // rows on this job are fine (UA is what gets deleted; Staff is their team hat).
                HasRequestedRoleOnThisJob = _context.Registrations.Any(r => r.UserId == u.Id
                    && r.JobId == jobId
                    && r.RoleId == requestedRoleId)
            })
            .Where(x => (x.HasLiveRegs && x.IsLanePure && !x.HasBlockingRegOnThisJob)
                || (x.ActiveUaCount == 1 && !x.HasRequestedRoleOnThisJob)
                || !x.HasAnyRegs)
            .OrderBy(x => x.User.LastName)
            .ThenBy(x => x.User.FirstName)
            .Take(maxResults)
            .Select(x => new UserSearchResult
            {
                UserId = x.User.Id,
                UserName = x.User.UserName!,
                FirstName = x.User.FirstName,
                LastName = x.User.LastName,
                AccountType = x.HasLiveRegs && x.IsLanePure
                    ? "Admin"
                    : x.HasAnyRegs ? "PendingAdult" : "NoRegistrations"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminCandidateMissReason> DiagnoseAdminCandidateMissAsync(
        string query,
        Guid jobId,
        string requestedRoleId,
        IReadOnlyCollection<string> laneRoleIds,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;

        // Same name predicate as SearchAdminCandidatesAsync (CI collation, no LOWER()), but no
        // eligibility filter — we're asking WHY the eligible set is empty. Only accounts with
        // SOME registration footprint (own or family-credential, any customer — the endpoint is
        // SuperUserOnly, so cross-customer acknowledgment is fine) are diagnosed; a bare
        // identity row reports NotFound.
        var matches = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u =>
                u.UserName!.Contains(query) ||
                (u.FirstName != null && u.FirstName.Contains(query)) ||
                (u.LastName != null && u.LastName.Contains(query)))
            .Where(u =>
                _context.Registrations.Any(r => r.FamilyUserId == u.Id) ||
                _context.Registrations.Any(r => r.UserId == u.Id))
            .Select(u => new
            {
                IsExactUserName = u.UserName! == query,
                // Family credential = global (structural). Player-role history no longer bars
                // an account — only the shared-household login does.
                IsFamilyCredential = _context.Registrations.Any(r => r.FamilyUserId == u.Id),
                ActiveUaCount = _context.Registrations.Count(r => r.UserId == u.Id
                    && r.BActive == true
                    && r.RoleId == RoleConstants.UnassignedAdult),
                HasLiveRegs = _context.Registrations.Any(r => r.UserId == u.Id
                    && r.BActive == true
                    && (RoleConstants.AdminRoleIds.Contains(r.RoleId!)
                        ? r.Job.ExpiryAdmin > now
                        : r.Job.ExpiryUsers > now)),
                // REQUESTED-role registration on THIS job (active or not) — the search excluded
                // them as "already holds this role here", so say that, not "outside lane". The
                // other D/SD lane role no longer blocks (dual-hat), so it must not trip this.
                IsRequestedRoleAdminOnThisJob = _context.Registrations.Any(r => r.UserId == u.Id
                    && r.JobId == jobId
                    && r.RoleId == requestedRoleId)
            })
            .Take(25)
            .ToListAsync(cancellationToken);

        // An exact username hit is what the user meant — diagnose that account. Otherwise prefer
        // the most actionable explanation over the generic one.
        var best = matches.FirstOrDefault(m => m.IsExactUserName)
            ?? matches.FirstOrDefault(m => m.IsFamilyCredential)
            ?? matches.FirstOrDefault(m => m.IsRequestedRoleAdminOnThisJob)
            ?? matches.FirstOrDefault(m => m.ActiveUaCount > 1)
            ?? matches.FirstOrDefault(m => m.HasLiveRegs);

        if (best == null)
            return AdminCandidateMissReason.NotFound;

        if (best.IsFamilyCredential)
            return AdminCandidateMissReason.FamilyOrPlayer;

        if (best.IsRequestedRoleAdminOnThisJob)
            return AdminCandidateMissReason.AlreadyAdmin;

        if (best.ActiveUaCount > 1)
            return AdminCandidateMissReason.MultiplePending;

        // Zero active pending regs: live non-lane registrations → outside the funnel; only
        // dead registrations (inactive / job expired) → same funnel as "not registered".
        return best.HasLiveRegs
            ? AdminCandidateMissReason.OutsideLane
            : AdminCandidateMissReason.NotFound;
    }

    public async Task<List<PasswordResetAccount>> GetPasswordResetAccountsAsync(
        string usernameOrEmail,
        CancellationToken cancellationToken = default)
    {
        var input = usernameOrEmail.Trim();
        // Reproduces Identity's UpperInvariantLookupNormalizer — same convention as AspNetUserEmail.Set.
        var normalized = input.Normalize().ToUpperInvariant();

        // A username identifies exactly one account — an exact hit wins outright (legacy semantics).
        var byUsername = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.NormalizedUserName == normalized)
            .Select(u => new PasswordResetAccount { UserId = u.Id, UserName = u.UserName!, Email = u.Email })
            .ToListAsync(cancellationToken);
        if (byUsername.Count > 0)
        {
            return byUsername;
        }

        // Email: direct account matches (NormalizedEmail is the column forgot-password searches) …
        var byEmail = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => new PasswordResetAccount { UserId = u.Id, UserName = u.UserName!, Email = u.Email })
            .ToListAsync(cancellationToken);

        // … plus family logins reached through the household record. Many family logins carry no
        // email of their own — the mom/dad address on Families is how those parents find their account.
        var byFamily = await (
            from f in _context.Families
            join u in _context.AspNetUsers on f.FamilyUserId equals u.Id
            where f.MomEmail == input || f.DadEmail == input
            select new PasswordResetAccount { UserId = u.Id, UserName = u.UserName!, Email = u.Email })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return byEmail.Concat(byFamily)
            .GroupBy(a => a.UserId)
            .Select(g => g.First())
            .ToList();
    }
}
