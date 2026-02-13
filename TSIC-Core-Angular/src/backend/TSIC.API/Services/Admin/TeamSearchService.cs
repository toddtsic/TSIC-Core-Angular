using AuthorizeNet.Api.Contracts.V1;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the Team Search admin tool.
/// Handles team search, detail view/edit, and all accounting operations
/// at the individual transaction, team, and cross-club levels.
///
/// Payment spreading logic ported from legacy IPaymentService:
///   RecordTx_ClubRep_ChargeCC          (lines 833-1045)
///   RecordTx_ClubRep_RefundCC          (lines 1047-1299)
///   RecordTx_ClubRep_CheckOrCorrection (lines 1301-1553)
/// </summary>
public sealed class TeamSearchService : ITeamSearchService
{
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationAccountingRepository _accountingRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IAdnApiService _adnApi;
    private readonly ILogger<TeamSearchService> _logger;

    // Known payment method GUIDs (from AccountingPaymentMethods table)
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CcCreditMethodId = Guid.Parse("31ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CheckMethodId = Guid.Parse("32ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CorrectionMethodId = Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D");

    public TeamSearchService(
        ITeamRepository teamRepo,
        IRegistrationAccountingRepository accountingRepo,
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo,
        IAdnApiService adnApi,
        ILogger<TeamSearchService> logger)
    {
        _teamRepo = teamRepo;
        _accountingRepo = accountingRepo;
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
        _adnApi = adnApi;
        _logger = logger;
    }

    // ── Search & Filters ──

    public async Task<TeamSearchResponse> SearchAsync(
        Guid jobId, TeamSearchRequest request, CancellationToken ct = default)
    {
        var results = await _teamRepo.SearchTeamsAsync(jobId, request, ct);
        return new TeamSearchResponse
        {
            Result = results,
            Count = results.Count,
            TotalPaid = results.Sum(r => r.PaidTotal),
            TotalOwed = results.Sum(r => r.OwedTotal)
        };
    }

    public async Task<TeamFilterOptionsDto> GetFilterOptionsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _teamRepo.GetTeamSearchFilterOptionsAsync(jobId, ct);
    }

    // ── Team Detail ──

    public async Task<TeamSearchDetailDto?> GetTeamDetailAsync(
        Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        var detail = await _teamRepo.GetTeamDetailAsync(teamId, ct);
        if (detail == null || detail.JobId != jobId) return null;

        var accountingRecords = await _accountingRepo.GetByTeamIdAsync(teamId, ct);

        var clubTeamSummaries = detail.ClubRepRegistrationId.HasValue
            ? await _teamRepo.GetClubTeamSummariesAsync(jobId, detail.ClubRepRegistrationId.Value, ct)
            : new List<ClubTeamSummaryDto>();

        return new TeamSearchDetailDto
        {
            TeamId = detail.TeamId,
            TeamName = detail.TeamName,
            ClubName = detail.ClubName,
            AgegroupName = detail.AgegroupName,
            DivName = detail.DivName,
            LevelOfPlay = detail.LevelOfPlay,
            Active = detail.Active,
            FeeBase = detail.FeeBase,
            FeeProcessing = detail.FeeProcessing,
            FeeTotal = detail.FeeTotal,
            PaidTotal = detail.PaidTotal,
            OwedTotal = detail.OwedTotal,
            TeamComments = detail.TeamComments,
            ClubRepRegistrationId = detail.ClubRepRegistrationId,
            ClubRepName = detail.ClubRepName,
            ClubRepEmail = detail.ClubRepEmail,
            ClubRepCellphone = detail.ClubRepCellphone,
            AccountingRecords = accountingRecords,
            ClubTeamSummaries = clubTeamSummaries
        };
    }

    public async Task EditTeamAsync(
        Guid teamId, Guid jobId, string userId, EditTeamRequest request, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct)
            ?? throw new InvalidOperationException("Team not found.");

        if (team.JobId != jobId)
            throw new InvalidOperationException("Team does not belong to this job.");

        if (request.TeamName != null) team.TeamName = request.TeamName;
        if (request.Active.HasValue) team.Active = request.Active.Value;
        if (request.LevelOfPlay != null) team.LevelOfPlay = request.LevelOfPlay;
        if (request.TeamComments != null) team.TeamComments = request.TeamComments;

        team.Modified = DateTime.UtcNow;
        team.LebUserId = userId;

        await _teamRepo.SaveChangesAsync(ct);
    }

    // ── CC Refund (individual transaction) ──
    // Legacy: RecordTx_ClubRep_RefundCC (lines 1047-1299)

    public async Task<RefundResponse> ProcessRefundAsync(
        Guid jobId, string userId, RefundRequest request, CancellationToken ct = default)
    {
        var original = await _accountingRepo.GetByAIdAsync(request.AccountingRecordId, ct);
        if (original == null)
            return new RefundResponse { Success = false, Message = "Accounting record not found." };

        if (string.IsNullOrWhiteSpace(original.AdnTransactionId))
            return new RefundResponse { Success = false, Message = "No Authorize.Net transaction ID — cannot refund." };

        var originalPay = original.Payamt ?? 0;
        if (request.RefundAmount <= 0 || request.RefundAmount > originalPay)
            return new RefundResponse { Success = false, Message = $"Refund amount must be between $0.01 and ${originalPay:F2}." };

        try
        {
            var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
            var env = _adnApi.GetADNEnvironment();

            // Check original transaction status to determine void vs refund
            var txDetails = _adnApi.ADN_GetTransactionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, original.AdnTransactionId);

            if (txDetails?.messages?.resultCode != messageTypeEnum.Ok)
                return new RefundResponse { Success = false, Message = "Could not look up original transaction details." };

            var txStatus = txDetails.transaction?.transactionStatus;
            string refundTransId;

            if (txStatus == "capturedPendingSettlement")
            {
                // VOID the transaction (full amount)
                var voidResult = _adnApi.ADN_Void(new AdnVoidRequest
                {
                    Env = env,
                    LoginId = creds.AdnLoginId ?? "",
                    TransactionKey = creds.AdnTransactionKey ?? "",
                    TransactionId = original.AdnTransactionId
                });

                if (voidResult?.messages?.resultCode != messageTypeEnum.Ok || voidResult.transactionResponse?.messages == null)
                {
                    var err = voidResult?.transactionResponse?.errors?.FirstOrDefault()?.errorText ?? "Void failed.";
                    return new RefundResponse { Success = false, Message = $"CC Void failed: {err}" };
                }

                refundTransId = voidResult.transactionResponse.transId ?? "";

                // Void reverses the full original amount
                original.Paymeth = (original.Paymeth ?? "") + $" VOIDED {DateTime.UtcNow}";
                original.Payamt = 0;
            }
            else if (txStatus == "settledSuccessfully")
            {
                // REFUND the transaction (partial or full)
                var refundResult = _adnApi.ADN_Refund(new AdnRefundRequest
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

                if (refundResult?.messages?.resultCode != messageTypeEnum.Ok || refundResult.transactionResponse?.messages == null)
                {
                    var err = refundResult?.transactionResponse?.errors?.FirstOrDefault()?.errorText ?? "Refund failed.";
                    return new RefundResponse { Success = false, Message = $"CC Refund failed: {err}" };
                }

                refundTransId = refundResult.transactionResponse.transId ?? "";

                // Create a negative accounting record for the refund
                _accountingRepo.Add(new RegistrationAccounting
                {
                    RegistrationId = original.RegistrationId,
                    TeamId = original.TeamId,
                    PaymentMethodId = CcCreditMethodId,
                    Payamt = -request.RefundAmount,
                    Dueamt = 0,
                    Comment = request.Reason ?? "Refund processed",
                    AdnTransactionId = refundTransId,
                    AdnCc4 = original.AdnCc4,
                    AdnCcexpDate = original.AdnCcexpDate,
                    AdnInvoiceNo = original.AdnInvoiceNo,
                    Active = true,
                    Createdate = DateTime.UtcNow,
                    Modified = DateTime.UtcNow,
                    LebUserId = userId
                });
            }
            else
            {
                return new RefundResponse { Success = false, Message = $"Transaction status '{txStatus}' does not support refund/void." };
            }

            // Update team financials
            if (original.TeamId.HasValue)
            {
                var team = await _teamRepo.GetTeamFromTeamId(original.TeamId.Value, ct);
                if (team != null)
                {
                    var refundAmt = txStatus == "capturedPendingSettlement"
                        ? (original.Dueamt ?? 0)   // void reverses full original
                        : request.RefundAmount;     // refund reverses requested amount

                    team.PaidTotal = (team.PaidTotal ?? 0) - refundAmt;
                    team.OwedTotal = (team.OwedTotal ?? 0) + refundAmt;
                }
            }

            // Update club rep financials
            if (original.RegistrationId.HasValue)
            {
                await _registrationRepo.SynchronizeClubRepFinancialsAsync(original.RegistrationId.Value, userId, ct);
            }

            await _accountingRepo.SaveChangesAsync(ct);

            _logger.LogInformation("Team refund processed: AId={AId}, Amount={Amount}, TransId={TransId}",
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
            _logger.LogError(ex, "Team refund failed for AId={AId}", request.AccountingRecordId);
            return new RefundResponse { Success = false, Message = $"Refund failed: {ex.Message}" };
        }
    }

    // ── CC Charge (team-level) ──
    // Legacy: RecordTx_ClubRep_ChargeCC (lines 833-1045)

    public async Task<TeamCcChargeResponse> ChargeCcForTeamAsync(
        Guid jobId, string userId, TeamCcChargeRequest request, CancellationToken ct = default)
    {
        if (!request.TeamId.HasValue)
            return new TeamCcChargeResponse { Success = false, Error = "TeamId is required for team-level charge." };

        return await ChargeCcInternalAsync(jobId, userId, request.ClubRepRegistrationId, request.CreditCard,
            new List<Guid> { request.TeamId.Value }, ct);
    }

    // ── CC Charge (club-level) ──

    public async Task<TeamCcChargeResponse> ChargeCcForClubAsync(
        Guid jobId, string userId, TeamCcChargeRequest request, CancellationToken ct = default)
    {
        // Get all active club teams with owed balance
        var clubTeams = await _teamRepo.GetActiveClubTeamsOrderedByOwedAsync(jobId, request.ClubRepRegistrationId, ct);
        var teamIds = clubTeams.Where(t => (t.OwedTotal ?? 0) > 0).Select(t => t.TeamId).ToList();

        if (teamIds.Count == 0)
            return new TeamCcChargeResponse { Success = false, Error = "No club teams have outstanding balances." };

        return await ChargeCcInternalAsync(jobId, userId, request.ClubRepRegistrationId, request.CreditCard, teamIds, ct);
    }

    private async Task<TeamCcChargeResponse> ChargeCcInternalAsync(
        Guid jobId, string userId, Guid clubRepRegistrationId, CreditCardInfo cc,
        List<Guid> teamIds, CancellationToken ct)
    {
        try
        {
            var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
            var env = _adnApi.GetADNEnvironment();

            // Load teams with Job→Customer navigation for invoice number generation
            var teamsWithNav = await _teamRepo.GetTeamsWithJobAndCustomerAsync(jobId, teamIds, ct);
            var allocations = new List<TeamPaymentAllocation>();

            foreach (var team in teamsWithNav)
            {
                if ((team.OwedTotal ?? 0) <= 0) continue;

                var chargeAmount = team.OwedTotal ?? 0;
                var customerAi = team.Job?.Customer?.CustomerAi ?? 0;
                var jobAi = team.Job?.JobAi ?? 0;
                var invoiceNumber = $"{customerAi}_{jobAi}_{team.TeamAi}";
                if (invoiceNumber.Length > 20) invoiceNumber = invoiceNumber[..20];

                // Create incomplete RA record first
                var raRecord = new RegistrationAccounting
                {
                    Active = true,
                    AdnCc4 = cc.Number?[^4..],
                    AdnCcexpDate = cc.Expiry,
                    Createdate = DateTime.UtcNow,
                    Dueamt = chargeAmount,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow,
                    PaymentMethodId = CcPaymentMethodId,
                    RegistrationId = clubRepRegistrationId,
                    TeamId = team.TeamId
                };
                _accountingRepo.Add(raRecord);
                await _accountingRepo.SaveChangesAsync(ct);

                // Charge the card
                var chargeResult = _adnApi.ADN_Charge(new AdnChargeRequest
                {
                    Env = env,
                    LoginId = creds.AdnLoginId ?? "",
                    TransactionKey = creds.AdnTransactionKey ?? "",
                    CardNumber = cc.Number ?? "",
                    CardCode = cc.Code ?? "",
                    Expiry = cc.Expiry ?? "",
                    FirstName = cc.FirstName ?? "",
                    LastName = cc.LastName ?? "",
                    Address = cc.Address ?? "",
                    Zip = cc.Zip ?? "",
                    Email = cc.Email ?? "",
                    Phone = cc.Phone ?? "",
                    Amount = chargeAmount,
                    InvoiceNumber = invoiceNumber,
                    Description = $"Team payment: {team.TeamName}"
                });

                var success = chargeResult?.messages?.resultCode == messageTypeEnum.Ok
                    && chargeResult.transactionResponse?.messages != null;

                if (success)
                {
                    raRecord.AdnInvoiceNo = invoiceNumber;
                    raRecord.AdnTransactionId = chargeResult!.transactionResponse!.transId;
                    raRecord.Payamt = chargeAmount;
                    raRecord.Paymeth = $"paid by cc: {chargeAmount:C} on {DateTime.UtcNow:G} txID: {chargeResult.transactionResponse.transId}";
                    raRecord.Modified = DateTime.UtcNow;

                    team.PaidTotal = (team.PaidTotal ?? 0) + chargeAmount;
                    team.OwedTotal = (team.OwedTotal ?? 0) - chargeAmount;

                    await _accountingRepo.SaveChangesAsync(ct);

                    allocations.Add(new TeamPaymentAllocation
                    {
                        TeamId = team.TeamId,
                        TeamName = team.TeamName ?? "",
                        AllocatedAmount = chargeAmount,
                        ProcessingFeeReduction = 0,
                        NewOwedTotal = team.OwedTotal ?? 0
                    });
                }
                else
                {
                    var errMsg = chargeResult?.transactionResponse?.errors?.FirstOrDefault()?.errorText ?? "CC charge failed.";
                    raRecord.Comment = errMsg;
                    raRecord.Payamt = 0;
                    raRecord.Active = false;
                    raRecord.Paymeth = errMsg;
                    raRecord.Modified = DateTime.UtcNow;
                    await _accountingRepo.SaveChangesAsync(ct);

                    // Sync financials for any partial success and return error
                    await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRepRegistrationId, userId, ct);
                    return new TeamCcChargeResponse
                    {
                        Success = false,
                        Error = $"CC charge failed for {team.TeamName}: {errMsg}",
                        PerTeamResults = allocations
                    };
                }
            }

            await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRepRegistrationId, userId, ct);

            return new TeamCcChargeResponse { Success = true, PerTeamResults = allocations };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CC charge failed for club rep {RegId}", clubRepRegistrationId);
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRepRegistrationId, userId, ct);
            return new TeamCcChargeResponse { Success = false, Error = $"CC charge error: {ex.Message}" };
        }
    }

    // ── Check/Correction (team-level) ──

    public async Task<TeamCheckOrCorrectionResponse> RecordCheckForTeamAsync(
        Guid jobId, string userId, TeamCheckOrCorrectionRequest request, CancellationToken ct = default)
    {
        if (!request.TeamId.HasValue)
            return new TeamCheckOrCorrectionResponse { Success = false, Error = "TeamId is required for team-level payment." };

        return await RecordCheckOrCorrectionInternalAsync(jobId, userId, request, singleTeamId: request.TeamId.Value, ct);
    }

    // ── Check/Correction (club-level) ──
    // Legacy: RecordTx_ClubRep_CheckOrCorrection (lines 1301-1553)

    public async Task<TeamCheckOrCorrectionResponse> RecordCheckForClubAsync(
        Guid jobId, string userId, TeamCheckOrCorrectionRequest request, CancellationToken ct = default)
    {
        return await RecordCheckOrCorrectionInternalAsync(jobId, userId, request, singleTeamId: null, ct);
    }

    /// <summary>
    /// Core check/correction recording logic.
    /// When singleTeamId is null, distributes across all active club teams ordered by OwedTotal DESC.
    /// Ported from legacy RecordTx_ClubRep_CheckOrCorrection (lines 1301-1553).
    /// </summary>
    private async Task<TeamCheckOrCorrectionResponse> RecordCheckOrCorrectionInternalAsync(
        Guid jobId, string userId, TeamCheckOrCorrectionRequest request,
        Guid? singleTeamId, CancellationToken ct)
    {
        try
        {
            var isCheck = string.Equals(request.PaymentType, "Check", StringComparison.OrdinalIgnoreCase);
            var paymentMethodId = isCheck ? CheckMethodId : CorrectionMethodId;

            // Load club rep registration (tracked via FindAsync for in-place mutation)
            var clubRep = await _registrationRepo.GetByIdAsync(request.ClubRepRegistrationId, ct);
            if (clubRep == null)
                return new TeamCheckOrCorrectionResponse { Success = false, Error = "Club rep registration not found." };

            // Validate payment doesn't exceed owed
            if (request.Amount > clubRep.OwedTotal)
                return new TeamCheckOrCorrectionResponse { Success = false, Error = $"Payment ({request.Amount:C}) exceeds amount owed ({clubRep.OwedTotal:C})." };

            // Get job processing fee config
            var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
            var bAddProcessingFees = feeSettings?.BAddProcessingFees ?? false;
            var bApplyToDeposit = feeSettings?.BApplyProcessingFeesToTeamDeposit ?? false;
            var bFullPayRequired = feeSettings?.BTeamsFullPaymentRequired ?? false;
            var processingFeePercent = await _jobRepo.GetProcessingFeePercentAsync(jobId, ct) ?? 0;

            // Get teams — single or all club teams
            var clubTeams = await _teamRepo.GetActiveClubTeamsOrderedByOwedAsync(jobId, request.ClubRepRegistrationId, ct);
            if (singleTeamId.HasValue)
                clubTeams = clubTeams.Where(t => t.TeamId == singleTeamId.Value).ToList();

            var allocations = new List<TeamPaymentAllocation>();
            var remainingBalance = request.Amount;

            foreach (var team in clubTeams)
            {
                if (remainingBalance <= 0) break;

                // Legacy algorithm: calculate team's check amount based on deposit/full-payment rules
                // (lines 1440-1454 in legacy IPaymentService)
                var rosterFee = team.Agegroup?.RosterFee ?? 0;
                var teamFee = team.Agegroup?.TeamFee ?? 0;

                decimal calculatedTeamCheckAmount;
                if ((team.PaidTotal ?? 0) >= rosterFee + teamFee)
                {
                    calculatedTeamCheckAmount = 0; // Fully paid
                }
                else if ((team.PaidTotal ?? 0) >= rosterFee)
                {
                    calculatedTeamCheckAmount = bFullPayRequired ? teamFee : 0;
                }
                else
                {
                    calculatedTeamCheckAmount = bFullPayRequired ? teamFee + rosterFee : rosterFee;
                }

                // Cap at payment amount and remaining balance
                if (calculatedTeamCheckAmount > request.Amount) calculatedTeamCheckAmount = request.Amount;
                if (calculatedTeamCheckAmount > remainingBalance) calculatedTeamCheckAmount = remainingBalance;
                if (calculatedTeamCheckAmount <= 0) continue;

                // Processing fee reduction (lines 1472-1514 in legacy)
                decimal processingFeeReduction = 0;
                if (bAddProcessingFees && (team.OwedTotal ?? 0) > calculatedTeamCheckAmount)
                {
                    if (!bApplyToDeposit)
                    {
                        if (bFullPayRequired)
                        {
                            // No processing fees on check payment at all
                            processingFeeReduction = (decimal)(team.FeeProcessing ?? 0);
                        }
                        else
                        {
                            processingFeeReduction = decimal.Round(
                                processingFeePercent * (calculatedTeamCheckAmount - rosterFee),
                                2, MidpointRounding.AwayFromZero);
                        }
                    }
                    else
                    {
                        processingFeeReduction = decimal.Round(
                            processingFeePercent * calculatedTeamCheckAmount,
                            2, MidpointRounding.AwayFromZero);
                    }

                    if (processingFeeReduction > 0 && (team.OwedTotal ?? 0) > 0 && (team.FeeProcessing ?? 0) >= processingFeeReduction)
                    {
                        team.FeeProcessing = (team.FeeProcessing ?? 0) - processingFeeReduction;
                        team.OwedTotal = (team.OwedTotal ?? 0) - processingFeeReduction;
                        team.FeeTotal = (team.FeeTotal ?? 0) - processingFeeReduction;

                        clubRep.FeeProcessing -= processingFeeReduction;
                        clubRep.OwedTotal -= processingFeeReduction;
                        clubRep.FeeTotal -= processingFeeReduction;
                    }
                    else
                    {
                        processingFeeReduction = 0;
                    }

                    await _accountingRepo.SaveChangesAsync(ct);
                }

                remainingBalance -= calculatedTeamCheckAmount;

                // Create accounting record
                _accountingRepo.Add(new RegistrationAccounting
                {
                    Active = true,
                    CheckNo = request.CheckNo,
                    Comment = request.Comment,
                    Createdate = DateTime.UtcNow,
                    Dueamt = calculatedTeamCheckAmount,
                    Payamt = calculatedTeamCheckAmount,
                    PaymentMethodId = paymentMethodId,
                    TeamId = team.TeamId,
                    RegistrationId = clubRep.RegistrationId,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });

                team.PaidTotal = (team.PaidTotal ?? 0) + calculatedTeamCheckAmount;
                team.OwedTotal = (team.OwedTotal ?? 0) - calculatedTeamCheckAmount;
                clubRep.PaidTotal += calculatedTeamCheckAmount;
                clubRep.OwedTotal -= calculatedTeamCheckAmount;

                await _accountingRepo.SaveChangesAsync(ct);
                await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRep.RegistrationId, userId, ct);

                allocations.Add(new TeamPaymentAllocation
                {
                    TeamId = team.TeamId,
                    TeamName = team.TeamName ?? "",
                    AllocatedAmount = calculatedTeamCheckAmount,
                    ProcessingFeeReduction = processingFeeReduction,
                    NewOwedTotal = team.OwedTotal ?? 0
                });
            }

            return new TeamCheckOrCorrectionResponse { Success = true, PerTeamAllocations = allocations };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Check/correction failed for club rep {RegId}", request.ClubRepRegistrationId);
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(request.ClubRepRegistrationId, userId, ct);
            return new TeamCheckOrCorrectionResponse { Success = false, Error = $"Payment error: {ex.Message}" };
        }
    }

    // ── Shared ──

    public async Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default)
    {
        return await _accountingRepo.GetPaymentMethodOptionsAsync(ct);
    }
}
