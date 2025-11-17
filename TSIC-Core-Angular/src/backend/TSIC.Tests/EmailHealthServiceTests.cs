using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Email;
using Xunit;

namespace TSIC.Tests;

public class EmailHealthServiceTests
{
    private static EmailSettings Settings(bool enabled = true, bool sandbox = false, string? region = "us-east-1") => new()
    {
        EmailingEnabled = enabled,
        SandboxMode = sandbox,
        AwsRegion = region
    };

    private static EmailHealthService Build(Moq.Mock<IAmazonSimpleEmailService> sesMock, EmailSettings settings)
    {
        var options = Options.Create(settings);
        var env = new Mock<Microsoft.Extensions.Hosting.IHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        var logger = new Mock<ILogger<EmailHealthService>>();
        return new EmailHealthService(sesMock.Object, options, env.Object, logger.Object);
    }

    [Fact]
    public async Task CheckAsync_EmailDisabled_ReturnsWarning_AndSkipsSesCall()
    {
        var ses = new Mock<IAmazonSimpleEmailService>(MockBehavior.Strict); // ensure no calls
        var svc = Build(ses, Settings(enabled: false));

        var status = await svc.CheckAsync();

        status.EmailingEnabled.Should().BeFalse();
        status.Warning.Should().NotBeNullOrWhiteSpace();
        status.SesReachable.Should().BeFalse(); // not attempted
        ses.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CheckAsync_HealthyQuota_NoWarning()
    {
        var ses = new Mock<IAmazonSimpleEmailService>(MockBehavior.Strict);
        ses.Setup(s => s.GetSendQuotaAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GetSendQuotaResponse
        {
            Max24HourSend = 5000,
            SentLast24Hours = 100,
            MaxSendRate = 14.0
        });
        var svc = Build(ses, Settings(enabled: true, sandbox: false));

        var status = await svc.CheckAsync();

        status.EmailingEnabled.Should().BeTrue();
        status.SesReachable.Should().BeTrue();
        status.Max24HourSend.Should().Be(5000);
        status.SentLast24Hours.Should().Be(100);
        status.MaxSendRate.Should().Be(14.0);
        status.Warning.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_LowQuota_NotSandbox_SetsWarning()
    {
        var ses = new Mock<IAmazonSimpleEmailService>(MockBehavior.Strict);
        ses.Setup(s => s.GetSendQuotaAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GetSendQuotaResponse
        {
            Max24HourSend = 400, // < 1000 triggers warning when not sandbox
            SentLast24Hours = 10,
            MaxSendRate = 2.0
        });
        var svc = Build(ses, Settings(enabled: true, sandbox: false));

        var status = await svc.CheckAsync();

        status.SesReachable.Should().BeTrue();
        status.Warning.Should().NotBeNull();
        status.Warning!.Should().Contain("Low SES 24h send quota");
    }

    [Fact]
    public async Task CheckAsync_LowQuota_Sandbox_DoesNotWarn()
    {
        var ses = new Mock<IAmazonSimpleEmailService>(MockBehavior.Strict);
        ses.Setup(s => s.GetSendQuotaAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GetSendQuotaResponse
        {
            Max24HourSend = 400,
            SentLast24Hours = 10,
            MaxSendRate = 2.0
        });
        var svc = Build(ses, Settings(enabled: true, sandbox: true));

        var status = await svc.CheckAsync();
        status.Warning.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_SesThrows_SetsReachableFalse_AndWarning()
    {
        var ses = new Mock<IAmazonSimpleEmailService>(MockBehavior.Strict);
        ses.Setup(s => s.GetSendQuotaAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("network"));
        var svc = Build(ses, Settings(enabled: true));

        var status = await svc.CheckAsync();
        status.SesReachable.Should().BeFalse();
        status.Warning.Should().NotBeNull();
        status.Warning!.Should().Contain("Failed to reach SES API");
    }
}
