namespace TSIC.API.Dtos
{
    /// <summary>
    /// JWT token response for both Phase 1 (minimal token) and Phase 2 (enriched token)
    /// </summary>
    public record AuthTokenResponse(
        string AccessToken,
        string? RefreshToken = null,
        int? ExpiresIn = null
    );
}
