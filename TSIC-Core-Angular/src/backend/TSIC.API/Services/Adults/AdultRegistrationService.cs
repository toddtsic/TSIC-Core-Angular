using Microsoft.AspNetCore.Identity;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared.Utilities;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.AdultRegistration;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Adults;

public class AdultRegistrationService : IAdultRegistrationService
{
    private readonly IAdultRegistrationRepository _repo;
    private readonly IProfileMetadataService _metadataService;
    private readonly IEmailService _emailService;
    private readonly UserManager<ApplicationUser> _userManager;

    public AdultRegistrationService(
        IAdultRegistrationRepository repo,
        IProfileMetadataService metadataService,
        IEmailService emailService,
        UserManager<ApplicationUser> userManager)
    {
        _repo = repo;
        _metadataService = metadataService;
        _emailService = emailService;
        _userManager = userManager;
    }

    public async Task<AdultRegJobInfoResponse> GetJobInfoByPathAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataByPathAsync(jobPath, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found for path '{jobPath}'.");

        var roles = new List<AdultRoleOption>
        {
            new()
            {
                RoleType = AdultRoleType.UnassignedAdult,
                DisplayName = "Coach / Volunteer",
                Description = "Register as an unassigned adult. A director will assign you to a team."
            },
            new()
            {
                RoleType = AdultRoleType.Referee,
                DisplayName = "Referee",
                Description = "Register as a referee for this event."
            },
            new()
            {
                RoleType = AdultRoleType.Recruiter,
                DisplayName = "College Recruiter",
                Description = "Register as a college recruiter to scout players."
            }
        };

        return new AdultRegJobInfoResponse
        {
            JobId = jobData.JobId,
            JobName = jobData.JobName,
            AvailableRoles = roles
        };
    }

    public async Task<AdultRegFormResponse> GetFormSchemaForRoleAsync(string jobPath, AdultRoleType roleType, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataByPathAsync(jobPath, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found for path '{jobPath}'.");

        var roleKey = GetRoleKey(roleType);
        var parsed = _metadataService.ParseForRole(jobData.AdultProfileMetadataJson, roleKey, jobData.JsonOptions);

        var fields = parsed.TypedFields.Select(tf => new JobRegFieldDto
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
            ConditionalOn = tf.ConditionalOn
        }).ToList();

        var waivers = BuildWaivers(jobData);

        return new AdultRegFormResponse
        {
            RoleType = roleType,
            Fields = fields,
            Waivers = waivers
        };
    }

    public async Task<AdultRegistrationResponse> RegisterNewUserAsync(string jobPath, AdultRegistrationRequest request, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataByPathAsync(jobPath, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found for path '{jobPath}'.");

        // Create ASP.NET Identity user
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            Cellphone = request.Phone.Trim(),
            Gender = "U",
            Dob = new DateTime(1980, 1, 1),
            LebUserId = TsicConstants.SuperUserId,
            Modified = DateTime.UtcNow
        };

        var identityResult = await _userManager.CreateAsync(user, request.Password);
        if (!identityResult.Succeeded)
        {
            var errorMessage = identityResult.Errors.First().Description;
            throw new InvalidOperationException(errorMessage);
        }

        var roleId = ResolveRoleId(request.RoleType);
        var registrationId = await CreateRegistrationAsync(jobData, user.Id, roleId, request.RoleType, request.FormValues, request.WaiverAcceptance, user.Id, cancellationToken);

        return new AdultRegistrationResponse
        {
            Success = true,
            RegistrationId = registrationId,
            Message = "Registration completed successfully."
        };
    }

    public async Task<AdultRegistrationResponse> RegisterExistingUserAsync(Guid jobId, string userId, AdultRegistrationExistingRequest request, string auditUserId, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found.");

        var roleId = ResolveRoleId(request.RoleType);

        // Check for duplicate registration
        var exists = await _repo.HasExistingRegistrationAsync(userId, jobId, roleId, cancellationToken);
        if (exists)
            throw new InvalidOperationException("You already have an active registration with this role for this event.");

        var registrationId = await CreateRegistrationAsync(jobData, userId, roleId, request.RoleType, request.FormValues, request.WaiverAcceptance, auditUserId, cancellationToken);

        return new AdultRegistrationResponse
        {
            Success = true,
            RegistrationId = registrationId,
            Message = "Registration completed successfully."
        };
    }

    public async Task<AdultConfirmationResponse> GetConfirmationAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var reg = await _repo.GetRegistrationWithJobAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration {registrationId} not found.");

        var roleType = ResolveRoleTypeFromId(reg.RoleId);
        var confirmationHtml = GetConfirmationOnScreen(reg.Job, roleType);
        var roleDisplayName = GetRoleDisplayName(roleType);

        return new AdultConfirmationResponse
        {
            RegistrationId = registrationId,
            ConfirmationHtml = confirmationHtml ?? $"<p>Thank you for registering as {roleDisplayName}.</p>",
            RoleDisplayName = roleDisplayName
        };
    }

    public async Task SendConfirmationEmailAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var reg = await _repo.GetRegistrationWithJobAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration {registrationId} not found.");

        var roleType = ResolveRoleTypeFromId(reg.RoleId);
        var emailHtml = GetConfirmationEmail(reg.Job, roleType);
        if (string.IsNullOrWhiteSpace(emailHtml)) return;

        var userEmail = reg.User?.Email;
        if (string.IsNullOrWhiteSpace(userEmail)) return;

        var roleDisplayName = GetRoleDisplayName(roleType);
        var message = new EmailMessageDto
        {
            Subject = $"{reg.Job.JobName} — {roleDisplayName} Registration Confirmation",
            HtmlBody = emailHtml,
            ToAddresses = { userEmail }
        };

        await _emailService.SendAsync(message, cancellationToken: cancellationToken);
    }

    // ============ Private Helpers ============

    private async Task<Guid> CreateRegistrationAsync(
        AdultRegJobData jobData,
        string userId,
        string roleId,
        AdultRoleType roleType,
        Dictionary<string, System.Text.Json.JsonElement>? formValues,
        Dictionary<string, bool>? waiverAcceptance,
        string auditUserId,
        CancellationToken cancellationToken)
    {
        var registration = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            UserId = userId,
            JobId = jobData.JobId,
            RoleId = roleId,
            BActive = true,
            AssignedTeamId = null,
            FamilyUserId = null,
            RegistrationFormName = null,
            RegistrationTs = DateTime.UtcNow,
            LebUserId = auditUserId,
            Modified = DateTime.UtcNow
        };

        // Apply dynamic form values via reflection
        if (formValues != null && formValues.Count > 0)
        {
            var roleKey = GetRoleKey(roleType);
            var nameToProperty = FormValueMapper.BuildFieldNameToPropertyMapForRole(jobData.AdultProfileMetadataJson, roleKey);
            var writableProps = FormValueMapper.BuildWritablePropertyMap();
            FormValueMapper.ApplyFormValues(registration, formValues, nameToProperty, writableProps);
        }

        // Apply waiver acceptance
        if (waiverAcceptance != null)
        {
            if (waiverAcceptance.TryGetValue("refundPolicy", out var w1) && w1)
                registration.BWaiverSigned1 = true;
            if (waiverAcceptance.TryGetValue("releaseOfLiability", out var w2) && w2)
                registration.BWaiverSigned2 = true;
            if (waiverAcceptance.TryGetValue("codeOfConduct", out var w3) && w3)
                registration.BWaiverSigned3 = true;
        }

        _repo.Add(registration);
        await _repo.SaveChangesAsync(cancellationToken);

        return registration.RegistrationId;
    }

    private static string ResolveRoleId(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => RoleConstants.UnassignedAdult,
        AdultRoleType.Referee => RoleConstants.Referee,
        AdultRoleType.Recruiter => RoleConstants.Recruiter,
        _ => throw new ArgumentOutOfRangeException(nameof(roleType), $"Unsupported role type: {roleType}")
    };

    private static AdultRoleType ResolveRoleTypeFromId(string? roleId) => roleId switch
    {
        RoleConstants.UnassignedAdult => AdultRoleType.UnassignedAdult,
        RoleConstants.Referee => AdultRoleType.Referee,
        RoleConstants.Recruiter => AdultRoleType.Recruiter,
        _ => AdultRoleType.UnassignedAdult
    };

    private static string GetRoleKey(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => "UnassignedAdult",
        AdultRoleType.Referee => "Referee",
        AdultRoleType.Recruiter => "Recruiter",
        _ => "UnassignedAdult"
    };

    private static string GetRoleDisplayName(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => "Coach / Volunteer",
        AdultRoleType.Referee => "Referee",
        AdultRoleType.Recruiter => "College Recruiter",
        _ => "Adult"
    };

    private static List<AdultWaiverDto> BuildWaivers(AdultRegJobData jobData)
    {
        var waivers = new List<AdultWaiverDto>();

        if (!string.IsNullOrWhiteSpace(jobData.AdultRegRefundPolicy))
        {
            waivers.Add(new AdultWaiverDto
            {
                Key = "refundPolicy",
                Title = "Refund Policy",
                HtmlContent = jobData.AdultRegRefundPolicy
            });
        }

        if (!string.IsNullOrWhiteSpace(jobData.AdultRegReleaseOfLiability))
        {
            waivers.Add(new AdultWaiverDto
            {
                Key = "releaseOfLiability",
                Title = "Release of Liability",
                HtmlContent = jobData.AdultRegReleaseOfLiability
            });
        }

        if (!string.IsNullOrWhiteSpace(jobData.AdultRegCodeOfConduct))
        {
            waivers.Add(new AdultWaiverDto
            {
                Key = "codeOfConduct",
                Title = "Code of Conduct",
                HtmlContent = jobData.AdultRegCodeOfConduct
            });
        }

        return waivers;
    }

    private static string? GetConfirmationOnScreen(Jobs job, AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => job.AdultRegConfirmationOnScreen,
        AdultRoleType.Referee => job.RefereeRegConfirmationOnScreen ?? job.AdultRegConfirmationOnScreen,
        AdultRoleType.Recruiter => job.RecruiterRegConfirmationOnScreen ?? job.AdultRegConfirmationOnScreen,
        _ => job.AdultRegConfirmationOnScreen
    };

    private static string? GetConfirmationEmail(Jobs job, AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => job.AdultRegConfirmationEmail,
        AdultRoleType.Referee => job.RefereeRegConfirmationEmail ?? job.AdultRegConfirmationEmail,
        AdultRoleType.Recruiter => job.RecruiterRegConfirmationEmail ?? job.AdultRegConfirmationEmail,
        _ => job.AdultRegConfirmationEmail
    };
}
