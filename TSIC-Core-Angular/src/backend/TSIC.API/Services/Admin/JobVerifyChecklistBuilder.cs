using TSIC.Contracts.Dtos.JobClone;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// The modern JobCloneQA (T9-C): builds the type-aware verify-then-release checklist —
/// the cloned job's LIVE settings grouped into sections, ordered by relevance to the
/// job's type. Rendered by the release page before anything goes public. This is the
/// human net for everything no automated check encodes.
/// </summary>
public static class JobVerifyChecklistBuilder
{
    private const string SecRegistration = "Registration";
    private const string SecPayments = "Payments & Fees";
    private const string SecCommunications = "Communications";
    private const string SecDates = "Dates & Expiries";
    private const string SecVisibility = "Visibility & Mobile";
    private const string SecStructure = "Structure (LADT)";
    private const string SecAdmins = "Administrators";

    // Route (relative to the job root) where each section's settings are edited.
    private const string JobSettingsRoute = "configure/job";
    private const string AdministratorsRoute = "configure/administrators";

    /// <summary>
    /// Section order per JobTypeId (1 Club Sport · 2 Tournament · 3 League · 4 Camp ·
    /// 5 Sales Venue · 6 Showcase). Field rules are type-INDEPENDENT — type influence is
    /// presentation only, and this ordering is one of its three homes (with the workbench
    /// form defaults and the release-page persona emphasis).
    /// </summary>
    private static readonly Dictionary<int, string[]> SectionOrderByType = new()
    {
        [1] = [SecRegistration, SecPayments, SecStructure, SecCommunications, SecDates, SecVisibility, SecAdmins],
        [2] = [SecStructure, SecRegistration, SecPayments, SecDates, SecCommunications, SecVisibility, SecAdmins],
        [3] = [SecStructure, SecRegistration, SecPayments, SecCommunications, SecDates, SecVisibility, SecAdmins],
        [4] = [SecRegistration, SecPayments, SecDates, SecCommunications, SecVisibility, SecStructure, SecAdmins],
        [5] = [SecPayments, SecCommunications, SecVisibility, SecDates, SecRegistration, SecStructure, SecAdmins],
        [6] = [SecStructure, SecRegistration, SecPayments, SecDates, SecCommunications, SecVisibility, SecAdmins],
    };

    private static readonly string[] DefaultSectionOrder =
        [SecRegistration, SecPayments, SecCommunications, SecDates, SecVisibility, SecStructure, SecAdmins];

    public static JobVerifyChecklistDto Build(
        Jobs job,
        string? jobTypeName,
        List<string> leagueNames,
        int agegroupCount,
        int divisionCount,
        int teamCount,
        List<Bulletins> bulletins,
        List<ReleasableAdminDto> admins,
        int feeCount,
        string? customerName,
        string? billingTypeName,
        bool customerHasAdnCredentials)
    {
        var sections = new Dictionary<string, List<VerifyItemDto>>
        {
            [SecRegistration] =
            [
                Item("Player registration", OnOff(job.BRegistrationAllowPlayer), JobSettingsRoute),
                Item("Team registration", OnOff(job.BRegistrationAllowTeam), JobSettingsRoute),
                Item("Staff registration", OnOff(job.BRegistrationAllowStaff), JobSettingsRoute),
                Item("Referee registration", OnOff(job.BRegistrationAllowReferee), JobSettingsRoute),
                Item("Recruiter registration", OnOff(job.BRegistrationAllowRecruiter), JobSettingsRoute),
                Item("Player reg requires token", OnOff(job.BplayerRegRequiresToken), JobSettingsRoute),
                Item("Team reg requires token", OnOff(job.BteamRegRequiresToken), JobSettingsRoute),
                Item("Use waitlists", OnOff(job.BUseWaitlists), JobSettingsRoute),
                Item("Restrict player teams to age range", OnOff(job.BRestrictPlayerTeamsToAgerange), JobSettingsRoute),
            ],
            [SecPayments] =
            [
                // Owner first: Jobs.CustomerId decides WHOSE Authorize.Net merchant account
                // collects on this job (credentials live on Customers, resolved job → customer
                // at charge time). On a retargeted clone it is the single most consequential
                // field on the page, and a missing merchant credential means cards simply fail.
                Item("Customer (owner)", customerName ?? "— UNKNOWN —", null),
                Item("Merchant credentials on file",
                    customerHasAdnCredentials ? "yes" : "NO — card payments will fail", null),
                Item("Billing type", billingTypeName ?? job.BillingTypeId.ToString(), null),
                Item("CC processing fee %", job.ProcessingFeePercent?.ToString("0.0#") ?? "—", JobSettingsRoute),
                Item("eCheck processing fee %", job.EcprocessingFeePercent?.ToString("0.0#") ?? "—", JobSettingsRoute),
                Item("eCheck enabled", OnOff(job.BEnableEcheck), JobSettingsRoute),
                Item("Add processing fees", OnOff(job.BAddProcessingFees), JobSettingsRoute),
                Item("Payment methods code", PaymentMethods(job.PaymentMethodsAllowedCode), JobSettingsRoute),
                Item("ARB (recurring billing)", OnOff(job.AdnArb), JobSettingsRoute),
                Item("ARB start date", DateOrDash(job.AdnArbstartDate), JobSettingsRoute),
                Item("Store enabled", OnOff(job.BEnableStore), JobSettingsRoute),
                Item("Fee rows configured", feeCount.ToString(), null),
                Item("Refund policy set", SetOrEmpty(job.PlayerRegRefundPolicy), JobSettingsRoute),
            ],
            [SecCommunications] =
            [
                Item("Reg-form From", job.RegFormFrom ?? "—", JobSettingsRoute),
                Item("Reg-form CCs", job.RegFormCcs ?? "—", JobSettingsRoute),
                Item("Reg-form BCCs", job.RegFormBccs ?? "—", JobSettingsRoute),
                Item("Reschedule email list", job.Rescheduleemaillist ?? "—", JobSettingsRoute),
                Item("Always-copy email list", job.Alwayscopyemaillist ?? "—", JobSettingsRoute),
                Item("Mail-to address", job.MailTo ?? "—", JobSettingsRoute),
                Item("Pay-to name", job.PayTo ?? "—", JobSettingsRoute),
                Item("Player confirmation email", SetOrEmpty(job.PlayerRegConfirmationEmail), JobSettingsRoute),
                Item("Adult confirmation email", SetOrEmpty(job.AdultRegConfirmationEmail), JobSettingsRoute),
                Item("Coach confirmation email", SetOrEmpty(job.CoachRegConfirmationEmail), JobSettingsRoute),
                Item("Referee confirmation email", SetOrEmpty(job.RefereeRegConfirmationEmail), JobSettingsRoute),
                Item("Recruiter confirmation email", SetOrEmpty(job.RecruiterRegConfirmationEmail), JobSettingsRoute),
                Item("Bulletins (all cloned inactive)",
                    $"{bulletins.Count} total, {bulletins.Count(b => b.Active)} active", null),
            ],
            [SecDates] =
            [
                Item("Admin expiry", DateOrDash(job.ExpiryAdmin), JobSettingsRoute),
                Item("User expiry", DateOrDash(job.ExpiryUsers), JobSettingsRoute),
                Item("Event start", DateOrDash(job.EventStartDate), JobSettingsRoute),
                Item("Event end", DateOrDash(job.EventEndDate), JobSettingsRoute),
                Item("USLax numbers valid through", DateOrDash(job.UslaxNumberValidThroughDate), JobSettingsRoute),
                Item("Year / Season", $"{job.Year ?? "—"} / {job.Season ?? "—"}", JobSettingsRoute),
            ],
            [SecVisibility] =
            [
                Item("Site suspended (pre-release)", OnOff(job.BSuspendPublic), null),
                Item("Public schedule access", OnOff(job.BScheduleAllowPublicAccess), JobSettingsRoute),
                Item("Player roster view", OnOff(job.BAllowRosterViewPlayer), JobSettingsRoute),
                Item("Adult roster view", OnOff(job.BAllowRosterViewAdult), JobSettingsRoute),
                Item("Restrict public rosters", OnOff(job.BRestrictPublicRosters), JobSettingsRoute),
                Item("Mobile login", OnOff(job.BAllowMobileLogin), JobSettingsRoute),
                Item("Mobile registration", OnOff(job.BAllowMobileRegn), JobSettingsRoute),
                Item("Mobile RSVP", OnOff(job.BEnableMobileRsvp), JobSettingsRoute),
                Item("Mobile team chat", OnOff(job.BEnableMobileTeamChat), JobSettingsRoute),
                Item("TSIC Teams", OnOff(job.BEnableTsicteams), JobSettingsRoute),
            ],
            [SecStructure] =
            [
                Item("Leagues", leagueNames.Count > 0 ? string.Join("; ", leagueNames) : "none", null),
                Item("Agegroups", agegroupCount.ToString(), null),
                Item("Divisions", divisionCount.ToString(), null),
                Item("Teams", teamCount.ToString(), null),
            ],
            [SecAdmins] =
            [
                Item("Active admins",
                    string.Join("; ", admins.Where(a => a.BActive)
                        .Select(a => $"{a.FirstName} {a.LastName}".Trim())
                        .DefaultIfEmpty("none")), AdministratorsRoute),
                Item("Inactive admins (awaiting release)",
                    admins.Count(a => !a.BActive).ToString(), AdministratorsRoute),
            ],
        };

        var order = SectionOrderByType.TryGetValue(job.JobTypeId, out var o) ? o : DefaultSectionOrder;

        return new JobVerifyChecklistDto
        {
            JobId = job.JobId,
            JobPath = job.JobPath,
            JobName = job.JobName ?? job.JobPath,
            JobTypeId = job.JobTypeId,
            JobTypeName = jobTypeName,
            BSuspendPublic = job.BSuspendPublic,
            RegistrationFlags = new RegistrationFlagsDto
            {
                JobId = job.JobId,
                AllowPlayer = job.BRegistrationAllowPlayer ?? false,
                AllowTeam = job.BRegistrationAllowTeam ?? false,
                AllowStaff = job.BRegistrationAllowStaff ?? false,
                AllowReferee = job.BRegistrationAllowReferee ?? false,
                AllowRecruiter = job.BRegistrationAllowRecruiter ?? false,
            },
            Sections = order
                .Where(sections.ContainsKey)
                .Select(key => new VerifySectionDto { Title = key, Items = sections[key] })
                .ToList(),
        };
    }

    private static VerifyItemDto Item(string label, string value, string? route) =>
        new() { Label = label, Value = value, ConfigureRoute = route };

    private static string OnOff(bool? value) => value == true ? "ON" : "off";

    private static string SetOrEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "EMPTY" : "set";

    private static string DateOrDash(DateTime? value) =>
        value?.ToString("MM/dd/yyyy") ?? "—";

    private static string PaymentMethods(int code) => code switch
    {
        1 => "Credit card only",
        2 => "Credit card or check",
        3 => "Check only",
        _ => $"? ({code})",
    };
}
