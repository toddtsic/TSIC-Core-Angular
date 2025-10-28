using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using TSIC.Application.Services;

namespace TSIC.Infrastructure.Services
{
    /// <summary>
    /// In-memory cache-based refresh token service
    /// </summary>
    public class RefreshTokenService : IRefreshTokenService
    {
        private readonly IMemoryCache _cache;
        private readonly int _refreshTokenExpirationDays;
        private const string CACHE_PREFIX = "refresh_token_";
        private const string USER_TOKENS_PREFIX = "user_tokens_";

        public RefreshTokenService(IMemoryCache cache, IConfiguration configuration)
        {
            _cache = cache;
            _refreshTokenExpirationDays = int.Parse(
                configuration["JwtSettings:RefreshTokenExpirationDays"] ?? "7"
            );
        }

        /// <summary>
        /// Generate a cryptographically secure refresh token
        /// </summary>
        public string GenerateRefreshToken(string userId)
        {
            // Generate cryptographically random token
            var randomBytes = new byte[64];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }
            var refreshToken = Convert.ToBase64String(randomBytes);

            // Store in cache with sliding expiration
            var cacheKey = CACHE_PREFIX + refreshToken;
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(_refreshTokenExpirationDays),
                SlidingExpiration = TimeSpan.FromDays(_refreshTokenExpirationDays / 2)
            };

            _cache.Set(cacheKey, userId, cacheOptions);

            // Track this token for the user (for revoke all functionality)
            var userTokensKey = USER_TOKENS_PREFIX + userId;
            var userTokens = _cache.GetOrCreate(userTokensKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(_refreshTokenExpirationDays + 1);
                return new HashSet<string>();
            }) ?? new HashSet<string>();

            userTokens.Add(refreshToken);
            _cache.Set(userTokensKey, userTokens);

            return refreshToken;
        }

        /// <summary>
        /// Validate refresh token and return user ID if valid
        /// </summary>
        public string? ValidateRefreshToken(string refreshToken)
        {
            var cacheKey = CACHE_PREFIX + refreshToken;
            if (_cache.TryGetValue(cacheKey, out string? userId))
            {
                return userId;
            }
            return null;
        }

        /// <summary>
        /// Revoke a specific refresh token
        /// </summary>
        public void RevokeRefreshToken(string refreshToken)
        {
            var cacheKey = CACHE_PREFIX + refreshToken;
            
            // Get user ID before removing
            if (_cache.TryGetValue(cacheKey, out string? userId))
            {
                _cache.Remove(cacheKey);

                // Remove from user's token list
                var userTokensKey = USER_TOKENS_PREFIX + userId;
                if (_cache.TryGetValue(userTokensKey, out HashSet<string>? userTokens))
                {
                    userTokens?.Remove(refreshToken);
                    if (userTokens != null && userTokens.Count > 0)
                    {
                        _cache.Set(userTokensKey, userTokens);
                    }
                    else
                    {
                        _cache.Remove(userTokensKey);
                    }
                }
            }
        }

        /// <summary>
        /// Revoke all refresh tokens for a user (useful for logout from all devices)
        /// </summary>
        public void RevokeAllUserTokens(string userId)
        {
            var userTokensKey = USER_TOKENS_PREFIX + userId;
            
            if (_cache.TryGetValue(userTokensKey, out HashSet<string>? userTokens) && userTokens != null)
            {
                // Remove each token from cache
                foreach (var token in userTokens)
                {
                    var cacheKey = CACHE_PREFIX + token;
                    _cache.Remove(cacheKey);
                }

                // Remove the user's token list
                _cache.Remove(userTokensKey);
            }
        }
    }
}
