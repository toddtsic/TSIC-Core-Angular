namespace TSIC.API.Services.Shared.UsLax;

public interface IUsLaxService
{
    Task<string?> GetMemberRawJsonAsync(string membershipId, CancellationToken ct = default);

    /// <summary>
    /// Ping the USA Lacrosse MemberPing endpoint and return a typed result. Returns null
    /// on network/parse failure (distinct from an API-level error, which surfaces as a
    /// StatusCode != 200 on the result).
    /// </summary>
    Task<UsLaxMemberPingResult?> GetMemberAsync(string membershipId, CancellationToken ct = default);
}
