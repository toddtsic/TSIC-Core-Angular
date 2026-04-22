namespace TSIC.API.Services.Shared.UsLax;

/// <summary>
/// Typed shape of the USA Lacrosse MemberPing response. Shape mirrors the raw API JSON
/// so callers can stop parsing strings. Only the fields we actually use are modeled —
/// the USALax API returns more fields that we don't care about.
/// </summary>
public sealed class UsLaxMemberPingResult
{
    /// <summary>200 on success; 500 on API errors; 0 on network/parse failure.</summary>
    public int StatusCode { get; init; }

    /// <summary>API error message when StatusCode != 200.</summary>
    public string? ErrorMessage { get; init; }

    public UsLaxMemberPingOutput? Output { get; init; }
}

public sealed class UsLaxMemberPingOutput
{
    public string? MembershipId { get; init; }
    public string? MemStatus { get; init; }
    public string? ExpDate { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? AgeVerified { get; init; }
    public IReadOnlyList<string>? Involvement { get; init; }
}
