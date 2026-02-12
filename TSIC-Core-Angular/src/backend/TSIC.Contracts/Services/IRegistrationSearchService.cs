using TSIC.Contracts.Dtos.RegistrationSearch;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for the Registration Search admin tool.
/// Handles search, detail view/edit, accounting, refunds, and batch email.
/// </summary>
public interface IRegistrationSearchService
{
    Task<RegistrationSearchResponse> SearchAsync(Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default);
    Task<RegistrationFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<RegistrationDetailDto?> GetRegistrationDetailAsync(Guid registrationId, Guid jobId, CancellationToken ct = default);
    Task UpdateRegistrationProfileAsync(Guid jobId, string userId, UpdateRegistrationProfileRequest request, CancellationToken ct = default);
    Task UpdateFamilyContactAsync(Guid jobId, string userId, UpdateFamilyContactRequest request, CancellationToken ct = default);
    Task UpdateUserDemographicsAsync(Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default);
    Task<AccountingRecordDto> CreateAccountingRecordAsync(Guid jobId, string userId, CreateAccountingRecordRequest request, CancellationToken ct = default);
    Task<RefundResponse> ProcessRefundAsync(Guid jobId, string userId, RefundRequest request, CancellationToken ct = default);
    Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default);
    Task<BatchEmailResponse> SendBatchEmailAsync(Guid jobId, string userId, BatchEmailRequest request, CancellationToken ct = default);
    Task<EmailPreviewResponse> PreviewEmailAsync(Guid jobId, EmailPreviewRequest request, CancellationToken ct = default);
    Task<List<JobOptionDto>> GetChangeJobOptionsAsync(Guid jobId, CancellationToken ct = default);
    Task<ChangeJobResponse> ChangeRegistrationJobAsync(Guid jobId, string userId, Guid registrationId, ChangeJobRequest request, CancellationToken ct = default);
    Task<DeleteRegistrationResponse> DeleteRegistrationAsync(Guid jobId, string userId, string callerRole, Guid registrationId, CancellationToken ct = default);
}
