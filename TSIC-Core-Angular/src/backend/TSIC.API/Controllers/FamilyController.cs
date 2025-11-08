using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Dtos;
using TSIC.API.Constants;
using System.Transactions;
using System.Globalization;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.Infrastructure.Data.Identity;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SqlDbContext _db;

    public FamilyController(UserManager<ApplicationUser> userManager, SqlDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(FamilyProfileResponse), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetMyFamily()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        // Load AspNetUsers profile (address/phone/email) and Families record
        var aspUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId);
        if (aspUser == null) return NotFound();

        var fam = await _db.Families.FirstOrDefaultAsync(f => f.FamilyUserId == userId);
        if (fam == null)
        {
            // If no Families record yet, return minimal profile using AspNetUsers fields
            var emptyChildren = new List<ChildDto>();
            var respLite = new FamilyProfileResponse(
                aspUser.UserName ?? string.Empty,
                new PersonDto(aspUser.FirstName ?? string.Empty, aspUser.LastName ?? string.Empty, aspUser.Cellphone ?? string.Empty, aspUser.Email ?? string.Empty),
                new PersonDto(string.Empty, string.Empty, string.Empty, string.Empty),
                new AddressDto(aspUser.StreetAddress ?? string.Empty, aspUser.City ?? string.Empty, aspUser.State ?? string.Empty, aspUser.PostalCode ?? string.Empty),
                emptyChildren
            );
            return Ok(respLite);
        }

        // Load existing child links and child users
        var links = await _db.FamilyMembers.Where(l => l.FamilyUserId == fam.FamilyUserId).ToListAsync();
        var childIds = links.Select(l => l.FamilyMemberUserId).ToList();
        var children = await _db.AspNetUsers.Where(u => childIds.Contains(u.Id)).ToListAsync();

        var childDtos = children.Select(c => new ChildDto(
            c.FirstName ?? string.Empty,
            c.LastName ?? string.Empty,
            c.Gender ?? string.Empty,
            c.Dob?.ToString("yyyy-MM-dd"),
            c.Email,
            c.Cellphone ?? c.Phone
        )).ToList();

        // Prefer Families contact data when present; fall back to AspNetUsers profile fields
        string Fallback(string? primary, string? fallback) => !string.IsNullOrWhiteSpace(primary) ? primary! : (fallback ?? string.Empty);

        var primary = new PersonDto(
            Fallback(fam.MomFirstName, aspUser.FirstName),
            Fallback(fam.MomLastName, aspUser.LastName),
            Fallback(fam.MomCellphone, aspUser.Cellphone ?? aspUser.Phone),
            Fallback(fam.MomEmail, aspUser.Email)
        );

        var secondary = new PersonDto(
            fam.DadFirstName ?? string.Empty,
            fam.DadLastName ?? string.Empty,
            fam.DadCellphone ?? string.Empty,
            fam.DadEmail ?? string.Empty
        );

        var response = new FamilyProfileResponse(
            aspUser.UserName ?? string.Empty,
            primary,
            secondary,
            new AddressDto(aspUser.StreetAddress ?? string.Empty, aspUser.City ?? string.Empty, aspUser.State ?? string.Empty, aspUser.PostalCode ?? string.Empty),
            childDtos
        );

        return Ok(response);
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 200)]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 400)]
    public async Task<IActionResult> Register([FromBody] FamilyRegistrationRequest request)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new FamilyRegistrationResponse(false, null, null, "Username and password are required"));
        }

        // Require at least one child
        if (request.Children == null || request.Children.Count == 0)
        {
            return BadRequest(new FamilyRegistrationResponse(false, null, null, "At least one child is required."));
        }
        // Use an ambient transaction so Identity and EF domain writes commit atomically
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        // Create Identity user with extended fields
        var user = new ApplicationUser
        {
            UserName = request.Username,
            Email = request.Primary.Email,
            FirstName = request.Primary.FirstName,
            LastName = request.Primary.LastName,
            Cellphone = request.Primary.Cellphone,
            Phone = request.Primary.Cellphone, // legacy mirror
            StreetAddress = request.Address.StreetAddress,
            City = request.Address.City,
            State = request.Address.State,
            PostalCode = request.Address.PostalCode,
            Modified = DateTime.UtcNow // ensure UTC
        };
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var msg = string.Join("; ", createResult.Errors.Select(e => e.Description));
            return BadRequest(new FamilyRegistrationResponse(false, null, null, msg));
        }

        // Insert Families record; default LebUserId to SuperUserId during creation
        var fam = new TSIC.Domain.Entities.Families
        {
            FamilyUserId = user.Id,
            MomFirstName = request.Primary.FirstName,
            MomLastName = request.Primary.LastName,
            MomCellphone = request.Primary.Cellphone,
            MomEmail = request.Primary.Email,
            DadFirstName = request.Secondary.FirstName,
            DadLastName = request.Secondary.LastName,
            DadCellphone = request.Secondary.Cellphone,
            DadEmail = request.Secondary.Email,
            Modified = DateTime.UtcNow,
            LebUserId = TsicConstants.SuperUserId
        };
        _db.Families.Add(fam);
        await _db.SaveChangesAsync();

        // Create child profiles as AspNetUsers (no password) and link via Family_Members
        foreach (var child in request.Children)
        {
            // Create a lightweight Identity user for the child
            var childUser = new ApplicationUser
            {
                UserName = $"child_{Guid.NewGuid():N}",
                Email = string.IsNullOrWhiteSpace(child.Email) ? null : child.Email,
                PhoneNumber = string.IsNullOrWhiteSpace(child.Phone) ? null : child.Phone,
                FirstName = child.FirstName,
                LastName = child.LastName,
                Gender = child.Gender,
                Cellphone = string.IsNullOrWhiteSpace(child.Phone) ? null : child.Phone,
                Phone = string.IsNullOrWhiteSpace(child.Phone) ? null : child.Phone,
                Modified = DateTime.UtcNow
            };
            if (!string.IsNullOrWhiteSpace(child.Dob) && DateTime.TryParse(child.Dob, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDob))
            {
                childUser.Dob = parsedDob.Date;
            }
            var createChildResult = await _userManager.CreateAsync(childUser);
            if (!createChildResult.Succeeded)
            {
                var msg = string.Join("; ", createChildResult.Errors.Select(e => e.Description));
                return BadRequest(new FamilyRegistrationResponse(false, null, null, $"Failed to create child profile: {msg}"));
            }

            // Link the child to the family via Family_Members
            var fm = new TSIC.Domain.Entities.FamilyMembers
            {
                FamilyUserId = fam.FamilyUserId,
                FamilyMemberUserId = childUser.Id,
                Modified = DateTime.UtcNow,
                LebUserId = TsicConstants.SuperUserId
            };
            _db.FamilyMembers.Add(fm);
        }

        await _db.SaveChangesAsync();

        // Commit
        scope.Complete();

        return Ok(new FamilyRegistrationResponse(true, user.Id, Guid.Empty, null));
    }

    [HttpPut("update")]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 200)]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 400)]
    [ProducesResponseType(typeof(FamilyRegistrationResponse), 404)]
    public async Task<IActionResult> Update([FromBody] FamilyUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return BadRequest(new FamilyRegistrationResponse(false, null, null, "Username is required"));
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        // Find the Identity user by username
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null)
        {
            return NotFound(new FamilyRegistrationResponse(false, null, null, "User not found"));
        }

        // Update AspNetUsers profile fields
        var aspUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == user.Id);
        if (aspUser != null)
        {
            aspUser.StreetAddress = request.Address.StreetAddress;
            aspUser.City = request.Address.City;
            aspUser.State = request.Address.State;
            aspUser.PostalCode = request.Address.PostalCode;
            aspUser.Cellphone = request.Primary.Cellphone;
            aspUser.Phone = request.Primary.Cellphone; // legacy support
            if (string.IsNullOrWhiteSpace(aspUser.LebUserId))
            {
                aspUser.LebUserId = TsicConstants.SuperUserId;
            }
            await _db.SaveChangesAsync();
        }

        // Update Families record if present
        var fam = await _db.Families.FirstOrDefaultAsync(f => f.FamilyUserId == user.Id);
        if (fam == null)
        {
            return NotFound(new FamilyRegistrationResponse(false, null, null, "Family record not found"));
        }

        fam.MomFirstName = request.Primary.FirstName;
        fam.MomLastName = request.Primary.LastName;
        fam.MomCellphone = request.Primary.Cellphone;
        fam.MomEmail = request.Primary.Email;
        fam.DadFirstName = request.Secondary.FirstName;
        fam.DadLastName = request.Secondary.LastName;
        fam.DadCellphone = request.Secondary.Cellphone;
        fam.DadEmail = request.Secondary.Email;
        fam.Modified = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(fam.LebUserId))
        {
            fam.LebUserId = TsicConstants.SuperUserId;
        }
        await _db.SaveChangesAsync();

        // ----- Synchronize children (Family_Members) -----
        // Load existing links for the family
        var existingLinks = await _db.FamilyMembers
            .Where(fm => fm.FamilyUserId == fam.FamilyUserId)
            .ToListAsync();

        // Load child users for existing links
        var childUserIds = existingLinks.Select(l => l.FamilyMemberUserId).ToList();
        var existingChildrenUsers = await _db.AspNetUsers
            .Where(u => childUserIds.Contains(u.Id))
            .ToListAsync();

        string DigitsOnly(string? s) => new string((s ?? string.Empty).Where(char.IsDigit).ToArray());
        string? Norm(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        string KeyEmail(string? e) => (e ?? string.Empty).Trim().ToLowerInvariant();
        string KeyPhone(string? p) => DigitsOnly(p);
        string KeyNameDob(string? f, string? l, DateTime? dob) => $"{(f ?? string.Empty).Trim().ToLowerInvariant()}|{(l ?? string.Empty).Trim().ToLowerInvariant()}|{(dob?.ToString("yyyy-MM-dd") ?? string.Empty)}";
        bool IsPlaceholderEmail(string? e)
            => string.Equals((e ?? string.Empty).Trim(), "not@given.com", StringComparison.OrdinalIgnoreCase);

        // Build lookup maps for existing children (duplicate-tolerant)
        // In real data, placeholders (e.g., not@given.com) or repeated phones can exist across siblings.
        // For email, keep ALL candidates per email to allow disambiguation (don't collapse to one).
        var listByEmail = existingChildrenUsers
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .GroupBy(c => KeyEmail(c.Email!), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x).ToList(), StringComparer.OrdinalIgnoreCase);

        // Collect all children per phone (digits-only) to allow disambiguation when multiple share a placeholder number
        var listByPhone = existingChildrenUsers
            .Select(c => new { Child = c, Key = KeyPhone(c.Cellphone ?? c.Phone) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Child).ToList());

        // Group by Name+DOB (not guaranteed unique; twins or identical data can exist)
        var listByNameDob = existingChildrenUsers
            .GroupBy(c => KeyNameDob(c.FirstName, c.LastName, c.Dob))
            .ToDictionary(g => g.Key, g => g.Select(x => x).ToList());

        var matchedChildIds = new HashSet<string>();

        foreach (var child in request.Children ?? new List<ChildDto>())
        {
            // Resolve match
            AspNetUsers? aspChild = null;
            // Email-based match only if the provided email is not a known placeholder.
            if (!string.IsNullOrWhiteSpace(child.Email) && !IsPlaceholderEmail(child.Email))
            {
                if (listByEmail.TryGetValue(KeyEmail(child.Email), out var emailCandidates) && emailCandidates.Count > 0)
                {
                    // If multiple children share the same email, disambiguate further
                    // Prefer a candidate not already matched in this request round.
                    // Then try Name + DOB, then Name-only.
                    var pick = emailCandidates.FirstOrDefault(c => !matchedChildIds.Contains(c.Id));

                    if (pick == null)
                    {
                        DateTime? emailChildDob = null;
                        if (!string.IsNullOrWhiteSpace(child.Dob) && DateTime.TryParse(child.Dob, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedEmailDob))
                        {
                            emailChildDob = parsedEmailDob.Date;
                        }
                        pick = emailCandidates.FirstOrDefault(c =>
                            string.Equals((c.FirstName ?? string.Empty).Trim(), (child.FirstName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((c.LastName ?? string.Empty).Trim(), (child.LastName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                            c.Dob.HasValue && emailChildDob.HasValue && c.Dob.Value.Date == emailChildDob.Value.Date);
                    }
                    if (pick == null)
                    {
                        pick = emailCandidates.FirstOrDefault(c =>
                            string.Equals((c.FirstName ?? string.Empty).Trim(), (child.FirstName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((c.LastName ?? string.Empty).Trim(), (child.LastName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    }
                    aspChild = pick ?? emailCandidates[0];
                }
            }
            if (aspChild == null && !string.IsNullOrWhiteSpace(child.Phone))
            {
                var phoneKey = KeyPhone(child.Phone);
                if (listByPhone.TryGetValue(phoneKey, out var candidates) && candidates.Count > 0)
                {
                    // Disambiguation strategy:
                    // 1. Exact email match among candidates if child provided email
                    // 2. Name + DOB match
                    // 3. Name-only match
                    // 4. Fallback to first
                    AspNetUsers? pick = null;
                    if (!string.IsNullOrWhiteSpace(child.Email))
                    {
                        pick = candidates.FirstOrDefault(c => string.Equals(Norm(c.Email), Norm(child.Email), StringComparison.OrdinalIgnoreCase));
                    }
                    if (pick == null)
                    {
                        DateTime? phoneChildDob = null;
                        if (!string.IsNullOrWhiteSpace(child.Dob) && DateTime.TryParse(child.Dob, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDob3))
                        {
                            phoneChildDob = parsedDob3.Date;
                        }
                        pick = candidates.FirstOrDefault(c =>
                            string.Equals((c.FirstName ?? string.Empty).Trim(), (child.FirstName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((c.LastName ?? string.Empty).Trim(), (child.LastName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                            c.Dob.HasValue && phoneChildDob.HasValue && c.Dob.Value.Date == phoneChildDob.Value.Date);
                    }
                    if (pick == null)
                    {
                        pick = candidates.FirstOrDefault(c =>
                            string.Equals((c.FirstName ?? string.Empty).Trim(), (child.FirstName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((c.LastName ?? string.Empty).Trim(), (child.LastName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                    }
                    // If still not decided, pick a candidate not yet matched this round; else first
                    aspChild = pick ?? candidates.FirstOrDefault(c => !matchedChildIds.Contains(c.Id)) ?? candidates[0];
                }
            }
            DateTime? childDob = null;
            if (!string.IsNullOrWhiteSpace(child.Dob) && DateTime.TryParse(child.Dob, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDob2))
            {
                childDob = parsedDob2.Date;
            }
            if (aspChild == null)
            {
                var nameDobKey = KeyNameDob(child.FirstName, child.LastName, childDob);
                if (listByNameDob.TryGetValue(nameDobKey, out var candidatesByNameDob) && candidatesByNameDob.Count > 0)
                {
                    // Prefer a candidate not already matched in this session
                    aspChild = candidatesByNameDob.FirstOrDefault(c => !matchedChildIds.Contains(c.Id))
                               ?? candidatesByNameDob[0];
                }
            }

            if (aspChild != null)
            {
                // Update profile fields
                aspChild.FirstName = child.FirstName;
                aspChild.LastName = child.LastName;
                aspChild.Gender = child.Gender;
                aspChild.Dob = childDob;
                aspChild.Email = Norm(child.Email);
                var phone = Norm(child.Phone);
                if (!string.IsNullOrWhiteSpace(phone))
                {
                    aspChild.Cellphone = phone;
                    aspChild.Phone = phone;
                }
                if (string.IsNullOrWhiteSpace(aspChild.LebUserId)) aspChild.LebUserId = TsicConstants.SuperUserId;

                // Ensure link exists
                if (!existingLinks.Any(l => l.FamilyMemberUserId == aspChild.Id))
                {
                    _db.FamilyMembers.Add(new FamilyMembers
                    {
                        FamilyUserId = fam.FamilyUserId,
                        FamilyMemberUserId = aspChild.Id,
                        Modified = DateTime.UtcNow,
                        LebUserId = TsicConstants.SuperUserId
                    });
                }
                matchedChildIds.Add(aspChild.Id);
            }
            else
            {
                // Create new child identity and link
                var childUser = new ApplicationUser
                {
                    UserName = $"child_{Guid.NewGuid():N}",
                    Email = Norm(child.Email),
                    PhoneNumber = Norm(child.Phone),
                    FirstName = child.FirstName,
                    LastName = child.LastName,
                    Gender = child.Gender,
                    Cellphone = Norm(child.Phone),
                    Phone = Norm(child.Phone),
                    Modified = DateTime.UtcNow
                };
                if (!string.IsNullOrWhiteSpace(child.Dob) && DateTime.TryParse(child.Dob, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDobNew))
                {
                    childUser.Dob = parsedDobNew.Date;
                }
                var created = await _userManager.CreateAsync(childUser);
                if (!created.Succeeded)
                {
                    var msg = string.Join("; ", created.Errors.Select(e => e.Description));
                    return BadRequest(new FamilyRegistrationResponse(false, null, null, $"Failed to create child profile: {msg}"));
                }

                _db.FamilyMembers.Add(new FamilyMembers
                {
                    FamilyUserId = fam.FamilyUserId,
                    FamilyMemberUserId = childUser.Id,
                    Modified = DateTime.UtcNow,
                    LebUserId = TsicConstants.SuperUserId
                });
                matchedChildIds.Add(childUser.Id);
            }
        }

        // Unlink removed children (do not delete AspNetUsers)
        int skippedUnlinks = 0;
        foreach (var link in existingLinks)
        {
            if (!matchedChildIds.Contains(link.FamilyMemberUserId))
            {
                // Prevent unlink if child has active registrations
                bool hasActiveRegs = await _db.Registrations
                    .AnyAsync(r => r.UserId == link.FamilyMemberUserId && (r.BActive == true || r.PaidTotal > 0 || r.OwedTotal > 0));
                if (hasActiveRegs)
                {
                    skippedUnlinks++;
                    continue;
                }

                _db.FamilyMembers.Remove(link);
            }
        }

        await _db.SaveChangesAsync();

        scope.Complete();
        var message = skippedUnlinks > 0
            ? $"Saved. {skippedUnlinks} child link(s) were kept because they have active registrations."
            : null;
        return Ok(new FamilyRegistrationResponse(true, user.Id, null, message));
    }

    // List child players for a family user within a job context, including registration status.
    // Lightweight listing of family account users (currently single-family user). Returns an array for future multi-user support.
    [HttpGet("users")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<object>), 200)]
    public async Task<IActionResult> GetFamilyUsers([FromQuery] string? jobPath)
    {
        // Caller identity
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();

        // Determine if caller has a Families record; if not, return empty list (must create one first)
        var fam = await _db.Families.FirstOrDefaultAsync(f => f.FamilyUserId == callerId);
        if (fam == null)
        {
            return Ok(Array.Empty<object>());
        }

        // Display name preference: MomFirstName/LastName then Dad fallback then username (from AspNetUsers)
        var aspUser = await _db.AspNetUsers.FirstOrDefaultAsync(u => u.Id == callerId);
        // Build a display name with clear imperative logic (avoid nested ternaries for readability / complexity)
        string display;
        if (!string.IsNullOrWhiteSpace(fam.MomFirstName) || !string.IsNullOrWhiteSpace(fam.MomLastName))
        {
            display = $"{fam.MomFirstName} {fam.MomLastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(fam.DadFirstName) || !string.IsNullOrWhiteSpace(fam.DadLastName))
        {
            display = $"{fam.DadFirstName} {fam.DadLastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(aspUser?.FirstName) || !string.IsNullOrWhiteSpace(aspUser?.LastName))
        {
            display = $"{aspUser?.FirstName} {aspUser?.LastName}".Trim();
        }
        else
        {
            display = aspUser?.UserName ?? "Family";
        }

        var result = new[]
        {
            new { familyUserId = fam.FamilyUserId, displayName = display, userName = aspUser?.UserName ?? string.Empty }
        };
        return Ok(result);
    }

    [HttpGet("players")]
    [Authorize]
    [ProducesResponseType(typeof(IEnumerable<object>), 200)]
    public async Task<IActionResult> GetFamilyPlayers([FromQuery] string jobPath, [FromQuery] string familyUserId)
    {
        if (string.IsNullOrWhiteSpace(jobPath) || string.IsNullOrWhiteSpace(familyUserId))
        {
            return BadRequest(new { message = "jobPath and familyUserId are required" });
        }

        // Ensure caller is the same family user (or has elevated roles) - basic check
        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId == null) return Unauthorized();
        if (!string.Equals(callerId, familyUserId, StringComparison.OrdinalIgnoreCase))
        {
            // Future: allow admin/superusers; for now restrict
            return Forbid();
        }

        // Load family member links
        var links = await _db.FamilyMembers.Where(fm => fm.FamilyUserId == familyUserId).ToListAsync();
        var childIds = links.Select(l => l.FamilyMemberUserId).Distinct().ToList();

        // Fallback/augment: include any children who previously registered under this family even if not linked in FamilyMembers
        var regChildIds = await _db.Registrations
            .Where(r => r.FamilyUserId == familyUserId && r.UserId != null)
            .Select(r => r.UserId!)
            .Distinct()
            .ToListAsync();
        var allChildIds = childIds.Union(regChildIds).Distinct().ToList();
        if (allChildIds.Count == 0) return Ok(Array.Empty<object>());

        // Load child profiles
        var children = await _db.AspNetUsers.Where(u => allChildIds.Contains(u.Id)).ToListAsync();

        // Determine registration status per child for the given job
        // Registrations table may not have JobPath; attempt filter via Registrations and associated Jobs metadata.
        // Fallback: load registrations for children and infer job match through a join if available; else treat all as registered.
        var regsRaw = await _db.Registrations
            .Where(r => r.UserId != null && allChildIds.Contains(r.UserId))
            .Select(r => new { r.UserId, r.BActive, r.PaidTotal, r.OwedTotal, r.JobId })
            .ToListAsync();

        // Attempt to map jobId -> jobPath via Jobs table if present
        Dictionary<Guid, string> jobPathMap = new();
        try
        {
            var jobIds = regsRaw.Select(r => r.JobId).Distinct().ToList();
            if (jobIds.Count > 0)
            {
                var jobs = await _db.Jobs.Where(j => jobIds.Contains(j.JobId)).Select(j => new { j.JobId, j.JobPath }).ToListAsync();
                jobPathMap = jobs.ToDictionary(j => j.JobId, j => j.JobPath ?? string.Empty);
            }
        }
        catch { /* Jobs table or mapping not available; proceed */ }

        var regs = regsRaw.Where(r => jobPathMap.TryGetValue(r.JobId, out var jp) && string.Equals(jp, jobPath, StringComparison.OrdinalIgnoreCase))
            .Select(r => new { r.UserId, r.BActive, r.PaidTotal, r.OwedTotal })
            .ToList();
        // Build a map indicating whether each child has any active/paid/owed registration rows for this job.
        // PaidTotal and OwedTotal are non-nullable decimals per the entity model; avoid null-coalescing.
        var regMap = regs
            .GroupBy(r => r.UserId!)
            .ToDictionary(
                g => g.Key,
                g => g.Any(x => (x.BActive ?? false) || x.PaidTotal > 0m || x.OwedTotal > 0m)
            );

        // Optional lightweight debug via response headers (non-breaking for clients)
        if (Request.Query.ContainsKey("debug"))
        {
            try
            {
                Response.Headers["X-FP-Counts"] = $"links={links.Count}; unionChildren={children.Count}; regsRaw={regsRaw.Count}; regsJob={regs.Count}";
                Response.Headers["X-FP-JobPath"] = jobPath;
            }
            catch { /* ignore header failures */ }
        }

        var result = children.Select(c => new
        {
            playerId = c.Id,
            firstName = c.FirstName ?? string.Empty,
            lastName = c.LastName ?? string.Empty,
            gender = c.Gender ?? string.Empty,
            dob = c.Dob.HasValue ? c.Dob.Value.ToString("yyyy-MM-dd") : null,
            registered = regMap.TryGetValue(c.Id, out var isReg) && isReg
        });

        return Ok(result);
    }
}
