using FluentAssertions;
using Microsoft.Extensions.Configuration;
using TSIC.API.Services.Invites;
using TSIC.Tests.Helpers;
using Xunit;

namespace TSIC.Tests.Invites;

/// <summary>
/// The invite token is the whole security boundary for token-gated registration: it must accept
/// exactly the invited user for the invited job within the window, and reject everything else.
/// These exercise the rejection paths that make "invitation-exclusivity" real — tamper, expiry,
/// wrong user, wrong job — plus the happy path.
/// </summary>
public class InviteTokenServiceTests
{
    private static InviteTokenService Build() => TestInviteTokens.Build();

    private static readonly Guid Job = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherJob = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string User = "user-abc";
    private const string OtherUser = "user-xyz";

    [Fact(DisplayName = "Valid token for the same user + job within the window is accepted")]
    public void Valid_SameUserSameJob_Accepted()
    {
        var svc = Build();
        var token = svc.Create(InvitePurpose.Registration, Job, User, DateTime.Now.AddHours(24));
        svc.IsValidFor(InvitePurpose.Registration, token, Job, User).Should().BeTrue();
    }

    [Fact(DisplayName = "Token minted for a different user is rejected (forward-proof)")]
    public void WrongUser_Rejected()
    {
        var svc = Build();
        var token = svc.Create(InvitePurpose.Registration, Job, User, DateTime.Now.AddHours(24));
        svc.IsValidFor(InvitePurpose.Registration, token, Job, OtherUser).Should().BeFalse();
    }

    [Fact(DisplayName = "Token minted for a different job is rejected")]
    public void WrongJob_Rejected()
    {
        var svc = Build();
        var token = svc.Create(InvitePurpose.Registration, Job, User, DateTime.Now.AddHours(24));
        svc.IsValidFor(InvitePurpose.Registration, token, OtherJob, User).Should().BeFalse();
    }

    [Fact(DisplayName = "Expired token is rejected (past the clock-skew allowance)")]
    public void Expired_Rejected()
    {
        var svc = Build();
        var token = svc.Create(InvitePurpose.Registration, Job, User, DateTime.Now.AddMinutes(-5));
        svc.IsValidFor(InvitePurpose.Registration, token, Job, User).Should().BeFalse();
    }

    [Fact(DisplayName = "Tampered signature is rejected")]
    public void Tampered_Rejected()
    {
        var svc = Build();
        var token = svc.Create(InvitePurpose.Registration, Job, User, DateTime.Now.AddHours(24));
        // Flip the last character of the signature segment.
        var last = token[^1] == 'A' ? 'B' : 'A';
        var tampered = token[..^1] + last;
        svc.IsValidFor(InvitePurpose.Registration, tampered, Job, User).Should().BeFalse();
    }

    [Fact(DisplayName = "A token signed with a different key is rejected")]
    public void ForeignKey_Rejected()
    {
        var attacker = new InviteTokenService(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "a-totally-different-secret-key-9876543210zyxwvu",
                ["JwtSettings:Issuer"] = "TSIC.API",
                ["JwtSettings:Audience"] = "TSIC.Client"
            })
            .Build());
        var forged = attacker.Create(InvitePurpose.Registration, Job, User, DateTime.Now.AddHours(24));

        Build().IsValidFor(InvitePurpose.Registration, forged, Job, User).Should().BeFalse();
    }

    [Theory(DisplayName = "A token minted for one purpose is rejected for the other (a registration invite can't preview a schedule, and back)")]
    [InlineData(InvitePurpose.Registration, InvitePurpose.SchedulePreview)]
    [InlineData(InvitePurpose.SchedulePreview, InvitePurpose.Registration)]
    public void WrongPurpose_Rejected(InvitePurpose minted, InvitePurpose checkedFor)
    {
        var svc = Build();
        var token = svc.Create(minted, Job, User, DateTime.Now.AddHours(24));
        svc.IsValidFor(checkedFor, token, Job, User).Should().BeFalse();
        svc.IsValidFor(minted, token, Job, User).Should().BeTrue("the control: the same token passes for its own purpose");
    }

    [Theory(DisplayName = "Missing/blank token or user is rejected")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    public void MissingOrGarbage_Rejected(string? token)
    {
        Build().IsValidFor(InvitePurpose.Registration, token, Job, User).Should().BeFalse();
    }
}
