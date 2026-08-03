using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Orchestrates job cloning under the COPY-EVERYTHING philosophy:
///   - JobClonePlanner builds ONE plan (eligibility + counts + warnings) consumed by both
///     preview and execute — execute re-plans INSIDE the transaction (no TOCTOU),
///   - JobCloneResetRules holds the careful, versioned exception list applied on top of
///     the mechanical field copy,
///   - JobCloneStepOrder is the single manifest driving insert order here and the
///     dev-undo cascade delete (reversed) in the repository.
///
/// Safe-by-default on every clone: BSuspendPublic=true; all five BRegistrationAllow*
/// false; the full safe-state reset list (see JobCloneResetRules.CloneJob); Director +
/// SuperDirector regs inactive until release. The release page then walks
/// verify → release site → release admins → open registration.
/// </summary>
public sealed class JobCloneService : IJobCloneService
{
    private readonly IJobCloneRepository _repo;
    private readonly IFeeRepository _feeRepo;
    private readonly JobClonePlanner _planner;
    private readonly ILogger<JobCloneService> _logger;

    public JobCloneService(
        IJobCloneRepository repo,
        IFeeRepository feeRepo,
        JobClonePlanner planner,
        ILogger<JobCloneService> logger)
    {
        _repo = repo;
        _feeRepo = feeRepo;
        _planner = planner;
        _logger = logger;
    }

    public async Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default)
    {
        return await _repo.GetCloneableJobsAsync(ct);
    }

    // ══════════════════════════════════════════════════════════
    // Entry normalization — one place, before anything reads the values
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Trims the two operator-typed identity fields at the door.
    /// SQL Server's '=' ignores TRAILING spaces but not LEADING ones, so " Fall 2026"
    /// sails past the duplicate-name check and then lands in Jobs looking identical to
    /// "Fall 2026" — the legacy failure this guard exists to prevent. Normalizing once
    /// here (rather than in the planner and again in the reset rules) means the value
    /// the existence check tested and the value actually written cannot disagree.
    /// Case needs no handling: the DB collates CI, so "fall 2026" already collides.
    /// </summary>
    private static JobCloneRequest Normalize(JobCloneRequest request) => request with
    {
        JobPathTarget = request.JobPathTarget?.Trim() ?? string.Empty,
        JobNameTarget = request.JobNameTarget?.Trim() ?? string.Empty,
    };

    // ══════════════════════════════════════════════════════════
    // Preview — render the plan (no writes)
    // ══════════════════════════════════════════════════════════

    public async Task<ClonePlanDto> PreviewCloneAsync(
        JobCloneRequest request,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        request = Normalize(request);

        var sourceJob = await _repo.GetSourceJobAsync(request.SourceJobId, ct)
            ?? throw new KeyNotFoundException($"Source job {request.SourceJobId} not found.");
        GuardCustomerScope(authorCustomerId, sourceJob.CustomerId, request.TargetCustomerId);

        var ctx = await _planner.BuildPlanAsync(request, actorUserId, ct);
        return ctx.Dto;
    }

    // ══════════════════════════════════════════════════════════
    // Clone — plan inside the transaction, then materialize the plan
    // ══════════════════════════════════════════════════════════

    public async Task<JobCloneResponse> CloneJobAsync(
        JobCloneRequest request,
        string superUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        request = Normalize(request);

        var sourceJob = await _repo.GetSourceJobAsync(request.SourceJobId, ct)
            ?? throw new KeyNotFoundException($"Source job {request.SourceJobId} not found.");
        GuardCustomerScope(authorCustomerId, sourceJob.CustomerId, request.TargetCustomerId);

        await _repo.BeginTransactionAsync(ct);
        try
        {
            // Re-plan INSIDE the transaction — the plan the executor materializes is by
            // construction the plan the counts/fingerprint describe.
            var ctx = await _planner.BuildPlanAsync(request, superUserId, ct);

            // Hard validation at execute (preview surfaces the same facts as warnings).
            if (!ctx.PathSlugValid)
                throw new ArgumentException(
                    $"Job path '{request.JobPathTarget}' is not a valid URL segment (letters, digits, hyphens; max {JobClonePlanner.MaxJobPathLength} chars).");
            if (ctx.PathExists)
                throw new InvalidOperationException($"Job path '{request.JobPathTarget}' already exists.");
            if (ctx.NameExists)
                throw new InvalidOperationException($"Job name '{request.JobNameTarget}' already exists.");
            if (ctx.MissingLeagueRenames.Count > 0)
                throw new ArgumentException(
                    "Every source league needs a target name. Missing: "
                    + string.Join(", ", ctx.MissingLeagueRenames));

            // Data-moved guard: the operator approved a specific plan. If the fresh
            // in-transaction plan differs, abort with the fresh plan for review.
            if (request.PlanFingerprint != null
                && !string.Equals(request.PlanFingerprint, ctx.Dto.PlanFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new ClonePlanChangedException(ctx.Dto);
            }

            var now = DateTime.Now;
            var newJobId = Guid.NewGuid();
            _logger.LogInformation(
                "Cloning job {SourceJobId} → {TargetPath} (yearDelta={YearDelta})",
                request.SourceJobId, request.JobPathTarget, ctx.YearDelta);

            var (steps, actorRegistrationId) = Materialize(ctx, newJobId, superUserId, now);

            await _repo.SaveChangesAsync(ct);
            await _repo.CommitTransactionAsync(ct);

            _logger.LogInformation(
                "Job clone complete: {NewJobPath} ({NewJobId}) — {Summary}",
                request.JobPathTarget, newJobId,
                string.Join(", ", steps.Select(s => $"{s.StepKey}={s.Count}")));

            return new JobCloneResponse
            {
                NewJobId = newJobId,
                NewJobPath = request.JobPathTarget,
                NewJobName = request.JobNameTarget,
                Steps = steps,
                NewSuperUserRegistrationId = actorRegistrationId,
            };
        }
        catch
        {
            await _repo.RollbackTransactionAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Builds the entire new-job graph in memory (client-minted GUIDs — every FK correct
    /// before any INSERT), then queues each step's rows in JobCloneStepOrder. The handler
    /// dictionary MUST cover the manifest exactly — checked at runtime on every clone, so
    /// a manifest key without an executor (or vice versa) fails loudly, never silently.
    /// </summary>
    private (List<ClonePlanStepDto> Steps, Guid ActorRegistrationId) Materialize(
        ClonePlanContext ctx, Guid newJobId, string userId, DateTime now)
    {
        var req = ctx.Request;
        var yearDelta = ctx.YearDelta;
        var advance = req.UpAgegroupNamesByOne;

        // ── Admin registrations first: the old→new map feeds PrimaryContactRegistrationId ──
        var registrationIdMap = new Dictionary<Guid, Guid>();
        var clonedRegs = JobCloneResetRules.CloneAdminRegistrations(
            ctx.AdminRegs, newJobId, userId, now, yearDelta, registrationIdMap);

        var actorRegId = clonedRegs
            .FirstOrDefault(r => string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase))
            ?.RegistrationId;
        if (actorRegId == null)
        {
            // Superusers are global by policy — many source jobs don't list them as
            // registered admins. Mint a fresh ACTIVE Superuser registration so the FE
            // always has a regId to switch into.
            var actorReg = new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = newJobId,
                RoleId = RoleConstants.Superuser,
                UserId = userId,
                BActive = true,
                BConfirmationSent = false,
                RegistrationTs = now,
                CustomerId = req.TargetCustomerId,   // the NEW job's owner, not the source's
                LebUserId = userId,
                Modified = now,
                FeeBase = 0, FeeProcessing = 0, FeeDiscount = 0, FeeDiscountMp = 0,
                FeeDonation = 0, FeeLatefee = 0, PaidTotal = 0,
            };
            clonedRegs.Add(actorReg);
            actorRegId = actorReg.RegistrationId;
        }

        // ── Job + 1:1s + flat children ──
        var job = JobCloneResetRules.CloneJob(
            ctx.SourceJob, newJobId, req, userId, now, yearDelta, registrationIdMap);
        var display = ctx.DisplayOptions != null
            ? JobCloneResetRules.CloneDisplayOptions(ctx.DisplayOptions, newJobId, req, userId, now)
            : null;
        var owl = ctx.OwlImages != null
            ? JobCloneResetRules.CloneOwlImages(ctx.OwlImages, newJobId, userId, now)
            : null;
        var bulletins = JobCloneResetRules.CloneBulletins(ctx.Bulletins, newJobId, advance, userId, now, yearDelta);
        var ageRanges = JobCloneResetRules.CloneAgeRanges(ctx.AgeRanges, newJobId, advance, userId, now);
        var (menus, menuItems) = JobCloneResetRules.CloneMenus(ctx.Menus, newJobId, userId, now);
        var reports = JobCloneResetRules.CloneJobReports(ctx.Reports, newJobId, userId, now);
        var navs = ctx.Navs.Select(n => JobCloneResetRules.CloneNav(n, newJobId, userId, now)).ToList();

        // ── LADT graph (all leagues — T3) ──
        var leagueIdMap = new Dictionary<Guid, Guid>();
        var agegroupIdMap = new Dictionary<Guid, Guid>();
        var divisionIdMap = new Dictionary<Guid, Guid>();
        var teamIdMap = new Dictionary<Guid, Guid>();
        var leagues = new List<Leagues>();
        var jobLeagues = new List<JobLeagues>();
        var agegroups = new List<Agegroups>();
        var divisions = new List<Divisions>();

        foreach (var unit in ctx.LeagueUnits)
        {
            var newLeagueId = Guid.NewGuid();
            leagueIdMap[unit.League.LeagueId] = newLeagueId;
            leagues.Add(JobCloneResetRules.CloneLeague(unit.League, newLeagueId, unit.NameTarget, userId, now));
            jobLeagues.Add(JobCloneResetRules.CloneJobLeague(unit.JobLeague, newJobId, newLeagueId, userId, now));
            agegroups.AddRange(JobCloneResetRules.CloneAgegroups(
                unit.Agegroups, newLeagueId, req, userId, now, agegroupIdMap));
            var newAgegroupIds = unit.Agegroups
                .Where(a => agegroupIdMap.ContainsKey(a.AgegroupId))
                .Select(a => agegroupIdMap[a.AgegroupId])
                .ToList();
            divisions.AddRange(JobCloneResetRules.CloneDivisions(
                unit.Divisions, agegroupIdMap, newAgegroupIds, divisionIdMap,
                req.CopyDivisions, userId, now));
        }

        List<TSIC.Domain.Entities.Teams> teams = ctx.TeamsScope
            ? JobCloneResetRules.CloneTeams(
                ctx.EligibleTeams.Select(t => t.Team), newJobId, req, userId, now, yearDelta,
                leagueIdMap, agegroupIdMap, divisionIdMap, teamIdMap)
            : new List<TSIC.Domain.Entities.Teams>();

        // ── Fees: the planner's eligible rows, remapped through the freshly-minted maps.
        //    Direct indexing is deliberate — the planner guaranteed membership; a miss is
        //    a planner/executor drift bug and must throw (rolling back), never skip.
        var fees = new List<JobFees>();
        var modifiers = new List<FeeModifiers>();
        foreach (var sourceFee in ctx.EligibleFees)
        {
            var newFeeId = Guid.NewGuid();
            fees.Add(JobCloneResetRules.CloneJobFee(
                sourceFee, newFeeId, newJobId,
                sourceFee.AgegroupId.HasValue ? agegroupIdMap[sourceFee.AgegroupId.Value] : null,
                sourceFee.TeamId.HasValue ? teamIdMap[sourceFee.TeamId.Value] : null,
                sourceFee.LeagueId.HasValue ? leagueIdMap[sourceFee.LeagueId.Value] : null,
                userId, now));
            if (sourceFee.FeeModifiers != null)
            {
                modifiers.AddRange(sourceFee.FeeModifiers.Select(m =>
                    JobCloneResetRules.CloneFeeModifier(m, newFeeId, userId, now, yearDelta)));
            }
        }

        // ── Queue in manifest order ──
        var handlers = new Dictionary<string, Func<(int Count, string? Notes)>>
        {
            [JobCloneStepOrder.Job] = () => { _repo.AddJob(job); return (1, req.JobNameTarget); },
            [JobCloneStepOrder.DisplayOptions] = () =>
            {
                if (display != null) _repo.AddDisplayOptions(display);
                return (display != null ? 1 : 0, null);
            },
            [JobCloneStepOrder.OwlImages] = () =>
            {
                if (owl != null) _repo.AddOwlImages(owl);
                return (owl != null ? 1 : 0, null);
            },
            [JobCloneStepOrder.Bulletins] = () =>
            {
                if (bulletins.Count > 0) _repo.AddBulletins(bulletins);
                return (bulletins.Count, bulletins.Count > 0 ? "all arrive INACTIVE — re-activate on the verify page" : null);
            },
            [JobCloneStepOrder.AgeRanges] = () =>
            {
                if (ageRanges.Count > 0) _repo.AddAgeRanges(ageRanges);
                return (ageRanges.Count, null);
            },
            [JobCloneStepOrder.Menus] = () =>
            {
                foreach (var m in menus) _repo.AddMenu(m);
                if (menuItems.Count > 0) _repo.AddMenuItems(menuItems);
                return (menus.Count, menus.Count > 0 ? $"{menuItems.Count} menu items" : null);
            },
            [JobCloneStepOrder.JobReports] = () =>
            {
                if (reports.Count > 0) _repo.AddJobReports(reports);
                return (reports.Count, null);
            },
            [JobCloneStepOrder.Nav] = () =>
            {
                foreach (var n in navs) _repo.AddNav(n);
                return (navs.Count, navs.Count > 0 ? $"{navs.Sum(n => n.NavItem.Count)} nav items" : null);
            },
            [JobCloneStepOrder.AdminRegistrations] = () =>
            {
                _repo.AddRegistrations(clonedRegs);
                return (clonedRegs.Count, null);
            },
            [JobCloneStepOrder.Leagues] = () =>
            {
                foreach (var l in leagues) _repo.AddLeague(l);
                return (leagues.Count, null);
            },
            [JobCloneStepOrder.JobLeagues] = () =>
            {
                foreach (var jl in jobLeagues) _repo.AddJobLeague(jl);
                return (jobLeagues.Count, null);
            },
            [JobCloneStepOrder.Agegroups] = () =>
            {
                if (agegroups.Count > 0) _repo.AddAgegroups(agegroups);
                return (agegroups.Count, null);
            },
            [JobCloneStepOrder.Divisions] = () =>
            {
                if (divisions.Count > 0) _repo.AddDivisions(divisions);
                return (divisions.Count, null);
            },
            [JobCloneStepOrder.Teams] = () =>
            {
                if (teams.Count > 0) _repo.AddTeams(teams);
                return (teams.Count, null);
            },
            [JobCloneStepOrder.JobFees] = () =>
            {
                foreach (var f in fees) _feeRepo.Add(f);
                return (fees.Count, null);
            },
            [JobCloneStepOrder.FeeModifiers] = () =>
            {
                foreach (var m in modifiers) _feeRepo.AddModifier(m);
                return (modifiers.Count, null);
            },
        };

        // Manifest parity — both directions, every clone.
        if (!handlers.Keys.ToHashSet().SetEquals(JobCloneStepOrder.Steps))
        {
            var missing = JobCloneStepOrder.Steps.Except(handlers.Keys).ToList();
            var extra = handlers.Keys.Except(JobCloneStepOrder.Steps).ToList();
            throw new InvalidOperationException(
                $"Clone executor / manifest drift. Missing handlers: [{string.Join(", ", missing)}]; "
                + $"handlers not in manifest: [{string.Join(", ", extra)}].");
        }

        var steps = new List<ClonePlanStepDto>();
        foreach (var key in JobCloneStepOrder.Steps)
        {
            var (count, notes) = handlers[key]();
            steps.Add(new ClonePlanStepDto { StepKey = key, Count = count, Notes = notes });
        }

        return (steps, actorRegId.Value);
    }

    /// <summary>
    /// authorCustomerId=null → the SuperUser-only controller, which may retarget freely (that IS
    /// the onboarding path). A scoped caller must own BOTH ends: the source it copies from and the
    /// customer it hands the new job to — checking only one would let a director either read
    /// another customer's job or mint a job under someone else's merchant account.
    /// </summary>
    private static void GuardCustomerScope(
        Guid? authorCustomerId, Guid sourceCustomerId, Guid targetCustomerId)
    {
        if (!authorCustomerId.HasValue) return;

        if (authorCustomerId.Value != sourceCustomerId)
            throw new UnauthorizedAccessException("Cannot clone another customer's job.");

        if (authorCustomerId.Value != targetCustomerId)
            throw new UnauthorizedAccessException("Cannot assign the new job to a different customer.");
    }

    // ══════════════════════════════════════════════════════════
    // Identity checks (inline uniqueness on the workbench)
    // ══════════════════════════════════════════════════════════

    public Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default)
    {
        return _repo.JobPathExistsAsync(jobPath, ct);
    }

    public Task<bool> JobNameExistsAsync(string jobName, CancellationToken ct = default)
    {
        return _repo.JobNameExistsAsync(jobName, ct);
    }

    // ══════════════════════════════════════════════════════════
    // Dev-only undo
    // ══════════════════════════════════════════════════════════

    public async Task<DevUndoStatusResponse> GetDevUndoStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var counts = await _repo.GetDevUndoCountsAsync(jobId, ct);
        var reasons = BuildUndoBlockReasons(counts);
        return new DevUndoStatusResponse
        {
            CanUndo = reasons.Count == 0,
            Reasons = reasons,
            Counts = counts,
        };
    }

    public async Task DeleteClonedJobAsync(Guid jobId, CancellationToken ct = default)
    {
        await _repo.BeginTransactionAsync(ct);
        try
        {
            // Already gone? A double-click sends two DELETEs. The second one used to run the
            // whole cascade against rows the first had removed and surface as an unhandled
            // DbUpdateConcurrencyException ("expected to affect 1 row(s), but actually affected
            // 0") from inside EF's batch executor — an alarming stack trace for what is simply
            // "that job isn't there any more". The transaction always rolled back, so nothing
            // was ever half-deleted; only the diagnosis was bad.
            var job = await _repo.GetSourceJobAsync(jobId, ct);
            if (job is null)
                throw new KeyNotFoundException(
                    $"Job {jobId} not found — it may already have been deleted.");

            // Re-run predicate inside the txn so a row inserted between status fetch and
            // delete can't slip through.
            var counts = await _repo.GetDevUndoCountsAsync(jobId, ct);
            var reasons = BuildUndoBlockReasons(counts);
            if (reasons.Count > 0)
            {
                throw new InvalidOperationException(
                    "Cannot delete cloned job: " + string.Join("; ", reasons));
            }

            // Multi-league (T3): each cloned league is deleted only when no OTHER job
            // references it.
            var leagueIdsToDelete = new List<Guid>();
            foreach (var jl in await _repo.GetJobLeaguesForJobAsync(jobId, ct))
            {
                if (await _repo.IsLeagueExclusivelyOwnedByJobAsync(jobId, jl.LeagueId, ct))
                {
                    leagueIdsToDelete.Add(jl.LeagueId);
                }
                else
                {
                    _logger.LogWarning(
                        "DevUndo: cloned Leagues {LeagueId} is referenced by another job; preserving it.",
                        jl.LeagueId);
                }
            }

            await _repo.CascadeDeleteJobAsync(jobId, leagueIdsToDelete, ct);
            await _repo.CommitTransactionAsync(ct);

            _logger.LogInformation("DevUndo: cascade-deleted job {JobId}", jobId);
        }
        catch
        {
            await _repo.RollbackTransactionAsync(ct);
            throw;
        }
    }

    private static List<string> BuildUndoBlockReasons(DevUndoCounts c)
    {
        var reasons = new List<string>();
        if (c.NonAdminRegistrations > 0)
            reasons.Add($"{c.NonAdminRegistrations} non-admin registration(s) exist");
        if (c.RegistrationAccounting > 0)
            reasons.Add($"{c.RegistrationAccounting} registration accounting record(s) exist");
        if (c.AncillaryRows > 0)
        {
            reasons.Add(
                $"{c.AncillaryRows} ancillary row(s) exist — job has been used"
                + (c.AncillaryBreakdown.Count > 0
                    ? $" ({string.Join(", ", c.AncillaryBreakdown)})"
                    : string.Empty));
        }
        return reasons;
    }
}
