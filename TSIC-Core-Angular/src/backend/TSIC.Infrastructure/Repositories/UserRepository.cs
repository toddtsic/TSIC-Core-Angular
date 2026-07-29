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
        Guid customerId,
        IReadOnlyCollection<string> laneRoleIds,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        var lowerQuery = query.ToLower();
        var cutoff = DateTime.Now.AddYears(-RoleConstants.AdminLaneLookbackYears);

        // AM-004 lane model: eligibility depends on the role being granted. Only registrations
        // from the lookback window count (a stale grant from years back must not poison an
        // otherwise lane-pure account). Two qualifying shapes:
        //  - Lane-pure: every windowed registration, on any job/customer, lies within the
        //    lane's role set (Director+SuperDirector share one lane; every other admin type
        //    is its own lane — no cross-type grants).
        //  - Pending adult: every windowed registration is Unassigned Adult with at least one
        //    on this customer — the sanctioned funnel for brand-new admins ("register as coach
        //    on the site, then get accepted here").
        // The family-credential exclusion stays GLOBAL (no window): a shared household login
        // is structural — accepting one would hand admin access to the whole household.
        return await _context.AspNetUsers
            .AsNoTracking()
            .Where(u =>
                u.UserName!.ToLower().Contains(lowerQuery) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(lowerQuery)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(lowerQuery)))
            // Never a family credential holder — global, not windowed
            .Where(u => !_context.Registrations.Any(r => r.FamilyUserId == u.Id))
            // Needs at least one windowed registration to classify
            .Where(u => _context.Registrations.Any(r => r.UserId == u.Id && r.RegistrationTs >= cutoff))
            .Select(u => new
            {
                User = u,
                IsLanePure = _context.Registrations
                    .Where(r => r.UserId == u.Id && r.RegistrationTs >= cutoff)
                    .All(r => laneRoleIds.Contains(r.RoleId!)),
                IsPendingAdultOnly =
                    _context.Registrations
                        .Where(r => r.UserId == u.Id && r.RegistrationTs >= cutoff)
                        .All(r => r.RoleId == RoleConstants.UnassignedAdult)
                    && _context.Registrations.Any(r => r.UserId == u.Id
                        && r.RegistrationTs >= cutoff
                        && r.RoleId == RoleConstants.UnassignedAdult
                        && r.Job.CustomerId == customerId)
            })
            .Where(x => x.IsLanePure || x.IsPendingAdultOnly)
            .OrderBy(x => x.User.LastName)
            .ThenBy(x => x.User.FirstName)
            .Take(maxResults)
            .Select(x => new UserSearchResult
            {
                UserId = x.User.Id,
                UserName = x.User.UserName!,
                FirstName = x.User.FirstName,
                LastName = x.User.LastName,
                AccountType = x.IsLanePure ? "Admin" : "PendingAdult"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AdminCandidateMissReason> DiagnoseAdminCandidateMissAsync(
        string query,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var lowerQuery = query.ToLower();
        var cutoff = DateTime.Now.AddYears(-RoleConstants.AdminLaneLookbackYears);

        // Same name predicate as SearchAdminCandidatesAsync, but no eligibility filter — we're
        // asking WHY the eligible set is empty. Enumeration guard: a director typing names must
        // not learn whether an account exists platform-wide, so only accounts with a registration
        // footprint on THIS customer (own or family-credential) are acknowledged at all.
        var matches = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u =>
                u.UserName!.ToLower().Contains(lowerQuery) ||
                (u.FirstName != null && u.FirstName.ToLower().Contains(lowerQuery)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(lowerQuery)))
            .Where(u =>
                _context.Registrations.Any(r => r.FamilyUserId == u.Id && r.Job.CustomerId == customerId) ||
                _context.Registrations.Any(r => r.UserId == u.Id && r.Job.CustomerId == customerId))
            .Select(u => new
            {
                IsExactUserName = u.UserName!.ToLower() == lowerQuery,
                // Family credential = global (structural); role classification = windowed,
                // mirroring the eligibility query.
                IsFamilyOrPlayer =
                    _context.Registrations.Any(r => r.FamilyUserId == u.Id) ||
                    _context.Registrations.Any(r => r.UserId == u.Id
                        && r.RegistrationTs >= cutoff
                        && (r.RoleId == RoleConstants.Player || r.RoleId == RoleConstants.Family)),
                HasWindowedRegs = _context.Registrations.Any(r => r.UserId == u.Id && r.RegistrationTs >= cutoff)
            })
            .Take(25)
            .ToListAsync(cancellationToken);

        // An exact username hit is what the user meant — diagnose that account. Otherwise prefer
        // the family/player explanation (the common household-login case) over the generic one.
        var best = matches.FirstOrDefault(m => m.IsExactUserName)
            ?? matches.FirstOrDefault(m => m.IsFamilyOrPlayer)
            ?? matches.FirstOrDefault(m => m.HasWindowedRegs);

        if (best == null)
            return AdminCandidateMissReason.NotFound;

        if (best.IsFamilyOrPlayer)
            return AdminCandidateMissReason.FamilyOrPlayer;

        // Only stale registrations (outside the window) → same funnel as "not registered here".
        return best.HasWindowedRegs
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
