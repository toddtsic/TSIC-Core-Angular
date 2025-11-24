using AuthorizeNet.Api.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Dtos;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;
using TSIC.API.Services.Email;
using MimeKit;

namespace TSIC.API.Services;

public interface IPaymentService
{
    Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId);
}

public class PaymentService : IPaymentService
{
    private readonly SqlDbContext _db;
    private readonly IAdnApiService _adnApiService;
    private readonly IPlayerBaseTeamFeeResolverService _feeResolver;
    private readonly ITeamLookupService _teamLookup;
    private readonly ILogger<PaymentService> _logger;
    private readonly IPlayerRegConfirmationService? _confirmation;
    private readonly IEmailService? _email;

    private sealed record JobInfo(bool? AdnArb, int? AdnArbbillingOccurences, int? AdnArbintervalLength, DateTime? AdnArbstartDate);

    public PaymentService(SqlDbContext db, IAdnApiService adnApiService, IPlayerBaseTeamFeeResolverService feeResolver, ITeamLookupService teamLookup, ILogger<PaymentService> logger)
    {
        _db = db;
        _adnApiService = adnApiService;
        _feeResolver = feeResolver;
        _teamLookup = teamLookup;
        _logger = logger;
    }

    // Extended constructor adding confirmation + email services; preserves backward compatibility with tests using the original signature.
    public PaymentService(SqlDbContext db, IAdnApiService adnApiService, IPlayerBaseTeamFeeResolverService feeResolver, ITeamLookupService teamLookup, ILogger<PaymentService> logger, IPlayerRegConfirmationService confirmation, IEmailService email)
        : this(db, adnApiService, feeResolver, teamLookup, logger)
    {
        _confirmation = confirmation;
        _email = email;
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId)
    {
        var v = await ValidatePaymentRequestAsync(request);
        if (v.Response != null) return v.Response;
        var job = v.Job!;
        var registrations = v.Registrations!;
        var cc = v.Card!;
        await NormalizeFeesAsync(registrations);
        if (request.PaymentOption == PaymentOption.ARB)
            return await ProcessArbAsync(request, userId, registrations, job, cc);
        var charges = await ComputeChargesAsync(registrations, request.PaymentOption);
        var total = charges.Values.Sum();
        if (total <= 0m) return Fail("Nothing due for selected registrations.", "NOTHING_DUE");
        return await ExecutePrimaryChargeAsync(request, userId, registrations, cc, charges, total);
    }

    private async Task<(PaymentResponseDto? Response, JobInfo? Job, List<Registrations>? Registrations, CreditCardInfo? Card)> ValidatePaymentRequestAsync(PaymentRequestDto request)
    {
        if (request == null) return (Fail("Invalid request", "INVALID_REQUEST"), null, null, null);
        var jobQuery = _db.Jobs.Where(j => j.JobId == request.JobId)
            .Select(j => new JobInfo(j.AdnArb, j.AdnArbbillingOccurences, j.AdnArbintervalLength, j.AdnArbstartDate));
        var job = await jobQuery.SingleOrDefaultAsync();
        if (job == null) return (Fail("Invalid job", "INVALID_JOB"), null, null, null);
        var regsQuery = _db.Registrations.Where(r => r.JobId == request.JobId && r.FamilyUserId == request.FamilyUserId.ToString() && r.UserId != null);
        var registrations = await regsQuery.ToListAsync();
        if (!registrations.Any()) return (Fail("No registrations found", "NO_REGISTRATIONS"), null, null, null);
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

    private async Task<PaymentResponseDto> ProcessArbAsync(PaymentRequestDto request, string userId, List<Registrations> registrations, JobInfo job, CreditCardInfo cc)
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
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(_db, request.JobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
        {
            return new PaymentResponseDto { Success = false, Message = "Missing payment gateway credentials (Authorize.Net).", ErrorCode = "MISSING_GATEWAY_CREDS" };
        }
        var env = _adnApiService.GetADNEnvironment();
        var schedule = BuildArbSchedule(job.AdnArbbillingOccurences, job.AdnArbintervalLength, job.AdnArbstartDate);
        var (occur, intervalLen, start) = schedule;
        NormalizeProcessingFees(registrations, request.JobId, userId);
        await _db.SaveChangesAsync();
        var args = new ArbSubArgs(env, credentials.AdnLoginId!, credentials.AdnTransactionKey!, occur, intervalLen, start, cc, userId);
        var (subs, failed) = await CreateArbSubscriptionsAsync(registrations, args);
        await _db.SaveChangesAsync();
        var response = BuildArbResponse(subs, failed);
        // Always attempt confirmation email after any successful subscription creation.
        if (response.Success && subs.Count > 0)
        {
            await TrySendConfirmationEmailAsync(request.JobId, request.FamilyUserId.ToString(), userId);
        }
        return response;
    }

    private async Task<PaymentResponseDto> ExecutePrimaryChargeAsync(PaymentRequestDto request, string userId, List<Registrations> registrations, CreditCardInfo cc, Dictionary<Guid, decimal> charges, decimal totalAmount)
    {
        var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(_db, request.JobId);
        if (credentials == null || string.IsNullOrWhiteSpace(credentials.AdnLoginId) || string.IsNullOrWhiteSpace(credentials.AdnTransactionKey))
        {
            return new PaymentResponseDto { Success = false, Message = "Missing payment gateway credentials (Authorize.Net).", ErrorCode = "MISSING_GATEWAY_CREDS" };
        }
        var env = _adnApiService.GetADNEnvironment();
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey) && await IsDuplicateAsync(request))
            return new PaymentResponseDto { Success = true, Message = "Duplicate prevented (idempotent).", ErrorCode = "DUPLICATE_PREVENTED" };
        // Build deterministic invoice number using first registration (pattern: customerAI_jobAI_registrationAI)
        var invoiceReg = registrations[0];
        var invoiceNumber = await BuildInvoiceNumberForRegistrationAsync(request.JobId, invoiceReg.RegistrationId);
        var response = _adnApiService.ADN_Charge(new AdnChargeRequest(
            Env: env,
            LoginId: credentials.AdnLoginId!,
            TransactionKey: credentials.AdnTransactionKey!,
            CardNumber: cc.Number!,
            CardCode: cc.Code!,
            Expiry: FormatExpiry(cc.Expiry!),
            FirstName: cc.FirstName!,
            LastName: cc.LastName!,
            Address: cc.Address!,
            Zip: cc.Zip!,
            Email: cc.Email!,
            Phone: cc.Phone!,
            Amount: totalAmount,
            InvoiceNumber: invoiceNumber,
            Description: "Registration Payment"
        ));
        if (response == null || response.messages == null)
        {
            return new PaymentResponseDto { Success = false, Message = "Payment gateway returned no response.", ErrorCode = "CHARGE_NULL_RESPONSE" };
        }
        if (response.messages.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok)
        {
            UpdateRegistrationsForCharge(registrations, userId, charges);
            AddAccountingEntries(registrations, request.PaymentOption, userId, response.transactionResponse.transId, request.IdempotencyKey, charges);
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
            await _db.SaveChangesAsync();
            // Attempt confirmation email after successful charge (always send, never gated by prior sends)
            await TrySendConfirmationEmailAsync(request.JobId, request.FamilyUserId.ToString(), userId);
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

    private async Task NormalizeFeesAsync(IEnumerable<Registrations> registrations)
    {
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue) continue;
            var baseFee = await _feeResolver.ResolveBaseFeeForTeamAsync(reg.AssignedTeamId.Value);
            if (baseFee <= 0) continue;
            if (reg.FeeBase != baseFee) reg.FeeBase = baseFee;
            if (reg.FeeTotal <= 0) reg.FeeTotal = baseFee;
            if (reg.OwedTotal <= 0 && reg.PaidTotal <= 0) reg.OwedTotal = reg.FeeTotal;
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
            var resp = _adnApiService.ADN_ARB_CreateMonthlySubscription(new AdnArbCreateRequest(
                Env: args.Env,
                LoginId: args.LoginId,
                TransactionKey: args.TransactionKey,
                CardNumber: args.Card.Number!,
                CardCode: args.Card.Code!,
                Expiry: FormatExpiry(args.Card.Expiry!),
                FirstName: args.Card.FirstName!,
                LastName: args.Card.LastName!,
                Address: args.Card.Address!,
                Zip: args.Card.Zip!,
                Email: args.Card.Email!,
                Phone: args.Card.Phone!,
                InvoiceNumber: invoiceNumber,
                Description: description,
                PerIntervalCharge: perOccur,
                StartDate: args.StartDate,
                BillingOccurrences: args.Occur,
                IntervalLength: args.IntervalLen
            ));
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

    private async Task<bool> IsDuplicateAsync(PaymentRequestDto request)
    {
        return await _db.RegistrationAccounting
            .Join(_db.Registrations, a => a.RegistrationId, r => r.RegistrationId, (a, r) => new { a, r })
            .AnyAsync(x => x.r.JobId == request.JobId && x.r.FamilyUserId == request.FamilyUserId.ToString() && x.a.AdnInvoiceNo == request.IdempotencyKey);
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

    private void AddAccountingEntries(IEnumerable<Registrations> registrations, PaymentOption option, string userId, string adnTransactionId, string? idempotencyKey, IReadOnlyDictionary<Guid, decimal> charges)
    {
        foreach (var reg in registrations)
        {
            if (reg.RegistrationId == Guid.Empty) continue;
            if (!charges.TryGetValue(reg.RegistrationId, out var payAmt) || payAmt <= 0) continue;

            _db.RegistrationAccounting.Add(new RegistrationAccounting
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
                AdnInvoiceNo = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey
            });
        }
    }

    // Build invoice number pattern: customerAI_jobAI_registrationAI (<=20 chars Authorize.Net limit).
    // Fallback strategy if length exceeds limit: jobAI_registrationAI, then registrationAI, then truncated.
    private async Task<string> BuildInvoiceNumberForRegistrationAsync(Guid jobId, Guid registrationId)
    {
        try
        {
            var q = _db.Registrations
                .Where(r => r.RegistrationId == registrationId && r.JobId == jobId)
                .Join(_db.Jobs, r => r.JobId, j => j.JobId, (r, j) => new { r, j })
                .Join(_db.Customers, x => x.j.CustomerId, c => c.CustomerId, (x, c) => new { x.r, x.j, c })
                .Select(x => new { x.c.CustomerAi, x.j.JobAi, x.r.RegistrationAi });
            var data = await q.SingleOrDefaultAsync();
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
            // Player + Job info
            var pjQuery = _db.Registrations
                .Where(r => r.RegistrationId == reg.RegistrationId)
                .Join(_db.Jobs, r => r.JobId, j => j.JobId, (r, j) => new { r, j })
                .Join(_db.AspNetUsers, x => x.r.UserId, u => u.Id, (x, u) => new { x.r, x.j.JobName, u.FirstName, u.LastName, u.UserName });
            var pj = await pjQuery.SingleOrDefaultAsync();
            string playerFirst = pj?.FirstName?.Trim() ?? "Player";
            string playerLast = pj?.LastName?.Trim() ?? pj?.UserName?.Trim() ?? reg.RegistrationAi.ToString();
            string jobName = pj?.JobName?.Trim() ?? "Registration";

            string? teamName = null;
            string? agegroupName = null;
            if (reg.AssignedTeamId.HasValue)
            {
                var taQuery = _db.Teams
                    .Where(t => t.TeamId == reg.AssignedTeamId.Value)
                    .Select(t => new { t.TeamName, AgegroupName = t.Agegroup.AgegroupName });
                var ta = await taQuery.SingleOrDefaultAsync();
                if (ta != null)
                {
                    teamName = ta.TeamName;
                    agegroupName = ta.AgegroupName;
                }
            }

            // Compose parts
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
            return "Registration Payment"; // Fallback to prior static description
        }
    }

    // Builds and sends the registration confirmation email, then marks BConfirmationSent=true for any registrations not yet flagged.
    // This never suppresses sending; flag is purely informational. Guarded against missing optional services.
    private async Task TrySendConfirmationEmailAsync(Guid jobId, string familyUserId, string userId)
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
            var message = await BuildConfirmationMessageAsync(jobId, familyUserId, toList);
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
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fam = await _db.Families.AsNoTracking().FirstOrDefaultAsync(f => f.FamilyUserId == familyUserId);
        if (!string.IsNullOrWhiteSpace(fam?.MomEmail)) recipients.Add(fam!.MomEmail!.Trim());
        if (!string.IsNullOrWhiteSpace(fam?.DadEmail)) recipients.Add(fam!.DadEmail!.Trim());
        var playerRegs = await _db.Registrations.AsNoTracking().Include(r => r.User)
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId)
            .Select(r => r.User!.Email)
            .ToListAsync();
        foreach (var e in playerRegs)
        {
            var norm = e?.Trim();
            if (!string.IsNullOrWhiteSpace(norm)) recipients.Add(norm!);
        }
        return recipients.Select(x => x.Trim()).Where(x => x.Contains('@')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<MimeMessage?> BuildConfirmationMessageAsync(Guid jobId, string familyUserId, List<string> toList)
    {
        var (subject, html) = await _confirmation!.BuildEmailAsync(jobId, familyUserId, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(html)) return null;
        var message = new MimeMessage();
        foreach (var addr in toList) message.To.Add(MailboxAddress.Parse(addr));
        message.Subject = string.IsNullOrWhiteSpace(subject) ? "Registration Confirmation" : subject;
        var builder = new BodyBuilder { HtmlBody = html };
        message.Body = builder.ToMessageBody();
        return message;
    }

    private async Task FlagRegistrationsAsync(Guid jobId, string familyUserId, string userId)
    {
        var regsToFlag = await _db.Registrations
            .Where(r => r.JobId == jobId && r.FamilyUserId == familyUserId && !r.BConfirmationSent)
            .ToListAsync();
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
        await _db.SaveChangesAsync();
        _logger.LogInformation("[ConfirmationEmail] Flagged {Count} registrations jobId={JobId} familyUserId={FamilyUserId}", regsToFlag.Count, jobId, familyUserId);
    }
}