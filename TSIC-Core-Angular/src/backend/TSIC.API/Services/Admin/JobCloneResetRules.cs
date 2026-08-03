using TSIC.API.Services.Shared.Utilities;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Extensions;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using static TSIC.API.Services.Admin.JobCloneTransforms;

// Alias to disambiguate from TSIC.API.Services.Teams namespace (CS0118).
using TeamsEntity = TSIC.Domain.Entities.Teams;

namespace TSIC.API.Services.Admin;

/// <summary>
/// THE CAREFUL LIST — the versioned reset rules of the copy-everything clone philosophy
/// (Todd-decided 08-02). Every cloned entity is first copied field-for-field by
/// JobCloneEntityCopier (every scalar column, mechanically); then the entity's rules
/// block here applies the ONLY hand-maintained exceptions:
///
///   1. New PKs / remapped FKs (incl. zeroing store-generated identity + rowversion),
///   2. Date shifts by yearDelta (seasonal cadence),
///   3. Year-token bumps under the advance flag (names + long-text content),
///   4. Forced resets (the safe-state list, financial/standings zeroing, lifecycle state).
///
/// Every rule carries a why-comment. A field with NO rule here COPIES — that is the
/// philosophy, not an oversight. The D1 property-snapshot tests fail when an entity
/// gains a column, forcing one look at this file per schema change.
/// </summary>
public static class JobCloneResetRules
{
    // ══════════════════════════════════════════════════════════
    // Jobs
    // ══════════════════════════════════════════════════════════

    public static Jobs CloneJob(
        Jobs source, Guid newJobId, JobCloneRequest req, string userId, DateTime now,
        int yearDelta, IReadOnlyDictionary<Guid, Guid> registrationIdMap)
    {
        var job = JobCloneEntityCopier.CopyScalars(source);
        var advance = req.UpAgegroupNamesByOne;

        // ── New identity + store-generated ──
        job.JobId = newJobId;
        job.JobAi = 0;          // identity — DB assigns
        job.UpdatedOn = null;   // rowversion — DB assigns
        job.Modified = now;
        job.LebUserId = userId;

        // ── Owning customer: EXPLICIT, never inherited ──
        // CopyScalars would carry source.CustomerId silently, and this column is the pointer to
        // whose Authorize.Net merchant account collects (Customers.AdnLoginId/AdnTransactionKey,
        // resolved job → customer at charge time). A same-customer clone makes this a no-op; the
        // point is that the money path stops being an accident of the copier.
        //
        // Jobs.customerID is the ONLY place job ownership is recorded. Registrations.customerID
        // and Leagues.teams.customerID also exist but are NULL on every row in the database
        // (probed 08-02: 0/664,870 and 0/51,050 populated), and Jobs.Job_Customers is empty —
        // do NOT "complete" the retarget by writing them. Nothing else maintains those columns.
        job.CustomerId = req.TargetCustomerId;

        // ── Target identity from the form ──
        job.JobPath = req.JobPathTarget;
        job.JobName = req.JobNameTarget;
        // JobDescription mirrors JobName in practice (DB probe 08-02: 1007/1066 identical;
        // remainder = "_chiuso" closure renames). Confirmed rule: description = new name.
        job.JobDescription = req.JobNameTarget;
        job.Year = req.YearTarget;
        job.Season = req.SeasonTarget;
        job.DisplayName = req.DisplayName;
        job.ExpiryAdmin = req.ExpiryAdmin;
        job.ExpiryUsers = req.ExpiryUsers;

        // ── Communications: all 8 parameters are request fields (workbench pre-fills from
        //    source, operator edits inline). Null = keep source; empty string = clear.
        job.RegFormFrom = req.RegFormFrom ?? source.RegFormFrom;
        job.RegFormCcs = req.RegFormCcs ?? source.RegFormCcs;
        job.RegFormBccs = req.RegFormBccs ?? source.RegFormBccs;
        job.Rescheduleemaillist = req.Rescheduleemaillist ?? source.Rescheduleemaillist;
        job.Alwayscopyemaillist = req.Alwayscopyemaillist ?? source.Alwayscopyemaillist;
        job.MailTo = req.MailTo ?? source.MailTo;
        job.PayTo = req.PayTo ?? source.PayTo;
        job.StoreContactEmail = req.StoreContactEmail ?? source.StoreContactEmail;

        // ── SAFE-STATE RESET LIST (Todd-decided 08-02) ──
        // Principle: access/exposure → OFF, restrictions → ON, behavior/config → copy.
        // The release page's verify-then-release flow deliberately re-opens each of these.
        job.BSuspendPublic = true;                 // hidden until Release panel 2
        job.BRegistrationAllowPlayer = false;      // all five personas closed until
        job.BRegistrationAllowTeam = false;        // Release panel 4 (open-registration)
        job.BRegistrationAllowStaff = false;
        job.BRegistrationAllowReferee = false;
        job.BRegistrationAllowRecruiter = false;
        job.BAllowMobileLogin = false;             // mobile surface off until configured
        job.BAllowMobileRegn = false;
        job.BEnableMobileRsvp = false;
        job.BEnableMobileTeamChat = false;         // TeamMessages is safety-critical — never auto-on
        job.BEnableTsicteams = false;
        job.BScheduleAllowPublicAccess = false;    // no public schedule until real
        job.BAllowRosterViewPlayer = false;        // roster exposure off
        job.BAllowRosterViewAdult = false;
        job.BRestrictPublicRosters = true;         // forced restrictive
        job.BSignalRschedule = false;              // DEAD property — zero out
        // ClubRep edit/delete/add forced ON (lifecycle reset — source may be locked down
        // post-schedule; a new clone starts at the pre-schedule registration phase).
        job.BClubRepAllowEdit = true;
        job.BClubRepAllowDelete = true;
        job.BClubRepAllowAdd = true;

        // ── RETIRED COLUMNS ZEROED (Todd-decided 08-02, = status-quo clone output) ──
        // BTeamsFullPaymentRequired / BPlayersFullPaymentRequired: abandoned as phase
        // sources — payment phase is per-scope in fees.JobFees.BFullPaymentRequired.
        // NOTE: BPlayersFullPaymentRequired is still READ as the PIF allow-gate in
        // PaymentService (RequestedPif validation) — zeroing means a new clone starts
        // with PIF-by-choice off until deliberately enabled.
        job.BTeamsFullPaymentRequired = null;
        job.BPlayersFullPaymentRequired = false;
        // Multi-player discount retired (CR-013, no readers).
        job.PlayerRegMultiPlayerDiscountMin = null;
        job.PlayerRegMultiPlayerDiscountPercent = null;

        // ── Processing rates: source rate floored at the CURRENT new-job rate.
        //    A clone is a new job — it never starts below today's rate (T1: no choices).
        job.ProcessingFeePercent = Math.Max(
            source.ProcessingFeePercent ?? FeeConstants.NewJobProcessingFeePercent,
            FeeConstants.NewJobProcessingFeePercent);
        job.EcprocessingFeePercent = Math.Max(
            source.EcprocessingFeePercent ?? FeeConstants.NewJobEcprocessingFeePercent,
            FeeConstants.NewJobEcprocessingFeePercent);

        // ── Payment methods: an explicit request field, not a CopyScalars inheritance ──
        //    The workbench seeds it from the source, so the resolved value is normally the
        //    source's — but this column decides whether the new site can take money at all
        //    (1 CC only / 2 CC or Check / 3 Check only), and it also gates eCheck: the
        //    registration UI hides eCheck when the code is CC-only. Too consequential to
        //    arrive by omission. Validated to a known code at execute.
        job.PaymentMethodsAllowedCode = req.PaymentMethodsAllowedCode;

        // ── eCheck / Store wizard choices ──
        job.BEnableEcheck =
            string.Equals(req.EnableEcheckChoice, "source", StringComparison.OrdinalIgnoreCase)
            && source.BEnableEcheck;
        var enableStore =
            string.Equals(req.StoreChoice, "keep", StringComparison.OrdinalIgnoreCase)
            ? source.BEnableStore
            : false;
        job.BEnableStore = enableStore;
        // Walk-up is a store sub-feature — follows the resolved store state so a
        // "disable store" clone can't leak walk-up back on.
        job.BAllowStoreWalkup = enableStore == true && source.BAllowStoreWalkup;

        // ── Home-page banner: keep Jobs.bBannerIsCustom in step with the real switch ──
        // The switch is JobDisplayOptions.parallaxSlideCount, not this column: both read
        // paths derive the flag from the count (JobRepository.GetJobMetadataByPathAsync,
        // JobConfigService.MapBranding), and nothing reads Jobs.bBannerIsCustom. Copied
        // blind it would land TRUE against a zeroed count — inert today, a lie the moment
        // anyone wires a read to it. See CloneDisplayOptions for the count side.
        if (req.NoParallaxSlide1) job.BBannerIsCustom = false;

        // ── Date fields shift by yearDelta (seasonal cadence; AddYears clamps Feb-29) ──
        job.EventStartDate = ShiftByYears(source.EventStartDate, yearDelta);
        job.EventEndDate = ShiftByYears(source.EventEndDate, yearDelta);
        job.AdnArbstartDate = ShiftByYears(source.AdnArbstartDate, yearDelta);
        job.AdnStartDateAfterTrial = ShiftByYears(source.AdnStartDateAfterTrial, yearDelta);
        // Legacy shifted this +1yr on every clone; generalized to yearDelta.
        job.UslaxNumberValidThroughDate = ShiftByYears(source.UslaxNumberValidThroughDate, yearDelta);

        // ── PrimaryContactRegistrationId: REMAPPED to the same person's NEW registration
        //    (Todd-decided 08-02 — the primary contact must carry over). Null only when the
        //    source has none or the source registration didn't clone (non-admin role).
        job.PrimaryContactRegistrationId =
            source.PrimaryContactRegistrationId.HasValue
            && registrationIdMap.TryGetValue(source.PrimaryContactRegistrationId.Value, out var newRegId)
                ? newRegId
                : null;

        // ── Year-token bump across long-text content (advance flag; Todd-decided 08-02).
        //    Confirmation emails/on-screens ×5 personas, waivers, refund policies, store
        //    texts, mail-in warning. JobNameQbp: QuickBooks IIF character-limit alias —
        //    if the source needed the short name the clone will too, so it copies and
        //    bumps like content (supersedes legacy's null).
        if (advance)
        {
            job.PlayerRegConfirmationEmail = BumpYears(source.PlayerRegConfirmationEmail);
            job.PlayerRegConfirmationOnScreen = BumpYears(source.PlayerRegConfirmationOnScreen);
            job.AdultRegConfirmationEmail = BumpYears(source.AdultRegConfirmationEmail);
            job.AdultRegConfirmationOnScreen = BumpYears(source.AdultRegConfirmationOnScreen);
            job.CoachRegConfirmationEmail = BumpYears(source.CoachRegConfirmationEmail);
            job.CoachRegConfirmationOnScreen = BumpYears(source.CoachRegConfirmationOnScreen);
            job.RefereeRegConfirmationEmail = BumpYears(source.RefereeRegConfirmationEmail);
            job.RefereeRegConfirmationOnScreen = BumpYears(source.RefereeRegConfirmationOnScreen);
            job.RecruiterRegConfirmationEmail = BumpYears(source.RecruiterRegConfirmationEmail);
            job.RecruiterRegConfirmationOnScreen = BumpYears(source.RecruiterRegConfirmationOnScreen);
            job.PlayerRegReleaseOfLiability = BumpYears(source.PlayerRegReleaseOfLiability);
            job.PlayerRegCodeOfConduct = BumpYears(source.PlayerRegCodeOfConduct);
            job.AdultRegReleaseOfLiability = BumpYears(source.AdultRegReleaseOfLiability);
            job.AdultRegCodeOfConduct = BumpYears(source.AdultRegCodeOfConduct);
            job.PlayerRegCovid19Waiver = BumpYears(source.PlayerRegCovid19Waiver);
            job.PlayerRegRefundPolicy = BumpYears(source.PlayerRegRefundPolicy);
            job.StoreRefundPolicy = BumpYears(source.StoreRefundPolicy);
            job.StorePickupDetails = BumpYears(source.StorePickupDetails);
            job.MailinPaymentWarning = BumpYears(source.MailinPaymentWarning);
            job.JobNameQbp = BumpYears(source.JobNameQbp);
        }

        // Everything else — behavior/config flags (BTeamPushDirectors, processing/donation/
        // insurance/waitlist/refund-window/credit flags, reg tokens, BRestrictPlayerTeamsToAgerange,
        // BShowTeamNameOnlyInSchedules, BBannerIsCustom, BReseedTournament,
        // BDisallowCcplayerConfirmations, BenableStp), branding, metadata JSON, billing
        // config, labels — COPIES via the copier. That is the philosophy.
        return job;
    }

    // ══════════════════════════════════════════════════════════
    // JobDisplayOptions (1:1, PK = JobId)
    // ══════════════════════════════════════════════════════════

    public static JobDisplayOptions CloneDisplayOptions(
        JobDisplayOptions source, Guid newJobId, JobCloneRequest req, string userId, DateTime now)
    {
        var d = JobCloneEntityCopier.CopyScalars(source);
        d.JobId = newJobId;
        d.Modified = now;
        d.LebUserId = userId;

        // ALL parallax slide texts year-bump under the advance flag (Todd-decided 08-02 —
        // extends the old slide-1-only source-year replace). Image filename strings copy:
        // they are shared aliases until re-upload, by design.
        if (req.UpAgegroupNamesByOne)
        {
            d.ParallaxSlide1Text1 = BumpYears(source.ParallaxSlide1Text1);
            d.ParallaxSlide1Text2 = BumpYears(source.ParallaxSlide1Text2);
            d.ParallaxSlide2Text1 = BumpYears(source.ParallaxSlide2Text1);
            d.ParallaxSlide2Text2 = BumpYears(source.ParallaxSlide2Text2);
            d.ParallaxSlide3Text1 = BumpYears(source.ParallaxSlide3Text1);
            d.ParallaxSlide3Text2 = BumpYears(source.ParallaxSlide3Text2);
        }

        // Author's own wording wins over the source's, bumped or not. Applied AFTER the bump so
        // the text typed in the workbench lands verbatim — it was seeded from the bumped value,
        // and bumping it again would advance the years twice. Null = "keep what the source gave".
        // Stored through the same converter the Branding tab uses, so a clone and a hand-edit
        // put the identical byte sequence in the column.
        if (req.BannerText1Target is not null)
            d.ParallaxSlide1Text1 = OverlayText.ToStoredHtml(req.BannerText1Target);
        if (req.BannerText2Target is not null)
            d.ParallaxSlide1Text2 = OverlayText.ToStoredHtml(req.BannerText2Target);

        // NoParallaxSlide1 = "open on a plain banner". parallaxSlideCount is the ONLY
        // custom-banner switch the homesite reads, so zeroing it drops client-banner to
        // its text-only branch (jobName as the headline).
        //
        // The overlay TEXT must go with it. client-banner.component.html paints
        // jobBannerText2 (= ParallaxSlide1Text2) as the subtitle even on that plain
        // branch — so before this, last season's caption stayed live on the new job's
        // public home page with the option ON. Text1 is dormant there but returns the
        // instant a director re-enables the custom banner, carrying the old season with
        // it. Both are cleared.
        //
        // ParallaxBackgroundImage is deliberately KEPT: the backdrop is usually a generic
        // sport photo worth reusing, and it renders nothing while the count is 0. The
        // slide-1 overlay is the season-branded artwork, so that one is dropped.
        if (req.NoParallaxSlide1)
        {
            d.ParallaxSlideCount = 0;
            d.ParallaxSlide1Image = null;
            d.ParallaxSlide1Text1 = null;
            d.ParallaxSlide1Text2 = null;
        }

        return d;
    }

    // ══════════════════════════════════════════════════════════
    // JobOwlImages (1:1, PK = JobId)
    // ══════════════════════════════════════════════════════════

    public static JobOwlImages CloneOwlImages(
        JobOwlImages source, Guid newJobId, string userId, DateTime now)
    {
        var o = JobCloneEntityCopier.CopyScalars(source);
        o.JobId = newJobId;
        o.Modified = now;
        o.LebUserId = userId;
        return o;
    }

    // ══════════════════════════════════════════════════════════
    // Bulletins
    // ══════════════════════════════════════════════════════════

    public static List<Bulletins> CloneBulletins(
        List<Bulletins> sources, Guid newJobId, bool advance, string userId, DateTime now, int yearDelta)
    {
        return sources.Select(src =>
        {
            var b = JobCloneEntityCopier.CopyScalars(src);
            b.BulletinId = Guid.NewGuid();
            b.JobId = newJobId;
            b.Modified = now;
            b.LebUserId = userId;

            // FORCED INACTIVE (Todd-decided 08-02, adopts legacy's 8/2024 rule): cloned
            // bulletins arrive dark; the verify page lists them for deliberate re-activation.
            b.Active = false;

            // Seasonal cadence preserved via yearDelta shift.
            b.CreateDate = ShiftByYears(src.CreateDate, yearDelta);
            b.StartDate = ShiftByYears(src.StartDate, yearDelta);
            b.EndDate = ShiftByYears(src.EndDate, yearDelta);

            // Title/Text year-bump under advance flag — re-activation becomes one review,
            // not a rewrite.
            if (advance)
            {
                b.Title = BumpYears(src.Title);
                b.Text = BumpYears(src.Text);
            }

            return b;
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // JobAgeRanges
    // ══════════════════════════════════════════════════════════

    public static List<JobAgeRanges> CloneAgeRanges(
        List<JobAgeRanges> sources, Guid newJobId, bool advance, string userId, DateTime now)
    {
        return sources.Select(src =>
        {
            var r = JobCloneEntityCopier.CopyScalars(src);
            r.AgeRangeId = 0;   // identity — DB assigns
            r.JobId = newJobId;
            r.Modified = now;
            r.LebUserId = userId;

            // Range bounds are DOB boundary dates — advance shifts them +1yr in lockstep
            // with agegroup DOB windows; name bumps its year tokens ("2014-2015 Boys").
            if (advance)
            {
                r.RangeLeft = ShiftByYears(src.RangeLeft, 1);
                r.RangeRight = ShiftByYears(src.RangeRight, 1);
                r.RangeName = BumpYears(src.RangeName);
            }

            return r;
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // JobMenus + JobMenuItems (parent/child Guid remap)
    // ══════════════════════════════════════════════════════════

    public static (List<JobMenus> Menus, List<JobMenuItems> Items) CloneMenus(
        List<JobMenus> sourceMenus, Guid newJobId, string userId, DateTime now)
    {
        var menus = new List<JobMenus>();
        var items = new List<JobMenuItems>();

        foreach (var srcMenu in sourceMenus)
        {
            var m = JobCloneEntityCopier.CopyScalars(srcMenu);
            m.MenuId = Guid.NewGuid();
            m.JobId = newJobId;
            m.Modified = now;
            m.LebUserId = userId;
            menus.Add(m);

            // Two-pass parent remap: mint IDs for every item first, then wire parents.
            var idMap = srcMenu.JobMenuItems.ToDictionary(i => i.MenuItemId, _ => Guid.NewGuid());
            foreach (var srcItem in srcMenu.JobMenuItems)
            {
                var it = JobCloneEntityCopier.CopyScalars(srcItem);
                it.MenuItemId = idMap[srcItem.MenuItemId];
                it.MenuId = m.MenuId;
                it.ParentMenuItemId =
                    srcItem.ParentMenuItemId.HasValue
                    && idMap.TryGetValue(srcItem.ParentMenuItemId.Value, out var newParent)
                        ? newParent
                        : null;
                it.Modified = now;
                it.LebUserId = userId;
                items.Add(it);
            }
        }

        return (menus, items);
    }

    // ══════════════════════════════════════════════════════════
    // reporting.JobReports (flat)
    // ══════════════════════════════════════════════════════════

    public static List<JobReports> CloneJobReports(
        List<JobReports> sources, Guid newJobId, string userId, DateTime now)
    {
        return sources.Select(src =>
        {
            var r = JobCloneEntityCopier.CopyScalars(src);
            r.JobReportId = Guid.NewGuid();
            r.JobId = newJobId;
            r.Modified = now;
            r.LebUserId = userId;
            return r;
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // nav.Nav + nav.NavItem (identity-int PKs — EF navigation fixup)
    // ══════════════════════════════════════════════════════════

    public static Nav CloneNav(Nav source, Guid newJobId, string userId, DateTime now)
    {
        var nav = JobCloneEntityCopier.CopyScalars(source);
        nav.NavId = 0;          // identity — DB assigns
        nav.JobId = newJobId;
        nav.Modified = now;
        nav.ModifiedBy = userId;

        // Identity-int FKs can't be pre-minted: attach children via the navigation
        // property and wire parents via ParentNavItem so EF resolves NavId/NavItemId/
        // ParentNavItemId at SaveChanges. DefaultNavItemId/DefaultParentNavItemId point
        // at the shared default nav (JobId IS NULL) — copied verbatim, never remapped.
        var itemMap = new Dictionary<int, NavItem>();
        foreach (var srcItem in source.NavItem)
        {
            var it = JobCloneEntityCopier.CopyScalars(srcItem);
            it.NavItemId = 0;           // identity — DB assigns
            it.NavId = 0;               // resolved via the Nav navigation
            it.ParentNavItemId = null;  // resolved via ParentNavItem in pass 2
            it.Nav = nav;
            it.Modified = now;
            it.ModifiedBy = userId;
            itemMap[srcItem.NavItemId] = it;
            nav.NavItem.Add(it);
        }

        foreach (var srcItem in source.NavItem)
        {
            if (srcItem.ParentNavItemId.HasValue
                && itemMap.TryGetValue(srcItem.ParentNavItemId.Value, out var parent))
            {
                itemMap[srcItem.NavItemId].ParentNavItem = parent;
            }
        }

        return nav;
    }

    // ══════════════════════════════════════════════════════════
    // Admin Registrations (Superuser / SuperDirector / Director rows only)
    // ══════════════════════════════════════════════════════════

    public static List<Registrations> CloneAdminRegistrations(
        List<Registrations> sources, Guid newJobId, string userId, DateTime now,
        int yearDelta, Dictionary<Guid, Guid> registrationIdMap)
    {
        return sources.Select(src =>
        {
            var r = JobCloneEntityCopier.CopyScalars(src);
            var newId = Guid.NewGuid();
            registrationIdMap[src.RegistrationId] = newId;

            r.RegistrationId = newId;
            r.RegistrationAi = 0;   // identity — DB assigns
            r.JobId = newJobId;
            r.Modified = now;
            r.LebUserId = userId;

            // Director + SuperDirector land INACTIVE until Release panel 3 activates them;
            // Superuser regs stay as-is (TSIC-central, must remain functional for QA work).
            var isCustomerAdmin =
                string.Equals(src.RoleId, RoleConstants.Director, StringComparison.OrdinalIgnoreCase)
                || string.Equals(src.RoleId, RoleConstants.SuperDirector, StringComparison.OrdinalIgnoreCase);
            if (isCustomerAdmin) r.BActive = false;

            // RegistrationTs = source + yearDelta (Todd-decided 08-02): preserves the
            // earliest-registered ordering, which is the DEFAULT-ADMINISTRATOR fallback
            // (UserPrivilegeLevelService, AdministratorRepository + 5 more OrderBy(RegistrationTs)
            // consumers). The old "= now" behavior was a bug — it made clone order, not
            // tenure, pick the default administrator.
            r.RegistrationTs = ShiftByYears(src.RegistrationTs, yearDelta);

            r.BConfirmationSent = false;    // no confirmation has been sent on the new job

            // Financial state — fresh slate, via the single writer.
            r.FeeBase = 0; r.FeeProcessing = 0; r.FeeDiscount = 0; r.FeeDiscountMp = 0;
            r.FeeDonation = 0; r.FeeLatefee = 0; r.PaidTotal = 0;
            r.PaymentMethodChosen = null;
            r.DiscountCodeId = null;        // job-scoped code — source's doesn't exist here
            r.RecalcTotals();

            // Cross-job pointers → null. These reference SOURCE-job LADT / forms / billing;
            // carrying them would point the new registration into the old job's graph.
            r.AssignedTeamId = null;
            r.AssignedAgegroupId = null;
            r.AssignedDivId = null;
            r.AssignedLeagueId = null;
            r.RequestedAgegroupId = null;
            r.RegformId = null;             // RegForms are job-scoped (and dead in new stack)
            r.AdnSubscriptionId = null;     // ARB subscription state never carries forward
            r.AdnSubscriptionStatus = null;
            r.AdnSubscriptionStartDate = null;
            r.AdnSubscriptionBillingOccurences = null;
            r.AdnSubscriptionAmountPerOccurence = null;
            r.AdnSubscriptionIntervalLength = null;
            r.RegsaverPolicyId = null;      // per-season insurance policy
            r.RegsaverPolicyIdCreateDate = null;

            return r;
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // Leagues (one per source JobLeagues row — T3 multi-league walk)
    // ══════════════════════════════════════════════════════════

    public static Leagues CloneLeague(
        Leagues source, Guid newLeagueId, string nameTarget, string userId, DateTime now)
    {
        var l = JobCloneEntityCopier.CopyScalars(source);
        l.LeagueId = newLeagueId;
        l.LeagueName = nameTarget;  // author-entered per-league rename row
        l.Modified = now;
        l.LebUserId = userId;
        // Everything else copies — incl. PlayerFeeOverride (live flag), scoring/standings
        // config, StrLop/StrGradYears.
        return l;
    }

    public static JobLeagues CloneJobLeague(
        JobLeagues source, Guid newJobId, Guid newLeagueId, string userId, DateTime now)
    {
        var jl = JobCloneEntityCopier.CopyScalars(source);
        jl.JobLeagueId = Guid.NewGuid();
        jl.JobId = newJobId;
        jl.LeagueId = newLeagueId;
        jl.Modified = now;
        jl.LebUserId = userId;
        // BIsPrimary copies — preserved per T3.

        // Legacy per-league fee columns ZEROED: both stacks have always minted JobLeagues
        // with these null (legacy inserted only the 4 core columns); league fee tiers live
        // role-scoped in fees.JobFees.LeagueId.
        jl.BaseFee = null;
        jl.LateFee = null;
        jl.LateFeeStart = null;
        jl.LateFeeEnd = null;
        jl.DiscountFee = null;
        jl.DiscountFeeStart = null;
        jl.DiscountFeeEnd = null;

        return jl;
    }

    // ══════════════════════════════════════════════════════════
    // Agegroups
    // ══════════════════════════════════════════════════════════

    public static List<Agegroups> CloneAgegroups(
        List<Agegroups> sources, Guid newLeagueId, JobCloneRequest req,
        string userId, DateTime now, Dictionary<Guid, Guid> agegroupIdMap)
    {
        return sources.Select(src =>
        {
            var ag = JobCloneEntityCopier.CopyScalars(src);
            var newId = Guid.NewGuid();
            agegroupIdMap[src.AgegroupId] = newId;

            ag.AgegroupId = newId;
            ag.LeagueId = newLeagueId;
            ag.Season = req.SeasonTarget;
            ag.Modified = now;
            ag.LebUserId = userId;

            // Advance: name year-bump, GradYear ±1, DOB ±1yr in lockstep (a name that
            // advances while DOB stays put is a silent age-mismatch).
            if (req.UpAgegroupNamesByOne)
            {
                ag.AgegroupName = BumpYears(src.AgegroupName);
                if (src.GradYearMin.HasValue) ag.GradYearMin = src.GradYearMin.Value + 1;
                if (src.GradYearMax.HasValue) ag.GradYearMax = src.GradYearMax.Value + 1;
                ag.DobMin = ShiftByYears(src.DobMin, 1);
                ag.DobMax = ShiftByYears(src.DobMax, 1);
            }

            // PRIVACY HARD RULE — never type-tuned, never inherited: Third-Party Roster
            // Access feeds a minors'-PII export; every new event opts in per agegroup
            // deliberately.
            ag.BAllowApiRosterAccess = false;

            // LEGACY AGEGROUP FEE COLUMNS ZEROED (Todd-decided 08-02, = status-quo):
            // no readers outside scaffolding — fees live in fees.JobFees/FeeModifiers.
            // PlayerFeeOverride is NOT in this list (live flag — copies).
            ag.TeamFee = null;
            ag.TeamFeeLabel = null;
            ag.RosterFee = null;
            ag.RosterFeeLabel = null;
            ag.LateFee = null;
            ag.LateFeeStart = null;
            ag.LateFeeEnd = null;
            ag.DiscountFee = null;
            ag.DiscountFeeStart = null;
            ag.DiscountFeeEnd = null;

            return ag;
        }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // Divisions
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Divisions for the cloned agegroups, in the same two modes the LADT agegroup-clone uses
    /// (<c>CloneAgegroupRequest.CopyDivisions</c>, LadtService):
    ///
    ///   copyDivisions = true  → carry the source's pools (Gold/Silver/Pool A…) verbatim.
    ///   copyDivisions = false → "clone the shape, not last year's pools": no pools carry, and
    ///                           every cloned agegroup gets the single Unassigned holding division.
    ///
    /// INVARIANT either way: every cloned agegroup ends with an Unassigned division. That is the
    /// system-wide rule (DivisionConstants.Unassigned — "the holding division every agegroup must
    /// have", auto-created by CreateAgegroupAsync and by team placement), and job clone previously
    /// broke it whenever a source agegroup had no Unassigned row of its own.
    ///
    /// In fresh-pool mode EVERY source division of an agegroup maps to that agegroup's new
    /// Unassigned row, so cloned structure teams land in the holding pool rather than dangling on
    /// a division that no longer exists (teams.divID is non-null on all 51,050 rows).
    /// </summary>
    /// <param name="newAgegroupIdsInScope">
    /// The NEW agegroup ids this call is responsible for — the current league's, not every
    /// agegroup mapped so far. The executor calls this once per league against a shared
    /// agegroupIdMap, so seeding from the whole map would re-mint earlier leagues' holding rows.
    /// </param>
    public static List<Divisions> CloneDivisions(
        List<Divisions> sources, IReadOnlyDictionary<Guid, Guid> agegroupIdMap,
        IEnumerable<Guid> newAgegroupIdsInScope,
        Dictionary<Guid, Guid> divisionIdMap, bool copyDivisions, string userId, DateTime now)
    {
        var cloned = new List<Divisions>();
        var eligible = sources.Where(d => agegroupIdMap.ContainsKey(d.AgegroupId)).ToList();

        // New-agegroup id → its Unassigned row, minted on demand below.
        var unassignedByNewAgegroup = new Dictionary<Guid, Guid>();

        Guid UnassignedFor(Guid newAgegroupId)
        {
            if (unassignedByNewAgegroup.TryGetValue(newAgegroupId, out var existing)) return existing;
            var newId = Guid.NewGuid();
            cloned.Add(new Divisions
            {
                DivId = newId,
                AgegroupId = newAgegroupId,
                DivName = DivisionConstants.Unassigned,
                LebUserId = userId,
                Modified = now,
            });
            unassignedByNewAgegroup[newAgegroupId] = newId;
            return newId;
        }

        if (copyDivisions)
        {
            foreach (var src in eligible)
            {
                var newAgegroupId = agegroupIdMap[src.AgegroupId];
                if (string.Equals(src.DivName, DivisionConstants.Unassigned, StringComparison.OrdinalIgnoreCase))
                {
                    // Route the source's own Unassigned through the same mint so an agegroup
                    // never ends up with two of them.
                    divisionIdMap[src.DivId] = UnassignedFor(newAgegroupId);
                    continue;
                }

                var d = JobCloneEntityCopier.CopyScalars(src);
                var newId = Guid.NewGuid();
                divisionIdMap[src.DivId] = newId;
                d.DivId = newId;
                d.AgegroupId = newAgegroupId;
                d.Modified = now;
                d.LebUserId = userId;
                cloned.Add(d);
            }
        }
        else
        {
            foreach (var src in eligible)
                divisionIdMap[src.DivId] = UnassignedFor(agegroupIdMap[src.AgegroupId]);
        }

        // Agegroups whose source had no divisions at all still need the holding row.
        foreach (var newAgegroupId in newAgegroupIdsInScope)
            UnassignedFor(newAgegroupId);

        return cloned;
    }

    // ══════════════════════════════════════════════════════════
    // Teams (structure teams only — eligibility decided by the planner)
    // ══════════════════════════════════════════════════════════

    public static List<TeamsEntity> CloneTeams(
        IEnumerable<TeamsEntity> sources, Guid newJobId, JobCloneRequest req,
        string userId, DateTime now, int yearDelta,
        IReadOnlyDictionary<Guid, Guid> leagueIdMap,
        IReadOnlyDictionary<Guid, Guid> agegroupIdMap,
        IReadOnlyDictionary<Guid, Guid> divisionIdMap,
        Dictionary<Guid, Guid> teamIdMap)
    {
        var advance = req.UpAgegroupNamesByOne;
        var cloned = new List<TeamsEntity>();

        foreach (var src in sources)
        {
            // Planner guarantees these maps resolve for eligible teams; the guards are
            // structural safety only.
            if (!agegroupIdMap.TryGetValue(src.AgegroupId, out var newAgegroupId)) continue;
            if (!leagueIdMap.TryGetValue(src.LeagueId, out var newLeagueId)) continue;

            var t = JobCloneEntityCopier.CopyScalars(src);
            var newTeamId = Guid.NewGuid();
            teamIdMap[src.TeamId] = newTeamId;

            t.TeamId = newTeamId;
            t.TeamAi = 0;   // identity — DB assigns
            t.JobId = newJobId;
            t.LeagueId = newLeagueId;
            t.AgegroupId = newAgegroupId;
            t.DivId = src.DivId.HasValue && divisionIdMap.TryGetValue(src.DivId.Value, out var newDiv)
                ? newDiv : null;
            t.Year = req.YearTarget;
            t.Season = req.SeasonTarget;
            t.Createdate = now;
            t.Modified = now;
            t.LebUserId = userId;

            // Team NAME year-bump (Todd-decided 08-02 — fixes a gap in both stacks):
            // names advance with the advance flag; no year token → untouched. LADT mapping
            // is by ID maps, never names — renames cannot break placement.
            if (advance)
            {
                t.TeamName = BumpYears(src.TeamName);
                t.TeamFullName = BumpYears(src.TeamFullName);
            }

            // ── Date windows shift by yearDelta ──
            t.Effectiveasofdate = ShiftByYears(src.Effectiveasofdate, yearDelta);
            t.Expireondate = ShiftByYears(src.Expireondate, yearDelta);
            t.Startdate = ShiftByYears(src.Startdate, yearDelta);
            t.Enddate = ShiftByYears(src.Enddate, yearDelta);
            t.LateFeeStart = ShiftByYears(src.LateFeeStart, yearDelta);
            t.LateFeeEnd = ShiftByYears(src.LateFeeEnd, yearDelta);
            t.DiscountFeeStart = ShiftByYears(src.DiscountFeeStart, yearDelta);
            t.DiscountFeeEnd = ShiftByYears(src.DiscountFeeEnd, yearDelta);

            // ── DOB / grad windows advance ±1 with the flag (lockstep with agegroups) ──
            if (advance)
            {
                t.DobMin = ShiftByYears(src.DobMin, 1);
                t.DobMax = ShiftByYears(src.DobMax, 1);
                if (src.GradYearMin.HasValue) t.GradYearMin = src.GradYearMin.Value + 1;
                if (src.GradYearMax.HasValue) t.GradYearMax = src.GradYearMax.Value + 1;
            }

            // ── ClubRep — never carries. Even empty-clubname exception teams arrive as
            //    ownerless structure (matches ISP 2026-27 observed state).
            t.ClubrepId = null;
            t.ClubrepRegistrationid = null;

            // ── Financial state — fresh slate; totals via the single writer ──
            t.FeeBase = 0m; t.FeeProcessing = 0m; t.FeeDiscount = 0m; t.FeeDiscountMp = 0m;
            t.FeeDonation = 0m; t.FeeLatefee = 0m; t.PaidTotal = 0m;
            t.DiscountCodeId = null;    // job-scoped code
            t.RecalcTotals();

            // ── Standings — new season starts 0-0; last-season record NOT carried
            //    (Todd-decided 08-02) ──
            t.Games = 0; t.Wins = 0; t.Losses = 0; t.Ties = 0; t.Points = 0;
            t.GoalsFor = 0; t.GoalsVs = 0; t.GoalDiff9 = 0;
            t.StandingsRank = null;
            t.LastLeagueRecord = null;
            t.DivRank = 0;              // per-season seeding artifact
            t.TbKey = null;             // tiebreaker key — per-season artifact

            // ── ADN subscription state — never carries forward ──
            t.AdnSubscriptionId = null;
            t.AdnSubscriptionStatus = null;
            t.AdnSubscriptionStartDate = null;
            t.AdnSubscriptionBillingOccurences = null;
            t.AdnSubscriptionAmountPerOccurence = null;
            t.AdnSubscriptionIntervalLength = null;

            // ── Insurance policy — per-season, never carries ──
            t.ViPolicyId = null;
            t.ViPolicyClubRepRegId = null;
            t.ViPolicyCreateDate = null;

            // ── Cross-job / per-season pointers → null ──
            t.FieldId1 = null; t.FieldId2 = null; t.FieldId3 = null;  // rescheduled per season
            t.ClubTeamId = null;        // club-library link re-established on re-registration
            t.LeagueTeamId = null;      // per-season league linkage
            t.KeywordPairs = null;      // per-season scheduling keywords
            t.Requests = null;          // per-season scheduling requests

            // ── Lineage — richer than legacy's nulls (deliberate keep) ──
            t.BnewTeam = true;
            t.BnewCoach = true;
            t.PrevTeamId = src.TeamId;
            t.LastSeasonYear = src.Year;
            t.OldCoach = null;
            t.OldTeamName = null;
            t.NoReturningPlayers = null;

            // DisplayName / BDoNotValidateUslaxNumber / NationalRankingData / identity
            // fields (gender, color, comments, LOP, DOW, requested placement) — COPY.
            cloned.Add(t);
        }

        return cloned;
    }

    // ══════════════════════════════════════════════════════════
    // fees.JobFees + fees.FeeModifiers
    // ══════════════════════════════════════════════════════════

    public static JobFees CloneJobFee(
        JobFees source, Guid newFeeId, Guid newJobId,
        Guid? newAgegroupId, Guid? newTeamId, Guid? newLeagueId,
        string userId, DateTime now)
    {
        var f = JobCloneEntityCopier.CopyScalars(source);
        f.JobFeeId = newFeeId;
        f.JobId = newJobId;
        f.AgegroupId = newAgegroupId;
        f.TeamId = newTeamId;
        f.LeagueId = newLeagueId;
        f.Modified = now;
        f.LebUserId = userId;

        // PHASE IS DERIVED FROM FEE SHAPE, never copied (Todd-decided 08-02): payment
        // phase is lifecycle state and a clone starts at the lifecycle start. Deposit
        // configured → deposit phase (false); no deposit → full-pay from day one (true),
        // because a deposit phase is meaningless without a deposit.
        f.BFullPaymentRequired = (source.Deposit ?? 0m) == 0m;

        return f;
    }

    public static FeeModifiers CloneFeeModifier(
        FeeModifiers source, Guid newJobFeeId, string userId, DateTime now, int yearDelta)
    {
        var m = JobCloneEntityCopier.CopyScalars(source);
        m.FeeModifierId = Guid.NewGuid();
        m.JobFeeId = newJobFeeId;
        m.Modified = now;
        m.LebUserId = userId;
        // Early-bird / late-fee windows keep their seasonal cadence.
        m.StartDate = ShiftByYears(source.StartDate, yearDelta);
        m.EndDate = ShiftByYears(source.EndDate, yearDelta);
        return m;
    }
}
