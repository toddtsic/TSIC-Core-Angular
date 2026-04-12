using Microsoft.AspNetCore.Identity;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared.Adn;
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

    public AdultRegistrationService(
        IAdultRegistrationRepository repo,
        IProfileMetadataService metadataService,
        IEmailService emailService,
        UserManager<ApplicationUser> userManager,
        IFeeResolutionService feeService,
        IAdnApiService adnApiService,
        IRegistrationAccountingRepository acctRepo)
    {
        _repo = repo;
        _metadataService = metadataService;
        _emailService = emailService;
        _userManager = userManager;
        _feeService = feeService;
        _adnApiService = adnApiService;
        _acctRepo = acctRepo;
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

        // Stamp fees if applicable
        var feeCtx = new FeeApplicationContext { AddProcessingFees = jobData.BAddProcessingFees };
        var resolved = await _feeService.ResolveJobLevelFeeAsync(jobData.JobId, roleId, cancellationToken);
        if (resolved != null && resolved.EffectiveBalanceDue > 0m)
        {
            var reg = await _repo.GetTrackedRegistrationAsync(registrationId, cancellationToken);
            if (reg != null)
            {
                await _feeService.ApplyNewAdultRegistrationFeesAsync(reg, jobData.JobId, roleId, feeCtx, cancellationToken);
                await _repo.SaveChangesAsync(cancellationToken);

                // Process payment if CC provided
                if (request.CreditCard != null && request.PaymentMethod == "CC" && reg.OwedTotal > 0m)
                {
                    var paymentResult = await ProcessPaymentAsync(
                        registrationId, user.Id,
                        new AdultPaymentRequestDto
                        {
                            RegistrationId = registrationId,
                            CreditCard = request.CreditCard,
                            PaymentMethod = "CC"
                        },
                        cancellationToken);

                    if (!paymentResult.Success)
                    {
                        return new AdultRegistrationResponse
                        {
                            Success = true,
                            RegistrationId = registrationId,
                            Message = $"Registration created but payment failed: {paymentResult.Message}"
                        };
                    }
                }
                else if (request.PaymentMethod == "Check" && reg.OwedTotal > 0m)
                {
                    reg.PaymentMethodChosen = 3; // 3 = Check
                    await _repo.SaveChangesAsync(cancellationToken);
                }
            }
        }

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

    // ============ PreSubmit + Payment ============

    public async Task<PreSubmitAdultRegResponseDto> PreSubmitAsync(Guid jobId, string? userId, PreSubmitAdultRegRequestDto request, CancellationToken cancellationToken = default)
    {
        var jobData = await _repo.GetJobAdultRegDataAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Job not found.");

        var roleId = ResolveRoleId(request.RoleType);

        // Validate form fields against schema
        var validationErrors = ValidateFormFields(jobData, request);
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
        var resolved = await _feeService.ResolveJobLevelFeeAsync(jobId, roleId, cancellationToken);
        var baseFee = resolved?.EffectiveBalanceDue ?? 0m;

        // Build fee breakdown preview
        var rate = baseFee > 0m && feeCtx.AddProcessingFees
            ? await _feeService.GetEffectiveProcessingRateAsync(jobId, cancellationToken)
            : 0m;
        var feeProcessing = baseFee > 0m && feeCtx.AddProcessingFees
            ? Math.Round(baseFee * rate, 2)
            : 0m;
        var feeTotal = baseFee + feeProcessing;

        var fees = new AdultFeeBreakdownDto
        {
            FeeBase = baseFee,
            FeeProcessing = feeProcessing,
            FeeDiscount = 0m,
            FeeLateFee = 0m,
            FeeTotal = feeTotal,
            OwedTotal = feeTotal
        };

        Guid? registrationId = null;

        // Login-mode: create the registration now (user exists)
        if (userId != null)
        {
            var exists = await _repo.HasExistingRegistrationAsync(userId, jobId, roleId, cancellationToken);
            if (exists)
                throw new InvalidOperationException("You already have an active registration with this role for this event.");

            var regId = await CreateRegistrationAsync(
                jobData, userId, roleId, request.RoleType,
                request.FormValues, request.WaiverAcceptance,
                userId, cancellationToken);

            // Stamp fees on the registration
            if (baseFee > 0m)
            {
                var reg = await _repo.GetTrackedRegistrationAsync(regId, cancellationToken);
                if (reg != null)
                {
                    await _feeService.ApplyNewAdultRegistrationFeesAsync(reg, jobId, roleId, feeCtx, cancellationToken);
                    await _repo.SaveChangesAsync(cancellationToken);
                }
            }

            registrationId = regId;
        }

        return new PreSubmitAdultRegResponseDto
        {
            Valid = true,
            ValidationErrors = null,
            RegistrationId = registrationId,
            Fees = fees
        };
    }

    public async Task<AdultPaymentResponseDto> ProcessPaymentAsync(Guid registrationId, string userId, AdultPaymentRequestDto request, CancellationToken cancellationToken = default)
    {
        var reg = await _repo.GetTrackedRegistrationAsync(registrationId, cancellationToken)
            ?? throw new KeyNotFoundException("Registration not found.");

        if (reg.UserId != userId)
            throw new UnauthorizedAccessException("Registration does not belong to this user.");

        if (reg.OwedTotal <= 0m)
        {
            return new AdultPaymentResponseDto
            {
                Success = true,
                Message = "No payment required."
            };
        }

        if (request.PaymentMethod == "Check")
        {
            reg.PaymentMethodChosen = 3; // 3 = Check
            await _repo.SaveChangesAsync(cancellationToken);

            return new AdultPaymentResponseDto
            {
                Success = true,
                Message = "Registration recorded. Payment by check selected."
            };
        }

        // CC payment via Authorize.Net
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
        var amount = reg.OwedTotal;
        var invoiceNumber = $"{reg.Job.JobAi}_{reg.RegistrationId.ToString("N")[..8]}";
        if (invoiceNumber.Length > 20) invoiceNumber = invoiceNumber[..20];

        var ccExpiryDate = FormatExpiry(request.CreditCard.Expiry ?? "");
        var roleType = ResolveRoleTypeFromId(reg.RoleId);
        var description = $"Adult Registration: {GetRoleDisplayName(roleType)}";

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
            Amount = amount,
            InvoiceNumber = invoiceNumber,
            Description = description
        });

        if (adnResponse?.messages?.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok
            && adnResponse.transactionResponse?.messages != null
            && !string.IsNullOrWhiteSpace(adnResponse.transactionResponse.transId))
        {
            var transId = adnResponse.transactionResponse.transId;

            // Create accounting entry
            _acctRepo.Add(new RegistrationAccounting
            {
                RegistrationId = registrationId,
                Payamt = amount,
                Dueamt = amount,
                Paymeth = $"paid by cc: {amount:C} on {DateTime.Now:G} txID: {transId}",
                PaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"), // CC payment method
                Active = true,
                Createdate = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = userId,
                AdnTransactionId = transId,
                AdnInvoiceNo = invoiceNumber,
                AdnCc4 = request.CreditCard.Number!.Length >= 4
                    ? request.CreditCard.Number[^4..]
                    : request.CreditCard.Number,
                AdnCcexpDate = ccExpiryDate,
                Comment = description
            });

            reg.PaidTotal += amount;
            reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
            reg.PaymentMethodChosen = 1; // 1 = CC

            await _repo.SaveChangesAsync(cancellationToken);
            await _acctRepo.SaveChangesAsync(cancellationToken);

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

    private List<AdultValidationErrorDto> ValidateFormFields(AdultRegJobData jobData, PreSubmitAdultRegRequestDto request)
    {
        var errors = new List<AdultValidationErrorDto>();

        var roleKey = GetRoleKey(request.RoleType);
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

    private static List<JobRegFieldDto> BuildFallbackFields(AdultRoleType roleType)
    {
        var (label, placeholder) = roleType switch
        {
            AdultRoleType.UnassignedAdult => (
                "Coaching Requests",
                "Please indicate the age group or team you wish to coach"),
            AdultRoleType.Referee => (
                "Special Requests",
                "Enter any special requests, or 'none' if you don't have any"),
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
