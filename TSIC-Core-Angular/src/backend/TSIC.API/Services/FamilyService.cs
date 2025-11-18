using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Transactions;
using System.Text.Json;
using TSIC.API.Dtos;
using TSIC.API.Constants;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.API.Services.Metadata;
using Microsoft.Extensions.Configuration;

namespace TSIC.API.Services;

public sealed class FamilyService : IFamilyService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SqlDbContext _db;
    private readonly IProfileMetadataService _profileMeta;
    private readonly IConfiguration _config;
    private const string DateFormat = "yyyy-MM-dd";

    public FamilyService(UserManager<ApplicationUser> userManager, SqlDbContext db, IProfileMetadataService profileMeta, IConfiguration config)
    {
        _userManager = userManager;
        _db = db;
        _profileMeta = profileMeta;
        _config = config;
    }

    // Generic mapper ported from controller: include ANY dbColumn-backed metadata field present on Registrations.
    private static IReadOnlyDictionary<string, JsonElement> BuildFormValuesDictionary(TSIC.Domain.Entities.Registrations reg, List<(string Name, string DbColumn)> mapped)
    {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (mapped.Count == 0) return dict;

        var regType = typeof(TSIC.Domain.Entities.Registrations);
        var excluded = new HashSet<string>(new[]
        {
            nameof(TSIC.Domain.Entities.Registrations.RegistrationId), nameof(TSIC.Domain.Entities.Registrations.FamilyUserId), nameof(TSIC.Domain.Entities.Registrations.UserId), nameof(TSIC.Domain.Entities.Registrations.AssignedTeamId), nameof(TSIC.Domain.Entities.Registrations.LebUserId),
            nameof(TSIC.Domain.Entities.Registrations.FeeBase), nameof(TSIC.Domain.Entities.Registrations.FeeProcessing), nameof(TSIC.Domain.Entities.Registrations.FeeDiscount), nameof(TSIC.Domain.Entities.Registrations.FeeDonation), nameof(TSIC.Domain.Entities.Registrations.FeeLatefee), nameof(TSIC.Domain.Entities.Registrations.FeeTotal), nameof(TSIC.Domain.Entities.Registrations.OwedTotal), nameof(TSIC.Domain.Entities.Registrations.PaidTotal),
            nameof(TSIC.Domain.Entities.Registrations.Modified),
            "JsonOptions", "JsonFormValues"
        }, StringComparer.OrdinalIgnoreCase);

        foreach (var (name, dbCol) in mapped)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dbCol)) continue;
            var prop = regType.GetProperty(dbCol, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) continue;
            if (excluded.Contains(prop.Name)) continue;

            object? value = prop.GetValue(reg);
            if (value == null) continue;

            JsonElement cloned;
            switch (value)
            {
                case DateTime dt:
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

    public async Task<FamilyPlayersResponseDto> GetFamilyPlayersAsync(string familyUserId, string jobPath)
    {
        if (string.IsNullOrWhiteSpace(familyUserId)) throw new ArgumentException("familyUserId is required", nameof(familyUserId));
        if (string.IsNullOrWhiteSpace(jobPath)) throw new ArgumentException("jobPath is required", nameof(jobPath));

        jobPath = jobPath.Trim();

        Guid? jobId = await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath != null && EF.Functions.Collate(j.JobPath!, "SQL_Latin1_General_CP1_CI_AS") == jobPath)
            .Select(j => (Guid?)j.JobId)
            .FirstOrDefaultAsync();
        // Determine discount code activity and AMEX allowance when job context is known
        bool jobHasActiveDiscountCodes = false;
        bool jobUsesAmex = false;
        if (jobId != null)
        {
            var now = DateTime.UtcNow;
            jobHasActiveDiscountCodes = await _db.JobDiscountCodes
                .AsNoTracking()
                .AnyAsync(dc => dc.JobId == jobId && dc.Active && dc.CodeStartDate <= now && dc.CodeEndDate >= now);

            // Pull CustomerId and evaluate against configured client id list for Amex allowance
            var jobMeta = await _db.Jobs.AsNoTracking().Where(j => j.JobId == jobId)
                .Select(j => new { j.CustomerId }).FirstOrDefaultAsync();
            if (jobMeta != null)
            {
                try
                {
                    var amexIds = _config.GetSection("PaymentMethods_NonMCVisa_ClientIds:Amex").Get<string[]>() ?? Array.Empty<string>();
                    var cust = jobMeta.CustomerId.ToString();
                    jobUsesAmex = amexIds.Any(id => string.Equals(id, cust, StringComparison.OrdinalIgnoreCase));
                }
                catch { jobUsesAmex = false; }
            }
        }

        var linkedChildIds = await _db.FamilyMembers
            .AsNoTracking()
            .Where(fm => fm.FamilyUserId == familyUserId)
            .Select(fm => fm.FamilyMemberUserId)
            .Distinct()
            .ToListAsync();

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

        // Build credit card info (prefer mom, then dad, then asp user)
        string? ccFirst = null;
        string? ccLast = null;
        if (!string.IsNullOrWhiteSpace(fam?.MomFirstName) || !string.IsNullOrWhiteSpace(fam?.MomLastName))
        {
            ccFirst = fam?.MomFirstName?.Trim();
            ccLast = fam?.MomLastName?.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(fam?.DadFirstName) || !string.IsNullOrWhiteSpace(fam?.DadLastName))
        {
            ccFirst = fam?.DadFirstName?.Trim();
            ccLast = fam?.DadLastName?.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(asp?.FirstName) || !string.IsNullOrWhiteSpace(asp?.LastName))
        {
            ccFirst = asp?.FirstName?.Trim();
            ccLast = asp?.LastName?.Trim();
        }
        var ccStreet = asp?.StreetAddress?.Trim();
        var ccZip = asp?.PostalCode?.Trim();
        var ccInfo = new CcInfoDto(ccFirst, ccLast, ccStreet, ccZip);

        if (linkedChildIds.Count == 0)
            return new FamilyPlayersResponseDto(familyUser, Enumerable.Empty<FamilyPlayerDto>(), CcInfo: ccInfo, JobHasActiveDiscountCodes: jobHasActiveDiscountCodes, JobUsesAmex: jobUsesAmex);

        var regsRaw = jobId == null
            ? new List<TSIC.Domain.Entities.Registrations>()
            : await _db.Registrations
                .AsNoTracking()
                .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.UserId != null && linkedChildIds.Contains(r.UserId))
                .ToListAsync();

        var regSet = regsRaw.Select(x => x.UserId!).Distinct().ToHashSet(StringComparer.Ordinal);

        var metadataJson = jobId == null ? null : await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.PlayerProfileMetadataJson)
            .SingleOrDefaultAsync();

        string? rawJsonOptions = jobId == null ? null : await _db.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JsonOptions)
            .SingleOrDefaultAsync();

        // Parse metadata/options via reusable service
        var parsed = _profileMeta.Parse(metadataJson, rawJsonOptions);
        var mappedFields = parsed.MappedFields;
        var typedFields = parsed.TypedFields;
        var waiverFieldNames = parsed.WaiverFieldNames;
        var visibleFieldNames = parsed.VisibleFieldNames;

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

        // visibleFieldNames provided by metadata service

        var regsByUser = regsRaw
            .GroupBy(r => r.UserId!)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r =>
                {
                    var fv = BuildFormValuesDictionary(r, mappedFields);
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

        RegSaverDetailsDto? regSaver = null;
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
                registered,
                prior
            );
        })
        .OrderBy(p => p.LastName)
        .ThenBy(p => p.FirstName)
        .ToList();

        if (jobId != null && typedFields.Count == 0)
        {
            throw new InvalidOperationException("Job profile metadata has no fields; this job must define fields.");
        }

        JobRegFormDto? jobRegForm = null;
        if (typedFields.Count > 0)
        {
            string? constraintType = null;
            if (!string.IsNullOrWhiteSpace(metadataJson) && jobId != null)
            {
                var job = await _db.Jobs.AsNoTracking().Where(j => j.JobId == jobId).Select(j => new { j.JsonOptions, j.CoreRegformPlayer }).FirstOrDefaultAsync();
                string? coreProfile = job?.CoreRegformPlayer;
                if (string.IsNullOrWhiteSpace(rawJsonOptions)) rawJsonOptions = job?.JsonOptions;
                if (!string.IsNullOrWhiteSpace(coreProfile) && coreProfile != "0" && coreProfile != "1")
                {
                    var parts = coreProfile.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var p in parts)
                    {
                        var up = p.ToUpperInvariant();
                        if (up is "BYGRADYEAR" or "BYAGEGROUP" or "BYAGERANGE" or "BYCLUBNAME")
                        {
                            constraintType = up;
                            break;
                        }
                    }
                }
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

        return new FamilyPlayersResponseDto(familyUser, players, regSaver, jobRegForm, ccInfo, JobHasActiveDiscountCodes: jobHasActiveDiscountCodes, JobUsesAmex: jobUsesAmex);
    }

    public async Task<FamilyProfileResponse?> GetMyFamilyAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var aspUser = await _db.AspNetUsers.SingleOrDefaultAsync(u => u.Id == userId);
        if (aspUser == null) return null;

        var fam = await _db.Families.SingleOrDefaultAsync(f => f.FamilyUserId == userId);
        if (fam == null)
        {
            return new FamilyProfileResponse(
                aspUser.UserName ?? string.Empty,
                new PersonDto(aspUser.FirstName ?? string.Empty, aspUser.LastName ?? string.Empty, aspUser.Cellphone ?? string.Empty, aspUser.Email ?? string.Empty),
                new PersonDto(string.Empty, string.Empty, string.Empty, string.Empty),
                new AddressDto(aspUser.StreetAddress ?? string.Empty, aspUser.City ?? string.Empty, aspUser.State ?? string.Empty, aspUser.PostalCode ?? string.Empty),
                new List<ChildDto>()
            );
        }

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

        return new FamilyProfileResponse(
            aspUser.UserName ?? string.Empty,
            primary,
            secondary,
            new AddressDto(aspUser.StreetAddress ?? string.Empty, aspUser.City ?? string.Empty, aspUser.State ?? string.Empty, aspUser.PostalCode ?? string.Empty),
            childDtos
        );
    }

    public async Task<FamilyRegistrationResponse> RegisterAsync(FamilyRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new FamilyRegistrationResponse(false, null, null, "Username and password are required");
        }
        if (request.Children == null || request.Children.Count == 0)
        {
            return new FamilyRegistrationResponse(false, null, null, "At least one child is required.");
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        var user = new ApplicationUser
        {
            UserName = request.Username,
            Email = request.Primary.Email,
            FirstName = request.Primary.FirstName,
            LastName = request.Primary.LastName,
            Cellphone = request.Primary.Cellphone,
            Phone = request.Primary.Cellphone,
            StreetAddress = request.Address.StreetAddress,
            City = request.Address.City,
            State = request.Address.State,
            PostalCode = request.Address.PostalCode,
            Modified = DateTime.UtcNow
        };
        var createResult = await _userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var msg = string.Join("; ", createResult.Errors.Select(e => e.Description));
            return new FamilyRegistrationResponse(false, null, null, msg);
        }

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

        // Create and link children
        foreach (var child in request.Children)
        {
            var (ok, error) = await CreateAndLinkChildAsync(child, fam.FamilyUserId);
            if (!ok)
            {
                return new FamilyRegistrationResponse(false, null, null, error);
            }
        }

        await _db.SaveChangesAsync();
        scope.Complete();
        return new FamilyRegistrationResponse(true, user.Id, Guid.Empty, null);
    }

    public async Task<FamilyRegistrationResponse> UpdateAsync(FamilyUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return new FamilyRegistrationResponse(false, null, null, "Username is required");
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null) return new FamilyRegistrationResponse(false, null, null, "User not found");

        var aspUser = await _db.AspNetUsers.SingleOrDefaultAsync(u => u.Id == user.Id);
        if (aspUser != null)
        {
            aspUser.StreetAddress = request.Address.StreetAddress;
            aspUser.City = request.Address.City;
            aspUser.State = request.Address.State;
            aspUser.PostalCode = request.Address.PostalCode;
            aspUser.Cellphone = request.Primary.Cellphone;
            aspUser.Phone = request.Primary.Cellphone;
            if (string.IsNullOrWhiteSpace(aspUser.LebUserId)) aspUser.LebUserId = TsicConstants.SuperUserId;
            await _db.SaveChangesAsync();
        }

        var fam = await _db.Families.SingleOrDefaultAsync(f => f.FamilyUserId == user.Id);
        if (fam == null) return new FamilyRegistrationResponse(false, null, null, "Family record not found");

        fam.MomFirstName = request.Primary.FirstName;
        fam.MomLastName = request.Primary.LastName;
        fam.MomCellphone = request.Primary.Cellphone;
        fam.MomEmail = request.Primary.Email;
        fam.DadFirstName = request.Secondary.FirstName;
        fam.DadLastName = request.Secondary.LastName;
        fam.DadCellphone = request.Secondary.Cellphone;
        fam.DadEmail = request.Secondary.Email;
        fam.Modified = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(fam.LebUserId)) fam.LebUserId = TsicConstants.SuperUserId;
        await _db.SaveChangesAsync();

        // Simplify: controller had complex child sync logic. Preserve existing children unchanged for now.
        // Future: extract full child synchronization rules into this service if still required.

        scope.Complete();
        return new FamilyRegistrationResponse(true, user.Id, Guid.Empty, null);
    }

    private async Task<(bool ok, string? error)> CreateAndLinkChildAsync(ChildDto child, string familyUserId)
    {
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
            return (false, $"Failed to create child profile: {msg}");
        }

        var fm = new TSIC.Domain.Entities.FamilyMembers
        {
            FamilyUserId = familyUserId,
            FamilyMemberUserId = childUser.Id,
            Modified = DateTime.UtcNow,
            LebUserId = TsicConstants.SuperUserId
        };
        _db.FamilyMembers.Add(fm);
        return (true, null);
    }
}
