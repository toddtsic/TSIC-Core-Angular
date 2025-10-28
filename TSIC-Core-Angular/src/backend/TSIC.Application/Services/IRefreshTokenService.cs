namespace TSIC.Application.Services
{
    /// <summary>
    /// Service for managing refresh tokens using in-memory cache
    /// </summary>
    public interface IRefreshTokenService
    {
        /// <summary>
        /// Generate a new cryptographically secure refresh token for a user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>The generated refresh token string</returns>
        string GenerateRefreshToken(string userId);

        /// <summary>
        /// Validate a refresh token and return the associated user ID if valid
        /// </summary>
        /// <param name="refreshToken">The refresh token to validate</param>
        /// <returns>User ID if valid, null if invalid or expired</returns>
        string? ValidateRefreshToken(string refreshToken);

        /// <summary>
        /// Revoke a refresh token (remove from cache)
        /// </summary>
        /// <param name="refreshToken">The refresh token to revoke</param>
        void RevokeRefreshToken(string refreshToken);

        /// <summary>
        /// Revoke all refresh tokens for a specific user
        /// </summary>
        /// <param name="userId">The user ID whose tokens should be revoked</param>
        void RevokeAllUserTokens(string userId);
    }
}
