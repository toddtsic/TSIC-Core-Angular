using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Shared.UsLax;

/// <summary>
/// Two-part identity verification for a coach's USA Lacrosse membership.
///
/// The approval queue is a placement gate, not an identity gate — a director can't tell a
/// real coach from someone who typed a coach's number. So we prove ownership at registration:
/// validate the number is an ACTIVE COACH membership, then email a one-time code to the
/// address USA Lacrosse has ON FILE for that membership. Controlling that mailbox ⇒ the
/// registrant is the member. The code never leaves the server except via that on-file email.
///
/// State lives in <see cref="IMemoryCache"/> (mirrors RefreshTokenService) — short-lived,
/// no DB. Membership validity is a HARD gate (begin fails for non-coach/expired numbers);
/// identity proof is SOFT downstream (recorded as verified/unverified per the fallback rule).
/// </summary>
public interface IUsLaxIdentityVerificationService
{
    /// <summary>Validate the membership, then email a one-time code to the on-file address.</summary>
    Task<UsLaxVerifyBeginResult> BeginAsync(string sportAssnId, CancellationToken ct = default);

    /// <summary>Check a submitted code against an outstanding verification.</summary>
    UsLaxVerifyConfirmResult Confirm(string verificationId, string code);

    /// <summary>Single-use read by the submit path: returns the verified record iff this
    /// verificationId is confirmed AND bound to the same membership number; else null.</summary>
    UsLaxVerifiedRecord? Consume(string verificationId, string sportAssnId);
}

public enum UsLaxVerifyBeginStatus
{
    /// <summary>Code generated and emailed to the on-file address.</summary>
    Sent,
    /// <summary>Number isn't an active Coach membership — registration must reject it.</summary>
    MembershipInvalid,
    /// <summary>Valid membership, but USA Lacrosse has no on-file email — drives the
    /// "continue unverified" fallback in the UI.</summary>
    EmailUnavailable,
    /// <summary>Transient failure reaching USA Lacrosse or sending the email.</summary>
    ServiceUnavailable,
}

public sealed record UsLaxVerifyBeginResult
{
    public required UsLaxVerifyBeginStatus Status { get; init; }
    public string? VerificationId { get; init; }
    /// <summary>Obfuscated on-file address for the UI hint, e.g. <c>j•••@gmail.com</c>.</summary>
    public string? MaskedEmail { get; init; }
    public int ExpiresInSeconds { get; init; }
    public string? Message { get; init; }
}

public sealed record UsLaxVerifyConfirmResult
{
    public required bool Success { get; init; }
    public string? ExpDate { get; init; }
    public string? Message { get; init; }
}

public sealed record UsLaxVerifiedRecord
{
    /// <summary>Padded (12-digit) membership number this verification was bound to.</summary>
    public required string PaddedSportAssnId { get; init; }
    public DateTime? ExpDate { get; init; }
}

public class UsLaxIdentityVerificationService : IUsLaxIdentityVerificationService
{
    private readonly IUsLaxService _usLax;
    private readonly IEmailService _email;
    private readonly IMemoryCache _cache;

    private const string CachePrefix = "uslax:idverify:";
    private static readonly TimeSpan CodeTtl = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 5;
    private const string CoachInvolvement = "Coach";

    public UsLaxIdentityVerificationService(
        IUsLaxService usLax, IEmailService email, IMemoryCache cache)
    {
        _usLax = usLax;
        _email = email;
        _cache = cache;
    }

    public async Task<UsLaxVerifyBeginResult> BeginAsync(string sportAssnId, CancellationToken ct = default)
    {
        var padded = NormalizeMembershipId(sportAssnId);
        if (padded == null)
        {
            return new UsLaxVerifyBeginResult
            {
                Status = UsLaxVerifyBeginStatus.MembershipInvalid,
                Message = "Enter a valid USA Lacrosse number (6–12 digits)."
            };
        }

        UsLaxMemberPingResult? member;
        try
        {
            member = await _usLax.GetMemberAsync(padded, ct);
        }
        catch
        {
            return ServiceUnavailable();
        }

        // Network/parse failure → transient; let the caller retry.
        if (member is null || member.StatusCode == 0)
            return ServiceUnavailable();

        if (!IsActiveCoach(member))
        {
            return new UsLaxVerifyBeginResult
            {
                Status = UsLaxVerifyBeginStatus.MembershipInvalid,
                Message = "This number isn't an active USA Lacrosse coach membership."
            };
        }

        var onFileEmail = member.Output?.Email?.Trim();
        if (string.IsNullOrWhiteSpace(onFileEmail))
        {
            return new UsLaxVerifyBeginResult
            {
                Status = UsLaxVerifyBeginStatus.EmailUnavailable,
                Message = "USA Lacrosse has no email on file for this membership, so we can't "
                        + "verify it automatically. You can continue — a director will verify you."
            };
        }

        var verificationId = Guid.NewGuid().ToString("N");
        var entry = new VerificationEntry
        {
            PaddedSportAssnId = padded,
            OnFileEmail = onFileEmail,
            ExpDate = ParseExpDate(member.Output?.ExpDate),
            Code = GenerateCode(),
            Attempts = 0,
            Verified = false,
        };

        var sent = await SendCodeEmailAsync(onFileEmail, entry.Code, ct);
        if (!sent) return ServiceUnavailable();

        _cache.Set(CachePrefix + verificationId, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CodeTtl
        });

        return new UsLaxVerifyBeginResult
        {
            Status = UsLaxVerifyBeginStatus.Sent,
            VerificationId = verificationId,
            MaskedEmail = MaskEmail(onFileEmail),
            ExpiresInSeconds = (int)CodeTtl.TotalSeconds
        };
    }

    public UsLaxVerifyConfirmResult Confirm(string verificationId, string code)
    {
        if (string.IsNullOrWhiteSpace(verificationId) || string.IsNullOrWhiteSpace(code)
            || !_cache.TryGetValue(CachePrefix + verificationId, out VerificationEntry? entry)
            || entry is null)
        {
            return new UsLaxVerifyConfirmResult { Success = false, Message = "This code has expired. Request a new one." };
        }

        if (entry.Attempts >= MaxAttempts)
        {
            _cache.Remove(CachePrefix + verificationId);
            return new UsLaxVerifyConfirmResult { Success = false, Message = "Too many attempts. Request a new code." };
        }

        if (!FixedTimeEquals(entry.Code, code.Trim()))
        {
            entry.Attempts++;
            // Re-set preserves the original TTL window (no sliding expiration configured).
            _cache.Set(CachePrefix + verificationId, entry, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CodeTtl
            });
            return new UsLaxVerifyConfirmResult { Success = false, Message = "Incorrect code. Try again." };
        }

        entry.Verified = true;
        _cache.Set(CachePrefix + verificationId, entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CodeTtl
        });

        return new UsLaxVerifyConfirmResult
        {
            Success = true,
            ExpDate = entry.ExpDate?.ToString("yyyy-MM-dd")
        };
    }

    public UsLaxVerifiedRecord? Consume(string verificationId, string sportAssnId)
    {
        var padded = NormalizeMembershipId(sportAssnId);
        if (padded == null || string.IsNullOrWhiteSpace(verificationId)) return null;

        if (!_cache.TryGetValue(CachePrefix + verificationId, out VerificationEntry? entry)
            || entry is null || !entry.Verified
            || !string.Equals(entry.PaddedSportAssnId, padded, StringComparison.Ordinal))
        {
            return null;
        }

        // Single-use: a verification proves identity for exactly one registration.
        _cache.Remove(CachePrefix + verificationId);
        return new UsLaxVerifiedRecord { PaddedSportAssnId = entry.PaddedSportAssnId, ExpDate = entry.ExpDate };
    }

    // ── helpers ──

    private static bool IsActiveCoach(UsLaxMemberPingResult member) =>
        member.StatusCode == 200
        && string.Equals(member.Output?.MemStatus, "Active", StringComparison.OrdinalIgnoreCase)
        && member.Output?.Involvement is { } inv
        && inv.Any(i => string.Equals(i, CoachInvolvement, StringComparison.OrdinalIgnoreCase));

    private async Task<bool> SendCodeEmailAsync(string toAddress, string code, CancellationToken ct)
    {
        var message = new EmailMessageDto
        {
            Subject = "Your USA Lacrosse verification code",
            HtmlBody = $"<p>Your coach registration verification code is:</p>"
                     + $"<p style=\"font-size:24px;font-weight:700;letter-spacing:3px\">{code}</p>"
                     + "<p>Enter it on the registration page to confirm your USA Lacrosse membership. "
                     + "This code expires in 10 minutes.</p>"
                     + "<p>If you didn't request this, you can ignore this email.</p>",
            TextBody = $"Your USA Lacrosse coach registration verification code is: {code}. "
                     + "It expires in 10 minutes. If you didn't request this, ignore this email.",
            ToAddresses = { toAddress }
        };
        try
        {
            return await _email.SendAsync(message, cancellationToken: ct);
        }
        catch
        {
            return false;
        }
    }

    private static UsLaxVerifyBeginResult ServiceUnavailable() => new()
    {
        Status = UsLaxVerifyBeginStatus.ServiceUnavailable,
        Message = "We couldn't reach USA Lacrosse just now. Please try again in a moment."
    };

    /// <summary>Trim, validate 6–12 digits, left-pad to 12 (the form USLax records/returns).</summary>
    private static string? NormalizeMembershipId(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length is < 6 or > 12 || !trimmed.All(char.IsDigit)) return null;
        return trimmed.PadLeft(12, '0');
    }

    private static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = System.Text.Encoding.ASCII.GetBytes(a);
        var bb = System.Text.Encoding.ASCII.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static DateTime? ParseExpDate(string? raw) =>
        DateTime.TryParse(raw, out var dt) ? dt : null;

    /// <summary>Mask an address for a UI hint: keep the first char + domain (j•••@gmail.com).</summary>
    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0) return "•••";
        var first = email[0];
        var domain = email[at..];
        return $"{first}•••{domain}";
    }

    private sealed class VerificationEntry
    {
        public required string PaddedSportAssnId { get; init; }
        public required string OnFileEmail { get; init; }
        public DateTime? ExpDate { get; init; }
        public required string Code { get; init; }
        public int Attempts { get; set; }
        public bool Verified { get; set; }
    }
}
