namespace TSIC.API.Services.Validation;

public interface IUsLaxService
{
    Task<string?> GetMemberRawJsonAsync(string membershipId, CancellationToken ct = default);
}
