using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Application.Validators;
using Microsoft.AspNetCore.Identity;
using TSIC.Application.Services.Auth;
using TSIC.Application.Services.Users;
using FluentValidation;
using TSIC.Infrastructure.Data.Identity;
using TSIC.API.Services.Auth;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IRoleLookupService _roleLookupService;
        private readonly IValidator<LoginRequest> _loginValidator;
        private readonly IConfiguration _configuration;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ITokenService _tokenService;
        private readonly IUserRepository _userRepository;
        private readonly IWebHostEnvironment _env;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            IRoleLookupService roleLookupService,
            IValidator<LoginRequest> loginValidator,
            IConfiguration configuration,
            IRefreshTokenService refreshTokenService,
            ITokenService tokenService,
            IUserRepository userRepository,
            IWebHostEnvironment env)
        {
            _userManager = userManager;
            _roleLookupService = roleLookupService;
            _loginValidator = loginValidator;
            _configuration = configuration;
            _refreshTokenService = refreshTokenService;
            _tokenService = tokenService;
            _userRepository = userRepository;
            _env = env;
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
            if (user == null)
            {
                return Unauthorized(new { Error = "Invalid username or password" });
            }

            // Dev-mode password bypass (Development environment only)
            bool passwordValid = false;
            if (_env.IsDevelopment())
            {
                var allowBypass = _configuration.GetValue<bool>("DevMode:AllowPasswordBypass");
                var bypassPassword = _configuration["DevMode:BypassPassword"];

                if (allowBypass && !string.IsNullOrEmpty(bypassPassword) && request.Password == bypassPassword)
                {
                    // Bypass password check in dev mode with special password
                    passwordValid = true;
                }
                else
                {
                    // Normal password validation
                    passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
                }
            }
            else
            {
                // Production mode - always validate actual password
                passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
            }

            if (!passwordValid)
            {
                return Unauthorized(new { Error = "Invalid username or password" });
            }

            // Check Terms of Service status via repository
            bool requiresTosSignature = await _userRepository.RequiresTosSignatureAsync(request.Username);

            // Generate Phase 1 JWT token with minimal claims (username only)
            var token = _tokenService.GenerateMinimalJwtToken(user);
            var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return Ok(new AuthTokenResponse
            {
                AccessToken = token,
                RefreshToken = refreshToken,
                ExpiresIn = expirationMinutes * 60, // Convert to seconds
                RequiresTosSignature = requiresTosSignature
            });
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

            return Ok(new LoginResponseDto { UserId = user.Id, Registrations = registrations });
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
            // Analyzer suggestion: prefer collection Find/Exists when underlying concrete lists are used
            var selectedReg = registrations
                .SelectMany(r => r.RoleRegistrations)
                .ToList()
                .Find(reg => reg.RegId == request.RegId);

            if (selectedReg == null)
            {
                return BadRequest(new { Error = "Selected role is not available for this user" });
            }

            // Determine the role name and jobPath from the registration
            var registrationRole = registrations
                .ToList()
                .Find(r => r.RoleRegistrations.Exists(reg => reg.RegId == request.RegId));

            var roleName = registrationRole?.RoleName ?? "User";
            var jobPath = selectedReg.JobPath ?? $"/{roleName.ToLowerInvariant()}/dashboard";
            var jobLogo = selectedReg.JobLogo; // Get the job logo from selected registration

            // Generate enriched Phase 2 JWT token with regId, jobPath, jobLogo, and role claims
            var token = _tokenService.GenerateEnrichedJwtToken(user, request.RegId, jobPath, jobLogo, roleName);
            var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return Ok(new AuthTokenResponse
            {
                AccessToken = token,
                RefreshToken = refreshToken,
                ExpiresIn = expirationMinutes * 60
            });
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

            // Try to get user's registrations to regenerate enriched token
            var registrations = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);
            var allRegs = registrations.SelectMany(r => r.RoleRegistrations).ToList();

            // If caller provided a RegId, preserve that session context.
            // Otherwise fall back to most recent registration (legacy behavior).
            RegistrationDto? targetReg = null;
            if (!string.IsNullOrEmpty(request.RegId))
            {
                targetReg = allRegs.Find(r => r.RegId == request.RegId);
            }
            targetReg ??= allRegs
                .OrderByDescending(reg => reg.RegId)
                .ToList()
                .Find(_ => true);

            string newAccessToken;
            if (targetReg != null && !string.IsNullOrEmpty(targetReg.JobPath))
            {
                // Regenerate enriched token preserving the original job/role context
                var registrationRole = registrations
                    .ToList()
                    .Find(r => r.RoleRegistrations.Exists(reg => reg.RegId == targetReg.RegId));
                var roleName = registrationRole?.RoleName ?? "User";

                newAccessToken = _tokenService.GenerateEnrichedJwtToken(user, targetReg.RegId, targetReg.JobPath, targetReg.JobLogo, roleName);
            }
            else
            {
                // No registration found - fall back to minimal token
                newAccessToken = _tokenService.GenerateMinimalJwtToken(user);
            }

            var newRefreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");

            return Ok(new AuthTokenResponse
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                ExpiresIn = expirationMinutes * 60
            });
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

        /// <summary>
        /// Accept Terms of Service for authenticated user
        /// </summary>
        [Authorize]
        [HttpPost("accept-tos")]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> AcceptTos()
        {
            // Extract userId from JWT token
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { Error = "Invalid token" });
            }

            await _userRepository.UpdateTosAcceptanceByUserIdAsync(userId);
            return Ok(new { Message = "Terms of Service accepted successfully" });
        }
    }
}


