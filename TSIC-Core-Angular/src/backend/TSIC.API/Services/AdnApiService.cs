using AuthorizeNet.Api.Contracts.V1;
using AuthorizeNet.Api.Controllers;
using AuthorizeNet.Api.Controllers.Bases;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TSIC.API.DTOs;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public interface IAdnApiService
{
    AuthorizeNet.Environment GetADNEnvironment(bool bProdOnly = false);

    Task<AdnCredentialsViewModel> GetJobAdnCredentials_FromJobId(
        SqlDbContext _context,
        Guid jobId,
        bool bProdOnly = false
    );
    Task<AdnCredentialsViewModel> GetJobAdnCredentials_FromCustomerId(
        SqlDbContext _context,
        Guid customerId,
        bool bProdOnly = false
    );

    getTransactionDetailsResponse ADN_GetTransactionDetails(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String transactionId
    );

    createTransactionResponse ADN_AuthorizeCard(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumber,
        String ccCode,
        String ccExpiryDate,
        String ccFirstName,
        String ccLastName,
        String ccAddress,
        String ccZip,
        Decimal ccAmount
    );

    createTransactionResponse ADN_ChargeCard(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumber,
        String ccCode,
        String ccExpiryDate,
        String ccFirstName,
        String ccLastName,
        String ccAddress,
        String ccZip,
        Decimal ccAmount,
        String invoiceNumber,
        String description
    );

    createTransactionResponse ADN_ChargeCustomerProfile(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String customerPaymentProfileId,
        Decimal ccAmount,
        String invoiceNumber,
        String description
    );

    createTransactionResponse ADN_RefundCard(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumberLast4,
        String ccExpiryDate,
        String transactionId,
        Decimal ccAmount,
        String invoiceNumber
    );

    createTransactionResponse ADN_VoidTransaction(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String transactionId
    );

    ARBCreateSubscriptionResponse ADN_ARB_CreateMonthlySubscription(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumber,
        String ccCode,
        String ccExpiryDate,
        String ccFirstName,
        String ccLastName,
        String ccAddress,
        String ccZip,
        String ccEmail,
        String ccInvoiceNumber,
        String ccDescription,
        Decimal ccPerIntervalCharge,
        DateTime? adnArbStartDate,
        short adnArbBillingOccurences,
        short adnArbIntervalLength
    );

    getSettledBatchListResponse GetSettleBatchList_FromDateRange(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        DateTime firstSettlementDate,
        DateTime lastSettlementDate,
        bool includeStatistics
    );

    getTransactionListResponse GetTransactionList_ByBatchId(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey, String batchId
    );

    ARBGetSubscriptionResponse GetSubscriptionDetails(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String subscriptionId
    );

    ARBGetSubscriptionStatusResponse GetSubscriptionStatus(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String subscriptionId
    );

    ARBCancelSubscriptionResponse ADN_CancelSubscription(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String subscriptionId
    );

    ARBUpdateSubscriptionResponse ADN_UpdateSubscription(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string subscriptionId,
        decimal chargePerOccurence,
        string cardNumber,
        string expirationDate,
        string cardCode,
        string firstName,
        string lastName,
        string address,
        string zip,
        string email
    );

    ARBGetSubscriptionListResponse ARBGetSubscriptionListRequest(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        ARBGetSubscriptionListSearchTypeEnum searchType
    );
}

public class AdnApiService : IAdnApiService
{
    public AuthorizeNet.Environment GetADNEnvironment(bool bProdOnly = false)
    {

#if DEBUG
        if (bProdOnly)
        {
            return AuthorizeNet.Environment.PRODUCTION;
        }
        else
        {
            return AuthorizeNet.Environment.SANDBOX;
        }
#else
        return AuthorizeNet.Environment.PRODUCTION;
#endif
    }

    public async Task<AdnCredentialsViewModel> GetJobAdnCredentials_FromJobId(SqlDbContext _context, Guid jobId, bool bProdOnly = false)
    {
        // Legacy LINQ block removed (commented out) to reduce noise; current logic below handles sandbox vs production.

        bool isDEBUG = false;
#if DEBUG
        if (!bProdOnly)
        {
            isDEBUG = true;
        }
#endif
        if (isDEBUG)
        {
            //TSIC Sandbox
            return new AdnCredentialsViewModel()
            {
                AdnLoginId = "4dE5m4WR9ey",
                AdnTransactionKey = "6zmzD35C47kv45Sn"
            };

            // Production credential example retained in version control history if needed.
        }
        else
        {
            return await (
                from j in _context.Jobs
                join c in _context.Customers on j.CustomerId equals c.CustomerId
                where j.JobId == jobId
                select new AdnCredentialsViewModel()
                {
                    AdnLoginId = c.AdnLoginId,
                    AdnTransactionKey = c.AdnTransactionKey
                }
            )
            .AsNoTracking()
            .SingleOrDefaultAsync();
        }
    }

    public async Task<AdnCredentialsViewModel> GetJobAdnCredentials_FromCustomerId(SqlDbContext _context, Guid customerId, bool bProdOnly = false)
    {
        bool isDEBUG = false;
#if DEBUG
        if (!bProdOnly)
        {
            isDEBUG = true;
        }
#endif
        if (isDEBUG)
        {
            //TSIC Sandbox
            return new AdnCredentialsViewModel()
            {
                AdnLoginId = "4dE5m4WR9ey",
                AdnTransactionKey = "6zmzD35C47kv45Sn"
            };
        }
        else
        {
            return await _context.Customers
                .AsNoTracking()
                .Where(c => c.CustomerId == customerId)
                .Select(c => new AdnCredentialsViewModel()
                {
                    AdnLoginId = c.AdnLoginId,
                    AdnTransactionKey = c.AdnTransactionKey
                })
                .SingleOrDefaultAsync();
        }
    }

    public createTransactionResponse ADN_AuthorizeCard(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumber,
        String ccCode,
        String ccExpiryDate,
        String ccFirstName,
        String ccLastName,
        String ccAddress,
        String ccZip,
        Decimal ccAmount
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey,
        };

        var creditCard = new creditCardType
        {
            cardNumber = ccNumber,
            expirationDate = ccExpiryDate,
            cardCode = ccCode
        };

        var billingAddress = new customerAddressType
        {
            firstName = ccFirstName,
            lastName = ccLastName,
            address = ccAddress,
            zip = ccZip
        };

        //standard api call to retrieve response
        var paymentType = new paymentType { Item = creditCard };

        var transactionRequest = new transactionRequestType
        {
            transactionType = transactionTypeEnum.authOnlyTransaction.ToString(),    // authorize only
            amount = ccAmount,
            payment = paymentType,
            billTo = billingAddress
        };

        var request = new createTransactionRequest { transactionRequest = transactionRequest };

        // instantiate the controller that will call the service
        var controller = new createTransactionController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        var response = controller.GetApiResponse();

        UpdateCreateTransactionApiResponse(response); // Normalize messages / errors

        return response;
    }

    public createTransactionResponse ADN_ChargeCustomerProfile(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String customerPaymentProfileId,
        Decimal ccAmount,
        String invoiceNumber,
        String description
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey,
        };

        //create a customer payment profile
        customerProfilePaymentType profileToCharge = new customerProfilePaymentType();
        profileToCharge.customerProfileId = customerPaymentProfileId;
        profileToCharge.paymentProfile = new paymentProfile { paymentProfileId = customerPaymentProfileId };

        orderType orderInfo = new orderType()
        {
            invoiceNumber = invoiceNumber,
            description = description
        };

        var transactionRequest = new transactionRequestType
        {
            transactionType = transactionTypeEnum.authCaptureTransaction.ToString(),    // refund type
            amount = ccAmount,
            profile = profileToCharge,
            order = orderInfo
        };

        var request = new createTransactionRequest
        {
            transactionRequest = transactionRequest
        };

        // instantiate the collector that will call the service
        var controller = new createTransactionController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        var response = controller.GetApiResponse();

        return response;
    }

    public createTransactionResponse ADN_ChargeCard(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumber,
        String ccCode,
        String ccExpiryDate,
        String ccFirstName,
        String ccLastName,
        String ccAddress,
        String ccZip,
        Decimal ccAmount,
        String invoiceNumber,
        String description
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey,
        };

        var creditCard = new creditCardType
        {
            cardNumber = ccNumber,
            expirationDate = ccExpiryDate,
            cardCode = ccCode
        };

        var billingAddress = new customerAddressType
        {
            firstName = ccFirstName.Trim(),
            lastName = ccLastName.Trim(),
            address = ccAddress.Trim(),
            zip = ccZip
        };

        //standard api call to retrieve response
        var paymentType = new paymentType { Item = creditCard };

        orderType orderInfo = new orderType()
        {
            invoiceNumber = invoiceNumber,
            description = description.Trim()
        };

        var transactionRequest = new transactionRequestType
        {
            transactionType = transactionTypeEnum.authCaptureTransaction.ToString(),    // charge the card
            amount = ccAmount,
            payment = paymentType,
            billTo = billingAddress,
            order = orderInfo
        };

        var request = new createTransactionRequest { transactionRequest = transactionRequest };

        // instantiate the controller that will call the service
        var controller = new createTransactionController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        var response = controller.GetApiResponse();

        UpdateCreateTransactionApiResponse(response);

        return response;
    }

    public createTransactionResponse ADN_RefundCard(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumberLast4,
        String ccExpiryDate,
        String transactionId,
        Decimal ccAmount,
        String invoiceNumber
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey,
        };

        var creditCard = new creditCardType
        {
            cardNumber = ccNumberLast4,
            expirationDate = ccExpiryDate
        };

        //standard api call to retrieve response
        var paymentType = new paymentType { Item = creditCard };

        orderType orderInfo = new orderType()
        {
            invoiceNumber = invoiceNumber,
            description = "Credit Card Credit"
        };

        var transactionRequest = new transactionRequestType
        {
            transactionType = transactionTypeEnum.refundTransaction.ToString(),    // refund type
            payment = paymentType,
            amount = ccAmount,
            refTransId = transactionId,
            order = orderInfo
        };

        var request = new createTransactionRequest { transactionRequest = transactionRequest };

        // instantiate the controller that will call the service
        var controller = new createTransactionController(request);
        controller.Execute();

        var response = controller.GetApiResponse();

        return response;
    }

    public createTransactionResponse ADN_VoidTransaction(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String transactionId
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        var transactionRequest = new transactionRequestType
        {
            transactionType = transactionTypeEnum.voidTransaction.ToString(),    // refund type
            refTransId = transactionId
        };

        var request = new createTransactionRequest { transactionRequest = transactionRequest };

        // instantiate the controller that will call the service
        var controller = new createTransactionController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        var response = controller.GetApiResponse();

        return response;
    }


    public getTransactionDetailsResponse ADN_GetTransactionDetails(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String transactionId
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        var request = new getTransactionDetailsRequest
        {
            transId = transactionId
        };

        // instantiate the controller that will call the service
        var controller = new getTransactionDetailsController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        var response = controller.GetApiResponse();

        return response;
    }

    public ARBCreateSubscriptionResponse ADN_ARB_CreateMonthlySubscription(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String ccNumber,
        String ccCode,
        String ccExpiryDate,
        String ccFirstName,
        String ccLastName,
        String ccAddress,
        String ccZip,
        String ccEmail,
        String ccInvoiceNumber,
        String ccDescription,
        Decimal ccPerIntervalCharge,
        DateTime? adnArbStartDate,
        short adnArbBillingOccurences,
        short adnArbIntervalLength
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey,
        };

        paymentScheduleTypeInterval interval = new paymentScheduleTypeInterval
        {
            length = adnArbIntervalLength,    // months can be indicated between 1 and 12
            unit = ARBSubscriptionUnitEnum.months
        };

        paymentScheduleType schedule = new paymentScheduleType
        {
            interval = interval,
            startDate = (adnArbStartDate != null) ? (DateTime)adnArbStartDate : DateTime.Now.AddDays(1),   // start date should be tomorrow if adnArbStartDate is null
            totalOccurrences = adnArbBillingOccurences, // 999 indicates no end date
            trialOccurrences = 0
        };

        #region Payment Information
        var creditCard = new creditCardType
        {
            cardNumber = ccNumber,
            expirationDate = ccExpiryDate,
            cardCode = ccCode
        };

        //standard api call to retrieve response
        paymentType cc = new paymentType { Item = creditCard };
        #endregion

        customerType customerInfo = new customerType()
        {
            email = ccEmail
        };

        nameAndAddressType addressInfo = new nameAndAddressType()
        {
            firstName = ccFirstName,
            lastName = ccLastName,
            address = ccAddress,
            zip = ccZip
        };

        orderType orderInfo = new orderType()
        {
            invoiceNumber = ccInvoiceNumber,
            description = ccDescription
        };

        ARBSubscriptionType subscriptionType = new ARBSubscriptionType()
        {
            amount = ccPerIntervalCharge,
            trialAmount = 0.00m,
            paymentSchedule = schedule,
            billTo = addressInfo,
            payment = cc,
            order = orderInfo,
            customer = customerInfo
        };

        var request = new ARBCreateSubscriptionRequest { subscription = subscriptionType };

        var controller = new ARBCreateSubscriptionController(request);          // instantiate the controller that will call the service
        controller.Execute();

        ARBCreateSubscriptionResponse response = controller.GetApiResponse();   // get the response from the service (errors contained if any)

        return response;
    }

    public getSettledBatchListResponse GetSettleBatchList_FromDateRange(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        DateTime firstSettlementDate,
        DateTime lastSettlementDate,
        bool includeStatistics
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        var request = new getSettledBatchListRequest
        {
            firstSettlementDate = firstSettlementDate,
            lastSettlementDate = lastSettlementDate,
            includeStatistics = includeStatistics
        };

        // instantiate the controller that will call the service
        var controller = new getSettledBatchListController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        return controller.GetApiResponse();
    }

    public getTransactionListResponse GetTransactionList_ByBatchId(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey, String batchId
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        var request = new getTransactionListRequest
        {
            batchId = batchId,
            paging = new Paging
            {
                limit = 1000,
                offset = 1
            },

            sorting = new TransactionListSorting
            {
                orderBy = TransactionListOrderFieldEnum.id,
                orderDescending = true
            }
        };

        // instantiate the controller that will call the service
        var controller = new getTransactionListController(request);
        controller.Execute();

        // get the response from the service (errors contained if any)
        getTransactionListResponse response = controller.GetApiResponse();
        return response;
    }

    public ARBGetSubscriptionResponse GetSubscriptionDetails(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String subscriptionId
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        var request = new ARBGetSubscriptionRequest { subscriptionId = subscriptionId };

        var controller = new ARBGetSubscriptionController(request);             // instantiate the controller that will call the service

        controller.Execute();

        var response = controller.GetApiResponse();

        return response;   // get the response from the service (errors contained if any)
    }

    public ARBGetSubscriptionStatusResponse GetSubscriptionStatus(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String subscriptionId
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        //please update the subscriptionId according to your sandbox credentials
        var request = new ARBGetSubscriptionStatusRequest { subscriptionId = subscriptionId };

        var controller = new ARBGetSubscriptionStatusController(request);                          // instantiate the controller that will call the service
        controller.Execute();

        ARBGetSubscriptionStatusResponse response = controller.GetApiResponse();                   // get the response from the service (errors contained if any)

        // Validation output suppressed (was previously Console logging) to keep service side-effect free.

        return response;
    }

    public ARBCancelSubscriptionResponse ADN_CancelSubscription(
        AuthorizeNet.Environment env,
        String adnLoginId,
        String adnTransactionKey,
        String subscriptionId
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        var request = new ARBCancelSubscriptionRequest { subscriptionId = subscriptionId };

        var controller = new ARBCancelSubscriptionController(request);             // instantiate the controller that will call the service

        controller.Execute();

        return controller.GetApiResponse();   // get the response from the service (errors contained if any)
    }

    public ARBUpdateSubscriptionResponse ADN_UpdateSubscription(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string subscriptionId,
        decimal chargePerOccurence,
        string cardNumber,
        string expirationDate,
        string cardCode,
        string firstName,
        string lastName,
        string address,
        string zip,
        string email
    )
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

        // define the merchant information (authentication / transaction id)
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = adnLoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = adnTransactionKey
        };

        #region Payment Information
        var creditCard = new creditCardType
        {
            cardNumber = cardNumber,
            expirationDate = expirationDate,
            cardCode = cardCode
        };

        //standard api call to retrieve response
        paymentType cc = new paymentType { Item = creditCard };
        #endregion

        customerType customerInfo = new customerType()
        {
            email = email
        };

        nameAndAddressType addressInfo = new nameAndAddressType()
        {
            firstName = firstName,
            lastName = lastName,
            address = address,
            zip = zip
        };

        ARBSubscriptionType subscriptionType = new ARBSubscriptionType()
        {
            amount = chargePerOccurence,
            billTo = addressInfo,
            payment = cc,
            customer = customerInfo
        };

        //Please change the subscriptionId according to your request
        var request = new ARBUpdateSubscriptionRequest { subscription = subscriptionType, subscriptionId = subscriptionId };
        var controller = new ARBUpdateSubscriptionController(request);
        controller.Execute();

        ARBUpdateSubscriptionResponse response = controller.GetApiResponse();
        return response;
    }

    public ARBGetSubscriptionListResponse ARBGetSubscriptionListRequest(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        ARBGetSubscriptionListSearchTypeEnum searchType
    )
    {
        try
        {
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;

            // define the merchant information (authentication / transaction id)
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
            {
                name = adnLoginId,
                ItemElementName = ItemChoiceType.transactionKey,
                Item = adnTransactionKey
            };

            var request = new ARBGetSubscriptionListRequest
            {
                searchType = searchType
            };

            var controller = new ARBGetSubscriptionListController(request);          // instantiate the controller that will call the service
            controller.Execute();

            ARBGetSubscriptionListResponse response = controller.GetApiResponse();   // get the response from the service (errors contained if any)

            return response;

        }
        catch
        {
            return null;
        }

    }

    #region helper functions
    private void UpdateCreateTransactionApiResponse(createTransactionResponse response)
    {
        // validate response
        if (response != null)
        {
            if (response.messages.resultCode == messageTypeEnum.Ok)
            {
                if (response.transactionResponse.messages != null)
                {
                    //success case
                }
                else
                {
                    if (response.transactionResponse.errors != null)
                    {
                        response.transactionResponse.errors[0].errorText = UpdateAdnErrorText(response.transactionResponse.errors[0].errorCode, response.transactionResponse.errors[0].errorText);
                    }
                }
            }
            else
            {
                if (response.transactionResponse != null && response.transactionResponse.errors != null)
                {
                    response.transactionResponse.errors[0].errorText = UpdateAdnErrorText(response.transactionResponse.errors[0].errorCode, response.transactionResponse.errors[0].errorText);
                }
                else
                {
                    response.messages.message[0].text = UpdateAdnMessageText(response.messages.message[0].code, response.messages.message[0].text);
                }
            }
        }
    }

    private string UpdateAdnErrorText(string errorCode, string errorText)
    {
        errorText = errorText.Replace(" because of an AVS mismatch.", ".");

        switch (errorCode)
        {
            case "2":
                return "This transaction has been declined. Contact your card issuer to determine the reason."; //This transaction has been declined.;
            case "11":
                return "This transaction duplicates a transaction submitted within the last 2 minutes and will not be processed."; //A duplicate transaction has been submitted.
            case "45":
                return "This transaction has been declined. Please check your inputs and try again."; // This transaction has been declined.
            case "65":
                return "This transaction has been declined. Please re-enter the card number and card code (CVV)."; // This transaction has been declined.
            default:
                return errorText;
        }
    }

    private string UpdateAdnMessageText(string messageCode, string messageText)
    {
        switch (messageCode)
        {
            case "2":
                return "new text here";
            default:
                return messageText;
        }
    }


    #endregion helper functions

}