using AuthorizeNet.Api.Contracts.V1;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Configuration;
using TSIC.API.Services.Shared.Adn;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.Tests.Adn;

/// <summary>
/// Tests for ADN_VerifyCardWithPennyAuth — the auth($0.01) → void composition that
/// fronts ARB sub create / ARB card update so a declined card surfaces synchronously
/// instead of waiting for the next-morning batch.
/// </summary>
public class AdnApiServiceTests
{
    [Fact]
    public void VerifyCardWithPennyAuth_AuthDeclined_ReturnsFailureWithGatewayError()
    {
        var sut = new TestableAdnApiService
        {
            AuthFn = _ => ErrorAuthResponse("16", "Transaction declined. Card code verification failed.")
        };

        var result = sut.ADN_VerifyCardWithPennyAuth(BuildRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Transaction declined. Card code verification failed.");
        result.AuthTransactionId.Should().BeNull();
        sut.VoidCalls.Should().BeEmpty();
    }

    [Fact]
    public void VerifyCardWithPennyAuth_AuthOkButNoTransactionId_ReturnsFailure()
    {
        var sut = new TestableAdnApiService
        {
            AuthFn = _ => new createTransactionResponse
            {
                messages = new messagesType { resultCode = messageTypeEnum.Ok },
                transactionResponse = new transactionResponse { transId = "" }
            }
        };

        var result = sut.ADN_VerifyCardWithPennyAuth(BuildRequest());

        result.Success.Should().BeFalse();
        sut.VoidCalls.Should().BeEmpty();
    }

    [Fact]
    public void VerifyCardWithPennyAuth_AuthOkVoidFails_ReturnsFailureCarryingAuthTxId()
    {
        var sut = new TestableAdnApiService
        {
            AuthFn = _ => OkAuthResponse(txId: "AUTH-99"),
            VoidFn = _ => ErrorVoidResponse("E00027", "The transaction was unsuccessful.")
        };

        var result = sut.ADN_VerifyCardWithPennyAuth(BuildRequest());

        result.Success.Should().BeFalse();
        result.AuthTransactionId.Should().Be("AUTH-99");
        result.ErrorMessage.Should().Contain("Card validated but void failed");
        result.ErrorMessage.Should().Contain("The transaction was unsuccessful.");
    }

    [Fact]
    public void VerifyCardWithPennyAuth_AuthOkVoidOk_ReturnsSuccess()
    {
        var sut = new TestableAdnApiService
        {
            AuthFn = _ => OkAuthResponse(txId: "AUTH-42"),
            VoidFn = _ => OkVoidResponse()
        };

        var result = sut.ADN_VerifyCardWithPennyAuth(BuildRequest());

        result.Success.Should().BeTrue();
        result.AuthTransactionId.Should().Be("AUTH-42");
        result.ErrorMessage.Should().BeNull();
        sut.VoidCalls[0].TransactionId.Should().Be("AUTH-42");
    }

    [Fact]
    public void VerifyCardWithPennyAuth_OverridesCallerAmountToPenny()
    {
        // Caller might pass any amount (e.g. an existing AdnAuthorizeRequest reused
        // from another flow); the helper must always force $0.01 so a real auth
        // can never accidentally be placed.
        var sut = new TestableAdnApiService
        {
            AuthFn = _ => OkAuthResponse(txId: "AUTH-1"),
            VoidFn = _ => OkVoidResponse()
        };

        var caller = BuildRequest() with { Amount = 999.99m };
        sut.ADN_VerifyCardWithPennyAuth(caller);

        sut.AuthCalls[0].Amount.Should().Be(0.01m);
    }

    [Fact]
    public void VerifyCardWithPennyAuth_NullAuthResponse_ReturnsFailure()
    {
        var sut = new TestableAdnApiService
        {
            AuthFn = _ => null!
        };

        var result = sut.ADN_VerifyCardWithPennyAuth(BuildRequest());

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Card validation failed.");
        sut.VoidCalls.Should().BeEmpty();
    }

    // ── Builders ──────────────────────────────────────────────────────

    private static AdnAuthorizeRequest BuildRequest() => new()
    {
        Env = AuthorizeNet.Environment.SANDBOX,
        LoginId = "login",
        TransactionKey = "key",
        CardNumber = "4111111111111111",
        CardCode = "123",
        Expiry = "2030-12",
        FirstName = "Jane",
        LastName = "Doe",
        Address = "1 Main St",
        Zip = "12345",
        Amount = 0.01m
    };

    private static createTransactionResponse OkAuthResponse(string txId) => new()
    {
        messages = new messagesType
        {
            resultCode = messageTypeEnum.Ok,
            message = [new messagesTypeMessage { code = "I00001", text = "Successful." }]
        },
        transactionResponse = new transactionResponse
        {
            transId = txId,
            messages = [new transactionResponseMessage { code = "1", description = "Approved." }]
        }
    };

    private static createTransactionResponse ErrorAuthResponse(string code, string text) => new()
    {
        messages = new messagesType
        {
            resultCode = messageTypeEnum.Error,
            message = [new messagesTypeMessage { code = code, text = text }]
        },
        transactionResponse = new transactionResponse
        {
            errors = [new transactionResponseError { errorCode = code, errorText = text }]
        }
    };

    private static createTransactionResponse OkVoidResponse() => new()
    {
        messages = new messagesType { resultCode = messageTypeEnum.Ok }
    };

    private static createTransactionResponse ErrorVoidResponse(string code, string text) => new()
    {
        messages = new messagesType
        {
            resultCode = messageTypeEnum.Error,
            message = [new messagesTypeMessage { code = code, text = text }]
        }
    };

    // ── Test double ───────────────────────────────────────────────────

    private sealed class TestableAdnApiService : AdnApiService
    {
        public Func<AdnAuthorizeRequest, createTransactionResponse>? AuthFn { get; set; }
        public Func<AdnVoidRequest, createTransactionResponse>? VoidFn { get; set; }
        public List<AdnAuthorizeRequest> AuthCalls { get; } = [];
        public List<AdnVoidRequest> VoidCalls { get; } = [];

        public TestableAdnApiService()
            : base(
                Mock.Of<IHostEnvironment>(),
                Mock.Of<ILogger<AdnApiService>>(),
                Options.Create(new AdnSettings()),
                Mock.Of<ICustomerRepository>())
        { }

        public override createTransactionResponse ADN_Authorize(AdnAuthorizeRequest request)
        {
            AuthCalls.Add(request);
            if (AuthFn == null) throw new InvalidOperationException("AuthFn not configured.");
            return AuthFn(request);
        }

        public override createTransactionResponse ADN_Void(AdnVoidRequest request)
        {
            VoidCalls.Add(request);
            if (VoidFn == null) throw new InvalidOperationException("VoidFn not configured.");
            return VoidFn(request);
        }
    }
}
