using AuthorizeNet.Api.Contracts.V1;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Contracts.Extensions;
using TSIC.Contracts.Services;
using TSIC.API.Services.Fees;
using TSIC.API.Services.Shared;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Shared.Email;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Players;
using TSIC.Domain.Constants;


namespace TSIC.API.Services.Payments;

public class PaymentService : IPaymentService
{
    private readonly IJobRepository _jobs;
    private readonly IRegistrationRepository _registrations;
    private readonly ITeamRepository _teams;
    private readonly IAdnApiService _adnApiService;
    private readonly IFeeResolutionService _feeService;
    private readonly ITeamLookupService _teamLookup;
    private readonly ILogger<PaymentService> _logger;
    private readonly IPlayerRegConfirmationService? _confirmation;
    private readonly IEmailService? _email;
    private readonly IFamiliesRepository _families;
    private readonly IRegistrationAccountingRepository _acct;
    private readonly IRegistrationFeeAdjustmentService _feeAdj;
    private readonly IEcheckSettlementRepository _settleRepo;
    private readonly IPaymentStateService _paymentState;
    private readonly ITeamPlacementService _placement;

    // Well-known E-Check Payment method GUID (matches production seed data).
    private static readonly Guid EcheckPaymentMethodId = Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D");
    // Well-known Credit-Card Payment method GUID (matches production seed data).
    private static readonly Guid CcPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

    private sealed record JobInfo(bool? AdnArb, int? AdnArbbillingOccurences, int? AdnArbintervalLength, DateTime? AdnArbstartDate, bool AllowPif, bool BPlayersFullPaymentRequired, bool BEnableEcheck);

    public PaymentService(IJobRepository jobs, IRegistrationRepository registrations, ITeamRepository teams, IFamiliesRepository families, IRegistrationAccountingRepository acct, IAdnApiService adnApiService, IFeeResolutionService feeService, ITeamLookupService teamLookup, IRegistrationFeeAdjustmentService feeAdj, IEcheckSettlementRepository settleRepo, ILogger<PaymentService> logger, IPaymentStateService paymentState, ITeamPlacementService placement)
    {
        _jobs = jobs;
        _registrations = registrations;
        _teams = teams;
        _families = families;
        _acct = acct;
        _adnApiService = adnApiService;
        _feeService = feeService;
        _teamLookup = teamLookup;
        _feeAdj = feeAdj;
        _settleRepo = settleRepo;
        _logger = logger;
        _paymentState = paymentState;
        _placement = placement;
    }

    // Extended constructor adding confirmation + email services; preserves backward compatibility with tests using the original signature.
    public PaymentService(IJobRepository jobs, IRegistrationRepository registrations, ITeamRepository teams, IFamiliesRepository families, IRegistrationAccountingRepository acct, IAdnApiService adnApiService, IFeeResolutionService feeService, ITeamLookupService teamLookup, IRegistrationFeeAdjustmentService feeAdj, IEcheckSettlementRepository settleRepo, ILogger<PaymentService> logger, IPaymentStateService paymentState, ITeamPlacementService placement, IPlayerRegConfirmationService confirmation, IEmailService email)
        : this(jobs, registrations, teams, families, acct, adnApiService, feeService, teamLookup, feeAdj, settleRepo, logger, paymentState, placement)
    {
        _confirmation = confirmation;
        _email = email;
    }

    public async Task<TeamPaymentResponseDto> ProcessTeamPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        decimal totalAmount,
        CreditCardInfo creditCard,
        decimal donation = 0m)
    {
        var jobId = await _registrations.GetRegistrationJobIdAsync(regId);
        if (jobId == null)
            return new TeamPaymentResponseDto { Success = false, Message = "Registration not found" };
        var jobIdValue = jobId.Value;

        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobIdValue);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
            return new TeamPaymentResponseDto { Success = false, Message = "Payment gateway credentials not configured" };
        var env = _adnApiService.GetADNEnvironment();

        return await ChargeTeamsAsync(
            regId, userId, jobIdValue, teamIds, totalAmount,
            TeamChargeKind.Cc, credentials, env, creditCard, bankAccount: null, donation);
    }

    public async Task<TeamPaymentResponseDto> ProcessTeamEcheckPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        decimal totalAmount,
        BankAccountInfo bankAccount,
        decimal donation = 0m)
    {
        var jobId = await _registrations.GetRegistrationJobIdAsync(regId);
        if (jobId == null)
            return new TeamPaymentResponseDto { Success = false, Error = "REG_NOT_FOUND", Message = "Registration not found" };
        var jobIdValue = jobId.Value;

        // Per-job opt-in for eCheck.
        var jobPaymentInfo = await _jobs.GetJobPaymentInfoAsync(jobIdValue);
        if (jobPaymentInfo == null)
            return new TeamPaymentResponseDto { Success = false, Error = "INVALID_JOB", Message = "Invalid job" };
        if (!jobPaymentInfo.BEnableEcheck)
            return new TeamPaymentResponseDto { Success = false, Error = "ECHECK_NOT_ENABLED", Message = "eCheck payments are not enabled for this job." };

        var (bankErr, bankCode) = NormalizeAndValidateBankAccount(bankAccount);
        if (bankErr != null)
            return new TeamPaymentResponseDto { Success = false, Error = bankCode, Message = bankErr };

        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobIdValue);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
            return new TeamPaymentResponseDto { Success = false, Error = "MISSING_GATEWAY_CREDS", Message = "Payment gateway credentials not configured" };
        var env = _adnApiService.GetADNEnvironment();

        return await ChargeTeamsAsync(
            regId, userId, jobIdValue, teamIds, totalAmount,
            TeamChargeKind.Echeck, credentials, env, creditCard: null, bankAccount, donation);
    }

    private enum TeamChargeKind { Cc, Echeck }
    private enum RegistrationChargeKind { Cc, Echeck }

    /// <summary>
    /// Shared per-team charge engine for the club-rep self-pay flows (CC and eCheck).
    /// One ADN transaction per team, each charged its OWN balance — never an equal split
    /// of the client total. The proc-fee model is unified via PaymentRateMath:
    ///
    ///   charge = OwedTotal − ProcCredit(principalRemaining, ccRate, methodRate)
    ///
    /// methodRate is ccRate for CC → credit 0 → charge == OwedTotal (CC behaviour
    /// unchanged); echeckRate for eCheck → credit = principal × (ccRate − echeckRate),
    /// so the gateway is debited the eCheck-rate gross, not the CC-rate gross. Payamt
    /// and PaidTotal accumulate that same gross; OwedTotal lands at 0 on a full pay.
    /// See go-live investigation 002 (Issues 1 &amp; 5).
    /// </summary>
    private async Task<TeamPaymentResponseDto> ChargeTeamsAsync(
        Guid regId, string userId, Guid jobIdValue,
        IReadOnlyCollection<Guid> teamIds, decimal totalAmount,
        TeamChargeKind kind, AdnCredentialsViewModel credentials, AuthorizeNet.Environment env,
        CreditCardInfo? creditCard, BankAccountInfo? bankAccount, decimal donation = 0m)
    {
        var teams = await _teams.GetTeamsWithJobAndCustomerAsync(jobIdValue, teamIds);
        if (teams.Count != teamIds.Count)
            return new TeamPaymentResponseDto { Success = false, Error = "TEAM_NOT_FOUND", Message = "One or more teams not found" };

        // Canonical principal-remaining per team (handles prior payments + phase); used
        // only to size the eCheck proc credit. CC never credits (methodRate == ccRate).
        var teamStates = await _paymentState.ForTeamsAsync(teamIds, jobIdValue);
        var rateRef = teamStates.Values.FirstOrDefault()
            ?? await _paymentState.ForTeamAsync(teams[0].TeamId, jobIdValue);
        var emptyState = PaymentState.Empty(rateRef.BAddProcessingFees, rateRef.CcRate, rateRef.EcheckRate);

        // ── Charge-entry realize: auto-activated late fee (Phase 2) ──
        // Re-derive each team's effective late fee from the LIVE cascade before sizing the charge,
        // so a late-fee window that opened without a director reprice still lands at payment. DRY:
        // the same swap applier the reprice engine runs — idempotent for a paying team (positive
        // owed ⇒ its stamped phase already matches; the applier never downgrades), so the ONLY field
        // that moves is FeeLatefee (+ proc/totals riding on it). Persist now (BEFORE the donation
        // block) so that if the client's total is stale the amount-mismatch tripwire below fires and
        // the rep's refresh shows the realized total — and a failed-charge return never persists an
        // unpaid donation, only the realized late fee (correct to carry). Inert (no SQL) when no
        // window is active or the team is paid in full.
        foreach (var team in teams)
            await _feeService.RealizeLateFeeAtChargeAsync(team, jobIdValue);
        await _teams.SaveChangesAsync();

        // Optional donation: the gift lands in full on ONE team (the client's first) and rides
        // this payment — ChargeTeamsAsync bills each team's full OwedTotal, which now carries it.
        // Proc is levied at the CC rate exactly as the splitter folds donation into netBase; the
        // eCheck (CC−EC) credit below then converts that proc to the eCheck rate. Idempotent
        // guard: skip when the gift is already on the row (so a partial-success re-submit, which
        // charges only the still-owed teams, never re-levies the donation onto the first team).
        if (donation > 0m)
        {
            var firstTeam = teams.First(t => t.TeamId == teamIds.First());
            if ((firstTeam.FeeDonation ?? 0m) != donation)
            {
                firstTeam.FeeDonation = donation;
                if (rateRef.BAddProcessingFees && rateRef.CcRate > 0m)
                    firstTeam.FeeProcessing = (firstTeam.FeeProcessing ?? 0m)
                        + Math.Round(donation * rateRef.CcRate, 2, MidpointRounding.AwayFromZero);
                firstTeam.RecalcTotals();
            }
        }

        // Pre-compute each team's charge + proc credit (no mutation) so the amount-mismatch
        // tripwire sees the method-correct total before any gateway hit.
        var plans = new List<(TSIC.Domain.Entities.Teams Team, decimal Charge, decimal Credit)>(teams.Count);
        decimal serverTotal = 0m;
        foreach (var team in teams)
        {
            var owedTotal = Math.Max(0m, team.OwedTotal ?? 0m);
            if (owedTotal <= 0m) continue; // already paid in full — nothing to charge

            var state = teamStates.GetValueOrDefault(team.TeamId) ?? emptyState;
            // Single owed resolver — the rep's shown total (display path) and this charge
            // are produced by the SAME PaymentState.ResolveOwed, so they cannot drift (a
            // drift would re-trip the AMOUNT_MISMATCH tripwire below). CC charges the full
            // owed (credit 0); eCheck charges the lower eCheck-rate gross.
            var owed = state.ResolveOwed(
                owedTotal, team.FeeBase ?? 0m, team.TotalDiscount(), team.FeeLatefee ?? 0m, team.FeeDonation ?? 0m, team.FeeProcessing ?? 0m);
            var charge = kind == TeamChargeKind.Cc ? owed.Cc : owed.Echeck;
            var credit = owedTotal - charge; // proc backed out for this method (0 for CC)
            if (charge <= 0m) continue;

            serverTotal += charge;
            plans.Add((team, charge, credit));
        }

        if (serverTotal <= 0m)
            return new TeamPaymentResponseDto { Success = false, Error = "NOTHING_DUE", Message = "Selected teams have no balance due." };

        // Tripwire: the client-submitted total must agree with the server-computed,
        // method-correct total. eCheck totals are LOWER than the displayed CC owed, so the
        // client must submit the eCheck total when paying by eCheck (else this fails closed).
        if (Math.Abs(serverTotal - totalAmount) > 0.01m)
            return new TeamPaymentResponseDto
            {
                Success = false,
                Error = "AMOUNT_MISMATCH",
                Message = $"Payment amount is out of date (shown {totalAmount:C}, now {serverTotal:C}). Please refresh and try again."
            };

        string? firstTransactionId = null;
        var failedCount = 0;
        var teamResults = new List<TeamPaymentResultDto>(plans.Count);
        var pendingSettlements = new List<(RegistrationAccounting Ra, string TxId)>();
        var nameOnAcct = bankAccount?.NameOnAccount?.Trim();
        var acctLast4 = bankAccount?.AccountNumber is { Length: >= 4 } acct ? acct[^4..] : bankAccount?.AccountNumber;
        var ccExpiryDate = kind == TeamChargeKind.Cc ? FormatExpiry(creditCard!.Expiry!) : null;

        foreach (var (team, charge, credit) in plans)
        {
            var invoiceNumber = $"{team.Job.Customer.CustomerAi}_{team.Job.JobAi}_{team.TeamAi}";
            if (invoiceNumber.Length > 20) invoiceNumber = $"{team.Job.JobAi}_{team.TeamAi}";
            if (invoiceNumber.Length > 20) invoiceNumber = team.TeamAi.ToString();
            var description = BuildTeamChargeDescription(team);

            var chargeResult = kind == TeamChargeKind.Cc
                ? _adnApiService.ADN_Charge_Result(new AdnChargeRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    CardNumber = creditCard!.Number!,
                    CardCode = creditCard.Code!,
                    Expiry = ccExpiryDate!,
                    FirstName = creditCard.FirstName!,
                    LastName = creditCard.LastName!,
                    Address = creditCard.Address!,
                    Zip = creditCard.Zip!,
                    Email = creditCard.Email!,
                    Phone = creditCard.Phone!,
                    Amount = charge,
                    InvoiceNumber = invoiceNumber,
                    Description = description
                })
                : _adnApiService.ADN_ChargeBankAccount_Result(new AdnChargeBankAccountRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    AccountType = bankAccount!.AccountType!,
                    RoutingNumber = bankAccount.RoutingNumber!,
                    AccountNumber = bankAccount.AccountNumber!,
                    NameOnAccount = nameOnAcct!,
                    FirstName = bankAccount.FirstName!,
                    LastName = bankAccount.LastName!,
                    Address = bankAccount.Address!,
                    Zip = bankAccount.Zip!,
                    Email = bankAccount.Email!,
                    Phone = bankAccount.Phone!,
                    Amount = charge,
                    InvoiceNumber = invoiceNumber,
                    Description = description
                });

            if (chargeResult.Success)
            {
                var transId = chargeResult.TransactionId!;
                firstTransactionId ??= transId;

                var ra = new RegistrationAccounting
                {
                    RegistrationId = regId,
                    TeamId = team.TeamId,
                    Payamt = charge,
                    Dueamt = charge,
                    Paymeth = kind == TeamChargeKind.Cc
                        ? $"paid by cc: {charge:C} on {DateTime.Now:G} txID: {transId}"
                        : $"eCheck pending settlement: {charge:C} of {serverTotal:C} on {DateTime.Now:G} txID: {transId}",
                    PaymentMethodId = kind == TeamChargeKind.Cc ? CcPaymentMethodId : EcheckPaymentMethodId,
                    // Pessimistic eCheck: born Active=false (excluded from the PaidTotal sum) until
                    // the sweep confirms settlement; CC born Active=true. Payamt carries the amount
                    // for the settle-time credit either way.
                    Active = kind == TeamChargeKind.Cc,
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
                    LebUserId = userId,
                    AdnTransactionId = transId,
                    AdnInvoiceNo = invoiceNumber,
                    AdnCc4 = kind == TeamChargeKind.Cc ? creditCard!.Number![^4..] : null,
                    AdnCcexpDate = kind == TeamChargeKind.Cc ? ccExpiryDate : null,
                    Comment = description
                };
                _acct.Add(ra);
                if (kind == TeamChargeKind.Echeck) pendingSettlements.Add((ra, transId));

                // Convert the CC-rate proc embedded in the balance to the method's rate
                // (no-op for CC), then book the gross for CC only. Pessimistic eCheck defers the
                // credit to settlement (sweep), so the balance stays owed-until-cleared; the proc
                // adjustment and RecalcTotals still run so OwedTotal reflects the eCheck rate.
                if (credit > 0m)
                {
                    team.FeeProcessing = (team.FeeProcessing ?? 0m) - credit;
                    team.RecalcTotals();
                }
                if (kind == TeamChargeKind.Cc)
                {
                    team.PaidTotal = (team.PaidTotal ?? 0m) + charge;
                }
                team.RecalcTotals();
                team.Modified = DateTime.Now;
                team.LebUserId = userId;

                _logger.LogInformation("Team {Kind} processed: Team={TeamId} Charge={Charge} Credit={Credit} TransId={TransId}",
                    kind, team.TeamId, charge, credit, transId);

                teamResults.Add(new TeamPaymentResultDto
                {
                    TeamId = team.TeamId,
                    TeamName = team.TeamName ?? string.Empty,
                    Charged = true,
                    ChargedAmount = charge
                });
            }
            else
            {
                failedCount++;
                _logger.LogWarning("Team {Kind} failed: Team={TeamId} Error={Error}", kind, team.TeamId, chargeResult.MessageForUser);

                teamResults.Add(new TeamPaymentResultDto
                {
                    TeamId = team.TeamId,
                    TeamName = team.TeamName ?? string.Empty,
                    Charged = false,
                    FailureReason = chargeResult.MessageForUser
                });
            }
        }

        var attempted = plans.Count;
        if (failedCount < attempted)
        {
            await _teams.SaveChangesAsync();
            await _acct.SaveChangesAsync();

            if (kind == TeamChargeKind.Echeck && pendingSettlements.Count > 0)
            {
                // Settlement.RegistrationAccountingId is the identity-generated AId on RA;
                // requires the RA inserts above to be saved first.
                var nextCheckAt = DateTime.Now.AddDays(1);
                foreach (var (ra, txId) in pendingSettlements)
                {
                    _settleRepo.Add(new Settlement
                    {
                        SettlementId = Guid.NewGuid(),
                        RegistrationAccountingId = ra.AId,
                        AdnTransactionId = txId,
                        Status = "Pending",
                        SubmittedAt = DateTime.Now,
                        NextCheckAt = nextCheckAt,
                        AccountLast4 = acctLast4,
                        AccountType = bankAccount!.AccountType,
                        NameOnAccount = nameOnAcct,
                        Modified = DateTime.Now,
                        LebUserId = userId
                    });
                }
                await _settleRepo.SaveChangesAsync();
            }

            // Re-aggregate the rep registration row from the new team financials. One sync
            // covers the batch — every team belongs to the same rep (regId == ClubrepRegistrationid).
            await _registrations.SynchronizeClubRepFinancialsAsync(regId, userId);
        }

        if (failedCount == 0)
            return new TeamPaymentResponseDto
            {
                Success = true,
                Message = kind == TeamChargeKind.Cc
                    ? $"All {attempted} team payment(s) processed successfully"
                    : $"{attempted} team eCheck submission(s) accepted; settlement pending (typically 3–5 business days).",
                TransactionId = firstTransactionId,
                Teams = teamResults
            };
        if (failedCount < attempted)
            return new TeamPaymentResponseDto
            {
                Success = false,
                Error = "PARTIAL_SUCCESS",
                Message = kind == TeamChargeKind.Cc
                    ? $"{attempted - failedCount} of {attempted} team payment(s) succeeded"
                    : $"{attempted - failedCount} of {attempted} team eCheck submission(s) accepted; rest failed.",
                TransactionId = firstTransactionId,
                Teams = teamResults
            };
        return new TeamPaymentResponseDto
        {
            Success = false,
            Error = "ALL_FAILED",
            Message = kind == TeamChargeKind.Cc ? "All team payments failed" : "All team eCheck submissions failed",
            Teams = teamResults
        };
    }

    /// <summary>
    /// ARB-Trial team registration payment. Creates one ADN ARB subscription per team:
    /// deposit billed tomorrow (trial occurrence), balance billed on the job's configured
    /// AdnStartDateAfterTrial. Capture-what-you-can: stops at first per-team failure,
    /// prior successes persist (subs stay live so money flows to the tournament).
    /// Falls back to a single full-amount charge when today is on/after AdnStartDateAfterTrial.
    /// </summary>
    public async Task<TeamArbTrialPaymentResponseDto> ProcessTeamArbTrialPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        CreditCardInfo? creditCard,
        BankAccountInfo? bankAccount,
        decimal donation = 0m)
    {
        if (creditCard != null && bankAccount != null)
            return FailTrial("PAYMENT_METHOD_AMBIGUOUS", "Provide either credit card or bank account, not both");
        if (creditCard == null && bankAccount == null)
            return FailTrial("PAYMENT_METHOD_REQUIRED", "Credit card or bank account information is required");

        var jobId = await _registrations.GetRegistrationJobIdAsync(regId);
        if (jobId == null)
            return FailTrial("REG_NOT_FOUND", "Registration not found");
        var jobIdValue = jobId.Value;

        var feeSettings = await _jobs.GetJobFeeSettingsAsync(jobIdValue);
        if (feeSettings == null)
            return FailTrial("INVALID_JOB", "Invalid job");
        if (feeSettings.AdnArbTrial != true)
            return FailTrial("ARB_TRIAL_NOT_ENABLED", "ARB-Trial is not enabled for this job.");
        if (!feeSettings.AdnStartDateAfterTrial.HasValue)
            return FailTrial("ARB_TRIAL_BALANCE_DATE_MISSING", "ARB-Trial balance date is not configured.");
        if (bankAccount != null && !feeSettings.BEnableEcheck)
            return FailTrial("ECHECK_NOT_ENABLED", "eCheck payments are not enabled for this job.");

        if (bankAccount != null)
        {
            var (bankErr, bankCode) = NormalizeAndValidateBankAccount(bankAccount);
            if (bankErr != null) return FailTrial(bankCode!, bankErr);
        }
        else
        {
            var (ccErr, ccCode) = ValidateAndSanitizeCreditCard(creditCard!);
            if (ccErr != null) return FailTrial(ccCode!, ccErr);
        }

        var teams = await _teams.GetTeamsWithJobAndCustomerAsync(jobIdValue, teamIds);
        if (teams.Count != teamIds.Count)
            return FailTrial("TEAM_NOT_FOUND", "One or more teams not found");

        // Optional donation: stamp the gift on the client's first team so the ArbTrialFeeSplitter
        // folds it into that team's trial deposit (proc levied on it via netBase). Both the trial
        // loop and the fallback path read team.FeeDonation; the loop resets FeeProcessing from the
        // splitter each pass, so the set is idempotent.
        if (donation > 0m)
            teams.First(t => t.TeamId == teamIds.First()).FeeDonation = donation;

        // ── Charge-entry realize: mint the late fee at subscription setup ──
        // Setting up an ADN subscription IS the qualifying payment commitment (the deposit bills
        // tomorrow, the balance on the config date — "cc payment is next day"), so it is the moment
        // to mint an auto-activated late fee, exactly like the immediate CC/eCheck charge engine does
        // in ChargeTeamsAsync. Re-derive each team's effective late fee from the LIVE cascade now —
        // BEFORE the branch — so BOTH the subscription loop and the fallback path read the minted
        // FeeLatefee into the ArbTrialFeeSplitter (it is billed) and into RecalcTotals (it lands on
        // the row). Same idempotent applier the reprice/charge paths run; the teams are tracked, so
        // the late fee persists with the schedule via each branch's success-gated SaveChanges — no
        // successful setup, no mint. RealizeLateFeeAtChargeAsync also re-derives FeeBase/FeeProcessing,
        // but both branches overwrite those with the ARB schedule values, so ONLY FeeLatefee carries
        // through. (A manual pay-by-check, which has no ADN event, intentionally never mints — there
        // is no commitment to hang the late fee on.)
        foreach (var team in teams)
            await _feeService.RealizeLateFeeAtChargeAsync(team, jobIdValue);

        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobIdValue);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
            return FailTrial("MISSING_GATEWAY_CREDS", "Payment gateway credentials not configured");
        var env = _adnApiService.GetADNEnvironment();

        // Deposit billed tomorrow; balance billed on the job-config date.
        var today = DateTime.Now.Date;
        var depositDate = today.AddDays(1);
        var balanceDate = feeSettings.AdnStartDateAfterTrial.Value.Date;

        // Effective processing rate per payment method — fed straight into the splitter
        // so the per-charge math reflects the customer's actual rate (CC vs eCheck).
        var processingRate = bankAccount != null
            ? await _feeService.GetEffectiveEcheckProcessingRateAsync(jobIdValue)
            : await _feeService.GetEffectiveProcessingRateAsync(jobIdValue);

        // Fallback: today on/after balance date → single full-amount charge per team.
        // No subscription is created; the standard CC/eCheck per-team loop runs instead.
        if (today >= balanceDate)
        {
            return await ProcessArbTrialFallbackAsync(
                regId, userId, teams, jobIdValue, credentials, env, feeSettings, creditCard, bankAccount);
        }

        var intervalDays = (short)(balanceDate - depositDate).Days;
        if (intervalDays < 1)
            return FailTrial("INVALID_INTERVAL", "Balance date must be at least 2 days after today");

        // Penny-verify CC up front: ARB sub create only schema-validates the card, so a
        // declined card wouldn't surface until tomorrow's batch. Forces a sync decline now.
        if (creditCard != null)
        {
            var verifyResult = _adnApiService.ADN_VerifyCardWithPennyAuth(new AdnAuthorizeRequest
            {
                Env = env,
                LoginId = credentials.AdnLoginId!,
                TransactionKey = credentials.AdnTransactionKey!,
                CardNumber = creditCard.Number!,
                CardCode = creditCard.Code!,
                Expiry = FormatExpiry(creditCard.Expiry!),
                FirstName = creditCard.FirstName!,
                LastName = creditCard.LastName!,
                Address = creditCard.Address!,
                Zip = creditCard.Zip!,
                Amount = 0.01m
            });
            if (!verifyResult.Success)
                return FailTrial("CARD_VERIFY_FAILED", $"Card validation failed: {verifyResult.ErrorMessage}");
        }

        var bAddProcessing = feeSettings.BAddProcessingFees ?? true;
        var bApplyToDeposit = feeSettings.BApplyProcessingFeesToTeamDeposit ?? false;

        var results = new List<TeamArbTrialResultDto>();
        var notAttempted = new List<Guid>();
        var teamsList = teams.ToList();
        var stoppedAt = -1;

        for (int i = 0; i < teamsList.Count; i++)
        {
            var team = teamsList[i];

            // Pull raw deposit + balance straight from the JobFees cascade for ClubRep.
            var resolved = await _feeService.ResolveFeeAsync(
                jobIdValue, RoleConstants.ClubRep, team.AgegroupId, team.TeamId);
            var rawDeposit = resolved?.EffectiveDeposit ?? 0m;
            var rawBalance = resolved?.EffectiveBalanceDue ?? 0m;

            // Modifiers are frozen on the team row from registration time. teamDonation reads
            // back the gift stamped above on the first team (0 on the rest). TotalDiscount() is the
            // same total FeeMath subtracts from FeeTotal, so the two ARB legs foot to it exactly.
            var discount = team.TotalDiscount();
            var lateFee = team.FeeLatefee ?? 0m;
            var teamDonation = team.FeeDonation ?? 0m;

            var split = ArbTrialFeeSplitter.Split(
                rawDeposit, rawBalance, discount, lateFee, teamDonation, processingRate,
                bAddProcessingFees: bAddProcessing,
                bApplyProcessingFeesToTeamDeposit: bApplyToDeposit);

            // ARB-Trial requires two non-zero charges. A team configured with only a
            // deposit OR only a balance can't meaningfully be put on this schedule —
            // surface that as a failure and stop the batch.
            if (split.DepositCharge <= 0m || split.BalanceCharge <= 0m)
            {
                results.Add(new TeamArbTrialResultDto
                {
                    TeamId = team.TeamId,
                    Registered = false,
                    FailureReason = "Team fee config does not support trial schedule (deposit and balance must both be > 0)"
                });
                stoppedAt = i;
                break;
            }

            var invoiceNumber = BuildTeamInvoiceNumber(team);
            var description = BuildTeamChargeDescription(team);

            AdnArbCreateResult arbResult;
            if (creditCard != null)
            {
                arbResult = _adnApiService.ADN_ARB_CreateTrialSubscription_Cc(new AdnArbCreateTrialRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    CardNumber = creditCard.Number!,
                    CardCode = creditCard.Code!,
                    Expiry = FormatExpiry(creditCard.Expiry!),
                    FirstName = creditCard.FirstName!,
                    LastName = creditCard.LastName!,
                    Address = creditCard.Address!,
                    Zip = creditCard.Zip!,
                    Email = creditCard.Email!,
                    Phone = creditCard.Phone!,
                    InvoiceNumber = invoiceNumber,
                    Description = description,
                    TrialAmount = split.DepositCharge,
                    PerIntervalCharge = split.BalanceCharge,
                    StartDate = depositDate,
                    IntervalLengthDays = intervalDays
                });
            }
            else
            {
                arbResult = _adnApiService.ADN_ARB_CreateTrialSubscription_Bank(new AdnArbCreateTrialBankAccountRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    AccountType = bankAccount!.AccountType!,
                    RoutingNumber = bankAccount.RoutingNumber!,
                    AccountNumber = bankAccount.AccountNumber!,
                    NameOnAccount = bankAccount.NameOnAccount!.Trim(),
                    FirstName = bankAccount.FirstName!,
                    LastName = bankAccount.LastName!,
                    Address = bankAccount.Address!,
                    Zip = bankAccount.Zip!,
                    Email = bankAccount.Email!,
                    Phone = bankAccount.Phone!,
                    InvoiceNumber = invoiceNumber,
                    Description = description,
                    TrialAmount = split.DepositCharge,
                    PerIntervalCharge = split.BalanceCharge,
                    StartDate = depositDate,
                    IntervalLengthDays = intervalDays
                });
            }

            if (!arbResult.Success || string.IsNullOrWhiteSpace(arbResult.SubscriptionId))
            {
                results.Add(new TeamArbTrialResultDto
                {
                    TeamId = team.TeamId,
                    Registered = false,
                    FailureReason = string.IsNullOrWhiteSpace(arbResult.MessageForUser)
                        ? "Gateway subscription create failed"
                        : arbResult.MessageForUser
                });
                stoppedAt = i;
                _logger.LogWarning("ARB-Trial sub failed: Team={TeamId} Reason={Reason}",
                    team.TeamId, arbResult.MessageForUser);
                break;
            }

            // Stamp full schedule onto the team row. FeeBase is the raw service amount
            // (deposit + balance); discount/latefee/donation stay in their dedicated columns;
            // FeeProcessing carries the splitter-computed processing total (now levied on the
            // donation too, since the splitter folds donation into netBase).
            team.FeeBase = rawDeposit + rawBalance;
            team.FeeProcessing = split.TotalProcessing;
            // RecalcTotals derives FeeTotal from the frozen components (base + proc − discount
            // − discountMp + donation + latefee), which == split.DepositCharge + split.BalanceCharge.
            // Both sides now net the SAME discount (TotalDiscount() above == what FeeMath subtracts),
            // so the two scheduled ADN charges foot exactly to FeeTotal — no residual divergence.
            team.RecalcTotals();

            team.AdnSubscriptionId = arbResult.SubscriptionId;
            team.AdnSubscriptionStatus = "active";
            team.AdnSubscriptionStartDate = depositDate;
            team.AdnSubscriptionBillingOccurences = 2;
            team.AdnSubscriptionAmountPerOccurence = split.BalanceCharge;
            team.AdnSubscriptionIntervalLength = intervalDays;
            team.Modified = DateTime.Now;
            team.LebUserId = userId;

            results.Add(new TeamArbTrialResultDto
            {
                TeamId = team.TeamId,
                Registered = true,
                AdnSubscriptionId = arbResult.SubscriptionId,
                DepositCharge = split.DepositCharge,
                BalanceCharge = split.BalanceCharge,
                DepositDate = depositDate,
                BalanceDate = balanceDate
            });

            _logger.LogInformation(
                "ARB-Trial sub created: Team={TeamId} Sub={SubId} Deposit={Deposit} on {DepositDate}; Balance={Balance} on {BalanceDate}",
                team.TeamId, arbResult.SubscriptionId, split.DepositCharge, depositDate, split.BalanceCharge, balanceDate);
        }

        if (stoppedAt >= 0)
        {
            for (int j = stoppedAt + 1; j < teamsList.Count; j++)
                notAttempted.Add(teamsList[j].TeamId);
        }

        var anySuccess = results.Any(r => r.Registered);
        if (anySuccess)
        {
            await _teams.SaveChangesAsync();
            // Roll team financial deltas onto the rep's Registrations row so the
            // search/balance-due UI reflects the new aggregate immediately.
            await _registrations.SynchronizeClubRepFinancialsAsync(regId, userId);
        }

        var successCount = results.Count(r => r.Registered);
        var allOk = stoppedAt < 0 && successCount == teamIds.Count;

        return new TeamArbTrialPaymentResponseDto
        {
            Success = allOk,
            Error = allOk ? null : (anySuccess ? "PARTIAL_SUCCESS" : "ALL_FAILED"),
            Message = allOk
                ? $"All {successCount} ARB-Trial subscription(s) created"
                : (anySuccess
                    ? $"{successCount} of {teamIds.Count} subscription(s) created; batch stopped at first failure"
                    : "All ARB-Trial submissions failed"),
            Teams = results,
            NotAttempted = notAttempted
        };
    }

    /// <summary>
    /// Fallback path for ARB-Trial when today is on/after the configured balance date.
    /// Charges each team the full netBase + processing as a single ADN transaction
    /// (CC or eCheck depending on which payment method was supplied). No subscription
    /// is created. Capture-what-you-can semantics mirror the trial loop.
    ///
    /// Aligns with the existing team eCheck pattern: stamp team with CC-rate schedule,
    /// then apply the (CC − EC) processing-fee credit via _feeAdj before charging. This
    /// keeps NSF reversal in <see cref="AdnSweepService.ProcessEcheckReturnAsync"/>
    /// uniform with the regular team eCheck flow.
    /// </summary>
    private async Task<TeamArbTrialPaymentResponseDto> ProcessArbTrialFallbackAsync(
        Guid regId,
        string userId,
        List<Domain.Entities.Teams> teams,
        Guid jobId,
        AdnCredentialsViewModel credentials,
        AuthorizeNet.Environment env,
        JobFeeSettings feeSettings,
        CreditCardInfo? creditCard,
        BankAccountInfo? bankAccount)
    {
        var bAddProcessing = feeSettings.BAddProcessingFees ?? true;
        var bApplyToDeposit = feeSettings.BApplyProcessingFeesToTeamDeposit ?? false;
        var ccProcessingRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);
        var ccExpiryDate = creditCard != null ? FormatExpiry(creditCard.Expiry!) : null;
        var last4 = bankAccount?.AccountNumber is { Length: >= 4 } an ? an[^4..] : bankAccount?.AccountNumber;
        var nameOnAcct = bankAccount?.NameOnAccount?.Trim();

        var results = new List<TeamArbTrialResultDto>();
        var notAttempted = new List<Guid>();
        var pendingSettlements = new List<(RegistrationAccounting Ra, string TxId)>();
        var stoppedAt = -1;

        for (int i = 0; i < teams.Count; i++)
        {
            var team = teams[i];

            var resolved = await _feeService.ResolveFeeAsync(
                jobId, RoleConstants.ClubRep, team.AgegroupId, team.TeamId);
            var rawDeposit = resolved?.EffectiveDeposit ?? 0m;
            var rawBalance = resolved?.EffectiveBalanceDue ?? 0m;
            // TotalDiscount() is the same total FeeMath subtracts from FeeTotal, so the split legs
            // foot to it exactly.
            var discount = team.TotalDiscount();
            var lateFee = team.FeeLatefee ?? 0m;
            var donation = team.FeeDonation ?? 0m;

            // Always splits at CC rate. For eCheck, the (CC−EC) credit is applied below
            // via _feeAdj so the team row carries the same processing-fee history that
            // ProcessTeamEcheckPaymentAsync would have written — sweep NSF reversal is
            // identical for both paths.
            var split = ArbTrialFeeSplitter.Split(
                rawDeposit, rawBalance, discount, lateFee, donation, ccProcessingRate,
                bAddProcessingFees: bAddProcessing,
                bApplyProcessingFeesToTeamDeposit: bApplyToDeposit);

            var ccFullCharge = split.DepositCharge + split.BalanceCharge;
            if (ccFullCharge <= 0m)
            {
                results.Add(new TeamArbTrialResultDto
                {
                    TeamId = team.TeamId,
                    Registered = false,
                    FailureReason = "No charge amount derived from team fees"
                });
                stoppedAt = i;
                break;
            }

            // Stamp full CC schedule on the team.
            team.FeeBase = rawDeposit + rawBalance;
            team.FeeProcessing = split.TotalProcessing;
            // RecalcTotals derives FeeTotal from the frozen components (base + proc − discount
            // − discountMp + donation + latefee), which == ccFullCharge. Both sides now net the SAME
            // discount (TotalDiscount() above == what FeeMath subtracts), so there is no divergence.
            team.RecalcTotals();
            team.Modified = DateTime.Now;
            team.LebUserId = userId;

            // For eCheck, drop the (CC − EC) credit onto the team (mutates FeeProcessing,
            // FeeTotal, OwedTotal in-place) and recompute the charge amount. The
            // ReverseTeamProcessingFeeForEcheckAsync path runs symmetrically on NSF.
            decimal chargeAmount = ccFullCharge;
            if (bankAccount != null)
            {
                await _feeAdj.ReduceTeamProcessingFeeForEcheckAsync(team, ccFullCharge, jobId, userId);
                chargeAmount = (team.FeeTotal ?? ccFullCharge) - (team.PaidTotal ?? 0m);
            }

            var invoiceNumber = BuildTeamInvoiceNumber(team);
            var fallbackTeamLabel = team.TeamName ?? team.DisplayName;
            var fallbackClubName = team.ClubrepRegistration?.ClubName?.Trim();
            if (!string.IsNullOrWhiteSpace(fallbackClubName))
                fallbackTeamLabel = $"{fallbackClubName}: {fallbackTeamLabel}";
            var description = $"Team Registration (ARB-Trial fallback): {fallbackTeamLabel}";

            AdnTxnResult chargeResult;
            if (creditCard != null)
            {
                chargeResult = _adnApiService.ADN_Charge_Result(new AdnChargeRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    CardNumber = creditCard.Number!,
                    CardCode = creditCard.Code!,
                    Expiry = ccExpiryDate!,
                    FirstName = creditCard.FirstName!,
                    LastName = creditCard.LastName!,
                    Address = creditCard.Address!,
                    Zip = creditCard.Zip!,
                    Email = creditCard.Email!,
                    Phone = creditCard.Phone!,
                    Amount = chargeAmount,
                    InvoiceNumber = invoiceNumber,
                    Description = description
                });
            }
            else
            {
                chargeResult = _adnApiService.ADN_ChargeBankAccount_Result(new AdnChargeBankAccountRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    AccountType = bankAccount!.AccountType!,
                    RoutingNumber = bankAccount.RoutingNumber!,
                    AccountNumber = bankAccount.AccountNumber!,
                    NameOnAccount = nameOnAcct!,
                    FirstName = bankAccount.FirstName!,
                    LastName = bankAccount.LastName!,
                    Address = bankAccount.Address!,
                    Zip = bankAccount.Zip!,
                    Email = bankAccount.Email!,
                    Phone = bankAccount.Phone!,
                    Amount = chargeAmount,
                    InvoiceNumber = invoiceNumber,
                    Description = description
                });
            }

            if (!chargeResult.Success)
            {
                results.Add(new TeamArbTrialResultDto
                {
                    TeamId = team.TeamId,
                    Registered = false,
                    FailureReason = chargeResult.MessageForUser
                });
                stoppedAt = i;
                _logger.LogWarning("ARB-Trial fallback charge failed: Team={TeamId} Error={Err}",
                    team.TeamId, chargeResult.MessageForUser);
                break;
            }

            var transId = chargeResult.TransactionId!;

            // CC books at submit (clears synchronously). Pessimistic eCheck defers the credit to
            // settlement: the RA below is born Active=false and the sweep credits it when the ACH
            // draft clears (MarkEcheckSettled), reversing via the NSF path if the bank returns it.
            if (creditCard != null)
            {
                team.PaidTotal = (team.PaidTotal ?? 0m) + chargeAmount;
                team.RecalcTotals();
                team.Modified = DateTime.Now;
                team.LebUserId = userId;
            }

            var fullCharge = chargeAmount;

            var pmId = creditCard != null
                ? Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D") // CC payment method
                : EcheckPaymentMethodId;
            var paymeth = creditCard != null
                ? $"paid by cc: {fullCharge:C} on {DateTime.Now:G} txID: {transId}"
                : $"eCheck pending settlement: {fullCharge:C} on {DateTime.Now:G} txID: {transId}";

            var ra = new RegistrationAccounting
            {
                RegistrationId = regId,
                TeamId = team.TeamId,
                Payamt = fullCharge,
                Dueamt = fullCharge,
                Paymeth = paymeth,
                PaymentMethodId = pmId,
                // Pessimistic eCheck: born Active=false (excluded from PaidTotal) until the sweep
                // settles it; CC born Active=true. Payamt carries the amount for the settle credit.
                Active = creditCard != null,
                Createdate = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = userId,
                AdnTransactionId = transId,
                AdnInvoiceNo = invoiceNumber,
                AdnCc4 = creditCard?.Number is { Length: >= 4 } cn ? cn[^4..] : null,
                AdnCcexpDate = ccExpiryDate,
                Comment = description
            };
            _acct.Add(ra);
            if (bankAccount != null) pendingSettlements.Add((ra, transId));

            results.Add(new TeamArbTrialResultDto
            {
                TeamId = team.TeamId,
                Registered = true,
                DepositCharge = fullCharge,
                BalanceCharge = 0m,
                DepositDate = DateTime.Now.Date
            });

            _logger.LogInformation("ARB-Trial fallback charge succeeded: Team={TeamId} Amount={Amount} TransId={TransId}",
                team.TeamId, fullCharge, transId);
        }

        if (stoppedAt >= 0)
        {
            for (int j = stoppedAt + 1; j < teams.Count; j++)
                notAttempted.Add(teams[j].TeamId);
        }

        var anySuccess = results.Any(r => r.Registered);
        if (anySuccess)
        {
            await _teams.SaveChangesAsync();
            await _acct.SaveChangesAsync();

            if (pendingSettlements.Count > 0)
            {
                // Settlement.RegistrationAccountingId = identity-generated AId on the RA;
                // requires the RA inserts above to be saved first.
                var nextCheckAt = DateTime.Now.AddDays(1);
                foreach (var (ra, txId) in pendingSettlements)
                {
                    _settleRepo.Add(new Settlement
                    {
                        SettlementId = Guid.NewGuid(),
                        RegistrationAccountingId = ra.AId,
                        AdnTransactionId = txId,
                        Status = "Pending",
                        SubmittedAt = DateTime.Now,
                        NextCheckAt = nextCheckAt,
                        AccountLast4 = last4,
                        AccountType = bankAccount!.AccountType,
                        NameOnAccount = nameOnAcct,
                        Modified = DateTime.Now,
                        LebUserId = userId
                    });
                }
                await _settleRepo.SaveChangesAsync();
            }

            await _registrations.SynchronizeClubRepFinancialsAsync(regId, userId);
        }

        var successCount = results.Count(r => r.Registered);
        var allOk = stoppedAt < 0 && successCount == teams.Count;

        return new TeamArbTrialPaymentResponseDto
        {
            Success = allOk,
            Mode = "FALLBACK_FULL_CHARGE",
            Error = allOk ? null : (anySuccess ? "PARTIAL_SUCCESS" : "ALL_FAILED"),
            Message = allOk
                ? $"All {successCount} team(s) charged in full (balance date already passed)"
                : (anySuccess
                    ? $"{successCount} of {teams.Count} team(s) charged; batch stopped at first failure"
                    : "All ARB-Trial fallback charges failed"),
            Teams = results,
            NotAttempted = notAttempted
        };
    }

    /// <summary>
    /// Build invoice number for a team payment (pattern: customerAi_jobAi_teamAi),
    /// truncating fallback variants to satisfy the ADN 20-char limit.
    /// </summary>
    private static string BuildTeamInvoiceNumber(Domain.Entities.Teams team)
    {
        var primary = $"{team.Job.Customer.CustomerAi}_{team.Job.JobAi}_{team.TeamAi}";
        if (primary.Length <= 20) return primary;
        var alt = $"{team.Job.JobAi}_{team.TeamAi}";
        if (alt.Length <= 20) return alt;
        return team.TeamAi.ToString();
    }

    /// <summary>
    /// ADN/statement description for a team charge: event name + team so the rep recognizes
    /// the charge on a card statement and in the gateway. Falls back gracefully when names are
    /// missing. ASCII-only separator and capped at ADN's 255-char order.description limit.
    /// </summary>
    private static string BuildTeamChargeDescription(Domain.Entities.Teams team)
    {
        var eventLabel = team.Job.JobName?.Trim();
        var teamLabel = (team.TeamName ?? team.DisplayName)?.Trim();
        if (string.IsNullOrWhiteSpace(teamLabel)) teamLabel = $"Team {team.TeamAi}";
        // Prefix the owning club name when the team is rostered by a club rep:
        // "{Club}: {Team}" mirrors the family accounting ledger label.
        var clubName = team.ClubrepRegistration?.ClubName?.Trim();
        if (!string.IsNullOrWhiteSpace(clubName)) teamLabel = $"{clubName}: {teamLabel}";
        var description = string.IsNullOrWhiteSpace(eventLabel)
            ? $"Team Registration: {teamLabel}"
            : $"{eventLabel} - Team Registration: {teamLabel}";
        return description.Length > 255 ? description[..255] : description;
    }

    /// <summary>
    /// Sanitize-in-place + validate a customer-supplied credit card. Used by the team
    /// ARB-Trial path; mirrors the field-level checks in <see cref="ValidatePaymentRequestInternalAsync"/>.
    /// Returns null Error on success.
    /// </summary>
    private static (string? Error, string? Code) ValidateAndSanitizeCreditCard(CreditCardInfo cc)
    {
        var fields = new (string Name, string? Value)[]
        {
            ("number", cc.Number), ("expiry", cc.Expiry), ("code", cc.Code),
            ("firstName", cc.FirstName), ("lastName", cc.LastName),
            ("address", cc.Address), ("zip", cc.Zip),
            ("email", cc.Email), ("phone", cc.Phone)
        };
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0)
            return ("Missing card field(s): " + string.Join(", ", missing), "CARD_FIELDS_MISSING");
        cc.Expiry = new string((cc.Expiry ?? "").Where(char.IsDigit).ToArray());
        if (cc.Expiry?.Length != 4)
            return ("Invalid expiry format (expected MMYY)", "CARD_EXPIRY_INVALID");
        cc.Phone = new string((cc.Phone ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cc.Phone))
            return ("Invalid phone (digits required)", "CARD_PHONE_INVALID");
        if (string.IsNullOrWhiteSpace(cc.Email) || !cc.Email.Contains('@') || !cc.Email.Contains('.'))
            return ("Invalid email format", "CARD_EMAIL_INVALID");
        return (null, null);
    }

    private static TeamArbTrialPaymentResponseDto FailTrial(string code, string msg) =>
        new TeamArbTrialPaymentResponseDto
        {
            Success = false,
            Error = code,
            Message = msg,
            Teams = new List<TeamArbTrialResultDto>(),
            NotAttempted = new List<Guid>()
        };

    /// <summary>
    /// Split the cart BEFORE charging: keep the players who still have a seat, and move the players
    /// whose team filled up while the family was checking out to the WAITLIST twin at $0 — those are
    /// NOT charged. Only NEW player seats are gated: an already-confirmed reg (balance payer) owns
    /// its seat, and non-players / team-less regs are untouched. A bounced player is re-priced onto
    /// the twin's $0 fee through the existing swap engine (<see cref="IFeeResolutionService.ApplySwapFeesAsync"/>,
    /// the same path the roster swapper uses), dropped from <paramref name="registrations"/> so the
    /// charge never sees it, and returned for the response's NeedsWaitlist bucket. Mutates
    /// <paramref name="registrations"/> in place. Seat availability is the read-only
    /// <see cref="IRegistrationRepository.IsSeatAvailableAsync"/> (confirmed members vs MaxCount).
    /// </summary>
    private Task<List<PaymentWaitlistedDto>> WaitlistFullTeamPlayersAsync(
        Guid jobId, string familyUserId, List<Registrations> registrations, CancellationToken ct = default)
        => SeatReconciliation.ReconcileSeatsAsync(
            jobId, familyUserId, registrations, _registrations, _placement, _teams, _feeService, ct);

    /// <summary>
    /// Overload that accepts jobId and familyUserId extracted from JWT claims.
    /// Creates a temporary request with these values to delegate to existing validation logic.
    /// </summary>
    public async Task<PaymentResponseDto> ProcessPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId)
    {
        // Create internal request with jobId and familyUserId for legacy validation
        var internalRequest = new PaymentRequestDto
        {
            JobPath = request.JobPath,
            PaymentOption = request.PaymentOption,
            CreditCard = request.CreditCard,
            IdempotencyKey = request.IdempotencyKey,
            ViConfirmed = request.ViConfirmed,
            ViPolicyNumber = request.ViPolicyNumber,
            ViPolicyCreateDate = request.ViPolicyCreateDate,
            ViQuoteIds = request.ViQuoteIds,
            ViToken = request.ViToken,
            Donation = request.Donation,
            ExpectedTotal = request.ExpectedTotal
        };

        // Use internal validation that still expects JobId and FamilyUserId
        var v = await ValidatePaymentRequestInternalAsync(jobId, familyUserId, internalRequest);
        if (v.Response != null) return v.Response;
        var job = v.Job!;
        var cc = v.Card!;
        var effective = v.Effective;

        // Registrations already financed by a live subscription are dropped before NormalizeFeesAsync
        // so this transaction neither re-stamps their fees nor charges them a second time.
        var (registrations, enrolled) = PartitionArbEnrolled(v.Registrations!);
        if (registrations.Count == 0) return ArbAlreadyActive(enrolled);

        await NormalizeFeesAsync(registrations, jobId);
        if (effective == PaymentOption.ARB)
            return await ProcessArbAsync(jobId, familyUserId, internalRequest, userId, registrations, job, cc);

        // Split the cart: players whose team filled up while the family checked out are moved to the
        // waitlist twin at $0 (not charged); the seatable players continue to the charge below.
        var needsWaitlist = await WaitlistFullTeamPlayersAsync(jobId, familyUserId, registrations);
        if (registrations.Count == 0)
            return new PaymentResponseDto
            {
                Success = true,
                Message = "Those teams just filled up — the players were placed on the waitlist. No payment was taken.",
                NeedsWaitlist = needsWaitlist
            };

        // Optional donation: the gift lands on ONE primary registration (the first in the
        // charge set) and is charged in full — principal + processing — WITH this payment.
        var donation = internalRequest.Donation ?? 0m;
        var primary = registrations.FirstOrDefault(r => r.RegistrationId != Guid.Empty);
        var hasDonation = donation > 0m && primary != null;

        // PIF chosen → re-stamp registrations with full amount (Deposit + BalanceDue)
        // before computing charges. Validation above already verified ALLOWPIF.
        // Snapshot the PRE-mutation FeeBase/FeeProcessing/FeeTotal/OwedTotal/FeeDonation BEFORE
        // UpgradeRegistrationsToPifAsync OR the donation stamp mutates them — the engine's
        // pre-gateway SaveChangesAsync (PaymentService.cs:~1393) flushes those mutations to the
        // DB along with the placeholder RA, so a declined card otherwise persists the
        // upgraded/donated posture with no charge to back it. The snapshot is passed to
        // ExecutePrimaryChargeAsync for restore on engine failure. (Taken for PIF and/or donation.)
        Dictionary<Guid, (decimal FeeBase, decimal FeeProcessing, decimal FeeTotal, decimal OwedTotal, decimal FeeDonation)>? pifSnapshot = null;
        if (effective == PaymentOption.PIF || hasDonation)
        {
            pifSnapshot = registrations.ToDictionary(
                r => r.RegistrationId,
                r => (r.FeeBase, r.FeeProcessing, r.FeeTotal, r.OwedTotal, r.FeeDonation));
        }

        decimal donationGross = 0m;
        if (hasDonation) primary!.FeeDonation = donation; // single primary carries the gift (idempotent set)

        if (effective == PaymentOption.PIF)
        {
            // PIF recompute re-levies FeeProcessing on the new base AND the stamped donation,
            // so ComputeChargesAsync(PIF) below (== OwedTotal) already bills the gift.
            await UpgradeRegistrationsToPifAsync(registrations, jobId);
        }
        else if (hasDonation)
        {
            // Deposit/non-PIF: the PIF recompute didn't run. Re-levy this registration's
            // processing+totals through the canonical path so proc lands on the donation and
            // OwedTotal carries the proc-inclusive gift. The OwedTotal delta IS that gift; add
            // it to the deposit charge below (the deposit cap in ComputeChargesAsync would
            // otherwise drop it).
            var owedBefore = pifSnapshot![primary!.RegistrationId].OwedTotal;
            await _feeService.RecomputeRegistrationFinancialsAsync(primary, jobId);
            donationGross = primary.OwedTotal - owedBefore;
        }

        var charges = await ComputeChargesAsync(registrations, effective);
        if (donationGross > 0m)
            charges[primary!.RegistrationId] = charges.GetValueOrDefault(primary.RegistrationId) + donationGross;
        var total = charges.Values.Sum();
        if (total <= 0m) return Fail("Nothing due for selected registrations.", "NOTHING_DUE");
        var resp = await ExecutePrimaryChargeAsync(jobId, familyUserId, internalRequest, userId, registrations, cc, charges, total, effective, pifSnapshot, internalRequest.ExpectedTotal);
        return needsWaitlist.Count > 0 ? resp with { NeedsWaitlist = needsWaitlist } : resp;
    }

    /// <summary>
    /// eCheck (ACH) sibling of <see cref="ProcessPaymentAsync"/>. Same fee/charge math,
    /// swaps CC validation + charge for bank-account validation + ADN_ChargeBankAccount,
    /// and writes a Settlement row per RA so the daily sweep can detect both clearance
    /// and NSF returns. ARB is rejected — eCheck-on-ARB is a separate ADN feature.
    /// </summary>
    public async Task<PaymentResponseDto> ProcessEcheckPaymentAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId)
    {
        if (request == null)
            return Fail("Invalid request", "INVALID_REQUEST");
        if (request.PaymentOption == PaymentOption.ARB)
            return Fail("Recurring billing (ARB) is not available for eCheck payments.", "ARB_NOT_ECHECK");
        var v = await ValidateEcheckPaymentRequestAsync(jobId, familyUserId, request);
        if (v.Response != null) return v.Response;
        var bank = v.Bank!;
        var effective = v.Effective;

        // An ARB job that also allows PIF can reach this path: debiting a financed registration's
        // balance by ACH while its subscription keeps drafting the card bills the family twice.
        var (registrations, enrolled) = PartitionArbEnrolled(v.Registrations!);
        if (registrations.Count == 0) return ArbAlreadyActive(enrolled);

        await NormalizeFeesAsync(registrations, jobId);

        // Split the cart: players whose team filled up are moved to the waitlist twin at $0 (not
        // charged); the seatable players continue to the eCheck debit below.
        var needsWaitlist = await WaitlistFullTeamPlayersAsync(jobId, familyUserId, registrations);
        if (registrations.Count == 0)
            return new PaymentResponseDto
            {
                Success = true,
                Message = "Those teams just filled up — the players were placed on the waitlist. No payment was taken.",
                NeedsWaitlist = needsWaitlist
            };

        // Optional donation: same model as the CC path — the gift lands on ONE primary
        // registration and is charged in full (principal + proc) with this eCheck.
        var donation = request.Donation ?? 0m;
        var primary = registrations.FirstOrDefault(r => r.RegistrationId != Guid.Empty);
        var hasDonation = donation > 0m && primary != null;

        // Snapshot the PRE-mutation fee posture (incl. FeeDonation) BEFORE UpgradeRegistrationsToPifAsync
        // OR the donation stamp mutates it, so a declined eCheck rolls back per-reg (CC-symmetric —
        // see ExecutePrimaryChargeAsync). The canonical engine's pre-gateway placeholder-RA flush
        // persists the mutation, so without this a declined eCheck would strand the registration in
        // the upgraded/donated posture with no charge to back it.
        Dictionary<Guid, (decimal FeeBase, decimal FeeProcessing, decimal FeeTotal, decimal OwedTotal, decimal FeeDonation)>? pifSnapshot = null;
        if (effective == PaymentOption.PIF || hasDonation)
        {
            pifSnapshot = registrations.ToDictionary(
                r => r.RegistrationId,
                r => (r.FeeBase, r.FeeProcessing, r.FeeTotal, r.OwedTotal, r.FeeDonation));
        }

        decimal donationGross = 0m;
        if (hasDonation) primary!.FeeDonation = donation; // single primary carries the gift (idempotent set)

        if (effective == PaymentOption.PIF)
        {
            await UpgradeRegistrationsToPifAsync(registrations, jobId);
        }
        else if (hasDonation)
        {
            // Deposit/non-PIF: re-levy proc on the donation via the canonical path; the OwedTotal
            // delta is the proc-inclusive gift, added to the deposit charge below.
            var owedBefore = pifSnapshot![primary!.RegistrationId].OwedTotal;
            await _feeService.RecomputeRegistrationFinancialsAsync(primary, jobId);
            donationGross = primary.OwedTotal - owedBefore;
        }

        var charges = await ComputeChargesAsync(registrations, effective);
        if (donationGross > 0m)
            charges[primary!.RegistrationId] = charges.GetValueOrDefault(primary.RegistrationId) + donationGross;
        var total = charges.Values.Sum();
        if (total <= 0m)
            return Fail("Nothing due for selected registrations.", "NOTHING_DUE");
        var resp = await ExecuteEcheckChargeAsync(jobId, familyUserId, request, userId, registrations, bank, charges, pifSnapshot, request.ExpectedTotal);
        return needsWaitlist.Count > 0 ? resp with { NeedsWaitlist = needsWaitlist } : resp;
    }

    private async Task<(PaymentResponseDto? Response, JobInfo? Job, List<Registrations>? Registrations, BankAccountInfo? Bank, PaymentOption Effective)> ValidateEcheckPaymentRequestAsync(Guid jobId, string familyUserId, PaymentRequestDto request)
    {
        var jobPaymentInfo = await _jobs.GetJobPaymentInfoAsync(jobId);
        if (jobPaymentInfo == null) return (Fail("Invalid job", "INVALID_JOB"), null, null, null, request.PaymentOption);
        if (!jobPaymentInfo.BEnableEcheck) return (Fail("eCheck payments are not enabled for this job.", "ECHECK_NOT_ENABLED"), null, null, null, request.PaymentOption);
        var job = new JobInfo(jobPaymentInfo.AdnArb, jobPaymentInfo.AdnArbbillingOccurences, jobPaymentInfo.AdnArbintervalLength, jobPaymentInfo.AdnArbstartDate, jobPaymentInfo.AllowPif, jobPaymentInfo.BPlayersFullPaymentRequired, jobPaymentInfo.BEnableEcheck);
        var registrations = await _registrations.GetByJobAndFamilyWithUsersAsync(jobId, familyUserId, activePlayersOnly: true);
        if (!registrations.Any()) return (Fail("No registrations found", "NO_REGISTRATIONS"), null, null, null, request.PaymentOption);
        // Team data is the authority on whether a deposit phase exists. When it doesn't,
        // the request's PaymentOption.Deposit value is meaningless — coerce to PIF so
        // ComputeChargesAsync runs against OwedTotal instead of team.deposit (=0). The
        // PIF allow-gate is then only meaningful when there's a deposit alternative
        // being bypassed; when no deposit exists, PIF is the only mode and ungated.
        var hasDeposit = await IsDepositScenarioAsync(registrations);
        var effective = (!hasDeposit && request.PaymentOption == PaymentOption.Deposit) ? PaymentOption.PIF : request.PaymentOption;
        if (effective == PaymentOption.PIF && hasDeposit && !job.AllowPif && !job.BPlayersFullPaymentRequired) return (Fail("Pay In Full is not enabled for this job", "PIF_NOT_ALLOWED"), null, null, null, effective);
        var bank = request.BankAccount;
        var (bankErr, bankCode) = NormalizeAndValidateBankAccount(bank);
        if (bankErr != null) return (Fail(bankErr, bankCode!), null, null, null, effective);
        return (null, job, registrations, bank, effective);
    }

    /// <summary>
    /// Sanitize-in-place + validate a customer-supplied bank account. Used by both player
    /// and team eCheck paths. Returns null Error on success.
    /// </summary>
    private static (string? Error, string? Code) NormalizeAndValidateBankAccount(BankAccountInfo? bank)
    {
        if (bank == null) return ("Bank account required", "BANK_REQUIRED");
        var fields = new (string Name, string? Value)[]
        {
            ("accountType", bank.AccountType), ("routingNumber", bank.RoutingNumber), ("accountNumber", bank.AccountNumber),
            ("nameOnAccount", bank.NameOnAccount), ("firstName", bank.FirstName), ("lastName", bank.LastName),
            ("address", bank.Address), ("zip", bank.Zip), ("email", bank.Email), ("phone", bank.Phone)
        };
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0) return ("Missing bank field(s): " + string.Join(", ", missing), "BANK_FIELDS_MISSING");
        var atype = bank.AccountType!.Trim().ToLowerInvariant();
        if (atype is not ("checking" or "savings" or "businesschecking"))
            return ("Invalid account type (checking, savings, or businessChecking).", "BANK_TYPE_INVALID");
        bank.RoutingNumber = new string((bank.RoutingNumber ?? "").Where(char.IsDigit).ToArray());
        if (bank.RoutingNumber.Length != 9) return ("Routing number must be 9 digits.", "BANK_ROUTING_INVALID");
        bank.AccountNumber = new string((bank.AccountNumber ?? "").Where(char.IsLetterOrDigit).ToArray());
        if (bank.AccountNumber.Length is < 4 or > 17) return ("Account number must be 4–17 characters.", "BANK_ACCOUNT_INVALID");
        if (bank.NameOnAccount!.Trim().Length > 22) return ("Name on account exceeds 22 characters.", "BANK_NAME_INVALID");
        bank.Phone = new string((bank.Phone ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(bank.Phone)) return ("Invalid phone (digits required).", "BANK_PHONE_INVALID");
        if (string.IsNullOrWhiteSpace(bank.Email) || !bank.Email.Contains('@') || !bank.Email.Contains('.'))
            return ("Invalid email format.", "BANK_EMAIL_INVALID");
        return (null, null);
    }

    /// <summary>
    /// eCheck (ACH) sibling of <see cref="ExecutePrimaryChargeAsync"/>. Routes the player
    /// self-pay through the SAME canonical per-registration engine the CC path uses
    /// (<see cref="ChargeRegistrationsCoreAsync"/> with <see cref="RegistrationChargeKind.Echeck"/>),
    /// so eCheck inherits the placeholder-RA audit trail, the ResolveOwed amount tripwire,
    /// per-registration transactions (granular refunds/NSF), and per-reg partial success.
    /// The only eCheck-specific concerns left here are the PIF rollback-on-decline and the
    /// pending-settlement confirmation email.
    /// </summary>
    private async Task<PaymentResponseDto> ExecuteEcheckChargeAsync(
        Guid jobId, string familyUserId, PaymentRequestDto request, string userId,
        List<Registrations> registrations, BankAccountInfo bank, Dictionary<Guid, decimal> charges,
        Dictionary<Guid, (decimal FeeBase, decimal FeeProcessing, decimal FeeTotal, decimal OwedTotal, decimal FeeDonation)>? pifSnapshot,
        decimal? expectedTotal)
    {
        var items = charges
            .Where(kv => kv.Value > 0m && kv.Key != Guid.Empty)
            .Select(kv => new RegistrationChargeItem { RegistrationId = kv.Key, Amount = kv.Value })
            .ToList();

        var result = await ChargeRegistrationsCoreAsync(
            jobId, items, RegistrationChargeKind.Echeck, creditCard: null, bankAccount: bank, userId,
            expectedTotal: expectedTotal);

        if (!result.Success)
        {
            // Per-reg partial success applies: restore the pre-PIF posture ONLY for the
            // registrations whose eCheck submission declined (a captured reg keeps its upgrade).
            await RestoreFailedPifRegsAsync(registrations, pifSnapshot, result.Outcomes);
            return new PaymentResponseDto
            {
                Success = false,
                Message = result.Message ?? "eCheck submission failed",
                ErrorCode = result.ErrorCode ?? "CHARGE_GATEWAY_ERROR",
                // Per-player outcomes only for true gateway declines (partial/total) — not for
                // pre-charge validation failures (AMOUNT_CHANGED etc.), which the error banner covers.
                Outcomes = result.ErrorCode == "CHARGE_GATEWAY_ERROR"
                    ? await BuildChargeOutcomeDtosAsync(jobId, result.Outcomes, registrations)
                    : null
            };
        }

        await TrySendConfirmationEmailAsync(jobId, familyUserId, userId, isEcheckPending: true);
        return new PaymentResponseDto
        {
            Success = true,
            Message = "eCheck submitted; settlement pending (typically 3–5 business days).",
            TransactionId = result.TransactionId
        };
    }

    /// <summary>
    /// Project the canonical engine's per-registration outcomes into the client-facing
    /// <see cref="RegistrationChargeOutcomeDto"/> list, resolving best-effort player + team
    /// names (one batched team lookup) so the frontend can show an itemized "who charged /
    /// who declined" panel without a second round-trip. Used on every non-full-success return.
    /// </summary>
    private async Task<List<RegistrationChargeOutcomeDto>> BuildChargeOutcomeDtosAsync(
        Guid jobId,
        IReadOnlyList<RegistrationCcChargeOutcome> outcomes,
        IReadOnlyCollection<Registrations> registrations)
    {
        var regById = registrations.ToDictionary(r => r.RegistrationId);
        var teamIds = registrations
            .Where(r => r.AssignedTeamId.HasValue)
            .Select(r => r.AssignedTeamId!.Value)
            .Distinct()
            .ToList();
        var teamList = teamIds.Count > 0 ? await _teams.GetTeamsForJobAsync(jobId, teamIds) : null;
        var teamNames = teamList?.ToDictionary(t => t.TeamId, t => t.TeamName ?? string.Empty)
            ?? new Dictionary<Guid, string>();

        return outcomes.Select(o =>
        {
            regById.TryGetValue(o.RegistrationId, out var reg);
            var teamName = reg?.AssignedTeamId is { } tid && teamNames.TryGetValue(tid, out var tn)
                ? tn : string.Empty;
            return new RegistrationChargeOutcomeDto
            {
                RegistrationId = o.RegistrationId,
                PlayerName = reg?.InsuredName ?? string.Empty,
                TeamName = teamName,
                Charged = o.Success,
                ChargedAmount = o.ChargedAmount,
                FailureReason = o.Success ? null : o.Error
            };
        }).ToList();
    }

    /// <summary>
    /// Restore the pre-PIF fee posture for the registrations whose charge declined. The
    /// canonical engine's pre-gateway placeholder-RA flush persists the PIF mutation, so a
    /// declined card/eCheck otherwise strands the registration in PIF posture with no charge
    /// to back it. Captured registrations keep their upgraded posture. No-op when no PIF
    /// snapshot was taken (Deposit path). Shared by the CC and eCheck self-pay wrappers.
    /// </summary>
    private async Task RestoreFailedPifRegsAsync(
        IEnumerable<Registrations> registrations,
        Dictionary<Guid, (decimal FeeBase, decimal FeeProcessing, decimal FeeTotal, decimal OwedTotal, decimal FeeDonation)>? pifSnapshot,
        IReadOnlyList<RegistrationCcChargeOutcome> outcomes)
    {
        if (pifSnapshot == null) return;
        var failedRegIds = outcomes.Where(o => !o.Success).Select(o => o.RegistrationId).ToHashSet();
        var restored = false;
        foreach (var reg in registrations)
        {
            if (!failedRegIds.Contains(reg.RegistrationId)) continue;
            if (!pifSnapshot.TryGetValue(reg.RegistrationId, out var snap)) continue;
            reg.FeeBase       = snap.FeeBase;
            reg.FeeProcessing = snap.FeeProcessing;
            reg.FeeDonation   = snap.FeeDonation;
            // PIF mutates FeeBase/FeeProcessing; a payment-time donation mutates FeeDonation
            // (+ FeeProcessing) on the primary reg. Restoring those three lets RecalcTotals
            // reproduce snap.FeeTotal/OwedTotal exactly (discount/latefee untouched by both).
            reg.RecalcTotals();
            restored = true;
        }
        if (restored) await _registrations.SaveChangesAsync();
    }

    /// <summary>
    /// Internal validation method accepting jobId and familyUserId parameters directly.
    /// Used by the overload to avoid DTO field dependencies.
    /// </summary>
    private async Task<(PaymentResponseDto? Response, JobInfo? Job, List<Registrations>? Registrations, CreditCardInfo? Card, PaymentOption Effective)> ValidatePaymentRequestInternalAsync(Guid jobId, string familyUserId, PaymentRequestDto request)
    {
        if (request == null) return (Fail("Invalid request", "INVALID_REQUEST"), null, null, null, PaymentOption.PIF);
        var jobPaymentInfo = await _jobs.GetJobPaymentInfoAsync(jobId);
        if (jobPaymentInfo == null) return (Fail("Invalid job", "INVALID_JOB"), null, null, null, request.PaymentOption);
        var job = new JobInfo(jobPaymentInfo.AdnArb, jobPaymentInfo.AdnArbbillingOccurences, jobPaymentInfo.AdnArbintervalLength, jobPaymentInfo.AdnArbstartDate, jobPaymentInfo.AllowPif, jobPaymentInfo.BPlayersFullPaymentRequired, jobPaymentInfo.BEnableEcheck);
        var registrations = await _registrations.GetByJobAndFamilyWithUsersAsync(jobId, familyUserId, activePlayersOnly: true);
        if (!registrations.Any()) return (Fail("No registrations found", "NO_REGISTRATIONS"), null, null, null, request.PaymentOption);
        // Team data is the authority on whether a deposit phase exists. When it doesn't,
        // the request's PaymentOption.Deposit value is meaningless — coerce to PIF so
        // ComputeChargesAsync runs against OwedTotal instead of team.deposit (=0). The
        // PIF allow-gate (ALLOWPIF / BPlayersFullPaymentRequired) is then only meaningful
        // when there's a deposit alternative being bypassed; when no deposit exists,
        // PIF is the only mode and ungated.
        // NOTE: this gate stays JOB-LEVEL on purpose (not the per-scope resolved phase).
        // A family payment can span multiple scopes; a full-payment-required scope is
        // already stamped at full (no deposit offered), so its regs never reach this gate,
        // and deposit-phase regs resolve to the job baseline anyway. Per-scope here would
        // add risk for zero behavioral change.
        var hasDeposit = await IsDepositScenarioAsync(registrations);
        var effective = (!hasDeposit && request.PaymentOption == PaymentOption.Deposit) ? PaymentOption.PIF : request.PaymentOption;
        if (effective == PaymentOption.PIF && hasDeposit && !job.AllowPif && !job.BPlayersFullPaymentRequired) return (Fail("Pay In Full is not enabled for this job", "PIF_NOT_ALLOWED"), null, null, null, effective);
        if (effective == PaymentOption.ARB && job.AdnArb != true) return (Fail("Recurring billing (ARB) not enabled", "ARB_NOT_ENABLED"), null, null, null, effective);
        var cc = request.CreditCard;
        if (cc == null) return (Fail("Credit card required", "CARD_REQUIRED"), null, null, null, effective);
        var fields = new (string Name, string? Value)[] { ("number", cc.Number), ("expiry", cc.Expiry), ("code", cc.Code), ("firstName", cc.FirstName), ("lastName", cc.LastName), ("address", cc.Address), ("zip", cc.Zip), ("email", cc.Email), ("phone", cc.Phone) };
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0) return (Fail("Missing card field(s): " + string.Join(", ", missing), "CARD_FIELDS_MISSING"), null, null, null, effective);
        cc.Expiry = new string((cc.Expiry ?? "").Where(char.IsDigit).ToArray());
        if (cc.Expiry?.Length != 4) return (Fail("Invalid expiry format (expected MMYY)", "CARD_EXPIRY_INVALID"), null, null, null, effective);
        // Sanitize phone to digits only
        cc.Phone = new string((cc.Phone ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cc.Phone)) return (Fail("Invalid phone (digits required)", "CARD_PHONE_INVALID"), null, null, null, effective);
        // Basic email sanity check (contains '@' and '.')
        if (string.IsNullOrWhiteSpace(cc.Email) || !cc.Email.Contains('@') || !cc.Email.Contains('.'))
            return (Fail("Invalid email format", "CARD_EMAIL_INVALID"), null, null, null, effective);
        return (null, job, registrations, cc, effective);
    }

    private async Task<PaymentResponseDto> ProcessArbAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId, List<Registrations> registrations, JobInfo job, CreditCardInfo cc)
    {
        // Callers hand us a set already stripped of live-subscription registrations (PartitionArbEnrolled),
        // so every reg here still needs financing.
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
        {
            return new PaymentResponseDto { Success = false, Message = "Missing payment gateway credentials (Authorize.Net).", ErrorCode = "MISSING_GATEWAY_CREDS" };
        }
        var env = _adnApiService.GetADNEnvironment();

        // ARB create only validates schema, not the card itself; declines wouldn't
        // surface until occurrence #1 fires next-day batch — by which point the
        // registration looks active with no money received. Penny-verify forces a
        // synchronous decline at checkout.
        var verifyResult = _adnApiService.ADN_VerifyCardWithPennyAuth(new AdnAuthorizeRequest
        {
            Env = env,
            LoginId = credentials.AdnLoginId!,
            TransactionKey = credentials.AdnTransactionKey!,
            CardNumber = cc.Number!,
            CardCode = cc.Code!,
            Expiry = FormatExpiry(cc.Expiry!),
            FirstName = cc.FirstName!,
            LastName = cc.LastName!,
            Address = cc.Address!,
            Zip = cc.Zip!,
            Amount = 0.01m
        });
        if (!verifyResult.Success)
            return Fail($"Card validation failed: {verifyResult.ErrorMessage}", "CARD_VERIFY_FAILED");

        var schedule = BuildArbSchedule(job.AdnArbbillingOccurences, job.AdnArbintervalLength, job.AdnArbstartDate);
        var (occur, intervalLen, start) = schedule;
        NormalizeProcessingFees(registrations, jobId, userId);
        await _registrations.SaveChangesAsync();
        var args = new ArbSubArgs(env, credentials.AdnLoginId!, credentials.AdnTransactionKey!, occur, intervalLen, start, cc, userId);
        var (subs, failed, activatedNoCharge) = await CreateArbSubscriptionsAsync(registrations, args);
        await _registrations.SaveChangesAsync();
        var response = BuildArbResponse(subs, failed, activatedNoCharge);
        // Attempt confirmation email after any successful completion — whether a subscription was
        // created or the registrant was activated with nothing left to finance.
        if (response.Success && (subs.Count > 0 || activatedNoCharge.Count > 0))
        {
            await TrySendConfirmationEmailAsync(jobId, familyUserId, userId);
        }
        return response;
    }

    /// <summary>
    /// Canonical single per-player CC charge engine. One ADN_Charge for the whole list:
    /// parent self-pay sends N regs in one transaction (preserving the single statement
    /// entry the parent saw on the wizard summary); admin admin-charge passes a single-
    /// element list. Every consumer (parent wizard, admin modal) goes through this method
    /// so the displayed total, the amount charged, and the recorded RA row cannot drift.
    ///
    /// Audit trail: a placeholder RA row is inserted (Payamt=0, Active=true) BEFORE the
    /// gateway call. On ADN failure the same row is marked Active=false with
    /// Comment="FAILED: …" so a director can see a card was tried and declined. On
    /// success the row is updated with Payamt, AdnTransactionId, AdnInvoiceNo, AdnCc4,
    /// AdnCcexpDate, Paymeth and the registration's PaidTotal/OwedTotal advance.
    ///
    /// Each item.Amount is validated against PaymentState.ResolveOwed.Cc — the same
    /// resolver the display path uses. A stale UI value larger than the current Cc bucket
    /// trips AMOUNT_MISMATCH and no gateway hit occurs. Caller-side concerns (RegSaver
    /// policy stamping, confirmation email) stay outside this method.
    /// </summary>
    public Task<RegistrationCcChargeResult> ChargeRegistrationsCcAsync(
        Guid jobId,
        IReadOnlyList<RegistrationChargeItem> items,
        CreditCardInfo creditCard,
        string userId,
        CancellationToken ct = default,
        decimal? expectedTotal = null)
        => ChargeRegistrationsCoreAsync(jobId, items, RegistrationChargeKind.Cc, creditCard, bankAccount: null, userId, ct, expectedTotal);

    /// <summary>
    /// Canonical per-registration charge engine, shared by CC and eCheck (the <paramref name="kind"/>
    /// switch) and by parent self-pay + admin charge. ONE ADN transaction per registration so
    /// refunds and NSF returns stay granular. eCheck differs only by: the per-job eCheck rate
    /// (the proc credit is backed out of FeeProcessing/FeeTotal so the gateway is debited the
    /// eCheck gross, never the CC gross), the bankAccount gateway object, and a post-success
    /// Settlement row per captured RA. Everything else — the placeholder-RA audit trail, the
    /// ResolveOwed AMOUNT_MISMATCH tripwire, per-reg partial success, the PaidTotal/OwedTotal
    /// bumps, the BActive flip — is identical across both methods. Caller-side concerns (RegSaver
    /// stamping, confirmation email, PIF rollback) stay outside this method.
    /// </summary>
    private async Task<RegistrationCcChargeResult> ChargeRegistrationsCoreAsync(
        Guid jobId,
        IReadOnlyList<RegistrationChargeItem> items,
        RegistrationChargeKind kind,
        CreditCardInfo? creditCard,
        BankAccountInfo? bankAccount,
        string userId,
        CancellationToken ct = default,
        decimal? expectedTotal = null)
    {
        if (items == null || items.Count == 0)
            return FailCcResult("NO_ITEMS", "No registrations to charge.", Array.Empty<Guid>());
        if (items.Any(i => i.Amount <= 0m))
            return FailCcResult("INVALID_AMOUNT", "Charge amounts must be > $0.00.", items.Select(i => i.RegistrationId));
        if (items.Select(i => i.RegistrationId).Distinct().Count() != items.Count)
            return FailCcResult("DUPLICATE_REGS", "Duplicate registrations in charge batch.", items.Select(i => i.RegistrationId));

        var regIds = items.Select(i => i.RegistrationId).ToList();
        var registrations = await _registrations.GetByIdsAsync(regIds, ct);
        if (registrations.Count != items.Count)
            return FailCcResult("REG_NOT_FOUND", "One or more registrations not found.", regIds);
        if (registrations.Any(r => r.JobId != jobId))
            return FailCcResult("REG_WRONG_JOB", "Registration does not belong to this job.", regIds);

        var states = await _paymentState.ForRegistrationsAsync(regIds, jobId, ct);
        var rateRef = states.Values.FirstOrDefault()
            ?? await _paymentState.ForRegistrationAsync(regIds[0], jobId, ct);
        var emptyState = PaymentState.Empty(rateRef.BAddProcessingFees, rateRef.CcRate, rateRef.EcheckRate);
        var regsById = registrations.ToDictionary(r => r.RegistrationId);

        // ── Charge-entry realize: auto-activated late fee (Phase 2) ──
        // Player twin of the team path: re-derive each reg's effective late fee from the LIVE
        // cascade before sizing the charge, so a late-fee window that opened without a director
        // reprice lands at payment. The fee cascade is keyed off the TEAM's agegroup (same source
        // the reprice engine uses), so resolve team → agegroup for the regs being charged. DRY: the
        // same swap applier, idempotent for a paying reg (only FeeLatefee + proc/totals move).
        // Persist now so a stale client total trips the mismatch below and the family's refresh
        // shows the realized owed. Inert (no SQL) when no window is active or the reg is paid up.
        var chargeTeamIds = registrations
            .Where(r => r.AssignedTeamId.HasValue).Select(r => r.AssignedTeamId!.Value).Distinct().ToList();
        if (chargeTeamIds.Count > 0)
        {
            var chargeTeams = await _teams.GetTeamsWithJobAndCustomerAsync(jobId, chargeTeamIds);
            var teamAgegroups = (chargeTeams ?? [])
                .ToDictionary(t => t.TeamId, t => t.AgegroupId);
            foreach (var reg in registrations)
            {
                if (reg.AssignedTeamId is Guid tId && teamAgegroups.TryGetValue(tId, out var agId))
                    await _feeService.RealizeLateFeeAtChargeAsync(reg, jobId, agId, tId, ct);
            }
            await _registrations.SaveChangesAsync();
        }

        // Per-item plan: the method-correct gateway charge + the proc credit to back out.
        // CC → credit 0, charge == item.Amount. eCheck → credit = ProcCreditForCharge (the
        // CC-rate proc embedded in this charge converted to the eCheck rate), so the gateway
        // is debited the eCheck gross while FeeProcessing/FeeTotal drop by the same credit.
        var plan = new Dictionary<Guid, (decimal Charge, decimal Credit)>(items.Count);
        foreach (var item in items)
        {
            var reg = regsById[item.RegistrationId];
            var state = states.GetValueOrDefault(item.RegistrationId) ?? emptyState;
            var regDiscount = reg.TotalDiscount();
            var owed = state.ResolveOwed(reg.OwedTotal, reg.FeeBase, regDiscount, reg.FeeLatefee, reg.FeeDonation, reg.FeeProcessing);
            // Tripwire: never charge more than the resolver currently shows for the CC bucket
            // (item.Amount is always the CC-basis charge). Penny tolerance covers rounding
            // between the display path and here.
            if (item.Amount > owed.Cc + 0.01m)
                return FailCcResult(
                    "AMOUNT_MISMATCH",
                    $"Payment amount is out of date (requested {item.Amount:C}, now {owed.Cc:C}). Please refresh and try again.",
                    regIds);
            var credit = kind == RegistrationChargeKind.Echeck
                ? state.ProcCreditForCharge(item.Amount, reg.FeeBase, regDiscount, reg.FeeLatefee, reg.FeeDonation, reg.FeeProcessing, state.EcheckRate)
                : 0m;
            plan[item.RegistrationId] = (item.Amount - credit, credit);
        }

        // ── Promise guard: charge exactly what the screen showed, or refuse ──
        // The self-pay flow computes the displayed total CLIENT-side and the charge SERVER-side
        // (from PaymentOption) independently — nothing structural forces them to agree, which is
        // how the deposit-proc bug billed $200 against a $207 screen. This is the one point both
        // converge: plan[reg].Charge is the real per-reg gateway debit (CC gross for CC, ACH debit
        // for eCheck), so its sum is precisely what is about to hit the customer's account. If that
        // disagrees with expectedTotal (the Pay-button amount) beyond per-item rounding, REFUSE the
        // whole batch before any gateway hit or placeholder RA — we never charge a number we did not
        // promise. Bidirectional (catches under- AND over-charge; the ResolveOwed tripwire above is
        // one-sided). null expectedTotal (admin charge, direct engine tests) skips the guard. The
        // tolerance scales by item count to absorb aggregate-vs-per-item proc rounding (e.g. eCheck
        // credits) without masking dollar-level drift.
        if (expectedTotal.HasValue)
        {
            var gatewayTotal = plan.Values.Sum(p => p.Charge);
            var tolerance = 0.01m * (plan.Count + 1);
            if (Math.Abs(gatewayTotal - expectedTotal.Value) > tolerance)
                return FailCcResult(
                    "AMOUNT_CHANGED",
                    $"The amount changed since this page loaded (shown {expectedTotal.Value:C}, now {gatewayTotal:C}). Please refresh and try again.",
                    regIds);
        }

        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
            return FailCcResult("MISSING_GATEWAY_CREDS", "Payment gateway credentials not configured.", regIds);
        var env = _adnApiService.GetADNEnvironment();

        // Audit trail: placeholder RA rows exist in the DB BEFORE the gateway hit so a
        // declined card leaves a row with Active=false + Comment="FAILED: …" instead of
        // vanishing without record.
        // Active semantics diverge by tender: CC is born Active=true (credited at submit).
        // eCheck is born Active=false — PESSIMISTIC: the row carries the real Payamt but is
        // EXCLUDED from the PaidTotal ledger sum (GetPaymentTotalsByEntityAsync sums Active==true)
        // until the daily sweep confirms settlement and flips it Active=true (MarkEcheckSettled).
        var methodId = kind == RegistrationChargeKind.Cc ? CcPaymentMethodId : EcheckPaymentMethodId;
        var rasByRegId = new Dictionary<Guid, RegistrationAccounting>(items.Count);
        foreach (var item in items)
        {
            var ra = new RegistrationAccounting
            {
                RegistrationId = item.RegistrationId,
                PaymentMethodId = methodId,
                Dueamt = plan[item.RegistrationId].Charge,
                Payamt = 0m,
                Active = kind == RegistrationChargeKind.Cc,
                Createdate = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = userId
            };
            _acct.Add(ra);
            rasByRegId[item.RegistrationId] = ra;
        }
        await _acct.SaveChangesAsync();

        // CC-only card metadata; null for eCheck (a bank account has no expiry / card last-4).
        var expiry = kind == RegistrationChargeKind.Cc ? FormatExpiry(creditCard!.Expiry!) : null;
        var ccLast4 = kind == RegistrationChargeKind.Cc ? Last4(creditCard!.Number) : null;
        var bankLast4 = bankAccount?.AccountNumber is { Length: >= 4 } bacct ? bacct[^4..] : bankAccount?.AccountNumber;
        var bankNameOnAcct = bankAccount?.NameOnAccount?.Trim();
        // eCheck collects a Settlement row per captured RA (after the post-loop flush, when
        // RA.AId is assigned); null for CC.
        var pendingSettlements = kind == RegistrationChargeKind.Echeck
            ? new List<(RegistrationAccounting Ra, string TxId)>(items.Count)
            : null;

        // Per-player charge: ONE ADN transaction per registration so refunds and
        // adjustments stay granular (mirrors legacy, which charged each player
        // independently). Charges are independent — a card can capture player 1 and
        // decline player 2 mid-batch; we persist every successful capture and mark
        // only the declined rows FAILED. No rollback of money already taken: the
        // parent retries the declined player(s) from the (now reduced) owed balance.
        var outcomes = new List<RegistrationCcChargeOutcome>(items.Count);
        string? firstTransId = null;
        string? firstInvoiceNo = null;
        string? firstError = null;
        var succeeded = 0;

        foreach (var item in items)
        {
            var reg = regsById[item.RegistrationId];
            var ra = rasByRegId[item.RegistrationId];
            var invoiceNumber = await BuildInvoiceNumberForRegistrationAsync(jobId, item.RegistrationId);
            var description = await BuildChargeDescriptionAsync(reg);
            firstInvoiceNo ??= invoiceNumber;

            var charge = plan[item.RegistrationId].Charge;
            var chargeResult = kind == RegistrationChargeKind.Cc
                ? _adnApiService.ADN_Charge_Result(new AdnChargeRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    CardNumber = creditCard!.Number!,
                    CardCode = creditCard.Code!,
                    Expiry = expiry!,
                    FirstName = creditCard.FirstName!,
                    LastName = creditCard.LastName!,
                    Address = creditCard.Address!,
                    Zip = creditCard.Zip!,
                    Email = creditCard.Email!,
                    Phone = creditCard.Phone!,
                    Amount = charge,
                    InvoiceNumber = invoiceNumber,
                    Description = description
                })
                : _adnApiService.ADN_ChargeBankAccount_Result(new AdnChargeBankAccountRequest
                {
                    Env = env,
                    LoginId = credentials.AdnLoginId!,
                    TransactionKey = credentials.AdnTransactionKey!,
                    AccountType = bankAccount!.AccountType!,
                    RoutingNumber = bankAccount.RoutingNumber!,
                    AccountNumber = bankAccount.AccountNumber!,
                    NameOnAccount = bankNameOnAcct!,
                    FirstName = bankAccount.FirstName!,
                    LastName = bankAccount.LastName!,
                    Address = bankAccount.Address!,
                    Zip = bankAccount.Zip!,
                    Email = bankAccount.Email!,
                    Phone = bankAccount.Phone!,
                    Amount = charge,
                    InvoiceNumber = invoiceNumber,
                    Description = description
                });

            if (!chargeResult.Success)
            {
                var err = chargeResult.MessageForUser;
                firstError ??= err;
                ra.Active = false;
                ra.Comment = $"FAILED: {err}";
                ra.Modified = DateTime.Now;
                outcomes.Add(new RegistrationCcChargeOutcome
                {
                    RegistrationId = item.RegistrationId,
                    Success = false,
                    Error = err
                });
                _logger.LogWarning("Player {Kind} charge declined: Job={JobId} Reg={RegId} Error={Error}", kind, jobId, item.RegistrationId, err);
                continue;
            }

            var transId = chargeResult.TransactionId!;
            firstTransId ??= transId;
            succeeded++;

            var credit = plan[item.RegistrationId].Credit;
            ra.Payamt = charge;
            ra.AdnTransactionId = transId;
            ra.AdnInvoiceNo = invoiceNumber;
            ra.AdnCc4 = ccLast4;
            ra.AdnCcexpDate = expiry;
            ra.Paymeth = kind == RegistrationChargeKind.Cc
                ? $"paid by cc: {charge:C} on {DateTime.Now:G} txID: {transId}"
                : $"eCheck pending settlement: {charge:C} on {DateTime.Now:G} txID: {transId} (acct ****{bankLast4})";
            ra.Comment = kind == RegistrationChargeKind.Cc
                ? "Registration Payment"
                : "eCheck Registration Payment (Pending Settlement)";
            ra.Modified = DateTime.Now;

            // eCheck: convert the CC-rate proc embedded in this charge to the eCheck rate by
            // dropping FeeProcessing by the credit; RecalcTotals re-derives FeeTotal + OwedTotal
            // from components below. No-op for CC.
            if (credit > 0m)
            {
                reg.FeeProcessing -= credit;
            }
            // Pessimistic eCheck: DO NOT credit PaidTotal at submit — the ACH draft has not
            // cleared. The pending RA above (Active=false) carries the amount; the sweep credits
            // it at settlement (MarkEcheckSettled flips Active=true + recomputes). CC clears
            // synchronously, so it books here exactly as before. RecalcTotals still runs for both:
            // eCheck's OwedTotal reflects the reduced proc fee while the charge stays owed-until-settled.
            if (kind == RegistrationChargeKind.Cc)
            {
                reg.PaidTotal += charge;
            }
            reg.RecalcTotals();
            // Flip the registration active. Pre-refactor parent CC went through
            // UpdateRegistrationsForCharge which set this; the canonical engine
            // success branch must match — every consumer (rosters, coach views,
            // batch email, division counts, JobFilterTree, ARB) gates on BActive==true.
            reg.BActive = true;
            reg.Modified = DateTime.Now;
            reg.LebUserId = userId;

            pendingSettlements?.Add((ra, transId));

            outcomes.Add(new RegistrationCcChargeOutcome
            {
                RegistrationId = item.RegistrationId,
                Success = true,
                ChargedAmount = charge
            });
        }

        // One flush covers every RA row (captures AND FAILED placeholders) and the
        // per-reg PaidTotal/OwedTotal bumps from the successful charges.
        await _acct.SaveChangesAsync();
        await _registrations.SaveChangesAsync();

        // eCheck-only: one Settlement row per captured RA so the daily sweep can detect
        // clearance and NSF returns PER registration. RA.AId is identity-generated, so the
        // flush above must precede this.
        if (pendingSettlements is { Count: > 0 })
        {
            var nextCheckAt = DateTime.Now.AddDays(1);
            foreach (var (ra, txId) in pendingSettlements)
            {
                _settleRepo.Add(new Settlement
                {
                    SettlementId = Guid.NewGuid(),
                    RegistrationAccountingId = ra.AId,
                    AdnTransactionId = txId,
                    Status = "Pending",
                    SubmittedAt = DateTime.Now,
                    NextCheckAt = nextCheckAt,
                    AccountLast4 = bankLast4,
                    AccountType = bankAccount!.AccountType,
                    NameOnAccount = bankNameOnAcct,
                    Modified = DateTime.Now,
                    LebUserId = userId
                });
            }
            await _settleRepo.SaveChangesAsync();
        }

        var failed = items.Count - succeeded;
        if (failed == 0)
        {
            _logger.LogInformation("Player {Kind} charge OK: Job={JobId} Regs={Count} (per-reg tx)", kind, jobId, items.Count);
            return new RegistrationCcChargeResult
            {
                Success = true,
                TransactionId = firstTransId,
                InvoiceNumber = firstInvoiceNo,
                Message = items.Count == 1
                    ? "Payment processed"
                    : $"All {items.Count} registration payment(s) processed",
                Outcomes = outcomes
            };
        }

        // Partial or total failure. Any successful captures are already persisted; the
        // caller restores pre-PIF state only for the declined regs (see
        // ExecutePrimaryChargeAsync). A single declined card (admin path) yields the raw
        // gateway message so the existing UX is unchanged.
        var message = succeeded == 0
            ? (firstError ?? "Payment failed")
            : $"{succeeded} of {items.Count} registration payment(s) processed; {failed} declined.";
        _logger.LogWarning("Player {Kind} charge incomplete: Job={JobId} OK={Ok} Failed={Failed}", kind, jobId, succeeded, failed);
        return new RegistrationCcChargeResult
        {
            Success = false,
            ErrorCode = "CHARGE_GATEWAY_ERROR",
            Message = message,
            TransactionId = firstTransId,
            InvoiceNumber = firstInvoiceNo,
            Outcomes = outcomes
        };
    }

    private static RegistrationCcChargeResult FailCcResult(string code, string msg, IEnumerable<Guid> regIds) =>
        new()
        {
            Success = false,
            ErrorCode = code,
            Message = msg,
            Outcomes = regIds.Select(id => new RegistrationCcChargeOutcome
            {
                RegistrationId = id,
                Success = false,
                Error = msg
            }).ToList()
        };

    private async Task<PaymentResponseDto> ExecutePrimaryChargeAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId, List<Registrations> registrations, CreditCardInfo cc, Dictionary<Guid, decimal> charges, decimal total, PaymentOption effectiveOption, Dictionary<Guid, (decimal FeeBase, decimal FeeProcessing, decimal FeeTotal, decimal OwedTotal, decimal FeeDonation)>? pifSnapshot, decimal? expectedTotal)
    {
        // Route the parent self-pay through the same canonical engine the admin path
        // uses. The primitive owns ResolveOwed validation, the placeholder-RA audit
        // trail, the single ADN call, and the per-reg PaidTotal/OwedTotal bumps.
        var items = charges
            .Where(kv => kv.Value > 0m && kv.Key != Guid.Empty)
            .Select(kv => new RegistrationChargeItem { RegistrationId = kv.Key, Amount = kv.Value })
            .ToList();

        // PIF guard: pifSnapshot (captured in ProcessPaymentAsync BEFORE
        // UpgradeRegistrationsToPifAsync mutated FeeBase/FeeProcessing/FeeTotal/
        // OwedTotal) holds the pre-PIF state. The engine's pre-gateway
        // _acct.SaveChangesAsync() at line 1373 flushes the PIF mutations to the
        // DB along with the placeholder RA — a declined card otherwise leaves the
        // registration permanently in PIF posture with no charge to back it.
        // Restore the pre-PIF state on engine failure. (Deposit path passes null.)

        var result = await ChargeRegistrationsCcAsync(jobId, items, cc, userId, expectedTotal: expectedTotal);
        if (!result.Success)
        {
            // Per-player charges can partially succeed. Restore the pre-PIF state ONLY
            // for the registrations whose charge declined — a player that captured paid
            // the PIF amount and must keep its upgraded FeeTotal/OwedTotal.
            await RestoreFailedPifRegsAsync(registrations, pifSnapshot, result.Outcomes);
            return new PaymentResponseDto
            {
                Success = false,
                Message = result.Message ?? "Payment failed",
                ErrorCode = result.ErrorCode ?? "CHARGE_GATEWAY_ERROR",
                // Per-player outcomes only for true gateway declines (partial/total) — not for
                // pre-charge validation failures (AMOUNT_CHANGED etc.), which the error banner covers.
                Outcomes = result.ErrorCode == "CHARGE_GATEWAY_ERROR"
                    ? await BuildChargeOutcomeDtosAsync(jobId, result.Outcomes, registrations)
                    : null
            };
        }

        // RegSaver policy stamping (parent-only — admin path does not buy insurance
        // through this flow). Stamped AFTER the canonical charge so a declined card
        // never leaves half-written insurance state on the registration.
        if (request.ViConfirmed == true && !string.IsNullOrWhiteSpace(request.ViPolicyNumber))
        {
            foreach (var reg in registrations.Where(r => string.IsNullOrWhiteSpace(r.RegsaverPolicyId)))
            {
                reg.RegsaverPolicyId = request.ViPolicyNumber;
                reg.RegsaverPolicyIdCreateDate = request.ViPolicyCreateDate ?? DateTime.Now;
                reg.Modified = DateTime.Now;
                reg.LebUserId = userId;
            }
            await _registrations.SaveChangesAsync();
        }

        await TrySendConfirmationEmailAsync(jobId, familyUserId, userId);
        return new PaymentResponseDto
        {
            Success = true,
            Message = "Payment processed",
            TransactionId = result.TransactionId
        };
    }

    private async Task<bool> IsDepositScenarioAsync(IEnumerable<Registrations> registrations)
    {
        // A deposit scenario exists when AT LEAST ONE selected registration is in deposit phase —
        // i.e. not yet stamped to full price (IsRegFullPaymentPhase false) AND its scope offers a
        // partial deposit slice (per-registrant Fee > 0 and Deposit > 0). This is intentionally
        // ANY, not ALL: a mixed cart (some deposit-phase, some full-payment) stays in Deposit mode
        // so ComputeChargesAsync can bill each reg by its OWN phase. Only when NO reg can take a
        // deposit does the caller coerce the request to PIF (ComputeChargesAsync would charge each
        // reg its full OwedTotal anyway, but coercion keeps the PIF allow-gate meaningful).
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue) continue;
            var resolved = await ResolveRegPlayerFeeAsync(reg);
            if (IsRegFullPaymentPhase(reg, resolved)) continue;
            var (fee, deposit) = await _teamLookup.ResolvePerRegistrantAsync(reg.AssignedTeamId.Value);
            if (fee > 0m && deposit > 0m) return true;
        }
        return false;
    }

    private async Task NormalizeFeesAsync(IEnumerable<Registrations> registrations, Guid jobId)
    {
        // Idempotent cleanup for registrations that never got a fee stamp.
        // Default is the deposit phase (Deposit when configured, else BalanceDue).
        // Does NOT force-overwrite a non-zero FeeBase — a PIF-upgraded registration
        // must retain its Deposit + BalanceDue stamp through this call.
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue) continue;
            // Agegroup resolves through the team (Registrations.AssignedAgegroupId is obsolete).
            var team = await _teams.GetTeamFromTeamId(reg.AssignedTeamId.Value);
            if (team is null) continue;
            var resolved = await _feeService.ResolveFeeAsync(
                jobId, Domain.Constants.RoleConstants.Player,
                team.AgegroupId, reg.AssignedTeamId.Value);
            var deposit = resolved?.EffectiveDeposit ?? 0m;
            var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;
            var baseFee = deposit > 0m ? deposit : balanceDue;
            if (baseFee <= 0) continue;
            if (reg.FeeBase <= 0) reg.FeeBase = baseFee;
            reg.RecalcTotals();
        }
    }

    private async Task UpgradeRegistrationsToPifAsync(IEnumerable<Registrations> registrations, Guid jobId)
    {
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue) continue;
            // Agegroup resolves through the team (Registrations.AssignedAgegroupId is obsolete).
            var team = await _teams.GetTeamFromTeamId(reg.AssignedTeamId.Value);
            if (team is null) continue;
            await _feeService.ApplyPifUpgradeAsync(
                reg, jobId, team.AgegroupId, reg.AssignedTeamId.Value,
                new FeeApplicationContext { AddProcessingFees = true });
        }
    }

    private void NormalizeProcessingFees(IEnumerable<Registrations> registrations, Guid jobId, string userId)
    {
        foreach (var reg in registrations)
        {
            if (reg.FeeProcessing <= 0m) continue;
            var removed = reg.FeeProcessing;
            reg.FeeProcessing = 0m;
            reg.RecalcTotals();
            reg.Modified = DateTime.Now;
            reg.LebUserId = userId;
            _logger.LogInformation("ARB normalization removed processing fee {Removed} from registration {RegistrationId} (job {JobId}).", removed, reg.RegistrationId, jobId);
        }
    }

    /// <summary>
    /// Authorize.Net subscription statuses that can no longer draft the card. Anything else —
    /// "active", "suspended", or a null status alongside a real subscription id — must be treated
    /// as live: a suspended subscription resumes on its own once the card clears.
    /// </summary>
    private static readonly string[] DeadArbStatuses = ["canceled", "terminated", "expired"];

    /// <summary>
    /// True when this registration's balance is already financed by a subscription that can still
    /// bill the card. ARB enrollment records NO money — PaidTotal stays 0 and OwedTotal stays at the
    /// full balance for the life of the plan — so fee math alone cannot tell a financed registration
    /// apart from an unpaid one. The subscription is the only marker.
    /// </summary>
    private static bool HasLiveArbSubscription(Registrations r) =>
        !string.IsNullOrWhiteSpace(r.AdnSubscriptionId)
        && !DeadArbStatuses.Contains(r.AdnSubscriptionStatus ?? string.Empty, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Single write-side intercept for the "already financed" invariant, applied to EVERY charge path
    /// before fee normalization or any gateway call.
    ///
    /// The family fetch (<see cref="IRegistrationRepository.GetByJobAndFamilyWithUsersAsync"/>) returns
    /// every registration the family holds in the job, not just the ones added on this pass. A parent who
    /// enrolls player A in ARB, walks back from the confirmation step, adds player B and returns to
    /// payment therefore arrives here with A still in the list, still showing a full OwedTotal. Charging
    /// that set would either mint a SECOND subscription for A (ARB) or take A's balance in cash while the
    /// first subscription keeps drafting (PIF/Deposit). Both bill A twice.
    ///
    /// Registrations whose subscription is canceled/terminated/expired stay in the set — an admin cancel
    /// (RegistrationSearchService.CancelSubscription) leaves the id behind and re-enrollment is legitimate.
    /// </summary>
    private (List<Registrations> Chargeable, List<Registrations> Enrolled) PartitionArbEnrolled(List<Registrations> registrations)
    {
        var enrolled = registrations.Where(HasLiveArbSubscription).ToList();
        if (enrolled.Count == 0) return (registrations, enrolled);
        foreach (var reg in enrolled)
        {
            _logger.LogInformation(
                "Excluding registration {RegistrationId} from the charge set: subscription {SubscriptionId} is {Status}.",
                reg.RegistrationId, reg.AdnSubscriptionId, reg.AdnSubscriptionStatus ?? "(no status)");
        }
        return (registrations.Where(r => !HasLiveArbSubscription(r)).ToList(), enrolled);
    }

    private static PaymentResponseDto ArbAlreadyActive(List<Registrations> enrolled) => new()
    {
        Success = false,
        Message = "All selected registrations already have active subscriptions.",
        ErrorCode = "ARB_ALREADY_ACTIVE",
        SubscriptionIds = enrolled.ToDictionary(r => r.RegistrationId, r => r.AdnSubscriptionId!)
    };

    private sealed record ArbSubArgs(AuthorizeNet.Environment Env, string LoginId, string TransactionKey, short Occur, short IntervalLen, DateTime StartDate, CreditCardInfo Card, string UserId);

    private async Task<(Dictionary<Guid, string> Subs, List<Guid> Failed, List<Guid> ActivatedNoCharge)> CreateArbSubscriptionsAsync(IEnumerable<Registrations> registrations, ArbSubArgs args)
    {
        var subs = new Dictionary<Guid, string>();
        var failed = new List<Guid>();
        var activatedNoCharge = new List<Guid>();
        foreach (var reg in registrations)
        {
            var basis = reg.OwedTotal < 0m ? 0m : reg.OwedTotal;
            var perOccur = Math.Round(basis / args.Occur, 2, MidpointRounding.AwayFromZero);
            // Zero-basis chokepoint. Every fee modifier (discount code, early-bird, scholarship,
            // late fee, prior payment) has already netted into OwedTotal via FeeMath before we get
            // here, so this single test is modifier-agnostic by construction. When nothing is left
            // to finance (perOccur <= 0) there is no subscription to create — Authorize.Net rejects
            // a $0 recurring charge, which would drop the reg into the failed branch below and leave
            // it bActive=0 with no subscription id. The registrant owes nothing, so activate it
            // directly, mirroring the "owes nothing -> active" rule already used by the discount
            // endpoint (PlayerRegistrationPaymentController.ApplyDiscount) and the immediate path.
            if (perOccur <= 0m)
            {
                reg.BActive = true;
                reg.Modified = DateTime.Now;
                reg.LebUserId = args.UserId;
                activatedNoCharge.Add(reg.RegistrationId);
                _logger.LogInformation("ARB: registration {RegistrationId} owes nothing after modifiers (basis={Basis}); activated without a subscription.", reg.RegistrationId, basis);
                continue;
            }
            _logger.LogInformation("Creating ARB subscription for registration {RegistrationId}: perOccurrence={PerOccur} occur={Occur} start={StartDate} basis={Basis}.", reg.RegistrationId, perOccur, args.Occur, args.StartDate, basis);
            var invoiceNumber = await BuildInvoiceNumberForRegistrationAsync(reg.JobId, reg.RegistrationId);
            var description = await BuildArbSubscriptionDescriptionAsync(reg);
            var resp = _adnApiService.ADN_ARB_CreateMonthlySubscription(new AdnArbCreateRequest
            {
                Env = args.Env,
                LoginId = args.LoginId,
                TransactionKey = args.TransactionKey,
                CardNumber = args.Card.Number!,
                CardCode = args.Card.Code!,
                Expiry = FormatExpiry(args.Card.Expiry!),
                FirstName = args.Card.FirstName!,
                LastName = args.Card.LastName!,
                Address = args.Card.Address!,
                Zip = args.Card.Zip!,
                Email = args.Card.Email!,
                Phone = args.Card.Phone!,
                InvoiceNumber = invoiceNumber,
                Description = description,
                PerIntervalCharge = perOccur,
                StartDate = args.StartDate,
                BillingOccurrences = args.Occur,
                IntervalLength = args.IntervalLen
            });
            if (resp?.messages?.resultCode == messageTypeEnum.Ok && !string.IsNullOrWhiteSpace(resp.subscriptionId))
            {
                subs[reg.RegistrationId] = resp.subscriptionId;
                ApplyArbSuccessToRegistration(reg, resp.subscriptionId, perOccur, args.Occur, args.IntervalLen, args.StartDate, args.UserId);
            }
            else
            {
                failed.Add(reg.RegistrationId);
                var msgText = resp?.messages?.message?.FirstOrDefault()?.text ?? "Gateway subscription error";
                _logger.LogWarning("ARB subscription failed for registration {RegistrationId}: {Message}", reg.RegistrationId, msgText);
            }
        }
        return (subs, failed, activatedNoCharge);
    }

    private static PaymentResponseDto BuildArbResponse(Dictionary<Guid, string> subs, List<Guid> failed, List<Guid> activatedNoCharge)
    {
        // Any outright failure (with a penny-verified card, an ADN reject on a non-zero charge).
        if (failed.Count > 0)
        {
            if (subs.Count == 0 && activatedNoCharge.Count == 0)
                return new PaymentResponseDto { Success = false, Message = "All ARB subscription attempts failed.", ErrorCode = "ARB_SUB_CREATE_FAIL", FailedSubscriptionIds = failed };
            return new PaymentResponseDto { Success = false, Message = $"{subs.Count} subscription(s) created; {failed.Count} failed.", ErrorCode = "ARB_PARTIAL_FAIL", SubscriptionIds = subs, FailedSubscriptionIds = failed };
        }
        // No failures: some mix of subscriptions created and/or registrants activated with nothing
        // left to finance (fully covered by discount/early-bird/other modifiers).
        if (subs.Count > 0)
        {
            var single = subs.Count == 1 ? subs.Values.First() : null;
            var msg = activatedNoCharge.Count > 0
                ? $"{subs.Count} ARB subscription(s) created; {activatedNoCharge.Count} registration(s) activated with nothing due"
                : (subs.Count == 1 ? "ARB subscription created" : "ARB subscriptions created");
            return new PaymentResponseDto { Success = true, Message = msg, SubscriptionId = single, SubscriptionIds = subs };
        }
        if (activatedNoCharge.Count > 0)
            return new PaymentResponseDto { Success = true, Message = activatedNoCharge.Count == 1 ? "Registration activated; nothing due after discount." : $"{activatedNoCharge.Count} registrations activated; nothing due after discount." };
        return new PaymentResponseDto { Success = false, Message = "No registrations were processed.", ErrorCode = "ARB_NOTHING_PROCESSED" };
    }

    private static PaymentResponseDto Fail(string msg, string code) => new PaymentResponseDto { Success = false, Message = msg, ErrorCode = code };

    // Convert raw MMYY or MMyy, MM/YY, MM-YY into Authorize.Net expected YYYY-MM format
    private static string FormatExpiry(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length == 4)
        {
            var mm = digits.Substring(0, 2);
            var yy = digits.Substring(2, 2);
            // Assume 2000-based; adjust if needed for >2099 edge cases later
            var year = 2000 + int.Parse(yy);
            return $"{year}-{mm}"; // YYYY-MM
        }
        // Already in YYYYMM or YYYY-MM
        if (digits.Length == 6)
        {
            var year = digits.Substring(0, 4);
            var mm = digits.Substring(4, 2);
            return $"{year}-{mm}";
        }
        return raw; // Fallback: return original; gateway may reject
    }

    private async Task<Dictionary<Guid, decimal>> ComputeChargesAsync(IEnumerable<Registrations> registrations, PaymentOption option)
    {
        if (option == PaymentOption.PIF)
        {
            return registrations
                .Where(r => r.RegistrationId != Guid.Empty)
                .ToDictionary(r => r.RegistrationId, r => Math.Max(0, r.OwedTotal));
        }
        else if (option == PaymentOption.Deposit)
        {
            var map = new Dictionary<Guid, decimal>();
            // Per-registration phase. A family "Deposit" submission can span scopes that differ
            // in phase (per-scope JobFees.BFullPaymentRequired cascade), so the charge is decided
            // PER REG, not by the single cart-wide option:
            //   • full-payment reg → its OwedTotal already carries FullPrice+proc; charge it whole.
            //   • deposit-phase reg → gross the deposit slice by the JOB CC rate and cap at OwedTotal.
            // Phase is read from the reg's STAMPED FeeBase vs FullPrice — the same per-row signal the
            // display layer (RegisteredPlayerShaper) uses — so the charge can never diverge from the
            // screen total (the ExpectedTotal shown↔charged guard). See IsRegFullPaymentPhase.
            //
            // The deposit principal carries its own processing fee. Players ALWAYS levy proc on
            // the FeeBase (FeeResolutionService.ApplyRegistrationProcessingAndTotalsAsync — there
            // is no ApplyProcessingFeesToDeposit gate; that flag is teams-only), so a deposit-phase
            // reg is stamped FeeBase=deposit, FeeProcessing=deposit×ccRate, OwedTotal=deposit+proc.
            // The payment screen shows OwedTotal (proc-inclusive). Charging the bare principal hit
            // the card for the deposit only and stranded the proc as a residual balance ($207 shown,
            // $200 charged). Gross the deposit by the JOB CC rate (donation-independent — a payment-
            // time gift is added separately via donationGross by the caller) and cap at OwedTotal.
            var first = registrations.FirstOrDefault(r => r.RegistrationId != Guid.Empty);
            var rateState = first != null ? await _paymentState.ForJobAsync(first.JobId) : null;
            var ccRate = (rateState?.BAddProcessingFees ?? false) ? rateState!.CcRate : 0m;
            foreach (var reg in registrations)
            {
                if (reg.RegistrationId == Guid.Empty) continue;
                var resolved = await ResolveRegPlayerFeeAsync(reg);
                decimal charge;
                if (IsRegFullPaymentPhase(reg, resolved))
                {
                    // Full-payment scope: OwedTotal already = FullPrice + proc. Charge it whole.
                    charge = Math.Max(0m, reg.OwedTotal);
                }
                else
                {
                    var dep = await ResolveDepositForRegAsync(reg);
                    if (dep > 0m && ccRate > 0m)
                        dep += Math.Round(dep * ccRate, 2, MidpointRounding.AwayFromZero);
                    charge = Math.Min(dep, reg.OwedTotal);
                }
                if (charge > 0m)
                    map[reg.RegistrationId] = charge;
            }
            return map;
        }
        return new Dictionary<Guid, decimal>();
    }

    private async Task<decimal> ResolveDepositForRegAsync(Registrations reg)
    {
        if (reg.AssignedTeamId.HasValue)
        {
            var resolved = await _teamLookup.ResolvePerRegistrantAsync(reg.AssignedTeamId.Value);
            return resolved.Deposit;
        }
        return 0m;
    }

    /// <summary>
    /// Resolve the cascaded Player fee (team → agegroup → league) for a registration's scope.
    /// Agegroup resolves THROUGH the team (Registrations.AssignedAgegroupId is obsolete). Returns
    /// null when the reg has no assigned team or no configured fee row at any cascade level.
    /// </summary>
    private async Task<ResolvedFee?> ResolveRegPlayerFeeAsync(Registrations reg)
    {
        if (!reg.AssignedTeamId.HasValue) return null;
        var team = await _teams.GetTeamFromTeamId(reg.AssignedTeamId.Value);
        if (team is null) return null;
        return await _feeService.ResolveFeeAsync(
            reg.JobId, RoleConstants.Player, team.AgegroupId, reg.AssignedTeamId.Value);
    }

    /// <summary>
    /// True when a registration is in the full-payment phase, judged by the SAME per-row signal
    /// the display layer uses (RegisteredPlayerShaper): the stamped FeeBase has reached the
    /// canonical <see cref="ResolvedFee.FullPrice"/>. Chosen over a pure-config
    /// <see cref="ResolvedFee.ResolveFullPaymentPhase"/> read so the charge can never diverge
    /// from the screen total (the ExpectedTotal shown↔charged guard) — per-scope phase flips
    /// re-price active regs, so the stamped FeeBase stays authoritative.
    /// </summary>
    private static bool IsRegFullPaymentPhase(Registrations reg, ResolvedFee? resolved)
    {
        var fullPrice = resolved?.FullPrice ?? 0m;
        return fullPrice > 0m && reg.FeeBase >= fullPrice - 0.005m;
    }

    private static (short occur, short intervalLen, DateTime start) BuildArbSchedule(int? occur, int? intervalLen, DateTime? start)
    {
        short o = (short)(occur ?? 10);
        if (o <= 0) o = 10;
        short i = (short)(intervalLen ?? 1);
        if (i <= 0) i = 1;
        var s = start ?? DateTime.Now.AddDays(1);
        return (o, i, s);
    }

    private static void ApplyArbSuccessToRegistration(Registrations reg, string subscriptionId, decimal perOccurrence, short occur, short intervalLen, DateTime start, string userId)
    {
        reg.AdnSubscriptionId = subscriptionId;
        reg.AdnSubscriptionAmountPerOccurence = perOccurrence;
        reg.AdnSubscriptionBillingOccurences = occur;
        reg.AdnSubscriptionIntervalLength = intervalLen;
        reg.AdnSubscriptionStartDate = start;
        reg.AdnSubscriptionStatus = "active";
        reg.BActive = true;
        reg.Modified = DateTime.Now;
        reg.LebUserId = userId;
    }

    private static string? Last4(string? cardNumber) =>
        string.IsNullOrEmpty(cardNumber) || cardNumber.Length < 4 ? null : cardNumber[^4..];

    // Build invoice number pattern: customerAI_jobAI_registrationAI (<=20 chars Authorize.Net limit).
    // Fallback strategy if length exceeds limit: jobAI_registrationAI, then registrationAI, then truncated.
    private async Task<string> BuildInvoiceNumberForRegistrationAsync(Guid jobId, Guid registrationId)
    {
        try
        {
            var data = await _registrations.GetRegistrationWithInvoiceDataAsync(registrationId, jobId);
            if (data == null)
            {
                _logger.LogWarning("Invoice build fallback (missing entities) for registration {RegistrationId} job {JobId}.", registrationId, jobId);
                return ("INV" + DateTime.UtcNow.Ticks).Substring(0, 20);
            }
            string primary = $"{data.CustomerAi}_{data.JobAi}_{data.RegistrationAi}";
            if (primary.Length <= 20) return primary;
            string alt1 = $"{data.JobAi}_{data.RegistrationAi}";
            if (alt1.Length <= 20) return alt1;
            string alt2 = data.RegistrationAi.ToString();
            if (alt2.Length <= 20) return alt2;
            _logger.LogWarning("Invoice number exceeded 20 chars; truncated. Original={Primary}", primary);
            return primary.Substring(0, 20);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice build error for registration {RegistrationId} job {JobId}", registrationId, jobId);
            return ("INV" + DateTime.UtcNow.Ticks).Substring(0, 20);
        }
    }

    // Build the Authorize.Net one-time-charge description in the legacy format:
    // "{JobName}:{First} {Last}:{Agegroup}:{Team}" (assigned team) or
    // "{RoleName}:{First} {Last}" (no team). Falls back to a minimal label on any miss
    // so a charge is never blocked by a description lookup; trims to ADN's 255-char cap.
    private async Task<string> BuildChargeDescriptionAsync(Registrations reg)
    {
        try
        {
            var desc = await _registrations.GetChargeDescriptionAsync(reg.RegistrationId, reg.JobId);
            if (string.IsNullOrWhiteSpace(desc))
                return $"Registration Payment (#{reg.RegistrationAi})";
            desc = desc.Trim();
            return desc.Length > 255 ? desc.Substring(0, 255) : desc;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Charge description build error for registration {RegistrationId}", reg.RegistrationId);
            return $"Registration Payment (#{reg.RegistrationAi})";
        }
    }

    // Build rich ARB description: "ARB subscription for {PlayerFirst} {PlayerLast}: {JobName} - {AgegroupName}:{TeamName}".
    // Falls back gracefully if any component missing; trims to max 255 chars.
    private async Task<string> BuildArbSubscriptionDescriptionAsync(Registrations reg)
    {
        try
        {
            string playerFirst = reg.User?.FirstName?.Trim() ?? "Player";
            string playerLast = reg.User?.LastName?.Trim() ?? reg.User?.UserName?.Trim() ?? reg.RegistrationAi.ToString();
            string jobName = await _jobs.GetJobNameAsync(reg.JobId) ?? "Registration";

            string? teamName = null;
            string? agegroupName = null;
            string? clubName = null;
            if (reg.AssignedTeamId.HasValue)
            {
                var team = await _teams.GetTeamFromTeamId(reg.AssignedTeamId.Value);
                if (team != null)
                {
                    teamName = team.TeamName ?? team.DisplayName;
                    agegroupName = team.Agegroup?.AgegroupName;
                }
                // Owning club name (when club-rostered) via a reliable projection — the
                // FindAsync above does not eager-load the ClubrepRegistration nav.
                clubName = (await _teams.GetOwnerClubNameAsync(reg.AssignedTeamId.Value))?.Trim();
            }

            var parts = new List<string>();
            parts.Add($"ARB subscription for {playerFirst} {playerLast}: {jobName}");
            if (!string.IsNullOrWhiteSpace(agegroupName) || !string.IsNullOrWhiteSpace(teamName))
            {
                // Prefix the owning club: "{Club}: {Team}" mirrors the ledger/charge labels.
                var teamSegment = string.IsNullOrWhiteSpace(clubName)
                    ? (teamName ?? "Team")
                    : $"{clubName}: {teamName ?? "Team"}";
                var suffix = $" - {agegroupName ?? "Agegroup"}:{teamSegment}";
                parts.Add(suffix);
            }
            var raw = string.Concat(parts);
            if (raw.Length > 255) raw = raw.Substring(0, 255);
            return raw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARB description build error for registration {RegistrationId}", reg.RegistrationId);
            return "Registration Payment";
        }
    }

    // Builds and sends the registration confirmation email, then marks BConfirmationSent=true for any registrations not yet flagged.
    // This never suppresses sending; flag is purely informational. Guarded against missing optional services.
    // isEcheckPending=true makes the builder prepend a "settlement pending" banner to set the
    // 3-to-5-business-day expectation that the standard receipt template doesn't carry.
    private async Task TrySendConfirmationEmailAsync(Guid jobId, string familyUserId, string userId, bool isEcheckPending = false)
    {
        if (_confirmation == null || _email == null) return; // Backward compatibility.
        try
        {
            var toList = await BuildConfirmationRecipientsAsync(jobId, familyUserId);
            if (toList.Count == 0)
            {
                _logger.LogInformation("[ConfirmationEmail] No recipient emails found jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
                return;
            }
            var message = await BuildConfirmationMessageAsync(jobId, familyUserId, toList, isEcheckPending);
            if (message == null)
            {
                _logger.LogWarning("[ConfirmationEmail] No HTML content jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
                return;
            }
            var sent = await _email.SendAsync(message, sendInDevelopment: false, CancellationToken.None);
            if (!sent)
            {
                _logger.LogWarning("[ConfirmationEmail] Send failed jobId={JobId} familyUserId={FamilyUserId} recipients={Count}", jobId, familyUserId, toList.Count);
                return;
            }
            await FlagRegistrationsAsync(jobId, familyUserId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConfirmationEmail] Exception during send jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
        }
    }

    private async Task<List<string>> BuildConfirmationRecipientsAsync(Guid jobId, string familyUserId)
    {
        return await _families.GetEmailsForFamilyAndPlayersAsync(jobId, familyUserId);
    }

    private async Task<EmailMessageDto?> BuildConfirmationMessageAsync(Guid jobId, string familyUserId, List<string> toList, bool isEcheckPending = false)
    {
        var (subject, html) = await _confirmation!.BuildEmailAsync(jobId, familyUserId, CancellationToken.None, isEcheckPending);
        if (string.IsNullOrWhiteSpace(html)) return null;
        var dto = new EmailMessageDto
        {
            Subject = string.IsNullOrWhiteSpace(subject) ? "Registration Confirmation" : subject,
            HtmlBody = html
        };
        dto.ToAddresses.AddRange(toList);

        // Player confirmations carry the job's CC/BCC and Reply-To, same as every other confirmation.
        // This is the chokepoint for all three player flows — initial submit, ARB, and eCheck-pending.
        var jobInfo = await _jobs.GetConfirmationEmailInfoAsync(jobId);
        if (jobInfo != null) JobConfirmationCopies.Apply(dto, jobInfo);

        return dto;
    }

    private async Task FlagRegistrationsAsync(Guid jobId, string familyUserId, string userId)
    {
        var regsToFlag = (await _registrations.GetByFamilyAndJobAsync(jobId, familyUserId))
            .Where(r => !r.BConfirmationSent)
            .ToList();
        if (regsToFlag.Count == 0)
        {
            _logger.LogDebug("[ConfirmationEmail] All registrations already flagged jobId={JobId} familyUserId={FamilyUserId}", jobId, familyUserId);
            return;
        }
        foreach (var reg in regsToFlag)
        {
            reg.BConfirmationSent = true;
            reg.Modified = DateTime.Now;
            reg.LebUserId = userId;
        }
        await _registrations.SaveChangesAsync();
        await _acct.SaveChangesAsync();
        _logger.LogInformation("[ConfirmationEmail] Flagged {Count} registrations jobId={JobId} familyUserId={FamilyUserId}", regsToFlag.Count, jobId, familyUserId);
    }
}
