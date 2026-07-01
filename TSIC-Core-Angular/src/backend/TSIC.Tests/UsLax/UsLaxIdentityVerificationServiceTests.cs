using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using TSIC.API.Services.Shared.UsLax;
using TSIC.Contracts.Services;

namespace TSIC.Tests.UsLax;

/// <summary>
/// Identity-verification OTP service for coach USA Lacrosse memberships.
///
/// The contract under test: BeginAsync HARD-gates on an active Coach membership and emails a
/// code only to the on-file address; Confirm enforces match + attempt-cap; Consume is
/// single-use and bound to the membership number. The vendor ping (IUsLaxService) and the
/// mailer (IEmailService) are faked so tests never hit the network.
/// </summary>
public class UsLaxIdentityVerificationServiceTests
{
    private const string GoodNumber = "123456";          // 6 digits → padded to 12 internally
    private const string OnFileEmail = "coach@example.com";

    private readonly Mock<IUsLaxService> _usLax = new();
    private readonly Mock<IEmailService> _email = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

    private UsLaxIdentityVerificationService CreateSut() =>
        new(_usLax.Object, _email.Object, _cache);

    private static UsLaxMemberPingResult ActiveCoach(string? email = OnFileEmail) => new()
    {
        StatusCode = 200,
        Output = new UsLaxMemberPingOutput
        {
            MemStatus = "Active",
            Involvement = new[] { "Player", "Coach" },
            Email = email,
            ExpDate = "2027-08-31",
        }
    };

    private void SetupPing(UsLaxMemberPingResult? result) =>
        _usLax.Setup(s => s.GetMemberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(result);

    private void SetupEmailSucceeds() =>
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);

    // ── BeginAsync: hard membership gate ──────────────────────────

    [Fact]
    public async Task Begin_ActiveCoach_SendsCode_AndMasksEmail()
    {
        SetupPing(ActiveCoach());
        SetupEmailSucceeds();

        var result = await CreateSut().BeginAsync(GoodNumber);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.Sent);
        result.VerificationId.Should().NotBeNullOrWhiteSpace();
        result.MaskedEmail.Should().Be("c•••@example.com");
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("12345")]      // too short
    [InlineData("1234567890123")] // too long
    [InlineData("12ab56")]     // non-digit
    public async Task Begin_MalformedNumber_RejectsWithoutPinging(string bad)
    {
        var result = await CreateSut().BeginAsync(bad);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.MembershipInvalid);
        _usLax.Verify(s => s.GetMemberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_NonCoachInvolvement_IsRejected()
    {
        SetupPing(new UsLaxMemberPingResult
        {
            StatusCode = 200,
            Output = new UsLaxMemberPingOutput
            {
                MemStatus = "Active",
                Involvement = new[] { "Player" },
                Email = OnFileEmail,
            }
        });

        var result = await CreateSut().BeginAsync(GoodNumber);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.MembershipInvalid);
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_ExpiredMembership_IsRejected()
    {
        SetupPing(new UsLaxMemberPingResult
        {
            StatusCode = 200,
            Output = new UsLaxMemberPingOutput
            {
                MemStatus = "Expired",
                Involvement = new[] { "Coach" },
                Email = OnFileEmail,
            }
        });

        var result = await CreateSut().BeginAsync(GoodNumber);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.MembershipInvalid);
    }

    [Fact]
    public async Task Begin_ActiveCoach_NoOnFileEmail_ReturnsEmailUnavailable()
    {
        SetupPing(ActiveCoach(email: null));

        var result = await CreateSut().BeginAsync(GoodNumber);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.EmailUnavailable);
        _email.Verify(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Begin_VendorNetworkFailure_ReturnsServiceUnavailable()
    {
        SetupPing(null); // GetMemberAsync returns null on network/parse failure

        var result = await CreateSut().BeginAsync(GoodNumber);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.ServiceUnavailable);
    }

    [Fact]
    public async Task Begin_EmailSendFails_ReturnsServiceUnavailable_AndCachesNothing()
    {
        SetupPing(ActiveCoach());
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);

        var result = await CreateSut().BeginAsync(GoodNumber);

        result.Status.Should().Be(UsLaxVerifyBeginStatus.ServiceUnavailable);
        result.VerificationId.Should().BeNull();
    }

    [Fact]
    public async Task Begin_ExceedsSendCap_IsThrottled_WithoutPingingVendorAgain()
    {
        SetupPing(ActiveCoach());
        SetupEmailSucceeds();
        var sut = CreateSut();

        // 5 sends (MaxSendsPerWindow) for the same number all succeed.
        for (var i = 0; i < 5; i++)
            (await sut.BeginAsync(GoodNumber)).Status.Should().Be(UsLaxVerifyBeginStatus.Sent);

        // 6th is throttled — surfaced as ServiceUnavailable so the UI shows the message.
        var sixth = await sut.BeginAsync(GoodNumber);
        sixth.Status.Should().Be(UsLaxVerifyBeginStatus.ServiceUnavailable);

        // The throttle short-circuits BEFORE the vendor ping (exactly 5 pings, not 6).
        _usLax.Verify(s => s.GetMemberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
    }

    [Fact]
    public async Task Begin_FailedSends_DoNotCountAgainstCap()
    {
        // Vendor down on every call → ServiceUnavailable, but nothing was actually emailed,
        // so the send counter stays at 0 and a coach isn't locked out by transient failures.
        SetupPing(null);
        var sut = CreateSut();

        for (var i = 0; i < 8; i++)
            (await sut.BeginAsync(GoodNumber)).Status.Should().Be(UsLaxVerifyBeginStatus.ServiceUnavailable);

        // Vendor recovers → the very next call still sends (not throttled).
        SetupPing(ActiveCoach());
        SetupEmailSucceeds();
        (await sut.BeginAsync(GoodNumber)).Status.Should().Be(UsLaxVerifyBeginStatus.Sent);
    }

    // ── Confirm + Consume: the OTP round-trip ─────────────────────

    private static string ExtractCode(EmailMessageDto msg)
    {
        // The 6-digit code is the only run of exactly 6 digits in the text body.
        var match = System.Text.RegularExpressions.Regex.Match(msg.TextBody ?? "", @"\b(\d{6})\b");
        match.Success.Should().BeTrue("the verification email must contain a 6-digit code");
        return match.Groups[1].Value;
    }

    private async Task<(UsLaxIdentityVerificationService sut, string verificationId, string code)> BeginAndCapture()
    {
        EmailMessageDto? captured = null;
        _email.Setup(e => e.SendAsync(It.IsAny<EmailMessageDto>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .Callback<EmailMessageDto, bool, CancellationToken>((m, _, _) => captured = m)
              .ReturnsAsync(true);
        SetupPing(ActiveCoach());

        var sut = CreateSut();
        var begin = await sut.BeginAsync(GoodNumber);
        begin.Status.Should().Be(UsLaxVerifyBeginStatus.Sent);
        captured.Should().NotBeNull();
        return (sut, begin.VerificationId!, ExtractCode(captured!));
    }

    [Fact]
    public async Task Confirm_CorrectCode_Succeeds_AndReturnsExpDate()
    {
        var (sut, vid, code) = await BeginAndCapture();

        var result = sut.Confirm(vid, code);

        result.Success.Should().BeTrue();
        result.ExpDate.Should().Be("2027-08-31");
    }

    [Fact]
    public async Task Confirm_WrongCode_Fails()
    {
        var (sut, vid, code) = await BeginAndCapture();
        var wrong = code == "000000" ? "000001" : "000000";

        sut.Confirm(vid, wrong).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_UnknownVerificationId_Fails()
    {
        var sut = CreateSut();
        sut.Confirm("does-not-exist", "123456").Success.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_ExceedsMaxAttempts_InvalidatesCode()
    {
        var (sut, vid, code) = await BeginAndCapture();
        var wrong = code == "000000" ? "000001" : "000000";

        // 5 wrong attempts (MaxAttempts) consume the allowance.
        for (var i = 0; i < 5; i++) sut.Confirm(vid, wrong).Success.Should().BeFalse();

        // 6th attempt — even with the CORRECT code — is locked out.
        sut.Confirm(vid, code).Success.Should().BeFalse();
    }

    [Fact]
    public async Task Consume_AfterConfirm_ReturnsRecord_AndIsSingleUse()
    {
        var (sut, vid, code) = await BeginAndCapture();
        sut.Confirm(vid, code).Success.Should().BeTrue();

        var first = sut.Consume(vid, GoodNumber);
        first.Should().NotBeNull();
        first!.ExpDate.Should().Be(new DateTime(2027, 8, 31));

        // Single-use: a second consume returns null.
        sut.Consume(vid, GoodNumber).Should().BeNull();
    }

    [Fact]
    public async Task Consume_WithoutConfirm_ReturnsNull()
    {
        var (sut, vid, _) = await BeginAndCapture();

        sut.Consume(vid, GoodNumber).Should().BeNull();
    }

    [Fact]
    public async Task Consume_WithMismatchedNumber_ReturnsNull()
    {
        var (sut, vid, code) = await BeginAndCapture();
        sut.Confirm(vid, code).Success.Should().BeTrue();

        // A verification proven for one membership can't be replayed against another number.
        sut.Consume(vid, "999999").Should().BeNull();
    }
}
