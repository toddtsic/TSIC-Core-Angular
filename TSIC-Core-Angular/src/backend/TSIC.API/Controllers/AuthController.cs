using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TSIC.API.Dtos;
using TSIC.Application.Validators;
using Microsoft.AspNetCore.Identity;
using TSIC.Application.Services;
using FluentValidation;

namespace TSIC.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IRoleLookupService _roleLookupService;
        private readonly IValidator<LoginRequest> _loginValidator;
        private readonly IConfiguration _configuration;
        private readonly IRefreshTokenService _refreshTokenService;

        public AuthController(
            UserManager<IdentityUser> userManager,
            IRoleLookupService roleLookupService,
            IValidator<LoginRequest> loginValidator,
            IConfiguration configuration,
            IRefreshTokenService refreshTokenService)
        {
            _userManager = userManager;
            _roleLookupService = roleLookupService;
            _loginValidator = loginValidator;
            _configuration = configuration;
            _refreshTokenService = refreshTokenService;
        }

        /// <summary>
        /// Phase 1: Validate username/password and return minimal JWT token (username claim only)
        /// </summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthTokenResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Validate request
            var validationResult = await _loginValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    Error = "Validation failed",
                    Errors = validationResult.Errors.Select(e => new
                    {
                        Field = e.PropertyName,
                        Message = e.ErrorMessage
                    })
                });
            }

            // Find user and validate password
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password))
            {
                return Unauthorized(new { Error = "Invalid username or password" });
            }

            // Generate Phase 1 JWT token with minimal claims (username only)
            var token = GenerateMinimalJwtToken(user);
            var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return Ok(new AuthTokenResponse(
                AccessToken: token,
                RefreshToken: refreshToken,
                ExpiresIn: expirationMinutes * 60 // Convert to seconds
            ));
        }

        /// <summary>
        /// Phase 2 - Step 1: Get available registrations for authenticated user
        /// </summary>
        [Authorize]
        [HttpGet("registrations")]
        [ProducesResponseType(typeof(LoginResponseDto), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetAvailableRegistrations()
        {
            // Extract username from Phase 1 JWT token
            var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(username))
            {
                return Unauthorized(new { Error = "Invalid token" });
            }

            // Find user by username (not ID, since Phase 1 token has username in sub claim)
            var user = await _userManager.FindByIdAsync(username);
            if (user == null)
            {
                return Unauthorized(new { Error = "User not found" });
            }

            // Query available registrations/roles for this user
            var registrations = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);

            return Ok(new LoginResponseDto(user.Id, registrations));
        }

        /// <summary>
        /// Phase 2 - Step 2: User selects a registration and receives enriched JWT token
        /// </summary>
        [Authorize]
        [HttpPost("select-registration")]
        [ProducesResponseType(typeof(AuthTokenResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> SelectRegistration([FromBody] RoleSelectionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RegId))
            {
                return BadRequest(new { Error = "RegId is required" });
            }

            // Extract username from Phase 1 JWT token
            var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(username))
            {
                return Unauthorized(new { Error = "Invalid token" });
            }

            // Validate user exists
            var user = await _userManager.FindByIdAsync(username);
            if (user == null)
            {
                return Unauthorized(new { Error = "Invalid user" });
            }

            // Verify the user has access to this registration
            var registrations = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);
            var selectedReg = registrations
                .SelectMany(r => r.RoleRegistrations)
                .FirstOrDefault(reg => reg.RegId == request.RegId);

            if (selectedReg == null)
            {
                return BadRequest(new { Error = "Selected role is not available for this user" });
            }

            // Determine the role name and jobPath from the registration
            var registrationRole = registrations
                .FirstOrDefault(r => r.RoleRegistrations.Any(reg => reg.RegId == request.RegId));

            var roleName = registrationRole?.RoleName ?? "User";
            var jobPath = selectedReg.JobPath ?? $"/{roleName.ToLowerInvariant()}/dashboard";

            // Generate enriched Phase 2 JWT token with regId and jobPath claims
            var token = GenerateEnrichedJwtToken(user, request.RegId, jobPath);
            var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return Ok(new AuthTokenResponse(
                AccessToken: token,
                RefreshToken: refreshToken,
                ExpiresIn: expirationMinutes * 60 // Convert to seconds
            ));
        }

        /// <summary>
        /// Generate Phase 1 JWT token with minimal claims (username only)
        /// </summary>
        private string GenerateMinimalJwtToken(IdentityUser user)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
            var issuer = jwtSettings["Issuer"] ?? "TSIC.API";
            var audience = jwtSettings["Audience"] ?? "TSIC.Client";
            var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id), // Store user ID in sub claim
                new Claim("username", user.UserName ?? ""),      // Also store username for convenience
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Generate Phase 2 JWT token with enriched claims (username, regId, jobPath)
        /// </summary>
        private string GenerateEnrichedJwtToken(IdentityUser user, string regId, string jobPath)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
            var issuer = jwtSettings["Issuer"] ?? "TSIC.API";
            var audience = jwtSettings["Audience"] ?? "TSIC.Client";
            var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim("username", user.UserName ?? ""),
                new Claim("regId", regId),
                new Claim("jobPath", jobPath),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Refresh access token using a valid refresh token
        /// </summary>
        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthTokenResponse), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return Unauthorized(new { Error = "Refresh token is required" });
            }

            // Validate refresh token
            var userId = _refreshTokenService.ValidateRefreshToken(request.RefreshToken);
            if (userId == null)
            {
                return Unauthorized(new { Error = "Invalid or expired refresh token" });
            }

            // Get user
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized(new { Error = "User not found" });
            }

            // Revoke old refresh token
            _refreshTokenService.RevokeRefreshToken(request.RefreshToken);

            // Generate new access token and refresh token
            // Note: We generate a minimal token here. Client should call select-registration again if needed
            var newAccessToken = GenerateMinimalJwtToken(user);
            var newRefreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return Ok(new AuthTokenResponse(
                AccessToken: newAccessToken,
                RefreshToken: newRefreshToken,
                ExpiresIn: expirationMinutes * 60
            ));
        }

        /// <summary>
        /// Revoke a refresh token (used for logout)
        /// </summary>
        [HttpPost("revoke")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public IActionResult RevokeToken([FromBody] RefreshTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return BadRequest(new { Error = "Refresh token is required" });
            }

            _refreshTokenService.RevokeRefreshToken(request.RefreshToken);
            return Ok(new { Message = "Token revoked successfully" });
        }
    }
}
