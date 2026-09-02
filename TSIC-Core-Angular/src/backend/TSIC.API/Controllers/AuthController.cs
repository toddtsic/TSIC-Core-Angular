using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Services;
using TSIC.Application.Validators;
using Microsoft.AspNetCore.Identity;
using TSIC.Application.Services.Auth;
using TSIC.Application.Services.Users;
using FluentValidation;
using TSIC.Infrastructure.Data.Identity;
using TSIC.API.Configuration;
using TSIC.API.Extensions;
using TSIC.API.Services.Auth;
using TSIC.API.Services.SuggestedEvents;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

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
        private readonly IAuthCredentialService _credentials;
        private readonly IRegistrationSelectionService _selection;
        private readonly IUserRepository _userRepository;
        private readonly IJobRepository _jobRepository;
        private readonly IRegistrationRepository _registrationRepository;
        private readonly IWebHostEnvironment _env;
        private readonly IEmailService _emailService;
        private readonly FrontendSettings _frontendSettings;
        private readonly ISuggestedEventsService _suggestedEventsService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            IRoleLookupService roleLookupService,
            IValidator<LoginRequest> loginValidator,
            IConfiguration configuration,
            IRefreshTokenService refreshTokenService,
            ITokenService tokenService,
            IAuthCredentialService credentials,
            IRegistrationSelectionService selection,
            IUserRepository userRepository,
            IJobRepository jobRepository,
            IRegistrationRepository registrationRepository,
            IWebHostEnvironment env,
            IEmailService emailService,
            IOptions<FrontendSettings> frontendSettings,
            ISuggestedEventsService suggestedEventsService,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _roleLookupService = roleLookupService;
            _loginValidator = loginValidator;
            _configuration = configuration;
            _refreshTokenService = refreshTokenService;
            _tokenService = tokenService;
            _credentials = credentials;
            _selection = selection;
            _userRepository = userRepository;
            _jobRepository = jobRepository;
            _registrationRepository = registrationRepository;
            _env = env;
            _emailService = emailService;
            _frontendSettings = frontendSettings.Value;
            _suggestedEventsService = suggestedEventsService;
            _logger = logger;
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

            // Password validation, including the Development-only bypass, lives in one shared
            // service. Three inline copies of it used to live in this file.
            var passwordValid = await _credentials.IsPasswordValidAsync(user, request.Password);

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
        /// Single-call login convenience endpoint.
        /// </summary>
        [HttpPost("quick-login")]
        [ProducesResponseType(typeof(QuickLoginResponse), 200)]
        [ProducesResponseType(401)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> QuickLogin([FromBody] QuickLoginRequest request)
        {
            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null)
                return Unauthorized(new { Error = "Invalid username or password" });

            var passwordValid = await _credentials.IsPasswordValidAsync(user, request.Password);

            if (!passwordValid)
                return Unauthorized(new { Error = "Invalid username or password" });

            var requiresTos = await _userRepository.RequiresTosSignatureAsync(request.Username);
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60");
            var expiresInSeconds = expirationMinutes * 60;

            var registrations = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);
            var allRegs = registrations.SelectMany(r => r.RoleRegistrations).ToList();

            RegistrationDto? targetReg = null;
            if (!string.IsNullOrEmpty(request.RegId))
            {
                targetReg = allRegs.Find(r => string.Equals(r.RegId, request.RegId, StringComparison.OrdinalIgnoreCase));
                if (targetReg == null)
                    return BadRequest(new { Error = "Selected registration is not available for this user" });
            }
            else if (allRegs.Count == 1)
            {
                targetReg = allRegs[0];
            }

            if (targetReg != null)
            {
                var registrationRole = registrations
                    .ToList()
                    .Find(r => r.RoleRegistrations.Exists(reg => string.Equals(reg.RegId, targetReg.RegId, StringComparison.OrdinalIgnoreCase)));
                var roleName = registrationRole?.RoleName ?? "User";
                var jobPath = targetReg.JobPath ?? $"/{roleName.ToLowerInvariant()}/dashboard";

                var enrichedToken = _tokenService.GenerateEnrichedJwtToken(user, targetReg.RegId, jobPath, targetReg.JobLogo, roleName);
                var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);

                return Ok(new QuickLoginResponse
                {
                    AccessToken = enrichedToken,
                    RefreshToken = refreshToken,
                    ExpiresIn = expiresInSeconds,
                    RequiresTosSignature = requiresTos
                });
            }

            var minimalToken = _tokenService.GenerateMinimalJwtToken(user);
            var minimalRefresh = _refreshTokenService.GenerateRefreshToken(user.Id);

            return Ok(new QuickLoginResponse
            {
                AccessToken = minimalToken,
                RefreshToken = minimalRefresh,
                ExpiresIn = expiresInSeconds,
                RequiresTosSignature = requiresTos,
                Registrations = registrations
            });
        }

        /// <summary>
        /// Mobile scorer login — one call, one outcome. Validates credentials AND an active
        /// Scorer registration for the requested job, then mints the same enriched token
        /// select-registration produces (sub, username, regId, jobPath, role="Scorer").
        ///
        /// Deliberately NOT quick-login: that path falls through to a minimal, roleless token
        /// and still returns 200, so a scorer appeared logged in and then 403'd on every score.
        /// Here failure is expressible — 401 bad credentials, 403 not a scorer for this event,
        /// 404 unknown event — and there is no minimal-token branch at all.
        ///
        /// Scoped to the Scorer role only, which is narrower than the CanScore policy: a
        /// director wanting to score on mobile needs an actual Scorer registration.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("scorer-login")]
        [ProducesResponseType(typeof(AuthTokenResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ScorerLogin([FromBody] ScorerLoginRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { Error = "Username and password are required." });
            }

            if (request.JobId == Guid.Empty)
            {
                return BadRequest(new { Error = "An event is required." });
            }

            var user = await _userManager.FindByNameAsync(request.Username);
            if (user == null || !await _credentials.IsPasswordValidAsync(user, request.Password))
            {
                return Unauthorized(new { Error = "Invalid username or password" });
            }

            // Job existence is resolved independently of the scorer lookup so that an unknown
            // event (404) stays distinguishable from a real event this user may not score (403).
            // One combined query cannot tell those two apart.
            var jobPath = await _jobRepository.GetJobPathAsync(request.JobId, ct);
            if (string.IsNullOrEmpty(jobPath))
            {
                return NotFound(new { Error = "Event not found." });
            }

            var registration = await _registrationRepository
                .GetScorerRegistrationForJobAsync(user.Id, request.JobId, ct);
            if (registration == null)
            {
                // Covers all three misses — no scorer row, deactivated scorer, expired event.
                // Message is shown to the scorer verbatim by the mobile client.
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { Error = "You are not a scorer for this event." });
            }

            // Sequential awaits, never Task.WhenAll — these share one scoped DbContext.
            var requiresTos = await _userRepository.RequiresTosSignatureAsync(request.Username);

            // A scorer works a tournament DAY. The legacy LoginAs endpoint this replaces issued a
            // 1-day token and no refresh token; the standard 60-minute lifetime would drop a scorer
            // to the app's 401 interceptor mid-event and make them log in again on the field.
            var expirationMinutes = int.Parse(_configuration["JwtSettings:ScorerExpirationMinutes"] ?? "1440");

            // RoleConstants.Names.ScorerName, never a literal: RequireClaim compares the claim
            // VALUE with StringComparer.Ordinal, so "scorer" would authenticate and still 403.
            var token = _tokenService.GenerateEnrichedJwtToken(
                user,
                registration.RegId,
                registration.JobPath ?? jobPath,
                registration.JobLogo,
                RoleConstants.Names.ScorerName,
                expirationMinutes);

            // Still issued: the mobile client posts it to /auth/revoke on logout. It must NOT be
            // sent to /auth/refresh — that path re-resolves through RoleLookupService, which has no
            // Scorer branch by design, and would hand back a roleless minimal token with a 200.
            var refreshToken = _refreshTokenService.GenerateRefreshToken(user.Id);

            return Ok(new AuthTokenResponse
            {
                AccessToken = token,
                RefreshToken = refreshToken,
                ExpiresIn = expirationMinutes * 60,
                RequiresTosSignature = requiresTos
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
        /// Role-selection helper: candidate Jobs the user hasn't registered in,
        /// run by Customers they have prior history with. Service auto-detects
        /// account class — Family users see player-reg-open Jobs, ClubRep users
        /// see team-reg-open Jobs. Returns [] for accounts with no relevant
        /// history or no candidate Jobs.
        /// </summary>
        [Authorize]
        [HttpGet("suggested-events")]
        [ProducesResponseType(typeof(List<SuggestedEventDto>), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> GetSuggestedEvents(CancellationToken ct)
        {
            var username = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(username))
            {
                return Unauthorized(new { Error = "Invalid token" });
            }

            var user = await _userManager.FindByIdAsync(username);
            if (user == null)
            {
                return Unauthorized(new { Error = "User not found" });
            }

            var suggestions = await _suggestedEventsService.GetSuggestedEventsForUserAsync(user.Id, ct);
            return Ok(suggestions);
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

            // sub carries the user ID, not the username — ASP.NET remaps it to NameIdentifier.
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Unauthorized(new { Error = "Invalid token" });
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized(new { Error = "Invalid user" });
            }

            // Ownership verification and token minting live in one shared service, called by
            // this endpoint and by the mobile route. Two copies of this would drift.
            var result = await _selection.SelectAsync(user, request.RegId);
            if (!result.Succeeded)
            {
                return BadRequest(new { Error = "Selected role is not available for this user" });
            }

            return Ok(new AuthTokenResponse
            {
                AccessToken = result.AccessToken!,
                ExpiresIn = result.ExpiresInSeconds
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
                targetReg = allRegs.Find(r => string.Equals(r.RegId, request.RegId, StringComparison.OrdinalIgnoreCase));
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
                    .Find(r => r.RoleRegistrations.Exists(reg => string.Equals(reg.RegId, targetReg.RegId, StringComparison.OrdinalIgnoreCase)));
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

        /// <summary>
        /// Request a password reset email. Accepts a username or an email address. An email can own
        /// several accounts here (a family login plus the parent's own logins), so one reset email is
        /// sent PER matching account, each naming its username and carrying a userId-keyed link —
        /// legacy AccountController semantics. Never FindByEmailAsync: it runs SingleOrDefault over
        /// NormalizedEmail and throws on the duplicates this database legitimately holds.
        /// Always returns 200 regardless of matches (no account enumeration).
        /// The send goes through the normal sandbox gate — real email only in Production. In
        /// Development the response carries the reset links so the flow stays testable end-to-end.
        /// </summary>
        [HttpPost("forgot-password")]
        [ProducesResponseType(200)]
        public async Task<ActionResult<ForgotPasswordResponse>> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        {
            const string Message = "If an account with that username or email exists, a password reset link has been sent.";

            if (string.IsNullOrWhiteSpace(request.UsernameOrEmail))
            {
                return Ok(new ForgotPasswordResponse { Message = Message });
            }

            var submitted = request.UsernameOrEmail.Trim();
            var accounts = await _userRepository.GetPasswordResetAccountsAsync(submitted, ct);

            // Development-only (never Staging — dev.* is client-facing, and a reset link returned to
            // an anonymous caller is an account takeover). See ForgotPasswordResponse.DevResetLinks.
            var devLinks = _env.IsDevelopment() ? new List<DevResetLink>() : null;

            foreach (var account in accounts)
            {
                // A submitted email is itself the proven reach — it matched the account or its
                // household record (which may be the only address a family login has). A submitted
                // username can only go to the address on the account.
                var recipient = submitted.Contains('@') ? submitted : account.Email;
                if (string.IsNullOrWhiteSpace(recipient))
                {
                    continue;
                }

                var user = await _userManager.FindByIdAsync(account.UserId);
                if (user == null)
                {
                    continue;
                }

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetUrl = $"{_frontendSettings.BaseUrl.TrimEnd('/')}/reset-password" +
                    $"?token={Uri.EscapeDataString(token)}&userId={Uri.EscapeDataString(account.UserId)}";

                // accounts.Count, not a flag: the email has to tell a recipient who is about to receive
                // three near-identical messages WHY, or they call support to ask which one is real.
                var forgotUrl = $"{_frontendSettings.BaseUrl.TrimEnd('/')}/forgot-password";

                // No sendInDevelopment override: SES transmits only in Production (SANDBOX rule).
                await _emailService.SendAsync(
                    BuildPasswordResetEmail(recipient, account.UserName, resetUrl, forgotUrl, accounts.Count),
                    cancellationToken: ct);

                devLinks?.Add(new DevResetLink { UserName = account.UserName, ResetUrl = resetUrl });

                if (_env.IsSandbox())
                {
                    // Staging has no email and no response links; the server log is the only way to
                    // walk the flow there.
                    _logger.LogInformation("Sandbox forgot-password: reset link for {UserName}: {ResetUrl}", account.UserName, resetUrl);
                }
            }

            return Ok(new ForgotPasswordResponse { Message = Message, DevResetLinks = devLinks ?? [] });
        }

        /// <summary>
        /// Reset password using a token from the forgot-password email.
        /// </summary>
        [HttpPost("reset-password")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { Error = "User, token, and new password are required." });
            }

            // Keyed by userId, never email — an email is one-to-many over accounts in this database.
            var user = await _userManager.FindByIdAsync(request.UserId.Trim());
            if (user == null)
            {
                return BadRequest(new { Error = "Invalid or expired reset link. Please request a new one." });
            }

            var result = await _userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(e => e.Description).ToList();

                // Surface a user-friendly message for expired/invalid tokens
                if (errors.Exists(e => e.Contains("Invalid token", StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(new { Error = "This reset link has expired or has already been used. Please request a new one." });
                }

                return BadRequest(new { Error = string.Join(" ", errors) });
            }

            return Ok(new { Message = "Your password has been reset successfully." });
        }

        /// <summary>
        /// Builds one reset email. Every sentence here exists to stop a support call:
        /// the lifespan and the single-use rule are stated up front (the error message says
        /// "expired OR already used" and the user was never warned of either), a
        /// request-a-new-link URL turns an expired link from a dead end into self-service,
        /// and <paramref name="accountCount"/> above 1 explains the several near-identical
        /// messages this address is about to receive.
        /// The lifespan text comes from TsicConstants so the prose cannot outlive the token.
        /// </summary>
        private static EmailMessageDto BuildPasswordResetEmail(
            string toEmail, string userName, string resetUrl, string forgotUrl, int accountCount)
        {
            // One email can own several accounts, and each account gets its own message — naming the
            // username is what tells the recipient which one this link resets.
            var encodedUserName = System.Net.WebUtility.HtmlEncode(userName);
            var lifespan = TsicConstants.PasswordResetTokenLifespanText;

            var multiAccountHtml = accountCount > 1
                ? $"""
                        <p style="color: #78716c; font-size: 13px; line-height: 1.5; margin: 16px 0 0; padding-top: 16px; border-top: 1px solid #e7e5e4;">
                            This email address is linked to more than one account, so you will receive a
                            separate email for each one. This message resets <strong>{encodedUserName}</strong>.
                        </p>
                """
                : "";

            var multiAccountText = accountCount > 1
                ? $"""


                    This email address is linked to more than one account, so you will receive a
                    separate email for each one. This message resets: {userName}
                    """
                : "";

            var htmlBody = $"""
                <div style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; max-width: 560px; margin: 0 auto; padding: 40px 20px;">
                    <div style="text-align: center; margin-bottom: 32px;">
                        <h2 style="color: #1c1917; margin: 0 0 8px;">Password Reset</h2>
                        <p style="color: #78716c; font-size: 14px; margin: 0;">{TsicConstants.SupportEmail}</p>
                    </div>
                    <div style="background: #ffffff; border: 1px solid #e7e5e4; border-radius: 8px; padding: 32px;">
                        <p style="color: #1c1917; font-size: 16px; line-height: 1.5; margin: 0 0 16px;">
                            We received a request to reset the password for account username:
                            <strong>{encodedUserName}</strong>. Click the button below to choose a new password.
                        </p>
                        <div style="text-align: center; margin: 24px 0;">
                            <a href="{resetUrl}" style="display: inline-block; background: #0ea5e9; color: #ffffff; text-decoration: none; padding: 12px 32px; border-radius: 6px; font-weight: 600; font-size: 16px;">
                                Reset Password
                            </a>
                        </div>
                        <p style="color: #78716c; font-size: 13px; line-height: 1.5; margin: 16px 0 0;">
                            This link expires in <strong>{lifespan}</strong> and can be used <strong>once</strong>.
                            If you didn't request a password reset, you can safely ignore this email &mdash; your password will remain unchanged.
                        </p>
                        <p style="color: #78716c; font-size: 13px; line-height: 1.5; margin: 12px 0 0;">
                            Link expired? Request a new one at
                            <a href="{forgotUrl}" style="color: #0ea5e9;">{forgotUrl}</a>
                        </p>
                {multiAccountHtml}
                    </div>
                    <p style="color: #a8a29e; font-size: 12px; text-align: center; margin-top: 24px;">
                        &copy; TEAMSPORTSINFO.COM
                    </p>
                </div>
                """;

            var textBody = $"""
                Password Reset — TEAMSPORTSINFO.COM

                We received a request to reset the password for account username: {userName}
                Visit the link below to choose a new password:

                {resetUrl}

                This link expires in {lifespan} and can be used once.
                If you didn't request this, ignore this email — your password will remain unchanged.

                Link expired? Request a new one at:
                {forgotUrl}{multiAccountText}
                """;

            return new EmailMessageDto
            {
                ToAddresses = [toEmail],
                Subject = "Reset Your Password — TEAMSPORTSINFO.COM",
                HtmlBody = htmlBody,
                TextBody = textBody
            };
        }
    }
}


