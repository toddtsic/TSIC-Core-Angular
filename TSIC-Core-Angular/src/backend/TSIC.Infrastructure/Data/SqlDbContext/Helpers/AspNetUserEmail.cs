using TSIC.Domain.Entities;

namespace TSIC.Infrastructure.Data.SqlDbContext.Helpers;

/// <summary>
/// Sets an account's email <b>on the SqlDbContext path</b> — the one where nothing normalizes for you.
///
/// It lives in the SqlDbContext helpers, next to the context that does the writing, and it takes the
/// scaffolded <see cref="AspNetUsers"/> entity. It has nothing to do with <c>TsicIdentityDbContext</c>
/// or <c>ApplicationUser</c> and must never be given one.
///
/// ── THE TWO LANES ──
///
///   1. <c>UserManager&lt;ApplicationUser&gt;</c> → <c>TsicIdentityDbContext</c>. Identity's
///      <c>UpperInvariantLookupNormalizer</c> runs inside <c>CreateAsync</c> / <c>UpdateAsync</c> /
///      <c>SetEmailAsync</c> and maintains <c>NormalizedEmail</c> itself. That lane never comes here.
///
///   2. <c>SqlDbContext.AspNetUsers</c> — the scaffolded entity, same <c>dbo.AspNetUsers</c> table.
///      <b>Identity is nowhere in this path.</b> Assign <c>Email</c> and <c>NormalizedEmail</c> keeps
///      its old value forever. This is that lane.
///
/// <c>NormalizedEmail</c> is the column Identity actually searches. <c>FindByEmailAsync</c> — which the
/// anonymous <c>forgot-password</c> and <c>reset-password</c> endpoints both run — looks up the
/// normalized value and never reads <c>Email</c>. So a lane-2 write that sets <c>Email</c> alone leaves
/// the account reachable at the address it USED to have and invisible at the one it has now: the parent
/// whose address an admin just corrected is told "no account with that email" when they try to reset
/// their password.
///
/// That is not hypothetical. It shipped in two methods and was found only because someone asked. Every
/// lane-2 email write goes through here so the two columns cannot be written apart again.
/// </summary>
public static class AspNetUserEmail
{
    /// <summary>
    /// Assigns the email and its lookup key together.
    ///
    /// Blank stores NULL, never <c>""</c>: an empty string is a VALUE, so code testing
    /// <c>Email != null</c> would treat a cleared account as having an address and try to mail it.
    /// NULL is already a normal value in this column.
    ///
    /// The normalization reproduces Identity's default <c>UpperInvariantLookupNormalizer</c> exactly —
    /// <c>email?.Normalize().ToUpperInvariant()</c>. No custom <c>ILookupNormalizer</c> is registered
    /// (see <c>AddIdentity</c> in Program.cs); if one ever is, this is the single place to change.
    /// </summary>
    public static void Set(AspNetUsers user, string? email)
    {
        var value = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

        user.Email = value;
        user.NormalizedEmail = value?.Normalize().ToUpperInvariant();
    }
}
