using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
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
    //  sweeps are gone. The write below is bounded by the accounts the SuperUser actually selected,
    //  re-read server-side by their FK.

    private sealed record Mom(string? FirstName, string? LastName, string? Email, string? Phone);

    /// <summary>The mother, who IS the family account's identity. Null if the household has no row.</summary>
    private async Task<Mom?> LoadMomAsync(string familyUserId, CancellationToken ct)
        => await _context.Families
            .AsNoTracking()
            .Where(f => f.FamilyUserId == familyUserId)
            .Select(f => new Mom(f.MomFirstName, f.MomLastName, f.MomEmail, f.MomCellphone))
            .FirstOrDefaultAsync(ct);

    // ═══════════════════════════════════════════════════════════
    //  Adult merge — one human, two logins
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Other logins that are the SAME ADULT. An adult signs in as themselves, so their account and
    /// their identity are one record: the key is their own email + phone + name.
    ///
    /// PLAYERS ARE REFUSED HERE, deliberately. A player has no login and no independent existence — they
    /// are a child inside a household. Legacy merged them on a global (name + DOB + role) sweep, which
    /// reached across every customer in the system and would happily fuse two unrelated children who
    /// share a birthday, or re-fuse a deliberate double-registration (two player rows for one child,
    /// created to get a second registration past an event's one-per-player rule). A child is collapsed
    /// as part of their household's merge, inside that boundary, and nowhere else.
    /// </summary>
    public async Task<MergeCandidatesResponse> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        var source = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RegistrationId == registrationId
            select new { u.Id, u.FirstName, u.LastName, u.Email, u.Cellphone, u.Phone, r.RoleId }
        ).AsNoTracking().SingleOrDefaultAsync(ct);

        if (source is null) return EmptyCandidates();

        if (string.Equals(source.RoleId, RoleConstants.Player, StringComparison.OrdinalIgnoreCase))
            return EmptyCandidates();

        if (!AccountKey.TryCreate(
                source.Email, source.Cellphone ?? source.Phone, source.FirstName, source.LastName, out var key))
        {
            return EmptyCandidates();
        }

        var sameEmail = await _context.AspNetUsers
            .AsNoTracking()
            .Where(u => u.Id != source.Id && u.Email != null && u.Email.Trim().ToLower() == key.Email)
            .Select(u => new { u.Id, u.Email, u.Cellphone, u.Phone, u.FirstName, u.LastName })
            .ToListAsync(ct);

        var candidateIds = sameEmail
            .Where(u => key.Matches(u.Email, u.Cellphone ?? u.Phone, u.FirstName, u.LastName))
            .Select(u => u.Id)
            .ToList();

        if (candidateIds.Count == 0) return EmptyCandidates();

        var accounts = await HydratePersonAccountsAsync([.. candidateIds, source.Id], ct);

        if (!accounts.TryGetValue(source.Id, out var sourceDto)) return EmptyCandidates();

        var candidates = accounts.Values
            .Where(a => a.UserId != source.Id)
            .OrderByDescending(a => a.RegistrationCount)
            .ThenBy(a => a.UserName)
            .ToList();

        return new MergeCandidatesResponse
        {
            Source = sourceDto,
            Candidates = candidates,
            RegistrationsAffected = sourceDto.RegistrationCount + candidates.Sum(c => c.RegistrationCount),
            AccountsAffected = candidates.Count
        };
    }

    /// <summary>
    /// Collapse duplicate ADULT logins onto the one the person asked for. Every registration under a
    /// losing account moves — bounded by the accounts the SuperUser selected, re-validated here against
    /// the candidate set.
    /// </summary>
    public async Task<MergeResultDto> MergeUserRegistrationsAsync(
        Guid registrationId,
        string targetUserName,
        IReadOnlyList<string> sourceUserNames,
        CancellationToken ct = default)
    {
        var preview = await GetUserMergeCandidatesAsync(registrationId, ct);

        var offered = preview.Candidates
            .Append(preview.Source)
            .ToDictionary(c => c.UserName, StringComparer.OrdinalIgnoreCase);

        if (!offered.TryGetValue(targetUserName, out var target))
        {
            throw new InvalidOperationException(
                $"'{targetUserName}' is not a merge candidate for this registration. The target must be " +
                "a login whose email, phone and name all match.");
        }

        var sourceIds = new List<string>();
        foreach (var name in sourceUserNames)
        {
            if (!offered.TryGetValue(name, out var src))
                throw new InvalidOperationException($"'{name}' is not a merge candidate for this registration.");

            if (!string.Equals(src.UserId, target.UserId, StringComparison.Ordinal))
                sourceIds.Add(src.UserId);
        }

        if (sourceIds.Count == 0)
            throw new InvalidOperationException("Select at least one account to merge into the target.");

        // Re-read by FK, not by name. Includes inactive registrations: leaving them on a login nobody
        // can sign into again is how a person loses their own history.
        var regs = await _context.Registrations
            .Where(r => r.UserId != null && sourceIds.Contains(r.UserId))
            .ToListAsync(ct);

        var moved = regs
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

    /// <summary>
    /// Family logins that are the SAME HOUSEHOLD as this registration's.
    ///
    /// The scenario this exists for: a parent forgets their credentials, creates a brand new family
    /// account, re-registers their children under it, and then calls and asks us to put everything back
    /// on one login they can actually get into. Two family logins, two copies of every child.
    ///
    /// NOT keyed on the child, tempting as the coverage is. "Owns the same child" says two households
    /// OVERLAP — divorced parents legitimately share a child — and merging on that basis hands one
    /// parent the other's household. Identity, not overlap.
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

        var mom = await LoadMomAsync(sourceFamilyUserId, ct);

        // NO KEY → NO CANDIDATES. A household whose contact block is blank, malformed, or a placeholder
        // cannot be identified — so we will not claim that anybody else is it. The SuperUser gets an
        // empty list, and the parent is told to use their new account. That is a fine outcome.
        if (mom is null ||
            !AccountKey.TryCreate(mom.Email, mom.Phone, mom.FirstName, mom.LastName, out var key))
        {
            return EmptyCandidates();
        }

        // The email half of the AND runs in SQL, because one exact address is highly selective. The
        // phone and name halves run in memory, because digits-only and Soundex have no SQL translation
        // — and the set reaching memory is the handful of households sharing that one address.
        var sameEmail = await _context.Families
            .AsNoTracking()
            .Where(f => f.FamilyUserId != sourceFamilyUserId
                     && f.MomEmail != null
                     && f.MomEmail.Trim().ToLower() == key.Email)
            .Select(f => new { f.FamilyUserId, f.MomEmail, f.MomCellphone, f.MomFirstName, f.MomLastName })
            .ToListAsync(ct);

        var candidateFamilyIds = sameEmail
            .Where(f => key.Matches(f.MomEmail, f.MomCellphone, f.MomFirstName, f.MomLastName))
            .Select(f => f.FamilyUserId)
            .Distinct()
            .ToList();

        if (candidateFamilyIds.Count == 0) return EmptyCandidates();

        // MORE THAN ONE CANDIDATE IS NORMAL AND IS NOT A RED FLAG. A parent who has forgotten their
        // password twice has three logins; the worst real household on file has eleven. They all key to
        // the same mother, so they are all the same household, and the SuperUser can collapse them in
        // one action instead of eleven.
        var sourceChildKeys = await ChildKeysAsync([sourceFamilyUserId], ct);
        var allFamilyIds = candidateFamilyIds.Append(sourceFamilyUserId).ToList();
        var accounts = await HydrateFamilyAccountsAsync(allFamilyIds, sourceChildKeys, ct);

        if (!accounts.TryGetValue(sourceFamilyUserId, out var sourceDto)) return EmptyCandidates();

        var candidates = accounts.Values
            .Where(a => a.UserId != sourceFamilyUserId)
            .OrderByDescending(a => a.RegistrationCount)
            .ThenBy(a => a.UserName)
            .ToList();

        return new MergeCandidatesResponse
        {
            Source = sourceDto,
            Candidates = candidates,
            RegistrationsAffected = sourceDto.RegistrationCount + candidates.Sum(c => c.RegistrationCount),
            AccountsAffected = candidates.Count
        };
    }

    /// <summary>The distinct children a set of family logins owns, folded to a comparable key.</summary>
    private async Task<HashSet<(string, string, DateTime?)>> ChildKeysAsync(
        IReadOnlyCollection<string> familyUserIds,
        CancellationToken ct)
    {
        var rows = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.FamilyUserId != null && familyUserIds.Contains(r.FamilyUserId)
            select new { u.FirstName, u.LastName, u.Dob }
        ).AsNoTracking().Distinct().ToListAsync(ct);

        return rows.Select(c => (Fold(c.FirstName), Fold(c.LastName), c.Dob)).ToHashSet();
    }

    // ═══════════════════════════════════════════════════════════
    //  The merge itself
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Collapse one or more duplicate family logins onto the one the parent asked for.
    ///
    /// Two writes, both bounded by <paramref name="sourceFamilyUserNames"/> — the accounts the
    /// SuperUser selected, re-validated here against the candidate set so a stale or forged body cannot
    /// name an account we never offered:
    ///
    ///   1. FAMILY. Every registration under a losing login is re-pointed to the target. EVERY one —
    ///      including inactive ones. Legacy filtered on BActive, which orphaned a parent's dropped and
    ///      pending registrations on a login nobody can sign into again. The whole point of the
    ///      operation is that the parent gets their history back.
    ///
    ///   2. CHILDREN. Without this, the parent signs into the account they asked for and sees Maya
    ///      twice and Ethan twice — one player row from each login that was just fused. So each child
    ///      is collapsed onto the target's row for that child.
    ///
    /// The child collapse is UNAMBIGUOUS-ONLY, and that is a load-bearing restriction: a family may
    /// deliberately hold two player rows for one child, created to get a second registration past an
    /// event's one-per-player rule. If either side has two rows for the same (name, DOB), we do not
    /// know which is which, so we leave BOTH alone rather than silently fuse a workaround back together.
    /// </summary>
    public async Task<MergeResultDto> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string targetFamilyUserName,
        IReadOnlyList<string> sourceFamilyUserNames,
        CancellationToken ct = default)
    {
        var preview = await GetFamilyMergeCandidatesAsync(registrationId, ct);

        var offered = preview.Candidates
            .Append(preview.Source)
            .ToDictionary(c => c.UserName, StringComparer.OrdinalIgnoreCase);

        if (!offered.TryGetValue(targetFamilyUserName, out var target))
        {
            throw new InvalidOperationException(
                $"'{targetFamilyUserName}' is not a merge candidate for this household. The target must " +
                "be a login whose mother's email, phone and name all match.");
        }

        // Every account the SuperUser asked to move must be one we offered. This is the security
        // boundary: the browser cannot introduce an account the key never approved.
        var sources = new List<MergeCandidateDto>();
        foreach (var name in sourceFamilyUserNames)
        {
            if (!offered.TryGetValue(name, out var src))
                throw new InvalidOperationException($"'{name}' is not a merge candidate for this household.");

            if (!string.Equals(src.UserId, target.UserId, StringComparison.Ordinal))
                sources.Add(src);
        }

        if (sources.Count == 0)
            throw new InvalidOperationException("Select at least one account to merge into the target.");

        var sourceIds = sources.Select(s => s.UserId).Distinct().ToList();

        // ── 1. the registrations ──────────────────────────────────────────────
        // Re-read by FK. NOT by name, and NOT from what the search happened to return: a search for
        // "Abell" will not return a sibling named Shimizu, and that sibling's registrations still have
        // to move.
        var regs = await _context.Registrations
            .Where(r => r.FamilyUserId != null && sourceIds.Contains(r.FamilyUserId))
            .ToListAsync(ct);

        // Snapshot the previous owner BEFORE the write. This is the ONLY way back — afterwards the
        // target owns registrations that used to be several other accounts', and nothing on the row
        // says which.
        var moved = regs
            .Select(r => new MergedRegistrationDto
            {
                RegistrationId = r.RegistrationId,
                PreviousUserId = r.FamilyUserId!
            })
            .ToList();

        // ── 2. the children ───────────────────────────────────────────────────
        var childMap = await BuildChildCollapseAsync(sourceIds, target.UserId, ct);

        foreach (var reg in regs)
        {
            reg.FamilyUserId = target.UserId;

            if (reg.UserId != null && childMap.TryGetValue(reg.UserId, out var survivingChildId))
                reg.UserId = survivingChildId;
        }

        await _context.SaveChangesAsync(ct);

        return new MergeResultDto
        {
            TargetUserId = target.UserId,
            TargetUserName = target.UserName,
            Moved = moved
        };
    }

    /// <summary>
    /// Maps each losing player row to the target household's row for the same child:
    /// <c>{ Maya_old → Maya_target, Ethan_old → Ethan_target }</c>.
    ///
    /// Scoped to the accounts being merged. It cannot reach an unrelated household, and it cannot touch
    /// a deliberate double-registration in a family that is not part of this merge.
    ///
    /// A child is only collapsed when BOTH sides hold exactly one row for that (name, DOB). Two rows on
    /// either side is ambiguous — it is what a deliberate double-registration looks like — so that child
    /// is left as it is.
    /// </summary>
    private async Task<Dictionary<string, string>> BuildChildCollapseAsync(
        IReadOnlyCollection<string> sourceFamilyUserIds,
        string targetFamilyUserId,
        CancellationToken ct)
    {
        var all = sourceFamilyUserIds.Append(targetFamilyUserId).ToList();

        var rows = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.FamilyUserId != null && all.Contains(r.FamilyUserId)
            select new { FamilyUserId = r.FamilyUserId!, UserId = u.Id, u.FirstName, u.LastName, u.Dob }
        ).AsNoTracking().Distinct().ToListAsync(ct);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var child in rows.GroupBy(x => (Fold(x.FirstName), Fold(x.LastName), x.Dob)))
        {
            var onTarget = child.Where(x => x.FamilyUserId == targetFamilyUserId)
                                .Select(x => x.UserId).Distinct().ToList();

            var onSources = child.Where(x => x.FamilyUserId != targetFamilyUserId)
                                 .Select(x => x.UserId).Distinct().ToList();

            // The target does not have this child at all — their row comes across untouched, still
            // pointing at itself. (And if a SOURCE holds two rows for them, both come across, still
            // separate: whatever made them two rows is not ours to undo.)
            if (onTarget.Count != 1) continue;
            if (onSources.Count != 1) continue;

            map[onSources[0]] = onTarget[0];
        }

        return map;
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
            // Every registration, not just the active ones. This count IS the blast radius the
            // SuperUser is shown, and the merge moves everything — an inactive count here would make
            // the number on screen smaller than what actually moves.
            where userIds.Contains(u.Id)
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
            // Every registration — see HydratePersonAccountsAsync. The count on the card has to be the
            // count the merge will move, or the blast radius is a lie.
            where r.FamilyUserId != null && familyUserIds.Contains(r.FamilyUserId)
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
