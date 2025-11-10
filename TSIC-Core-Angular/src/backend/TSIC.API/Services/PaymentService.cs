using AuthorizeNet.Api.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using TSIC.API.DTOs;
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

    public PaymentService(SqlDbContext db, IAdnApiService adnApiService)
    {
        _db = db;
        _adnApiService = adnApiService;
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request, string userId)
    {
        // Get registrations for the family
        var registrations = await _db.Registrations
            .Where(r => r.JobId == request.JobId && r.FamilyUserId == request.FamilyUserId.ToString() && r.UserId != null)
            .ToListAsync();

        if (!registrations.Any())
        {
            return new PaymentResponseDto { Success = false, Message = "No registrations found" };
        }

        // Calculate total amount based on payment option
        decimal totalAmount = 0;
        foreach (var reg in registrations)
        {
            if (request.PaymentOption == PaymentOption.PIF)
            {
                totalAmount += reg.FeeTotal;
            }
            else if (request.PaymentOption == PaymentOption.Deposit)
            {
                totalAmount += reg.FeeBase; // Assuming deposit is FeeBase
            }
            // For ARB, handle subscription creation
        }

        if (request.PaymentOption == PaymentOption.ARB)
        {
            // Create ARB subscription
            var credentials = await _adnApiService.GetJobAdnCredentials_FromJobId(_db, request.JobId);
            var env = _adnApiService.GetADNEnvironment();

            // Assume monthly payments, calculate per occurrence
            decimal perOccurrence = totalAmount / 10; // Example: 10 months

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
                adnArbStartDate: DateTime.Now.AddDays(30),
                adnArbBillingOccurences: 10,
                adnArbIntervalLength: 1
            );

            if (response.messages.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok)
            {
                // Update registrations with subscription ID
                foreach (var reg in registrations)
                {
                    reg.AdnSubscriptionId = response.subscriptionId;
                    reg.AdnSubscriptionAmountPerOccurence = perOccurrence;
                    reg.AdnSubscriptionBillingOccurences = 10;
                    reg.AdnSubscriptionIntervalLength = 1;
                    reg.AdnSubscriptionStartDate = DateTime.Now.AddDays(30);
                    reg.AdnSubscriptionStatus = "active";
                    reg.BActive = true;
                    reg.Modified = DateTime.Now;
                    reg.LebUserId = userId;
                }
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
                invoiceNumber: Guid.NewGuid().ToString(),
                description: "Registration Payment"
            );

            if (response.messages.resultCode == AuthorizeNet.Api.Contracts.V1.messageTypeEnum.Ok)
            {
                // Update registrations
                foreach (var reg in registrations)
                {
                    reg.PaidTotal = reg.PaidTotal + (request.PaymentOption == PaymentOption.PIF ? reg.FeeTotal : reg.FeeBase);
                    reg.OwedTotal = reg.OwedTotal - (request.PaymentOption == PaymentOption.PIF ? reg.FeeTotal : reg.FeeBase);
                    reg.BActive = true;
                    reg.Modified = DateTime.Now;
                    reg.LebUserId = userId;
                }

                // Add to RegistrationAccounting
                foreach (var reg in registrations)
                {
                    var accounting = new RegistrationAccounting
                    {
                        RegistrationId = reg.RegistrationId,
                        Payamt = request.PaymentOption == PaymentOption.PIF ? reg.FeeTotal : reg.FeeBase,
                        Paymeth = $"Credit Card Payment - {request.PaymentOption}",
                        PaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D"), // Assuming CC payment method ID
                        Active = true,
                        Createdate = DateTime.Now,
                        Modified = DateTime.Now,
                        LebUserId = userId,
                        AdnTransactionId = response.transactionResponse.transId
                    };
                    _db.RegistrationAccounting.Add(accounting);
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
}