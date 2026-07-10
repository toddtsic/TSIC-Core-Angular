using AuthorizeNet.Api.Contracts.V1;
using TSIC.API.Services.Payments;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Teams;
using TSIC.Contracts.Constants;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Ladt;
using TSIC.Contracts.Dtos.RegistrationSearch;
using TSIC.Contracts.Dtos.Scheduling;
using TSIC.Contracts.Dtos.TeamSearch;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Extensions;
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
    private readonly IFeeResolutionService _feeService;
    private readonly IPaymentStateService _paymentState;
    private readonly IAdnApiService _adnApi;
    private readonly ILadtService _ladtService;
    private readonly IEmailService _emailService;
    private readonly IPaymentService _paymentService;
    private readonly IRegisteredTeamShaper _shaper;
    private readonly ILogger<TeamSearchService> _logger;

    // Known payment method GUIDs (from AccountingPaymentMethods table)
    private static readonly Guid CcCreditMethodId = Guid.Parse("31ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CheckMethodId = Guid.Parse("32ECA575-A268-E111-9D56-F04DA202060D");
    private static readonly Guid CorrectionMethodId = Guid.Parse("33ECA575-A268-E111-9D56-F04DA202060D");

    public TeamSearchService(
        ITeamRepository teamRepo,
        IRegistrationAccountingRepository accountingRepo,
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo,
        IFeeResolutionService feeService,
        IPaymentStateService paymentState,
        IAdnApiService adnApi,
        ILadtService ladtService,
        IEmailService emailService,
        IPaymentService paymentService,
        IRegisteredTeamShaper shaper,
        ILogger<TeamSearchService> logger)
    {
        _teamRepo = teamRepo;
        _accountingRepo = accountingRepo;
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
        _feeService = feeService;
        _paymentState = paymentState;
        _adnApi = adnApi;
        _ladtService = ladtService;
        _emailService = emailService;
        _paymentService = paymentService;
        _shaper = shaper;
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

    public async Task<List<CadtClubNode>> GetCadtTreeAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _registrationRepo.GetCadtTreeForJobAsync(jobId, ct);
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

        // CheckFeeReduction = proc that would NOT be charged if remainder paid by check.
        // Derived from canonical PaymentState (handles CC reverse-out + eCheck proc),
        // not the OwedTotal/(1+rate) shortcut which breaks once any CC payment has hit.
        var clubTeamIds = clubTeamSummaries.Select(t => t.TeamId).ToList();
        var clubTeamStates = await _paymentState.ForTeamsAsync(clubTeamIds, jobId, ct);
        var emptyClubState = await BuildEmptyPaymentStateAsync(jobId, ct);
        clubTeamSummaries = clubTeamSummaries.Select(t =>
        {
            var state = clubTeamStates.GetValueOrDefault(t.TeamId, emptyClubState);
            // Canonical per-method owed from the single resolver. CkOwedTotal is the
            // check/correction owed (CC owed minus the capped proc credit) — exactly what
            // recording a check will settle. CC owed is OwedTotal itself.
            // donation: 0m — ClubTeamSummaryDto display; a paid-in-full donation nets out of the
            // resolver's principal-remaining (it lands in both base and PrincipalPaid).
            var owed = state.ResolveOwed(t.OwedTotal, t.FeeBase, t.FeeDiscount, t.FeeLatefee, donation: 0m, t.FeeProcessing);
            return t with
            {
                CkOwedTotal = owed.Check
            };
        }).ToList();

        var lopOptions = await GetLopOptionsAsync(jobId, ct);
        var distinctClubCount = await _teamRepo.GetDistinctClubCountAsync(jobId, ct);

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
            LopOptions = lopOptions,
            JobDistinctClubCount = distinctClubCount,
            ClubRepRegistrationId = detail.ClubRepRegistrationId,
            ClubRepName = detail.ClubRepName,
            ClubRepEmail = detail.ClubRepEmail,
            ClubRepCellphone = detail.ClubRepCellphone,
            ClubRepStreetAddress = detail.ClubRepStreetAddress,
            ClubRepCity = detail.ClubRepCity,
            ClubRepState = detail.ClubRepState,
            ClubRepPostalCode = detail.ClubRepPostalCode,
            AccountingRecords = accountingRecords,
            ClubTeamSummaries = clubTeamSummaries,
            PaymentScheduled = detail.PaymentScheduled,
            NextChargeDate = detail.NextChargeDate,
            PaymentFlagged = detail.PaymentFlagged,
            HasSubscription = detail.HasSubscription,
            StoredSubscription = detail.StoredSubscription
        };
    }

    // ── ARB Subscription (live Authorize.Net) ──

    public async Task<SubscriptionDetailDto?> GetTeamSubscriptionDetailAsync(
        Guid jobId, Guid teamId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetByIdReadOnlyAsync(teamId, ct);
        if (team == null || team.JobId != jobId)
            return null;

        if (string.IsNullOrWhiteSpace(team.AdnSubscriptionId))
            return null;

        try
        {
            // Read the subscription from the same ADN account that created it — sandbox off-Production,
            // production on Production. A sandbox-origin subscription cannot be resolved against the
            // production account (and vice versa), so the read env must match the create env.
            var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
            var env = _adnApi.GetADNEnvironment();

            _logger.LogInformation(
                "Fetching team subscription from ADN: TeamId={TeamId}, SubscriptionId={SubId}, Env={Env}",
                teamId, team.AdnSubscriptionId, env);

            var details = _adnApi.GetSubscriptionDetails(env, creds.AdnLoginId!, creds.AdnTransactionKey!, team.AdnSubscriptionId);

            if (details == null)
            {
                _logger.LogWarning("ADN GetSubscriptionDetails returned null for team SubId={SubId}", team.AdnSubscriptionId);
                return null;
            }

            if (details.messages?.resultCode != messageTypeEnum.Ok)
            {
                var errorMsg = details.messages?.message?.FirstOrDefault()?.text ?? "Unknown ADN error";
                _logger.LogWarning(
                    "ADN GetSubscriptionDetails failed: team SubId={SubId}, ResultCode={Code}, Error={Error}",
                    team.AdnSubscriptionId, details.messages?.resultCode, errorMsg);
                return null;
            }

            if (details.subscription == null)
            {
                _logger.LogWarning("ADN returned Ok but subscription object is null for team SubId={SubId}", team.AdnSubscriptionId);
                return null;
            }

            var sub = details.subscription;
            var intervalLength = sub.paymentSchedule?.interval?.length ?? 1;
            var intervalLabel = intervalLength == 1 ? "every month" : $"every {intervalLength} months";

            return new SubscriptionDetailDto
            {
                SubscriptionId = team.AdnSubscriptionId,
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
            _logger.LogWarning(ex, "Failed to load subscription for team {TeamId}, SubId={SubId}", teamId, team.AdnSubscriptionId);
            return null;
        }
    }

    public async Task CancelTeamSubscriptionAsync(
        Guid jobId, string userId, Guid teamId, CancellationToken ct = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct)
            ?? throw new KeyNotFoundException("Team not found.");

        if (team.JobId != jobId)
            throw new InvalidOperationException("Team does not belong to this job.");

        if (string.IsNullOrWhiteSpace(team.AdnSubscriptionId))
            throw new InvalidOperationException("Team has no ARB subscription.");

        // Env-bound: cancel against the SAME account that created the subscription (sandbox
        // off-Production, production on Production). A prod-origin subscription is therefore not
        // cancellable from a non-Production host — by design, so a preview environment can never
        // cancel a real customer's recurring billing.
        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
        var env = _adnApi.GetADNEnvironment();

        var result = _adnApi.ADN_CancelSubscription(env, creds.AdnLoginId!, creds.AdnTransactionKey!, team.AdnSubscriptionId);

        if (result?.messages?.resultCode != messageTypeEnum.Ok)
        {
            var err = result?.messages?.message?.FirstOrDefault()?.text ?? "Cancel failed.";
            throw new InvalidOperationException($"Failed to cancel subscription: {err}");
        }

        team.AdnSubscriptionStatus = "canceled";
        team.Modified = DateTime.Now;
        team.LebUserId = userId;

        await _teamRepo.SaveChangesAsync(ct);

        _logger.LogInformation("Team subscription canceled: TeamId={TeamId}, SubId={SubId}", teamId, team.AdnSubscriptionId);
    }

    public async Task<ClubRepAccountingDto?> GetClubRepAccountingAsync(
        Guid clubRepRegistrationId, Guid jobId, CancellationToken ct = default)
    {
        var reg = await _registrationRepo.GetByIdAsync(clubRepRegistrationId, ct);
        if (reg == null || reg.JobId != jobId) return null;

        // Source the club rep's teams (incl. waitlist/dropped/inactive) and shape them
        // through the SAME shaper the rep's own payment grid uses, so the director's
        // accounting grid renders identical per-method owed / proc-fee / discount columns.
        var rawTeams = await _teamRepo.GetRegisteredTeamsForClubRepAndJobAsync(clubRepRegistrationId: clubRepRegistrationId, jobId: jobId, cancellationToken: ct);
        var teams = await _shaper.ShapeAsync(jobId, rawTeams, ct: ct);
        var accountingRecords = await _accountingRepo.GetByRegistrationIdAsync(clubRepRegistrationId, ct);
        var feeSettings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);

        return new ClubRepAccountingDto
        {
            ClubRepRegistrationId = clubRepRegistrationId,
            ClubName = reg.ClubName ?? "",
            FeeTotal = reg.FeeTotal,
            PaidTotal = reg.PaidTotal,
            OwedTotal = reg.OwedTotal,
            Teams = teams,
            AccountingRecords = accountingRecords,
            PaymentMethodsAllowedCode = feeSettings?.PaymentMethodsAllowedCode ?? PaymentMethodConstants.CreditCardOrCheck
        };
    }

    /// <summary>
    /// Extract LOP options from Jobs.JsonOptions → List_Lops.
    /// </summary>
    private async Task<List<string>> GetLopOptionsAsync(Guid jobId, CancellationToken ct)
    {
        var metadata = await _jobRepo.GetJobMetadataAsync(jobId, ct);
        if (string.IsNullOrWhiteSpace(metadata?.JsonOptions)) return [];

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metadata.JsonOptions);
            if (!doc.RootElement.TryGetProperty("List_Lops", out var lopsElement)) return [];
            if (lopsElement.ValueKind != System.Text.Json.JsonValueKind.Array) return [];

            var result = new List<string>();
            foreach (var item in lopsElement.EnumerateArray())
            {
                var value = item.TryGetProperty("Value", out var v) ? v.GetString()
                          : item.TryGetProperty("value", out var v2) ? v2.GetString()
                          : null;
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
            return result;
        }
        catch
        {
            return [];
        }
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

        team.Modified = DateTime.Now;
        team.LebUserId = userId;

        await _teamRepo.SaveChangesAsync(ct);

        // Active is one of the filters on the rep-aggregate sync query — flipping it
        // here without re-aggregating leaves clubRep.OwedTotal counting (or omitting)
        // this team incorrectly. Defensive: sync whenever the team has a club rep.
        if (team.ClubrepRegistrationid.HasValue)
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(
                team.ClubrepRegistrationid.Value, userId, ct);
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
            {
                var adnError = txDetails?.messages?.message?.FirstOrDefault()?.text ?? "Gateway returned no error details";
                return new RefundResponse { Success = false, Message = adnError };
            }

            var txStatus = txDetails.transaction?.transactionStatus;
            string refundTransId;

            if (txStatus == "capturedPendingSettlement")
            {
                // VOID the transaction (full amount)
                var voidResult = _adnApi.ADN_Void_Result(new AdnVoidRequest
                {
                    Env = env,
                    LoginId = creds.AdnLoginId ?? "",
                    TransactionKey = creds.AdnTransactionKey ?? "",
                    TransactionId = original.AdnTransactionId
                });

                if (!voidResult.Success)
                    return new RefundResponse { Success = false, Message = voidResult.MessageForUser };

                refundTransId = voidResult.TransactionId ?? "";

                // Void reverses the full original amount
                original.Paymeth = (original.Paymeth ?? "") + $" VOIDED {DateTime.Now}";
                original.Payamt = 0;
            }
            else if (txStatus == "settledSuccessfully")
            {
                // REFUND the transaction (partial or full)
                var refundResult = _adnApi.ADN_Refund_Result(new AdnRefundRequest
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

                if (!refundResult.Success)
                    return new RefundResponse { Success = false, Message = refundResult.MessageForUser };

                refundTransId = refundResult.TransactionId ?? "";

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
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
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
                    team.RecalcTotals();
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

        var team = await _teamRepo.GetTeamFromTeamId(request.TeamId.Value, ct);
        if (team == null || team.JobId != jobId)
            return new TeamCcChargeResponse { Success = false, Error = "Team not found in this event." };

        var owed = Math.Max(0m, team.OwedTotal ?? 0m);
        if (owed <= 0m)
            return new TeamCcChargeResponse { Success = false, Error = "This team has no outstanding balance." };

        return await ChargeCcViaEngineAsync(
            request.ClubRepRegistrationId, userId, request.CreditCard,
            new List<Guid> { request.TeamId.Value }, owed);
    }

    // ── CC Charge (club-level) ──

    public async Task<TeamCcChargeResponse> ChargeCcForClubAsync(
        Guid jobId, string userId, TeamCcChargeRequest request, CancellationToken ct = default)
    {
        // All active club teams that still owe a balance.
        var clubTeams = await _teamRepo.GetActiveClubTeamsOrderedByOwedAsync(jobId, request.ClubRepRegistrationId, ct);
        var owedTeams = clubTeams.Where(t => (t.OwedTotal ?? 0m) > 0m).ToList();

        if (owedTeams.Count == 0)
            return new TeamCcChargeResponse { Success = false, Error = "No club teams have outstanding balances." };

        var total = owedTeams.Sum(t => Math.Max(0m, t.OwedTotal ?? 0m));
        return await ChargeCcViaEngineAsync(
            request.ClubRepRegistrationId, userId, request.CreditCard,
            owedTeams.Select(t => t.TeamId).ToList(), total);
    }

    // ── CC Charge (shared) ──
    // Admin CC charges delegate to the canonical PaymentService engine so the admin
    // modal and the club-rep wizard charge cards through the SAME ResolveOwed-based path
    // (one implementation, no drift). CC charges the full owed per team, so the
    // server-computed total we pass equals the engine's own total — the AMOUNT_MISMATCH
    // tripwire passes. Replaces the former bespoke ADN_Charge loop.
    private async Task<TeamCcChargeResponse> ChargeCcViaEngineAsync(
        Guid clubRepRegistrationId, string userId, CreditCardInfo creditCard, List<Guid> teamIds, decimal total)
    {
        var result = await _paymentService.ProcessTeamPaymentAsync(
            clubRepRegistrationId, userId, teamIds, total, creditCard);

        return new TeamCcChargeResponse
        {
            Success = result.Success,
            Error = result.Success ? null : (result.Message ?? result.Error ?? "CC charge failed."),
            // Pass the engine's per-team outcomes through so a partial club charge surfaces which
            // teams cleared vs declined (and why) — not collapsed to a flat Success/Error.
            Teams = result.Teams
        };
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
            var processingRate = await _feeService.GetEffectiveProcessingRateAsync(jobId, ct);

            // Get teams — single or all club teams
            var clubTeams = await _teamRepo.GetActiveClubTeamsOrderedByOwedAsync(jobId, request.ClubRepRegistrationId, ct);
            if (singleTeamId.HasValue)
                clubTeams = clubTeams.Where(t => t.TeamId == singleTeamId.Value).ToList();

            var clubTeamIds = clubTeams.Select(t => t.TeamId).ToList();
            var teamPaymentStates = await _paymentState.ForTeamsAsync(clubTeamIds, jobId, ct);
            var emptyTeamState = await BuildEmptyPaymentStateAsync(jobId, ct);

            // Check-specific cap — sum of CkOwedTotal across teams in scope (one team
            // when singleTeamId is set; all club teams otherwise). Tighter than the
            // OwedTotal cap above because check skips processing fees. Mirrors the
            // FE balance-due. Corrections keep the OwedTotal cap (intentional ± adj).
            if (isCheck)
            {
                decimal scopeCheckOwed = 0m;
                foreach (var capTeam in clubTeams)
                {
                    var capState = teamPaymentStates.GetValueOrDefault(capTeam.TeamId, emptyTeamState);
                    var capOwed = capState.ResolveOwed(
                        capTeam.OwedTotal ?? 0m,
                        capTeam.FeeBase ?? 0m,
                        capTeam.FeeDiscount ?? 0m,
                        capTeam.FeeLatefee ?? 0m,
                        capTeam.FeeDonation ?? 0m,
                        capTeam.FeeProcessing ?? 0m);
                    scopeCheckOwed += capOwed.Check;
                }
                if (request.Amount > scopeCheckOwed)
                    return new TeamCheckOrCorrectionResponse { Success = false, Error = $"Check payment ({request.Amount:C}) exceeds check balance ({scopeCheckOwed:C})." };
            }
            else
            {
                // Correction bounds — invariant: balance stays in [0, FeeTotal].
                // Upper = sum(OwedTotal) ("can't charge more than they owe"),
                // lower = -sum(PaidTotal) ("can't credit more than they paid").
                // Per-scope (one team when singleTeamId, club aggregate otherwise) —
                // mirrors the same scoping discipline as the check cap above.
                decimal scopeOwed = 0m;
                decimal scopePaid = 0m;
                foreach (var capTeam in clubTeams)
                {
                    scopeOwed += capTeam.OwedTotal ?? 0m;
                    scopePaid += capTeam.PaidTotal ?? 0m;
                }
                if (request.Amount > scopeOwed)
                    return new TeamCheckOrCorrectionResponse { Success = false, Error = $"Correction ({request.Amount:C}) exceeds amount owed ({scopeOwed:C})." };
                if (request.Amount < -scopePaid)
                    return new TeamCheckOrCorrectionResponse { Success = false, Error = $"Correction ({request.Amount:C}) exceeds amount paid ({scopePaid:C} refundable)." };
            }

            var allocations = new List<TeamPaymentAllocation>();
            var remainingBalance = request.Amount;

            foreach (var team in clubTeams)
            {
                if (remainingBalance <= 0) break;

                // Step 1: Compute principal still owed via canonical PaymentState
                // (handles CC reverse-out + eCheck proc collected). Old shortcut
                // OwedTotal/(1+rate) breaks once any CC payment hits.
                var state = teamPaymentStates.GetValueOrDefault(team.TeamId, emptyTeamState);
                var feeBase = team.FeeBase ?? 0m;
                var feeDiscount = team.FeeDiscount ?? 0m;
                var feeLatefee = team.FeeLatefee ?? 0m;
                var feeDonation = team.FeeDonation ?? 0m;
                var baseOwed = state.PrincipalRemaining(feeBase, feeDiscount, feeLatefee, feeDonation);

                // Step 2: Allocate base amount from remaining check balance
                var calculatedTeamCheckAmount = Math.Min(baseOwed, remainingBalance);
                if (calculatedTeamCheckAmount <= 0) continue;

                // Step 3: Fee reduction = allocation × rate (canonical full-CC-rate credit).
                // Reduce only the team's processing fee; the chokepoint below re-derives the
                // team's FeeTotal/OwedTotal from the ledger, and SynchronizeClubRepFinancials
                // re-aggregates the rep from its teams (the sole writer of rep totals — the old
                // inline clubRep math was redundant with it).
                decimal processingFeeReduction = 0;
                if (bAddProcessingFees && (team.FeeProcessing ?? 0) > 0)
                {
                    processingFeeReduction = decimal.Round(
                        PaymentRateMath.NonProcCheckCredit(calculatedTeamCheckAmount, processingRate),
                        2, MidpointRounding.AwayFromZero);

                    team.FeeProcessing = (team.FeeProcessing ?? 0) - processingFeeReduction;
                }

                remainingBalance -= calculatedTeamCheckAmount;

                // Record the check row and re-derive the team's totals from the ledger in one
                // transaction, then re-aggregate the rep from its teams.
                await _accountingRepo.RecordPaymentAndRecomputeAsync(new RegistrationAccounting
                {
                    Active = true,
                    CheckNo = request.CheckNo,
                    Comment = request.Comment,
                    Createdate = DateTime.Now,
                    Dueamt = calculatedTeamCheckAmount,
                    Payamt = calculatedTeamCheckAmount,
                    PaymentMethodId = paymentMethodId,
                    TeamId = team.TeamId,
                    RegistrationId = clubRep.RegistrationId,
                    LebUserId = userId,
                    Modified = DateTime.Now
                }, userId, ct);

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

    // ── Club Rep Operations ──

    public async Task<List<ClubRegistrationDto>> GetClubRegistrationsForJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return await _ladtService.GetClubRegistrationsForJobAsync(jobId, ct);
    }

    public async Task<ClubOperationResultDto> ChangeClubAsync(
        Guid teamId, Guid jobId, string userId, ChangeClubRequest request, CancellationToken ct = default)
    {
        var moveRequest = new MoveTeamToClubRequest
        {
            TargetRegistrationId = request.TargetRegistrationId,
            MoveAllFromClub = false
        };
        var result = await _ladtService.MoveTeamToClubAsync(teamId, moveRequest, jobId, userId, ct);
        return new ClubOperationResultDto
        {
            TeamsAffected = result.TeamsAffected,
            Message = result.Message,
            SourceDeactivated = false
        };
    }

    public async Task<ClubOperationResultDto> TransferAllTeamsAndDeactivateAsync(
        Guid jobId, string userId, TransferAllTeamsRequest request, CancellationToken ct = default)
    {
        // Get any team from the source club rep to pass to the move method
        var sourceTeams = await _teamRepo.GetTeamsByClubRepRegistrationAsync(jobId, request.SourceRegistrationId, ct);
        if (sourceTeams.Count == 0)
            throw new InvalidOperationException("Source club rep has no teams to transfer.");

        var moveRequest = new MoveTeamToClubRequest
        {
            TargetRegistrationId = request.TargetRegistrationId,
            MoveAllFromClub = true
        };
        var result = await _ladtService.MoveTeamToClubAsync(sourceTeams[0].TeamId, moveRequest, jobId, userId, ct);

        // Deactivate source club rep registration
        var sourceReg = await _registrationRepo.GetByIdAsync(request.SourceRegistrationId, ct);
        if (sourceReg != null)
        {
            sourceReg.BActive = false;
            sourceReg.Modified = DateTime.Now;
            sourceReg.LebUserId = userId;
            await _registrationRepo.SaveChangesAsync(ct);
        }

        return new ClubOperationResultDto
        {
            TeamsAffected = result.TeamsAffected,
            Message = $"{result.Message} Source club rep deactivated.",
            SourceDeactivated = true
        };
    }

    // ── Shared ──

    public async Task<List<PaymentMethodOptionDto>> GetPaymentMethodOptionsAsync(CancellationToken ct = default)
    {
        return await _accountingRepo.GetPaymentMethodOptionsAsync(ct);
    }

    // ── AUTOPAY FAILED triage queue: resend invoices ──

    public async Task<ResendInvoicesResponse> ResendInvoicesAsync(
        Guid jobId, string userId, ResendInvoicesRequest request, CancellationToken ct = default)
    {
        var probes = await _teamRepo.FindFlaggedTeamsForResendAsync(jobId, request.TeamIds, ct);
        if (probes.Count == 0)
        {
            return new ResendInvoicesResponse
            {
                TeamsTargeted = 0,
                TeamsEmailed = 0,
                TeamsSkipped = 0,
                RepsEmailed = 0,
                Message = "No flagged teams found."
            };
        }

        var jobInfo = await _jobRepo.GetConfirmationEmailInfoAsync(jobId, ct);
        var jobPath = jobInfo?.JobPath;
        var jobName = jobInfo?.JobName ?? "your event";
        if (string.IsNullOrWhiteSpace(jobPath))
            return new ResendInvoicesResponse
            {
                TeamsTargeted = probes.Count,
                TeamsEmailed = 0,
                TeamsSkipped = probes.Count,
                RepsEmailed = 0,
                Message = "Job path missing — cannot build payment link."
            };

        var teamsTargeted = probes.Count;
        var teamsSkipped = 0;
        var teamsEmailed = 0;
        var repsEmailed = 0;

        var paymentUrl = $"https://www.teamsportsinfo.com/{jobPath}/registration/team?step=payment";

        // Group by rep so each rep gets one rolled-up email
        foreach (var repGroup in probes.GroupBy(p => p.RepRegistrationId))
        {
            var repTeams = repGroup.ToList();
            var rep = repTeams[0];

            if (rep.RepEmailOptOut || string.IsNullOrWhiteSpace(rep.RepEmail))
            {
                teamsSkipped += repTeams.Count;
                continue;
            }

            var greetingName = !string.IsNullOrWhiteSpace(rep.RepFirstName)
                ? rep.RepFirstName
                : "Club Rep";

            var rows = string.Join("", repTeams.Select(t => $"""
                <tr>
                    <td style="padding:6px 12px; border-bottom:1px solid #eee;">{System.Net.WebUtility.HtmlEncode(t.TeamName)}</td>
                    <td style="padding:6px 12px; border-bottom:1px solid #eee;">{System.Net.WebUtility.HtmlEncode(t.AgegroupName)}</td>
                    <td style="padding:6px 12px; border-bottom:1px solid #eee; text-align:right;">{t.OwedTotal:C}</td>
                </tr>
                """));
            var totalOwed = repTeams.Sum(t => t.OwedTotal);

            var html = $"""
                <p>Hi {System.Net.WebUtility.HtmlEncode(greetingName)},</p>
                <p>Your scheduled payment for <strong>{System.Net.WebUtility.HtmlEncode(jobName)}</strong>
                did not go through. The team(s) below have an outstanding balance:</p>
                <table style="border-collapse:collapse; margin:16px 0;">
                    <thead>
                        <tr style="background:#f7f7f7;">
                            <th style="padding:6px 12px; text-align:left; border-bottom:2px solid #ddd;">Team</th>
                            <th style="padding:6px 12px; text-align:left; border-bottom:2px solid #ddd;">Age Group</th>
                            <th style="padding:6px 12px; text-align:right; border-bottom:2px solid #ddd;">Balance</th>
                        </tr>
                    </thead>
                    <tbody>{rows}</tbody>
                    <tfoot>
                        <tr>
                            <td colspan="2" style="padding:8px 12px; text-align:right; font-weight:600;">Total:</td>
                            <td style="padding:8px 12px; text-align:right; font-weight:600;">{totalOwed:C}</td>
                        </tr>
                    </tfoot>
                </table>
                <p>
                    <a href="{paymentUrl}"
                       style="display:inline-block; padding:10px 20px; background:#0d6efd; color:#fff;
                              text-decoration:none; border-radius:4px;">
                        Pay Balance Due
                    </a>
                </p>
                <p style="color:#666; font-size:13px;">
                    If the button does not work, copy and paste this link into your browser:<br/>
                    <a href="{paymentUrl}">{paymentUrl}</a>
                </p>
                """;

            var msg = new EmailMessageDto
            {
                Subject = $"Payment Reminder — {jobName}",
                HtmlBody = html,
                ToAddresses = new List<string> { rep.RepEmail!.Trim() }
            };

            try
            {
                var sent = await _emailService.SendAsync(msg, cancellationToken: ct);
                if (sent)
                {
                    repsEmailed++;
                    teamsEmailed += repTeams.Count;
                }
                else
                {
                    teamsSkipped += repTeams.Count;
                    _logger.LogWarning("Resend invoice failed for rep {RegId} ({Email})",
                        rep.RepRegistrationId, rep.RepEmail);
                }
            }
            catch (Exception ex)
            {
                teamsSkipped += repTeams.Count;
                _logger.LogError(ex, "Resend invoice exception for rep {RegId}", rep.RepRegistrationId);
            }
        }

        var summary = $"Sent {teamsEmailed} team reminder(s) to {repsEmailed} rep(s)"
                    + (teamsSkipped > 0 ? $"; skipped {teamsSkipped}." : ".");

        return new ResendInvoicesResponse
        {
            TeamsTargeted = teamsTargeted,
            TeamsEmailed = teamsEmailed,
            TeamsSkipped = teamsSkipped,
            RepsEmailed = repsEmailed,
            Message = summary
        };
    }

    private async Task<PaymentState> BuildEmptyPaymentStateAsync(Guid jobId, CancellationToken ct)
    {
        var settings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
        return PaymentState.Empty(
            bAddProcessingFees: settings?.BAddProcessingFees ?? false,
            ccRate: await _feeService.GetEffectiveProcessingRateAsync(jobId, ct),
            echeckRate: await _feeService.GetEffectiveEcheckProcessingRateAsync(jobId, ct));
    }
}
