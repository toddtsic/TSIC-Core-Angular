using Microsoft.Extensions.Configuration;
using TSIC.API.Services.Invites;

namespace TSIC.Tests.Helpers;

/// <summary>
/// Invite tokens on a fixed test key. A test minting a token for a service built by a test factory must mint it
/// here, so both sides share the key.
/// </summary>
public static class TestInviteTokens
{
    public static InviteTokenService Build() =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "invite-token-tests-secret-key-0123456789abcdef",
                ["JwtSettings:Issuer"] = "TSIC.API",
                ["JwtSettings:Audience"] = "TSIC.Client"
            })
            .Build());
}
