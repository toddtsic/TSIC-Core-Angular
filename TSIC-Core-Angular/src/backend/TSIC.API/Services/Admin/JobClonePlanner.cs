using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TSIC.API.Services.Shared.Utilities;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using static TSIC.API.Services.Admin.JobCloneTransforms;

namespace TSIC.API.Services.Admin;

/// <summary>
/// ONE PLAN, TWO CONSUMERS: preview renders the plan; execute re-builds it inside the
/// clone transaction (killing TOCTOU) and materializes exactly the rows the plan counted.
/// Preview/clone parity is structural — both consume this planner's eligibility output,
/// so the counts cannot diverge.
/// </summary>
public sealed class JobClonePlanner
{
    private readonly IJobCloneRepository _repo;
    private readonly IFeeRepository _feeRepo;

    public JobClonePlanner(IJobCloneRepository repo, IFeeRepository feeRepo)
    {
        _repo = repo;
        _feeRepo = feeRepo;
    }

    // ── Validation vocabulary ──

    private static readonly HashSet<string> ValidLadtScopes =
        new(StringComparer.OrdinalIgnoreCase) { "none", "lad", "ladt" };
    private static readonly HashSet<string> ValidEnableEcheckChoices =
        new(StringComparer.OrdinalIgnoreCase) { "off", "source" };
    private static readonly HashSet<string> ValidStoreChoices =
        new(StringComparer.OrdinalIgnoreCase) { "keep", "disable" };

    /// <summary>
    /// jobPath is a URL segment. DB reality (probed 08-02): 1066/1066 existing paths are
    /// letters/digits/hyphens, mixed case — so the rule matches reality rather than
    /// forcing lowercase.
    /// </summary>
    public static readonly Regex JobPathSlugRegex =
        new(@"^[A-Za-z0-9][A-Za-z0-9-]*$", RegexOptions.Compiled);

    public const int MaxJobPathLength = 80;

    private static void ValidateChoices(JobCloneRequest req)
    {
        if (!ValidLadtScopes.Contains(req.LadtScope))
            throw new ArgumentException($"Invalid LadtScope '{req.LadtScope}'. Expected: none, lad, ladt.");
        if (!ValidEnableEcheckChoices.Contains(req.EnableEcheckChoice))
            throw new ArgumentException($"Invalid EnableEcheckChoice '{req.EnableEcheckChoice}'. Expected: off, source.");
        if (!ValidStoreChoices.Contains(req.StoreChoice))
            throw new ArgumentException($"Invalid StoreChoice '{req.StoreChoice}'. Expected: keep, disable.");
    }

    /// <summary>
    /// Team-clone bucket exclusion: WAITLIST mirrors + the Dropped graveyard. The
    /// Registration holding bucket is deliberately KEPT (T5) — its teams are real teams
    /// awaiting placement.
    /// </summary>
    private static bool IsExcludedBucket(string? agegroupName) =>
        !string.IsNullOrEmpty(agegroupName)
        && (agegroupName.Contains(AgegroupConstants.WaitlistPrefix, StringComparison.OrdinalIgnoreCase)
            || agegroupName.Contains(AgegroupConstants.DroppedTeams, StringComparison.OrdinalIgnoreCase));

    public async Task<ClonePlanContext> BuildPlanAsync(
        JobCloneRequest req, string actorUserId, CancellationToken ct = default)
    {
        ValidateChoices(req);

        var sourceJob = await _repo.GetSourceJobAsync(req.SourceJobId, ct)
            ?? throw new KeyNotFoundException($"Source job {req.SourceJobId} not found.");

        var warnings = new List<string>();

        // ── Cross-customer onboarding: clone a template job, then retarget the owner ──
        // Jobs.CustomerId points at the Authorize.Net merchant account that will collect
        // (Customers.AdnLoginId/AdnTransactionKey, resolved job → customer at charge time), so a
        // retarget moves the money and every warning below exists to make that visible pre-release.
        var isCrossCustomer = req.TargetCustomerId != sourceJob.CustomerId;
        if (isCrossCustomer)
        {
            var targetCustomerName = await _repo.GetCustomerNameAsync(req.TargetCustomerId, ct);
            if (targetCustomerName == null)
                warnings.Add($"Target customer {req.TargetCustomerId} does not exist.");

            warnings.Add(
                $"NEW OWNER: this job will belong to {targetCustomerName ?? "the target customer"}, "
                + "not the source's customer. Source admin registrations are NOT copied — you get a "
                + "Superuser row and nothing else, so the new owner's directors must be added by hand.");

            if (!await _repo.CustomerHasAdnCredentialsAsync(req.TargetCustomerId, ct))
                warnings.Add(
                    "Target customer has NO Authorize.Net credentials on file — card payments on this "
                    + "job will fail until they are set. Fix before opening registration.");

            // Points at the branding preview rather than listing tables: the author can SEE the
            // banner and logo they are about to inherit, so naming entities here would be both
            // jargon and a worse description than the picture. Image files are named for the
            // SOURCE job, so until re-upload this job literally serves the old owner's artwork.
            warnings.Add(
                "The banner and header logo shown below carry over from the source job — this new "
                + "owner's site will open with the previous customer's artwork on every page until "
                + "you replace it under Configure → Job → Branding.");

            warnings.Add(
                "Billing type is inherited from the source job — confirm it matches the new customer's agreement.");
        }

        var yearDelta = ComputeYearDelta(sourceJob.Year, req.YearTarget);
        var advance = req.UpAgegroupNamesByOne;
        var ladCloned = !string.Equals(req.LadtScope, "none", StringComparison.OrdinalIgnoreCase);
        var teamsScope = string.Equals(req.LadtScope, "ladt", StringComparison.OrdinalIgnoreCase);

        // ── Identity validation (execute throws on these; preview surfaces them) ──
        var pathSlugValid = JobPathSlugRegex.IsMatch(req.JobPathTarget)
                            && req.JobPathTarget.Length <= MaxJobPathLength;
        if (!pathSlugValid)
            warnings.Add($"Job path '{req.JobPathTarget}' is not a valid URL segment (letters, digits, hyphens; max {MaxJobPathLength} chars).");
        var pathExists = await _repo.JobPathExistsAsync(req.JobPathTarget, ct);
        if (pathExists)
            warnings.Add($"Job path '{req.JobPathTarget}' already exists.");
        var nameExists = await _repo.JobNameExistsAsync(req.JobNameTarget, ct);
        if (nameExists)
            warnings.Add($"Job name '{req.JobNameTarget}' already exists.");

        if (!int.TryParse(sourceJob.Year, out _) || !int.TryParse(req.YearTarget, out _))
            warnings.Add("Source or target year is not numeric — date fields will NOT shift (yearDelta = 0).");

        // ── Load the job-scoped graph (sequential — shared DbContext) ──
        var displayOptions = await _repo.GetSourceDisplayOptionsAsync(req.SourceJobId, ct);
        var owlImages = await _repo.GetSourceOwlImagesAsync(req.SourceJobId, ct);
        var bulletins = await _repo.GetSourceBulletinsAsync(req.SourceJobId, ct);
        var ageRanges = await _repo.GetSourceAgeRangesAsync(req.SourceJobId, ct);
        var menus = await _repo.GetSourceMenusWithItemsAsync(req.SourceJobId, ct);
        var reports = await _repo.GetSourceJobReportsAsync(req.SourceJobId, ct);
        var navs = await _repo.GetSourceNavWithItemsAsync(req.SourceJobId, ct);
        var adminRegs = await _repo.GetSourceAdminRegistrationsAsync(req.SourceJobId, ct);
        var sourceFees = await _feeRepo.GetJobFeesByJobAsync(req.SourceJobId, ct);

        // Retargeting the owner means the source's admins belong to a DIFFERENT company. They
        // would land inactive, but release panel 3 lists inactive admins as activation candidates
        // — one wrong click hands another customer's staff a way in. Drop them; the executor's
        // "no actor row" branch mints a fresh Superuser registration, which is all a new owner
        // should start with.
        if (isCrossCustomer) adminRegs = [];

        var actorHasSourceReg = adminRegs.Any(r =>
            string.Equals(r.UserId, actorUserId, StringComparison.OrdinalIgnoreCase));

        // ── Leagues: walk ALL of Jobs.JobLeagues (T3 — nothing silently dropped) ──
        var jobLeagues = await _repo.GetSourceJobLeaguesAsync(req.SourceJobId, ct);
        var renameByLeague = req.Leagues
            .GroupBy(l => l.SourceLeagueId)
            .ToDictionary(g => g.Key, g => g.First().NameTarget);
        var missingRenames = new List<Guid>();
        var leagueUnits = new List<LeagueCloneUnit>();

        if (string.IsNullOrEmpty(sourceJob.Season) && jobLeagues.Count > 0 && ladCloned)
            warnings.Add("Source job has no season — agegroups from ALL seasons on its league(s) will clone. Review the agegroup list carefully.");

        if (ladCloned)
        {
            foreach (var jl in jobLeagues)
            {
                var league = jl.League;
                var allAgegroups = await _repo.GetSourceAgegroupsAsync(league.LeagueId, sourceJob.Season, ct);

                // WAITLIST mirrors + Dropped graveyard never clone (they are re-minted on
                // demand); the Registration holding bucket DOES clone (T5).
                var eligibleAgegroups = allAgegroups
                    .Where(ag => !IsExcludedBucket(ag.AgegroupName))
                    .ToList();

                if (string.IsNullOrEmpty(sourceJob.Season) && eligibleAgegroups.Count > 0)
                {
                    var bySeason = eligibleAgegroups
                        .GroupBy(a => string.IsNullOrEmpty(a.Season) ? "(none)" : a.Season!)
                        .Select(g => $"{g.Key}: {g.Count()}");
                    warnings.Add($"League '{league.LeagueName}' agegroups by season — {string.Join(", ", bySeason)}.");
                }

                var divisions = eligibleAgegroups.Count == 0
                    ? new List<Divisions>()
                    : await _repo.GetSourceDivisionsAsync(
                        eligibleAgegroups.Select(a => a.AgegroupId).ToList(), ct);

                // Rename row resolution: request row wins; default = year-bumped source
                // name for next-year clones, verbatim for same-year siblings.
                var defaultName = yearDelta >= 1 && !string.IsNullOrEmpty(league.LeagueName)
                    ? IncrementYearsInName(league.LeagueName)
                    : league.LeagueName ?? string.Empty;
                var hasRename = renameByLeague.TryGetValue(league.LeagueId, out var renameTarget)
                                && !string.IsNullOrWhiteSpace(renameTarget);
                if (!hasRename) missingRenames.Add(league.LeagueId);
                var nameTarget = hasRename ? renameTarget!.Trim() : defaultName;

                if (string.Equals(nameTarget.Trim(), league.LeagueName?.Trim(), StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"League name '{nameTarget}' is unchanged from the source — a NEW league with the same name will be created.");

                leagueUnits.Add(new LeagueCloneUnit
                {
                    JobLeague = jl,
                    League = league,
                    Agegroups = eligibleAgegroups,
                    Divisions = divisions,
                    NameTarget = nameTarget,
                    DefaultNameTarget = defaultName,
                });
            }

            var duplicateNames = leagueUnits
                .GroupBy(u => u.NameTarget, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);
            foreach (var dup in duplicateNames)
                warnings.Add($"Two or more leagues share the target name '{dup}'.");
        }

        var eligibleAgegroupIds = leagueUnits
            .SelectMany(u => u.Agegroups.Select(a => a.AgegroupId))
            .ToHashSet();
        var sourceLeagueIds = leagueUnits.Select(u => u.League.LeagueId).ToHashSet();

        // ── Team eligibility (Todd-decided 08-02, CRITICAL): clone STRUCTURE teams,
        //    never COMPETING teams. Eligible iff Active AND not WAITLIST/Dropped bucket
        //    AND (unowned OR owner's club_name empty — the director-created house-team
        //    pattern: Main Event, ISP, HHH, Garden State; DB-verified zero false
        //    positives in 2yr data). External clubs re-register each season.
        var teamRows = await _repo.GetSourceTeamsAsync(req.SourceJobId, ct);
        var eligibleTeams = new List<TeamCloneSource>();
        int excludedInactive = 0, excludedBucket = 0, excludedCompeting = 0, excludedUnmapped = 0;
        foreach (var row in teamRows)
        {
            if (row.Team.Active != true) { excludedInactive++; continue; }
            if (IsExcludedBucket(row.AgegroupName)) { excludedBucket++; continue; }
            var isCompeting = row.Team.ClubrepRegistrationid != null
                              && !string.IsNullOrWhiteSpace(row.OwnerClubName);
            if (isCompeting) { excludedCompeting++; continue; }
            // Structure team — but it can only land if its agegroup/league cloned
            // (season mismatch etc.). Same predicate the executor's maps enforce.
            if (!teamsScope) { eligibleTeams.Add(row); continue; } // counted below as scope-gated
            if (!eligibleAgegroupIds.Contains(row.Team.AgegroupId)
                || !sourceLeagueIds.Contains(row.Team.LeagueId))
            {
                excludedUnmapped++;
                continue;
            }
            eligibleTeams.Add(row);
        }
        // Teams only actually clone under "ladt" — the plan reports the split regardless.
        var plannedTeams = teamsScope ? eligibleTeams.Count : 0;

        // Structure teams arrive ownerless (ClubrepId/ClubrepRegistrationid nulled), so nothing
        // points back at the source customer — but the NAMES do carry. Choosing "lad" avoids it.
        if (isCrossCustomer && plannedTeams > 0)
            warnings.Add(
                $"{plannedTeams} structure team(s) will clone under the new owner carrying the source's "
                + "team names. They arrive ownerless, but the names are the previous customer's — "
                + "choose the \"lad\" scope to leave teams out entirely.");

        // ── Event window: warn on the dates the new job will ACTUALLY carry ──
        //    Same resolution the executor applies (request wins; null = year-shifted source),
        //    so the warning can't describe a date the clone won't land. EventEndDate is the
        //    second rung of the EventConcluded hierarchy: land it in the past and the job is
        //    born concluded — the create door shuts and no registration link ever renders,
        //    however many toggles the director turns on afterward. That is the exact failure
        //    a summer event cloned in late summer produces, so it is called out by name.
        var effectiveEventStart = req.EventStartDate ?? ShiftByYears(sourceJob.EventStartDate, yearDelta);
        var effectiveEventEnd = req.EventEndDate ?? ShiftByYears(sourceJob.EventEndDate, yearDelta);
        var today = DateTime.Now.Date;

        // NOTE: absent event dates get NO warning. They are the norm, not an omission — DB probe
        // 08-03: null on 345/347 clubs, 100/101 camps, 15/16 leagues, and 246/354 TOURNAMENTS.
        // "Is the event over?" then rests on ExpiryUsers, which the clone sets a year forward, so
        // the door is open and registration works. Warning here would fire on nearly every clone
        // and train the operator to skim past the list that carries the real warning below.
        if (effectiveEventEnd.HasValue && effectiveEventEnd.Value.Date < today)
            warnings.Add(
                $"Event end date {effectiveEventEnd.Value:yyyy-MM-dd} is in the PAST — the new job "
                + "will be treated as concluded, and NO registration links will appear on its public "
                + "site regardless of which registration toggles are on. Set it forward in Dates.");

        if (effectiveEventStart.HasValue && effectiveEventEnd.HasValue
            && effectiveEventEnd.Value.Date < effectiveEventStart.Value.Date)
            warnings.Add(
                $"Event end date {effectiveEventEnd.Value:yyyy-MM-dd} is before the start date "
                + $"{effectiveEventStart.Value:yyyy-MM-dd}.");

        // ── Fee eligibility: same maps the executor will mint, expressed as set
        //    membership. A skipped row means its scope target didn't clone.
        var eligibleTeamIds = teamsScope
            ? eligibleTeams.Select(t => t.Team.TeamId).ToHashSet()
            : new HashSet<Guid>();
        var eligibleFees = new List<JobFees>();
        var skippedFees = 0;
        foreach (var fee in sourceFees)
        {
            var ok = true;
            if (fee.AgegroupId.HasValue && !eligibleAgegroupIds.Contains(fee.AgegroupId.Value)) ok = false;
            if (fee.TeamId.HasValue && !eligibleTeamIds.Contains(fee.TeamId.Value)) ok = false;
            if (fee.LeagueId.HasValue && !sourceLeagueIds.Contains(fee.LeagueId.Value)) ok = false;
            if (ok) eligibleFees.Add(fee); else skippedFees++;
        }
        var plannedModifiers = eligibleFees.Sum(f => f.FeeModifiers?.Count ?? 0);

        // ── Admin split ──
        var adminsToDeactivate = adminRegs.Count(r =>
            string.Equals(r.RoleId, RoleConstants.Director, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.RoleId, RoleConstants.SuperDirector, StringComparison.OrdinalIgnoreCase));
        var adminsPreserved = adminRegs.Count - adminsToDeactivate;
        var plannedAdminRegs = adminRegs.Count + (actorHasSourceReg ? 0 : 1);

        // ── Divisions: mirrors JobCloneResetRules.CloneDivisions exactly (one Unassigned per
        //    cloned agegroup, always; the source's named pools only when CopyDivisions) ──
        var clonedAgegroupCount = leagueUnits.Sum(u => u.Agegroups.Count);
        var namedPools = req.CopyDivisions
            ? leagueUnits.SelectMany(u => u.Divisions)
                .Count(d => !string.Equals(d.DivName, DivisionConstants.Unassigned, StringComparison.OrdinalIgnoreCase))
            : 0;
        var plannedDivisions = clonedAgegroupCount + namedPools;
        var divisionsNote = req.CopyDivisions
            ? $"{namedPools} source pool(s) + 1 Unassigned per agegroup"
            : $"fresh pools — 1 Unassigned per agegroup, {leagueUnits.Sum(u => u.Divisions.Count)} source division(s) dropped";

        // ── Steps in manifest order (Count = rows the executor will create) ──
        var menuItemCount = menus.Sum(m => m.JobMenuItems.Count);
        var navItemCount = navs.Sum(n => n.NavItem.Count);
        var steps = new List<ClonePlanStepDto>
        {
            new() { StepKey = JobCloneStepOrder.Job, Count = 1, Notes = req.JobNameTarget },
            new() { StepKey = JobCloneStepOrder.DisplayOptions, Count = displayOptions != null ? 1 : 0 },
            new() { StepKey = JobCloneStepOrder.OwlImages, Count = owlImages != null ? 1 : 0 },
            new() { StepKey = JobCloneStepOrder.Bulletins, Count = bulletins.Count,
                    Notes = bulletins.Count > 0 ? "all arrive INACTIVE — re-activate on the verify page" : null },
            new() { StepKey = JobCloneStepOrder.AgeRanges, Count = ageRanges.Count },
            new() { StepKey = JobCloneStepOrder.Menus, Count = menus.Count,
                    Notes = menus.Count > 0 ? $"{menuItemCount} menu items" : null },
            new() { StepKey = JobCloneStepOrder.JobReports, Count = reports.Count },
            new() { StepKey = JobCloneStepOrder.Nav, Count = navs.Count,
                    Notes = navs.Count > 0 ? $"{navItemCount} nav items" : null },
            new() { StepKey = JobCloneStepOrder.AdminRegistrations, Count = plannedAdminRegs,
                    Notes = isCrossCustomer
                        ? "new owner — source admins NOT copied; 1 fresh Superuser row for you"
                        : $"{adminsToDeactivate} director(s) land inactive"
                          + (actorHasSourceReg ? string.Empty : "; +1 fresh Superuser row for you") },
            new() { StepKey = JobCloneStepOrder.Leagues, Count = leagueUnits.Count },
            new() { StepKey = JobCloneStepOrder.JobLeagues, Count = leagueUnits.Count },
            new() { StepKey = JobCloneStepOrder.Agegroups, Count = leagueUnits.Sum(u => u.Agegroups.Count) },
            new() { StepKey = JobCloneStepOrder.Divisions, Count = plannedDivisions,
                    Notes = divisionsNote },
            new() { StepKey = JobCloneStepOrder.Teams, Count = plannedTeams,
                    Notes = BuildTeamsNote(teamsScope, eligibleTeams.Count, excludedCompeting,
                                           excludedBucket, excludedInactive, excludedUnmapped) },
            new() { StepKey = JobCloneStepOrder.JobFees, Count = eligibleFees.Count,
                    Notes = skippedFees > 0 ? $"{skippedFees} row(s) skipped — scope target not cloned" : null },
            new() { StepKey = JobCloneStepOrder.FeeModifiers, Count = plannedModifiers },
        };

        // ── Detail lists (workbench plan pane) ──
        var bulletinShifts = bulletins.Select(b => new BulletinShiftDto
        {
            SourceBulletinId = b.BulletinId,
            Title = b.Title,
            CreateDate = new DateShiftDto { From = b.CreateDate, To = ShiftByYears(b.CreateDate, yearDelta) },
            StartDate = ShiftDto(b.StartDate, yearDelta),
            EndDate = ShiftDto(b.EndDate, yearDelta),
        }).ToList();

        // Per-agegroup early-bird/late-fee window hints come from Player agegroup-scoped
        // fee modifiers (the full list is surfaced separately below).
        var agegroupModifiers = sourceFees
            .Where(f => f.RoleId == RoleConstants.Player && f.TeamId == null && f.AgegroupId.HasValue)
            .GroupBy(f => f.AgegroupId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(f => f.FeeModifiers ?? Enumerable.Empty<FeeModifiers>()).ToList());

        var agegroupPreviews = new List<AgegroupPreviewDto>();
        foreach (var ag in leagueUnits.SelectMany(u => u.Agegroups))
        {
            var newName = advance ? BumpYears(ag.AgegroupName) : ag.AgegroupName;
            var newGradMin = advance && ag.GradYearMin.HasValue ? ag.GradYearMin + 1 : ag.GradYearMin;
            var newGradMax = advance && ag.GradYearMax.HasValue ? ag.GradYearMax + 1 : ag.GradYearMax;
            var newDobMin = advance ? ShiftByYears(ag.DobMin, 1) : ag.DobMin;
            var newDobMax = advance ? ShiftByYears(ag.DobMax, 1) : ag.DobMax;

            var mods = agegroupModifiers.TryGetValue(ag.AgegroupId, out var m) ? m : new List<FeeModifiers>();
            var earlyBird = mods.FirstOrDefault(x => x.ModifierType == FeeConstants.ModifierEarlyBird);
            var lateFee = mods.FirstOrDefault(x => x.ModifierType == FeeConstants.ModifierLateFee);

            agegroupPreviews.Add(new AgegroupPreviewDto
            {
                SourceAgegroupId = ag.AgegroupId,
                SourceName = ag.AgegroupName,
                NewName = newName,
                SourceGradYearMin = ag.GradYearMin,
                NewGradYearMin = newGradMin,
                SourceGradYearMax = ag.GradYearMax,
                NewGradYearMax = newGradMax,
                DobMin = DateOnlyShift(ag.DobMin, newDobMin),
                DobMax = DateOnlyShift(ag.DobMax, newDobMax),
                DiscountFeeStart = ShiftDto(earlyBird?.StartDate, yearDelta),
                DiscountFeeEnd = ShiftDto(earlyBird?.EndDate, yearDelta),
                LateFeeStart = ShiftDto(lateFee?.StartDate, yearDelta),
                LateFeeEnd = ShiftDto(lateFee?.EndDate, yearDelta),
            });
        }

        var feeModifierShifts = eligibleFees
            .SelectMany(f => f.FeeModifiers ?? Enumerable.Empty<FeeModifiers>())
            .Select(mod => new FeeModifierShiftDto
            {
                SourceFeeModifierId = mod.FeeModifierId,
                ModifierType = mod.ModifierType,
                Amount = mod.Amount,
                StartDate = ShiftDto(mod.StartDate, yearDelta),
                EndDate = ShiftDto(mod.EndDate, yearDelta),
            }).ToList();

        var leaguePlans = leagueUnits.Select(u => new LeaguePlanDto
        {
            SourceLeagueId = u.League.LeagueId,
            SourceName = u.League.LeagueName,
            DefaultNameTarget = u.DefaultNameTarget,
            IsPrimary = u.JobLeague.BIsPrimary,
            AgegroupCount = u.Agegroups.Count,
            DivisionCount = u.Divisions.Count,
            TeamCount = teamsScope
                ? eligibleTeams.Count(t => t.Team.LeagueId == u.League.LeagueId)
                : 0,
        }).ToList();

        var fingerprint = ComputeFingerprint(req.SourceJobId, req.TargetCustomerId, yearDelta, steps,
            excludedCompeting, excludedBucket, excludedInactive);

        var dto = new ClonePlanDto
        {
            Steps = steps,
            PlanFingerprint = fingerprint,
            YearDelta = yearDelta,
            AdvanceFlagDefault = yearDelta >= 1,
            ResolvedProcessingFeePercent = Math.Max(
                sourceJob.ProcessingFeePercent ?? FeeConstants.NewJobProcessingFeePercent,
                FeeConstants.NewJobProcessingFeePercent),
            SourceProcessingFeePercent = sourceJob.ProcessingFeePercent,
            ResolvedEcheckProcessingFeePercent = Math.Max(
                sourceJob.EcprocessingFeePercent ?? FeeConstants.NewJobEcprocessingFeePercent,
                FeeConstants.NewJobEcprocessingFeePercent),
            SourceEcheckProcessingFeePercent = sourceJob.EcprocessingFeePercent,
            SourceBEnableEcheck = sourceJob.BEnableEcheck,
            SourceBEnableStore = sourceJob.BEnableStore ?? false,
            BrandingPreview = BuildBrandingPreview(displayOptions, req),
            RegFormFrom = sourceJob.RegFormFrom,
            RegFormCcs = sourceJob.RegFormCcs,
            RegFormBccs = sourceJob.RegFormBccs,
            Rescheduleemaillist = sourceJob.Rescheduleemaillist,
            Alwayscopyemaillist = sourceJob.Alwayscopyemaillist,
            MailTo = sourceJob.MailTo,
            PayTo = sourceJob.PayTo,
            StoreContactEmail = sourceJob.StoreContactEmail,
            SourceJobTypeId = sourceJob.JobTypeId,
            SourceCustomerId = sourceJob.CustomerId,
            IsCrossCustomer = isCrossCustomer,
            // No EventStart/EventEnd shift rows: those two are editable fields on the workbench
            // now (seeded +1yr), so a read-only "from → to" preview beside them could contradict
            // what the operator typed. The Dates section IS the preview; warnings above cover the
            // dangerous values.
            AdnArbStartShift = ShiftDto(sourceJob.AdnArbstartDate, yearDelta),
            AdnStartDateAfterTrialShift = ShiftDto(sourceJob.AdnStartDateAfterTrial, yearDelta),
            UslaxNumberValidThroughShift = ShiftDto(sourceJob.UslaxNumberValidThroughDate, yearDelta),
            AdminsToDeactivate = adminsToDeactivate,
            AdminsPreserved = adminsPreserved,
            TeamsToClone = eligibleTeams.Count,
            TeamsExcludedCompeting = excludedCompeting,
            TeamsExcludedWaitlistDropped = excludedBucket,
            TeamsExcludedInactive = excludedInactive,
            Leagues = leaguePlans,
            Bulletins = bulletinShifts,
            Agegroups = agegroupPreviews,
            FeeModifiers = feeModifierShifts,
            Warnings = warnings,
        };

        return new ClonePlanContext
        {
            Request = req,
            SourceJob = sourceJob,
            DisplayOptions = displayOptions,
            OwlImages = owlImages,
            Bulletins = bulletins,
            AgeRanges = ageRanges,
            Menus = menus,
            Reports = reports,
            Navs = navs,
            AdminRegs = adminRegs,
            ActorHasSourceReg = actorHasSourceReg,
            LeagueUnits = leagueUnits,
            EligibleTeams = eligibleTeams,
            EligibleFees = eligibleFees,
            YearDelta = yearDelta,
            TeamsScope = teamsScope,
            PathSlugValid = pathSlugValid,
            PathExists = pathExists,
            NameExists = nameExists,
            MissingLeagueRenames = missingRenames,
            Dto = dto,
        };
    }

    private static string? BuildTeamsNote(
        bool teamsScope, int eligible, int competing, int bucket, int inactive, int unmapped)
    {
        var exclusions = $"{competing} competing excluded, {bucket} waitlist/dropped, {inactive} inactive"
                         + (unmapped > 0 ? $", {unmapped} without a cloned agegroup" : string.Empty);
        return teamsScope
            ? $"structure teams only — {exclusions}"
            : $"LADT scope not selected — no teams clone ({eligible} structure team(s) would be eligible; {exclusions})";
    }

    /// <summary>
    /// What the cloned banner and logo will be. Runs the REAL reset rule against the source row
    /// rather than re-deriving the option logic here — a second implementation would drift from
    /// the executing one the first time either changes, and the whole point of showing the
    /// branding is that the preview is trustworthy.
    ///
    /// Safe to call outside a transaction: CloneDisplayOptions is pure, and CopyScalars hands
    /// back a detached copy — the source entity is never mutated.
    ///
    /// Text comes back as plain text because the workbench EDITS it: what it renders is what
    /// sits in its textareas, and what those textareas hold is what comes back on the request.
    /// </summary>
    private static ClonedBrandingPreviewDto? BuildBrandingPreview(
        JobDisplayOptions? source, JobCloneRequest req)
    {
        if (source is null) return null;

        var cloned = JobCloneResetRules.CloneDisplayOptions(
            source, newJobId: Guid.Empty, req, userId: string.Empty, now: default);

        return new ClonedBrandingPreviewDto
        {
            IsCustom = cloned.ParallaxSlideCount > 0,
            BackgroundImage = cloned.ParallaxBackgroundImage,
            OverlayImage = cloned.ParallaxSlide1Image,
            Text1 = OverlayText.ToPlainText(cloned.ParallaxSlide1Text1),
            Text2 = OverlayText.ToPlainText(cloned.ParallaxSlide1Text2),
            LogoImage = cloned.LogoHeader,
        };
    }

    /// <summary>
    /// Data-moved guard fingerprint: SHA-256 over the ordered per-step counts + team
    /// split. Coarse by design — it catches source rows appearing/disappearing between
    /// preview and execute; in-place edits to a row don't change counts and are accepted.
    /// The target customer is folded in because it decides where money settles: changing it
    /// after approval must invalidate the plan even when every step count happens to match.
    /// </summary>
    private static string ComputeFingerprint(
        Guid sourceJobId, Guid targetCustomerId, int yearDelta, List<ClonePlanStepDto> steps,
        int competing, int bucket, int inactive)
    {
        var canonical = $"{sourceJobId:N}|{targetCustomerId:N}|{yearDelta}"
            + $"|{string.Join(",", steps.Select(s => $"{s.StepKey}={s.Count}"))}"
            + $"|T:{competing}/{bucket}/{inactive}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DateShiftDto? ShiftDto(DateTime? source, int yearDelta)
    {
        if (!source.HasValue) return null;
        return new DateShiftDto { From = source, To = ShiftByYears(source, yearDelta) };
    }

    private static DateShiftDto? DateOnlyShift(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue && !to.HasValue) return null;
        return new DateShiftDto
        {
            From = from?.ToDateTime(TimeOnly.MinValue),
            To = to?.ToDateTime(TimeOnly.MinValue),
        };
    }
}

/// <summary>
/// The plan with its loaded source graph — everything the executor needs to materialize
/// exactly what the plan counted. Built fresh inside the clone transaction.
/// </summary>
public sealed class ClonePlanContext
{
    public required JobCloneRequest Request { get; init; }
    public required Jobs SourceJob { get; init; }
    public JobDisplayOptions? DisplayOptions { get; init; }
    public JobOwlImages? OwlImages { get; init; }
    public required List<Bulletins> Bulletins { get; init; }
    public required List<JobAgeRanges> AgeRanges { get; init; }
    public required List<JobMenus> Menus { get; init; }
    public required List<JobReports> Reports { get; init; }
    public required List<Nav> Navs { get; init; }
    public required List<Registrations> AdminRegs { get; init; }
    public required bool ActorHasSourceReg { get; init; }
    public required List<LeagueCloneUnit> LeagueUnits { get; init; }
    public required List<TeamCloneSource> EligibleTeams { get; init; }
    public required List<JobFees> EligibleFees { get; init; }
    public required int YearDelta { get; init; }
    public required bool TeamsScope { get; init; }
    public required bool PathSlugValid { get; init; }
    public required bool PathExists { get; init; }
    public required bool NameExists { get; init; }
    public required List<Guid> MissingLeagueRenames { get; init; }
    public required ClonePlanDto Dto { get; init; }
}

/// <summary>One source league with its eligible agegroups/divisions and resolved rename.</summary>
public sealed class LeagueCloneUnit
{
    public required JobLeagues JobLeague { get; init; }
    public required Leagues League { get; init; }
    public required List<Agegroups> Agegroups { get; init; }
    public required List<Divisions> Divisions { get; init; }
    public required string NameTarget { get; init; }
    public required string DefaultNameTarget { get; init; }
}
