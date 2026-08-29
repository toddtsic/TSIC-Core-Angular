using System;
using System.Collections.Generic;
using System.Net;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Shared.TextSubstitution;

/// <summary>
/// The JOB-SCOPED half of the token vocabulary — the values that depend only on the job, never
/// on who is looking. Safe for anonymous, public content (bulletins); the person-scoped tokens
/// (!PERSON, !EMAIL, !AMTOWED, the !F-* blocks) live in TextSubstitutionService and are
/// deliberately unreachable from here.
///
/// Keys carry NO leading '!' — that is the convention BulletinTokenRegistry resolves against
/// (see IBulletinTokenResolver.TokenName). TextSubstitutionService's own dictionary uses the
/// '!' prefix; the two are different channels and do not share a key format.
/// </summary>
public static class JobTokens
{
    /// <summary>
    /// Every token this builder produces. BulletinTokenRegistry cross-checks its resolvers
    /// against this list so a name served by two sources fails loudly instead of one silently
    /// shadowing the other.
    /// </summary>
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "JSEG", "JOBNAME", "JOBCODE", "JOBPATH", "JOBURL", "JOBDESCRIPTION", "JOBLOGO",
        "JOBLINK", "PAYTO", "MAILTO", "CUSTOMERNAME", "SEASON", "SPORT", "YEAR",
        "USLAXVALIDTHROUGHDATE"
    };

    /// <summary>
    /// Build the job token values. Mirrors the job-scoped lines of
    /// TextSubstitutionService.AddSimpleTokens, including its null-handling.
    /// </summary>
    /// <param name="j">Job slice from ITextSubstitutionRepository.LoadJobInvariantFieldsAsync.</param>
    /// <param name="frontendBaseUrl">
    /// FrontendSettings.BaseUrl, already trimmed of a trailing slash. Link tokens must point at
    /// the environment that rendered them, never a hardwired www.
    /// </param>
    public static Dictionary<string, string> Build(JobInvariantFieldsData j, string frontendBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(j);

        var logo = string.IsNullOrEmpty(j.JobLogoHeader)
            ? string.Empty
            : $"<img src='{TsicConstants.BaseUrlStatics}BannerFiles/{j.JobLogoHeader}' alt='Job Logo'>";

        // !JSEG and !JOBPATH both come from the stored JobPath, never from the URL the visitor
        // typed: Angular routes are case-sensitive, so a mis-cased request segment would emit a
        // broken link out of an otherwise correct bulletin.
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["JSEG"] = j.JobPath,
            ["JOBNAME"] = j.JobName,
            ["JOBCODE"] = j.JobCode ?? string.Empty,
            ["JOBPATH"] = j.JobPath,
            ["JOBURL"] = $"{frontendBaseUrl}/{j.JobPath}/home",
            ["JOBDESCRIPTION"] = j.JobDescription ?? string.Empty,
            ["JOBLOGO"] = logo,
            ["JOBLINK"] = $"<a href='{frontendBaseUrl}/{j.JobPath}' target='_blank'>{WebUtility.HtmlEncode(j.JobName)}</a>",
            ["PAYTO"] = j.PayTo ?? string.Empty,
            ["MAILTO"] = j.MailTo ?? string.Empty,
            ["CUSTOMERNAME"] = j.CustomerName ?? string.Empty,
            ["SEASON"] = j.Season ?? string.Empty,
            ["SPORT"] = j.SportName ?? string.Empty,
            ["YEAR"] = j.UslaxNumberValidThroughDate?.Year.ToString() ?? DateTime.Now.Year.ToString(),
            ["USLAXVALIDTHROUGHDATE"] = j.UslaxNumberValidThroughDate?.ToString("d") ?? string.Empty
        };
    }
}
