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

    /// <summary>
    /// Batch ping. Returns a result for every input id (synthesized for invalid format,
    /// not-in-AMMS, or transport failure). Keyed by the caller-supplied raw id, so callers
    /// don't have to know about 12-digit padding. Honors the vendor's hard cap of 499 ids
    /// per request internally — the caller may pass any number of ids.
    /// </summary>
    Task<IReadOnlyDictionary<string, UsLaxMemberPingResult>> GetMembersAsync(
        IReadOnlyCollection<string> membershipIds, CancellationToken ct = default);
}
