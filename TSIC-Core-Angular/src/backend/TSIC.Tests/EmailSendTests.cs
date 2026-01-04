using Amazon.SimpleEmail;
using Amazon.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Shared.Email;
using TSIC.Contracts.Services;
using Xunit;

namespace TSIC.Tests;

public class EmailSendTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task SendTestEmail_ToToddTsic_RealSes()
    {
        // Uses real AWS credentials from environment variables (AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION)
        var emailSettings = new EmailSettings
        {
            EmailingEnabled = true,
            SupportEmail = "noreply@teamsportsinfo.com",
            AwsRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-west-2",
            SandboxMode = false
        };

        // Create real SES client using environment credentials
        var accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        var secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");

        IAmazonSimpleEmailService ses;
        if (!string.IsNullOrEmpty(accessKey) && !string.IsNullOrEmpty(secretKey))
        {
            var credentials = new BasicAWSCredentials(accessKey, secretKey);
            ses = new AmazonSimpleEmailServiceClient(credentials, Amazon.RegionEndpoint.GetBySystemName(emailSettings.AwsRegion));
        }
        else
        {
            ses = new AmazonSimpleEmailServiceClient(Amazon.RegionEndpoint.GetBySystemName(emailSettings.AwsRegion));
        }

        var loggerMock = new Mock<ILogger<EmailService>>();
        var envMock = new Mock<IHostEnvironment>();
        envMock.Setup(e => e.EnvironmentName).Returns("Production");

        var optionsMock = Options.Create(emailSettings);
        var emailService = new EmailService(optionsMock, ses, loggerMock.Object, envMock.Object);

        var message = new EmailMessageDto
        {
            ToAddresses = new List<string> { "toddtsic@gmail.com" },
            Subject = "Test",
            HtmlBody = "<p>From Test</p>",
            TextBody = "From Test",
            FromAddress = "support@teamsportsinfo.com",  // Verified SES sender
            FromName = "TSIC Test"
        };

        var result = await emailService.SendAsync(message, sendInDevelopment: true);

        // Log captured to see what happened
        loggerMock.Invocations.ToList().ForEach(i => Console.WriteLine($"{i.Method.Name}: {string.Join(", ", i.Arguments)}"));

        Assert.True(result, "Email send should succeed");
    }
}
