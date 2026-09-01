using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TSIC.API.Services.Shared.UsLax;

public class UsLaxService : IUsLaxService
{
    // Vendor spec: "a request with 500 or more is rejected" — so the maximum legal
    // chunk size is 499. DO NOT lower: smaller chunks = more round-trips = more rate-
    // limit pressure on a vendor that has blacklisted us before.
    private const int MaxBatchSize = 499;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IOptions<UsLaxSettings> _options;
    private readonly IHostEnvironment _env;
    private readonly ILogger<UsLaxService> _logger;
    private const string AccessTokenCacheKey = "uslax:access_token";
    private const string AccessTokenExpiryKey = "uslax:access_token_exp";
    private const string MemberCachePrefix = "uslax:member:";
    // Spans a registration/wizard session so the MemberPing made to VALIDATE a number on the
    // Forms step is reused when the same number is stamped at submit (player expiry minting) —
    // one vendor call, not two. A longer TTL only REDUCES vendor pressure (the blacklist lever
    // is batch size, not this window); exp dates don't change minute-to-minute, and a corrected
    // number is a different cache key, so the staleness cost is negligible. Written ONLY by the
    // single-ping GET path above; coach submit and revalidate read it.
    //
    // The batch path READS this keyspace but deliberately caches NOTHING of its own. Two reasons,
    // and do not "optimize" either away:
    //   1. A batch record is a REDUCED projection — the vendor omits `birthdate`.
    //      UsLaxEligibilityPolicy needs it for the lastname+DOB identity match and fails CLOSED
    //      without it (DobMismatch), so a batch record reaching the eligibility path would tell
    //      families with perfectly good memberships that their DOB doesn't match.
    //   2. It buys nothing. A reconcile is ONE POST regardless of how many ids are cached; a
    //      cache only saves a call when EVERY id is cached — exactly the immediate re-run where
    //      the director is iterating and needs fresh data.
    // Caching batch records here previously did both harms at once: the flat record could not be
    // read back by ParsePingResponse (which wants `status_code` at the root), so a second
    // reconcile inside the TTL reported "API error" on every row and silently wrote no dates.
    private static readonly TimeSpan MemberCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly Regex ValidMembershipFormat = new(@"^\d{6,12}$", RegexOptions.Compiled);

    public UsLaxService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<UsLaxSettings> options,
        IHostEnvironment env,
        ILogger<UsLaxService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _options = options;
        _env = env;
        _logger = logger;
    }

    public async Task<string?> GetMemberRawJsonAsync(string membershipId, CancellationToken ct = default)
    {
        // Defense-in-depth: never forward malformed numbers to USALax (blacklist risk).
        var trimmed = membershipId?.Trim() ?? string.Empty;
        if (!ValidMembershipFormat.IsMatch(trimmed)) return null;

        // USALax API requires 12-digit zero-padded membership IDs
        var padded = trimmed.PadLeft(12, '0');

        var cacheKey = MemberCachePrefix + padded;
        if (_cache.TryGetValue<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
        {
            return cached;
        }

        var client = _httpClientFactory.CreateClient("uslax");
        var token = await GetValidAccessTokenAsync(client, ct);
        if (string.IsNullOrWhiteSpace(token)) return null;

        var content = await SendMemberPingAsync(client, token, padded, ct);
        if (content is null || IsBearerTokenInvalid(content))
        {
            // Try one refresh by forcing new token
            token = await FetchAccessTokenAsync(client, ct);
            if (string.IsNullOrWhiteSpace(token)) return content; // return whatever we had
            content = await SendMemberPingAsync(client, token, padded, ct);
        }

        if (!string.IsNullOrEmpty(content))
        {
            _cache.Set(cacheKey, content, MemberCacheTtl);
        }
        return content;
    }

    public async Task<UsLaxMemberPingResult?> GetMemberAsync(string membershipId, CancellationToken ct = default)
    {
        var json = await GetMemberRawJsonAsync(membershipId, ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        return ParsePingResponse(json);
    }

    public async Task<IReadOnlyDictionary<string, UsLaxMemberPingResult>> GetMembersAsync(
        IReadOnlyCollection<string> membershipIds, CancellationToken ct = default)
    {
        var results = new Dictionary<string, UsLaxMemberPingResult>(membershipIds.Count, StringComparer.Ordinal);
        if (membershipIds.Count == 0) return results;

        // 1. Boundary filter + dedup.
        //    Format-rejected ids get a synthesized result and are NOT sent to the wire —
        //    spec says "one bad entry fails the whole request," so a single junk id in
        //    the input could otherwise tank a 499-id batch.
        //    Multiple raw ids that pad to the same 12-digit form are deduped on the wire
        //    but each raw caller id receives a copy of the result.
        var paddedToRawIds = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var raw in membershipIds)
        {
            var trimmed = raw?.Trim() ?? string.Empty;
            if (!ValidMembershipFormat.IsMatch(trimmed))
            {
                results[raw ?? string.Empty] = new UsLaxMemberPingResult
                {
                    StatusCode = 0,
                    ErrorMessage = "Invalid format"
                };
                continue;
            }
            var padded = trimmed.PadLeft(12, '0');
            if (!paddedToRawIds.TryGetValue(padded, out var rawList))
            {
                rawList = new List<string>(1);
                paddedToRawIds[padded] = rawList;
            }
            rawList.Add(raw!);
        }

        // 2. Cache read-through. Reads the same `uslax:member:` keyspace the GET path
        //    populates — a wizard validation inside the TTL makes this entry a freebie.
        //    Read-only: nothing fetched below is written back. See MemberCacheTtl.
        var paddedResults = new Dictionary<string, UsLaxMemberPingResult>(paddedToRawIds.Count, StringComparer.Ordinal);
        var toFetch = new List<string>(paddedToRawIds.Count);
        foreach (var padded in paddedToRawIds.Keys)
        {
            if (_cache.TryGetValue<string>(MemberCachePrefix + padded, out var cachedJson)
                && !string.IsNullOrEmpty(cachedJson))
            {
                var parsed = ParsePingResponse(cachedJson);
                if (parsed != null)
                {
                    paddedResults[padded] = parsed;
                    continue;
                }
            }
            toFetch.Add(padded);
        }

        // 3. Chunk and fetch. Serial, not parallel — typical jobs fit in one chunk and a
        //    burst pattern on the vendor is the wrong trade for ~200ms wall-clock saving.
        foreach (var chunk in toFetch.Chunk(MaxBatchSize))
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, UsLaxMemberPingResult> chunkResults;
            try
            {
                chunkResults = await FetchBatchChunkAsync(chunk, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Per-chunk isolation: one chunk's failure does not abort the others.
                _logger.LogWarning(ex, "USLax batch ping chunk failed ({ChunkSize} ids).", chunk.Length);
                chunkResults = SynthesizeFailureForChunk(chunk, "Network or parse failure");
            }

            foreach (var id in chunk)
            {
                paddedResults[id] = chunkResults.TryGetValue(id, out var r)
                    ? r
                    : new UsLaxMemberPingResult { StatusCode = 0, ErrorMessage = "Network or parse failure" };
            }
        }

        // 4. Fan-back to raw caller ids.
        foreach (var (padded, rawList) in paddedToRawIds)
        {
            if (!paddedResults.TryGetValue(padded, out var result)) continue;
            foreach (var raw in rawList)
            {
                results[raw] = result;
            }
        }

        return results;
    }

    /// <summary>
    /// Wire-level fetch for a single chunk (≤499 padded ids). Returns a result for every
    /// input id (404 synthesized when the API silently omits an id per spec). Bearer-
    /// token expiry triggers exactly one refresh + retry. Test fixtures override this to
    /// avoid the network.
    /// </summary>
    protected virtual async Task<IReadOnlyDictionary<string, UsLaxMemberPingResult>> FetchBatchChunkAsync(
        IReadOnlyList<string> paddedIds, CancellationToken ct)
    {
        if (paddedIds.Count == 0)
        {
            return new Dictionary<string, UsLaxMemberPingResult>(StringComparer.Ordinal);
        }

        var client = _httpClientFactory.CreateClient("uslax");
        var token = await GetValidAccessTokenAsync(client, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            return SynthesizeFailureForChunk(paddedIds, "Authentication failed");
        }

        var body = await SendBatchPostAsync(client, token, paddedIds, ct);
        if (body != null && IsBearerTokenInvalid(body))
        {
            token = await FetchAccessTokenAsync(client, ct);
            if (!string.IsNullOrWhiteSpace(token))
            {
                body = await SendBatchPostAsync(client, token, paddedIds, ct);
            }
        }

        if (string.IsNullOrEmpty(body))
        {
            return SynthesizeFailureForChunk(paddedIds, "Network or parse failure");
        }

        return ParseBatchResponse(body, paddedIds);
    }

    private static UsLaxMemberPingResult? ParsePingResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseEnvelopeRoot(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static UsLaxMemberPingResult? ParseEnvelopeRoot(JsonElement root)
    {
        var statusCode = root.TryGetProperty("status_code", out var sc) && sc.ValueKind == JsonValueKind.Number
            ? sc.GetInt32()
            : 0;

        // On error the API nests either a string output or an object output w/ error_message.
        if (statusCode != 200)
        {
            string? err = null;
            if (root.TryGetProperty("output", out var errOut))
            {
                err = errOut.ValueKind switch
                {
                    JsonValueKind.String => errOut.GetString(),
                    JsonValueKind.Object => errOut.TryGetProperty("error_message", out var em) ? em.GetString() : errOut.GetRawText(),
                    _ => null
                };
            }
            return new UsLaxMemberPingResult { StatusCode = statusCode, ErrorMessage = err };
        }

        UsLaxMemberPingOutput? output = null;
        if (root.TryGetProperty("output", out var outEl) && outEl.ValueKind == JsonValueKind.Object)
        {
            output = ExtractMemberFields(outEl);
        }

        return new UsLaxMemberPingResult { StatusCode = statusCode, Output = output };
    }

    private static UsLaxMemberPingOutput ExtractMemberFields(JsonElement el)
    {
        return new UsLaxMemberPingOutput
        {
            MembershipId = el.TryGetProperty("membership_id", out var mid) ? mid.GetString() : null,
            MemStatus = el.TryGetProperty("mem_status", out var ms) ? ms.GetString() : null,
            ExpDate = el.TryGetProperty("exp_date", out var ed) ? ed.GetString() : null,
            FirstName = el.TryGetProperty("firstname", out var fn) ? fn.GetString() : null,
            LastName = el.TryGetProperty("lastname", out var ln) ? ln.GetString() : null,
            Birthdate = el.TryGetProperty("birthdate", out var bd) ? bd.GetString() : null,
            Email = el.TryGetProperty("email", out var em) ? em.GetString() : null,
            // age_verified is the one field whose shape we have seen the vendor treat loosely
            // ("Approved", not a boolean). GetString() THROWS on a non-string JsonValueKind, which
            // would fail the whole batch parse rather than one field, so read it defensively and
            // let a boolean or number come through as its text.
            AgeVerified = el.TryGetProperty("age_verified", out var av) && av.ValueKind != JsonValueKind.Null
                ? (av.ValueKind == JsonValueKind.String ? av.GetString() : av.ToString())
                : null,
            Involvement = ExtractInvolvement(el)
        };
    }

    private static IReadOnlyList<string>? ExtractInvolvement(JsonElement outEl)
    {
        if (!outEl.TryGetProperty("involvement", out var inv)) return null;
        // USALax returns this as an array of strings in some versions, a single string in others.
        return inv.ValueKind switch
        {
            JsonValueKind.Array => inv.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList(),
            JsonValueKind.String => inv.GetString() is { } s ? new[] { s } : null,
            _ => null
        };
    }

    protected IReadOnlyDictionary<string, UsLaxMemberPingResult> ParseBatchResponse(
        string body, IReadOnlyList<string> paddedIds)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return SynthesizeFailureForChunk(paddedIds, "Network or parse failure");
        }

        using (doc)
        {
            return ParseBatchRoot(doc.RootElement, paddedIds);
        }
    }

    private IReadOnlyDictionary<string, UsLaxMemberPingResult> ParseBatchRoot(
        JsonElement root, IReadOnlyList<string> paddedIds)
    {
        var results = new Dictionary<string, UsLaxMemberPingResult>(paddedIds.Count, StringComparer.Ordinal);

        // Object root with status_code=200 + output:Array = envelope-wrapped success.
        // Vendor returns this shape despite the spec doc showing a bare array. Confirmed
        // by live debugger inspection of the response body. Descend into output[] so the
        // existing array branch handles the records.
        // Object root with status_code != 200 (or no output array) = error envelope —
        // 404 "no records," 500 "validation failed," etc. Applied uniformly to all ids.
        if (root.ValueKind == JsonValueKind.Object)
        {
            var statusCode = root.TryGetProperty("status_code", out var sc) && sc.ValueKind == JsonValueKind.Number
                ? sc.GetInt32() : 0;

            if (statusCode == 200
                && root.TryGetProperty("output", out var successOut)
                && successOut.ValueKind == JsonValueKind.Array)
            {
                return ParseBatchRoot(successOut, paddedIds);
            }

            string? errMsg = null;
            if (root.TryGetProperty("output", out var errOut))
            {
                errMsg = errOut.ValueKind switch
                {
                    JsonValueKind.String => errOut.GetString(),
                    JsonValueKind.Object => errOut.TryGetProperty("error_message", out var em) ? em.GetString() : errOut.GetRawText(),
                    _ => null
                };
            }
            if (statusCode == 404)
            {
                foreach (var id in paddedIds)
                {
                    results[id] = new UsLaxMemberPingResult { StatusCode = 404, ErrorMessage = errMsg ?? "No Users record found." };
                }
                return results;
            }
            foreach (var id in paddedIds)
            {
                results[id] = new UsLaxMemberPingResult { StatusCode = statusCode, ErrorMessage = errMsg };
            }
            return results;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return SynthesizeFailureForChunk(paddedIds, "Network or parse failure");
        }

        // 200 success → array of records. Spec says records are flat; existing GET parser
        // expects an envelope wrapper. Accept BOTH per-element shapes — detect by the
        // presence of `status_code` on the element. Eliminates a pre-flight guess.
        // Records the API didn't recognize are silently omitted (NOT returned as null
        // placeholders) — synthesize 404 for any input id not found in the response.
        var byId = new Dictionary<string, UsLaxMemberPingResult>(StringComparer.Ordinal);
        // Shape telemetry — see the LogInformation below. Field NAMES and counts only.
        var fieldsSeen = new SortedSet<string>(StringComparer.Ordinal);
        var withBirthdate = 0;
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;

            UsLaxMemberPingResult? parsed;
            string? recordId;
            // The element holding the member fields: the element itself when flat, `output`
            // when envelope-shaped. Used for the shape telemetry only.
            JsonElement fieldsEl;
            if (element.TryGetProperty("status_code", out _))
            {
                // Envelope-shaped element.
                parsed = ParseEnvelopeRoot(element);
                recordId = parsed?.Output?.MembershipId;
                fieldsEl = element.TryGetProperty("output", out var outEl)
                           && outEl.ValueKind == JsonValueKind.Object
                    ? outEl
                    : element;
            }
            else if (element.TryGetProperty("membership_id", out var midEl))
            {
                // Flat-shaped element (spec).
                recordId = midEl.GetString();
                parsed = new UsLaxMemberPingResult
                {
                    StatusCode = 200,
                    Output = ExtractMemberFields(element)
                };
                fieldsEl = element;
            }
            else
            {
                continue;
            }

            if (parsed == null || string.IsNullOrWhiteSpace(recordId)) continue;

            foreach (var prop in fieldsEl.EnumerateObject())
            {
                fieldsSeen.Add(prop.Name);
            }
            if (fieldsEl.TryGetProperty("birthdate", out var bdEl)
                && bdEl.ValueKind != JsonValueKind.Null
                && !string.IsNullOrWhiteSpace(bdEl.ToString()))
            {
                withBirthdate++;
            }

            // Defensive: an un-padded id in the response still matches our padded keys.
            var key = recordId.PadLeft(12, '0');
            byId[key] = parsed;
        }

        // The batch response is a REDUCED projection of the single-ping record and we do not
        // know with certainty which fields it carries — `birthdate` in particular decides
        // whether a batch record could ever back an eligibility check (UsLaxEligibilityPolicy
        // fails CLOSED without one). Log the SHAPE so we can settle it from real traffic.
        //
        // NON-PRODUCTION ONLY. Diagnostic telemetry, not an operational signal — gating it here
        // rather than at a log level means it can stay in the code permanently without ever
        // adding noise (or an audit surface) to the production log. Run a reconcile on
        // dev/staging to answer the question; nothing to revert afterwards.
        //
        // Field NAMES and counts only — never values. Member details (name, DOB, email,
        // membership number) are PII and do not belong in Seq; same rule the wizard's rejection
        // log follows in ValidationController.
        if (!_env.IsProduction())
        {
            _logger.LogInformation(
                "USLax batch response shape: {RequestedCount} requested, {ReturnedCount} returned, "
                + "{WithBirthdate} carrying a non-empty birthdate. Fields present: {Fields}",
                paddedIds.Count, byId.Count, withBirthdate,
                fieldsSeen.Count == 0 ? "(none)" : string.Join(",", fieldsSeen));
        }

        foreach (var id in paddedIds)
        {
            if (byId.TryGetValue(id, out var r))
            {
                results[id] = r;
            }
            else
            {
                results[id] = new UsLaxMemberPingResult
                {
                    StatusCode = 404,
                    ErrorMessage = "No Users record found."
                };
            }
        }
        return results;
    }

    private static IReadOnlyDictionary<string, UsLaxMemberPingResult> SynthesizeFailureForChunk(
        IReadOnlyList<string> paddedIds, string errorMessage)
    {
        var d = new Dictionary<string, UsLaxMemberPingResult>(paddedIds.Count, StringComparer.Ordinal);
        foreach (var id in paddedIds)
        {
            d[id] = new UsLaxMemberPingResult { StatusCode = 0, ErrorMessage = errorMessage };
        }
        return d;
    }

    private async Task<string?> GetValidAccessTokenAsync(HttpClient client, CancellationToken ct)
    {
        if (_cache.TryGetValue<string>(AccessTokenCacheKey, out var token) &&
            _cache.TryGetValue<DateTimeOffset>(AccessTokenExpiryKey, out var exp) &&
            DateTimeOffset.UtcNow < exp.AddMinutes(-1))
        {
            return token;
        }
        return await FetchAccessTokenAsync(client, ct);
    }

    private async Task<string?> FetchAccessTokenAsync(HttpClient client, CancellationToken ct)
    {
        var (clientId, secret, username, password, _) = ResolveCredentials();
        // Use password grant; refresh flow can be added later if needed
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth2/")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string?, string?>("client_id", clientId),
                new("secret", secret),
                new("username", username),
                new("password", password),
                new("grant_type", "password")
            })
        };
        request.Headers.UserAgent.ParseAdd("PostmanRuntime/7.29.0");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var access = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var expiresInSeconds = root.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 900;
            if (string.IsNullOrWhiteSpace(access)) return null;
            var exp = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
            _cache.Set(AccessTokenCacheKey, access, exp);
            _cache.Set(AccessTokenExpiryKey, exp, exp);
            return access;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> SendMemberPingAsync(HttpClient client, string accessToken, string membershipId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/MemberPing?membership_id={Uri.EscapeDataString(membershipId)}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.UserAgent.ParseAdd("PostmanRuntime/7.29.0");
        using var response = await client.SendAsync(req, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static async Task<string?> SendBatchPostAsync(
        HttpClient client, string accessToken, IReadOnlyList<string> paddedIds, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { membership_ids = paddedIds });
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/MemberPing")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.UserAgent.ParseAdd("PostmanRuntime/7.29.0");
        try
        {
            using var response = await client.SendAsync(req, ct);
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout (not caller cancellation) — treat as transport failure.
            return null;
        }
    }

    private static bool IsBearerTokenInvalid(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        if (!body.Contains("\"status_code\":500")) return false;
        return body.Contains("Invalid Bearer token") || body.Contains("Bearer token expired");
    }

    private (string clientId, string secret, string username, string password, string? refresh) ResolveCredentials()
    {
        var s = _options.Value;
        string clientId = s.ClientId ?? Environment.GetEnvironmentVariable("USLAX_CLIENT_ID") ?? string.Empty;
        string secret = s.Secret ?? Environment.GetEnvironmentVariable("USLAX_SECRET") ?? string.Empty;
        string username = s.Username ?? Environment.GetEnvironmentVariable("USLAX_USERNAME") ?? string.Empty;
        string password = s.Password ?? Environment.GetEnvironmentVariable("USLAX_PASSWORD") ?? string.Empty;
        string? refresh = s.RefreshToken ?? Environment.GetEnvironmentVariable("USLAX_REFRESH_TOKEN");
        return (clientId, secret, username, password, refresh);
    }
}
