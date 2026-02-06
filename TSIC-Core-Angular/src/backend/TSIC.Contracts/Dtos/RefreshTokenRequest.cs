namespace TSIC.Contracts.Dtos
{
    /// <summary>
    /// Request to refresh or revoke a refresh token
    /// </summary>
    public record RefreshTokenRequest
    {
        public required string RefreshToken { get; init; }
    }
}
