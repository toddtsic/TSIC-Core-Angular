using AuthorizeNet.Api.Contracts.V1;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the Registration Search admin tool.
/// Orchestrates repositories, ADN refunds, text substitution, and email.
/// </summary>
public sealed class RegistrationSearchService : IRegistrationSearchService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly IAdnApiService _adnApi;
    private readonly ITextSubstitutionService _textSubstitution;
    private readonly IEmailService _emailService;
    private readonly ILogger<RegistrationSearchService> _logger;

    // Known CC payment method GUID
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

    public RegistrationSearchService(
        IRegistrationRepository registrationRepo,
        IRegistrationAccountingRepository accountingRepo,
        IJobRepository jobRepo,
        IDeviceRepository deviceRepo,
        IAdnApiService adnApi,
        ITextSubstitutionService textSubstitution,
        IEmailService emailService,
        ILogger<RegistrationSearchService> logger)
    {
        _registrationRepo = registrationRepo;
        _accountingRepo = accountingRepo;
        _jobRepo = jobRepo;
        _deviceRepo = deviceRepo;
        _adnApi = adnApi;
        _textSubstitution = textSubstitution;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<RegistrationSearchResponse> SearchAsync(
        Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default)
    {
        return await _registrationRepo.SearchAsync(jobId, request, ct);
    }

    public async Task<RegistrationFilterOptionsDto> GetFilterOptionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _registrationRepo.GetFilterOptionsAsync(jobId, ct);
    }

    public async Task<RegistrationDetailDto?> GetRegistrationDetailAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default)
    {
        return await _registrationRepo.GetRegistrationDetailAsync(registrationId, jobId, ct);
    }

    public async Task UpdateRegistrationProfileAsync(
        Guid jobId, string userId, UpdateRegistrationProfileRequest request, CancellationToken ct = default)
    {
        await _registrationRepo.UpdateRegistrationProfileAsync(jobId, userId, request, ct);
    }

    public async Task UpdateFamilyContactAsync(
        Guid jobId, string userId, UpdateFamilyContactRequest request, CancellationToken ct = default)
    {
        await _registrationRepo.UpdateFamilyContactAsync(jobId, userId, request, ct);
    }

    public async Task UpdateUserDemographicsAsync(
        Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default)
    {
        await _registrationRepo.UpdateUserDemographicsAsync(jobId, userId, request, ct);
    }

    public async Task<AccountingRecordDto> CreateAccountingRecordAsync(
        Guid jobId, string userId, CreateAccountingRecordRequest request, CancellationToken ct = default)
    {
        // Validate registration belongs to job
        var regJobId = await _registrationRepo.GetRegistrationJobIdAsync(request.RegistrationId, ct);
        if (regJobId == null || regJobId.Value != jobId)
            throw new InvalidOperationException("Registration not found or does not belong to this job.");

        var entity = new RegistrationAccounting
        {
            RegistrationId = request.RegistrationId,
            PaymentMethodId = request.PaymentMethodId,
            Dueamt = request.DueAmount,
            Payamt = request.PaidAmount,
            Comment = request.Comment,
            CheckNo = request.CheckNo,
            PromoCode = request.PromoCode,
            Active = true,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };

        _accountingRepo.Add(entity);

        // Update registration financial totals
        var reg = await _registrationRepo.GetByIdAsync(request.RegistrationId, ct)
            ?? throw new InvalidOperationException("Registration not found.");

        reg.PaidTotal += request.PaidAmount ?? 0;
        reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
        reg.Modified = DateTime.UtcNow;
        reg.LebUserId = userId;

        await _accountingRepo.SaveChangesAsync(ct);

        return new AccountingRecordDto
        {
            AId = entity.AId,
            Date = entity.Createdate,
            PaymentMethod = "",
            DueAmount = entity.Dueamt,
            PaidAmount = entity.Payamt,
            Comment = entity.Comment,
            CheckNo = entity.CheckNo,
            PromoCode = entity.PromoCode,
            Active = entity.Active,
            CanRefund = false
        };
    }

    public async Task<RefundResponse> ProcessRefundAsync(
        Guid jobId, string userId, RefundRequest request, CancellationToken ct = default)
    {
        // Load original accounting record
        var original = await _accountingRepo.GetByAIdAsync(request.AccountingRecordId, ct);
        if (original == null)
            return new RefundResponse { Success = false, Message = "Accounting record not found." };

        // Validate record belongs to a registration in this job
        if (original.Registration == null || original.Registration.JobId != jobId)
            return new RefundResponse { Success = false, Message = "Accounting record does not belong to this job." };

        // Validate it's a CC payment with transaction ID
        if (string.IsNullOrWhiteSpace(original.AdnTransactionId))
            return new RefundResponse { Success = false, Message = "No Authorize.Net transaction ID — cannot refund." };

        // Validate refund amount
        var originalPay = original.Payamt ?? 0;
        if (request.RefundAmount <= 0 || request.RefundAmount > originalPay)
            return new RefundResponse { Success = false, Message = $"Refund amount must be between $0.01 and ${originalPay:F2}." };

        try
        {
            // Get ADN credentials from job's customer
            var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
            var env = _adnApi.GetADNEnvironment();

            var adnResult = _adnApi.ADN_Refund(new AdnRefundRequest
            {
                Env = env,
                LoginId = creds.AdnLoginId ?? "",
                TransactionKey = creds.AdnTransactionKey ?? "",
                CardNumberLast4 = original.AdnCc4 ?? "0000",
                Expiry = original.AdnCcexpDate ?? "XXXX",
                TransactionId = original.AdnTransactionId,
                Amount = request.RefundAmount,
                InvoiceNumber = original.AdnInvoiceNo ?? ""
            });

            if (adnResult?.messages?.resultCode != messageTypeEnum.Ok)
            {
                var errorMsg = adnResult?.messages?.message?.FirstOrDefault()?.text ?? "Unknown error from Authorize.Net.";
                _logger.LogWarning("ADN refund failed for AId={AId}: {Error}", request.AccountingRecordId, errorMsg);
                return new RefundResponse { Success = false, Message = errorMsg };
            }

            var refundTransId = adnResult.transactionResponse?.transId ?? "";

            // Create negative accounting record
            var refundEntry = new RegistrationAccounting
            {
                RegistrationId = original.RegistrationId,
                PaymentMethodId = CcPaymentMethodId,
                Paymeth = "Credit Card Refund",
                Payamt = -request.RefundAmount,
                Dueamt = 0,
                Comment = request.Reason ?? "Refund processed",
                AdnTransactionId = refundTransId,
                AdnCc4 = original.AdnCc4,
                AdnCcexpDate = original.AdnCcexpDate,
                Active = true,
                Createdate = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                LebUserId = userId
            };
            _accountingRepo.Add(refundEntry);

            // Update registration financials
            var reg = original.Registration;
            reg.PaidTotal -= request.RefundAmount;
            reg.OwedTotal = reg.FeeTotal - reg.PaidTotal;
            reg.Modified = DateTime.UtcNow;
            reg.LebUserId = userId;

            await _accountingRepo.SaveChangesAsync(ct);

            _logger.LogInformation("Refund processed: AId={AId}, Amount={Amount}, RefundTransId={TransId}",
                request.AccountingRecordId, request.RefundAmount, refundTransId);

            return new RefundResponse
            {
                Success = true,
                Message = "Refund processed successfully.",
                TransactionId = refundTransId,
                RefundedAmount = request.RefundAmount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund failed for AId={AId}", request.AccountingRecordId);
            return new RefundResponse { Success = false, Message = $"Refund failed: {ex.Message}" };
        }
    }

    public async Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default)
    {
        return await _accountingRepo.GetPaymentMethodOptionsAsync(ct);
    }

    public async Task<BatchEmailResponse> SendBatchEmailAsync(
        Guid jobId, string userId, BatchEmailRequest request, CancellationToken ct = default)
    {
        // Load registrations with User nav property to get email addresses
        var registrations = await _registrationRepo.GetByIdsAsync(request.RegistrationIds, ct);

        // Validate all belong to this job
        var invalidRegs = registrations.Where(r => r.JobId != jobId).ToList();
        if (invalidRegs.Count > 0)
            throw new InvalidOperationException("Some registrations do not belong to this job.");

        // Load job info for text substitution
        var jobConfirmation = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobPath = jobConfirmation?.JobPath ?? "";

        var sent = 0;
        var failed = 0;
        var failedAddresses = new List<string>();

        foreach (var reg in registrations)
        {
            try
            {
                // Get email
                var regWithUser = await _registrationRepo.GetByJobAndFamilyWithUsersAsync(
                    jobId, reg.FamilyUserId ?? "", cancellationToken: ct);
                var thisReg = regWithUser.FirstOrDefault(r => r.RegistrationId == reg.RegistrationId);
                var email = thisReg?.User?.Email;

                if (string.IsNullOrWhiteSpace(email))
                {
                    failed++;
                    failedAddresses.Add($"(no email for RegistrationAi #{reg.RegistrationAi})");
                    continue;
                }

                // Substitute tokens
                var renderedSubject = await _textSubstitution.SubstituteAsync(
                    jobPath, jobId, CcPaymentMethodId, reg.RegistrationId, reg.FamilyUserId ?? "", request.Subject);
                var renderedBody = await _textSubstitution.SubstituteAsync(
                    jobPath, jobId, CcPaymentMethodId, reg.RegistrationId, reg.FamilyUserId ?? "", request.BodyTemplate);

                var emailMsg = new EmailMessageDto
                {
                    FromAddress = jobConfirmation?.JobName,
                    Subject = renderedSubject,
                    HtmlBody = renderedBody,
                    ToAddresses = new List<string> { email }
                };

                var success = await _emailService.SendAsync(emailMsg, cancellationToken: ct);
                if (success) sent++;
                else { failed++; failedAddresses.Add(email); }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch email failed for reg {RegId}", reg.RegistrationId);
                failed++;
                failedAddresses.Add($"(error for RegistrationAi #{reg.RegistrationAi})");
            }
        }

        return new BatchEmailResponse
        {
            TotalRecipients = registrations.Count,
            Sent = sent,
            Failed = failed,
            FailedAddresses = failedAddresses
        };
    }

    public async Task<EmailPreviewResponse> PreviewEmailAsync(
        Guid jobId, EmailPreviewRequest request, CancellationToken ct = default)
    {
        var registrations = await _registrationRepo.GetByIdsAsync(request.RegistrationIds, ct);
        var jobConfirmation = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobPath = jobConfirmation?.JobPath ?? "";

        var previews = new List<RenderedEmailPreview>();

        foreach (var reg in registrations)
        {
            // Load user info
            var regWithUser = await _registrationRepo.GetByJobAndFamilyWithUsersAsync(
                jobId, reg.FamilyUserId ?? "", cancellationToken: ct);
            var thisReg = regWithUser.FirstOrDefault(r => r.RegistrationId == reg.RegistrationId);

            var name = thisReg?.User != null
                ? $"{thisReg.User.FirstName} {thisReg.User.LastName}".Trim()
                : "Unknown";
            var email = thisReg?.User?.Email ?? "(no email)";

            var renderedSubject = await _textSubstitution.SubstituteAsync(
                jobPath, jobId, CcPaymentMethodId, reg.RegistrationId, reg.FamilyUserId ?? "", request.Subject);
            var renderedBody = await _textSubstitution.SubstituteAsync(
                jobPath, jobId, CcPaymentMethodId, reg.RegistrationId, reg.FamilyUserId ?? "", request.BodyTemplate);

            previews.Add(new RenderedEmailPreview
            {
                RecipientName = name,
                RecipientEmail = email,
                RenderedSubject = renderedSubject,
                RenderedBody = renderedBody
            });
        }

        return new EmailPreviewResponse { Previews = previews };
    }

    public async Task<List<JobOptionDto>> GetChangeJobOptionsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _jobRepo.GetOtherJobsForCustomerAsync(jobId, ct);
    }

    public async Task<ChangeJobResponse> ChangeRegistrationJobAsync(
        Guid jobId, string userId, Guid registrationId, ChangeJobRequest request, CancellationToken ct = default)
    {
        // Load the registration (tracked for update)
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct);
        if (reg == null)
            return new ChangeJobResponse { Success = false, Message = "Registration not found." };

        // Validate registration belongs to current job
        if (reg.JobId != jobId)
            return new ChangeJobResponse { Success = false, Message = "Registration does not belong to this job." };

        // Validate it's a Player role
        if (reg.RoleId != Domain.Constants.RoleConstants.Player)
            return new ChangeJobResponse { Success = false, Message = "Only Player registrations can be moved between jobs." };

        // Validate new job is different
        if (reg.JobId == request.NewJobId)
            return new ChangeJobResponse { Success = false, Message = "Registration is already in this job." };

        // Find matching registration team in target job
        var newTeamId = await _registrationRepo.FindMatchingRegistrationTeamAsync(registrationId, request.NewJobId, ct);

        // Update the registration
        reg.JobId = request.NewJobId;
        reg.AssignedTeamId = newTeamId;
        reg.Modified = DateTime.UtcNow;
        reg.LebUserId = userId;

        await _registrationRepo.SaveChangesAsync(ct);

        // Get new job name for response
        var newJobName = await _jobRepo.GetJobNameAsync(request.NewJobId, ct);

        _logger.LogInformation(
            "Registration {RegId} moved from job {OldJobId} to {NewJobId} by {UserId}",
            registrationId, jobId, request.NewJobId, userId);

        return new ChangeJobResponse
        {
            Success = true,
            Message = $"Registration moved to {newJobName ?? "new job"} successfully.",
            NewJobName = newJobName
        };
    }

    public async Task<DeleteRegistrationResponse> DeleteRegistrationAsync(
        Guid jobId, string userId, string callerRole, Guid registrationId, CancellationToken ct = default)
    {
        // Load the registration (tracked for deletion)
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct);
        if (reg == null)
            return new DeleteRegistrationResponse { Success = false, Message = "Registration not found." };

        // Validate registration belongs to current job
        if (reg.JobId != jobId)
            return new DeleteRegistrationResponse { Success = false, Message = "Registration does not belong to this job." };

        // Role-based authorization: check the registration's role
        var regRoleName = await _registrationRepo.GetRegistrationRoleNameAsync(registrationId, ct);

        if (string.Equals(regRoleName, RoleConstants.Names.UnassignedAdultName, StringComparison.OrdinalIgnoreCase))
        {
            // Unassigned Adult → only Superuser can delete
            if (!string.Equals(callerRole, RoleConstants.Names.SuperuserName, StringComparison.OrdinalIgnoreCase))
                return new DeleteRegistrationResponse { Success = false, Message = "Only Superuser can delete Unassigned Adult registrations." };
        }
        else if (!string.Equals(regRoleName, RoleConstants.Names.PlayerName, StringComparison.OrdinalIgnoreCase)
              && !string.Equals(regRoleName, RoleConstants.Names.StaffName, StringComparison.OrdinalIgnoreCase))
        {
            // Only Player, Staff, and Unassigned Adult roles are deletable
            return new DeleteRegistrationResponse { Success = false, Message = $"Registrations with role '{regRoleName}' cannot be deleted." };
        }

        // Pre-condition checks
        var hasAccounting = await _registrationRepo.HasAccountingRecordsAsync(registrationId, ct);
        if (hasAccounting)
            return new DeleteRegistrationResponse { Success = false, Message = "Cannot delete: registration has accounting records." };

        var hasStoreRecords = await _registrationRepo.HasStoreCartBatchRecordsAsync(registrationId, ct);
        if (hasStoreRecords)
            return new DeleteRegistrationResponse { Success = false, Message = "Cannot delete: registration has store purchase records." };

        if (!string.IsNullOrEmpty(reg.RegsaverPolicyId))
            return new DeleteRegistrationResponse { Success = false, Message = "Cannot delete: registration has an active insurance policy." };

        // Device cleanup before deletion
        var deviceRegIds = await _deviceRepo.GetDeviceRegistrationIdsByRegistrationAsync(registrationId, ct);
        if (deviceRegIds.Count > 0)
        {
            _deviceRepo.RemoveDeviceRegistrationIds(deviceRegIds);
            await _deviceRepo.SaveChangesAsync(ct);
        }

        // Delete the registration
        _registrationRepo.Remove(reg);
        await _registrationRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Registration {RegId} (role={Role}) deleted from job {JobId} by {UserId}",
            registrationId, regRoleName, jobId, userId);

        return new DeleteRegistrationResponse { Success = true, Message = "Registration deleted successfully." };
    }
}
