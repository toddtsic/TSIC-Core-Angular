using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TSIC.API.Dtos;
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

        public AuthController(
            UserManager<IdentityUser> userManager,
            IRoleLookupService roleLookupService,
            IValidator<LoginRequest> loginValidator)
        {
            _userManager = userManager;
            _roleLookupService = roleLookupService;
            _loginValidator = loginValidator;
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponseDto), 200)]
        [ProducesResponseType(401)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Use FluentValidation instead of manual validation
            var validationResult = await _loginValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    Errors = validationResult.Errors.Select(e => new
                    {
                        Field = e.PropertyName,
                        Message = e.ErrorMessage
                    })
                });
            }

            var user = await _userManager.FindByNameAsync(request.Username);
            if (user != null && await _userManager.CheckPasswordAsync(user, request.Password))
            {
                // Query registrations for this user using IRoleLookupService
                var regs = await _roleLookupService.GetRegistrationsForUserAsync(user.Id);
                return Ok(new LoginResponseDto(regs));
            }
            return Unauthorized();
        }

        [HttpPost("token")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(401)]
        public IActionResult Token([FromBody] TokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.RegId))
            {
                return BadRequest("Username and regId are required.");
            }
            // In real app, validate user and regId, then issue JWT
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, request.Username ?? string.Empty),
                new Claim("regId", request.RegId ?? string.Empty)
            };
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("YourSuperSecretKey123!"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer: "TSIC",
                audience: "TSICUsers",
                claims: claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: creds
            );
            return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
        }
    }

    // DTOs moved to TSIC.API.Dtos namespace as records
}
