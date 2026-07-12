using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Read <c>docs/Domain/change-password-contract.md</c> before changing anything in here.
/// The two things that matter most: a player never signs in (the FAMILY login does), and a merge is
/// not a rename — it re-points registrations onto another account, irreversibly.
/// </summary>
public class ChangePasswordRepository : IChangePasswordRepository
{
    private readonly SqlDbContext _context;

    // The UI groups registrations into login accounts, so the cap has to bound ACCOUNTS.
    // Capping raw rows let one prolific family exhaust the budget and silently hide both
    // other accounts and — worse — some of that family's own players. Search runs in two
    // passes: find the matching accounts, then return every registration they own.
    // MaxRows is only a runaway backstop; it should never bite in practice.
    private const int MaxAccounts = 50;   // keep in sync with MAX_ACCOUNTS (change-password.component.ts)
    private const int MaxRows = 5000;

    public ChangePasswordRepository(SqlDbContext context)
    {
        _context = context;
    }

    // ═══════════════════════════════════════════════════════════
    //  Search — Player role (includes family data)
    // ═══════════════════════════════════════════════════════════

    public async Task<List<ChangePasswordSearchResultDto>> SearchPlayerRegistrationsAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default)
    {
        var query =
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join uP in _context.AspNetUsers on r.UserId equals uP.Id
            join uF in _context.AspNetUsers on r.FamilyUserId equals uF.Id
            join f in _context.Families on uF.Id equals f.FamilyUserId
            where r.RoleId == request.RoleId
            select new { r, role, j, c, uP, uF, f };

        if (!string.IsNullOrWhiteSpace(request.LastName))
        {
            query = from q in query
                    where (q.uP.LastName != null && q.uP.LastName.Contains(request.LastName))
                       || (q.f.MomLastName != null && q.f.MomLastName.Contains(request.LastName))
                       || (q.f.DadLastName != null && q.f.DadLastName.Contains(request.LastName))
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName))
        {
            query = from q in query
                    where (q.uP.FirstName != null && q.uP.FirstName.Contains(request.FirstName))
                       || (q.f.MomFirstName != null && q.f.MomFirstName.Contains(request.FirstName))
                       || (q.f.DadFirstName != null && q.f.DadFirstName.Contains(request.FirstName))
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            query = from q in query
                    where q.c.CustomerName != null && q.c.CustomerName.Contains(request.CustomerName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.JobName))
        {
            query = from q in query
                    where q.j.JobName != null && q.j.JobName.Contains(request.JobName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            query = from q in query
                    where (q.uP.Email != null && q.uP.Email.Contains(request.Email))
                       || (q.f.MomEmail != null && q.f.MomEmail.Contains(request.Email))
                       || (q.f.DadEmail != null && q.f.DadEmail.Contains(request.Email))
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            query = from q in query
                    where (q.uP.Cellphone != null && q.uP.Cellphone.Contains(request.Phone))
                       || (q.f.MomCellphone != null && q.f.MomCellphone.Contains(request.Phone))
                       || (q.f.DadCellphone != null && q.f.DadCellphone.Contains(request.Phone))
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.FamilyUserName))
        {
            query = from q in query
                    where q.uF.UserName != null && q.uF.UserName.Contains(request.FamilyUserName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            query = from q in query
                    where q.uP.UserName != null && q.uP.UserName.Contains(request.UserName)
                    select q;
        }

        // Pass 1 — which FAMILY LOGINS match? Ordered so the cut is stable across runs.
        var familyUserIds = await query
            .Select(q => new { q.uF.Id, q.uF.UserName })
            .Distinct()
            .OrderBy(a => a.UserName)
            .Take(MaxAccounts)
            .Select(a => a.Id)
            .ToListAsync(ct);

        // Pass 2 — every player registration those families own. The person-level filters are
        // deliberately NOT re-applied: a search for "Shoulberg" that matched via the mom must
        // still return a player whose own last name differs, or the family table loses a child.
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join uP in _context.AspNetUsers on r.UserId equals uP.Id
            join uF in _context.AspNetUsers on r.FamilyUserId equals uF.Id
            join f in _context.Families on uF.Id equals f.FamilyUserId
            where r.RoleId == request.RoleId && familyUserIds.Contains(uF.Id)
            orderby uF.UserName, uP.LastName, uP.FirstName, c.CustomerName, j.JobName
            select new ChangePasswordSearchResultDto
            {
                RegistrationId = r.RegistrationId,
                RoleName = role.Name ?? "",
                CustomerName = c.CustomerName ?? "",
                JobName = j.JobName ?? "",
                UserName = uP.UserName ?? "",
                FirstName = uP.FirstName,
                LastName = uP.LastName,
                Email = uP.Email,
                Phone = uP.Cellphone,
                FamilyUserName = uF.UserName,
                FamilyEmail = uF.Email,
                MomFirstName = f.MomFirstName,
                MomLastName = f.MomLastName,
                MomEmail = f.MomEmail,
                MomPhone = f.MomCellphone,
                DadFirstName = f.DadFirstName,
                DadLastName = f.DadLastName,
                DadEmail = f.DadEmail,
                DadPhone = f.DadCellphone
            }
        ).AsNoTracking().Take(MaxRows).ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Search — Non-player roles (no family data)
    // ═══════════════════════════════════════════════════════════

    public async Task<List<ChangePasswordSearchResultDto>> SearchAdultRegistrationsAsync(
        ChangePasswordSearchRequest request,
        CancellationToken ct = default)
    {
        var query =
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RoleId == request.RoleId
            select new { r, role, j, c, u };

        if (!string.IsNullOrWhiteSpace(request.LastName))
        {
            query = from q in query
                    where q.u.LastName != null && q.u.LastName.Contains(request.LastName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.FirstName))
        {
            query = from q in query
                    where q.u.FirstName != null && q.u.FirstName.Contains(request.FirstName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
        {
            query = from q in query
                    where q.c.CustomerName != null && q.c.CustomerName.Contains(request.CustomerName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.JobName))
        {
            query = from q in query
                    where q.j.JobName != null && q.j.JobName.Contains(request.JobName)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            query = from q in query
                    where q.u.Email != null && q.u.Email.Contains(request.Email)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            query = from q in query
                    where q.u.Cellphone != null && q.u.Cellphone.Contains(request.Phone)
                    select q;
        }

        if (!string.IsNullOrWhiteSpace(request.UserName))
        {
            query = from q in query
                    where q.u.UserName != null && q.u.UserName.Contains(request.UserName)
                    select q;
        }

        // Pass 1 — which LOGINS match? (For non-players the login is the person's own account.)
        var userIds = await query
            .Select(q => new { q.u.Id, q.u.UserName })
            .Distinct()
            .OrderBy(a => a.UserName)
            .Take(MaxAccounts)
            .Select(a => a.Id)
            .ToListAsync(ct);

        // Pass 2 — every registration those logins own.
        return await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RoleId == request.RoleId && userIds.Contains(u.Id)
            orderby u.UserName, c.CustomerName, j.JobName
            select new ChangePasswordSearchResultDto
            {
                RegistrationId = r.RegistrationId,
                RoleName = role.Name ?? "",
                CustomerName = c.CustomerName ?? "",
                JobName = j.JobName ?? "",
                UserName = u.UserName ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                Email = u.Email,
                Phone = u.Cellphone
            }
        ).AsNoTracking().Take(MaxRows).ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Reset targeting
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Follow the registration's OWN FK to the account being reset. The caller says which side
    /// (own login vs family login); the server resolves the account. The caller never names it.
    /// </summary>
    public async Task<ResetTargetDto?> ResolveResetTargetAsync(
        Guid registrationId,
        ResetPasswordTarget target,
        CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.UserId, r.FamilyUserId })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (reg == null) return null;

        var accountId = target == ResetPasswordTarget.Family ? reg.FamilyUserId : reg.UserId;
        if (string.IsNullOrWhiteSpace(accountId)) return null;

        return await _context.AspNetUsers
            .Where(u => u.Id == accountId && u.UserName != null)
            .Select(u => new ResetTargetDto { UserId = u.Id, UserName = u.UserName! })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Email updates
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// A blank email CLEARS the address — that is a legitimate edit (contract §3 #9).
    /// Store NULL, never "": <c>NormalizedEmail</c> is what <c>FindByEmailAsync</c> looks up for the
    /// public forgot-password flow, and an empty string is a VALUE — code that tests
    /// <c>Email != null</c> would treat a cleared account as having an address and try to mail it.
    /// NULL is already a normal value in this column, so this is also the consistent choice.
    /// </summary>
    private static string? BlankToNull(string? email)
        => string.IsNullOrWhiteSpace(email) ? null : email.Trim();

    public async Task UpdateUserEmailAsync(
        Guid registrationId,
        string? newEmail,
        CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found.");

        var user = await _context.AspNetUsers
            .FirstOrDefaultAsync(u => u.Id == reg.UserId, ct)
            ?? throw new InvalidOperationException($"User for registration {registrationId} not found.");

        var email = BlankToNull(newEmail);
        if (string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)) return;

        user.Email = email;
        user.NormalizedEmail = email?.ToUpperInvariant();

        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateFamilyEmailsAsync(
        Guid registrationId,
        string? familyEmail,
        string? momEmail,
        string? dadEmail,
        CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found.");

        if (string.IsNullOrWhiteSpace(reg.FamilyUserId))
            throw new InvalidOperationException("Registration has no family account.");

        // PATCH semantics, established by 5a121a2c: an OMITTED field (null) means "leave it alone";
        // an EMPTY field ("") means "clear the address". Do not collapse the two — collapsing them
        // is what made a stale address unremovable in the first place.
        if (familyEmail != null)
        {
            var familyUser = await _context.AspNetUsers
                .FirstOrDefaultAsync(u => u.Id == reg.FamilyUserId, ct)
                ?? throw new InvalidOperationException("Family user not found.");

            var normalized = BlankToNull(familyEmail);
            familyUser.Email = normalized;
            familyUser.NormalizedEmail = normalized?.ToUpperInvariant();
        }

        if (momEmail != null || dadEmail != null)
        {
            // Fail loud. Silently no-op'ing here returned a success message for a write that never
            // happened. (Measured: 0 player registrations have a family login with no Families row.)
            var family = await _context.Families
                .FirstOrDefaultAsync(f => f.FamilyUserId == reg.FamilyUserId, ct)
                ?? throw new InvalidOperationException("Family record not found.");

            if (momEmail != null) family.MomEmail = BlankToNull(momEmail);
            if (dadEmail != null) family.DadEmail = BlankToNull(dadEmail);
        }

        await _context.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Merge — the identity keys
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// THE identity key for a person-level merge: same first name, last name, DOB and role.
    ///
    /// ONE definition, used by the candidate preview AND by the merge itself. If they ever diverge,
    /// the blast radius shown to the admin becomes a lie — which, on an irreversible bulk update, is
    /// the worst failure this tool can have.
    ///
    /// The net is deliberately WIDE: it sweeps every account matching the identity, not just the
    /// source's. That is correct and must not be narrowed — a child accumulates one account per
    /// season, so this consolidates all six in a single action instead of forcing six merges.
    /// (name + DOB + role) identifies ONE human, so every account matching it IS that human.
    ///
    /// The null-permissive DOB/email branches are inherited from legacy and are harmless here: a
    /// player's DOB is never null (measured 0 of 130,831 — contract §2), so for players this reduces
    /// to an exact name+DOB+role match.
    /// </summary>
    private IQueryable<Registrations> PersonWorkSet(
        string? roleId, string firstName, string lastName, DateTime? dob, string? email)
        => from r in _context.Registrations
           join u in _context.AspNetUsers on r.UserId equals u.Id
           where r.BActive == true
              && r.RoleId == roleId
              && u.FirstName != null && u.FirstName.ToLower() == firstName.ToLower()
              && u.LastName != null && u.LastName.ToLower() == lastName.ToLower()
              && (u.Dob == null || (dob != null && u.Dob == dob))
              && (u.Email == null || (email != null && u.Email == email))
           select r;

    // ═══════════════════════════════════════════════════════════
    //  Merge candidates
    // ═══════════════════════════════════════════════════════════

    public async Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        var source = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RegistrationId == registrationId
            select new { u.Id, u.FirstName, u.LastName, u.Dob, u.Email, r.RoleId }
        ).AsNoTracking().SingleOrDefaultAsync(ct);

        if (source?.FirstName == null || source.LastName == null) return EmptyCandidates();

        // Exactly what the merge will move. Same predicate, one definition.
        var work = await PersonWorkSet(source.RoleId, source.FirstName, source.LastName, source.Dob, source.Email)
            .Select(r => new { r.RegistrationId, r.UserId })
            .AsNoTracking()
            .ToListAsync(ct);

        var accountIds = work
            .Select(w => w.UserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Append(source.Id)
            .Distinct()
            .ToList();

        var accounts = await HydratePersonAccountsAsync(accountIds, ct);

        if (!accounts.TryGetValue(source.Id, out var sourceDto)) return EmptyCandidates();

        return new MergeCandidatesResponse
        {
            Source = sourceDto,
            Candidates = [.. accounts.Values
                .Where(a => a.UserId != source.Id)
                .OrderByDescending(a => a.RegistrationCount)
                .ThenBy(a => a.UserName)],
            RegistrationsAffected = work.Count,
            AccountsAffected = work.Select(w => w.UserId).Distinct().Count()
        };
    }

    /// <summary>
    /// Family-merge candidates, keyed on <b>the child</b> — another family login that owns a player
    /// with the same (first name, last name, DOB).
    ///
    /// Legacy keyed this on an exact match of all six parent fields plus postal code. That finds only
    /// 47% of the real duplicates (28,755 of 60,780 — contract §2), because households re-register
    /// from scratch each season and the parents get retyped into swapped slots, with typos and
    /// nicknames: the SAME household is on file as both "Su Kang / Jesse Abraham" and
    /// "Jesse Abraham / Su Kang". The child is the stable key; the parents are not.
    /// </summary>
    public async Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        var sourceFamilyUserId = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => r.FamilyUserId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(sourceFamilyUserId)) return EmptyCandidates();

        // The source household's children — the key.
        var sourceChildren = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.FamilyUserId == sourceFamilyUserId && r.BActive == true && u.Dob != null
            select new { u.FirstName, u.LastName, u.Dob }
        ).AsNoTracking().Distinct().ToListAsync(ct);

        var sourceChildKeys = sourceChildren
            .Select(c => (Fold(c.FirstName), Fold(c.LastName), c.Dob))
            .ToHashSet();

        // Other family logins owning one of those children. The join compares strings under the
        // database collation, which is case-insensitive — and it must be: the same child is on file
        // as both "Maya Abell" and "maya abell".
        var candidateFamilyIds = await (
            from srcReg in _context.Registrations
            join srcUser in _context.AspNetUsers on srcReg.UserId equals srcUser.Id
            join othUser in _context.AspNetUsers
                on new { srcUser.FirstName, srcUser.LastName, srcUser.Dob }
                equals new { othUser.FirstName, othUser.LastName, othUser.Dob }
            join othReg in _context.Registrations on othUser.Id equals othReg.UserId
            where srcReg.FamilyUserId == sourceFamilyUserId
               && srcReg.BActive == true
               && srcUser.Dob != null
               && othReg.BActive == true
               && othReg.FamilyUserId != null
               && othReg.FamilyUserId != sourceFamilyUserId
            select othReg.FamilyUserId!
        ).AsNoTracking().Distinct().ToListAsync(ct);

        var allFamilyIds = candidateFamilyIds.Append(sourceFamilyUserId).Distinct().ToList();
        var accounts = await HydrateFamilyAccountsAsync(allFamilyIds, sourceChildKeys, ct);

        if (!accounts.TryGetValue(sourceFamilyUserId, out var sourceDto)) return EmptyCandidates();

        // A family merge is PAIRWISE — it moves the SOURCE household onto the ONE target the admin
        // picks. It does NOT sweep every candidate, and the asymmetry with the person merge above is
        // deliberate:
        //
        //   (name + DOB + role) identifies ONE human — every account matching it IS that human, so
        //   sweeping them all is right.
        //
        //   "shares a child" identifies households that OVERLAP, which is not the same as being the
        //   same household. Divorced parents legitimately share a child across two households.
        //   Sweeping would silently fuse them. Contract §1.
        return new MergeCandidatesResponse
        {
            Source = sourceDto,
            Candidates = [.. accounts.Values
                .Where(a => a.UserId != sourceFamilyUserId)
                .OrderByDescending(a => a.Children.Count(c => c.MatchesSource))
                .ThenByDescending(a => a.RegistrationCount)
                .ThenBy(a => a.UserName)],
            RegistrationsAffected = sourceDto.RegistrationCount,
            AccountsAffected = 1
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Merge operations
    // ═══════════════════════════════════════════════════════════

    public async Task<MergeResultDto> MergeUserRegistrationsAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default)
    {
        // The target must be one of the accounts we OFFERED. Resolving it straight out of
        // AspNetUsers, as legacy did, let the API re-point registrations onto any account in the
        // system — the candidate list was decoration.
        var preview = await GetUserMergeCandidatesAsync(registrationId, ct);

        var target = preview.Candidates.FirstOrDefault(c =>
            string.Equals(c.UserName, targetUserName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"'{targetUserName}' is not a merge candidate for this registration.");

        var source = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RegistrationId == registrationId
            select new { u.FirstName, u.LastName, u.Dob, u.Email, r.RoleId }
        ).AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found.");

        if (source.FirstName == null || source.LastName == null)
            throw new InvalidOperationException("This registration's user has no name; identity cannot be established.");

        // The SAME work-set the preview showed the admin. One definition — see PersonWorkSet.
        var regs = await PersonWorkSet(source.RoleId, source.FirstName, source.LastName, source.Dob, source.Email)
            .ToListAsync(ct);

        // Snapshot the old owner BEFORE the write. This is the reversal key and it cannot be
        // reconstructed afterwards — the sweep pulls registrations off every seasonal duplicate of
        // this child at once, so there is no single "previous account" to work back to.
        var moved = regs
            .Where(r => r.UserId != null && r.UserId != target.UserId)
            .Select(r => new MergedRegistrationDto
            {
                RegistrationId = r.RegistrationId,
                PreviousUserId = r.UserId!
            })
            .ToList();

        foreach (var reg in regs)
        {
            reg.UserId = target.UserId;
        }

        await _context.SaveChangesAsync(ct);

        return new MergeResultDto
        {
            TargetUserId = target.UserId,
            TargetUserName = target.UserName,
            Moved = moved
        };
    }

    public async Task<MergeResultDto> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default)
    {
        var preview = await GetFamilyMergeCandidatesAsync(registrationId, ct);

        var target = preview.Candidates.FirstOrDefault(c =>
            string.Equals(c.UserName, targetFamilyUserName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"'{targetFamilyUserName}' is not a merge candidate for this family. The target must be " +
                "a login that shares a child with this household.");

        var sourceFamilyUserId = await _context.Registrations
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => r.FamilyUserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} has no family account.");

        // PAIRWISE — only the SOURCE household moves, onto the one target the admin chose.
        // See GetFamilyMergeCandidatesAsync for why this does not sweep like the person merge does.
        var regs = await _context.Registrations
            .Where(r => r.FamilyUserId == sourceFamilyUserId && r.BActive == true)
            .ToListAsync(ct);

        var moved = regs
            .Select(r => new MergedRegistrationDto
            {
                RegistrationId = r.RegistrationId,
                PreviousUserId = sourceFamilyUserId
            })
            .ToList();

        foreach (var reg in regs)
        {
            reg.FamilyUserId = target.UserId;
        }

        await _context.SaveChangesAsync(ct);

        return new MergeResultDto
        {
            TargetUserId = target.UserId,
            TargetUserName = target.UserName,
            Moved = moved
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Hydration — turning account ids into something an admin can actually judge
    // ═══════════════════════════════════════════════════════════

    private const int MaxJobsShown = 6;
    private const int MaxChildrenShown = 12;

    /// <summary>Case-folded key component. The same child is on file as both "Maya Abell" and "maya abell".</summary>
    private static string Fold(string? s) => (s ?? "").Trim().ToLowerInvariant();

    private static string? NameOrNull(string? first, string? last)
    {
        var s = $"{first} {last}".Trim();
        return s.Length == 0 ? null : s;
    }

    private static MergeCandidatesResponse EmptyCandidates() => new()
    {
        Source = new MergeCandidateDto { UserName = "", UserId = "", RegistrationCount = 0, Children = [], Jobs = [] },
        Candidates = [],
        RegistrationsAffected = 0,
        AccountsAffected = 0
    };

    private sealed record Household(string? MomName, string? MomEmail, string? DadName, string? DadEmail);

    private async Task<Dictionary<string, Household>> LoadHouseholdsAsync(
        IReadOnlyCollection<string> familyUserIds,
        CancellationToken ct)
    {
        if (familyUserIds.Count == 0) return [];

        var rows = await _context.Families
            .Where(f => familyUserIds.Contains(f.FamilyUserId))
            .Select(f => new
            {
                f.FamilyUserId,
                f.MomFirstName,
                f.MomLastName,
                f.MomEmail,
                f.DadFirstName,
                f.DadLastName,
                f.DadEmail
            })
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.ToDictionary(
            f => f.FamilyUserId,
            f => new Household(
                NameOrNull(f.MomFirstName, f.MomLastName), f.MomEmail,
                NameOrNull(f.DadFirstName, f.DadLastName), f.DadEmail));
    }

    /// <summary>Hydrate PERSON accounts (a player, or an adult) — the merge candidates for a person.</summary>
    private async Task<Dictionary<string, MergeCandidateDto>> HydratePersonAccountsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        var rows = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            where userIds.Contains(u.Id) && r.BActive == true
            select new
            {
                u.Id,
                u.UserName,
                u.Email,
                u.FirstName,
                u.LastName,
                u.Dob,
                j.JobName,
                r.FamilyUserId
            }
        ).AsNoTracking().ToListAsync(ct);

        var familyIds = rows
            .Select(x => x.FamilyUserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct()
            .ToList();

        var households = await LoadHouseholdsAsync(familyIds, ct);

        return rows.GroupBy(x => x.Id).ToDictionary(g => g.Key, g =>
        {
            var first = g.First();
            var famId = g.Select(x => x.FamilyUserId).FirstOrDefault(id => !string.IsNullOrEmpty(id));
            var hh = famId != null && households.TryGetValue(famId, out var h) ? h : null;

            return new MergeCandidateDto
            {
                UserId = first.Id,
                UserName = first.UserName ?? "",
                Email = first.Email,
                PersonName = NameOrNull(first.FirstName, first.LastName),
                Dob = first.Dob,
                MomName = hh?.MomName,
                MomEmail = hh?.MomEmail,
                DadName = hh?.DadName,
                DadEmail = hh?.DadEmail,
                Children = [],
                RegistrationCount = g.Count(),
                Jobs = [.. g.Select(x => x.JobName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!)
                            .Distinct().OrderBy(n => n).Take(MaxJobsShown)]
            };
        });
    }

    /// <summary>
    /// Hydrate FAMILY LOGINS, each with the children it owns. The children ARE the evidence: a family
    /// username is frequently unrecognisable, and the parents' names are typed inconsistently, so the
    /// only way an admin can confirm "this is the same household" is to see the same kids.
    /// </summary>
    private async Task<Dictionary<string, MergeCandidateDto>> HydrateFamilyAccountsAsync(
        IReadOnlyCollection<string> familyUserIds,
        IReadOnlySet<(string, string, DateTime?)> sourceChildKeys,
        CancellationToken ct)
    {
        if (familyUserIds.Count == 0) return [];

        var logins = await _context.AspNetUsers
            .Where(u => familyUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.UserName, u.Email })
            .AsNoTracking()
            .ToListAsync(ct);

        var households = await LoadHouseholdsAsync(familyUserIds, ct);

        var childRows = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            where r.FamilyUserId != null && familyUserIds.Contains(r.FamilyUserId) && r.BActive == true
            select new { FamilyUserId = r.FamilyUserId!, u.FirstName, u.LastName, u.Dob, j.JobName }
        ).AsNoTracking().ToListAsync(ct);

        var byFamily = childRows
            .GroupBy(x => x.FamilyUserId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return logins.ToDictionary(login => login.Id, login =>
        {
            var rows = byFamily.TryGetValue(login.Id, out var r) ? r : [];
            var hh = households.TryGetValue(login.Id, out var h) ? h : null;

            var children = rows
                .GroupBy(x => (Fold(x.FirstName), Fold(x.LastName), x.Dob))
                .Select(g => new MergeCandidateChildDto
                {
                    Name = NameOrNull(g.First().FirstName, g.First().LastName) ?? "(no name)",
                    Dob = g.Key.Item3,
                    MatchesSource = sourceChildKeys.Contains(g.Key)
                })
                .OrderByDescending(c => c.MatchesSource)
                .ThenBy(c => c.Name)
                .Take(MaxChildrenShown)
                .ToList();

            return new MergeCandidateDto
            {
                UserId = login.Id,
                UserName = login.UserName ?? "",
                Email = login.Email,
                // A family login is not a person — it is a household. The people are the parents
                // and the children below.
                PersonName = null,
                Dob = null,
                MomName = hh?.MomName,
                MomEmail = hh?.MomEmail,
                DadName = hh?.DadName,
                DadEmail = hh?.DadEmail,
                Children = children,
                RegistrationCount = rows.Count,
                Jobs = [.. rows.Select(x => x.JobName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!)
                             .Distinct().OrderBy(n => n).Take(MaxJobsShown)]
            };
        });
    }
}
