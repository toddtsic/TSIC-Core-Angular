using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
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
        var rows = await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join uP in _context.AspNetUsers on r.UserId equals uP.Id
            join uF in _context.AspNetUsers on r.FamilyUserId equals uF.Id
            join f in _context.Families on uF.Id equals f.FamilyUserId
            where r.RoleId == request.RoleId && familyUserIds.Contains(uF.Id)
            orderby uF.UserName, uP.LastName, uP.FirstName, c.CustomerName, j.JobName
            select new
            {
                FamilyUserId = uF.Id,
                Dto = new ChangePasswordSearchResultDto
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
                    DadPhone = f.DadCellphone,
                    MergeCandidateCount = 0   // annotated below — it needs the key, which SQL cannot compute
                }
            }
        ).AsNoTracking().Take(MaxRows).ToListAsync(ct);

        var counts = await CountFamilyMergeCandidatesAsync(familyUserIds, ct);

        return [.. rows.Select(x => x.Dto with
        {
            MergeCandidateCount = counts.TryGetValue(x.FamilyUserId, out var n) ? n : 0
        })];
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
        var rows = await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RoleId == request.RoleId && userIds.Contains(u.Id)
            orderby u.UserName, c.CustomerName, j.JobName
            select new
            {
                UserId = u.Id,
                Dto = new ChangePasswordSearchResultDto
                {
                    RegistrationId = r.RegistrationId,
                    RoleName = role.Name ?? "",
                    CustomerName = c.CustomerName ?? "",
                    JobName = j.JobName ?? "",
                    UserName = u.UserName ?? "",
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Email = u.Email,
                    Phone = u.Cellphone,
                    MergeCandidateCount = 0   // annotated below
                }
            }
        ).AsNoTracking().Take(MaxRows).ToListAsync(ct);

        var counts = await CountAdultMergeCandidatesAsync(userIds, request.RoleId, ct);

        return [.. rows.Select(x => x.Dto with
        {
            MergeCandidateCount = counts.TryGetValue(x.UserId, out var n) ? n : 0
        })];
    }

    // ═══════════════════════════════════════════════════════════
    //  "Is a merge even possible here?" — answered in the SEARCH
    // ═══════════════════════════════════════════════════════════
    //
    //  An admin must never open a merge dialog to be told there is nothing in it. So the search tells
    //  each row how many OTHER logins key to its account, and the button only appears when that is
    //  nonzero.
    //
    //  This CANNOT be a subquery. The key is email AND phone AND name, and two of those three
    //  (digits-only, Soundex) have no SQL translation — so the shape is: one query to fetch the keys of
    //  the logins on screen, ONE query to pull every account sharing any of those emails, one to find
    //  which of them own registrations, and the AND is finished in memory. Three queries for the whole
    //  page, not one per row.
    //
    //  It applies the SAME rules as the merge itself — placeholders have no key, and an account that
    //  owns no registrations is not a candidate — so the number on the button IS the number in the
    //  dialog. If those two ever disagree, this is what drifted.

    /// <summary>Households that key to the same MOTHER as each of these family logins. Excludes itself.</summary>
    private async Task<Dictionary<string, int>> CountFamilyMergeCandidatesAsync(
        IReadOnlyCollection<string> familyUserIds,
        CancellationToken ct)
    {
        if (familyUserIds.Count == 0) return [];

        var moms = await _context.Families
            .AsNoTracking()
            .Where(f => familyUserIds.Contains(f.FamilyUserId))
            .Select(f => new { f.FamilyUserId, f.MomFirstName, f.MomLastName, f.MomEmail, f.MomCellphone })
            .ToListAsync(ct);

        var keyed = new List<(string Id, AccountKey Key)>();
        foreach (var m in moms)
        {
            if (AccountKey.TryCreate(m.MomEmail, m.MomCellphone, m.MomFirstName, m.MomLastName, out var key))
                keyed.Add((m.FamilyUserId, key));
        }

        // No key, no candidates — a placeholder is not an identity, so those rows get no merge button
        // and the reason is the same one the dialog would have given them.
        if (keyed.Count == 0) return [];

        var emails = keyed.Select(k => k.Key.Email).Distinct().ToList();

        var pool = await _context.Families
            .AsNoTracking()
            .Where(f => f.MomEmail != null && emails.Contains(f.MomEmail.Trim().ToLower()))
            .Select(f => new { f.FamilyUserId, f.MomEmail, f.MomCellphone, f.MomFirstName, f.MomLastName })
            .ToListAsync(ct);

        var owners = await OwningFamilyLoginsAsync([.. pool.Select(p => p.FamilyUserId).Distinct()], ct);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, key) in keyed)
        {
            counts[id] = pool.Count(p =>
                !string.Equals(p.FamilyUserId, id, StringComparison.Ordinal)
                && owners.Contains(p.FamilyUserId)
                && key.Matches(p.MomEmail, p.MomCellphone, p.MomFirstName, p.MomLastName));
        }

        return counts;
    }

    /// <summary>Adult logins that are the same person as each of these, IN THE SAME ROLE. Excludes itself.</summary>
    private async Task<Dictionary<string, int>> CountAdultMergeCandidatesAsync(
        IReadOnlyCollection<string> userIds,
        string roleId,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        var people = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email, u.Cellphone, u.Phone })
            .ToListAsync(ct);

        var keyed = new List<(string Id, AccountKey Key)>();
        foreach (var p in people)
        {
            if (AccountKey.TryCreate(p.Email, p.Cellphone ?? p.Phone, p.FirstName, p.LastName, out var key))
                keyed.Add((p.Id, key));
        }

        if (keyed.Count == 0) return [];

        var emails = keyed.Select(k => k.Key.Email).Distinct().ToList();

        var pool = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.Email != null && emails.Contains(u.Email.Trim().ToLower()))
            .Select(u => new { u.Id, u.Email, u.Cellphone, u.Phone, u.FirstName, u.LastName })
            .ToListAsync(ct);

        var owners = await OwningAdultLoginsAsync([.. pool.Select(p => p.Id).Distinct()], roleId, ct);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, key) in keyed)
        {
            counts[id] = pool.Count(p =>
                !string.Equals(p.Id, id, StringComparison.Ordinal)
                && owners.Contains(p.Id)
                && key.Matches(p.Email, p.Cellphone ?? p.Phone, p.FirstName, p.LastName));
        }

        return counts;
    }

    /// <summary>Which of these family logins own at least one registration. A login that owns nothing is
    /// not a candidate — that is what makes a retired one self-hide.</summary>
    private async Task<HashSet<string>> OwningFamilyLoginsAsync(
        IReadOnlyCollection<string> familyUserIds,
        CancellationToken ct)
    {
        if (familyUserIds.Count == 0) return [];

        var ids = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.FamilyUserId != null && familyUserIds.Contains(r.FamilyUserId))
            .Select(r => r.FamilyUserId!)
            .Distinct()
            .ToListAsync(ct);

        return [.. ids];
    }

    /// <summary>Which of these adult logins own at least one registration IN THIS ROLE.</summary>
    private async Task<HashSet<string>> OwningAdultLoginsAsync(
        IReadOnlyCollection<string> userIds,
        string roleId,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        var ids = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.UserId != null && userIds.Contains(r.UserId) && r.RoleId == roleId)
            .Select(r => r.UserId!)
            .Distinct()
            .ToListAsync(ct);

        return [.. ids];
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

    /// <summary>
    /// Everything the reset dialog shows BEFORE anyone types a password.
    ///
    /// The row the admin clicked is a REGISTRATION — for a player, that is the CHILD. The account being
    /// changed is a different row entirely, because a player has no usable login. So the dialog reads:
    /// the row you clicked → the account you are changing → what that account signs in for. The last one
    /// is what stops the mistake, and it is why this is a server call rather than the grid's own cells.
    /// </summary>
    public async Task<ResetContextDto?> GetResetContextAsync(
        Guid registrationId,
        ResetPasswordTarget target,
        CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => new { r.UserId, r.FamilyUserId })
            .FirstOrDefaultAsync(ct);

        if (reg is null) return null;

        var accountId = target == ResetPasswordTarget.Family ? reg.FamilyUserId : reg.UserId;
        if (string.IsNullOrWhiteSpace(accountId)) return null;

        var account = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.Id == accountId && u.UserName != null)
            .Select(u => new
            {
                u.Id,
                UserName = u.UserName!,
                u.Email,
                u.FirstName,
                u.LastName,
                u.Cellphone,
                u.Phone
            })
            .FirstOrDefaultAsync(ct);

        if (account is null) return null;

        if (target == ResetPasswordTarget.Family)
        {
            var mom = await LoadMomAsync(account.Id, ct);

            // The children this login selects between once it is signed in. This is THE line that tells
            // the admin they are holding the whole household's credential, not one child's.
            var childRows = await (
                from r in _context.Registrations
                join u in _context.AspNetUsers on r.UserId equals u.Id
                where r.FamilyUserId == account.Id
                select new { u.FirstName, u.LastName, u.Dob }
            ).AsNoTracking().ToListAsync(ct);

            var children = childRows
                .GroupBy(c => (Fold(c.FirstName), Fold(c.LastName), c.Dob))
                .Select(g => new AccountReachDto
                {
                    Label = NameOrNull(g.First().FirstName, g.First().LastName) ?? "(no name)",
                    Dob = g.Key.Item3
                })
                .OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new ResetContextDto
            {
                Target = target,
                UserName = account.UserName,
                // The LOGIN's address — that is the account being changed, and it is what the public
                // forgot-password flow looks up. Fall back to the mother's only so the line is not blank.
                Email = account.Email ?? mom?.Email,
                OwnerName = NameOrNull(mom?.FirstName, mom?.LastName),
                OwnerPhone = mom?.Phone,
                IsFamilyLogin = true,
                SignsInFor = children
            };
        }

        // An adult signs in as themselves. What tells the admin WHICH John Smith is the role and the
        // events he holds — so that is what "signs in for" means on this side.
        var heldRows = await (
            from r in _context.Registrations
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            where r.UserId == account.Id
            select new { RoleName = role.Name, c.CustomerName, j.JobName }
        ).AsNoTracking().ToListAsync(ct);

        var held = heldRows
            .Select(x => new AccountReachDto
            {
                Label = x.RoleName ?? "(no role)",
                Where = Where(x.CustomerName, x.JobName)
            })
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Where, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ResetContextDto
        {
            Target = target,
            UserName = account.UserName,
            Email = account.Email,
            OwnerName = NameOrNull(account.FirstName, account.LastName),
            OwnerPhone = account.Cellphone ?? account.Phone,
            IsFamilyLogin = false,
            SignsInFor = held
        };
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
    //  Merge — the identity key
    // ═══════════════════════════════════════════════════════════
    //
    //  ONE key, defined once, in TSIC.Domain/Constants/HouseholdIdentity.cs:
    //
    //      email  AND  phone  AND  name          all three, normalized. Placeholder = no key.
    //
    //  It is a SECURITY control, not a matching heuristic — read that file before touching anything
    //  here. A merge that gets the identity wrong hands one family another family's children, across
    //  customers, irreversibly.
    //
    //  WHAT THIS REPLACED. Legacy derived its work-set from the registration and swept every
    //  registration IN THE SYSTEM whose user matched (FirstName, LastName, RoleId) — with DOB and email
    //  branches that treated NULL as "matches anything". The candidate list was decoration; the write
    //  re-computed its own set from field equality and could reach any household in any customer. Both
    //  sweeps are gone. The write below is bounded by the two accounts the SuperUser named, re-validated
    //  against the candidate list the key produced, and re-read server-side by FK.
    //
    //  ONE RETIREMENT PER ACT. Never a list. A parent with four logins is three deliberate merges —
    //  three confirmations, three audit lines, three independently reversible operations. A multi-select
    //  puts a dozen irreversible cross-tenant writes behind one button.
    //
    //  AND A RETIRED LOGIN SELF-HIDES. Candidates are the accounts that OWN registrations; a merge
    //  leaves the retiree owning none, so it can never be offered again. No flag, no column, no schema.

    private sealed record Mom(string? FirstName, string? LastName, string? Email, string? Phone);

    /// <summary>The mother, who IS the family account's identity. Null if the household has no row.</summary>
    private async Task<Mom?> LoadMomAsync(string familyUserId, CancellationToken ct)
        => await _context.Families
            .AsNoTracking()
            .Where(f => f.FamilyUserId == familyUserId)
            .Select(f => new Mom(f.MomFirstName, f.MomLastName, f.MomEmail, f.MomCellphone))
            .FirstOrDefaultAsync(ct);

    /// <summary>One resolved merge: the identity, and every login that keys to it.</summary>
    private sealed record AdultMerge(AccountKey Key, string RoleId, string RoleName, List<MergeCandidateDto> Accounts);
    private sealed record FamilyMerge(AccountKey Key, List<MergeCandidateDto> Accounts);

    /// <summary>
    /// The security boundary, enforced. Both names must be accounts the identity key actually produced —
    /// so a stale, hand-edited or forged body cannot introduce a login the key never approved — and they
    /// must be two different accounts.
    /// </summary>
    private static (MergeCandidateDto Keep, MergeCandidateDto Retire) ResolvePair(
        IReadOnlyList<MergeCandidateDto> offered,
        string keepUserName,
        string retireUserName,
        string requirement)
    {
        var byName = offered.ToDictionary(a => a.UserName, StringComparer.OrdinalIgnoreCase);

        if (!byName.TryGetValue(keepUserName, out var keep))
            throw new InvalidOperationException($"'{keepUserName}' is not a merge candidate here. {requirement}");

        if (!byName.TryGetValue(retireUserName, out var retire))
            throw new InvalidOperationException($"'{retireUserName}' is not a merge candidate here. {requirement}");

        if (string.Equals(keep.UserId, retire.UserId, StringComparison.Ordinal))
            throw new InvalidOperationException("The login to keep and the login to retire are the same account.");

        return (keep, retire);
    }

    // ═══════════════════════════════════════════════════════════
    //  Adult merge — one human, two logins, ONE role
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// ADULTS MERGE WITHIN A ROLE. A Club Rep collapses onto a Club Rep and never onto the same person's
    /// Staff login: those two accounts carry different permissions and are deliberately separate records
    /// of the same human. Legacy had this (<c>r.RoleId == registrantKeys.RoleId</c>); it is the one part
    /// of its sweep that was right, and it constrains BOTH the candidate list and the rows that move.
    ///
    /// Returns null when the registration is a player, or when the account has no identity — a blank,
    /// malformed or placeholder contact block. No key, no candidates. That is the safe outcome.
    /// </summary>
    private async Task<AdultMerge?> LoadAdultMergeAsync(Guid registrationId, CancellationToken ct)
    {
        var source = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            where r.RegistrationId == registrationId
            select new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.Cellphone,
                u.Phone,
                r.RoleId,
                RoleName = role.Name
            }
        ).AsNoTracking().SingleOrDefaultAsync(ct);

        if (source is null) return null;

        // PLAYERS ARE REFUSED HERE, deliberately. A player has no login and no independent existence —
        // they are a child inside a household, and a child is collapsed only as part of that household's
        // merge, inside that boundary. Legacy's global (name + DOB + role) player sweep reached every
        // customer in the system: 33,214 clusters of player accounts share a name and a birthday.
        if (string.Equals(source.RoleId, RoleConstants.Player, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!AccountKey.TryCreate(
                source.Email, source.Cellphone ?? source.Phone, source.FirstName, source.LastName, out var key))
        {
            return null;
        }

        // The email half of the AND runs in SQL, because one exact address is highly selective. The phone
        // and name halves run in memory, because digits-only and Soundex have no SQL translation — and
        // what reaches memory is the handful of accounts sharing that one address.
        var sameEmail = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.Email != null && u.Email.Trim().ToLower() == key.Email)
            .Select(u => new { u.Id, u.Email, u.Cellphone, u.Phone, u.FirstName, u.LastName })
            .ToListAsync(ct);

        var matchedIds = sameEmail
            .Where(u => key.Matches(u.Email, u.Cellphone ?? u.Phone, u.FirstName, u.LastName))
            .Select(u => u.Id)
            .Distinct()
            .ToList();

        var accounts = await HydrateAdultAccountsAsync(matchedIds, source.RoleId, ct);

        return new AdultMerge(key, source.RoleId, source.RoleName ?? "", accounts);
    }

    public async Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        var merge = await LoadAdultMergeAsync(registrationId, ct);

        return merge is null
            ? NoCandidates()
            : new MergeCandidatesResponse
            {
                Identity = Identity(merge.Key),
                Accounts = merge.Accounts,
                RoleName = merge.RoleName
            };
    }

    /// <summary>
    /// Retire one duplicate ADULT login onto the one the person asked for. Only their registrations
    /// IN THIS ROLE move — see <see cref="LoadAdultMergeAsync"/>. If the same person also has a duplicate
    /// Coach login, that is a second, separate merge.
    /// </summary>
    public async Task<MergeResultDto> MergeUserRegistrationsAsync(
        Guid registrationId,
        string keepUserName,
        string retireUserName,
        CancellationToken ct = default)
    {
        var merge = await LoadAdultMergeAsync(registrationId, ct)
            ?? throw new InvalidOperationException(
                "This registration has no merge identity. An account can only be merged when its email, "
                + "phone and name are all present and real — a placeholder is not an identity.");

        var (keep, retire) = ResolvePair(merge.Accounts, keepUserName, retireUserName,
            "Both logins must key to the same person — same email, same phone, same name — and both must "
            + "hold a registration in this role.");

        // Re-read by FK, and CONSTRAINED TO THE ROLE. Never by name: the write must not be able to reach
        // a row the candidate list did not account for.
        var regs = await _context.Registrations
            .Where(r => r.UserId == retire.UserId && r.RoleId == merge.RoleId)
            .ToListAsync(ct);

        // Snapshot the previous owner BEFORE the write — the only way back. Afterwards the surviving
        // account owns rows that used to be someone else's and nothing on the row says which.
        var moved = regs
            .Select(r => new MergedRegistrationDto
            {
                RegistrationId = r.RegistrationId,
                PreviousUserId = r.UserId!
            })
            .ToList();

        foreach (var reg in regs)
        {
            reg.UserId = keep.UserId;
        }

        await _context.SaveChangesAsync(ct);

        return new MergeResultDto
        {
            KeepUserId = keep.UserId,
            KeepUserName = keep.UserName,
            RetireUserName = retire.UserName,
            Moved = moved,
            ChildrenCollapsed = 0
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  Family merge — the one this tool exists for
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Family logins that are the SAME HOUSEHOLD as this registration's.
    ///
    /// The scenario: a parent forgets their credentials, creates a brand new family account, re-registers
    /// their children under it, and then calls and asks us to put everything back on one login they can
    /// actually get into. Two family logins, two copies of every child.
    ///
    /// NOT keyed on the child, tempting as the coverage is. "Owns the same child" says two households
    /// OVERLAP — divorced parents legitimately share one — not that they ARE one. Keyed on the child,
    /// merging the father's login would land the mother's new husband's son in it. Identity, not overlap.
    /// </summary>
    private async Task<FamilyMerge?> LoadFamilyMergeAsync(Guid registrationId, CancellationToken ct)
    {
        var sourceFamilyUserId = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == registrationId)
            .Select(r => r.FamilyUserId)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(sourceFamilyUserId)) return null;

        var mom = await LoadMomAsync(sourceFamilyUserId, ct);

        // NO KEY → NO CANDIDATES. A household whose contact block is blank, malformed, or a placeholder
        // (0000000000 sits on 106 of them) cannot be identified — so we will not claim that anybody else
        // is it. The SuperUser gets nothing, and the parent is told to use their new account. Fine outcome.
        if (mom is null ||
            !AccountKey.TryCreate(mom.Email, mom.Phone, mom.FirstName, mom.LastName, out var key))
        {
            return null;
        }

        var sameEmail = await _context.Families
            .AsNoTracking()
            .Where(f => f.MomEmail != null && f.MomEmail.Trim().ToLower() == key.Email)
            .Select(f => new { f.FamilyUserId, f.MomEmail, f.MomCellphone, f.MomFirstName, f.MomLastName })
            .ToListAsync(ct);

        // MORE THAN TWO CANDIDATES IS NORMAL AND IS NOT A RED FLAG. A parent who has forgotten their
        // password twice has three logins; the worst real household on file has eleven. They all key to
        // the same mother, so they are all the same household — and each is retired one at a time.
        var matchedIds = sameEmail
            .Where(f => key.Matches(f.MomEmail, f.MomCellphone, f.MomFirstName, f.MomLastName))
            .Select(f => f.FamilyUserId)
            .Distinct()
            .ToList();

        var accounts = await HydrateFamilyAccountsAsync(matchedIds, ct);

        return new FamilyMerge(key, accounts);
    }

    public async Task<MergeCandidatesResponse> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        var merge = await LoadFamilyMergeAsync(registrationId, ct);

        return merge is null
            ? NoCandidates()
            : new MergeCandidatesResponse
            {
                Identity = Identity(merge.Key),
                Accounts = merge.Accounts,
                // A family login owns players and nothing else, so there is no role to constrain.
                RoleName = null
            };
    }

    /// <summary>
    /// Retire one duplicate family login onto the one the parent asked for. Two writes, both bounded by
    /// the pair the SuperUser named and re-validated against the candidate list:
    ///
    ///   1. FAMILY. Every registration under the retiring login is re-pointed. EVERY one — including
    ///      inactive ones. Legacy filtered on BActive, which orphaned a parent's dropped and pending
    ///      registrations on a login nobody can sign into again. The whole point of the operation is that
    ///      the parent gets their history back.
    ///
    ///   2. CHILDREN. Without this the parent signs into the account they asked for and sees Maya twice
    ///      and Ethan twice — one player row from each login that was just fused. So each child is
    ///      collapsed onto the surviving login's row for that child.
    ///
    /// The child collapse is UNAMBIGUOUS-ONLY, and that restriction is load-bearing: a family may
    /// deliberately hold two player rows for one child, created to get a second registration past an
    /// event's one-per-player rule. If either side has two rows for the same (name, DOB) we do not know
    /// which is which, so we leave BOTH alone rather than silently fuse a workaround back together.
    /// </summary>
    public async Task<MergeResultDto> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string keepUserName,
        string retireUserName,
        CancellationToken ct = default)
    {
        var merge = await LoadFamilyMergeAsync(registrationId, ct)
            ?? throw new InvalidOperationException(
                "This household has no merge identity. A family login can only be merged when the mother's "
                + "email, phone and name are all present and real — a placeholder is not an identity.");

        var (keep, retire) = ResolvePair(merge.Accounts, keepUserName, retireUserName,
            "Both logins must key to the same mother — same email, same phone, same name.");

        // ── 1. the registrations ──────────────────────────────────────────────
        // Re-read by FK. NOT by name, and NOT from what the search happened to return: a search for
        // "Abell" will not return a sibling retyped "Shimizu", and that sibling's registrations still
        // have to move.
        var regs = await _context.Registrations
            .Where(r => r.FamilyUserId == retire.UserId)
            .ToListAsync(ct);

        // Snapshot the previous owner BEFORE the write. This is the ONLY way back — afterwards the
        // surviving login owns registrations that used to be another account's, and nothing on the row
        // says which.
        var moved = regs
            .Select(r => new MergedRegistrationDto
            {
                RegistrationId = r.RegistrationId,
                PreviousUserId = r.FamilyUserId!
            })
            .ToList();

        // ── 2. the children ───────────────────────────────────────────────────
        var childMap = await BuildChildCollapseAsync(retire.UserId, keep.UserId, ct);

        foreach (var reg in regs)
        {
            reg.FamilyUserId = keep.UserId;

            if (reg.UserId != null && childMap.TryGetValue(reg.UserId, out var survivingChildId))
                reg.UserId = survivingChildId;
        }

        await _context.SaveChangesAsync(ct);

        return new MergeResultDto
        {
            KeepUserId = keep.UserId,
            KeepUserName = keep.UserName,
            RetireUserName = retire.UserName,
            Moved = moved,
            ChildrenCollapsed = childMap.Count
        };
    }

    /// <summary>
    /// Maps each retiring player row to the surviving household's row for the same child:
    /// <c>{ Maya_old → Maya_keep, Ethan_old → Ethan_keep }</c>.
    ///
    /// Scoped to the two accounts being merged. It cannot reach an unrelated household, and it cannot
    /// touch a deliberate double-registration in a family that is not part of this merge.
    ///
    /// A child is collapsed only when BOTH sides hold exactly one row for that (name, DOB). Two rows on
    /// either side is ambiguous — it is what a deliberate double-registration looks like — so that child
    /// is left exactly as it is, and both rows come across still separate.
    /// </summary>
    private async Task<Dictionary<string, string>> BuildChildCollapseAsync(
        string retireFamilyUserId,
        string keepFamilyUserId,
        CancellationToken ct)
    {
        var rows = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.FamilyUserId == retireFamilyUserId || r.FamilyUserId == keepFamilyUserId
            select new { FamilyUserId = r.FamilyUserId!, UserId = u.Id, u.FirstName, u.LastName, u.Dob }
        ).AsNoTracking().Distinct().ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var child in rows.GroupBy(x => (Fold(x.FirstName), Fold(x.LastName), x.Dob)))
        {
            var onKeep = child.Where(x => x.FamilyUserId == keepFamilyUserId)
                              .Select(x => x.UserId).Distinct().ToList();

            var onRetire = child.Where(x => x.FamilyUserId == retireFamilyUserId)
                                .Select(x => x.UserId).Distinct().ToList();

            // The survivor does not have this child at all — their row comes across untouched, still
            // pointing at itself. (And if the retiring login holds two rows for them, both come across,
            // still separate: whatever made them two rows is not ours to undo.)
            if (onKeep.Count != 1) continue;
            if (onRetire.Count != 1) continue;

            map[onRetire[0]] = onKeep[0];
        }

        return map;
    }

    // ═══════════════════════════════════════════════════════════
    //  Hydration — turning account ids into something an admin can actually judge
    // ═══════════════════════════════════════════════════════════
    //
    //  Half the family usernames in this system are raw GUIDs. Two dropdowns of
    //  76da3519-7842-400e-84ed-4ea6005e974c are not a decision, they are a coin flip — so a candidate
    //  carries its identity block, its children, and EVERY registration a merge would move. The admin is
    //  about to relocate a family's whole history; they get to look at it first.

    /// <summary>Case-folded key component. The same child is on file as both "Maya Abell" and "maya abell".</summary>
    private static string Fold(string? s) => (s ?? "").Trim().ToLowerInvariant();

    private static string? NameOrNull(string? first, string? last)
    {
        var s = $"{first} {last}".Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary><c>Steps Lacrosse — Fall League 2025</c>. Customer is the disambiguator on a phone call.</summary>
    private static string Where(string? customerName, string? jobName)
    {
        var c = (customerName ?? "").Trim();
        var j = (jobName ?? "").Trim();

        if (c.Length == 0) return j;
        if (j.Length == 0) return c;
        return $"{c} — {j}";
    }

    /// <summary>No identity → no accounts → no merge. The contact block was blank, malformed, or a
    /// placeholder, and absence must never match absence.</summary>
    private static MergeCandidatesResponse NoCandidates() => new()
    {
        Identity = null,
        Accounts = [],
        RoleName = null
    };

    private static MergeIdentityDto Identity(AccountKey key) => new()
    {
        Name = $"{key.FirstName} {key.LastName}".Trim(),
        Email = key.Email,
        Phone = key.Phone
    };

    private sealed record Household(
        string? MomFirstName, string? MomLastName, string? MomEmail, string? MomPhone,
        string? DadFirstName, string? DadLastName, string? DadEmail);

    private async Task<Dictionary<string, Household>> LoadHouseholdsAsync(
        IReadOnlyCollection<string> familyUserIds,
        CancellationToken ct)
    {
        if (familyUserIds.Count == 0) return [];

        var rows = await _context.Families
            .AsNoTracking()
            .Where(f => familyUserIds.Contains(f.FamilyUserId))
            .Select(f => new
            {
                f.FamilyUserId,
                f.MomFirstName,
                f.MomLastName,
                f.MomEmail,
                f.MomCellphone,
                f.DadFirstName,
                f.DadLastName,
                f.DadEmail
            })
            .ToListAsync(ct);

        return rows.ToDictionary(
            f => f.FamilyUserId,
            f => new Household(
                f.MomFirstName, f.MomLastName, f.MomEmail, f.MomCellphone,
                f.DadFirstName, f.DadLastName, f.DadEmail));
    }

    /// <summary>
    /// Hydrate ADULT logins — one human, one role. Registrations are scoped to that role, because that is
    /// the set a merge would move; a candidate's list must be the truth about what happens if it is retired.
    ///
    /// A login that owns nothing IN THIS ROLE is not offered. That is what makes a retired account
    /// self-hide, and it is why this tool needs no deactivation flag.
    /// </summary>
    private async Task<List<MergeCandidateDto>> HydrateAdultAccountsAsync(
        IReadOnlyCollection<string> userIds,
        string roleId,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return [];

        var logins = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.UserName != null)
            .Select(u => new
            {
                u.Id,
                UserName = u.UserName!,
                u.Email,
                u.FirstName,
                u.LastName,
                u.Cellphone,
                u.Phone
            })
            .ToListAsync(ct);

        // Every registration, not just the active ones — the merge moves them all, so a list that
        // omitted the inactive ones would understate what the admin is about to do.
        var rows = await (
            from r in _context.Registrations
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            where r.UserId != null && userIds.Contains(r.UserId) && r.RoleId == roleId
            select new
            {
                UserId = r.UserId!,
                r.RegistrationId,
                c.CustomerName,
                j.JobName,
                RoleName = role.Name
            }
        ).AsNoTracking().ToListAsync(ct);

        var byUser = rows.GroupBy(x => x.UserId).ToDictionary(g => g.Key, g => g.ToList());

        var accounts = new List<MergeCandidateDto>();

        foreach (var login in logins)
        {
            if (!byUser.TryGetValue(login.Id, out var mine)) continue;

            accounts.Add(new MergeCandidateDto
            {
                UserId = login.Id,
                UserName = login.UserName,
                // An adult signs in as themselves — their account and their identity are one record.
                PersonName = NameOrNull(login.FirstName, login.LastName),
                Email = login.Email,
                Phone = login.Cellphone ?? login.Phone,
                Children = [],
                Registrations = [.. mine
                    .Select(x => new MergeCandidateRegistrationDto
                    {
                        RegistrationId = x.RegistrationId,
                        CustomerName = x.CustomerName ?? "",
                        JobName = x.JobName ?? "",
                        RoleName = x.RoleName ?? "",
                        PersonName = null
                    })
                    .OrderBy(x => x.CustomerName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.JobName, StringComparer.OrdinalIgnoreCase)]
            });
        }

        return [.. accounts
            .OrderByDescending(a => a.Registrations.Count)
            .ThenBy(a => a.UserName, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Hydrate FAMILY LOGINS. The mother's block is the identity key, SHOWN — the admin compares it
    /// across the two panels, and if those are not the same woman they stop. The children are the second
    /// check: a family username is frequently unrecognisable and the parents' names are typed
    /// inconsistently, so seeing the same kids is what confirms the same household.
    ///
    /// A login that owns no registrations is not offered — nothing to move off it, and nothing the parent
    /// could not reach by resetting the password on the login that DOES hold their history. It is also
    /// what makes a retired login self-hide.
    /// </summary>
    private async Task<List<MergeCandidateDto>> HydrateFamilyAccountsAsync(
        IReadOnlyCollection<string> familyUserIds,
        CancellationToken ct)
    {
        if (familyUserIds.Count == 0) return [];

        var logins = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => familyUserIds.Contains(u.Id) && u.UserName != null)
            .Select(u => new { u.Id, UserName = u.UserName! })
            .ToListAsync(ct);

        var households = await LoadHouseholdsAsync(familyUserIds, ct);

        // Every registration — see HydrateAdultAccountsAsync.
        var rows = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            join j in _context.Jobs on r.JobId equals j.JobId
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join role in _context.AspNetRoles on r.RoleId equals role.Id
            where r.FamilyUserId != null && familyUserIds.Contains(r.FamilyUserId)
            select new
            {
                FamilyUserId = r.FamilyUserId!,
                r.RegistrationId,
                ChildUserId = u.Id,
                u.FirstName,
                u.LastName,
                u.Dob,
                c.CustomerName,
                j.JobName,
                RoleName = role.Name
            }
        ).AsNoTracking().ToListAsync(ct);

        var byFamily = rows.GroupBy(x => x.FamilyUserId).ToDictionary(g => g.Key, g => g.ToList());

        var accounts = new List<MergeCandidateDto>();

        foreach (var login in logins)
        {
            if (!byFamily.TryGetValue(login.Id, out var mine)) continue;

            var hh = households.TryGetValue(login.Id, out var h) ? h : null;

            // Grouped by the PLAYER ROW, not by (name, DOB) — so a household that deliberately holds two
            // rows for one child shows two rows. Collapsing them here would hide the exact ambiguity that
            // makes BuildChildCollapseAsync refuse to touch them.
            var children = mine
                .GroupBy(x => x.ChildUserId)
                .Select(g => new MergeCandidateChildDto
                {
                    UserId = g.Key,
                    Name = NameOrNull(g.First().FirstName, g.First().LastName) ?? "(no name)",
                    Dob = g.First().Dob
                })
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            accounts.Add(new MergeCandidateDto
            {
                UserId = login.Id,
                UserName = login.UserName,
                // A family login is not a person — it is a household. The people are the parents in the
                // identity block and the children below it.
                MomName = NameOrNull(hh?.MomFirstName, hh?.MomLastName),
                MomEmail = hh?.MomEmail,
                MomPhone = hh?.MomPhone,
                DadName = NameOrNull(hh?.DadFirstName, hh?.DadLastName),
                DadEmail = hh?.DadEmail,
                Children = children,
                Registrations = [.. mine
                    .Select(x => new MergeCandidateRegistrationDto
                    {
                        RegistrationId = x.RegistrationId,
                        CustomerName = x.CustomerName ?? "",
                        JobName = x.JobName ?? "",
                        RoleName = x.RoleName ?? "",
                        PersonName = NameOrNull(x.FirstName, x.LastName)
                    })
                    .OrderBy(x => x.CustomerName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.JobName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.PersonName, StringComparer.OrdinalIgnoreCase)]
            });
        }

        return [.. accounts
            .OrderByDescending(a => a.Registrations.Count)
            .ThenBy(a => a.UserName, StringComparer.OrdinalIgnoreCase)];
    }
}
