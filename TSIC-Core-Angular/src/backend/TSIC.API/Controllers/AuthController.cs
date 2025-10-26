using Microsoft.AspNetCore.Mvc;
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

        public AuthController(
            UserManager<IdentityUser> userManager,
            IRoleLookupService roleLookupService,
            IValidator<LoginRequest> loginValidator,
            IConfiguration configuration)
        {
            _userManager = userManager;
            _roleLookupService = roleLookupService;
            _loginValidator = loginValidator;
            _configuration = configuration;
        }

        /// <summary>
        /// Phase 1: Validate username/password and return available roles
        /// </summary>
        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponseDto), 200)]
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

            // Query available registrations/roles for this user
            var registrations = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);

            return Ok(new LoginResponseDto(registrations));
        }

        /// <summary>
        /// Phase 2: User selects a role/registration and receives JWT token
        /// </summary>
        [HttpPost("select-role")]
        [ProducesResponseType(typeof(AuthTokenResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> SelectRole([FromBody] RoleSelectionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.RegId))
            {
                return BadRequest(new { Error = "UserId and RegId are required" });
            }

            // Validate user exists
            var user = await _userManager.FindByIdAsync(request.UserId);
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

            // Determine the role name from the registration
            var roleName = registrations
                .FirstOrDefault(r => r.RoleRegistrations.Any(reg => reg.RegId == request.RegId))
                ?.RoleName ?? "User";

            // Generate JWT token
            var token = GenerateJwtToken(user, request.RegId, roleName, selectedReg.DisplayText);

            var response = new AuthTokenResponse(
                AccessToken: token,
                ExpiresIn: int.Parse(_configuration["JwtSettings:ExpirationMinutes"] ?? "60") * 60,
                User: new AuthenticatedUserDto(
                    UserId: user.Id,
                    Username: user.UserName ?? "",
                    FirstName: "", // TODO: Get from user profile
                    LastName: "",  // TODO: Get from user profile
                    SelectedRole: roleName,
                    JobPath: selectedReg.DisplayText // Using DisplayText as jobPath for now
                )
            );

            return Ok(response);
        }

        private string GenerateJwtToken(IdentityUser user, string regId, string roleName, string jobPath)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
            var issuer = jwtSettings["Issuer"] ?? "TSIC.API";
            var audience = jwtSettings["Audience"] ?? "TSIC.Client";
            var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, roleName),
                new Claim("regId", regId),
                new Claim("jobPath", jobPath)
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
    }
}
