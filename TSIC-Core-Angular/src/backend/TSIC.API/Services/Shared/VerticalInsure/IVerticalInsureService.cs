using TSIC.Contracts.Dtos; // Unified DTOs (includes CreditCardInfo via PaymentDtos, VerticalInsurePurchaseResult via InsurancePurchaseDtos)
using TSIC.Contracts.Dtos.VerticalInsure; // VICreditCardDto and related

namespace TSIC.API.Services.Shared.VerticalInsure;

public interface IVerticalInsureService
{
    /// <summary>
    /// Builds a VerticalInsure player offer for the specified job/family context if the job is configured
    /// to offer RegSaver insurance. Returns Available=false and Error set when unavailable.
    /// </summary>
    Task<PreSubmitInsuranceDto> BuildOfferAsync(Guid jobId, string familyUserId);

    /// <summary>
    /// Independently purchases RegSaver / VerticalInsure policies for the supplied registration IDs using
    /// previously quoted products (identified by quoteIds). This operation is completely decoupled from
    /// registration fee payment – no Authorize.Net logic is invoked here.
    ///
    /// Implementation note: Current stub synthesizes policy numbers (POL-{8 chars}) until real HTTP
    /// integration details (endpoint, payload, auth) are provided. On success, returns mapping of
    /// RegistrationId -> PolicyNumber and persists to the database. On failure, no persistence occurs.
    /// </summary>
    Task<VerticalInsurePurchaseResult> PurchasePoliciesAsync(
        Guid jobId,
        string familyUserId,
        IReadOnlyCollection<Guid> registrationIds,
        IReadOnlyCollection<string> quoteIds,
        string? token,
        CreditCardInfo? card,
        CancellationToken ct = default);

    /// <summary>
    /// Builds a VerticalInsure team registration insurance offer for the specified job/club rep context
    /// if the job is configured to offer team RegSaver insurance. Returns Available=false and Error set when unavailable.
    /// </summary>
    Task<PreSubmitTeamInsuranceDto> BuildTeamOfferAsync(Guid jobId, Guid clubRepRegId);

    /// <summary>
    /// Independently purchases VerticalInsure team registration protection policies for the supplied team IDs
    /// using previously quoted products (identified by quoteIds). This operation is completely decoupled from
    /// team registration fee payment – no Authorize.Net logic is invoked here.
    ///
    /// On success, returns mapping of TeamId -> PolicyNumber and persists to Teams.ViPolicyId.
    /// On failure, no persistence occurs.
    /// </summary>
    Task<VerticalInsureTeamPurchaseResult> PurchaseTeamPoliciesAsync(
        Guid jobId,
        Guid clubRepRegId,
        IReadOnlyCollection<Guid> teamIds,
        IReadOnlyCollection<string> quoteIds,
        string? token,
        CreditCardInfo? card,
        CancellationToken ct = default);
}
