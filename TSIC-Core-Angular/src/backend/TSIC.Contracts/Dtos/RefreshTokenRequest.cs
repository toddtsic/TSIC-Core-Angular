namespace TSIC.Contracts.Dtos
{
    /// <summary>
    /// Request to refresh or revoke a refresh token.
    /// RegId preserves the user's current registration context across refreshes.
    /// </summary>
    public record RefreshTokenRequest
    {
        public required string RefreshToken { get; init; }

        /// <summary>
        /// The registration ID from the current session.
        /// When provided, the refreshed token preserves the same job/role context
        /// instead of defaulting to the most recent registration.
        /// </summary>
        public string? RegId { get; init; }
    }
}
