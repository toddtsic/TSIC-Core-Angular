using AuthorizeNet.Api.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Players;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.RosterSwapper;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Payments;
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
    private readonly IFamiliesRepository _familiesRepo;
    private readonly IDeviceRepository _deviceRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IAdnApiService _adnApi;
    private readonly IArbSubscriptionRepository _arbRepo;
    private readonly ITextSubstitutionService _textSubstitution;
    private readonly IEmailBatchService _emailBatch;
    private readonly IRegistrationFeeAdjustmentService _feeAdjustment;
    private readonly IPaymentService _paymentService;
    private readonly IPaymentStateService _paymentState;
    private readonly IRegisteredPlayerShaper _playerShaper;
    private readonly IUserRepository _userRepo;
    private readonly IUsLaxService _usLax;
    private readonly ILogger<RegistrationSearchService> _logger;

    // Known payment method GUIDs. CC charging itself goes through PaymentService's
    // canonical engine; the constant remains here for non-charge consumers that key on
    // CC payments (text substitution, ledger filters).
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CcCreditMethodId = Guid.Parse("31ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CheckMethodId = Guid.Parse("32ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CorrectionMethodId = Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D");

    public RegistrationSearchService(
        IRegistrationRepository registrationRepo,
        IRegistrationAccountingRepository accountingRepo,
        IJobRepository jobRepo,
        IFamiliesRepository familiesRepo,
        IDeviceRepository deviceRepo,
        ITeamRepository teamRepo,
        IAdnApiService adnApi,
        IArbSubscriptionRepository arbRepo,
        ITextSubstitutionService textSubstitution,
        IEmailBatchService emailBatch,
        IRegistrationFeeAdjustmentService feeAdjustment,
        IPaymentService paymentService,
        IPaymentStateService paymentState,
        IRegisteredPlayerShaper playerShaper,
        IUserRepository userRepo,
        IUsLaxService usLax,
        ILogger<RegistrationSearchService> logger)
    {
        _registrationRepo = registrationRepo;
        _accountingRepo = accountingRepo;
        _jobRepo = jobRepo;
        _familiesRepo = familiesRepo;
        _deviceRepo = deviceRepo;
        _teamRepo = teamRepo;
        _adnApi = adnApi;
        _arbRepo = arbRepo;
        _textSubstitution = textSubstitution;
        _emailBatch = emailBatch;
        _feeAdjustment = feeAdjustment;
        _paymentService = paymentService;
        _paymentState = paymentState;
        _playerShaper = playerShaper;
        _userRepo = userRepo;
        _usLax = usLax;
        _logger = logger;
    }

    public async Task<RegistrationSearchResponse> SearchAsync(
        Guid jobId, RegistrationSearchRequest request, CancellationToken ct = default)
    {
        return await _registrationRepo.SearchAsync(jobId, request, ct);
    }

    public async Task<RegistrationSearchResponse> ArbCardExpiringLookupAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Env-bound: subscriptions resolve only against the account that created them (sandbox
        // off-Production). On Production this hits the real ADN account as before.
        var env = _adnApi.GetADNEnvironment();
        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);

        var response = _adnApi.ARBGetSubscriptionListRequest(
            env, creds.AdnLoginId!, creds.AdnTransactionKey!,
            ARBGetSubscriptionListSearchTypeEnum.cardExpiringThisMonth);

        if (response?.messages?.resultCode != messageTypeEnum.Ok
            || response.subscriptionDetails == null)
        {
            return EmptySearchResponse();
        }

        var invoices = response.subscriptionDetails
            .Where(s => !string.IsNullOrEmpty(s.invoice))
            .Select(s => s.invoice)
            .ToList();

        if (invoices.Count == 0) return EmptySearchResponse();

        var regs = await _arbRepo.GetRegistrationsByInvoiceNumbersAsync(invoices, jobId, ct);
        var registrationIds = regs.Select(r => r.RegistrationId).Distinct().ToList();

        if (registrationIds.Count == 0) return EmptySearchResponse();

        // Route through the normal search pipeline with only the registration-id filter —
        // no Active / Role / etc. gates, so dropped or otherwise out-of-scope registrations
        // with owed balances still surface for collection follow-up.
        var request = new RegistrationSearchRequest { RegistrationIds = registrationIds };
        return await _registrationRepo.SearchAsync(jobId, request, ct);
    }

    private static RegistrationSearchResponse EmptySearchResponse() => new()
    {
        Result = [],
        Count = 0,
        TotalFees = 0m,
        TotalPaid = 0m,
        TotalOwed = 0m
    };

    public async Task<RegistrationFilterOptionsDto> GetFilterOptionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        // Sequential awaits — these three reads share the scoped DbContext (no Task.WhenAll).
        var options = await _registrationRepo.GetFilterOptionsAsync(jobId, ct);
        var playerTargets = await _jobRepo.GetInviteTargetJobsForCustomerAsync(
            jobId, Contracts.Dtos.RegistrationSearch.InviteRegistrationKind.Player, ct);
        var clubRepTargets = await _jobRepo.GetInviteTargetJobsForCustomerAsync(
            jobId, Contracts.Dtos.RegistrationSearch.InviteRegistrationKind.Team, ct);

        return options with
        {
            EligiblePlayerInviteTargetJobs = playerTargets,
            EligibleClubRepInviteTargetJobs = clubRepTargets
        };
    }

    public async Task<List<CadtClubNode>> GetCadtTreeAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _registrationRepo.GetCadtTreeForJobAsync(jobId, ct);
    }

    public async Task<RegistrationDetailDto?> GetRegistrationDetailAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default)
    {
        return await _registrationRepo.GetRegistrationDetailAsync(registrationId, jobId, ct);
    }

    /// <summary>
    /// Re-ping this single registration's USA Lacrosse membership and refresh the stored
    /// <c>SportAssnIdexpDate</c> on that one row. Unlike the coach-approval-queue re-validate
    /// (which is anchor-scoped and fans the expiry across every Staff grant for the user), this
    /// records onto exactly the registration in view — so it works for players and coaches alike.
    /// A vendor outage leaves the stored expiry untouched and reports the transient failure.
    /// </summary>
    public async Task<RevalidateUsLaxResultDto> RevalidateUsLaxAsync(
        Guid jobId, Guid registrationId, CancellationToken ct = default)
    {
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct);
        if (reg is null || reg.JobId != jobId)
            return new RevalidateUsLaxResultDto { Found = false, Message = "Registration not found for this job." };

        if (string.IsNullOrWhiteSpace(reg.SportAssnId))
            return new RevalidateUsLaxResultDto { Found = false, Message = "No USA Lacrosse number on file." };

        var member = await _usLax.GetMemberAsync(reg.SportAssnId, ct);

        // Vendor unreachable / transient → leave the stored value untouched, just report.
        if (member is null || member.StatusCode == 0)
            return new RevalidateUsLaxResultDto { Found = false, Message = "USA Lacrosse is unreachable right now. Try again shortly." };

        var expDate = DateTime.TryParse(member.Output?.ExpDate, out var dt) ? dt : (DateTime?)null;

        // Definitive membership hit with a parseable expiry → record it on this registration.
        if (member.StatusCode == 200 && expDate.HasValue)
            await _registrationRepo.UpdateSportAssnIdExpDateAsync(registrationId, expDate.Value, ct);

        return new RevalidateUsLaxResultDto
        {
            Found = member.StatusCode == 200,
            MemStatus = member.Output?.MemStatus ?? (member.StatusCode == 404 ? "Not found" : null),
            ExpDate = expDate?.ToString("yyyy-MM-dd"),
            Message = member.StatusCode == 200 ? null : (member.ErrorMessage ?? "Membership not found.")
        };
    }

    public async Task<FamilyAccountingDto?> GetFamilyAccountingAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default)
    {
        var anchor = await _registrationRepo.GetByIdAsync(registrationId, ct);
        if (anchor == null || anchor.JobId != jobId) return null;
        if (string.IsNullOrWhiteSpace(anchor.FamilyUserId)) return null;

        var familyUserId = anchor.FamilyUserId;

        // The sibling set — keyed by (JobId, FamilyUserId), the parent-side analog of the
        // club rep's (JobId, ClubrepRegistrationid). Mirrors GetClubRepAccountingAsync.
        var rawPlayers = await _registrationRepo.GetFamilyPlayersForAccountingAsync(jobId, familyUserId, ct);
        if (rawPlayers.Count == 0) return null;

        // Shape each child into a RegisteredTeamDto through the canonical player shaper — the
        // same payment-state path teams use (RegisteredTeamShaper). Keeps per-method owed,
        // proc, and deposit/balance off the one source so the family grid can never drift.
        var playerRows = await _playerShaper.ShapeAsync(jobId, rawPlayers, ct);

        // Merge each child's ledger, stamping every record with its owning player — the family
        // analog of the club-rep TeamId discriminator. Sequential awaits per child: these share
        // one scoped DbContext, so Task.WhenAll would throw.
        var records = new List<AccountingRecordDto>();
        foreach (var p in rawPlayers)
        {
            var recs = await _accountingRepo.GetByRegistrationIdAsync(p.RegistrationId, ct);
            records.AddRange(recs.Select(r => r with
            {
                OwnerRegistrationId = p.RegistrationId,
                OwnerName = p.PlayerName,
                OwnerTeamName = p.AssignedTeamName,
                OwnerAgeGroupName = p.AssignedAgeGroupName,
                OwnerClubName = p.AssignedClubName
            }));
        }

        var family = await _familiesRepo.GetByFamilyUserIdAsync(familyUserId, ct);
        var familyName = !string.IsNullOrWhiteSpace(family?.DadLastName) ? $"{family!.DadLastName} Family"
            : !string.IsNullOrWhiteSpace(family?.MomLastName) ? $"{family!.MomLastName} Family"
            : "Family";

        // Director's complete family ledger: EVERY player (active and inactive) is shown and
        // counted in the family totals. Inactive pay-by-check siblings owe real money and must be
        // included; dropped/waitlist siblings carry $0 so they add nothing — no classification
        // needed. FeeTotal/PaidTotal/OwedTotal already sum all rawPlayers.
        var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);

        return new FamilyAccountingDto
        {
            AnchorRegistrationId = registrationId,
            FamilyName = familyName,
            FeeTotal = rawPlayers.Sum(p => p.FeeTotal),
            PaidTotal = rawPlayers.Sum(p => p.PaidTotal),
            OwedTotal = rawPlayers.Sum(p => p.OwedTotal),
            Players = playerRows,
            AccountingRecords = records.OrderByDescending(r => r.Date).ToList(),
            PaymentMethodsAllowedCode = feeSettings?.PaymentMethodsAllowedCode ?? PaymentMethodConstants.CreditCardOrCheck
        };
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

    public async Task UpdateFamilyAccountDemographicsAsync(
        Guid jobId, string userId, UpdateUserDemographicsRequest request, CancellationToken ct = default)
    {
        await _registrationRepo.UpdateFamilyAccountDemographicsAsync(jobId, userId, request, ct);
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
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = userId
        };

        // Record the row and re-derive PaidTotal/OwedTotal from the ledger in one
        // transaction, so the registration's totals can't drift from its accounting rows.
        // (Existence/ownership already validated above.)
        await _accountingRepo.RecordPaymentAndRecomputeAsync(entity, userId, ct);

        return new AccountingRecordDto
        {
            AId = entity.AId,
            TeamId = entity.TeamId,
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

            // Check original transaction status to determine void vs refund
            var txDetails = _adnApi.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, original.AdnTransactionId);

            if (txDetails?.messages?.resultCode != messageTypeEnum.Ok)
                return new RefundResponse { Success = false, Message = "Could not look up original transaction details." };

            var txStatus = txDetails.transaction?.transactionStatus;
            string refundTransId;
            decimal reversedAmount;

            if (txStatus == "capturedPendingSettlement")
            {
                // VOID the transaction (full amount — ADN voids are always full)
                var voidResult = _adnApi.ADN_Void_Result(new AdnVoidRequest
                {
                    Env = env,
                    LoginId = creds.AdnLoginId ?? "",
                    TransactionKey = creds.AdnTransactionKey ?? "",
                    TransactionId = original.AdnTransactionId
                });

                if (!voidResult.Success)
                    return new RefundResponse { Success = false, Message = $"CC Void failed: {voidResult.MessageForUser}" };

                refundTransId = voidResult.TransactionId ?? "";
                reversedAmount = original.Payamt ?? 0; // void reverses full original amount

                // Mark original record as voided. Write the void fact into Comment — the field
                // the accounting tab actually renders. (Paymeth is NOT shown: the display prefers
                // the AccountingPaymentMethods lookup name, so anything appended to Paymeth is
                // invisible.) Make it unmistakably a VOID, not a refund, so admin can tell at a glance.
                var voidNote = $"VOIDED {DateTime.Now:g} — CC was not yet settled at "
                    + $"Authorize.Net, so the original ${reversedAmount:F2} charge was VOIDED (not refunded). "
                    + $"ADN void tx {refundTransId}."
                    + (string.IsNullOrWhiteSpace(request.Reason) ? "" : $" Reason: {request.Reason}");
                original.Comment = string.IsNullOrWhiteSpace(original.Comment)
                    ? voidNote
                    : $"{original.Comment} | {voidNote}";
                original.Payamt = 0;
            }
            else if (txStatus == "settledSuccessfully")
            {
                // REFUND the transaction (partial or full)
                var adnResult = _adnApi.ADN_Refund_Result(new AdnRefundRequest
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

                if (!adnResult.Success)
                    return new RefundResponse { Success = false, Message = $"CC Refund failed: {adnResult.MessageForUser}" };

                refundTransId = adnResult.TransactionId ?? "";
                reversedAmount = request.RefundAmount;

                // Create negative accounting record for the refund
                _accountingRepo.Add(new RegistrationAccounting
                {
                    RegistrationId = original.RegistrationId,
                    PaymentMethodId = CcCreditMethodId,
                    Paymeth = "Credit Card Refund",
                    Payamt = -request.RefundAmount,
                    Dueamt = 0,
                    Comment = request.Reason ?? "Refund processed",
                    AdnTransactionId = refundTransId,
                    AdnCc4 = original.AdnCc4,
                    AdnCcexpDate = original.AdnCcexpDate,
                    AdnInvoiceNo = original.AdnInvoiceNo,
                    Active = true,
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
                    LebUserId = userId
                });
            }
            else
            {
                return new RefundResponse { Success = false, Message = $"Transaction status '{txStatus}' does not support refund/void." };
            }

            // Update registration financials
            var reg = original.Registration;
            reg.PaidTotal -= reversedAmount;
            reg.RecalcTotals();
            reg.Modified = DateTime.Now;
            reg.LebUserId = userId;

            await _accountingRepo.SaveChangesAsync(ct);

            var action = txStatus == "capturedPendingSettlement" ? "voided" : "refunded";
            _logger.LogInformation("Refund/{Action} processed: AId={AId}, Amount={Amount}, TransId={TransId}",
                action, request.AccountingRecordId, reversedAmount, refundTransId);

            return new RefundResponse
            {
                Success = true,
                Message = txStatus == "capturedPendingSettlement"
                    ? $"Transaction voided successfully (${reversedAmount:F2})."
                    : "Refund processed successfully.",
                TransactionId = refundTransId,
                RefundedAmount = reversedAmount
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

    public async Task<RegistrationCheckOrCorrectionResponse> RecordCheckOrCorrectionAsync(
        Guid jobId, string userId, RegistrationCheckOrCorrectionRequest request, CancellationToken ct = default)
    {
        // Validate registration belongs to job
        var regJobId = await _registrationRepo.GetRegistrationJobIdAsync(request.RegistrationId, ct);
        if (regJobId == null || regJobId.Value != jobId)
            return new RegistrationCheckOrCorrectionResponse { Success = false, Error = "Registration not found or does not belong to this job." };

        var isCheck = string.Equals(request.PaymentType, "Check", StringComparison.OrdinalIgnoreCase);
        var isCorrection = string.Equals(request.PaymentType, "Correction", StringComparison.OrdinalIgnoreCase);

        if (!isCheck && !isCorrection)
            return new RegistrationCheckOrCorrectionResponse { Success = false, Error = "PaymentType must be 'Check' or 'Correction'." };

        if (isCheck && request.Amount <= 0)
            return new RegistrationCheckOrCorrectionResponse { Success = false, Error = "A check payment must be > $0.00." };
        if (isCorrection && request.Amount == 0)
            return new RegistrationCheckOrCorrectionResponse { Success = false, Error = "A correction amount cannot be $0.00." };

        // Overpayment guard — check/correction cannot exceed what is owed
        var regForValidation = await _registrationRepo.GetByIdAsync(request.RegistrationId, ct);
        if (regForValidation != null && request.Amount > regForValidation.OwedTotal)
            return new RegistrationCheckOrCorrectionResponse { Success = false, Error = $"Amount ${request.Amount:F2} exceeds the balance owed of ${regForValidation.OwedTotal:F2}." };

        // Check-specific cap — CkOwed (CC owed minus the processing-fee credit that
        // a check would skip). Tighter than the OwedTotal cap above; mirrors the FE
        // balance-due. Corrections keep the OwedTotal cap (intentional ± adjustments).
        if (isCheck && regForValidation != null)
        {
            var state = await _paymentState.ForRegistrationAsync(request.RegistrationId, jobId, ct);
            var owed = state.ResolveOwed(
                regForValidation.OwedTotal,
                regForValidation.FeeBase,
                regForValidation.TotalDiscount(),
                regForValidation.FeeLatefee,
                regForValidation.FeeDonation,
                regForValidation.FeeProcessing);
            if (request.Amount > owed.Check)
                return new RegistrationCheckOrCorrectionResponse { Success = false, Error = $"Check payment ${request.Amount:F2} exceeds the check balance owed of ${owed.Check:F2}." };
        }

        // Correction lower-floor — invariant: balance stays in [0, FeeTotal]. Upper
        // bound covered by the OwedTotal cap above ("can't charge more than owed");
        // this adds the symmetric floor ("can't credit more than paid").
        if (isCorrection && regForValidation != null && request.Amount < -regForValidation.PaidTotal)
            return new RegistrationCheckOrCorrectionResponse { Success = false, Error = $"Correction ${request.Amount:F2} exceeds the amount paid (${regForValidation.PaidTotal:F2} refundable)." };

        var paymentMethodId = isCheck ? CheckMethodId : CorrectionMethodId;

        var entity = new RegistrationAccounting
        {
            RegistrationId = request.RegistrationId,
            PaymentMethodId = paymentMethodId,
            Dueamt = 0,
            Payamt = request.Amount,
            CheckNo = request.CheckNo,
            Comment = request.Comment,
            Active = true,
            Createdate = DateTime.Now,
            Modified = DateTime.Now,
            LebUserId = userId
        };

        // Reduce the processing fee proportionally first (non-CC payments; core accounting
        // principle), then record the row and re-derive PaidTotal/OwedTotal from the ledger in
        // one transaction. The reducer mutates the tracked registration without saving and does
        // not read PaidTotal, so the chokepoint's recompute picks up the reduced FeeProcessing.
        var reg = await _registrationRepo.GetByIdAsync(request.RegistrationId, ct)
            ?? throw new InvalidOperationException("Registration not found.");

        await _feeAdjustment.ReduceProcessingFeeProportionalAsync(reg, request.Amount, jobId, userId);

        // A recorded check is money in — activate the registration, mirroring the canonical CC
        // engine (PaymentService sets BActive=true on charge success). Pending pay-by-check siblings
        // start life bActive=0 on the Unassigned team; this is what puts them onto rosters/coach
        // views/division counts once their check lands. Corrections are fee adjustments, not tender,
        // so they do NOT activate. reg is tracked — RecordPaymentAndRecomputeAsync's save persists it.
        if (isCheck)
            reg.BActive = true;

        await _accountingRepo.RecordPaymentAndRecomputeAsync(entity, userId, ct);

        _logger.LogInformation("{Type} recorded: RegId={RegId}, Amount={Amount}, ProcessingFeeReduced={Reduced}, Activated={Activated}",
            request.PaymentType, request.RegistrationId, request.Amount, reg.FeeProcessing, isCheck);

        return new RegistrationCheckOrCorrectionResponse { Success = true };
    }

    public async Task<RegistrationCcChargeResponse> ChargeCcAsync(
        Guid jobId, string userId, RegistrationCcChargeRequest request, CancellationToken ct = default)
    {
        // Admin admin-charges a single registration at a time. The canonical engine
        // owns ownership/job validation, the per-method owed tripwire (ResolveOwed.Cc),
        // the placeholder-RA audit trail, the ADN call, and the success/failure-update.
        var items = new[] { new RegistrationChargeItem { RegistrationId = request.RegistrationId, Amount = request.Amount } };
        var result = await _paymentService.ChargeRegistrationsCcAsync(jobId, items, request.CreditCard, userId, ct);

        if (!result.Success)
        {
            return new RegistrationCcChargeResponse { Success = false, Error = result.Message ?? "Charge failed." };
        }
        return new RegistrationCcChargeResponse
        {
            Success = true,
            TransactionId = result.TransactionId,
            ChargedAmount = result.Outcomes.FirstOrDefault()?.ChargedAmount
        };
    }

    public async Task EditAccountingRecordAsync(
        Guid jobId, string userId, int aId, EditAccountingRecordRequest request, CancellationToken ct = default)
    {
        var record = await _accountingRepo.GetByAIdAsync(aId, ct)
            ?? throw new KeyNotFoundException($"Accounting record {aId} not found.");

        // Validate record belongs to a registration in this job
        if (record.Registration == null || record.Registration.JobId != jobId)
            throw new InvalidOperationException("Accounting record does not belong to this job.");

        record.Comment = request.Comment;
        record.CheckNo = request.CheckNo;
        record.Modified = DateTime.Now;
        record.LebUserId = userId;

        await _accountingRepo.SaveChangesAsync(ct);
    }

    public async Task<SubscriptionDetailDto?> GetSubscriptionDetailAsync(
        Guid jobId, Guid registrationId, CancellationToken ct = default)
    {
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct);
        if (reg == null || reg.JobId != jobId)
            return null;

        if (string.IsNullOrWhiteSpace(reg.AdnSubscriptionId))
            return null;

        try
        {
            // Read the subscription from the same ADN account that created it — sandbox on
            // Staging/Dev, production on Production. No-op on Production. A sandbox-origin
            // subscription cannot be resolved against the production account (and vice versa),
            // so the read env must match the create env (PaymentService.ProcessArbAsync).
            var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
            var env = _adnApi.GetADNEnvironment();

            _logger.LogInformation(
                "Fetching subscription from ADN: RegId={RegId}, SubscriptionId={SubId}, Env={Env}",
                registrationId, reg.AdnSubscriptionId, env);

            var details = _adnApi.GetSubscriptionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, reg.AdnSubscriptionId);

            if (details == null)
            {
                _logger.LogWarning("ADN GetSubscriptionDetails returned null for SubId={SubId}", reg.AdnSubscriptionId);
                return null;
            }

            if (details.messages?.resultCode != messageTypeEnum.Ok)
            {
                var errorMsg = details.messages?.message?.FirstOrDefault()?.text ?? "Unknown ADN error";
                _logger.LogWarning(
                    "ADN GetSubscriptionDetails failed: SubId={SubId}, ResultCode={Code}, Error={Error}",
                    reg.AdnSubscriptionId, details.messages?.resultCode, errorMsg);
                return null;
            }

            if (details.subscription == null)
            {
                _logger.LogWarning("ADN returned Ok but subscription object is null for SubId={SubId}", reg.AdnSubscriptionId);
                return null;
            }

            var sub = details.subscription;
            var intervalLength = sub.paymentSchedule?.interval?.length ?? 1;
            var intervalLabel = intervalLength == 1 ? "every month" : $"every {intervalLength} months";

            return new SubscriptionDetailDto
            {
                SubscriptionId = reg.AdnSubscriptionId,
                Status = sub.status.ToString(),
                PerOccurrenceAmount = sub.amount,
                TotalOccurrences = sub.paymentSchedule?.totalOccurrences ?? 0,
                TotalAmount = sub.amount * (sub.paymentSchedule?.totalOccurrences ?? 0),
                StartDate = sub.paymentSchedule?.startDate ?? DateTime.MinValue,
                IntervalLabel = intervalLabel
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load subscription for reg {RegId}, SubId={SubId}", registrationId, reg.AdnSubscriptionId);
            return null;
        }
    }

    public async Task CancelSubscriptionAsync(
        Guid jobId, string userId, Guid registrationId, CancellationToken ct = default)
    {
        var reg = await _registrationRepo.GetByIdAsync(registrationId, ct)
            ?? throw new KeyNotFoundException("Registration not found.");

        if (reg.JobId != jobId)
            throw new InvalidOperationException("Registration does not belong to this job.");

        if (string.IsNullOrWhiteSpace(reg.AdnSubscriptionId))
            throw new InvalidOperationException("Registration has no ARB subscription.");

        // Env-bound: cancel against the SAME account that created the subscription (sandbox
        // off-Production, production on Production). A prod-origin subscription is therefore not
        // cancellable from a non-Production host — by design, so a preview environment can never
        // cancel a real customer's recurring billing.
        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
        var env = _adnApi.GetADNEnvironment();

        var result = _adnApi.ADN_CancelSubscription(env, creds.AdnLoginId!, creds.AdnTransactionKey!, reg.AdnSubscriptionId);

        if (result?.messages?.resultCode != messageTypeEnum.Ok)
        {
            var err = result?.messages?.message?.FirstOrDefault()?.text ?? "Cancel failed.";
            throw new InvalidOperationException($"Failed to cancel subscription: {err}");
        }

        reg.AdnSubscriptionStatus = "canceled";
        reg.Modified = DateTime.Now;
        reg.LebUserId = userId;

        await _accountingRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Subscription canceled: RegId={RegId}, SubId={SubId}", registrationId, reg.AdnSubscriptionId);
    }

    /// <summary>
    /// Batch send: validates + seeds synchronously, then hands the work to the background
    /// <see cref="IEmailBatchService"/> engine and returns a job handle immediately. Recipient
    /// resolution + render run per-recipient on the engine's render-worker scopes; the engine owns
    /// opt-out suppression, the unsubscribe footer, retry, rate-limiting, and the audit row.
    /// </summary>
    /// <summary>Which source drives a batch-email recipient set. See <see cref="SelectBatchRecipientSource"/>.</summary>
    public enum BatchRecipientSource { ExplicitIds, Criteria }

    /// <summary>
    /// Pure recipient-selection rule (isolated for testability): a non-empty explicit id list is used
    /// SOLELY and any Criteria is ignored; otherwise recipients resolve from Criteria; supplying NEITHER
    /// is invalid and throws (fail closed — a mis-wired caller must never fall through to "everyone").
    /// </summary>
    public static BatchRecipientSource SelectBatchRecipientSource(
        IReadOnlyCollection<Guid>? registrationIds, RegistrationSearchRequest? criteria)
    {
        if (registrationIds is { Count: > 0 }) return BatchRecipientSource.ExplicitIds;
        if (criteria is not null) return BatchRecipientSource.Criteria;
        throw new InvalidOperationException("Batch email requires either explicit recipients or search criteria.");
    }

    public async Task<EmailBatchHandle> StartBatchEmailAsync(
        Guid jobId, string userId, BatchEmailRequest request, CancellationToken ct = default)
    {
        // Recipient set: an explicit id list is used SOLELY; otherwise resolve the full matching set
        // server-side from Criteria (how Email-All targets everyone without the client enumerating up
        // to 10K ids). Resolved unpaged. Selection rule (incl. fail-closed) is in SelectBatchRecipientSource.
        var ids = SelectBatchRecipientSource(request.RegistrationIds, request.Criteria) == BatchRecipientSource.ExplicitIds
            ? request.RegistrationIds
            : await _registrationRepo.GetMatchingRegistrationIdsAsync(jobId, request.Criteria!, ct);

        var registrations = await _registrationRepo.GetByIdsAsync(ids, ct);

        var invalidRegs = registrations.Where(r => r.JobId != jobId).ToList();
        if (invalidRegs.Count > 0)
            throw new InvalidOperationException("Some registrations do not belong to this job.");

        var jobConfirmation = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobPath = jobConfirmation?.JobPath ?? "";

        string? inviteTargetJobPath = null;
        string? inviteTargetJobName = null;
        DateTime? inviteExpires = null;
        if (request.InviteLinkTargetJobId.HasValue)
        {
            // One read yields both the target path (link URL) and the event name for the anchor text.
            // Use the full JobName ("Lax For the Cure:Summer 2027") — the same value !JOBNAME renders —
            // NOT DisplayName, which is the short brand label ("Lax For The Cure") and drops the season.
            var inviteTargetInfo = await _jobRepo.GetConfirmationEmailInfoAsync(request.InviteLinkTargetJobId.Value, ct);
            inviteTargetJobPath = inviteTargetInfo?.JobPath;
            inviteTargetJobName = inviteTargetInfo?.JobName;
            // One expiry instant for the whole batch, so every emailed invite states the same deadline
            // and its signed token expires at exactly that moment. Local time (codebase runs local AZ).
            // Default 24h when the caller doesn't specify (matches the modal's default selection).
            inviteExpires = DateTime.Now.AddHours(request.InviteExpiryHours is > 0 ? request.InviteExpiryHours.Value : 24);
        }

        // Render-win #2: load the job-invariant token slice ONCE (Jobs/Customers/Sports/DisplayOptions)
        // and let every recipient's render reuse it, so the per-recipient query drops those four joins.
        // Captured by the render closure below; it's plain immutable data, safe across worker scopes.
        var jobFields = await _textSubstitution.LoadJobInvariantFieldsAsync(jobId, ct);

        // Snapshot identities now (in-memory); SeedAsync filters opt-out without a second query.
        var allItems = registrations
            .Select(r => new BatchEmailItem(r.RegistrationId, r.FamilyUserId, r.RoleId, r.RegistrationAi, r.BemailOptOut))
            .ToList();

        // Recipient resolution, batch-loaded ONCE. The per-recipient version ran 1-2 tracked Include
        // queries each (Families + GetByJobAndFamilyWithUsers), saturating SQL and crawling a 10K test
        // to ~1/sec. Two bulk AsNoTracking reads here turn the render loop's address lookup into pure
        // in-memory dictionary hits. emailByRegId = each registrant's own email; familyEmailsById =
        // parent emails for player recipients only.
        var emailByRegId = (await _registrationRepo.GetRecipientEmailsByIdsAsync(ids, ct))
            .GroupBy(r => r.RegistrationId)
            .ToDictionary(g => g.Key, g => g.First().Email);
        var playerFamilyIds = allItems
            .Where(i => i.RoleId == RoleConstants.Player && !string.IsNullOrWhiteSpace(i.FamilyUserId))
            .Select(i => i.FamilyUserId!)
            .ToList();
        var familyEmailsById = (await _familiesRepo.GetByFamilyUserIdsAsync(playerFamilyIds, ct))
            .GroupBy(f => f.FamilyUserId)
            .ToDictionary(g => g.Key, g => g.First());

        // `required` guarantees the property is PRESENT, not non-null — an explicit JSON null still
        // binds. Coalesce once here so the audit row and every render see a real string.
        var subject = request.Subject ?? "";
        var body = request.BodyTemplate ?? "";
        // From display = the public job/org label (Jobs.DisplayName, falling back to JobName). The From
        // ADDRESS is forced to the SES-verified identity downstream; this is only what recipients see.
        var fromName = jobConfirmation?.DisplayName ?? jobConfirmation?.JobName;

        // Reply-To = the logged-in admin who sent the batch, so replies reach a person, not support@.
        // (Legacy parity: SearchController put the sender on From+ReplyTo; SES forces From to support@,
        // so the sender identity now lives solely on Reply-To.)
        var sender = await _userRepo.GetByIdAsync(userId, ct);
        var replyToAddress = sender?.Email;
        var replyToName = $"{sender?.FirstName} {sender?.LastName}".Trim();

        // Render-win #3: the per-recipient fixed-fields load takes the family path (a Registrations
        // scan filtered by FamilyUserId — ~70ms each, the true batch bottleneck) ONLY to satisfy
        // family-aggregation tokens (!F-PLAYERS, !F-ACCOUNTING, ...). When the template uses none of
        // them, pass familyUserId="" so the load is a registrationId PK seek (~0ms) — same per-recipient
        // tokens, no scan. Computed once for the whole batch (the template is job-invariant).
        var templateNeedsFamily =
            subject.Contains("!F-", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("!F-", StringComparison.OrdinalIgnoreCase);

        var plan = new EmailBatchPlan<BatchEmailItem>
        {
            SeedAsync = (_, _) => Task.FromResult(new EmailBatchSeed<BatchEmailItem>
            {
                Items = allItems // engine applies opt-out via IsOptedOut below
            }),
            IsOptedOut = i => i.OptedOut,
            DescribeItem = i => $"(no email for RegistrationAi #{i.RegistrationAi})",
            Audit = new EmailBatchAudit
            {
                JobId = jobId,
                SenderUserId = userId,
                Subject = subject,
                BodyTemplate = body,
                SendFrom = fromName
            },
            RenderAsync = async (item, sp, token) =>
            {
                var textSub = sp.GetRequiredService<ITextSubstitutionService>();

                var toAddresses = BatchEmailRecipientFilter.ResolveRecipients(
                    item.RoleId, item.FamilyUserId, item.RegistrationId, emailByRegId, familyEmailsById);
                if (toAddresses.Count == 0) return null;

                // Single-pass render (one fixed-fields load for subject + body; skips load when token-less).
                // Render-win #2: hand in the once-loaded job slice so this recipient's load is the light 3-join.
                // Render-win #3: only feed familyUserId when the template needs family tokens; otherwise ""
                // routes the load to the registrationId PK seek instead of the FamilyUserId scan.
                var renderFamilyUserId = templateNeedsFamily ? (item.FamilyUserId ?? "") : "";
                var (renderedSubject, renderedBody) = await textSub.SubstituteSubjectAndBodyAsync(
                    jobPath, jobId, CcPaymentMethodId, item.RegistrationId, renderFamilyUserId, subject, body,
                    inviteTargetJobPath, inviteTargetJobName, request.InviteLinkTargetJobId, inviteExpires, jobFields: jobFields);

                return new EmailBatchRendered
                {
                    Message = new EmailMessageDto
                    {
                        // From address is forced to the SES-verified identity at the send chokepoint;
                        // FromName is the display label (job DisplayName) and ReplyTo carries the admin.
                        FromName = fromName,
                        ReplyToName = replyToName,
                        ReplyToAddress = replyToAddress,
                        Subject = renderedSubject,
                        HtmlBody = renderedBody,
                        ToAddresses = toAddresses
                    },
                    UnsubscribeRegId = item.RegistrationId // engine appends the standard footer
                };
            }
        };

        var simulating = request.SimulatedPerUnitDelayMs.HasValue;
        var options = new EmailBatchOptions
        {
            SimulatedPerUnitDelayMs = request.SimulatedPerUnitDelayMs,
            SyntheticFailEveryN = simulating ? 13 : null,
            // Staging-only test inbox from the invite modal. Honored only in a sandbox host
            // (the send step re-checks IsSandbox()); harmless/ignored everywhere else.
            SandboxTestRecipient = request.SandboxTestRecipient,
            // Render parallelism. Each worker now owns a fresh DI scope/DbContext PER ITEM, so N>1 is
            // safe (no shared-context concurrency) — the old "serial by construction" default for real
            // sends was an unnecessary footgun, not a safety requirement. The TEST run renders the full
            // real set with no send delay, so render is its whole cost: go wide (16). Real sends are
            // governed downstream by the SES MaxSendRate cap on the send stage, so render only needs to
            // stay ahead of that — a modest 4 (with PK-seek loads, ~thousands/sec) never bottlenecks.
            RenderWorkers = simulating ? 16 : 4
        };

        return await _emailBatch.StartAsync(plan, options, ct);
    }

    /// <summary>Lightweight, context-free identity for a batch recipient (no EF entity captured).</summary>
    private sealed record BatchEmailItem(
        Guid RegistrationId, string? FamilyUserId, string? RoleId, int RegistrationAi, bool OptedOut);

    public async Task<EmailPreviewResponse> PreviewEmailAsync(
        Guid jobId, EmailPreviewRequest request, CancellationToken ct = default)
    {
        // Same recipient rule as the send (explicit ids win), but for Email-All (criteria-only) we
        // only need a few representative recipients to render the token preview — never thousands.
        var previewIds = request.RegistrationIds is { Count: > 0 }
            ? request.RegistrationIds
            : request.Criteria is not null
                ? (await _registrationRepo.GetMatchingRegistrationIdsAsync(jobId, request.Criteria, ct)).Take(3).ToList()
                : new List<Guid>();
        var registrations = await _registrationRepo.GetByIdsAsync(previewIds, ct);
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
        reg.Modified = DateTime.Now;
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
        else if (string.Equals(regRoleName, RoleConstants.Names.ClubRepName, StringComparison.OrdinalIgnoreCase))
        {
            // Club Rep → only Superuser can delete
            if (!string.Equals(callerRole, RoleConstants.Names.SuperuserName, StringComparison.OrdinalIgnoreCase))
                return new DeleteRegistrationResponse { Success = false, Message = "Only Superuser can delete Club Rep registrations." };

            // FK guard: Teams.ClubrepRegistrationid references this registration. A delete would
            // violate referential integrity while any team (active or inactive) still points to it.
            var teamCount = await _teamRepo.CountTeamsByClubRepRegistrationAsync(registrationId, ct);
            if (teamCount > 0)
            {
                var plural = teamCount == 1 ? "" : "s";
                return new DeleteRegistrationResponse
                {
                    Success = false,
                    Message = $"Cannot delete: this club rep has {teamCount} team{plural} attached. Reassign or remove the team{plural} first."
                };
            }
        }
        else if (!string.Equals(regRoleName, RoleConstants.Names.PlayerName, StringComparison.OrdinalIgnoreCase)
              && !string.Equals(regRoleName, RoleConstants.Names.StaffName, StringComparison.OrdinalIgnoreCase))
        {
            // Only Player, Staff, Unassigned Adult, and Club Rep roles are deletable
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

    public async Task SetEmailOptOutAsync(Guid jobId, Guid registrationId, bool optOut, CancellationToken ct = default)
    {
        // Validate registration belongs to this job
        var regs = await _registrationRepo.GetByIdsAsync(new List<Guid> { registrationId }, ct);
        var reg = regs.FirstOrDefault();
        if (reg == null)
            throw new KeyNotFoundException($"Registration {registrationId} not found.");
        if (reg.JobId != jobId)
            throw new InvalidOperationException("Registration does not belong to this job.");

        await _registrationRepo.SetEmailOptOutAsync(registrationId, optOut, ct);
    }

    public async Task SetActiveAsync(Guid jobId, Guid registrationId, bool active, CancellationToken ct = default)
    {
        var regs = await _registrationRepo.GetByIdsAsync(new List<Guid> { registrationId }, ct);
        var reg = regs.FirstOrDefault();
        if (reg == null)
            throw new KeyNotFoundException($"Registration {registrationId} not found.");
        if (reg.JobId != jobId)
            throw new InvalidOperationException("Registration does not belong to this job.");

        await _registrationRepo.SetActiveAsync(registrationId, active, ct);
    }
}
