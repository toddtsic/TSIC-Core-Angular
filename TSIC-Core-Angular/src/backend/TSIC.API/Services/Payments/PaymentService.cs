using AuthorizeNet.Api.Contracts.V1;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
using TSIC.Contracts.Services;
using TSIC.API.Services.Shared.Adn;
using TSIC.API.Services.Teams;
using TSIC.API.Services.Players;


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

    // Well-known E-Check Payment method GUID (matches production seed data).
    private static readonly Guid EcheckPaymentMethodId = Guid.Parse("2EECA575-A268-E111-9D56-F04DA202060D");

    private sealed record JobInfo(bool? AdnArb, int? AdnArbbillingOccurences, int? AdnArbintervalLength, DateTime? AdnArbstartDate, bool AllowPif, bool BPlayersFullPaymentRequired, bool BEnableEcheck);

    public PaymentService(IJobRepository jobs, IRegistrationRepository registrations, ITeamRepository teams, IFamiliesRepository families, IRegistrationAccountingRepository acct, IAdnApiService adnApiService, IFeeResolutionService feeService, ITeamLookupService teamLookup, IRegistrationFeeAdjustmentService feeAdj, IEcheckSettlementRepository settleRepo, ILogger<PaymentService> logger)
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
    }

    // Extended constructor adding confirmation + email services; preserves backward compatibility with tests using the original signature.
    public PaymentService(IJobRepository jobs, IRegistrationRepository registrations, ITeamRepository teams, IFamiliesRepository families, IRegistrationAccountingRepository acct, IAdnApiService adnApiService, IFeeResolutionService feeService, ITeamLookupService teamLookup, IRegistrationFeeAdjustmentService feeAdj, IEcheckSettlementRepository settleRepo, ILogger<PaymentService> logger, IPlayerRegConfirmationService confirmation, IEmailService email)
        : this(jobs, registrations, teams, families, acct, adnApiService, feeService, teamLookup, feeAdj, settleRepo, logger)
    {
        _confirmation = confirmation;
        _email = email;
    }

    public async Task<TeamPaymentResponseDto> ProcessTeamPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        decimal totalAmount,
        CreditCardInfo creditCard)
    {
        // Get registration to derive jobId
        var jobId = await _registrations.GetRegistrationJobIdAsync(regId);

        if (jobId == null)
        {
            return new TeamPaymentResponseDto
            {
                Success = false,
                Message = "Registration not found"
            };
        }
        var jobIdValue = jobId.Value;

        // Get job payment credentials
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobIdValue);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
        {
            return new TeamPaymentResponseDto
            {
                Success = false,
                Message = "Payment gateway credentials not configured"
            };
        }

        var env = _adnApiService.GetADNEnvironment();

        // Get all teams with their Job and Customer data for invoice numbers
        var teams = await _teams.GetTeamsWithJobAndCustomerAsync(jobIdValue, teamIds);

        if (teams.Count != teamIds.Count)
        {
            return new TeamPaymentResponseDto
            {
                Success = false,
                Message = "One or more teams not found"
            };
        }

        // Calculate per-team amount (equal split)
        var perTeamAmount = totalAmount / teamIds.Count;

        // Track first successful transaction ID for response
        string? firstTransactionId = null;
        var failedCount = 0;

        // Process payment for each team
        foreach (var team in teams)
        {
            // Build invoice number for this team (pattern: customerAI_jobAI_teamAI)
            var invoiceNumber = $"{team.Job.Customer.CustomerAi}_{team.Job.JobAi}_{team.TeamAi}";
            if (invoiceNumber.Length > 20)
            {
                invoiceNumber = $"{team.Job.JobAi}_{team.TeamAi}";
            }
            if (invoiceNumber.Length > 20)
            {
                invoiceNumber = team.TeamAi.ToString();
            }

            var description = $"Team Registration: {team.TeamName ?? team.DisplayName}";
            var ccExpiryDate = FormatExpiry(creditCard.Expiry!);

            // Process ADN transaction using ADN_Charge
            var adnResponse = _adnApiService.ADN_Charge(new AdnChargeRequest
            {
                Env = env,
                LoginId = credentials.AdnLoginId!,
                TransactionKey = credentials.AdnTransactionKey!,
                CardNumber = creditCard.Number!,
                CardCode = creditCard.Code!,
                Expiry = ccExpiryDate,
                FirstName = creditCard.FirstName!,
                LastName = creditCard.LastName!,
                Address = creditCard.Address!,
                Zip = creditCard.Zip!,
                Email = creditCard.Email!,
                Phone = creditCard.Phone!,
                Amount = perTeamAmount,
                InvoiceNumber = invoiceNumber,
                Description = description
            });

            // Validate ADN response
            if (adnResponse?.messages?.resultCode == messageTypeEnum.Ok
                && adnResponse.transactionResponse?.messages != null
                && !string.IsNullOrWhiteSpace(adnResponse.transactionResponse.transId))
            {
                var transId = adnResponse.transactionResponse.transId;
                firstTransactionId ??= transId;

                // Create accounting entry for this team (per-team transaction for refund capability)
                _acct.Add(new RegistrationAccounting
                {
                    RegistrationId = regId,
                    TeamId = team.TeamId,
                    Payamt = perTeamAmount,
                    Dueamt = perTeamAmount,
                    Paymeth = $"paid by cc: {perTeamAmount:C} of {totalAmount:C} on {DateTime.Now:G} txID: {transId}",
                    PaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"), // CC payment method ID
                    Active = true,
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
                    LebUserId = userId,
                    AdnTransactionId = transId,
                    AdnInvoiceNo = invoiceNumber,
                    AdnCc4 = creditCard.Number!.Substring(creditCard.Number.Length - 4, 4),
                    AdnCcexpDate = ccExpiryDate,
                    Comment = description
                });

                // Update team record
                team.PaidTotal += perTeamAmount;
                team.OwedTotal -= perTeamAmount;
                if (team.PaidTotal > team.FeeTotal)
                {
                    team.OwedTotal = 0;
                    team.FeeTotal = team.PaidTotal;
                }
                team.Modified = DateTime.Now;
                team.LebUserId = userId;

                _logger.LogInformation("Team payment processed: Team={TeamId} Amount={Amount} TransId={TransId}",
                    team.TeamId, perTeamAmount, transId);
            }
            else
            {
                failedCount++;
                var errMsg = adnResponse?.transactionResponse?.errors?.FirstOrDefault()?.errorText
                    ?? adnResponse?.messages?.message?.FirstOrDefault()?.text
                    ?? "Gateway transaction failed";
                _logger.LogWarning("Team payment failed: Team={TeamId} Error={Error}", team.TeamId, errMsg);
            }
        }

        // Save all changes
        if (failedCount < teams.Count)
        {
            await _teams.SaveChangesAsync();
            await _acct.SaveChangesAsync();

            // Re-aggregate the rep registration row from the new team financials.
            // Single sync covers the whole batch — every team in this call belongs
            // to the same rep (regId is the rep's RegistrationId from the JWT and
            // matches Teams.ClubrepRegistrationid for every authorized team).
            // Without this, rep.PaidTotal/OwedTotal stay at the pre-payment values
            // while team rows hold the post-payment values, and downstream callers
            // (TeamSearchService balance-due gate at line 564) read stale aggregates.
            await _registrations.SynchronizeClubRepFinancialsAsync(regId, userId);
        }

        // Build response
        if (failedCount == 0)
        {
            return new TeamPaymentResponseDto
            {
                Success = true,
                Message = $"All {teamIds.Count} team payment(s) processed successfully",
                TransactionId = firstTransactionId
            };
        }
        else if (failedCount < teams.Count)
        {
            return new TeamPaymentResponseDto
            {
                Success = false,
                Error = "PARTIAL_SUCCESS",
                Message = $"{teams.Count - failedCount} of {teamIds.Count} team payment(s) succeeded",
                TransactionId = firstTransactionId
            };
        }
        else
        {
            return new TeamPaymentResponseDto
            {
                Success = false,
                Error = "ALL_FAILED",
                Message = "All team payments failed"
            };
        }
    }

    public async Task<TeamPaymentResponseDto> ProcessTeamEcheckPaymentAsync(
        Guid regId,
        string userId,
        IReadOnlyCollection<Guid> teamIds,
        decimal totalAmount,
        BankAccountInfo bankAccount)
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

        var teams = await _teams.GetTeamsWithJobAndCustomerAsync(jobIdValue, teamIds);
        if (teams.Count != teamIds.Count)
            return new TeamPaymentResponseDto { Success = false, Error = "TEAM_NOT_FOUND", Message = "One or more teams not found" };

        var perTeamAmount = totalAmount / teamIds.Count;
        var last4 = bankAccount.AccountNumber!.Length >= 4 ? bankAccount.AccountNumber[^4..] : bankAccount.AccountNumber;
        var nameOnAcct = bankAccount.NameOnAccount?.Trim();
        string? firstTransactionId = null;
        var failedCount = 0;
        var pendingSettlements = new List<(RegistrationAccounting Ra, string TxId)>();

        foreach (var team in teams)
        {
            var invoiceNumber = $"{team.Job.Customer.CustomerAi}_{team.Job.JobAi}_{team.TeamAi}";
            if (invoiceNumber.Length > 20) invoiceNumber = $"{team.Job.JobAi}_{team.TeamAi}";
            if (invoiceNumber.Length > 20) invoiceNumber = team.TeamAi.ToString();

            var description = $"Team Registration: {team.TeamName ?? team.DisplayName}";

            // Apply (CC − EC) processing-fee credit BEFORE the debit so the recorded eCheck
            // amount matches the team's now-reduced obligation. Mirrors the player path.
            await _feeAdj.ReduceTeamProcessingFeeForEcheckAsync(team, perTeamAmount, jobIdValue, userId);

            var adnResponse = _adnApiService.ADN_ChargeBankAccount(new AdnChargeBankAccountRequest
            {
                Env = env,
                LoginId = credentials.AdnLoginId!,
                TransactionKey = credentials.AdnTransactionKey!,
                AccountType = bankAccount.AccountType!,
                RoutingNumber = bankAccount.RoutingNumber!,
                AccountNumber = bankAccount.AccountNumber!,
                NameOnAccount = nameOnAcct!,
                FirstName = bankAccount.FirstName!,
                LastName = bankAccount.LastName!,
                Address = bankAccount.Address!,
                Zip = bankAccount.Zip!,
                Email = bankAccount.Email!,
                Phone = bankAccount.Phone!,
                Amount = perTeamAmount,
                InvoiceNumber = invoiceNumber,
                Description = description
            });

            if (adnResponse?.messages?.resultCode == messageTypeEnum.Ok
                && adnResponse.transactionResponse?.messages != null
                && !string.IsNullOrWhiteSpace(adnResponse.transactionResponse.transId))
            {
                var transId = adnResponse.transactionResponse.transId;
                firstTransactionId ??= transId;

                var ra = new RegistrationAccounting
                {
                    RegistrationId = regId,
                    TeamId = team.TeamId,
                    Payamt = perTeamAmount,
                    Dueamt = perTeamAmount,
                    Paymeth = $"eCheck pending settlement: {perTeamAmount:C} of {totalAmount:C} on {DateTime.Now:G} txID: {transId}",
                    PaymentMethodId = EcheckPaymentMethodId,
                    Active = true,
                    Createdate = DateTime.Now,
                    Modified = DateTime.Now,
                    LebUserId = userId,
                    AdnTransactionId = transId,
                    AdnInvoiceNo = invoiceNumber,
                    Comment = description
                };
                _acct.Add(ra);
                pendingSettlements.Add((ra, transId));

                team.PaidTotal += perTeamAmount;
                team.OwedTotal -= perTeamAmount;
                if (team.PaidTotal > team.FeeTotal)
                {
                    team.OwedTotal = 0;
                    team.FeeTotal = team.PaidTotal;
                }
                team.Modified = DateTime.Now;
                team.LebUserId = userId;

                _logger.LogInformation("Team eCheck submitted: Team={TeamId} Amount={Amount} TransId={TransId}",
                    team.TeamId, perTeamAmount, transId);
            }
            else
            {
                failedCount++;
                var errMsg = adnResponse?.transactionResponse?.errors?.FirstOrDefault()?.errorText
                    ?? adnResponse?.messages?.message?.FirstOrDefault()?.text
                    ?? "Gateway transaction failed";
                _logger.LogWarning("Team eCheck failed: Team={TeamId} Error={Error}", team.TeamId, errMsg);
            }
        }

        if (failedCount < teams.Count)
        {
            await _teams.SaveChangesAsync();
            await _acct.SaveChangesAsync();
            // Settlement.RegistrationAccountingId is the identity-generated AId on RA;
            // requires the RA inserts above to be saved first.
            var nextCheckAt = DateTime.UtcNow.AddDays(1);
            foreach (var (ra, txId) in pendingSettlements)
            {
                _settleRepo.Add(new Settlement
                {
                    SettlementId = Guid.NewGuid(),
                    RegistrationAccountingId = ra.AId,
                    AdnTransactionId = txId,
                    Status = "Pending",
                    SubmittedAt = DateTime.UtcNow,
                    NextCheckAt = nextCheckAt,
                    AccountLast4 = last4,
                    AccountType = bankAccount.AccountType,
                    NameOnAccount = nameOnAcct,
                    Modified = DateTime.UtcNow,
                    LebUserId = userId
                });
            }
            await _settleRepo.SaveChangesAsync();
            await _registrations.SynchronizeClubRepFinancialsAsync(regId, userId);
        }

        if (failedCount == 0)
            return new TeamPaymentResponseDto
            {
                Success = true,
                Message = $"{teamIds.Count} team eCheck submission(s) accepted; settlement pending (typically 3–5 business days).",
                TransactionId = firstTransactionId
            };
        if (failedCount < teams.Count)
            return new TeamPaymentResponseDto
            {
                Success = false,
                Error = "PARTIAL_SUCCESS",
                Message = $"{teams.Count - failedCount} of {teamIds.Count} team eCheck submission(s) accepted; rest failed.",
                TransactionId = firstTransactionId
            };
        return new TeamPaymentResponseDto
        {
            Success = false,
            Error = "ALL_FAILED",
            Message = "All team eCheck submissions failed"
        };
    }

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
            ViToken = request.ViToken
        };

        // Use internal validation that still expects JobId and FamilyUserId
        var v = await ValidatePaymentRequestInternalAsync(jobId, familyUserId, internalRequest);
        if (v.Response != null) return v.Response;
        var job = v.Job!;
        var registrations = v.Registrations!;
        var cc = v.Card!;
        await NormalizeFeesAsync(registrations, jobId);
        if (request.PaymentOption == PaymentOption.ARB)
            return await ProcessArbAsync(jobId, familyUserId, internalRequest, userId, registrations, job, cc);
        // PIF chosen → re-stamp registrations with full amount (Deposit + BalanceDue)
        // before computing charges. Validation above already verified ALLOWPIF.
        if (request.PaymentOption == PaymentOption.PIF)
            await UpgradeRegistrationsToPifAsync(registrations, jobId);
        var charges = await ComputeChargesAsync(registrations, request.PaymentOption);
        var total = charges.Values.Sum();
        if (total <= 0m) return Fail("Nothing due for selected registrations.", "NOTHING_DUE");
        return await ExecutePrimaryChargeAsync(jobId, familyUserId, internalRequest, userId, registrations, cc, charges, total);
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
        var registrations = v.Registrations!;
        var bank = v.Bank!;
        await NormalizeFeesAsync(registrations, jobId);
        if (request.PaymentOption == PaymentOption.PIF)
            await UpgradeRegistrationsToPifAsync(registrations, jobId);
        var charges = await ComputeChargesAsync(registrations, request.PaymentOption);
        var total = charges.Values.Sum();
        if (total <= 0m)
            return Fail("Nothing due for selected registrations.", "NOTHING_DUE");
        return await ExecuteEcheckChargeAsync(jobId, familyUserId, request, userId, registrations, bank, charges, total);
    }

    private async Task<(PaymentResponseDto? Response, JobInfo? Job, List<Registrations>? Registrations, BankAccountInfo? Bank)> ValidateEcheckPaymentRequestAsync(Guid jobId, string familyUserId, PaymentRequestDto request)
    {
        var jobPaymentInfo = await _jobs.GetJobPaymentInfoAsync(jobId);
        if (jobPaymentInfo == null) return (Fail("Invalid job", "INVALID_JOB"), null, null, null);
        if (!jobPaymentInfo.BEnableEcheck) return (Fail("eCheck payments are not enabled for this job.", "ECHECK_NOT_ENABLED"), null, null, null);
        var job = new JobInfo(jobPaymentInfo.AdnArb, jobPaymentInfo.AdnArbbillingOccurences, jobPaymentInfo.AdnArbintervalLength, jobPaymentInfo.AdnArbstartDate, jobPaymentInfo.AllowPif, jobPaymentInfo.BPlayersFullPaymentRequired, jobPaymentInfo.BEnableEcheck);
        var registrations = await _registrations.GetByJobAndFamilyWithUsersAsync(jobId, familyUserId, activePlayersOnly: true);
        if (!registrations.Any()) return (Fail("No registrations found", "NO_REGISTRATIONS"), null, null, null);
        if (request.PaymentOption == PaymentOption.PIF && !job.AllowPif && !job.BPlayersFullPaymentRequired) return (Fail("Pay In Full is not enabled for this job", "PIF_NOT_ALLOWED"), null, null, null);
        if (request.PaymentOption == PaymentOption.Deposit && !await IsDepositScenarioAsync(registrations)) return (Fail("Deposit not available", "DEPOSIT_NOT_AVAILABLE"), null, null, null);
        var bank = request.BankAccount;
        var (bankErr, bankCode) = NormalizeAndValidateBankAccount(bank);
        if (bankErr != null) return (Fail(bankErr, bankCode!), null, null, null);
        return (null, job, registrations, bank);
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

    private async Task<PaymentResponseDto> ExecuteEcheckChargeAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId, List<Registrations> registrations, BankAccountInfo bank, Dictionary<Guid, decimal> charges, decimal total)
    {
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
            return new PaymentResponseDto { Success = false, Message = "Missing payment gateway credentials (Authorize.Net).", ErrorCode = "MISSING_GATEWAY_CREDS" };
        var env = _adnApiService.GetADNEnvironment();
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && await IsDuplicateAsync(jobId, familyUserId, request))
            return new PaymentResponseDto { Success = true, Message = "Duplicate prevented (idempotent).", ErrorCode = "DUPLICATE_PREVENTED" };
        var invoiceReg = registrations[0];
        var invoiceNumber = await BuildInvoiceNumberForRegistrationAsync(jobId, invoiceReg.RegistrationId);
        // Apply EC processing-fee credit (CC_rate − EC_rate) × charge per registration BEFORE charging,
        // so the recorded eCheck amount matches the registration's now-reduced obligation.
        foreach (var reg in registrations)
        {
            if (!charges.TryGetValue(reg.RegistrationId, out var chargeAmt) || chargeAmt <= 0m) continue;
            await _feeAdj.ReduceProcessingFeeForEcheckAsync(reg, chargeAmt, jobId, userId);
        }
        var response = _adnApiService.ADN_ChargeBankAccount(new AdnChargeBankAccountRequest
        {
            Env = env,
            LoginId = credentials.AdnLoginId!,
            TransactionKey = credentials.AdnTransactionKey!,
            AccountType = bank.AccountType!,
            RoutingNumber = bank.RoutingNumber!,
            AccountNumber = bank.AccountNumber!,
            NameOnAccount = bank.NameOnAccount!.Trim(),
            FirstName = bank.FirstName!,
            LastName = bank.LastName!,
            Address = bank.Address!,
            Zip = bank.Zip!,
            Email = bank.Email!,
            Phone = bank.Phone!,
            Amount = total,
            InvoiceNumber = invoiceNumber,
            Description = "Registration Payment"
        });
        if (response == null || response.messages == null)
            return new PaymentResponseDto { Success = false, Message = "Payment gateway returned no response.", ErrorCode = "CHARGE_NULL_RESPONSE" };
        if (response.messages.resultCode != messageTypeEnum.Ok)
            return new PaymentResponseDto { Success = false, Message = response.transactionResponse?.errors?[0].errorText ?? "Payment failed", ErrorCode = "CHARGE_GATEWAY_ERROR" };
        var transId = response.transactionResponse.transId;
        UpdateRegistrationsForCharge(registrations, userId, charges);
        var addedAccts = AddEcheckAccountingEntries(registrations, userId, transId, invoiceNumber, charges, bank);
        await _registrations.SaveChangesAsync();
        // Settlement rows reference RegistrationAccounting.AId, which is identity-generated —
        // requires the RA inserts above to be saved first.
        var nextCheckAt = DateTime.UtcNow.AddDays(1);
        foreach (var ra in addedAccts)
        {
            _settleRepo.Add(new Settlement
            {
                SettlementId = Guid.NewGuid(),
                RegistrationAccountingId = ra.AId,
                AdnTransactionId = transId,
                Status = "Pending",
                SubmittedAt = DateTime.UtcNow,
                NextCheckAt = nextCheckAt,
                AccountLast4 = bank.AccountNumber!.Length >= 4 ? bank.AccountNumber[^4..] : bank.AccountNumber,
                AccountType = bank.AccountType,
                NameOnAccount = bank.NameOnAccount?.Trim(),
                Modified = DateTime.UtcNow,
                LebUserId = userId
            });
        }
        await _settleRepo.SaveChangesAsync();
        await TrySendConfirmationEmailAsync(jobId, familyUserId, userId, isEcheckPending: true);
        return new PaymentResponseDto
        {
            Success = true,
            Message = "eCheck submitted; settlement pending (typically 3–5 business days).",
            TransactionId = transId
        };
    }

    private List<RegistrationAccounting> AddEcheckAccountingEntries(IEnumerable<Registrations> registrations, string userId, string adnTransactionId, string invoiceNumber, IReadOnlyDictionary<Guid, decimal> charges, BankAccountInfo bank)
    {
        var added = new List<RegistrationAccounting>();
        var last4 = bank.AccountNumber!.Length >= 4 ? bank.AccountNumber[^4..] : bank.AccountNumber;
        foreach (var reg in registrations)
        {
            if (reg.RegistrationId == Guid.Empty) continue;
            if (!charges.TryGetValue(reg.RegistrationId, out var payAmt) || payAmt <= 0m) continue;
            var ra = new RegistrationAccounting
            {
                RegistrationId = reg.RegistrationId,
                Payamt = payAmt,
                Paymeth = $"eCheck Payment — pending settlement (acct ****{last4})",
                PaymentMethodId = EcheckPaymentMethodId,
                Active = true,
                Createdate = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = userId,
                AdnTransactionId = adnTransactionId,
                AdnInvoiceNo = invoiceNumber,
                Comment = "eCheck Registration Payment (Pending Settlement)"
            };
            _acct.Add(ra);
            added.Add(ra);
        }
        return added;
    }

    /// <summary>
    /// Internal validation method accepting jobId and familyUserId parameters directly.
    /// Used by the overload to avoid DTO field dependencies.
    /// </summary>
    private async Task<(PaymentResponseDto? Response, JobInfo? Job, List<Registrations>? Registrations, CreditCardInfo? Card)> ValidatePaymentRequestInternalAsync(Guid jobId, string familyUserId, PaymentRequestDto request)
    {
        if (request == null) return (Fail("Invalid request", "INVALID_REQUEST"), null, null, null);
        var jobPaymentInfo = await _jobs.GetJobPaymentInfoAsync(jobId);
        if (jobPaymentInfo == null) return (Fail("Invalid job", "INVALID_JOB"), null, null, null);
        var job = new JobInfo(jobPaymentInfo.AdnArb, jobPaymentInfo.AdnArbbillingOccurences, jobPaymentInfo.AdnArbintervalLength, jobPaymentInfo.AdnArbstartDate, jobPaymentInfo.AllowPif, jobPaymentInfo.BPlayersFullPaymentRequired, jobPaymentInfo.BEnableEcheck);
        var registrations = await _registrations.GetByJobAndFamilyWithUsersAsync(jobId, familyUserId, activePlayersOnly: true);
        if (!registrations.Any()) return (Fail("No registrations found", "NO_REGISTRATIONS"), null, null, null);
        // PIF allowed when ALLOWPIF token is set (parent-voluntary path) OR job is in
        // full-payment phase (BPlayersFullPaymentRequired — every reg owes the full
        // amount anyway, so PaymentOption.PIF is the natural checkout mode).
        if (request.PaymentOption == PaymentOption.PIF && !job.AllowPif && !job.BPlayersFullPaymentRequired) return (Fail("Pay In Full is not enabled for this job", "PIF_NOT_ALLOWED"), null, null, null);
        if (request.PaymentOption == PaymentOption.ARB && job.AdnArb != true) return (Fail("Recurring billing (ARB) not enabled", "ARB_NOT_ENABLED"), null, null, null);
        if (request.PaymentOption == PaymentOption.Deposit && !await IsDepositScenarioAsync(registrations)) return (Fail("Deposit not available", "DEPOSIT_NOT_AVAILABLE"), null, null, null);
        var cc = request.CreditCard;
        if (cc == null) return (Fail("Credit card required", "CARD_REQUIRED"), null, null, null);
        var fields = new (string Name, string? Value)[] { ("number", cc.Number), ("expiry", cc.Expiry), ("code", cc.Code), ("firstName", cc.FirstName), ("lastName", cc.LastName), ("address", cc.Address), ("zip", cc.Zip), ("email", cc.Email), ("phone", cc.Phone) };
        var missing = fields.Where(f => string.IsNullOrWhiteSpace(f.Value)).Select(f => f.Name).ToList();
        if (missing.Count > 0) return (Fail("Missing card field(s): " + string.Join(", ", missing), "CARD_FIELDS_MISSING"), null, null, null);
        cc.Expiry = new string((cc.Expiry ?? "").Where(char.IsDigit).ToArray());
        if (cc.Expiry?.Length != 4) return (Fail("Invalid expiry format (expected MMYY)", "CARD_EXPIRY_INVALID"), null, null, null);
        // Sanitize phone to digits only
        cc.Phone = new string((cc.Phone ?? "").Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(cc.Phone)) return (Fail("Invalid phone (digits required)", "CARD_PHONE_INVALID"), null, null, null);
        // Basic email sanity check (contains '@' and '.')
        if (string.IsNullOrWhiteSpace(cc.Email) || !cc.Email.Contains('@') || !cc.Email.Contains('.'))
            return (Fail("Invalid email format", "CARD_EMAIL_INVALID"), null, null, null);
        return (null, job, registrations, cc);
    }

    private async Task<PaymentResponseDto> ProcessArbAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId, List<Registrations> registrations, JobInfo job, CreditCardInfo cc)
    {
        // Early exit: if every registration already has an active ARB subscription, avoid duplicate gateway calls
        if (registrations.All(r => !string.IsNullOrWhiteSpace(r.AdnSubscriptionId) && string.Equals(r.AdnSubscriptionStatus, "active", StringComparison.OrdinalIgnoreCase)))
        {
            return new PaymentResponseDto
            {
                Success = false,
                Message = "All selected registrations already have active subscriptions.",
                ErrorCode = "ARB_ALREADY_ACTIVE",
                SubscriptionIds = registrations.Where(r => !string.IsNullOrWhiteSpace(r.AdnSubscriptionId)).ToDictionary(r => r.RegistrationId, r => r.AdnSubscriptionId!)
            };
        }
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
        var (subs, failed) = await CreateArbSubscriptionsAsync(registrations, args);
        await _registrations.SaveChangesAsync();
        var response = BuildArbResponse(subs, failed);
        // Always attempt confirmation email after any successful subscription creation.
        if (response.Success && subs.Count > 0)
        {
            await TrySendConfirmationEmailAsync(jobId, familyUserId, userId);
        }
        return response;
    }

    private async Task<PaymentResponseDto> ExecutePrimaryChargeAsync(Guid jobId, string familyUserId, PaymentRequestDto request, string userId, List<Registrations> registrations, CreditCardInfo cc, Dictionary<Guid, decimal> charges, decimal total)
    {
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(jobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
        {
            return new PaymentResponseDto { Success = false, Message = "Missing payment gateway credentials (Authorize.Net).", ErrorCode = "MISSING_GATEWAY_CREDS" };
        }
        var env = _adnApiService.GetADNEnvironment();
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && await IsDuplicateAsync(jobId, familyUserId, request))
            return new PaymentResponseDto { Success = true, Message = "Duplicate prevented (idempotent).", ErrorCode = "DUPLICATE_PREVENTED" };
        // Build deterministic invoice number using first registration (pattern: customerAI_jobAI_registrationAI)
        var invoiceReg = registrations[0];
        var invoiceNumber = await BuildInvoiceNumberForRegistrationAsync(jobId, invoiceReg.RegistrationId);
        var response = _adnApiService.ADN_Charge(new AdnChargeRequest
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
            Email = cc.Email!,
            Phone = cc.Phone!,
            Amount = total,
            InvoiceNumber = invoiceNumber,
            Description = "Registration Payment"
        });
        if (response == null || response.messages == null)
        {
            return new PaymentResponseDto { Success = false, Message = "Payment gateway returned no response.", ErrorCode = "CHARGE_NULL_RESPONSE" };
        }
        if (response.messages.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok)
        {
            UpdateRegistrationsForCharge(registrations, userId, charges);
            AddAccountingEntries(registrations, request.PaymentOption, userId, response.transactionResponse.transId, invoiceNumber, charges, Last4(cc.Number), FormatExpiry(cc.Expiry!));
            if (request.ViConfirmed == true && !string.IsNullOrWhiteSpace(request.ViPolicyNumber))
            {
                foreach (var reg in registrations.Where(r => string.IsNullOrWhiteSpace(r.RegsaverPolicyId)))
                {
                    reg.RegsaverPolicyId = request.ViPolicyNumber;
                    reg.RegsaverPolicyIdCreateDate = request.ViPolicyCreateDate ?? DateTime.Now;
                    reg.Modified = DateTime.Now;
                    reg.LebUserId = userId;
                }
            }
            await _registrations.SaveChangesAsync();
            // Attempt confirmation email after successful charge (always send, never gated by prior sends)
            await TrySendConfirmationEmailAsync(jobId, familyUserId, userId);
            return new PaymentResponseDto
            {
                Success = true,
                Message = "Payment processed",
                TransactionId = response.transactionResponse.transId
            };
        }
        return new PaymentResponseDto { Success = false, Message = response.transactionResponse?.errors?[0].errorText ?? "Payment failed", ErrorCode = "CHARGE_GATEWAY_ERROR" };
    }

    private async Task<bool> IsDepositScenarioAsync(IEnumerable<Registrations> registrations)
    {
        // Deposit scenario requires every selected registration to have an assigned team
        // whose per-registrant Fee > 0 and Deposit > 0.
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue)
            {
                return false;
            }
            var (fee, deposit) = await _teamLookup.ResolvePerRegistrantAsync(reg.AssignedTeamId.Value);
            if (fee <= 0m || deposit <= 0m)
            {
                return false;
            }
        }
        return registrations.Any();
    }

    private async Task NormalizeFeesAsync(IEnumerable<Registrations> registrations, Guid jobId)
    {
        // Idempotent cleanup for registrations that never got a fee stamp.
        // Default is the deposit phase (Deposit when configured, else BalanceDue).
        // Does NOT force-overwrite a non-zero FeeBase — a PIF-upgraded registration
        // must retain its Deposit + BalanceDue stamp through this call.
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue || !reg.AssignedAgegroupId.HasValue) continue;
            var resolved = await _feeService.ResolveFeeAsync(
                jobId, Domain.Constants.RoleConstants.Player,
                reg.AssignedAgegroupId.Value, reg.AssignedTeamId.Value);
            var deposit = resolved?.EffectiveDeposit ?? 0m;
            var balanceDue = resolved?.EffectiveBalanceDue ?? 0m;
            var baseFee = deposit > 0m ? deposit : balanceDue;
            if (baseFee <= 0) continue;
            if (reg.FeeBase <= 0) reg.FeeBase = baseFee;
            if (reg.FeeTotal <= 0) reg.FeeTotal = reg.FeeBase;
            if (reg.OwedTotal <= 0 && reg.PaidTotal <= 0) reg.OwedTotal = reg.FeeTotal;
        }
    }

    private async Task UpgradeRegistrationsToPifAsync(IEnumerable<Registrations> registrations, Guid jobId)
    {
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue || !reg.AssignedAgegroupId.HasValue) continue;
            await _feeService.ApplyPifUpgradeAsync(
                reg, jobId, reg.AssignedAgegroupId.Value, reg.AssignedTeamId.Value,
                new FeeApplicationContext { AddProcessingFees = true });
        }
    }

    private void NormalizeProcessingFees(IEnumerable<Registrations> registrations, Guid jobId, string userId)
    {
        foreach (var reg in registrations)
        {
            if (reg.FeeProcessing <= 0m) continue;
            var removed = reg.FeeProcessing;
            reg.OwedTotal = Math.Max(0m, reg.OwedTotal - removed);
            if (reg.FeeTotal >= removed) reg.FeeTotal -= removed;
            reg.FeeProcessing = 0m;
            reg.Modified = DateTime.Now;
            reg.LebUserId = userId;
            _logger.LogInformation("ARB normalization removed processing fee {Removed} from registration {RegistrationId} (job {JobId}).", removed, reg.RegistrationId, jobId);
        }
    }

    private sealed record ArbSubArgs(AuthorizeNet.Environment Env, string LoginId, string TransactionKey, short Occur, short IntervalLen, DateTime StartDate, CreditCardInfo Card, string UserId);

    private async Task<(Dictionary<Guid, string> Subs, List<Guid> Failed)> CreateArbSubscriptionsAsync(IEnumerable<Registrations> registrations, ArbSubArgs args)
    {
        var subs = new Dictionary<Guid, string>();
        var failed = new List<Guid>();
        foreach (var reg in registrations)
        {
            var basis = reg.OwedTotal < 0m ? 0m : reg.OwedTotal;
            var perOccur = Math.Round(basis / args.Occur, 2, MidpointRounding.AwayFromZero);
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
        return (subs, failed);
    }

    private static PaymentResponseDto BuildArbResponse(Dictionary<Guid, string> subs, List<Guid> failed)
    {
        if (subs.Count == 0)
            return new PaymentResponseDto { Success = false, Message = "All ARB subscription attempts failed.", ErrorCode = "ARB_SUB_CREATE_FAIL", FailedSubscriptionIds = failed };
        if (failed.Count == 0)
        {
            var single = subs.Count == 1 ? subs.Values.First() : null;
            return new PaymentResponseDto { Success = true, Message = subs.Count == 1 ? "ARB subscription created" : "ARB subscriptions created", SubscriptionId = single, SubscriptionIds = subs };
        }
        return new PaymentResponseDto { Success = false, Message = $"{subs.Count} subscription(s) created; {failed.Count} failed.", ErrorCode = "ARB_PARTIAL_FAIL", SubscriptionIds = subs, FailedSubscriptionIds = failed };
    }

    private async Task<bool> IsDuplicateAsync(Guid jobId, string familyUserId, PaymentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)) return false;
        return await _acct.AnyDuplicateAsync(jobId, familyUserId, request.IdempotencyKey);
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
            foreach (var reg in registrations)
            {
                var dep = await ResolveDepositForRegAsync(reg);
                var cap = Math.Min(dep, reg.OwedTotal);
                if (cap > 0 && reg.RegistrationId != Guid.Empty)
                    map[reg.RegistrationId] = cap;
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

    private static (short occur, short intervalLen, DateTime start) BuildArbSchedule(int? occur, int? intervalLen, DateTime? start)
    {
        short o = (short)(occur ?? 10);
        if (o <= 0) o = 10;
        short i = (short)(intervalLen ?? 1);
        if (i <= 0) i = 1;
        var s = start ?? DateTime.Now.AddDays(1);
        return (o, i, s);
    }

    private static void UpdateRegistrationsForCharge(IEnumerable<Registrations> registrations, string userId, IReadOnlyDictionary<Guid, decimal> charges)
    {
        // Iterate once; apply all charge deltas. LINQ avoided to minimize allocations.
        foreach (var reg in registrations)
        {
            if (reg.RegistrationId == Guid.Empty) continue;
            if (!charges.TryGetValue(reg.RegistrationId, out var charge) || charge <= 0) continue;
            reg.PaidTotal += charge;
            reg.OwedTotal -= charge;
            reg.BActive = true;
            reg.Modified = DateTime.Now;
            reg.LebUserId = userId;
        }
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

    private void AddAccountingEntries(IEnumerable<Registrations> registrations, PaymentOption option, string userId, string adnTransactionId, string invoiceNumber, IReadOnlyDictionary<Guid, decimal> charges, string? adnCc4, string? adnCcExpDate)
    {
        foreach (var reg in registrations)
        {
            if (reg.RegistrationId == Guid.Empty) continue;
            if (!charges.TryGetValue(reg.RegistrationId, out var payAmt) || payAmt <= 0) continue;

            _acct.Add(new RegistrationAccounting
            {
                RegistrationId = reg.RegistrationId,
                Payamt = payAmt,
                Paymeth = $"Credit Card Payment - {option}",
                PaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"), // CC payment method ID
                Active = true,
                Createdate = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = userId,
                AdnTransactionId = adnTransactionId,
                AdnInvoiceNo = invoiceNumber,
                AdnCc4 = adnCc4,
                AdnCcexpDate = adnCcExpDate,
                Comment = "Registration Payment"
            });
        }
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
            if (reg.AssignedTeamId.HasValue)
            {
                var team = await _teams.GetTeamFromTeamId(reg.AssignedTeamId.Value);
                if (team != null)
                {
                    teamName = team.TeamName ?? team.DisplayName;
                    agegroupName = team.Agegroup?.AgegroupName;
                }
            }

            var parts = new List<string>();
            parts.Add($"ARB subscription for {playerFirst} {playerLast}: {jobName}");
            if (!string.IsNullOrWhiteSpace(agegroupName) || !string.IsNullOrWhiteSpace(teamName))
            {
                var suffix = $" - {agegroupName ?? "Agegroup"}:{teamName ?? "Team"}";
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