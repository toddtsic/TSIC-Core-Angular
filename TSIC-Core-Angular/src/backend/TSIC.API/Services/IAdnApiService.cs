using AuthorizeNet.Api.Contracts.V1;
using TSIC.API.Dtos;
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
        string adnLoginId,
        string adnTransactionKey,
        string transactionId
    );

    createTransactionResponse ADN_Authorize(AdnAuthorizeRequest request);
    createTransactionResponse ADN_Charge(AdnChargeRequest request);

    createTransactionResponse ADN_ChargeCustomerProfile(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string customerPaymentProfileId,
        decimal ccAmount,
        string invoiceNumber,
        string description
    );

    createTransactionResponse ADN_Refund(AdnRefundRequest request);
    createTransactionResponse ADN_Void(AdnVoidRequest request);

    ARBCreateSubscriptionResponse ADN_ARB_CreateMonthlySubscription(AdnArbCreateRequest request);
    AdnArbCreateResult ADN_ARB_CreateMonthlySubscription_Result(AdnArbCreateRequest request);

    getSettledBatchListResponse GetSettleBatchList_FromDateRange(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        DateTime firstSettlementDate,
        DateTime lastSettlementDate,
        bool includeStatistics
    );

    getTransactionListResponse GetTransactionList_ByBatchId(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string batchId
    );

    ARBGetSubscriptionResponse GetSubscriptionDetails(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string subscriptionId
    );

    ARBGetSubscriptionStatusResponse GetSubscriptionStatus(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string subscriptionId
    );

    ARBCancelSubscriptionResponse ADN_CancelSubscription(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        string subscriptionId
    );

    ARBUpdateSubscriptionResponse ADN_UpdateSubscription(AdnArbUpdateRequest request);

    ARBGetSubscriptionListResponse ARBGetSubscriptionListRequest(
        AuthorizeNet.Environment env,
        string adnLoginId,
        string adnTransactionKey,
        ARBGetSubscriptionListSearchTypeEnum searchType
    );
}
