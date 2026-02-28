using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.ChangePassword;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class ChangePasswordRepository : IChangePasswordRepository
{
    private readonly SqlDbContext _context;
    private const int MaxResults = 200;

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

        return await (
            from q in query
            select new ChangePasswordSearchResultDto
            {
                RegistrationId = q.r.RegistrationId,
                RoleName = q.role.Name ?? "",
                CustomerName = q.c.CustomerName ?? "",
                JobName = q.j.JobName ?? "",
                UserName = q.uP.UserName ?? "",
                FirstName = q.uP.FirstName,
                LastName = q.uP.LastName,
                Email = q.uP.Email,
                Phone = q.uP.Cellphone,
                FamilyUserName = q.uF.UserName,
                FamilyEmail = q.uF.Email,
                MomFirstName = q.f.MomFirstName,
                MomLastName = q.f.MomLastName,
                MomEmail = q.f.MomEmail,
                MomPhone = q.f.MomCellphone,
                DadFirstName = q.f.DadFirstName,
                DadLastName = q.f.DadLastName,
                DadEmail = q.f.DadEmail,
                DadPhone = q.f.DadCellphone
            }
        ).AsNoTracking().Take(MaxResults).ToListAsync(ct);
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

        return await (
            from q in query
            select new ChangePasswordSearchResultDto
            {
                RegistrationId = q.r.RegistrationId,
                RoleName = q.role.Name ?? "",
                CustomerName = q.c.CustomerName ?? "",
                JobName = q.j.JobName ?? "",
                UserName = q.u.UserName ?? "",
                FirstName = q.u.FirstName,
                LastName = q.u.LastName,
                Email = q.u.Email,
                Phone = q.u.Cellphone
            }
        ).AsNoTracking().Take(MaxResults).ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Email updates
    // ═══════════════════════════════════════════════════════════

    public async Task UpdateUserEmailAsync(
        Guid registrationId,
        string newEmail,
        CancellationToken ct = default)
    {
        var reg = await _context.Registrations
            .FirstOrDefaultAsync(r => r.RegistrationId == registrationId, ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found.");

        var user = await _context.AspNetUsers
            .FirstOrDefaultAsync(u => u.Id == reg.UserId, ct)
            ?? throw new InvalidOperationException($"User for registration {registrationId} not found.");

        user.Email = newEmail;
        user.NormalizedEmail = newEmail.ToUpperInvariant();

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

        // Update family user email
        if (familyEmail != null)
        {
            var familyUser = await _context.AspNetUsers
                .FirstOrDefaultAsync(u => u.Id == reg.FamilyUserId, ct)
                ?? throw new InvalidOperationException("Family user not found.");

            familyUser.Email = familyEmail;
            familyUser.NormalizedEmail = familyEmail.ToUpperInvariant();
        }

        // Update mom/dad emails on Families record
        var family = await _context.Families
            .FirstOrDefaultAsync(f => f.FamilyUserId == reg.FamilyUserId, ct);

        if (family != null)
        {
            if (momEmail != null) family.MomEmail = momEmail;
            if (dadEmail != null) family.DadEmail = dadEmail;
        }

        await _context.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Merge candidates
    // ═══════════════════════════════════════════════════════════

    public async Task<List<MergeCandidateDto>> GetUserMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        // Step 1: get the source registration's identifying info
        var source = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RegistrationId == registrationId
            select new
            {
                u.FirstName,
                u.LastName,
                u.Dob,
                u.Email,
                u.UserName,
                r.RoleId
            }
        ).AsNoTracking().SingleOrDefaultAsync(ct);

        if (source == null) return [];

        // Step 2: find other users with matching identity fields but different username
        return await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.BActive == true
               && u.FirstName != null && source.FirstName != null
               && u.FirstName.ToLower() == source.FirstName.ToLower()
               && u.LastName != null && source.LastName != null
               && u.LastName.ToLower() == source.LastName.ToLower()
               && r.RoleId == source.RoleId
               && (u.Dob == null || (source.Dob != null && u.Dob == source.Dob))
               && (u.Email == null || (source.Email != null && u.Email == source.Email))
               && u.UserName != source.UserName
            select new MergeCandidateDto
            {
                UserName = u.UserName ?? "",
                UserId = u.Id
            }
        ).AsNoTracking().Distinct().ToListAsync(ct);
    }

    public async Task<List<MergeCandidateDto>> GetFamilyMergeCandidatesAsync(
        Guid registrationId,
        CancellationToken ct = default)
    {
        // Step 1: get the source family's identifying info
        var source = await (
            from r in _context.Registrations
            join f in _context.Families on r.FamilyUserId equals f.FamilyUserId
            join u in _context.AspNetUsers on f.FamilyUserId equals u.Id
            where r.RegistrationId == registrationId
            select new
            {
                f.MomFirstName,
                f.MomLastName,
                f.MomEmail,
                f.DadFirstName,
                f.DadLastName,
                f.DadEmail,
                u.PostalCode,
                r.RoleId,
                u.UserName
            }
        ).AsNoTracking().SingleOrDefaultAsync(ct);

        if (source == null) return [];

        // Step 2: find other family accounts with matching identity fields
        return await (
            from r in _context.Registrations
            join f in _context.Families on r.FamilyUserId equals f.FamilyUserId
            join u in _context.AspNetUsers on f.FamilyUserId equals u.Id
            where r.BActive == true
               && f.MomFirstName != null && source.MomFirstName != null
               && f.MomFirstName.ToLower() == source.MomFirstName.ToLower()
               && f.MomLastName != null && source.MomLastName != null
               && f.MomLastName.ToLower() == source.MomLastName.ToLower()
               && f.MomEmail != null && source.MomEmail != null
               && f.MomEmail.ToLower() == source.MomEmail.ToLower()
               && f.DadFirstName != null && source.DadFirstName != null
               && f.DadFirstName.ToLower() == source.DadFirstName.ToLower()
               && f.DadLastName != null && source.DadLastName != null
               && f.DadLastName.ToLower() == source.DadLastName.ToLower()
               && f.DadEmail != null && source.DadEmail != null
               && f.DadEmail.ToLower() == source.DadEmail.ToLower()
               && u.PostalCode != null && source.PostalCode != null
               && u.PostalCode.ToLower() == source.PostalCode.ToLower()
               && r.RoleId == source.RoleId
               && u.UserName != source.UserName
            select new MergeCandidateDto
            {
                UserName = u.UserName ?? "",
                UserId = u.Id
            }
        ).AsNoTracking().Distinct().ToListAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════
    //  Merge operations
    // ═══════════════════════════════════════════════════════════

    public async Task<int> MergeUserRegistrationsAsync(
        Guid registrationId,
        string targetUserName,
        CancellationToken ct = default)
    {
        // Resolve target userId
        var targetUserId = await _context.AspNetUsers
            .Where(u => u.UserName == targetUserName)
            .Select(u => u.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Target user '{targetUserName}' not found.");

        // Get source registration's identity
        var source = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.RegistrationId == registrationId
            select new
            {
                u.FirstName,
                u.LastName,
                u.Dob,
                u.Email,
                r.RoleId
            }
        ).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found.");

        // Find all matching registrations and reassign to target user
        var matchingRegs = await (
            from r in _context.Registrations
            join u in _context.AspNetUsers on r.UserId equals u.Id
            where r.BActive == true
               && u.FirstName != null && source.FirstName != null
               && u.FirstName.ToLower() == source.FirstName.ToLower()
               && u.LastName != null && source.LastName != null
               && u.LastName.ToLower() == source.LastName.ToLower()
               && r.RoleId == source.RoleId
               && (u.Dob == null || (source.Dob != null && u.Dob == source.Dob))
               && (u.Email == null || (source.Email != null && u.Email == source.Email))
            select r
        ).Distinct().ToListAsync(ct);

        foreach (var reg in matchingRegs)
        {
            reg.UserId = targetUserId;
        }

        await _context.SaveChangesAsync(ct);
        return matchingRegs.Count;
    }

    public async Task<int> MergeFamilyRegistrationsAsync(
        Guid registrationId,
        string targetFamilyUserName,
        CancellationToken ct = default)
    {
        // Resolve target familyUserId
        var targetFamilyUserId = await _context.AspNetUsers
            .Where(u => u.UserName == targetFamilyUserName)
            .Select(u => u.Id)
            .SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Target family user '{targetFamilyUserName}' not found.");

        // Get source family's identity
        var source = await (
            from r in _context.Registrations
            join f in _context.Families on r.FamilyUserId equals f.FamilyUserId
            join u in _context.AspNetUsers on f.FamilyUserId equals u.Id
            where r.RegistrationId == registrationId
            select new
            {
                f.MomFirstName,
                f.MomLastName,
                f.MomEmail,
                f.DadFirstName,
                f.DadLastName,
                f.DadEmail,
                u.PostalCode,
                r.RoleId
            }
        ).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Registration {registrationId} not found or has no family.");

        // Find all matching registrations and reassign to target family
        var matchingRegs = await (
            from r in _context.Registrations
            join f in _context.Families on r.FamilyUserId equals f.FamilyUserId
            join u in _context.AspNetUsers on f.FamilyUserId equals u.Id
            where r.BActive == true
               && f.MomFirstName != null && source.MomFirstName != null
               && f.MomFirstName.ToLower() == source.MomFirstName.ToLower()
               && f.MomLastName != null && source.MomLastName != null
               && f.MomLastName.ToLower() == source.MomLastName.ToLower()
               && f.MomEmail != null && source.MomEmail != null
               && f.MomEmail.ToLower() == source.MomEmail.ToLower()
               && f.DadFirstName != null && source.DadFirstName != null
               && f.DadFirstName.ToLower() == source.DadFirstName.ToLower()
               && f.DadLastName != null && source.DadLastName != null
               && f.DadLastName.ToLower() == source.DadLastName.ToLower()
               && f.DadEmail != null && source.DadEmail != null
               && f.DadEmail.ToLower() == source.DadEmail.ToLower()
               && u.PostalCode != null && source.PostalCode != null
               && u.PostalCode.ToLower() == source.PostalCode.ToLower()
               && r.RoleId == source.RoleId
            select r
        ).ToListAsync(ct);

        foreach (var reg in matchingRegs)
        {
            reg.FamilyUserId = targetFamilyUserId;
        }

        await _context.SaveChangesAsync(ct);
        return matchingRegs.Count;
    }
}
