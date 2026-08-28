using AuthorizeNet.Api.Contracts.V1;
using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Shared.Adn;

/// <inheritdoc cref="IAdnReversalService"/>
public class AdnReversalService : IAdnReversalService
{
    private readonly IAdnApiService _adnApi;
    private readonly ILogger<AdnReversalService> _logger;

    /// <summary>Captured but not yet settled — reversible only by a full void.</summary>
    private const string StatusCapturedPendingSettlement = "capturedPendingSettlement";

    /// <summary>Settled — reversible by a refund, partial or full.</summary>
    private const string StatusSettledSuccessfully = "settledSuccessfully";

    public AdnReversalService(IAdnApiService adnApi, ILogger<AdnReversalService> logger)
    {
        _adnApi = adnApi;
        _logger = logger;
    }

    public async Task<AdnReversalResult> ReverseAsync(
        AdnReversalRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.AdnTransactionId))
            return AdnReversalResult.Failed("No Authorize.Net transaction ID — cannot refund.");

        // Carried separately from here on so the branches below take a value already known to be
        // present, rather than each re-asserting it.
        var transactionId = request.AdnTransactionId;

        if (request.RequestedAmount <= 0 || request.RequestedAmount > request.OriginalPaidAmount)
            return AdnReversalResult.Failed(
                $"Refund amount must be between $0.01 and ${request.OriginalPaidAmount:F2}.");

        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(request.JobId);
        var env = _adnApi.GetADNEnvironment();

        // The gateway decides which operation applies — ask it, do not infer from local state.
        // A locally-stored "settled" flag goes stale the moment ADN runs its nightly batch.
        var (status, rawStatus, error) = LookUpStatus(env, creds, transactionId);

        if (error != null)
            return AdnReversalResult.Failed(error);

        return status switch
        {
            AdnChargeStatus.Unsettled => Void(request, transactionId, env, creds),
            AdnChargeStatus.Settled => Refund(request, transactionId, env, creds),
            _ => AdnReversalResult.Failed(
                $"Transaction status '{rawStatus}' does not support refund/void.")
        };
    }

    public async Task<AdnChargeStatus> GetChargeStatusAsync(
        Guid jobId, string? adnTransactionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(adnTransactionId))
            return AdnChargeStatus.Unknown;

        var creds = await _adnApi.GetJobAdnCredentials_FromJobId(jobId);
        var env = _adnApi.GetADNEnvironment();

        var (status, _, _) = LookUpStatus(env, creds, adnTransactionId);
        return status;
    }

    /// <summary>
    /// The single call to the gateway for "where does this charge stand". Returns the mapped
    /// status, the gateway's raw status string for messages, and a user-facing error when the
    /// lookup itself failed.
    /// </summary>
    private (AdnChargeStatus Status, string? RawStatus, string? Error) LookUpStatus(
        AuthorizeNet.Environment env, AdnCredentialsViewModel creds, string transactionId)
    {
        var txDetails = _adnApi.ADN_GetTransactionDetails(
            env, creds.AdnLoginId ?? "", creds.AdnTransactionKey ?? "", transactionId);

        if (txDetails?.messages?.resultCode != messageTypeEnum.Ok)
        {
            // Surface the gateway's own words. A generic "could not look up the transaction"
            // tells a director nothing they can act on.
            var adnError = txDetails?.messages?.message?.FirstOrDefault()?.text
                ?? "Gateway returned no error details";
            return (AdnChargeStatus.Unknown, null, adnError);
        }

        var rawStatus = txDetails.transaction?.transactionStatus;

        var status = rawStatus switch
        {
            StatusCapturedPendingSettlement => AdnChargeStatus.Unsettled,
            StatusSettledSuccessfully => AdnChargeStatus.Settled,
            _ => AdnChargeStatus.NotReversible
        };

        return (status, rawStatus, null);
    }

    /// <summary>
    /// Unsettled: void the whole charge. Authorize.Net has no partial void, so the reversed amount
    /// is the FULL original payment regardless of what was asked for.
    /// </summary>
    private AdnReversalResult Void(
        AdnReversalRequest request, string transactionId,
        AuthorizeNet.Environment env, AdnCredentialsViewModel creds)
    {
        var result = _adnApi.ADN_Void_Result(new AdnVoidRequest
        {
            Env = env,
            LoginId = creds.AdnLoginId ?? "",
            TransactionKey = creds.AdnTransactionKey ?? "",
            TransactionId = transactionId
        });

        if (!result.Success)
            return AdnReversalResult.Failed($"CC Void failed: {result.MessageForUser}");

        _logger.LogInformation(
            "ADN void succeeded: OriginalTx={OriginalTx}, VoidTx={VoidTx}, Amount={Amount}",
            transactionId, result.TransactionId, request.OriginalPaidAmount);

        return new AdnReversalResult
        {
            Success = true,
            Kind = AdnReversalKind.Void,
            Message = $"Transaction voided successfully (${request.OriginalPaidAmount:F2}).",
            TransactionId = result.TransactionId ?? "",
            ReversedAmount = request.OriginalPaidAmount
        };
    }

    /// <summary>Settled: refund the requested amount, which may be partial.</summary>
    private AdnReversalResult Refund(
        AdnReversalRequest request, string transactionId,
        AuthorizeNet.Environment env, AdnCredentialsViewModel creds)
    {
        var result = _adnApi.ADN_Refund_Result(new AdnRefundRequest
        {
            Env = env,
            LoginId = creds.AdnLoginId ?? "",
            TransactionKey = creds.AdnTransactionKey ?? "",
            CardNumberLast4 = request.CardLast4 ?? "0000",
            Expiry = request.CardExpiry ?? "XXXX",
            TransactionId = transactionId,
            Amount = request.RequestedAmount,
            InvoiceNumber = request.InvoiceNumber ?? ""
        });

        if (!result.Success)
            return AdnReversalResult.Failed($"CC Refund failed: {result.MessageForUser}");

        _logger.LogInformation(
            "ADN refund succeeded: OriginalTx={OriginalTx}, RefundTx={RefundTx}, Amount={Amount}",
            transactionId, result.TransactionId, request.RequestedAmount);

        return new AdnReversalResult
        {
            Success = true,
            Kind = AdnReversalKind.Refund,
            Message = "Refund processed successfully.",
            TransactionId = result.TransactionId ?? "",
            ReversedAmount = request.RequestedAmount
        };
    }
}
