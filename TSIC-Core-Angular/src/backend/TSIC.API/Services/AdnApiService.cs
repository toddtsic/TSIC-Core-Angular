using AuthorizeNet.Api.Contracts.V1;
using AuthorizeNet.Api.Controllers;
using AuthorizeNet.Api.Controllers.Bases;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using TSIC.API.Dtos;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

// Authorize.Net transport records moved to Dtos/Payments/AuthorizeNet/AuthorizeNetRequests.cs

public class AdnApiService : IAdnApiService
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<AdnApiService> _logger;
    private readonly IConfiguration _config;

    public AdnApiService(IHostEnvironment env, ILogger<AdnApiService> logger, IConfiguration config)
    {
        _env = env;
        _logger = logger;
        _config = config;
    }

    public AuthorizeNet.Environment GetADNEnvironment(bool bProdOnly = false)
    {
        // Explicit environment gating: Development always sandbox unless caller forces production.
        if (_env.IsDevelopment() && !bProdOnly)
        {
            return AuthorizeNet.Environment.SANDBOX;
        }
        return AuthorizeNet.Environment.PRODUCTION;
    }

    public async Task<AdnCredentialsViewModel> GetJobAdnCredentials_FromJobId(SqlDbContext _context, Guid jobId, bool bProdOnly = false)
    {
        var isSandbox = _env.IsDevelopment() && !bProdOnly;
        if (isSandbox)
        {
            // Prefer configuration / env vars over hard-coded fallbacks.
            var login = _config["AuthorizeNet:SandboxLoginId"] ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_LOGINID");
            var key = _config["AuthorizeNet:SandboxTransactionKey"] ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_TRANSACTIONKEY");
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("Sandbox Authorize.Net credentials missing; using legacy hard-coded fallback. Configure AuthorizeNet:SandboxLoginId and SandboxTransactionKey.");
                login = login ?? "4dE5m4WR9ey";
                key = key ?? "6zmzD35C47kv45Sn";
            }
            return new AdnCredentialsViewModel { AdnLoginId = login, AdnTransactionKey = key };
        }

        // Production: fetch from associated customer; fail fast if missing.
        var creds = await (
            from j in _context.Jobs
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            where j.JobId == jobId
            select new AdnCredentialsViewModel
            {
                AdnLoginId = c.AdnLoginId,
                AdnTransactionKey = c.AdnTransactionKey
            }
        ).AsNoTracking().SingleOrDefaultAsync();

        if (creds == null || string.IsNullOrWhiteSpace(creds.AdnLoginId) || string.IsNullOrWhiteSpace(creds.AdnTransactionKey))
        {
            _logger.LogError("Production Authorize.Net credentials missing for Job {JobId}.", jobId);
            throw new InvalidOperationException($"Authorize.Net production credentials not configured for Job {jobId}.");
        }
        return creds;
    }

    public async Task<AdnCredentialsViewModel> GetJobAdnCredentials_FromCustomerId(SqlDbContext _context, Guid customerId, bool bProdOnly = false)
    {
        var isSandbox = _env.IsDevelopment() && !bProdOnly;
        if (isSandbox)
        {
            var login = _config["AuthorizeNet:SandboxLoginId"] ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_LOGINID");
            var key = _config["AuthorizeNet:SandboxTransactionKey"] ?? Environment.GetEnvironmentVariable("ADN_SANDBOX_TRANSACTIONKEY");
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("Sandbox Authorize.Net credentials missing (customer scope). Using legacy fallback.");
                login = login ?? "4dE5m4WR9ey";
                key = key ?? "6zmzD35C47kv45Sn";
            }
            return new AdnCredentialsViewModel { AdnLoginId = login, AdnTransactionKey = key };
        }

        var creds = await _context.Customers
            .AsNoTracking()
            .Where(c => c.CustomerId == customerId)
            .Select(c => new AdnCredentialsViewModel
            {
                AdnLoginId = c.AdnLoginId,
                AdnTransactionKey = c.AdnTransactionKey
            })
            .SingleOrDefaultAsync();

        if (creds == null || string.IsNullOrWhiteSpace(creds.AdnLoginId) || string.IsNullOrWhiteSpace(creds.AdnTransactionKey))
        {
            _logger.LogError("Production Authorize.Net credentials missing for Customer {CustomerId}.", customerId);
            throw new InvalidOperationException($"Authorize.Net production credentials not configured for Customer {customerId}.");
        }
        return creds;
    }

    // Legacy authorize method removed; request-based wrapper used instead.

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

    // Legacy charge method removed; request-based wrapper used instead.

    // Legacy refund method removed; request-based wrapper used instead.

    // Legacy void method removed; request-based wrapper used instead.


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

    // Legacy ARB create method removed; request-based wrapper used instead.

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

    // Legacy update subscription method removed; request-based wrapper used instead.

    private static AdnArbCreateResult ParseArbCreateResponse(ARBCreateSubscriptionResponse resp, string cardNumber)
    {
        if (resp == null)
        {
            return new AdnArbCreateResult(false, null, null, null, null, "NULLRESP", "No response from gateway.", "No response from gateway.", null);
        }
        var success = resp.messages?.resultCode == messageTypeEnum.Ok && !string.IsNullOrWhiteSpace(resp.subscriptionId);
        string userMsg;
        string gwMsg;
        string? gwCode = null;
        if (success)
        {
            userMsg = "Subscription created.";
            gwMsg = resp.messages?.message?.FirstOrDefault()?.text ?? "OK";
            gwCode = resp.messages?.message?.FirstOrDefault()?.code;
        }
        else
        {
            if (resp.messages?.message != null && resp.messages.message.Length > 0)
            {
                gwCode = resp.messages.message[0].code;
                gwMsg = resp.messages.message[0].text;
            }
            else
            {
                gwMsg = "Subscription create failed.";
            }
            userMsg = gwMsg;
        }
        string? last4 = !string.IsNullOrWhiteSpace(cardNumber) && cardNumber.Length >= 4 ? cardNumber[^4..] : null;
        return new AdnArbCreateResult(success, resp.subscriptionId, null, null, null, gwCode, userMsg, gwMsg, last4);
    }

    public ARBUpdateSubscriptionResponse ADN_UpdateSubscription(AdnArbUpdateRequest request)
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = request.Env;
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = request.LoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = request.TransactionKey
        };

        var subscription = new ARBSubscriptionType
        {
            amount = request.ChargePerOccurrence
        };

        if (!string.IsNullOrWhiteSpace(request.CardNumber) && !string.IsNullOrWhiteSpace(request.ExpirationDate))
        {
            var cardNum = request.Env == AuthorizeNet.Environment.SANDBOX ? MapSandboxTestCard(request.CardNumber) : request.CardNumber;
            var normalizedExpiry = NormalizeExpiry(request.ExpirationDate);
            var creditCard = new creditCardType { cardNumber = cardNum, expirationDate = normalizedExpiry, cardCode = request.CardCode };
            subscription.payment = new paymentType { Item = creditCard };
        }

        var apiReq = new ARBUpdateSubscriptionRequest { subscriptionId = request.SubscriptionId, subscription = subscription };
        var controller = new ARBUpdateSubscriptionController(apiReq);
        controller.Execute();
        return controller.GetApiResponse();
    }

    public ARBGetSubscriptionListResponse ARBGetSubscriptionListRequest(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        ARBGetSubscriptionListSearchTypeEnum searchType)
    {
        try
        {
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = env;
            ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
            {
                name = adnLoginId,
                ItemElementName = ItemChoiceType.transactionKey,
                Item = adnTransactionKey
            };
            var request = new ARBGetSubscriptionListRequest { searchType = searchType };
            var controller = new ARBGetSubscriptionListController(request);
            controller.Execute();
            return controller.GetApiResponse();
        }
        catch
        {
            return null;
        }
    }

    #region helper functions
    // Common internal helpers to reduce duplication and align with API reference:
    // https://developer.authorize.net/api/reference/index.html#payment-transactions-charge-a-credit-card
    private static void SetupMerchantAuth(string loginId, string transactionKey)
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType
        {
            name = loginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = transactionKey
        };
    }

    private static creditCardType BuildCreditCard(AuthorizeNet.Environment env, string cardNumber, string cardCode, string expiry)
    {
        var num = env == AuthorizeNet.Environment.SANDBOX ? MapSandboxTestCard(cardNumber) : cardNumber;
        var normalizedExpiry = NormalizeExpiry(expiry);
        return new creditCardType { cardNumber = num, expirationDate = normalizedExpiry, cardCode = cardCode };
    }

    private sealed record ExecTxnArgs(
        AuthorizeNet.Environment Env,
        string LoginId,
        string TransactionKey,
        transactionTypeEnum TxnType,
        decimal? Amount = null,
        creditCardType? CreditCard = null,
        customerAddressType? BillTo = null,
        orderType? OrderInfo = null,
        string? RefTransId = null,
        Action<transactionRequestType>? Configure = null);

    private static createTransactionResponse ExecuteTransaction(ExecTxnArgs a)
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = a.Env;
        SetupMerchantAuth(a.LoginId, a.TransactionKey);

        var txnReq = new transactionRequestType
        {
            transactionType = a.TxnType.ToString(),
            payment = a.CreditCard != null ? new paymentType { Item = a.CreditCard } : null,
            billTo = a.BillTo,
            order = a.OrderInfo,
            refTransId = a.RefTransId
        };
        if (a.Amount.HasValue)
        {
            txnReq.amount = a.Amount.Value;
        }

        // Apply standard duplicate window (2 min) for charge/authorize to prevent accidental resubmits.
        if (a.TxnType == transactionTypeEnum.authCaptureTransaction || a.TxnType == transactionTypeEnum.authOnlyTransaction)
        {
            txnReq.transactionSettings = new settingType[]
            {
                new settingType { settingName = "duplicateWindow", settingValue = "120" }
            };
        }

        a.Configure?.Invoke(txnReq);

        var apiReq = new createTransactionRequest { transactionRequest = txnReq };
        var controller = new createTransactionController(apiReq);
        controller.Execute();
        var resp = controller.GetApiResponse();
        if (resp == null)
        {
            return new createTransactionResponse
            {
                messages = new messagesType
                {
                    resultCode = messageTypeEnum.Error,
                    message = new[] { new messagesTypeMessage { code = "NULLRESP", text = "Authorize.Net returned no transaction response." } }
                }
            };
        }
        UpdateCreateTransactionApiResponse(resp);
        return resp;
    }
    // New public request-based wrappers
    public createTransactionResponse ADN_Authorize(AdnAuthorizeRequest request)
    {
        var cc = BuildCreditCard(request.Env, request.CardNumber, request.CardCode, request.Expiry);
        var addr = new customerAddressType { firstName = request.FirstName, lastName = request.LastName, address = request.Address, zip = request.Zip };
        return ExecuteTransaction(new ExecTxnArgs(request.Env, request.LoginId, request.TransactionKey, transactionTypeEnum.authOnlyTransaction, request.Amount, cc, addr));
    }

    public createTransactionResponse ADN_Charge(AdnChargeRequest request)
    {
        var cc = BuildCreditCard(request.Env, request.CardNumber, request.CardCode, request.Expiry);
        var addr = new customerAddressType { firstName = request.FirstName.Trim(), lastName = request.LastName.Trim(), address = request.Address.Trim(), zip = request.Zip };
        var order = new orderType { invoiceNumber = request.InvoiceNumber, description = request.Description.Trim() };
        return ExecuteTransaction(new ExecTxnArgs(request.Env, request.LoginId, request.TransactionKey, transactionTypeEnum.authCaptureTransaction, request.Amount, cc, addr, order, null, txn =>
        {
            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                txn.customer = new customerDataType { email = request.Email };
            }
        }));
    }

    public createTransactionResponse ADN_Refund(AdnRefundRequest request)
    {
        // Refund: gateway requires last4 + matching prior transaction id & amount.
        var normalizedExpiry = NormalizeExpiry(request.Expiry); // Keep direct normalization (no remap of last4)
        var creditCard = new creditCardType { cardNumber = request.CardNumberLast4, expirationDate = normalizedExpiry };
        var order = new orderType { invoiceNumber = request.InvoiceNumber, description = "Credit Card Credit" };
        return ExecuteTransaction(new ExecTxnArgs(request.Env, request.LoginId, request.TransactionKey, transactionTypeEnum.refundTransaction, request.Amount, creditCard, null, order, request.TransactionId));
    }

    public createTransactionResponse ADN_Void(AdnVoidRequest request)
    {
        return ExecuteTransaction(new ExecTxnArgs(request.Env, request.LoginId, request.TransactionKey, transactionTypeEnum.voidTransaction, null, null, null, null, request.TransactionId));
    }

    public ARBCreateSubscriptionResponse ADN_ARB_CreateMonthlySubscription(AdnArbCreateRequest request)
    {
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.RunEnvironment = request.Env;
        ApiOperationBase<ANetApiRequest, ANetApiResponse>.MerchantAuthentication = new merchantAuthenticationType()
        {
            name = request.LoginId,
            ItemElementName = ItemChoiceType.transactionKey,
            Item = request.TransactionKey,
        };
        var interval = new paymentScheduleTypeInterval { length = request.IntervalLength, unit = ARBSubscriptionUnitEnum.months };
        var schedule = new paymentScheduleType { interval = interval, startDate = request.StartDate ?? DateTime.Now.AddDays(1), totalOccurrences = request.BillingOccurrences, trialOccurrences = 0 };
        var cardNumber = request.CardNumber;
        if (request.Env == AuthorizeNet.Environment.SANDBOX)
            cardNumber = MapSandboxTestCard(cardNumber);
        var normalizedExpiry = NormalizeExpiry(request.Expiry);
        // ARB Create does not require/accept CVV in many cases; omit cardCode to avoid schema/validation issues.
        // Include cardCode again (CCV) for card-not-present security when supplied.
        var creditCard = new creditCardType { cardNumber = cardNumber, expirationDate = normalizedExpiry, cardCode = string.IsNullOrWhiteSpace(request.CardCode) ? null : request.CardCode };
        var payment = new paymentType { Item = creditCard };
        var customerInfo = new customerType { email = request.Email };
        var addressInfo = new nameAndAddressType { firstName = request.FirstName, lastName = request.LastName, address = request.Address, zip = request.Zip };
        // Authorize.Net limits invoiceNumber to 20 chars. Trim and sanitize to be safe.
        var safeInvoice = (request.InvoiceNumber ?? string.Empty).Trim();
        if (safeInvoice.Length > 20) safeInvoice = safeInvoice.Substring(0, 20);
        var orderInfo = new orderType { invoiceNumber = safeInvoice, description = request.Description };
        var subscriptionType = new ARBSubscriptionType { amount = request.PerIntervalCharge, trialAmount = 0.00m, paymentSchedule = schedule, billTo = addressInfo, payment = payment, order = orderInfo, customer = customerInfo };
        var apiReq = new ARBCreateSubscriptionRequest { subscription = subscriptionType };
        try
        {
            var controller = new ARBCreateSubscriptionController(apiReq);
            controller.Execute();
            var response = controller.GetApiResponse();
            if (response == null)
            {
                // Attempt to extract a structured error from the controller if available
                var err = controller.GetErrorResponse();
                if (err != null)
                {
                    return new ARBCreateSubscriptionResponse
                    {
                        messages = new messagesType
                        {
                            resultCode = err.messages?.resultCode ?? messageTypeEnum.Error,
                            message = err.messages?.message != null && err.messages.message.Length > 0
                                ? err.messages.message
                                : new[] { new messagesTypeMessage { code = "NULLRESP", text = "Authorize.Net returned no subscription response." } }
                        }
                    };
                }
                return new ARBCreateSubscriptionResponse
                {
                    messages = new messagesType
                    {
                        resultCode = messageTypeEnum.Error,
                        message = new[] { new messagesTypeMessage { code = "NULLRESP", text = "Authorize.Net returned no subscription response." } }
                    }
                };
            }
            return response;
        }
        catch (Exception ex)
        {
            return new ARBCreateSubscriptionResponse
            {
                messages = new messagesType
                {
                    resultCode = messageTypeEnum.Error,
                    message = new[] { new messagesTypeMessage { code = "EX", text = "Authorize.Net ARB create failed: " + ex.Message } }
                }
            };
        }
    }
    public AdnArbCreateResult ADN_ARB_CreateMonthlySubscription_Result(AdnArbCreateRequest request)
    {
        var raw = ADN_ARB_CreateMonthlySubscription(request);
        return ParseArbCreateResponse(raw, request.CardNumber);
    }
    private static string MapSandboxTestCard(string cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber)) return cardNumber;
        // Only remap our local generic test Visa to Authorize.Net's accepted sandbox Visa.
        if (cardNumber == "4242424242424242")
            return "4111111111111111"; // Authorize.Net sandbox Visa
        return cardNumber;
    }

    private static string NormalizeExpiry(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        raw = raw.Trim();
        // Already YYYY-MM
        if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"^\d{4}-\d{2}$")) return raw;
        // MMYY (4 digits)
        if (System.Text.RegularExpressions.Regex.IsMatch(raw, @"^\d{4}$"))
        {
            var mm = raw.Substring(0, 2);
            var yy = raw.Substring(2, 2);
            return $"20{yy}-{mm}";
        }
        // MM/YY or MM - YY variants
        var match = System.Text.RegularExpressions.Regex.Match(raw, @"^(?<mm>\d{2})\s*[/\-]\s*(?<yy>\d{2})$");
        if (match.Success)
        {
            var mm = match.Groups["mm"].Value;
            var yy = match.Groups["yy"].Value;
            return $"20{yy}-{mm}";
        }
        return raw; // Fallback; gateway will validate
    }
    private static void UpdateCreateTransactionApiResponse(createTransactionResponse response)
    {
        // validate response
        if (response == null) return;

        var txn = response.transactionResponse;
        bool ok = response.messages.resultCode == messageTypeEnum.Ok;
        if (ok)
        {
            if (txn?.messages == null && txn?.errors != null)
            {
                txn.errors[0].errorText = UpdateAdnErrorText(txn.errors[0].errorCode, txn.errors[0].errorText);
            }
            return;
        }

        if (txn?.errors != null)
        {
            txn.errors[0].errorText = UpdateAdnErrorText(txn.errors[0].errorCode, txn.errors[0].errorText);
        }
        else if (response.messages?.message != null && response.messages.message.Length > 0)
        {
            response.messages.message[0].text = UpdateAdnMessageText(response.messages.message[0].code, response.messages.message[0].text);
        }
    }

    private static string UpdateAdnErrorText(string errorCode, string errorText)
    {
        errorText = errorText.Replace(" because of an AVS mismatch.", ".");

        switch (errorCode)
        {
            case "2":
                return "This transaction has been declined. Contact your card issuer to determine the reason.";
            case "11":
                return "This transaction duplicates a transaction submitted within the last 2 minutes and will not be processed."; //A duplicate transaction has been submitted.
            case "45":
                return "This transaction has been declined. Please check your inputs and try again."; // This transaction has been declined.
            case "65":
                return "This transaction has been declined. Please re-enter the card number and card code (CVV)."; // This transaction has been declined.
            case "6":
                return "The transaction was declined by the issuer.";
            case "7":
                return "Card number is invalid or does not pass Luhn check.";
            case "8":
                return "Expiration date is invalid.";
            case "9":
                return "Routing number is invalid.";
            case "16":
                return "Transaction declined. Card code verification failed.";
            case "E00027":
                return "The transaction was unsuccessful. Please verify details and try again.";
            default:
                return errorText;
        }
    }

    private static string UpdateAdnMessageText(string messageCode, string messageText)
    {
        switch (messageCode)
        {
            case "2":
                return "new text here";
            case "E00003":
                return "Validation failed on the gateway.";
            default:
                return messageText;
        }
    }


    #endregion helper functions

}