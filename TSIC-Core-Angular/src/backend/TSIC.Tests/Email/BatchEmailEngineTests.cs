using FluentAssertions;
using TSIC.API.Services.Shared.Email;

namespace TSIC.Tests.Email;

/// <summary>
/// Correctness unit tests for the batch-email engine's pure logic: recipient filtering
/// (dedup, sentinel/invalid strip) and the in-memory job registry's tallying.
/// End-to-end behavior is covered by the dev "TEST BATCH PROCESSING" button.
/// </summary>
public class BatchEmailRecipientFilterTests
{
    [Fact(DisplayName = "Distinct union of mom+dad+player addresses, order preserved")]
    public void BuildSendableSet_DistinctUnion()
    {
        var result = BatchEmailRecipientFilter.BuildSendableSet(new[] { "mom@x.com", "dad@x.com", "kid@x.com" });
        result.Should().Equal("mom@x.com", "dad@x.com", "kid@x.com");
    }

    [Fact(DisplayName = "Blank, whitespace, and null candidates are dropped")]
    public void BuildSendableSet_DropsBlanks()
    {
        var result = BatchEmailRecipientFilter.BuildSendableSet(new[] { null, "", "   ", "a@x.com" });
        result.Should().Equal("a@x.com");
    }

    [Fact(DisplayName = "not@given.com sentinel is dropped, case-insensitively")]
    public void BuildSendableSet_DropsSentinel()
    {
        var result = BatchEmailRecipientFilter.BuildSendableSet(new[] { "not@given.com", "NOT@GIVEN.COM", "real@x.com" });
        result.Should().Equal("real@x.com");
    }

    [Fact(DisplayName = "Addresses without '@' are dropped as invalid")]
    public void BuildSendableSet_DropsInvalid()
    {
        var result = BatchEmailRecipientFilter.BuildSendableSet(new[] { "notanemail", "ok@x.com" });
        result.Should().Equal("ok@x.com");
    }

    [Fact(DisplayName = "Dedup is case-insensitive and trims surrounding whitespace")]
    public void BuildSendableSet_DedupCaseInsensitiveAndTrims()
    {
        var result = BatchEmailRecipientFilter.BuildSendableSet(new[] { "Mom@X.com", "  mom@x.com  ", "mom@x.COM" });
        result.Should().Equal("Mom@X.com");
    }

    [Theory(DisplayName = "IsSendable rules")]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not@given.com", false)]
    [InlineData("nope", false)]
    [InlineData("a@b.com", true)]
    public void IsSendable_Rules(string? email, bool expected)
    {
        BatchEmailRecipientFilter.IsSendable(email).Should().Be(expected);
    }
}

public class EmailBatchJobRegistryTests
{
    [Fact(DisplayName = "Create then Get exposes totals and not-done")]
    public void Create_Then_Get()
    {
        var registry = new EmailBatchJobRegistry();
        var id = Guid.NewGuid();
        registry.Create(id, totalRecipients: 10, optedOut: 2);

        var status = registry.Get(id);
        status.Should().NotBeNull();
        status!.TotalRecipients.Should().Be(10);
        status.OptedOut.Should().Be(2);
        status.Sent.Should().Be(0);
        status.Failed.Should().Be(0);
        status.Done.Should().BeFalse();
        status.Processed.Should().Be(0);
    }

    [Fact(DisplayName = "Successes and failures tally; failed addresses captured + deduped")]
    public void RecordResult_Tallies()
    {
        var registry = new EmailBatchJobRegistry();
        var id = Guid.NewGuid();
        registry.Create(id, totalRecipients: 3, optedOut: 0);

        registry.RecordResult(id, success: true, Array.Empty<string>());
        registry.RecordResult(id, success: false, new[] { "bad@x.com" });
        registry.RecordResult(id, success: false, new[] { "bad@x.com", "bad2@x.com" }); // dedup bad@x.com

        var status = registry.Get(id)!;
        status.Sent.Should().Be(1);
        status.Failed.Should().Be(2);
        status.Processed.Should().Be(3);
        status.FailedAddresses.Should().BeEquivalentTo("bad@x.com", "bad2@x.com");
    }

    [Fact(DisplayName = "Complete marks the job done")]
    public void Complete_SetsDone()
    {
        var registry = new EmailBatchJobRegistry();
        var id = Guid.NewGuid();
        registry.Create(id, 1, 0);
        registry.Complete(id);
        registry.Get(id)!.Done.Should().BeTrue();
    }

    [Fact(DisplayName = "Unknown job id returns null")]
    public void Get_Unknown_ReturnsNull()
    {
        new EmailBatchJobRegistry().Get(Guid.NewGuid()).Should().BeNull();
    }
}
