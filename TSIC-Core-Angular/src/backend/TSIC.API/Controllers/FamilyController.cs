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

        // Build lookup maps for existing children
        var mapByEmail = existingChildrenUsers
            .Where(c => !string.IsNullOrWhiteSpace(c.Email))
            .ToDictionary(c => KeyEmail(c.Email!), c => c, StringComparer.OrdinalIgnoreCase);
        var mapByPhone = existingChildrenUsers
            .Where(c => !string.IsNullOrWhiteSpace(c.Cellphone) || !string.IsNullOrWhiteSpace(c.Phone))
            .ToDictionary(c => KeyPhone(c.Cellphone ?? c.Phone), c => c);
        var mapByNameDob = existingChildrenUsers
            .ToDictionary(c => KeyNameDob(c.FirstName, c.LastName, c.Dob), c => c);

        var matchedChildIds = new HashSet<string>();

        foreach (var child in request.Children ?? new List<ChildDto>())
        {
            // Resolve match
            AspNetUsers? aspChild = null;
            if (!string.IsNullOrWhiteSpace(child.Email))
            {
                mapByEmail.TryGetValue(KeyEmail(child.Email), out aspChild);
            }
            if (aspChild == null && !string.IsNullOrWhiteSpace(child.Phone))
            {
                mapByPhone.TryGetValue(KeyPhone(child.Phone), out aspChild);
            }
            DateTime? childDob = null;
            if (!string.IsNullOrWhiteSpace(child.Dob) && DateTime.TryParse(child.Dob, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDob2))
            {
                childDob = parsedDob2.Date;
            }
            if (aspChild == null)
            {
                mapByNameDob.TryGetValue(KeyNameDob(child.FirstName, child.LastName, childDob), out aspChild);
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
}
