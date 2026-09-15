using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TSIC.API.Services.Auth;
using TSIC.API.Services.Invites;
using TSIC.Infrastructure.Data.Identity;
using Xunit;

namespace TSIC.Tests.Invites;

/// <summary>
/// An invite token must never authenticate a request. Invites are signed with the same key, issuer and
/// audience as login JWTs, so the bearer handler's standard validation accepts them; the refusal lives in
/// <see cref="InviteTokenService.RefuseAsLogin"/>, which Program.cs wires as OnTokenValidated.
///
/// These run a real JWT bearer pipeline in a TestServer with the same TokenValidationParameters shape as
/// Program.cs, minting tokens with the production TokenService and InviteTokenService.
/// </summary>
public class InviteTokenLoginRefusalTests
{
    private const string Secret = "invite-login-refusal-tests-secret-key-0123456789abcdef";
    private const string Issuer = "TSIC.API";
    private const string Audience = "TSIC.Client";
    private const string UserId = "user-abc";

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SecretKey"] = Secret,
            ["JwtSettings:Issuer"] = Issuer,
            ["JwtSettings:Audience"] = Audience,
            ["JwtSettings:ExpirationMinutes"] = "60"
        })
        .Build();

    private static async Task<(WebApplication app, HttpClient client)> StartApiAsync(bool withRefusal)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = Issuer,
                    ValidAudience = Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret))
                };
                if (withRefusal)
                    options.Events = new JwtBearerEvents { OnTokenValidated = InviteTokenService.RefuseAsLogin };
            });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        // Stand-in for any [Authorize] endpoint, e.g. GET api/auth/registrations, which trusts NameIdentifier.
        app.MapGet("/whoami", (ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier))
            .RequireAuthorization();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static async Task<HttpResponseMessage> CallWithBearerAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string InviteToken() =>
        new InviteTokenService(Config()).Create(Guid.NewGuid(), UserId, DateTime.Now.AddHours(24));

    private static string LoginToken() =>
        new TokenService(Config()).GenerateMinimalJwtToken(new ApplicationUser { Id = UserId, UserName = "rep@test.com" });

    [Fact(DisplayName = "Without the refusal hook, an invite token authenticates as its recipient (the hole)")]
    public async Task WithoutRefusal_InviteTokenAuthenticates()
    {
        var (app, client) = await StartApiAsync(withRefusal: false);
        await using var _ = app;

        var response = await CallWithBearerAsync(client, InviteToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "this proves the test pipeline reproduces the vulnerability the hook closes");
        (await response.Content.ReadAsStringAsync()).Should().Be(UserId);
    }

    [Fact(DisplayName = "With the refusal hook, an invite token used as a Bearer login is rejected (401)")]
    public async Task WithRefusal_InviteTokenRejected()
    {
        var (app, client) = await StartApiAsync(withRefusal: true);
        await using var _ = app;

        var response = await CallWithBearerAsync(client, InviteToken());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "With the refusal hook, a real Phase-1 login token still authenticates")]
    public async Task WithRefusal_LoginTokenStillWorks()
    {
        var (app, client) = await StartApiAsync(withRefusal: true);
        await using var _ = app;

        var response = await CallWithBearerAsync(client, LoginToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(UserId);
    }

    [Fact(DisplayName = "With the refusal hook, a real Phase-2 login token still authenticates")]
    public async Task WithRefusal_EnrichedLoginTokenStillWorks()
    {
        var (app, client) = await StartApiAsync(withRefusal: true);
        await using var _ = app;

        var token = new TokenService(Config()).GenerateEnrichedJwtToken(
            new ApplicationUser { Id = UserId, UserName = "rep@test.com" },
            regId: Guid.NewGuid().ToString(), jobPath: "some-job", jobLogo: null, roleName: "Club Rep");
        var response = await CallWithBearerAsync(client, token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(DisplayName = "The refusal does not affect the invite's real purpose: IsValidFor still accepts it")]
    public void Refusal_DoesNotBreakInviteValidation()
    {
        var svc = new InviteTokenService(Config());
        var job = Guid.NewGuid();
        var token = svc.Create(job, UserId, DateTime.Now.AddHours(24));

        svc.IsValidFor(token, job, UserId).Should().BeTrue();
    }
}
