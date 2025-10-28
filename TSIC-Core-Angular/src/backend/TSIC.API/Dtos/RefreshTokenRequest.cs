namespace TSIC.API.Dtos
{
    /// <summary>
    /// Request to refresh or revoke a refresh token
    /// </summary>
    public record RefreshTokenRequest(
        string RefreshToken
    );
}
