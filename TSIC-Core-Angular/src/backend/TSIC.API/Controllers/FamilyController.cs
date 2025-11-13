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
using System.Text.Json;

namespace TSIC.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SqlDbContext _db;
    private const string DateFormat = "yyyy-MM-dd";

    // Generic mapper: include ANY dbColumn-backed metadata field present on Registrations.
    // Avoid special-casing (fees, waivers, sportAssnId, etc.) so client can rely on a single source of truth.
    private static IReadOnlyDictionary<string, JsonElement> BuildFormValuesDictionary(Registrations reg, List<(string Name, string DbColumn)> mapped)
    {
        // Design Principle (central invariant): Each registration row is an immutable snapshot of form values
        // at the time that registration was created or explicitly updated. We do NOT merge values across
        // multiple registrations for a player. The API returns per-registration FormValues; the client
        // chooses which snapshot to display or copy when creating new registrations.
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (mapped.Count == 0) return dict;

        var regType = typeof(Registrations);
        // Exclude infrastructure/system columns that should never surface as editable form fields even if
        // profile metadata mistakenly maps them. Keep user-entered domain fields (waivers, membership numbers, apparel, etc.).
        var excluded = new HashSet<string>(new[]
        {
            // Identity / linkage
            nameof(Registrations.RegistrationId), nameof(Registrations.FamilyUserId), nameof(Registrations.UserId), nameof(Registrations.AssignedTeamId), nameof(Registrations.LebUserId),
            // Financial snapshot columns (not user-entered form fields)
            nameof(Registrations.FeeBase), nameof(Registrations.FeeProcessing), nameof(Registrations.FeeDiscount), nameof(Registrations.FeeDonation), nameof(Registrations.FeeLatefee), nameof(Registrations.FeeTotal), nameof(Registrations.OwedTotal), nameof(Registrations.PaidTotal),
            // Timestamps / audit
            nameof(Registrations.Modified),
            // Misc technical columns that may appear in legacy metadata dumps
            "JsonOptions", "JsonFormValues"
        }, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, dbCol) in mapped)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dbCol)) continue;

            // Use case-insensitive property lookup to tolerate metadata casing differences.
            var prop = regType.GetProperty(dbCol, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) continue; // Unknown property -> skip (extensibility without errors)
            if (excluded.Contains(prop.Name)) continue; // System/infrastructure field -> skip

            object? value = prop.GetValue(reg);
            if (value == null) continue; // Unset -> omit from FormValues

            // Serialize primitive/object value into a JSON element preserving native types (bool/number/string/date)
            // For DateTime/DateTime? normalize to yyyy-MM-dd when time component not needed.
            JsonElement cloned;
            switch (value)
            {
                case DateTime dt:
                    // Preserve date only if time is midnight, else serialize full ISO 8601
                    var normalized = dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd") : dt.ToString("O");
                    cloned = JsonDocument.Parse(JsonSerializer.Serialize(normalized)).RootElement.Clone();
                    break;
                case DateTimeOffset dto:
                    cloned = JsonDocument.Parse(JsonSerializer.Serialize(dto.ToString("O"))).RootElement.Clone();
                    break;
                default:
                    cloned = JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement.Clone();
                    break;
            }
            dict[name] = cloned;
        }
        return dict;
    }

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
        var aspUser = await _db.AspNetUsers.SingleOrDefaultAsync(u => u.Id == userId);
        if (aspUser == null) return NotFound();

        var fam = await _db.Families.SingleOrDefaultAsync(f => f.FamilyUserId == userId);
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
            c.Dob?.ToString(DateFormat),
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
        var aspUser = await _db.AspNetUsers.SingleOrDefaultAsync(u => u.Id == user.Id);
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
        var fam = await _db.Families.SingleOrDefaultAsync(f => f.FamilyUserId == user.Id);
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
        var fam = await _db.Families.SingleOrDefaultAsync(f => f.FamilyUserId == callerId);
        if (fam == null)
        {
            return Ok(Array.Empty<object>());
        }

        // Display name preference: MomFirstName/LastName then Dad fallback then username (from AspNetUsers)
        var aspUser = await _db.AspNetUsers.SingleOrDefaultAsync(u => u.Id == callerId);
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

    // ...existing code...
    [HttpGet("players")]
    [Authorize]
    [ProducesResponseType(typeof(FamilyPlayersResponseDto), 200)]
    public async Task<IActionResult> GetFamilyPlayers([FromQuery] string jobPath)
    {
        if (string.IsNullOrWhiteSpace(jobPath))
            return BadRequest(new { message = "jobPath is required" });

        var familyUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(familyUserId))
            return Unauthorized();

        jobPath = jobPath.Trim();

        // Resolve jobId from jobPath (case-insensitive); if not found, treat as no prior registrations.
        Guid? jobId = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath != null && EF.Functions.Collate(j.JobPath!, "SQL_Latin1_General_CP1_CI_AS") == jobPath.Trim())
            .Select(j => (Guid?)j.JobId)
            .FirstOrDefaultAsync();

        // Linked children for this family
        var linkedChildIds = await _db.FamilyMembers
            .AsNoTracking()
            .Where(fm => fm.FamilyUserId == familyUserId)
            .Select(fm => fm.FamilyMemberUserId)
            .Distinct()
            .ToListAsync();

        // Build family header (always present in response)
        var fam = await _db.Families.AsNoTracking().SingleOrDefaultAsync(f => f.FamilyUserId == familyUserId);
        var asp = await _db.AspNetUsers.AsNoTracking().SingleOrDefaultAsync(u => u.Id == familyUserId);

        string display;
        if (!string.IsNullOrWhiteSpace(fam?.MomFirstName) || !string.IsNullOrWhiteSpace(fam?.MomLastName))
            display = $"{fam?.MomFirstName} {fam?.MomLastName}".Trim();
        else if (!string.IsNullOrWhiteSpace(fam?.DadFirstName) || !string.IsNullOrWhiteSpace(fam?.DadLastName))
            display = $"{fam?.DadFirstName} {fam?.DadLastName}".Trim();
        else if (!string.IsNullOrWhiteSpace(asp?.FirstName) || !string.IsNullOrWhiteSpace(asp?.LastName))
            display = $"{asp?.FirstName} {asp?.LastName}".Trim();
        else
            display = asp?.UserName ?? "Family";

        var familyUser = new FamilyUserSummaryDto(familyUserId, display, asp?.UserName ?? string.Empty);

        if (linkedChildIds.Count == 0)
            return Ok(new FamilyPlayersResponseDto(familyUser, Enumerable.Empty<FamilyPlayerDto>()));

        // Compute registrations for this job and family among linked children (existence-only semantics + summaries)
        var regsRaw = jobId == null
            ? new List<Registrations>()
            : await _db.Registrations
                .AsNoTracking()
                .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.UserId != null && linkedChildIds.Contains(r.UserId))
                .ToListAsync();

        var regSet = regsRaw.Select(x => x.UserId!).Distinct().ToHashSet(StringComparer.Ordinal);

        // Load profile metadata fields (for mapping DbColumn -> FormValues)
        var metadataJson = jobId == null ? null : await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.PlayerProfileMetadataJson)
            .SingleOrDefaultAsync();
        // Load job JsonOptions (option sets for select fields)
        string? rawJsonOptions = jobId == null ? null : await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JsonOptions)
            .SingleOrDefaultAsync();

        List<(string Name, string DbColumn)> mappedFields = new();
        List<ProfileMetadataField> typedFields = new();
        // Capture raw waiver-like field names for JobRegForm (reuse detection logic later if needed)
        var waiverFieldNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(metadataJson);
                if (doc.RootElement.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in fieldsEl.EnumerateArray())
                    {
                        var name = f.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
                        var dbCol = f.TryGetProperty("dbColumn", out var dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty;
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dbCol))
                        {
                            mappedFields.Add((name, dbCol));
                        }

                        // Build a typed field for richer metadata (validators/options)
                        var field = new ProfileMetadataField
                        {
                            Name = name,
                            DbColumn = dbCol,
                            DisplayName = f.TryGetProperty("displayName", out var dnEl) ? (dnEl.GetString() ?? name) : name,
                            InputType = f.TryGetProperty("inputType", out var itEl) ? (itEl.GetString() ?? "TEXT") : "TEXT",
                            DataSource = f.TryGetProperty("dataSource", out var dsEl) ? dsEl.GetString() : null,
                            Order = f.TryGetProperty("order", out var ordEl) && ordEl.ValueKind == JsonValueKind.Number ? ordEl.GetInt32() : 0,
                            Visibility = f.TryGetProperty("visibility", out var visEl) ? (visEl.GetString() ?? "public") : "public",
                            Computed = f.TryGetProperty("computed", out var compEl) && compEl.ValueKind == JsonValueKind.True,
                        };
                        // Legacy adminOnly flag support -> map to Visibility
                        try
                        {
                            if (f.TryGetProperty("adminOnly", out var aoEl) && aoEl.ValueKind == JsonValueKind.True)
                            {
                                field.Visibility = "adminOnly";
                            }
                        }
                        catch { /* ignore */ }

                        // options
                        if (f.TryGetProperty("options", out var optsEl) && optsEl.ValueKind == JsonValueKind.Array)
                        {
                            var opts = new List<ProfileFieldOption>();
                            foreach (var o in optsEl.EnumerateArray())
                            {
                                var val = o.TryGetProperty("value", out var vEl) ? (vEl.GetString() ?? string.Empty) : string.Empty;
                                var lab = o.TryGetProperty("label", out var lEl) ? (lEl.GetString() ?? string.Empty) : string.Empty;
                                opts.Add(new ProfileFieldOption { Value = val, Label = lab });
                            }
                            field.Options = opts;
                        }

                        // validation
                        if (f.TryGetProperty("validation", out var valEl) && valEl.ValueKind == JsonValueKind.Object)
                        {
                            var v = new FieldValidation
                            {
                                Required = valEl.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.True,
                                Email = valEl.TryGetProperty("email", out var emEl) && emEl.ValueKind == JsonValueKind.True,
                                RequiredTrue = valEl.TryGetProperty("requiredTrue", out var rtEl) && rtEl.ValueKind == JsonValueKind.True,
                                MinLength = valEl.TryGetProperty("minLength", out var minLEl) && minLEl.ValueKind == JsonValueKind.Number ? minLEl.GetInt32() : null,
                                MaxLength = valEl.TryGetProperty("maxLength", out var maxLEl) && maxLEl.ValueKind == JsonValueKind.Number ? maxLEl.GetInt32() : null,
                                Pattern = valEl.TryGetProperty("pattern", out var patEl) ? patEl.GetString() : null,
                                Min = valEl.TryGetProperty("min", out var minEl) && (minEl.ValueKind == JsonValueKind.Number) ? minEl.GetDouble() : null,
                                Max = valEl.TryGetProperty("max", out var maxEl) && (maxEl.ValueKind == JsonValueKind.Number) ? maxEl.GetDouble() : null,
                                Compare = valEl.TryGetProperty("compare", out var cmpEl) ? cmpEl.GetString() : null,
                                Remote = valEl.TryGetProperty("remote", out var remEl) ? remEl.GetString() : null,
                                Message = valEl.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null
                            };
                            field.Validation = v;
                        }

                        // conditionalOn
                        if (f.TryGetProperty("conditionalOn", out var condEl) && condEl.ValueKind == JsonValueKind.Object)
                        {
                            var cond = new FieldCondition
                            {
                                Field = condEl.TryGetProperty("field", out var cf) ? (cf.GetString() ?? string.Empty) : string.Empty,
                                Operator = condEl.TryGetProperty("operator", out var op) ? (op.GetString() ?? "equals") : "equals",
                                Value = condEl.TryGetProperty("value", out var cv) ? JsonSerializer.Deserialize<object>(cv.GetRawText()) : null
                            };
                            field.ConditionalOn = cond;
                        }

                        if (!string.IsNullOrWhiteSpace(field.Name))
                        {
                            typedFields.Add(field);
                            // Basic waiver heuristic for schema-level listing (mirrors client logic)
                            var lname = field.Name.ToLowerInvariant();
                            var ldisp = field.DisplayName.ToLowerInvariant();
                            var isCheckbox = (field.InputType ?? "").ToLowerInvariant().Contains("checkbox");
                            bool looksLikeWaiver = isCheckbox && (
                                ldisp.StartsWith("i agree") ||
                                ldisp.Contains("waiver") || ldisp.Contains("release") ||
                                (ldisp.Contains("code") && ldisp.Contains("conduct")) ||
                                ldisp.Contains("refund") || (ldisp.Contains("terms") && ldisp.Contains("conditions"))
                            );
                            if (looksLikeWaiver || lname.Contains("waiver") || lname.Contains("codeofconduct") || lname.Contains("refund"))
                            {
                                waiverFieldNames.Add(field.Name);
                            }
                        }
                    }
                }
            }
            catch { /* swallow parse errors; FormValues will be empty and no FormFields will be returned */ }
        }

        // Enrich typed field options from JsonOptions when available using DataSource key
        if (!string.IsNullOrWhiteSpace(rawJsonOptions) && typedFields.Count > 0)
        {
            try
            {
                using var optsDoc = JsonDocument.Parse(rawJsonOptions);
                if (optsDoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Build a case-insensitive lookup of option sets
                    var optionSets = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in optsDoc.RootElement.EnumerateObject())
                    {
                        optionSets[prop.Name] = prop.Value;
                    }
                    List<ProfileFieldOption> MapOptions(JsonElement set)
                    {
                        var list = new List<ProfileFieldOption>();
                        if (set.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in set.EnumerateArray())
                            {
                                if (el.ValueKind == JsonValueKind.Object)
                                {
                                    string GetStr(string a, string b) => el.TryGetProperty(a, out var x) ? (x.GetString() ?? string.Empty) : (el.TryGetProperty(b, out var y) ? (y.GetString() ?? string.Empty) : string.Empty);
                                    var val = GetStr("value", "Value");
                                    if (string.IsNullOrWhiteSpace(val)) val = GetStr("id", "Id");
                                    if (string.IsNullOrWhiteSpace(val)) val = GetStr("code", "Code");
                                    if (string.IsNullOrWhiteSpace(val)) val = GetStr("year", "Year");
                                    var lab = GetStr("label", "Label");
                                    if (string.IsNullOrWhiteSpace(lab)) lab = GetStr("text", "Text");
                                    if (string.IsNullOrWhiteSpace(lab)) lab = val;
                                    list.Add(new ProfileFieldOption { Value = val, Label = lab });
                                }
                                else if (el.ValueKind == JsonValueKind.String)
                                {
                                    var v = el.GetString() ?? string.Empty; list.Add(new ProfileFieldOption { Value = v, Label = v });
                                }
                                else if (el.ValueKind == JsonValueKind.Number || el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
                                {
                                    var v = el.GetRawText(); list.Add(new ProfileFieldOption { Value = v, Label = v });
                                }
                            }
                        }
                        return list;
                    }
                    foreach (var tf in typedFields)
                    {
                        if (!string.IsNullOrWhiteSpace(tf.DataSource) && optionSets.TryGetValue(tf.DataSource!, out var setEl))
                        {
                            var mapped = MapOptions(setEl);
                            if (mapped.Count > 0)
                            {
                                tf.Options = mapped;
                            }
                        }
                    }
                }
            }
            catch { /* ignore malformed JsonOptions */ }
        }

        // Map team names for assigned teams (if any)
        var teamNameMap = new Dictionary<Guid, string>();
        if (jobId != null)
        {
            var teamIds = regsRaw.Where(x => x.AssignedTeamId.HasValue).Select(x => x.AssignedTeamId!.Value).Distinct().ToList();
            if (teamIds.Count > 0)
            {
                teamNameMap = await _db.Teams
                    .AsNoTracking()
                    .Where(t => t.JobId == jobId && teamIds.Contains(t.TeamId))
                    .ToDictionaryAsync(t => t.TeamId, t => t.TeamName ?? string.Empty);
            }
        }

        // Prepare set of visible (non-hidden, non-adminOnly) field names for filtering
        var visibleFieldNames = new HashSet<string>(
            typedFields
                .Where(tf => !string.Equals(tf.Visibility, "hidden", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(tf.Visibility, "adminOnly", StringComparison.OrdinalIgnoreCase))
                .Select(tf => tf.Name),
            StringComparer.OrdinalIgnoreCase);

        var regsByUser = regsRaw
            .GroupBy(r => r.UserId!)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r =>
                {
                    var fv = BuildFormValuesDictionary(r, mappedFields);
                    // Visible-only dictionary for registration working values (authoritative per-registration values)
                    IReadOnlyDictionary<string, JsonElement>? formFieldValues = null;
                    if (visibleFieldNames.Count > 0)
                    {
                        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                        foreach (var name in visibleFieldNames)
                        {
                            if (fv.TryGetValue(name, out var found))
                            {
                                var clone = JsonDocument.Parse(found.GetRawText()).RootElement.Clone();
                                dict[name] = clone;
                            }
                        }
                        formFieldValues = dict;
                    }

                    return new FamilyPlayerRegistrationDto(
                        r.RegistrationId,
                        r.BActive == true,
                        new RegistrationFinancialsDto(
                            r.FeeBase,
                            r.FeeProcessing,
                            r.FeeDiscount,
                            r.FeeDonation,
                            r.FeeLatefee,
                            r.FeeTotal,
                            r.OwedTotal,
                            r.PaidTotal
                        ),
                        r.AssignedTeamId,
                        r.AssignedTeamId.HasValue && teamNameMap.ContainsKey(r.AssignedTeamId.Value) ? teamNameMap[r.AssignedTeamId.Value] : null,
                        formFieldValues ?? new Dictionary<string, JsonElement>()
                    );
                }).ToList(),
                StringComparer.Ordinal);

        // RegSaver details: pick first active registration with policy; else any registration with policy
        RegSaverDetailsDto? regSaver = null;
        // Placeholder variables 'withPolicy' and 'policySource' removed (unused).

        // Refine: actually need policy fields from registrations
        var regSaverRaw = jobId == null ? null : await _db.Registrations
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.RegsaverPolicyId != null)
            .OrderByDescending(r => r.BActive == true)
            .ThenByDescending(r => r.RegsaverPolicyIdCreateDate)
            .Select(r => new { r.RegsaverPolicyId, r.RegsaverPolicyIdCreateDate })
            .FirstOrDefaultAsync();
        if (regSaverRaw != null && !string.IsNullOrWhiteSpace(regSaverRaw.RegsaverPolicyId) && regSaverRaw.RegsaverPolicyIdCreateDate.HasValue)
        {
            regSaver = new RegSaverDetailsDto(regSaverRaw.RegsaverPolicyId, regSaverRaw.RegsaverPolicyIdCreateDate.Value);
        }

        // Load child profiles and project DTOs
        var children = await _db.AspNetUsers
            .AsNoTracking()
            .Where(u => linkedChildIds.Contains(u.Id))
            .ToListAsync();

        var players = children.Select(c =>
        {
            var prior = regsByUser.TryGetValue(c.Id, out var list) ? (IReadOnlyList<FamilyPlayerRegistrationDto>)list : Array.Empty<FamilyPlayerRegistrationDto>();
            var registered = regSet.Contains(c.Id);
            return new FamilyPlayerDto(
                c.Id,
                c.FirstName ?? string.Empty,
                c.LastName ?? string.Empty,
                c.Gender ?? string.Empty,
                c.Dob.HasValue ? c.Dob.Value.ToString(DateFormat) : null,
                registered,
                registered, // Selected defaults to Registered; UI may toggle when not registered
                prior
            );
        })
        .OrderBy(p => p.LastName)
        .ThenBy(p => p.FirstName)
        .ToList();

        // Build JobRegForm (immutable schema) if we have typedFields
        JobRegFormDto? jobRegForm = null;
        if (typedFields.Count > 0)
        {
            // Derive constraint type heuristically from JsonOptions (BYGRADYEAR etc.) - conservative reuse of earlier logic
            string? constraintType = null;
            if (!string.IsNullOrWhiteSpace(metadataJson) && jobId != null)
            {
                // Simple scan of JsonOptions for explicit constraint keys
                var job = await _db.Jobs.AsNoTracking().Where(j => j.JobId == jobId).Select(j => new { j.JsonOptions, j.CoreRegformPlayer }).FirstOrDefaultAsync();
                string? coreProfile = job?.CoreRegformPlayer;
                if (string.IsNullOrWhiteSpace(rawJsonOptions)) rawJsonOptions = job?.JsonOptions;
                if (!string.IsNullOrWhiteSpace(rawJsonOptions))
                {
                    try
                    {
                        using var optsDoc = JsonDocument.Parse(rawJsonOptions);
                        foreach (var prop in optsDoc.RootElement.EnumerateObject())
                        {
                            var key = prop.Name.ToLowerInvariant();
                            if (key is "constrainttype" or "teamconstraint" or "eligibilityconstraint")
                            {
                                var val = prop.Value.GetRawText().Trim('"').ToUpperInvariant();
                                if (val is "BYGRADYEAR" or "BYAGEGROUP" or "BYAGERANGE" or "BYCLUBNAME")
                                {
                                    constraintType = val;
                                    break;
                                }
                            }
                        }
                    }
                    catch { /* ignore malformed JsonOptions */ }
                }
                // Compute version token from metadata + options presence (stable-ish)
                var versionSeed = $"{jobId}-{metadataJson?.Length ?? 0}-{rawJsonOptions?.Length ?? 0}-{typedFields.Count}";
                var version = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(versionSeed))).Substring(0, 16);
                jobRegForm = new JobRegFormDto(
                    version,
                    coreProfile,
                    typedFields.Select(tf => new JobRegFieldDto(
                        tf.Name,
                        tf.DbColumn,
                        string.IsNullOrWhiteSpace(tf.DisplayName) ? tf.Name : tf.DisplayName,
                        string.IsNullOrWhiteSpace(tf.InputType) ? "TEXT" : tf.InputType,
                        tf.DataSource,
                        tf.Options,
                        tf.Validation,
                        tf.Order,
                        string.IsNullOrWhiteSpace(tf.Visibility) ? "public" : tf.Visibility,
                        tf.Computed,
                        tf.ConditionalOn
                    )).ToList(),
                    waiverFieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    constraintType
                );
            }
        }

        return Ok(new FamilyPlayersResponseDto(familyUser, players, regSaver, jobRegForm));
    }
    // ...existing code...
}
