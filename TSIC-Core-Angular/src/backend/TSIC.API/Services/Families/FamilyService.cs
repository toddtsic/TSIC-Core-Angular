using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Transactions;
using System.Text.Json;
using TSIC.Contracts.Dtos;
using TSIC.API.Services.Metadata;
using TSIC.Contracts.Services;
using TSIC.Application.Services.Users;
using TSIC.Application.Services.Shared.Mapping;
using TSIC.Domain.Constants;
using TSIC.Infrastructure.Data.Identity;
using TSIC.Contracts.Repositories;
using Microsoft.Extensions.Configuration;

namespace TSIC.API.Services.Families;

public sealed class FamilyService : IFamilyService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IProfileMetadataService _profileMeta;
    private readonly IConfiguration _config;
    private readonly IUserPrivilegeLevelService _privilegeService;
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IFamiliesRepository _familiesRepo;
    private readonly IUserRepository _userRepo;
    private readonly IJobDiscountCodeRepository _jobDiscountRepo;
    private readonly IFamilyMemberRepository _familyMemberRepo;
    private const string DateFormat = "yyyy-MM-dd";

    public FamilyService(
        UserManager<ApplicationUser> userManager,
        IProfileMetadataService profileMeta,
        IConfiguration config,
        IUserPrivilegeLevelService privilegeService,
        IJobRepository jobRepo,
        IRegistrationRepository registrationRepo,
        ITeamRepository teamRepo,
        IFamiliesRepository familiesRepo,
        IUserRepository userRepo,
        IJobDiscountCodeRepository jobDiscountRepo,
        IFamilyMemberRepository familyMemberRepo)
    {
        _userManager = userManager;
        _profileMeta = profileMeta;
        _config = config;
        _privilegeService = privilegeService;
        _jobRepo = jobRepo;
        _registrationRepo = registrationRepo;
        _teamRepo = teamRepo;
        _familiesRepo = familiesRepo;
        _userRepo = userRepo;
        _jobDiscountRepo = jobDiscountRepo;
        _familyMemberRepo = familyMemberRepo;
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

        Guid? jobId = await _jobRepo.GetJobIdByPathAsync(jobPath);

        var (jobHasActiveDiscountCodes, jobUsesAmex) = await GetJobPaymentFeaturesAsync(jobId);

        var linkedChildIds = await _familyMemberRepo.GetChildUserIdsAsync(familyUserId);

        var fam = await _familiesRepo.GetByFamilyUserIdAsync(familyUserId);
        var asp = await _userRepo.GetByIdAsync(familyUserId);

        var familyUser = BuildFamilyUserSummary(familyUserId, fam, asp);
        var ccInfo = BuildCreditCardInfo(fam, asp);

        if (linkedChildIds.Count == 0)
            return new FamilyPlayersResponseDto { FamilyUser = familyUser, FamilyPlayers = Enumerable.Empty<FamilyPlayerDto>(), CcInfo = ccInfo, JobHasActiveDiscountCodes = jobHasActiveDiscountCodes, JobUsesAmex = jobUsesAmex };

        // Load registrations for this job
        var regsRaw = await LoadRegistrationsForJobAsync(jobId, familyUserId, linkedChildIds);
        var regSet = regsRaw.Select(x => x.UserId!).Distinct().ToHashSet(StringComparer.Ordinal);

        // Fetch metadata and options
        var (metadataJson, rawJsonOptions) = await GetJobMetadataAndOptionsAsync(jobId);

        // Parse metadata/options via reusable service
        var parsed = _profileMeta.Parse(metadataJson, rawJsonOptions);
        var mappedFields = parsed.MappedFields;
        var typedFields = parsed.TypedFields;
        var waiverFieldNames = parsed.WaiverFieldNames;
        var visibleFieldNames = parsed.VisibleFieldNames;

        // Build team name lookup
        var teamNameMap = await BuildTeamNameMapAsync(jobId, regsRaw);

        // Build registration DTOs by user
        var regsByUser = regsRaw
            .GroupBy(r => r.UserId!)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => BuildRegistrationDto(r, mappedFields, visibleFieldNames, teamNameMap)).ToList(),
                StringComparer.Ordinal);

        // For players not yet registered in this job, compute latest defaults across ALL jobs.
        var allRegsByUser = await LoadAllRegistrationsByUserAsync(linkedChildIds);

        // Extract RegSaver details
        var regSaver = await ExtractRegSaverDetailsAsync(jobId, familyUserId);

        // Build player DTOs
        var children = await _userRepo.Query()
            .AsNoTracking()
            .Where(u => linkedChildIds.Contains(u.Id))
            .ToListAsync();

        var players = BuildPlayerDtos(children, regsByUser, regSet, allRegsByUser, jobId, mappedFields, visibleFieldNames);

        if (jobId != null && typedFields.Count == 0)
        {
            throw new InvalidOperationException("Job profile metadata has no fields; this job must define fields.");
        }

        // Build job registration form DTO
        JobRegFormDto? jobRegForm = null;
        if (typedFields.Count > 0)
        {
            jobRegForm = await BuildJobRegFormDtoAsync(jobId, metadataJson, rawJsonOptions, typedFields, waiverFieldNames);
        }

        return new FamilyPlayersResponseDto { FamilyUser = familyUser, FamilyPlayers = players, RegSaverDetails = regSaver, JobRegForm = jobRegForm, CcInfo = ccInfo, JobHasActiveDiscountCodes = jobHasActiveDiscountCodes, JobUsesAmex = jobUsesAmex };
    }

    // Build a dictionary of latest non-null values per visible field from a user's registration history (most-recent-first list)
    private static IReadOnlyDictionary<string, JsonElement> BuildLatestVisibleFieldValues(
        List<TSIC.Domain.Entities.Registrations> history,
        List<(string Name, string DbColumn)> mapped,
        IEnumerable<string> visibleFieldNames)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (history.Count == 0 || mapped.Count == 0 || !visibleFieldNames.Any()) return result;

        var regType = typeof(TSIC.Domain.Entities.Registrations);
        // Prepare property map for visible fields only
        var props = new List<(string Name, System.Reflection.PropertyInfo Prop)>();
        var visible = new HashSet<string>(visibleFieldNames, StringComparer.OrdinalIgnoreCase);
        foreach (var (name, dbCol) in mapped)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dbCol)) continue;
            if (!visible.Contains(name)) continue;
            var pi = regType.GetProperty(dbCol, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (pi != null) props.Add((name, pi));
        }

        foreach (var (name, pi) in props)
        {
            foreach (var reg in history)
            {
                var val = pi.GetValue(reg);
                if (val == null) continue;
                JsonElement cloned;
                switch (val)
                {
                    case DateTime dt:
                        var normalized = dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd") : dt.ToString("O");
                        cloned = JsonDocument.Parse(JsonSerializer.Serialize(normalized)).RootElement.Clone();
                        break;
                    case DateTimeOffset dto:
                        cloned = JsonDocument.Parse(JsonSerializer.Serialize(dto.ToString("O"))).RootElement.Clone();
                        break;
                    default:
                        cloned = JsonDocument.Parse(JsonSerializer.Serialize(val)).RootElement.Clone();
                        break;
                }
                result[name] = cloned;
                break; // move to next field once first non-null found
            }
        }
        return result;
    }

    public async Task<FamilyProfileResponse?> GetMyFamilyAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var aspUser = await _userRepo.GetByIdAsync(userId);
        if (aspUser == null) return null;

        var fam = await _familiesRepo.GetByFamilyUserIdAsync(userId);
        if (fam == null)
        {
            return new FamilyProfileResponse
            {
                Username = aspUser.UserName ?? string.Empty,
                Primary = new PersonDto
                {
                    FirstName = aspUser.FirstName ?? string.Empty,
                    LastName = aspUser.LastName ?? string.Empty,
                    Cellphone = aspUser.Cellphone ?? string.Empty,
                    Email = aspUser.Email ?? string.Empty
                },
                Secondary = new PersonDto
                {
                    FirstName = string.Empty,
                    LastName = string.Empty,
                    Cellphone = string.Empty,
                    Email = string.Empty
                },
                Address = new AddressDto
                {
                    StreetAddress = aspUser.StreetAddress ?? string.Empty,
                    City = aspUser.City ?? string.Empty,
                    State = aspUser.State ?? string.Empty,
                    PostalCode = aspUser.PostalCode ?? string.Empty
                },
                Children = new List<ChildDto>()
            };
        }

        var childIds = await _familyMemberRepo.GetChildUserIdsAsync(fam.FamilyUserId);
        var children = await _userRepo.Query().Where(u => childIds.Contains(u.Id)).ToListAsync();

        var childDtos = children.Select(c => new ChildDto
        {
            FirstName = c.FirstName ?? string.Empty,
            LastName = c.LastName ?? string.Empty,
            Gender = c.Gender ?? string.Empty,
            Dob = c.Dob?.ToString(DateFormat),
            Email = c.Email,
            Phone = c.Cellphone ?? c.Phone
        }).ToList();

        string Fallback(string? primary, string? fallback) => !string.IsNullOrWhiteSpace(primary) ? primary! : (fallback ?? string.Empty);

        var primary = new PersonDto
        {
            FirstName = Fallback(fam.MomFirstName, aspUser.FirstName),
            LastName = Fallback(fam.MomLastName, aspUser.LastName),
            Cellphone = Fallback(fam.MomCellphone, aspUser.Cellphone ?? aspUser.Phone),
            Email = Fallback(fam.MomEmail, aspUser.Email)
        };

        var secondary = new PersonDto
        {
            FirstName = fam.DadFirstName ?? string.Empty,
            LastName = fam.DadLastName ?? string.Empty,
            Cellphone = fam.DadCellphone ?? string.Empty,
            Email = fam.DadEmail ?? string.Empty
        };

        return new FamilyProfileResponse
        {
            Username = aspUser.UserName ?? string.Empty,
            Primary = primary,
            Secondary = secondary,
            Address = new AddressDto
            {
                StreetAddress = aspUser.StreetAddress ?? string.Empty,
                City = aspUser.City ?? string.Empty,
                State = aspUser.State ?? string.Empty,
                PostalCode = aspUser.PostalCode ?? string.Empty
            },
            Children = childDtos
        };
    }

    public async Task<FamilyRegistrationResponse> RegisterAsync(FamilyRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = "Username and password are required" };
        }
        if (request.Children == null || request.Children.Count == 0)
        {
            return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = "At least one child is required." };
        }

        // Check if user already exists (by username or email)
        var existingUser = await _userManager.FindByNameAsync(request.Username)
            ?? await _userManager.FindByEmailAsync(request.Primary.Email);

        if (existingUser != null)
        {
            // User exists - validate privilege separation policy
            var isValid = await _privilegeService.ValidatePrivilegeForRegistrationAsync(existingUser.Id, RoleConstants.Player);
            if (!isValid)
            {
                var existingPrivilege = await _privilegeService.GetUserPrivilegeLevelAsync(existingUser.Id);
                var privilegeName = PrivilegeNameMapper.GetPrivilegeName(existingPrivilege);
                return new FamilyRegistrationResponse
                {
                    Success = false,
                    FamilyUserId = null,
                    FamilyId = null,
                    Message = $"This account is locked to {privilegeName} privilege level. To protect player data, one account can only be used for one privilege level. Please use a different email address and username for Player registration."
                };
            }

            // Verify password for existing user
            var passwordValid = await _userManager.CheckPasswordAsync(existingUser, request.Password);
            if (!passwordValid)
            {
                return new FamilyRegistrationResponse
                {
                    Success = false,
                    FamilyUserId = null,
                    FamilyId = null,
                    Message = "Invalid password for existing account."
                };
            }
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        ApplicationUser user;
        bool isNewUser = existingUser == null;

        if (isNewUser)
        {
            user = new ApplicationUser
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
                return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = msg };
            }
        }
        else
        {
            // Use existing validated user
            user = existingUser;
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
        _familiesRepo.Add(fam);
        await _familiesRepo.SaveChangesAsync();

        // Create and link children
        foreach (var child in request.Children)
        {
            var (ok, error) = await CreateAndLinkChildAsync(child, fam.FamilyUserId);
            if (!ok)
            {
                return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = error };
            }
        }

        await _familyMemberRepo.SaveChangesAsync();
        scope.Complete();
        return new FamilyRegistrationResponse { Success = true, FamilyUserId = user.Id, FamilyId = Guid.Empty, Message = null };
    }

    public async Task<FamilyRegistrationResponse> UpdateAsync(FamilyUpdateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = "Username is required" };
        }

        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null) return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = "User not found" };

        var appUser = await _userManager.FindByIdAsync(user.Id);
        if (appUser != null)
        {
            appUser.StreetAddress = request.Address.StreetAddress;
            appUser.City = request.Address.City;
            appUser.State = request.Address.State;
            appUser.PostalCode = request.Address.PostalCode;
            appUser.Cellphone = request.Primary.Cellphone;
            appUser.Phone = request.Primary.Cellphone;
            if (string.IsNullOrWhiteSpace(appUser.LebUserId)) appUser.LebUserId = TsicConstants.SuperUserId;
            await _userManager.UpdateAsync(appUser);
        }

        var fam = await _familiesRepo.GetByFamilyUserIdAsync(user.Id);
        if (fam == null) return new FamilyRegistrationResponse { Success = false, FamilyUserId = null, FamilyId = null, Message = "Family record not found" };

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
        _familiesRepo.Update(fam);
        await _familiesRepo.SaveChangesAsync();

        // Simplify: controller had complex child sync logic. Preserve existing children unchanged for now.
        // Future: extract full child synchronization rules into this service if still required.

        scope.Complete();
        return new FamilyRegistrationResponse { Success = true, FamilyUserId = user.Id, FamilyId = Guid.Empty, Message = null };
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
        _familyMemberRepo.Add(fm);
        return (true, null);
    }

    private async Task<(bool HasActiveDiscountCodes, bool UsesAmex)> GetJobPaymentFeaturesAsync(Guid? jobId)
    {
        if (jobId == null) return (false, false);

        var now = DateTime.UtcNow;
        var hasActiveDiscountCodes = await _jobDiscountRepo.Query()
            .AsNoTracking()
            .AnyAsync(dc => dc.JobId == jobId && dc.Active && dc.CodeStartDate <= now && dc.CodeEndDate >= now);

        var jobMeta = await _jobRepo.Query().AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.CustomerId })
            .FirstOrDefaultAsync();

        var usesAmex = false;
        if (jobMeta != null)
        {
            try
            {
                var amexIds = _config.GetSection("PaymentMethods_NonMCVisa_ClientIds:Amex").Get<string[]>() ?? Array.Empty<string>();
                var cust = jobMeta.CustomerId.ToString();
                usesAmex = Array.Exists(amexIds, id => string.Equals(id, cust, StringComparison.OrdinalIgnoreCase));
            }
            catch { usesAmex = false; }
        }

        return (hasActiveDiscountCodes, usesAmex);
    }

    private static FamilyUserSummaryDto BuildFamilyUserSummary(string familyUserId, TSIC.Domain.Entities.Families? fam, TSIC.Domain.Entities.AspNetUsers? asp)
    {
        string display;
        if (!string.IsNullOrWhiteSpace(fam?.MomFirstName) || !string.IsNullOrWhiteSpace(fam?.MomLastName))
            display = $"{fam?.MomFirstName} {fam?.MomLastName}".Trim();
        else if (!string.IsNullOrWhiteSpace(fam?.DadFirstName) || !string.IsNullOrWhiteSpace(fam?.DadLastName))
            display = $"{fam?.DadFirstName} {fam?.DadLastName}".Trim();
        else if (!string.IsNullOrWhiteSpace(asp?.FirstName) || !string.IsNullOrWhiteSpace(asp?.LastName))
            display = $"{asp?.FirstName} {asp?.LastName}".Trim();
        else
            display = asp?.UserName ?? "Family";

        return new FamilyUserSummaryDto
        {
            FamilyUserId = familyUserId,
            DisplayName = display,
            UserName = asp?.UserName ?? string.Empty
        };
    }

    private static CcInfoDto BuildCreditCardInfo(TSIC.Domain.Entities.Families? fam, TSIC.Domain.Entities.AspNetUsers? asp)
    {
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
        var ccEmail = !string.IsNullOrWhiteSpace(fam?.MomEmail) ? fam!.MomEmail!.Trim() : asp?.Email?.Trim();
        var ccPhone = !string.IsNullOrWhiteSpace(fam?.MomCellphone) ? fam!.MomCellphone!.Trim() : (asp?.Cellphone?.Trim() ?? asp?.Phone?.Trim());

        return new CcInfoDto
        {
            FirstName = ccFirst,
            LastName = ccLast,
            StreetAddress = ccStreet,
            Zip = ccZip,
            Email = ccEmail,
            Phone = ccPhone
        };
    }

    private static FamilyPlayerRegistrationDto BuildRegistrationDto(
        TSIC.Domain.Entities.Registrations r,
        List<(string Name, string DbColumn)> mappedFields,
        HashSet<string> visibleFieldNames,
        Dictionary<Guid, string> teamNameMap)
    {
        var fv = BuildFormValuesDictionary(r, mappedFields);
        var formFieldValues = BuildVisibleFieldValues(fv, visibleFieldNames);

        return new FamilyPlayerRegistrationDto
        {
            RegistrationId = r.RegistrationId,
            Active = r.BActive == true,
            Financials = new RegistrationFinancialsDto
            {
                FeeBase = r.FeeBase,
                FeeProcessing = r.FeeProcessing,
                FeeDiscount = r.FeeDiscount,
                FeeDonation = r.FeeDonation,
                FeeLateFee = r.FeeLatefee,
                FeeTotal = r.FeeTotal,
                OwedTotal = r.OwedTotal,
                PaidTotal = r.PaidTotal
            },
            AssignedTeamId = r.AssignedTeamId,
            AssignedTeamName = r.AssignedTeamId.HasValue && teamNameMap.ContainsKey(r.AssignedTeamId.Value)
                ? teamNameMap[r.AssignedTeamId.Value]
                : null,
            AdnSubscriptionId = r.AdnSubscriptionId,
            AdnSubscriptionStatus = r.AdnSubscriptionStatus,
            AdnSubscriptionAmountPerOccurence = r.AdnSubscriptionAmountPerOccurence,
            AdnSubscriptionBillingOccurences = r.AdnSubscriptionBillingOccurences.HasValue
                ? (short?)r.AdnSubscriptionBillingOccurences.Value
                : null,
            AdnSubscriptionIntervalLength = r.AdnSubscriptionIntervalLength.HasValue
                ? (short?)r.AdnSubscriptionIntervalLength.Value
                : null,
            AdnSubscriptionStartDate = r.AdnSubscriptionStartDate,
            FormFieldValues = formFieldValues
        };
    }

    private async Task<List<TSIC.Domain.Entities.Registrations>> LoadRegistrationsForJobAsync(
        Guid? jobId,
        string familyUserId,
        List<string> linkedChildIds)
    {
        if (jobId == null)
            return new List<TSIC.Domain.Entities.Registrations>();

        return await _registrationRepo.GetFamilyRegistrationsForPlayersAsync(jobId.Value, familyUserId, linkedChildIds);
    }

    private async Task<(string? metadataJson, string? rawJsonOptions)> GetJobMetadataAndOptionsAsync(Guid? jobId)
    {
        if (jobId == null)
            return (null, null);

        var jm = await _jobRepo.GetJobMetadataAsync(jobId.Value);
        return (jm?.PlayerProfileMetadataJson, jm?.JsonOptions);
    }

    private async Task<Dictionary<Guid, string>> BuildTeamNameMapAsync(
        Guid? jobId,
        List<TSIC.Domain.Entities.Registrations> regsRaw)
    {
        var teamNameMap = new Dictionary<Guid, string>();
        if (jobId != null)
        {
            var teamIds = regsRaw.Where(x => x.AssignedTeamId.HasValue).Select(x => x.AssignedTeamId!.Value).Distinct().ToList();
            if (teamIds.Count > 0)
            {
                var teams = await _teamRepo.GetTeamsForJobAsync(jobId.Value, teamIds);
                teamNameMap = teams.ToDictionary(t => t.TeamId, t => t.TeamName ?? string.Empty);
            }
        }
        return teamNameMap;
    }

    private async Task<Dictionary<string, List<TSIC.Domain.Entities.Registrations>>> LoadAllRegistrationsByUserAsync(List<string> linkedChildIds)
    {
        var allRegsForChildren = await _registrationRepo.Query()
            .AsNoTracking()
            .Where(r => r.UserId != null && linkedChildIds.Contains(r.UserId))
            .OrderByDescending(r => r.Modified)
            .ThenByDescending(r => r.RegistrationTs)
            .ToListAsync();

        return allRegsForChildren
            .GroupBy(r => r.UserId!)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }

    private async Task<RegSaverDetailsDto?> ExtractRegSaverDetailsAsync(Guid? jobId, string familyUserId)
    {
        if (jobId == null)
            return null;

        var regSaverRaw = await _registrationRepo.Query()
            .AsNoTracking()
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && r.RegsaverPolicyId != null)
            .OrderByDescending(r => r.BActive == true)
            .ThenByDescending(r => r.RegsaverPolicyIdCreateDate)
            .Select(r => new { r.RegsaverPolicyId, r.RegsaverPolicyIdCreateDate })
            .FirstOrDefaultAsync();

        if (regSaverRaw != null && !string.IsNullOrWhiteSpace(regSaverRaw.RegsaverPolicyId) && regSaverRaw.RegsaverPolicyIdCreateDate.HasValue)
        {
            return new RegSaverDetailsDto { PolicyNumber = regSaverRaw.RegsaverPolicyId, PolicyCreateDate = regSaverRaw.RegsaverPolicyIdCreateDate.Value };
        }
        return null;
    }

    private static List<FamilyPlayerDto> BuildPlayerDtos(
        List<TSIC.Domain.Entities.AspNetUsers> children,
        Dictionary<string, List<FamilyPlayerRegistrationDto>> regsByUser,
        HashSet<string> regSet,
        Dictionary<string, List<TSIC.Domain.Entities.Registrations>> allRegsByUser,
        Guid? jobId,
        List<(string Name, string DbColumn)> mappedFields,
        HashSet<string> visibleFieldNames)
    {
        return children.Select(c =>
        {
            var prior = regsByUser.TryGetValue(c.Id, out var list) ? (IReadOnlyList<FamilyPlayerRegistrationDto>)list : Array.Empty<FamilyPlayerRegistrationDto>();
            var registered = regSet.Contains(c.Id);
            IReadOnlyDictionary<string, JsonElement>? defaults = null;
            if (!registered && jobId != null && mappedFields.Count > 0 && visibleFieldNames.Count > 0)
            {
                if (allRegsByUser.TryGetValue(c.Id, out var history) && history.Count > 0)
                {
                    defaults = BuildLatestVisibleFieldValues(history, mappedFields, visibleFieldNames);
                }
                else
                {
                    defaults = new Dictionary<string, JsonElement>();
                }
            }
            return new FamilyPlayerDto
            {
                PlayerId = c.Id,
                FirstName = c.FirstName ?? string.Empty,
                LastName = c.LastName ?? string.Empty,
                Gender = c.Gender ?? string.Empty,
                Dob = c.Dob.HasValue ? c.Dob.Value.ToString(DateFormat) : null,
                Registered = registered,
                Selected = registered,
                PriorRegistrations = prior,
                DefaultFieldValues = defaults
            };
        })
        .OrderBy(p => p.LastName)
        .ThenBy(p => p.FirstName)
        .ToList();
    }

    private async Task<JobRegFormDto?> BuildJobRegFormDtoAsync(
        Guid? jobId,
        string? metadataJson,
        string? rawJsonOptions,
        List<TSIC.Contracts.Dtos.ProfileMetadataField> typedFields,
        List<string> waiverFieldNames)
    {
        string? constraintType = null;
        if (!string.IsNullOrWhiteSpace(metadataJson) && jobId != null)
        {
            var meta = await _jobRepo.GetJobMetadataAsync(jobId.Value);
            string? coreProfile = meta?.CoreRegformPlayer;
            if (string.IsNullOrWhiteSpace(rawJsonOptions)) rawJsonOptions = meta?.JsonOptions;

            constraintType = ExtractConstraintType(coreProfile);

            var versionSeed = $"{jobId}-{metadataJson?.Length ?? 0}-{rawJsonOptions?.Length ?? 0}-{typedFields.Count}";
            var version = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(versionSeed))).Substring(0, 16);

            return new JobRegFormDto
            {
                Version = version,
                CoreProfileName = coreProfile,
                Fields = typedFields.Select(tf => new JobRegFieldDto
                {
                    Name = tf.Name,
                    DbColumn = tf.DbColumn,
                    DisplayName = string.IsNullOrWhiteSpace(tf.DisplayName) ? tf.Name : tf.DisplayName,
                    InputType = string.IsNullOrWhiteSpace(tf.InputType) ? "TEXT" : tf.InputType,
                    DataSource = tf.DataSource,
                    Options = tf.Options,
                    Validation = tf.Validation,
                    Order = tf.Order,
                    Visibility = string.IsNullOrWhiteSpace(tf.Visibility) ? "public" : tf.Visibility,
                    Computed = tf.Computed,
                    ConditionalOn = tf.ConditionalOn
                }).ToList(),
                WaiverFieldNames = waiverFieldNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ConstraintType = constraintType
            };
        }
        return null;
    }

    private static string? ExtractConstraintType(string? coreProfile)
    {
        if (string.IsNullOrWhiteSpace(coreProfile) || coreProfile == "0" || coreProfile == "1")
            return null;

        var parts = coreProfile.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var p in parts)
        {
            var up = p.ToUpperInvariant();
            if (up is "BYGRADYEAR" or "BYAGEGROUP" or "BYAGERANGE" or "BYCLUBNAME")
            {
                return up;
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, JsonElement> BuildVisibleFieldValues(
        IReadOnlyDictionary<string, JsonElement> allFieldValues,
        HashSet<string> visibleFieldNames)
    {
        if (visibleFieldNames.Count == 0)
            return new Dictionary<string, JsonElement>();

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var name in visibleFieldNames)
        {
            if (allFieldValues.TryGetValue(name, out var found))
            {
                var clone = JsonDocument.Parse(found.GetRawText()).RootElement.Clone();
                dict[name] = clone;
            }
        }
        return dict;
    }

}


