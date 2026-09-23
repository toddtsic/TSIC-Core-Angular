using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;
using TSIC.Contracts.Services;

namespace TSIC.Infrastructure.Services;

/// <summary>
/// DNS-backed implementation of <see cref="IRecipientDomainService"/>, using DnsClient.NET because
/// the BCL has no MX lookup and <c>Dns.GetHostEntryAsync</c> cannot tell "domain does not exist"
/// from "has MX but no A record" — which would condemn plenty of legitimate mail domains.
///
/// Fails open, always. Every path that is not a clean answer returns
/// <see cref="RecipientDomainStatus.Unknown"/>, so a blocked port 53 on the app pool, a SERVFAIL or
/// a slow resolver degrades the diagnosis to "could not check" and never to "this address is bad".
/// </summary>
public sealed class RecipientDomainService : IRecipientDomainService
{
    private readonly ILookupClient _dns;
    private readonly ILogger<RecipientDomainService> _logger;

    public RecipientDomainService(ILookupClient dns, ILogger<RecipientDomainService> logger)
    {
        _dns = dns;
        _logger = logger;
    }

    public async Task<RecipientDomainStatus> CheckAsync(
        string email, CancellationToken cancellationToken = default)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at < 0 || at == email!.Length - 1)
        {
            return RecipientDomainStatus.Unknown;
        }

        var domain = email[(at + 1)..].Trim().TrimEnd('.');
        if (string.IsNullOrEmpty(domain))
        {
            return RecipientDomainStatus.Unknown;
        }

        try
        {
            // MX is the authoritative answer. NXDOMAIN here settles it: the domain does not exist,
            // so there is no point asking for an A record.
            var mx = await _dns.QueryAsync(domain, QueryType.MX, cancellationToken: cancellationToken);
            if (mx.HasError)
            {
                return mx.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain
                    ? RecipientDomainStatus.NoMailExchanger
                    : Unresolved(domain, "MX", mx.Header.ResponseCode, mx.ErrorMessage);
            }

            var mxRecords = mx.Answers.OfType<MxRecord>().ToList();

            // RFC 7505: a single MX pointing at the root (".") is a "null MX" - an explicit
            // declaration that the domain accepts no mail. It is a stronger no than having no MX
            // at all, so it must not be read as simply "an MX record exists".
            if (mxRecords.Any(r => !IsNullExchange(r.Exchange?.Value)))
            {
                return RecipientDomainStatus.Accepts;
            }

            if (mxRecords.Count > 0)
            {
                return RecipientDomainStatus.NoMailExchanger;
            }

            // No MX is not the same as no mail: RFC 5321 treats an A record as an implicit MX, and
            // small domains rely on that.
            var a = await _dns.QueryAsync(domain, QueryType.A, cancellationToken: cancellationToken);
            if (a.HasError)
            {
                return a.Header.ResponseCode == DnsHeaderResponseCode.NotExistentDomain
                    ? RecipientDomainStatus.NoMailExchanger
                    : Unresolved(domain, "A", a.Header.ResponseCode, a.ErrorMessage);
            }

            return a.Answers.OfType<ARecord>().Any()
                ? RecipientDomainStatus.Accepts
                : RecipientDomainStatus.NoMailExchanger;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Timeout, blocked port 53, resolver misconfiguration. Diagnosing an address is never
            // worth reporting a false negative over.
            _logger.LogWarning(ex, "DNS lookup failed for {Domain}", domain);
            return RecipientDomainStatus.Unknown;
        }
    }

    private static bool IsNullExchange(string? exchange)
    {
        var value = exchange?.Trim();
        return string.IsNullOrEmpty(value) || value == ".";
    }

    private RecipientDomainStatus Unresolved(
        string domain, string queryType, DnsHeaderResponseCode code, string? error)
    {
        _logger.LogWarning(
            "DNS {QueryType} lookup for {Domain} returned {ResponseCode} ({ResponseCodeValue}): {Error}",
            queryType, domain, code, (int)code, error);
        return RecipientDomainStatus.Unknown;
    }
}
