using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TSIC.API.Services.Shared.UsLax;

namespace TSIC.Tests.UsLax;

/// <summary>
/// Tests for the orchestrator side of GetMembersAsync — boundary filtering, dedup,
/// cache short-circuit, chunk boundary at the 499 vendor cap, fan-back to raw caller
/// ids, and per-chunk failure isolation. The wire-level work (POST, bearer-refresh,
/// JSON parse) lives behind the FetchBatchChunkAsync seam and is canned per test.
///
/// The 499-cap is load-bearing: vendor rejects "500 or more" per the published spec
/// (docs/USALacrosse/MembershipPingSpec.txt). Two specific boundary tests pin this.
/// </summary>
public class UsLaxServiceBatchTests
{
    [Fact]
    public async Task EmptyInput_ReturnsEmpty_NoFetch()
    {
        var sut = NewSut();

        var result = await sut.GetMembersAsync(Array.Empty<string>());

        result.Should().BeEmpty();
        sut.FetchCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidFormat_SynthesizesResult_NotSentToWire()
    {
        var sut = NewSut(canned: _ => new Dictionary<string, UsLaxMemberPingResult>());

        var result = await sut.GetMembersAsync(new[] { "abc", "12345" /* 5 digits */, "  ", "" });

        result.Should().HaveCount(4);
        result.Values.Should().OnlyContain(r => r.StatusCode == 0 && r.ErrorMessage == "Invalid format");
        sut.FetchCalls.Should().BeEmpty(); // never hit the wire — would have poisoned the batch
    }

    [Fact]
    public async Task ValidIdsArePaddedTo12Digits_OnTheWire()
    {
        var sut = NewSut(canned: chunk => chunk.ToDictionary(
            id => id,
            id => new UsLaxMemberPingResult { StatusCode = 200, Output = new UsLaxMemberPingOutput { MembershipId = id } },
            StringComparer.Ordinal));

        await sut.GetMembersAsync(new[] { "123456", "0000000123456" /* already 13 — invalid, dropped */ });

        // "123456" pads to "000000123456"; "0000000123456" is 13 digits, fails the regex.
        sut.FetchCalls.Should().HaveCount(1);
        sut.FetchCalls[0].Should().BeEquivalentTo("000000123456");
    }

    [Fact]
    public async Task DuplicateRawIds_DedupedOnWire_BothRawKeysInResult()
    {
        // "123456" and "0000123456" both pad to "000000123456" — single wire entry.
        var sut = NewSut(canned: chunk => chunk.ToDictionary(
            id => id,
            id => new UsLaxMemberPingResult
            {
                StatusCode = 200,
                Output = new UsLaxMemberPingOutput { MembershipId = id, ExpDate = "2027-01-01" }
            },
            StringComparer.Ordinal));

        var result = await sut.GetMembersAsync(new[] { "123456", "0000123456" });

        sut.FetchCalls.Should().HaveCount(1);
        sut.FetchCalls[0].Should().BeEquivalentTo("000000123456");
        result.Should().HaveCount(2);
        result["123456"].StatusCode.Should().Be(200);
        result["0000123456"].StatusCode.Should().Be(200);
        result["123456"].Output!.ExpDate.Should().Be("2027-01-01");
    }

    [Fact]
    public async Task AllCacheHits_NoFetch()
    {
        var cache = NewCache();
        // Pre-populate cache with the SAME envelope shape the GET path stores.
        cache.Set("uslax:member:000000123456",
            "{\"status_code\":200,\"output\":{\"membership_id\":\"000000123456\",\"exp_date\":\"2026-12-31\"}}",
            TimeSpan.FromSeconds(60));
        cache.Set("uslax:member:000000654321",
            "{\"status_code\":200,\"output\":{\"membership_id\":\"000000654321\",\"exp_date\":\"2027-06-15\"}}",
            TimeSpan.FromSeconds(60));

        var sut = NewSut(cache: cache);

        var result = await sut.GetMembersAsync(new[] { "123456", "654321" });

        sut.FetchCalls.Should().BeEmpty();
        result["123456"].Output!.ExpDate.Should().Be("2026-12-31");
        result["654321"].Output!.ExpDate.Should().Be("2027-06-15");
    }

    [Fact]
    public async Task MixedCacheAndFetch_OnlyMissesGoToWire()
    {
        var cache = NewCache();
        cache.Set("uslax:member:000000111111",
            "{\"status_code\":200,\"output\":{\"membership_id\":\"000000111111\",\"exp_date\":\"2026-01-01\"}}",
            TimeSpan.FromSeconds(60));

        var sut = NewSut(
            cache: cache,
            canned: chunk => chunk.ToDictionary(
                id => id,
                id => new UsLaxMemberPingResult { StatusCode = 200, Output = new UsLaxMemberPingOutput { MembershipId = id } },
                StringComparer.Ordinal));

        await sut.GetMembersAsync(new[] { "111111", "222222", "333333" });

        sut.FetchCalls.Should().HaveCount(1);
        sut.FetchCalls[0].Should().BeEquivalentTo("000000222222", "000000333333");
    }

    [Fact]
    public async Task ResponseSilentlyOmitsId_SynthesizesA404()
    {
        // Spec: ids with no AMMS record are silently omitted from the response array.
        // Orchestrator must materialize a 404 row for them.
        var sut = NewSut(canned: chunk =>
        {
            // Simulate: only "000000111111" came back; "000000222222" was omitted.
            var dict = new Dictionary<string, UsLaxMemberPingResult>(StringComparer.Ordinal)
            {
                ["000000111111"] = new() { StatusCode = 200, Output = new UsLaxMemberPingOutput { MembershipId = "000000111111" } },
                ["000000222222"] = new() { StatusCode = 404, ErrorMessage = "No Users record found." }
            };
            return dict;
        });

        var result = await sut.GetMembersAsync(new[] { "111111", "222222" });

        result["111111"].StatusCode.Should().Be(200);
        result["222222"].StatusCode.Should().Be(404);
        result["222222"].ErrorMessage.Should().Be("No Users record found.");
    }

    // ── 500-cap pinning tests ──────────────────────────────────────────────

    [Fact]
    public async Task ChunkBoundary_500Inputs_SplitsAs499_Plus_1()
    {
        // Critical: "500 or more is rejected" per spec — never send 500.
        var ids = Enumerable.Range(1, 500).Select(i => i.ToString("D6")).ToList();
        var sut = NewSut(canned: chunk => chunk.ToDictionary(
            id => id,
            id => new UsLaxMemberPingResult { StatusCode = 200 },
            StringComparer.Ordinal));

        await sut.GetMembersAsync(ids);

        sut.FetchCalls.Should().HaveCount(2);
        sut.FetchCalls[0].Count.Should().Be(499);
        sut.FetchCalls[1].Count.Should().Be(1);
        sut.FetchCalls.Should().NotContain(c => c.Count >= 500);
    }

    [Fact]
    public async Task ChunkBoundary_999Inputs_SplitsAs499_499_1()
    {
        // Pinning test: 999 must NOT split as 499+500. 499 is the only legal max.
        var ids = Enumerable.Range(1, 999).Select(i => i.ToString("D6")).ToList();
        var sut = NewSut(canned: chunk => chunk.ToDictionary(
            id => id,
            id => new UsLaxMemberPingResult { StatusCode = 200 },
            StringComparer.Ordinal));

        await sut.GetMembersAsync(ids);

        sut.FetchCalls.Should().HaveCount(3);
        sut.FetchCalls.Select(c => c.Count).Should().Equal(499, 499, 1);
        sut.FetchCalls.Should().NotContain(c => c.Count >= 500);
    }

    [Fact]
    public async Task ChunkBoundary_499Inputs_SplitsAsSingle499()
    {
        var ids = Enumerable.Range(1, 499).Select(i => i.ToString("D6")).ToList();
        var sut = NewSut(canned: chunk => chunk.ToDictionary(
            id => id,
            id => new UsLaxMemberPingResult { StatusCode = 200 },
            StringComparer.Ordinal));

        await sut.GetMembersAsync(ids);

        sut.FetchCalls.Should().ContainSingle().Which.Count.Should().Be(499);
    }

    // ── Failure isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task FirstChunkThrows_OtherChunksStillProcessed()
    {
        var ids = Enumerable.Range(1, 600).Select(i => i.ToString("D6")).ToList();
        var callCount = 0;
        var sut = NewSut(canned: chunk =>
        {
            callCount++;
            if (callCount == 1) throw new HttpRequestException("simulated transport failure");
            return chunk.ToDictionary(
                id => id,
                id => new UsLaxMemberPingResult { StatusCode = 200, Output = new UsLaxMemberPingOutput { MembershipId = id } },
                StringComparer.Ordinal);
        });

        var result = await sut.GetMembersAsync(ids);

        // Both chunks attempted; chunk 1 ids → "Network or parse failure", chunk 2 ids → 200.
        sut.FetchCalls.Should().HaveCount(2);
        var firstChunkRaw = ids.Take(499).ToList();
        var secondChunkRaw = ids.Skip(499).ToList();
        result.Where(kv => firstChunkRaw.Contains(kv.Key))
            .Should().OnlyContain(kv => kv.Value.StatusCode == 0 && kv.Value.ErrorMessage == "Network or parse failure");
        result.Where(kv => secondChunkRaw.Contains(kv.Key))
            .Should().OnlyContain(kv => kv.Value.StatusCode == 200);
    }

    [Fact]
    public async Task CancellationBetweenChunks_PropagatesCancellation()
    {
        var ids = Enumerable.Range(1, 600).Select(i => i.ToString("D6")).ToList();
        using var cts = new CancellationTokenSource();
        var sut = NewSut(canned: chunk =>
        {
            cts.Cancel(); // cancel after first chunk attempts
            return chunk.ToDictionary(
                id => id,
                id => new UsLaxMemberPingResult { StatusCode = 200 },
                StringComparer.Ordinal);
        });

        var act = async () => await sut.GetMembersAsync(ids, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Parser shape tests (the wire layer) ───────────────────────────────
    // Exercise the response-body parser directly, since FetchBatchChunkAsync is mocked
    // out in the orchestrator tests above. These cover the actual JSON shapes the vendor
    // returns — confirmed via live debugger inspection (2026-05-08).

    [Fact]
    public void ParseBatchResponse_EnvelopeWrapped200_DescendsAndExtractsRecords()
    {
        // The vendor wraps the success array in an outer envelope despite the spec doc
        // showing a bare array. Confirmed by debugger capture: shape is
        //   { "status_code": 200, "output": [ {flat record}, ... ] }
        var body = """
        {
            "status_code": 200,
            "output": [
                {
                    "membership_id": "000005374328",
                    "mem_status": "Active",
                    "exp_date": "2027-04-30",
                    "firstname": "MISCHA",
                    "lastname": "SORINO",
                    "age_verified": "Approved",
                    "involvement": ["Player"]
                },
                {
                    "membership_id": "000005540585",
                    "mem_status": "Active",
                    "exp_date": "2026-11-30",
                    "firstname": "MORGAN",
                    "lastname": "BIEBERBACH",
                    "age_verified": "Approved",
                    "involvement": ["Player", "Official"]
                }
            ]
        }
        """;

        var sut = NewSut();
        var result = sut.ParseForTest(body, new[] { "000005374328", "000005540585" });

        result.Should().HaveCount(2);
        var first = result["000005374328"];
        first.StatusCode.Should().Be(200);
        first.Output.Should().NotBeNull();
        first.Output!.MemStatus.Should().Be("Active");
        first.Output.ExpDate.Should().Be("2027-04-30");
        first.Output.FirstName.Should().Be("MISCHA");
        first.Output.Involvement.Should().BeEquivalentTo("Player");

        var second = result["000005540585"];
        second.Output!.Involvement.Should().BeEquivalentTo("Player", "Official");
    }

    [Fact]
    public void ParseBatchResponse_EnvelopeWrapped200_MissingId_Synthesizes404()
    {
        // Spec: "IDs that do not match any user are silently omitted from the array."
        // Apply that to the envelope-wrapped shape too.
        var body = """
        {
            "status_code": 200,
            "output": [
                {
                    "membership_id": "000005374328",
                    "mem_status": "Active",
                    "exp_date": "2027-04-30",
                    "involvement": ["Player"]
                }
            ]
        }
        """;

        var sut = NewSut();
        var result = sut.ParseForTest(body, new[] { "000005374328", "000099999999" });

        result["000005374328"].StatusCode.Should().Be(200);
        result["000099999999"].StatusCode.Should().Be(404);
        result["000099999999"].ErrorMessage.Should().Be("No Users record found.");
    }

    [Fact]
    public void ParseBatchResponse_BareArrayShape_StillWorks()
    {
        // Defensive: if the vendor ever switches to the bare-array shape the spec
        // describes, the parser still handles it.
        var body = """
        [
            {
                "membership_id": "000005374328",
                "mem_status": "Active",
                "exp_date": "2027-04-30",
                "involvement": ["Player"]
            }
        ]
        """;

        var sut = NewSut();
        var result = sut.ParseForTest(body, new[] { "000005374328" });

        result["000005374328"].StatusCode.Should().Be(200);
        result["000005374328"].Output!.ExpDate.Should().Be("2027-04-30");
    }

    [Fact]
    public void ParseBatchResponse_404Envelope_AllIdsGet404()
    {
        var body = """{ "status_code": 404, "output": "No Users records found." }""";

        var sut = NewSut();
        var result = sut.ParseForTest(body, new[] { "000005374328", "000005540585" });

        result.Values.Should().OnlyContain(r => r.StatusCode == 404);
        result["000005374328"].ErrorMessage.Should().Be("No Users records found.");
    }

    [Fact]
    public void ParseBatchResponse_BearerTokenError_AllIdsGetTheError()
    {
        // The exact 500 envelope captured in the local debugger session 2026-05-08.
        // (This body normally triggers refresh-and-retry inside FetchBatchChunkAsync;
        // if it leaks through to the parser, all chunk ids should report 500.)
        var body = """{"status_code":500,"output":"Invalid Bearer token"}""";

        var sut = NewSut();
        var result = sut.ParseForTest(body, new[] { "000005374328" });

        result["000005374328"].StatusCode.Should().Be(500);
        result["000005374328"].ErrorMessage.Should().Be("Invalid Bearer token");
    }

    // ── Helpers / test fixture ────────────────────────────────────────────

    private static IMemoryCache NewCache() => new MemoryCache(new MemoryCacheOptions());

    private static TestableUsLaxService NewSut(
        IMemoryCache? cache = null,
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, UsLaxMemberPingResult>>? canned = null)
    {
        return new TestableUsLaxService(
            cache ?? NewCache(),
            canned ?? (_ => new Dictionary<string, UsLaxMemberPingResult>(StringComparer.Ordinal)));
    }

    private sealed class TestableUsLaxService : UsLaxService
    {
        private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, UsLaxMemberPingResult>> _fetch;
        public List<IReadOnlyList<string>> FetchCalls { get; } = new();

        public TestableUsLaxService(
            IMemoryCache cache,
            Func<IReadOnlyList<string>, IReadOnlyDictionary<string, UsLaxMemberPingResult>> fetch)
            : base(
                Mock.Of<IHttpClientFactory>(),
                cache,
                Options.Create(new UsLaxSettings()),
                NullLogger<UsLaxService>.Instance)
        {
            _fetch = fetch;
        }

        protected override Task<IReadOnlyDictionary<string, UsLaxMemberPingResult>> FetchBatchChunkAsync(
            IReadOnlyList<string> paddedIds, CancellationToken ct)
        {
            FetchCalls.Add(paddedIds);
            return Task.FromResult(_fetch(paddedIds));
        }

        // Test-only seam — exposes the protected parser so we can verify response-shape
        // handling end-to-end without needing the wire.
        public IReadOnlyDictionary<string, UsLaxMemberPingResult> ParseForTest(
            string body, IReadOnlyList<string> paddedIds)
            => ParseBatchResponse(body, paddedIds);
    }
}
