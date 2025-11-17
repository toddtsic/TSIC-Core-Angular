using AuthorizeNet.Api.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using TSIC.API.Dtos;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface IPaymentService
{
    Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId);
}

public class PaymentService : IPaymentService
{
    private readonly SqlDbContext _db;
    private readonly IAdnApiService _adnApiService;
    private readonly IFeeResolverService _feeResolver;
    private readonly ITeamLookupService _teamLookup;

    public PaymentService(SqlDbContext db, IAdnApiService adnApiService, IFeeResolverService feeResolver, ITeamLookupService teamLookup)
    {
        _db = db;
        _adnApiService = adnApiService;
        _feeResolver = feeResolver;
        _teamLookup = teamLookup;
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId)
    {
        // Load job flags for gating
        var job = await _db.Jobs
            .Where(j => j.JobId == request.JobId)
            .Select(j => new
            {
                j.AdnArb,
                j.AdnArbbillingOccurences,
                j.AdnArbintervalLength,
                j.AdnArbstartDate
            })
            .SingleOrDefaultAsync();

        if (job == null)
        {
            return new PaymentResponseDto { Success = false, Message = "Invalid job" };
        }

        var allowPif = await _db.RegForms.Where(rf => rf.JobId == request.JobId).Select(rf => rf.AllowPif).AnyAsync(v => v);
        var isArb = job.AdnArb == true;

        // Validate requested option vs job flags (extracted)
        if (!ValidatePaymentOption(request.PaymentOption, allowPif, isArb, out var validationMessage))
        {
            return new PaymentResponseDto { Success = false, Message = validationMessage };
        }

        // Get registrations for the family
        var registrations = await _db.Registrations
            .Where(r => r.JobId == request.JobId && r.FamilyUserId == request.FamilyUserId.ToString() && r.UserId != null)
            .ToListAsync();

        if (!registrations.Any())
        {
            return new PaymentResponseDto { Success = false, Message = "No registrations found" };
        }

        // Ensure fees are populated consistently using the centralized resolver
        foreach (var reg in registrations)
        {
            if (reg.AssignedTeamId.HasValue)
            {
                var baseFee = await _feeResolver.ResolveBaseFeeForTeamAsync(reg.AssignedTeamId.Value);
                if (baseFee > 0 && reg.FeeBase != baseFee)
                {
                    reg.FeeBase = baseFee;
                }
                if (reg.FeeTotal <= 0 && baseFee > 0)
                {
                    reg.FeeTotal = baseFee; // minimal parity until discounts/late fees are modeled
                }
                if (reg.OwedTotal <= 0 && reg.FeeTotal > 0 && reg.PaidTotal <= 0)
                {
                    reg.OwedTotal = reg.FeeTotal;
                }
            }
        }

        // Calculate per-registration charges and total (non-ARB)
        Dictionary<Guid, decimal> charges = new();
        decimal totalAmount = 0m;
        if (request.PaymentOption != PaymentOption.ARB)
        {
            charges = await ComputeChargesAsync(registrations, request.PaymentOption);
            totalAmount = charges.Values.Sum();
        }
        if (request.PaymentOption != PaymentOption.ARB && totalAmount <= 0)
        {
            return new PaymentResponseDto { Success = false, Message = "Nothing due for selected registrations." };
        }

        if (request.PaymentOption == PaymentOption.ARB)
        {
            // Create ARB subscription
            var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(_db, request.JobId);
            var env = _adnApiService.GetADNEnvironment();

            // Derive ARB schedule from job settings (extracted)
            var (occur, intervalLen, start) = BuildArbSchedule(job.AdnArbbillingOccurences, job.AdnArbintervalLength, job.AdnArbstartDate);

            // Per occurrence: divide fee total evenly across occurrences
            decimal grandTotal = registrations.Sum(r => r.OwedTotal);
            decimal perOccurrence = Math.Round(grandTotal / occur, 2, MidpointRounding.AwayFromZero);

            var response = _adnApiService.ADN_ARB_CreateMonthlySubscription(
                env: env,
                adnLoginId: credentials.AdnLoginId!,
                adnTransactionKey: credentials.AdnTransactionKey!,
                ccNumber: request.CreditCard!.Number!,
                ccCode: request.CreditCard.Code!,
                ccExpiryDate: request.CreditCard.Expiry!,
                ccFirstName: request.CreditCard.FirstName!,
                ccLastName: request.CreditCard.LastName!,
                ccAddress: request.CreditCard.Address!,
                ccZip: request.CreditCard.Zip!,
                ccEmail: "", // Need to get from user
                ccInvoiceNumber: Guid.NewGuid().ToString(),
                ccDescription: "Registration Payment",
                ccPerIntervalCharge: perOccurrence,
                adnArbStartDate: start,
                adnArbBillingOccurences: occur,
                adnArbIntervalLength: intervalLen
            );

            if (response.messages.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok)
            {
                // Update registrations with subscription ID
                await UpdateRegistrationsForArbAsync(registrations, response.subscriptionId, perOccurrence, occur, intervalLen, start, userId);
                await _db.SaveChangesAsync();

                return new PaymentResponseDto
                {
                    Success = true,
                    Message = "ARB subscription created",
                    SubscriptionId = response.subscriptionId
                };
            }
            else
            {
                return new PaymentResponseDto { Success = false, Message = response.messages.message[0].text };
            }
        }
        else
        {
            // Charge card for PIF or Deposit
            var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(_db, request.JobId);
            var env = _adnApiService.GetADNEnvironment();

            // Idempotency pre-check
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var dup = await _db.RegistrationAccounting
                    .Join(_db.Registrations, a => a.RegistrationId, r => r.RegistrationId, (a, r) => new { a, r })
                    .Where(x => x.r.JobId == request.JobId
                                && x.r.FamilyUserId == request.FamilyUserId.ToString()
                                && x.a.AdnInvoiceNo == request.IdempotencyKey)
                    .AnyAsync();
                if (dup)
                {
                    return new PaymentResponseDto { Success = true, Message = "Duplicate prevented (idempotent)." };
                }
            }

            var response = _adnApiService.ADN_ChargeCard(
                env: env,
                adnLoginId: credentials.AdnLoginId!,
                adnTransactionKey: credentials.AdnTransactionKey!,
                ccNumber: request.CreditCard!.Number!,
                ccCode: request.CreditCard.Code!,
                ccExpiryDate: request.CreditCard.Expiry!,
                ccFirstName: request.CreditCard.FirstName!,
                ccLastName: request.CreditCard.LastName!,
                ccAddress: request.CreditCard.Address!,
                ccZip: request.CreditCard.Zip!,
                ccAmount: totalAmount,
                invoiceNumber: string.IsNullOrWhiteSpace(request.IdempotencyKey) ? Guid.NewGuid().ToString() : request.IdempotencyKey,
                description: "Registration Payment"
            );

            if (response.messages.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok)
            {
                // Update registrations (static helper; no await required)
                UpdateRegistrationsForCharge(registrations, userId, charges);

                // Add to RegistrationAccounting (static helper)
                AddAccountingEntries(registrations, request.PaymentOption, userId, response.transactionResponse.transId, request.IdempotencyKey, charges);

                // Persist VerticalInsure policy details when user confirmed (best-effort; optional)
                if (request.ViConfirmed == true && !string.IsNullOrWhiteSpace(request.ViPolicyNumber))
                {
                    foreach (var reg in registrations)
                    {
                        if (string.IsNullOrWhiteSpace(reg.RegsaverPolicyId))
                        {
                            reg.RegsaverPolicyId = request.ViPolicyNumber;
                            reg.RegsaverPolicyIdCreateDate = request.ViPolicyCreateDate ?? DateTime.Now;
                            reg.Modified = DateTime.Now;
                            reg.LebUserId = userId;
                        }
                    }
                }

                await _db.SaveChangesAsync();

                return new PaymentResponseDto
                {
                    Success = true,
                    Message = "Payment processed",
                    TransactionId = response.transactionResponse.transId
                };
            }
            else
            {
                return new PaymentResponseDto { Success = false, Message = response.transactionResponse?.errors?[0].errorText ?? "Payment failed" };
            }
        }
    }

    private static bool ValidatePaymentOption(PaymentOption option, bool allowPif, bool isArb, out string message)
    {
        message = string.Empty;
        if (option == PaymentOption.PIF && !allowPif && !isArb)
        {
            message = "Pay In Full is not allowed for this job.";
            return false;
        }
        if (option == PaymentOption.Deposit && isArb)
        {
            message = "Deposit option is not available when ARB is enabled.";
            return false;
        }
        if (option == PaymentOption.ARB && !isArb)
        {
            message = "Recurring billing (ARB) is not enabled for this job.";
            return false;
        }
        return true;
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

    private static Task UpdateRegistrationsForArbAsync(IEnumerable<Registrations> registrations, string subscriptionId, decimal perOccurrence, short occur, short intervalLen, DateTime start, string userId)
    {
        foreach (var reg in registrations)
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
        return Task.CompletedTask;
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
}