namespace TSIC.Contracts.Dtos
{
    /// <summary>
    /// JWT token response for both Phase 1 (minimal token) and Phase 2 (enriched token)
    /// </summary>
    public record AuthTokenResponse
    {
        public required string AccessToken { get; init; }
        public string? RefreshToken { get; init; }
        public int? ExpiresIn { get; init; }
        public bool RequiresTosSignature { get; init; }
    }
}
