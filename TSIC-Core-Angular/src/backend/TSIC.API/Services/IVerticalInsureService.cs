using TSIC.API.Dtos;

namespace TSIC.API.Services;

public interface IVerticalInsureService
{
    /// <summary>
    /// Builds a VerticalInsure player offer for the specified job/family context if the job is configured
    /// to offer RegSaver insurance. Returns Available=false and Error set when unavailable.
    /// </summary>
    Task<PreSubmitInsuranceDto> BuildOfferAsync(Guid jobId, string familyUserId);
}
