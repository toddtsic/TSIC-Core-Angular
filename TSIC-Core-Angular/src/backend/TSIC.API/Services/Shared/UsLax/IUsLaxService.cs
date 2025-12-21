namespace TSIC.API.Services.Shared.UsLax;

public interface IUsLaxService
{
    Task<string?> GetMemberRawJsonAsync(string membershipId, CancellationToken ct = default);
}
