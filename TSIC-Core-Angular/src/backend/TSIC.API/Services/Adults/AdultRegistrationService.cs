using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.TextSubstitution;
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
    private readonly IFeeResolutionService _feeService;
    private readonly IAdnApiService _adnApiService;
    private readonly IRegistrationAccountingRepository _acctRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IUserRepository _userRepo;
    private readonly ITextSubstitutionService _textSub;

    private static readonly Guid CreditCardPaymentMethodId =
        Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

    public AdultRegistrationService(
        IAdultRegistrationRepository repo,
        IProfileMetadataService metadataService,
        IEmailService emailService,
        UserManager<ApplicationUser> userManager,
        IFeeResolutionService feeService,
        IAdnApiService adnApiService,
        IRegistrationAccountingRepository acctRepo,
        ITeamRepository teamRepo,
        IUserRepository userRepo,
        ITextSubstitutionService textSub)
    {
        _repo = repo;
        _metadataService = metadataService;
        _emailService = emailService;
        _userManager = userManager;
        _feeService = feeService;
        _adnApiService = adnApiService;
        _acctRepo = acctRepo;
        _teamRepo = teamRepo;
        _userRepo = userRepo;
        _textSub = textSub;
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

        List<JobRegFieldDto> fields;

        if (!string.IsNullOrWhiteSpace(jobData.AdultProfileMetadataJson))
        {
            var roleKey = GetRoleKey(roleType);
            var parsed = _metadataService.ParseForRole(jobData.AdultProfileMetadataJson, roleKey, jobData.JsonOptions);

            fields = parsed.TypedFields.Select(tf => new JobRegFieldDto
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
        }
        else
        {
            fields = [];
        }

        // Fallback: if no metadata fields configured, provide SpecialRequests per role
        if (fields.Count == 0)
        {
            fields = BuildFallbackFields(roleType);
        }

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

        // ToS must be accepted
        if (!request.AcceptedTos)
        {
            throw new InvalidOperationException("You must accept the Terms of Service.");
        }

        // Phone: digits only (legacy RegularExpression @"^\d+$")
        var phoneDigits = request.Phone?.Where(char.IsDigit).ToArray() ?? [];
        if (phoneDigits.Length < 10)
        {
            throw new InvalidOperationException("Cell phone must be at least 10 digits.");
        }

        // Create ASP.NET Identity user with FULL profile (matches legacy StaffRegister_ViewModel)
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = request.Email.Trim(),
            Cellphone = new string(phoneDigits),
            Gender = request.Gender.Trim(),
            StreetAddress = request.StreetAddress.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim(),
            PostalCode = request.PostalCode.Trim(),
            Dob = new DateTime(1980, 1, 1),  // Legacy default — adults have no DOB field
            LebUserId = TsicConstants.SuperUserId,
            Modified = DateTime.UtcNow
        };

        // Resolve role server-side (security model gate). This validates roleKey,
        // checks job type, enforces the BAllowRosterViewAdult invariant for Tournament.
        var resolution = ResolveAdultRole(jobData, request.RoleKey);
        var roleId = resolution.RoleId;
        var roleType = ResolveRoleTypeFromId(roleId);

        // Team selection is required when the resolver says so (coach in tournament).
        if (resolution.NeedsTeamSelection && (request.TeamIdsCoaching == null || request.TeamIdsCoaching.Count == 0))
        {
            throw new InvalidOperationException("You must select at least one team to coach.");
        }

        var identityResult = await _userManager.CreateAsync(user, request.Password);
        if (!identityResult.Succeeded)
        {
            var errorMessage = identityResult.Errors.First().Description;
            throw new InvalidOperationException(errorMessage);
        }

        // Persist ToS acceptance to the user record (BTsicwaiverSigned + timestamp).
        // This is the same field AuthController.AcceptTos writes, so a returning
        // signed-in user won't be prompted again by any other flow that checks it.
        await _userRepo.UpdateTosAcceptanceByUserIdAsync(user.Id, cancellationToken);

        // Build N registrations (Staff: one per team; otherwise single).
        var registrations = await BuildAndAddRegistrationsAsync(
            jobData, user.Id, roleId, roleType,
            request.FormValues, request.WaiverAcceptance,
            request.TeamIdsCoaching,
            user.Id, cancellationToken);

        // Stamp fees per-registration (Staff: per-team cascade).
        var feeCtx = new FeeApplicationContext { AddProcessingFees = jobData.BAddProcessingFees };
        await ApplyFeesToRegistrationsAsync(jobData.JobId, roleId, roleType, registrations, feeCtx, cancellationToken);

        // Single SaveChanges: inserts all registrations with fees stamped.
        await _repo.SaveChangesAsync(cancellationToken);

        var primary = registrations[0];
        var owedSum = registrations.Sum(r => r.OwedTotal);

        // Payment — charges the whole group as one transaction.
        if (owedSum > 0m)
        {
            if (request.CreditCard != null && request.PaymentMethod == "CC")
            {
                var paymentResult = await ProcessPaymentAsync(
                    primary.RegistrationId, user.Id,
                    new AdultPaymentRequestDto
                    {
                        RegistrationId = primary.RegistrationId,
                        CreditCard = request.CreditCard,
                        PaymentMethod = "CC"
                    },
                    cancellationToken);

                if (!paymentResult.Success)
                {
                    return new AdultRegistrationResponse
                    {
                        Success = true,
                        RegistrationId = primary.RegistrationId,
                        Message = $"Registration created but payment failed: {paymentResult.Message}"
                    };
                }
            }
            else if (request.PaymentMethod == "Check")
            {
                foreach (var r in registrations) r.PaymentMethodChosen = 3; // Check
                await _repo.SaveChangesAsync(cancellationToken);
            }
        }

        return new AdultRegistrationResponse
        {
            Success = true,
            RegistrationId = primary.RegistrationId,
            Message = "Registration completed successfully."
        };
    }

    public async Task<AdultRegistrationResponse> RegisterExistingUserAsync(Guid jobId, string userId, AdultRegistrationExistingRequest request, string auditUserId, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found.");

        // Resolve role server-side (security model gate).
        var resolution = ResolveAdultRole(jobData, request.RoleKey);
        var roleId = resolution.RoleId;
        var roleType = ResolveRoleTypeFromId(roleId);

        if (resolution.NeedsTeamSelection && (request.TeamIdsCoaching == null || request.TeamIdsCoaching.Count == 0))
        {
            throw new InvalidOperationException("You must select at least one team to coach.");
        }

        // Edit semantics: soft-delete any existing active registrations for
        // (user, job, role) before creating fresh rows. Matches legacy
        // EditLoggedInStaff pattern so returning users can add/remove teams.
        await _repo.DeactivateActiveByRoleAsync(userId, jobId, roleId, cancellationToken);

        var registrations = await BuildAndAddRegistrationsAsync(
            jobData, userId, roleId, roleType,
            request.FormValues, request.WaiverAcceptance,
            request.TeamIdsCoaching,
            auditUserId, cancellationToken);

        await _repo.SaveChangesAsync(cancellationToken);

        return new AdultRegistrationResponse
        {
            Success = true,
            RegistrationId = registrations[0].RegistrationId,
            Message = "Registration completed successfully."
        };
    }

    public async Task<List<AdultTeamOptionDto>> GetAvailableTeamsAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataByPathAsync(jobPath, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found for path '{jobPath}'.");

        return await _repo.GetAvailableTeamsAsync(jobData.JobId, cancellationToken);
    }

    public async Task<AdultExistingRegistrationDto> GetMyExistingRegistrationAsync(
        string jobPath, string roleKey, string userId, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataByPathAsync(jobPath, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found for path '{jobPath}'.");

        // Resolve role per the same security model used elsewhere.
        var resolution = ResolveAdultRole(jobData, roleKey);
        var roleType = ResolveRoleTypeFromId(resolution.RoleId);

        // All active rows for (user, job, role). Staff → N rows; others → 0 or 1.
        var regs = await _repo.GetTrackedActiveByRoleAsync(userId, jobData.JobId, resolution.RoleId, cancellationToken);

        if (regs.Count == 0)
        {
            return new AdultExistingRegistrationDto
            {
                HasExisting = false,
                RegistrationIds = [],
                TeamIds = [],
                FormValues = null,
                WaiverAcceptance = null,
            };
        }

        var first = regs[0];
        var teamIds = regs
            .Where(r => r.AssignedTeamId.HasValue)
            .Select(r => r.AssignedTeamId!.Value)
            .Distinct()
            .ToList();

        // Reverse-map stored columns back to form field values. We look up the
        // role's metadata (or fallback) schema, then pull each field's value
        // from the appropriate property on the first registration.
        var formValues = new Dictionary<string, JsonElement>();
        var schemaFields = !string.IsNullOrWhiteSpace(jobData.AdultProfileMetadataJson)
            ? _metadataService.ParseForRole(jobData.AdultProfileMetadataJson, GetRoleKey(roleType), jobData.JsonOptions).TypedFields
            : [];

        // SpecialRequests column — used both by the fallback SpecialRequests field
        // and by any metadata field with DbColumn=SpecialRequests.
        if (!string.IsNullOrWhiteSpace(first.SpecialRequests))
        {
            // Find matching field name from schema (fallback "SpecialRequests" if none).
            var fieldName = "SpecialRequests";
            foreach (var f in schemaFields)
            {
                if (string.Equals(f.DbColumn, "SpecialRequests", StringComparison.OrdinalIgnoreCase))
                {
                    fieldName = f.Name;
                    break;
                }
            }
            formValues[fieldName] = JsonSerializer.SerializeToElement(first.SpecialRequests);
        }

        var waiverAcceptance = new Dictionary<string, bool>
        {
            ["refundPolicy"] = first.BWaiverSigned1,
            ["releaseOfLiability"] = first.BWaiverSigned2,
            ["codeOfConduct"] = first.BWaiverSigned3,
        };

        return new AdultExistingRegistrationDto
        {
            HasExisting = true,
            RegistrationIds = regs.Select(r => r.RegistrationId).ToList(),
            TeamIds = teamIds,
            FormValues = formValues.Count > 0 ? formValues : null,
            WaiverAcceptance = waiverAcceptance,
        };
    }

    public async Task<AdultRoleConfigDto> GetRoleConfigAsync(string jobPath, string roleKey, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataByPathAsync(jobPath, cancellationToken)
            ?? throw new KeyNotFoundException($"Job not found for path '{jobPath}'.");

        // Security gate — throws InvalidOperationException if invariant violated.
        var resolution = ResolveAdultRole(jobData, roleKey);
        var roleType = ResolveRoleTypeFromId(resolution.RoleId);

        // Profile fields: metadata-configured, or fallback per role.
        List<JobRegFieldDto> fields;
        if (!string.IsNullOrWhiteSpace(jobData.AdultProfileMetadataJson))
        {
            var metaKey = GetRoleKey(roleType);
            var parsed = _metadataService.ParseForRole(jobData.AdultProfileMetadataJson, metaKey, jobData.JsonOptions);

            fields = parsed.TypedFields.Select(tf => new JobRegFieldDto
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
        }
        else
        {
            fields = [];
        }

        if (fields.Count == 0)
        {
            fields = BuildFallbackFields(roleType);
        }

        return new AdultRoleConfigDto
        {
            RoleKey = roleKey.Trim().ToLowerInvariant(),
            DisplayName = resolution.DisplayName,
            Description = resolution.Description,
            Icon = resolution.Icon,
            NeedsTeamSelection = resolution.NeedsTeamSelection,
            ProfileFields = fields,
            Waivers = BuildWaivers(jobData),
        };
    }

    public async Task<AdultConfirmationResponse> GetConfirmationAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        var reg = await _repo.GetRegistrationWithJobAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Registration {registrationId} not found.");

        var roleType = ResolveRoleTypeFromId(reg.RoleId);
        var template = GetConfirmationOnScreen(reg.Job, roleType);
        var roleDisplayName = GetRoleDisplayName(roleType);

        // Run the template through the shared token substitution service so
        // tokens like !JOBNAME, !F-TEAMS, !F-ACCOUNTING, !J-CONTACTBLOCK, etc.
        // are resolved to real content (same pattern as team + player wizards).
        var confirmationHtml = await SubstituteConfirmationAsync(reg, template);

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
        var template = GetConfirmationEmail(reg.Job, roleType);
        if (string.IsNullOrWhiteSpace(template)) return;

        var userEmail = reg.User?.Email;
        if (string.IsNullOrWhiteSpace(userEmail)) return;

        // Resolve tokens before sending.
        var emailHtml = await SubstituteConfirmationAsync(reg, template);
        if (string.IsNullOrWhiteSpace(emailHtml)) return;

        var roleDisplayName = GetRoleDisplayName(roleType);
        var message = new EmailMessageDto
        {
            Subject = $"{reg.Job.JobName} — {roleDisplayName} Registration Confirmation",
            HtmlBody = emailHtml,
            ToAddresses = { userEmail }
        };

        await _emailService.SendAsync(message, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Run an adult-registration confirmation template through the shared
    /// text-substitution service so tokens resolve to real content.
    /// </summary>
    private async Task<string?> SubstituteConfirmationAsync(Registrations reg, string? template)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;
        try
        {
            // Adult Staff registrations are not club-rep rows, so the shared
            // !F-TEAMS resolver (which joins Teams by ClubrepRegistrationid)
            // returns empty. Pre-render the teams list for Staff and inline it
            // before handing off to the shared substitution service.
            if (reg.RoleId == RoleConstants.Staff && template.Contains("!F-TEAMS", StringComparison.Ordinal))
            {
                var staffTeamsHtml = await BuildStaffTeamsHtmlAsync(reg, CancellationToken.None);
                template = template.Replace("!F-TEAMS", staffTeamsHtml, StringComparison.Ordinal);
            }

            return await _textSub.SubstituteAsync(
                jobSegment: reg.Job?.JobPath ?? string.Empty,
                jobId: reg.JobId,
                paymentMethodCreditCardId: CreditCardPaymentMethodId,
                registrationId: reg.RegistrationId,
                familyUserId: string.Empty,
                template: template);
        }
        catch
        {
            // Never fail the wizard if substitution errors; return the raw template.
            return template;
        }
    }

    /// <summary>
    /// Render the teams list for an adult Staff registration (all active Staff
    /// rows for this user+job, one per team). Replaces !F-TEAMS in confirmation
    /// templates since the shared resolver only handles club-rep rows.
    /// </summary>
    private async Task<string> BuildStaffTeamsHtmlAsync(Registrations reg, CancellationToken cancellationToken)
    {
        var staffRegs = await _repo.GetTrackedActiveByRoleAsync(reg.UserId!, reg.JobId, RoleConstants.Staff, cancellationToken);
        var teamIds = staffRegs
            .Where(r => r.AssignedTeamId.HasValue)
            .Select(r => r.AssignedTeamId!.Value)
            .ToHashSet();
        if (teamIds.Count == 0) return string.Empty;

        var options = await _repo.GetAvailableTeamsAsync(reg.JobId, cancellationToken);
        var selected = options.Where(o => teamIds.Contains(o.TeamId)).ToList();
        if (selected.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        sb.Append("<table style='border-collapse:collapse;margin:8px 0;'>");
        sb.Append("<thead><tr>")
          .Append("<th style='text-align:left;padding:4px 12px 4px 0;border-bottom:1px solid #ccc;'>Club</th>")
          .Append("<th style='text-align:left;padding:4px 12px 4px 0;border-bottom:1px solid #ccc;'>Age Group</th>")
          .Append("<th style='text-align:left;padding:4px 12px 4px 0;border-bottom:1px solid #ccc;'>Division</th>")
          .Append("<th style='text-align:left;padding:4px 0;border-bottom:1px solid #ccc;'>Team</th>")
          .Append("</tr></thead><tbody>");
        foreach (var t in selected)
        {
            sb.Append("<tr>")
              .Append($"<td style='padding:4px 12px 4px 0;'>{System.Net.WebUtility.HtmlEncode(t.ClubName)}</td>")
              .Append($"<td style='padding:4px 12px 4px 0;'>{System.Net.WebUtility.HtmlEncode(t.AgegroupName)}</td>")
              .Append($"<td style='padding:4px 12px 4px 0;'>{System.Net.WebUtility.HtmlEncode(t.DivName)}</td>")
              .Append($"<td style='padding:4px 0;'>{System.Net.WebUtility.HtmlEncode(t.TeamName)}</td>")
              .Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    // ============ PreSubmit + Payment ============

    public async Task<PreSubmitAdultRegResponseDto> PreSubmitAsync(Guid jobId, string? userId, PreSubmitAdultRegRequestDto request, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Job not found.");

        // Resolve role server-side (security gate).
        var resolution = ResolveAdultRole(jobData, request.RoleKey);
        var roleId = resolution.RoleId;
        var roleType = ResolveRoleTypeFromId(roleId);

        if (resolution.NeedsTeamSelection && (request.TeamIdsCoaching == null || request.TeamIdsCoaching.Count == 0))
        {
            throw new InvalidOperationException("You must select at least one team to coach.");
        }

        // Validate form fields against schema
        var validationErrors = ValidateFormFields(jobData, request, roleType);
        if (validationErrors.Count > 0)
        {
            return new PreSubmitAdultRegResponseDto
            {
                Valid = false,
                ValidationErrors = validationErrors,
                RegistrationId = null,
                Fees = new AdultFeeBreakdownDto
                {
                    FeeBase = 0m, FeeProcessing = 0m, FeeDiscount = 0m,
                    FeeLateFee = 0m, FeeTotal = 0m, OwedTotal = 0m
                }
            };
        }

        // Resolve fees
        var feeCtx = new FeeApplicationContext { AddProcessingFees = jobData.BAddProcessingFees };
        AdultFeeBreakdownDto fees;
        Guid? registrationId = null;

        if (userId != null)
        {
            // Login-mode: create/recreate the registrations NOW (user exists).
            //
            // Edit semantics (matches legacy EditLoggedInStaff pattern):
            // any existing active registrations for (user, job, role) are
            // soft-deleted (BActive=false) and new rows are created with the
            // current state data. This lets returning users add/remove teams
            // and update form values in one submit without accumulating stale rows,
            // and preserves audit history (rows aren't hard-deleted).
            await _repo.DeactivateActiveByRoleAsync(userId, jobId, roleId, cancellationToken);

            var registrations = await BuildAndAddRegistrationsAsync(
                jobData, userId, roleId, roleType,
                request.FormValues, request.WaiverAcceptance,
                request.TeamIdsCoaching,
                userId, cancellationToken);

            await ApplyFeesToRegistrationsAsync(jobId, roleId, roleType, registrations, feeCtx, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);

            fees = SummarizeFees(registrations);
            registrationId = registrations[0].RegistrationId;
        }
        else
        {
            // Create-mode: no user yet → fee preview only, never persisted.
            fees = await ComputeFeePreviewAsync(
                jobId, roleId, roleType,
                request.TeamIdsCoaching,
                feeCtx, cancellationToken);
        }

        return new PreSubmitAdultRegResponseDto
        {
            Valid = true,
            ValidationErrors = null,
            RegistrationId = registrationId,
            Fees = fees
        };
    }

    /// <summary>
    /// Fee preview for create-mode (no user exists yet, no registrations to stamp).
    /// Staff multi-team: sum per-team cascade fees. Others: single job-level fee.
    /// </summary>
    private async Task<AdultFeeBreakdownDto> ComputeFeePreviewAsync(
        Guid jobId, string roleId, AdultRoleType roleType,
        List<Guid>? teamIdsCoaching,
        FeeApplicationContext feeCtx,
        CancellationToken cancellationToken)
    {
        decimal baseTotal = 0m;

        if (roleType == AdultRoleType.Staff && teamIdsCoaching != null && teamIdsCoaching.Count > 0)
        {
            var teams = await _teamRepo.GetTeamsForJobAsync(jobId, teamIdsCoaching.Distinct().ToList(), cancellationToken);
            foreach (var team in teams)
            {
                var resolved = await _feeService.ResolveFeeAsync(jobId, RoleConstants.Staff, team.AgegroupId, team.TeamId, cancellationToken);
                baseTotal += resolved?.EffectiveBalanceDue ?? 0m;
            }
        }
        else
        {
            var resolved = await _feeService.ResolveJobLevelFeeAsync(jobId, roleId, cancellationToken);
            baseTotal = resolved?.EffectiveBalanceDue ?? 0m;
        }

        var rate = baseTotal > 0m && feeCtx.AddProcessingFees
            ? await _feeService.GetEffectiveProcessingRateAsync(jobId, cancellationToken)
            : 0m;
        var processing = baseTotal > 0m && feeCtx.AddProcessingFees
            ? Math.Round(baseTotal * rate, 2)
            : 0m;
        var total = baseTotal + processing;

        return new AdultFeeBreakdownDto
        {
            FeeBase = baseTotal,
            FeeProcessing = processing,
            FeeDiscount = 0m,
            FeeLateFee = 0m,
            FeeTotal = total,
            OwedTotal = total,
        };
    }

    public async Task<AdultPaymentResponseDto> ProcessPaymentAsync(Guid registrationId, string userId, AdultPaymentRequestDto request, CancellationToken cancellationToken = default)
    {
        var reg = await _repo.GetTrackedRegistrationAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException("Registration not found.");

        if (reg.UserId != userId)
            throw new UnauthorizedAccessException("Registration does not belong to this user.");

        // Staff registrations come as N rows per (user, job, role) — one per team.
        // Load all siblings and charge the whole set as one transaction.
        // Matches legacy StaffTournamentController pattern: no group id, the
        // (UserId, JobId, RoleId) tuple IS the grouping.
        var group = await _repo.GetTrackedActiveByRoleAsync(userId, reg.JobId, reg.RoleId!, cancellationToken);
        if (group.Count == 0) group = [reg];

        var totalOwed = group.Sum(r => r.OwedTotal);
        if (totalOwed <= 0m)
        {
            return new AdultPaymentResponseDto { Success = true, Message = "No payment required." };
        }

        if (request.PaymentMethod == "Check")
        {
            foreach (var r in group) r.PaymentMethodChosen = 3; // Check
            await _repo.SaveChangesAsync(cancellationToken);

            return new AdultPaymentResponseDto
            {
                Success = true,
                Message = "Registration recorded. Payment by check selected."
            };
        }

        // CC payment via Authorize.Net — one charge for the whole group total.
        if (request.CreditCard == null)
            throw new InvalidOperationException("Credit card information is required for CC payment.");

        var jobId = reg.JobId;
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
        {
            return new AdultPaymentResponseDto
            {
                Success = false,
                Message = "Payment gateway credentials not configured.",
                ErrorCode = "NO_CREDENTIALS"
            };
        }

        var env = _adnApiService.GetADNEnvironment();
        var invoiceNumber = $"{reg.Job.JobAi}_{reg.RegistrationId.ToString("N")[..8]}";
        if (invoiceNumber.Length > 20) invoiceNumber = invoiceNumber[..20];

        var ccExpiryDate = FormatExpiry(request.CreditCard.Expiry ?? "");
        var roleType = ResolveRoleTypeFromId(reg.RoleId);
        var description = group.Count > 1
            ? $"Adult Registration: {GetRoleDisplayName(roleType)} ({group.Count} teams)"
            : $"Adult Registration: {GetRoleDisplayName(roleType)}";

        var adnResponse = _adnApiService.ADN_Charge(new AdnChargeRequest
        {
            Env = env,
            LoginId = credentials.AdnLoginId!,
            TransactionKey = credentials.AdnTransactionKey!,
            CardNumber = request.CreditCard.Number!,
            CardCode = request.CreditCard.Code!,
            Expiry = ccExpiryDate,
            FirstName = request.CreditCard.FirstName!,
            LastName = request.CreditCard.LastName!,
            Address = request.CreditCard.Address!,
            Zip = request.CreditCard.Zip!,
            Email = request.CreditCard.Email!,
            Phone = request.CreditCard.Phone!,
            Amount = totalOwed,
            InvoiceNumber = invoiceNumber,
            Description = description
        });

        if (adnResponse?.messages?.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok
            && adnResponse.transactionResponse?.messages != null
            && !string.IsNullOrWhiteSpace(adnResponse.transactionResponse.transId))
        {
            var transId = adnResponse.transactionResponse.transId;
            var last4 = request.CreditCard.Number!.Length >= 4
                ? request.CreditCard.Number[^4..]
                : request.CreditCard.Number;

            // One accounting row per registration, Payamt = that registration's own OwedTotal.
            // This matches the player CAC pattern (one accounting row per registration
            // enables per-registration refunds later).
            var ccMethod = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
            foreach (var r in group)
            {
                var thisAmt = r.OwedTotal;
                if (thisAmt <= 0m) continue;

                _acctRepo.Add(new RegistrationAccounting
                {
                    RegistrationId = r.RegistrationId,
                    Payamt = thisAmt,
                    Dueamt = thisAmt,
                    Paymeth = $"paid by cc: {thisAmt:C} on {DateTime.Now:G} txID: {transId}",
                    PaymentMethodId = ccMethod,
                    Active = true,
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
                    LebUserId = userId,
                    AdnTransactionId = transId,
                    AdnInvoiceNo = invoiceNumber,
                    AdnCc4 = last4,
                    AdnCcexpDate = ccExpiryDate,
                    Comment = description
                });

                r.PaidTotal += thisAmt;
                r.OwedTotal = r.FeeTotal - r.PaidTotal;
                r.PaymentMethodChosen = 1; // CC
            }

            await _repo.SaveChangesAsync(cancellationToken);

            return new AdultPaymentResponseDto
            {
                Success = true,
                TransactionId = transId,
                Message = "Payment processed successfully."
            };
        }

        // Payment failed
        var errorText = adnResponse?.transactionResponse?.errors?.FirstOrDefault()?.errorText
            ?? "Payment processing failed. Please check your card details and try again.";
        var errorCode = adnResponse?.transactionResponse?.errors?.FirstOrDefault()?.errorCode;

        return new AdultPaymentResponseDto
        {
            Success = false,
            Message = errorText,
            ErrorCode = errorCode
        };
    }

    // ============ Private Helpers ============

    /// <summary>
    /// Builds Registrations entities (with form values + waivers) and adds them
    /// to the context. Does NOT call SaveChangesAsync — caller applies fees and
    /// persists in one round-trip.
    /// <para>
    /// Cardinality rule (matches legacy StaffTournamentController + player CAC):
    /// </para>
    /// <list type="bullet">
    /// <item>Staff (tournament coach): ONE Registration PER SELECTED TEAM.
    ///   Each row gets its own AssignedTeamId + team-level fee. Rows are
    ///   linked implicitly by <c>(UserId, JobId, RoleId)</c> — no group id,
    ///   matching legacy. Zero teams → throws.</item>
    /// <item>UnassignedAdult (Club/League coach): ONE Registration, no team assignment.</item>
    /// <item>Referee / Recruiter: ONE Registration, no team assignment.</item>
    /// </list>
    /// </summary>
    private async Task<List<Registrations>> BuildAndAddRegistrationsAsync(
        AdultRegJobData jobData,
        string userId,
        string roleId,
        AdultRoleType roleType,
        Dictionary<string, System.Text.Json.JsonElement>? formValues,
        Dictionary<string, bool>? waiverAcceptance,
        List<Guid>? teamIdsCoaching,
        string auditUserId,
        CancellationToken cancellationToken)
    {
        var registrations = new List<Registrations>();

        if (roleType == AdultRoleType.Staff)
        {
            if (teamIdsCoaching == null || teamIdsCoaching.Count == 0)
            {
                throw new InvalidOperationException("Staff registration requires at least one team.");
            }

            // Look up selected teams with their AgegroupId for per-team fee cascade.
            var teams = await _teamRepo.GetTeamsForJobAsync(jobData.JobId, teamIdsCoaching, cancellationToken);
            var teamById = teams.ToDictionary(t => t.TeamId);

            foreach (var tid in teamIdsCoaching.Distinct())
            {
                if (!teamById.TryGetValue(tid, out var team))
                {
                    throw new InvalidOperationException($"Team {tid} is not registered for this event.");
                }

                var reg = BuildRegistrationEntity(
                    jobData, userId, roleId, roleType,
                    formValues, waiverAcceptance,
                    assignedTeamId: tid,
                    auditUserId);
                _repo.Add(reg);
                registrations.Add(reg);
            }
        }
        else
        {
            // Single registration — no team assignment (UA, Referee, Recruiter).
            var reg = BuildRegistrationEntity(
                jobData, userId, roleId, roleType,
                formValues, waiverAcceptance,
                assignedTeamId: null,
                auditUserId);
            _repo.Add(reg);
            registrations.Add(reg);
        }

        return registrations;
    }

    private Registrations BuildRegistrationEntity(
        AdultRegJobData jobData,
        string userId,
        string roleId,
        AdultRoleType roleType,
        Dictionary<string, System.Text.Json.JsonElement>? formValues,
        Dictionary<string, bool>? waiverAcceptance,
        Guid? assignedTeamId,
        string auditUserId)
    {
        var registration = new Registrations
        {
            RegistrationId = Guid.NewGuid(),
            UserId = userId,
            JobId = jobData.JobId,
            RoleId = roleId,
            BActive = true,
            AssignedTeamId = assignedTeamId,
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

        return registration;
    }

    /// <summary>
    /// Apply fees across a set of registrations. For Staff (multi-team group), each
    /// row gets its own per-team fee cascade; totals are independent per row. For
    /// UA/Ref/Recruiter, single row gets the job-level fee.
    /// </summary>
    private async Task ApplyFeesToRegistrationsAsync(
        Guid jobId,
        string roleId,
        AdultRoleType roleType,
        List<Registrations> registrations,
        FeeApplicationContext feeCtx,
        CancellationToken cancellationToken)
    {
        if (roleType == AdultRoleType.Staff)
        {
            // Per-team cascade for each registration.
            var teamIds = registrations
                .Where(r => r.AssignedTeamId.HasValue)
                .Select(r => r.AssignedTeamId!.Value)
                .ToList();
            var teams = await _teamRepo.GetTeamsForJobAsync(jobId, teamIds, cancellationToken);
            var teamById = teams.ToDictionary(t => t.TeamId);

            foreach (var reg in registrations)
            {
                if (!reg.AssignedTeamId.HasValue) continue;
                if (!teamById.TryGetValue(reg.AssignedTeamId.Value, out var team)) continue;
                await _feeService.ApplyNewStaffRegistrationFeesAsync(
                    reg, jobId, team.AgegroupId, reg.AssignedTeamId.Value,
                    feeCtx, cancellationToken);
            }
        }
        else
        {
            foreach (var reg in registrations)
            {
                await _feeService.ApplyNewAdultRegistrationFeesAsync(
                    reg, jobId, roleId, feeCtx, cancellationToken);
            }
        }
    }

    /// <summary>Sum OwedTotal across a list of registrations.</summary>
    private static AdultFeeBreakdownDto SummarizeFees(List<Registrations> registrations)
    {
        decimal baseTotal = 0, processingTotal = 0, discountTotal = 0, lateTotal = 0, feeTotal = 0, owedTotal = 0;
        foreach (var r in registrations)
        {
            baseTotal       += r.FeeBase;
            processingTotal += r.FeeProcessing;
            discountTotal   += r.FeeDiscount;
            lateTotal       += r.FeeLatefee;
            feeTotal        += r.FeeTotal;
            owedTotal       += r.OwedTotal;
        }
        return new AdultFeeBreakdownDto
        {
            FeeBase = baseTotal,
            FeeProcessing = processingTotal,
            FeeDiscount = discountTotal,
            FeeLateFee = lateTotal,
            FeeTotal = feeTotal,
            OwedTotal = owedTotal,
        };
    }

    /// <summary>
    /// Server-side resolution of a URL role key + job type → concrete RoleId and
    /// wizard behavior. This is the SINGLE source of truth for the security model:
    /// no caller (frontend, tests, external) gets to pick Staff directly.
    /// </summary>
    private readonly record struct AdultRoleResolution(
        string RoleId,
        bool NeedsTeamSelection,
        string DisplayName,
        string Description,
        string Icon);

    /// <summary>
    /// Resolve the role key + job type into the concrete role the server will assign
    /// and the wizard behavior the frontend should render. Enforces the minor-PII
    /// security model:
    /// <list type="bullet">
    /// <item>coach + Tournament  → Staff  (requires BAllowRosterViewAdult=false)</item>
    /// <item>coach + Club/League → UnassignedAdult (director promotes later)</item>
    /// <item>coach + other types → reject</item>
    /// <item>referee / recruiter → identical across supported job types</item>
    /// </list>
    /// Throws <see cref="InvalidOperationException"/> with a user-facing message on
    /// any violation.
    /// </summary>
    private static AdultRoleResolution ResolveAdultRole(AdultRegJobData job, string? roleKey)
    {
        if (!AdultRegRoleKeys.IsValid(roleKey))
        {
            throw new InvalidOperationException(
                $"Unknown registration role '{roleKey}'. " +
                $"Allowed: {string.Join(", ", AdultRegRoleKeys.All)}.");
        }

        var normalized = roleKey!.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case AdultRegRoleKeys.Coach:
                return ResolveCoach(job);

            case AdultRegRoleKeys.Referee:
                return new AdultRoleResolution(
                    RoleId: RoleConstants.Referee,
                    NeedsTeamSelection: false,
                    DisplayName: "Referee",
                    Description: "Register as a referee for this event.",
                    Icon: "bi-whistle");

            case AdultRegRoleKeys.Recruiter:
                return new AdultRoleResolution(
                    RoleId: RoleConstants.Recruiter,
                    NeedsTeamSelection: false,
                    DisplayName: "College Recruiter",
                    Description: "Register as a college recruiter to scout players.",
                    Icon: "bi-mortarboard");

            default:
                // Unreachable — IsValid guards this.
                throw new InvalidOperationException($"Unhandled role key '{roleKey}'.");
        }
    }

    /// <summary>
    /// Coach role resolution — three-way discrimination by job type with a hard
    /// security invariant for the Tournament branch.
    /// </summary>
    private static AdultRoleResolution ResolveCoach(AdultRegJobData job)
    {
        switch (job.JobTypeId)
        {
            case JobConstants.JobTypeClub:
            case JobConstants.JobTypeLeague:
                // Player-site context: self-registration ALWAYS creates UnassignedAdult.
                // No team selection (director assigns after approval, which also
                // promotes the role to Staff). This is the minor-PII firewall.
                return new AdultRoleResolution(
                    RoleId: RoleConstants.UnassignedAdult,
                    NeedsTeamSelection: false,
                    DisplayName: "Coach / Volunteer",
                    Description: "Register as an unassigned adult. A director will review " +
                                 "your request and assign you to a team.",
                    Icon: "bi-person-badge");

            case JobConstants.JobTypeTournament:
                // Tournament context: self-rostering coach — directly becomes Staff with
                // AssignedTeamId. BUT only if roster visibility is locked (minor-PII guard).
                if (job.BAllowRosterViewAdult)
                {
                    throw new InvalidOperationException(
                        "Adult self-registration is disabled for this event. " +
                        "Contact the tournament director. " +
                        "(To enable: set 'Allow Adult Roster View' to OFF in job configuration.)");
                }
                return new AdultRoleResolution(
                    RoleId: RoleConstants.Staff,
                    NeedsTeamSelection: true,
                    DisplayName: "Coach / Volunteer",
                    Description: "Register to coach one or more teams at this tournament.",
                    Icon: "bi-person-badge");

            default:
                // Root, Camp, Sales, or anything else — adult coach self-reg not supported.
                throw new InvalidOperationException(
                    "Adult coach registration is not supported for this event type.");
        }
    }

    private static string ResolveRoleId(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => RoleConstants.UnassignedAdult,
        AdultRoleType.Referee => RoleConstants.Referee,
        AdultRoleType.Recruiter => RoleConstants.Recruiter,
        AdultRoleType.Staff => RoleConstants.Staff,
        _ => throw new ArgumentOutOfRangeException(nameof(roleType), $"Unsupported role type: {roleType}")
    };

    private static AdultRoleType ResolveRoleTypeFromId(string? roleId) => roleId switch
    {
        RoleConstants.UnassignedAdult => AdultRoleType.UnassignedAdult,
        RoleConstants.Referee => AdultRoleType.Referee,
        RoleConstants.Recruiter => AdultRoleType.Recruiter,
        RoleConstants.Staff => AdultRoleType.Staff,
        _ => AdultRoleType.UnassignedAdult
    };

    /// <summary>
    /// Metadata-lookup key for <c>Jobs.AdultProfileMetadataJson</c>. Staff uses the
    /// same metadata block as UnassignedAdult since they're the same persona
    /// (a coach/volunteer) from the user's perspective.
    /// </summary>
    private static string GetRoleKey(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => "UnassignedAdult",
        AdultRoleType.Staff => "UnassignedAdult",
        AdultRoleType.Referee => "Referee",
        AdultRoleType.Recruiter => "Recruiter",
        _ => "UnassignedAdult"
    };

    /// <summary>
    /// URL role key ("coach", "referee", "recruiter") from the resolved AdultRoleType.
    /// Inverse of <see cref="ResolveAdultRole"/>'s key input. Used when legacy callers
    /// pass only the numeric RoleType and we need to re-derive the key.
    /// </summary>
    private static string GetUrlRoleKey(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => AdultRegRoleKeys.Coach,
        AdultRoleType.Staff => AdultRegRoleKeys.Coach,
        AdultRoleType.Referee => AdultRegRoleKeys.Referee,
        AdultRoleType.Recruiter => AdultRegRoleKeys.Recruiter,
        _ => AdultRegRoleKeys.Coach
    };

    private static string GetRoleDisplayName(AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult => "Coach / Volunteer",
        AdultRoleType.Staff => "Coach / Volunteer",
        AdultRoleType.Referee => "Referee",
        AdultRoleType.Recruiter => "College Recruiter",
        _ => "Adult"
    };

    private List<AdultValidationErrorDto> ValidateFormFields(AdultRegJobData jobData, PreSubmitAdultRegRequestDto request, AdultRoleType roleType)
    {
        var errors = new List<AdultValidationErrorDto>();

        var roleKey = GetRoleKey(roleType);
        var parsed = _metadataService.ParseForRole(jobData.AdultProfileMetadataJson, roleKey, jobData.JsonOptions);

        var formValues = request.FormValues ?? new();

        foreach (var field in parsed.TypedFields)
        {
            if (field.Visibility == "hidden" || field.Visibility == "adminOnly") continue;
            if (field.Validation?.Required != true) continue;

            var hasValue = formValues.TryGetValue(field.Name, out var val)
                && val.ValueKind != System.Text.Json.JsonValueKind.Null
                && val.ValueKind != System.Text.Json.JsonValueKind.Undefined
                && val.ToString()?.Trim().Length > 0;

            if (!hasValue)
            {
                var displayName = string.IsNullOrWhiteSpace(field.DisplayName) ? field.Name : field.DisplayName;
                errors.Add(new AdultValidationErrorDto
                {
                    Field = field.Name,
                    Message = $"{displayName} is required."
                });
            }
        }

        return errors;
    }

    private static string FormatExpiry(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 4)
        {
            var mm = digits[..2];
            var yy = digits[2..];
            var year = 2000 + int.Parse(yy);
            return $"{year}-{mm}";
        }
        if (digits.Length == 6)
        {
            var year = digits[..4];
            var mm = digits[4..];
            return $"{year}-{mm}";
        }
        return raw;
    }

    /// <summary>
    /// Fallback profile fields when <c>AdultProfileMetadataJson</c> is not configured.
    /// <para>
    /// The "Coaching Requests" free-text is ONLY shown to UnassignedAdult (Club/League
    /// coaches who have no team picker and need to express their team preference in
    /// words). Staff (Tournament coaches) have already selected specific teams via the
    /// multi-select, so asking again in free text is redundant — return empty.
    /// </para>
    /// </summary>
    private static List<JobRegFieldDto> BuildFallbackFields(AdultRoleType roleType)
    {
        // Staff: teams-coaching multi-select already captures intent. No free-text needed.
        if (roleType == AdultRoleType.Staff) return [];

        var (label, placeholder) = roleType switch
        {
            AdultRoleType.UnassignedAdult => (
                "Coaching Requests",
                "Indicate the age group or team you wish to coach"),
            AdultRoleType.Referee => (
                "Special Requests",
                "Enter special requests, or 'none' if you don't have any"),
            AdultRoleType.Recruiter => (
                "College / University",
                "What college or university do you represent?"),
            _ => ("Special Requests", "")
        };

        return
        [
            new JobRegFieldDto
            {
                Name = "SpecialRequests",
                DbColumn = "SpecialRequests",
                DisplayName = label,
                InputType = roleType == AdultRoleType.Recruiter ? "TEXT" : "TEXTAREA",
                Order = 1,
                Visibility = "public",
                Validation = new FieldValidation { Required = true, Message = placeholder }
            }
        ];
    }

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
        AdultRoleType.UnassignedAdult or AdultRoleType.Staff => job.AdultRegConfirmationOnScreen,
        AdultRoleType.Referee => job.RefereeRegConfirmationOnScreen ?? job.AdultRegConfirmationOnScreen,
        AdultRoleType.Recruiter => job.RecruiterRegConfirmationOnScreen ?? job.AdultRegConfirmationOnScreen,
        _ => job.AdultRegConfirmationOnScreen
    };

    private static string? GetConfirmationEmail(Jobs job, AdultRoleType roleType) => roleType switch
    {
        AdultRoleType.UnassignedAdult or AdultRoleType.Staff => job.AdultRegConfirmationEmail,
        AdultRoleType.Referee => job.RefereeRegConfirmationEmail ?? job.AdultRegConfirmationEmail,
        AdultRoleType.Recruiter => job.RecruiterRegConfirmationEmail ?? job.AdultRegConfirmationEmail,
        _ => job.AdultRegConfirmationEmail
    };
}
