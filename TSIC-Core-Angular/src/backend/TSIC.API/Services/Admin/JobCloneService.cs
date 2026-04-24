using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using static TSIC.API.Services.Admin.JobCloneTransforms;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Orchestrates job cloning — copies a source job and its related entities into a new job.
///
/// Safe-by-default state on every clone:
///   - Jobs.BSuspendPublic = true (hidden from public until author releases)
///   - Director + SuperDirector cloned Registrations land with BActive = false
///     (Superuser Registrations are NOT deactivated — TSIC-central, must stay functional)
///   - BClubRepAllowEdit/Delete/Add forced true (lifecycle reset — source may have them off
///     from post-schedule lockdown; new clone is at the pre-schedule registration phase)
///   - ProcessingFeePercent reset to current minimum (avoids stale-rate carryover)
///
/// Date-sensitive fields shift by year-delta (targetYear − sourceYear):
///   - Bulletins CreateDate/StartDate/EndDate
///   - FeeModifier StartDate/EndDate
///   - Agegroup DobMin/DobMax (when UpAgegroupNamesByOne), DiscountFeeStart/End, LateFeeStart/End
///   - Jobs EventStartDate/EventEndDate, AdnArbstartDate
///
/// Cross-customer cloning is disallowed (same-customer guard). Use Blank flow for new customers.
/// </summary>
public sealed class JobCloneService : IJobCloneService
{
    private readonly IJobCloneRepository _repo;
    private readonly IFeeRepository _feeRepo;
    private readonly ILogger<JobCloneService> _logger;

    public JobCloneService(IJobCloneRepository repo, IFeeRepository feeRepo, ILogger<JobCloneService> logger)
    {
        _repo = repo;
        _feeRepo = feeRepo;
        _logger = logger;
    }

    public async Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default)
    {
        return await _repo.GetCloneableJobsAsync(ct);
    }

    public async Task<JobCloneResponse> CloneJobAsync(
        JobCloneRequest request,
        string superUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        // ── Validate ──
        var sourceJob = await _repo.GetSourceJobAsync(request.SourceJobId, ct)
            ?? throw new KeyNotFoundException($"Source job {request.SourceJobId} not found.");

        // Same-customer guard (cross-customer cloning disallowed — use Blank flow for new customers).
        // authorCustomerId is null when caller is SuperUser-only (current controller) — skip the check
        // since target always inherits source.CustomerId, making cross-customer impossible by construction.
        if (authorCustomerId.HasValue && authorCustomerId.Value != sourceJob.CustomerId)
        {
            throw new UnauthorizedAccessException(
                "Cross-customer cloning is not allowed. Use the Blank-job flow to onboard a new customer.");
        }

        if (await _repo.JobPathExistsAsync(request.JobPathTarget, ct))
            throw new InvalidOperationException($"Job path '{request.JobPathTarget}' already exists.");

        var now = DateTime.UtcNow;
        var newJobId = Guid.NewGuid();
        var summary = new CloneSummaryBuilder();
        var yearDelta = ComputeYearDelta(sourceJob.Year, request.YearTarget);

        await _repo.BeginTransactionAsync(ct);
        try
        {
            // ── Step 1: Clone Jobs.Jobs ──
            _logger.LogInformation(
                "Cloning job {SourceJobId} → {TargetPath} (yearDelta={YearDelta})",
                request.SourceJobId, request.JobPathTarget, yearDelta);
            var clonedJob = CloneJob(sourceJob, newJobId, request, superUserId, now, yearDelta);
            _repo.AddJob(clonedJob);

            // ── Step 2: Clone Jobs.JobDisplayOptions ──
            var sourceDisplay = await _repo.GetSourceDisplayOptionsAsync(request.SourceJobId, ct);
            if (sourceDisplay != null)
            {
                var clonedDisplay = CloneDisplayOptions(sourceDisplay, newJobId, sourceJob.Year, request, superUserId, now);
                _repo.AddDisplayOptions(clonedDisplay);
            }

            // ── Step 3: Clone Jobs.JobOwlImages ──
            var sourceOwl = await _repo.GetSourceOwlImagesAsync(request.SourceJobId, ct);
            if (sourceOwl != null)
            {
                var clonedOwl = CloneOwlImages(sourceOwl, newJobId, superUserId, now);
                _repo.AddOwlImages(clonedOwl);
            }

            // ── Step 4: Clone Jobs.Bulletins (year-delta shift) ──
            var sourceBulletins = await _repo.GetSourceBulletinsAsync(request.SourceJobId, ct);
            if (sourceBulletins.Count > 0)
            {
                var clonedBulletins = CloneBulletins(sourceBulletins, newJobId, superUserId, now, yearDelta);
                _repo.AddBulletins(clonedBulletins);
                summary.BulletinsCloned = clonedBulletins.Count;
            }

            // ── Step 5: Clone Jobs.JobAgeRanges ──
            var sourceRanges = await _repo.GetSourceAgeRangesAsync(request.SourceJobId, ct);
            if (sourceRanges.Count > 0)
            {
                var clonedRanges = CloneAgeRanges(sourceRanges, newJobId, superUserId, now);
                _repo.AddAgeRanges(clonedRanges);
                summary.AgeRangesCloned = clonedRanges.Count;
            }

            // ── Steps 6–7: Clone Jobs.JobMenus + JobMenuItems ──
            var sourceMenus = await _repo.GetSourceMenusWithItemsAsync(request.SourceJobId, ct);
            if (sourceMenus.Count > 0)
            {
                var (clonedMenus, clonedItems) = CloneMenus(sourceMenus, newJobId, superUserId, now);
                foreach (var menu in clonedMenus)
                    _repo.AddMenu(menu);
                if (clonedItems.Count > 0)
                    _repo.AddMenuItems(clonedItems);
                summary.MenusCloned = clonedMenus.Count;
                summary.MenuItemsCloned = clonedItems.Count;
            }

            // ── Step 8: Clone admin Registrations (Director/SuperDirector forced inactive) ──
            var sourceRegs = await _repo.GetSourceAdminRegistrationsAsync(request.SourceJobId, ct);
            if (sourceRegs.Count > 0)
            {
                var clonedRegs = CloneAdminRegistrations(sourceRegs, newJobId, superUserId, now);
                _repo.AddRegistrations(clonedRegs);
                summary.AdminRegistrationsCloned = clonedRegs.Count;
            }

            // ── Steps 9–12: Clone LAD hierarchy ──
            var agegroupIdMap = new Dictionary<Guid, Guid>();
            var sourceLeague = await _repo.GetSourceLeagueAsync(request.SourceJobId, ct);
            if (sourceLeague != null)
            {
                var sourceSeasonForAgegroups = sourceJob.Season;
                var newLeagueId = Guid.NewGuid();

                // Step 9: Clone League (with name inference from source pattern)
                var clonedLeague = CloneLeague(sourceLeague, newLeagueId, request, superUserId, now);
                _repo.AddLeague(clonedLeague);
                summary.LeaguesCloned = 1;

                // Step 10: Link Job ↔ League — carry forward per-league fee fields from the
                // source JobLeagues row (BaseFee + discount/late windows), shifting window
                // dates by year-delta so they don't land on last year's schedule.
                var sourceJobLeague = await _repo.GetSourceJobLeagueAsync(
                    request.SourceJobId, sourceLeague.LeagueId, ct);

                var jobLeague = new JobLeagues
                {
                    JobLeagueId = Guid.NewGuid(),
                    JobId = newJobId,
                    LeagueId = newLeagueId,
                    BIsPrimary = true,
                    BaseFee = sourceJobLeague?.BaseFee,
                    DiscountFee = sourceJobLeague?.DiscountFee,
                    DiscountFeeStart = ShiftByYears(sourceJobLeague?.DiscountFeeStart, yearDelta),
                    DiscountFeeEnd = ShiftByYears(sourceJobLeague?.DiscountFeeEnd, yearDelta),
                    LateFee = sourceJobLeague?.LateFee,
                    LateFeeStart = ShiftByYears(sourceJobLeague?.LateFeeStart, yearDelta),
                    LateFeeEnd = ShiftByYears(sourceJobLeague?.LateFeeEnd, yearDelta),
                    LebUserId = superUserId,
                    Modified = now,
                };
                _repo.AddJobLeague(jobLeague);

                // Step 11: Clone Agegroups (age-bump + DOB shift + fee-window shift)
                var sourceAgegroups = await _repo.GetSourceAgegroupsAsync(sourceLeague.LeagueId, sourceSeasonForAgegroups, ct);

                if (sourceAgegroups.Count > 0)
                {
                    var clonedAgegroups = CloneAgegroups(
                        sourceAgegroups, newLeagueId, request, superUserId, now, agegroupIdMap, yearDelta);
                    _repo.AddAgegroups(clonedAgegroups);
                    summary.AgegroupsCloned = clonedAgegroups.Count;
                }

                // Step 12: Clone Divisions (remapping AgegroupId)
                if (agegroupIdMap.Count > 0)
                {
                    var sourceAgegroupIds = agegroupIdMap.Keys.ToList();
                    var sourceDivisions = await _repo.GetSourceDivisionsAsync(sourceAgegroupIds, ct);
                    if (sourceDivisions.Count > 0)
                    {
                        var clonedDivisions = CloneDivisions(sourceDivisions, agegroupIdMap, superUserId, now);
                        _repo.AddDivisions(clonedDivisions);
                        summary.DivisionsCloned = clonedDivisions.Count;
                    }
                }
            }

            // ── Step 13: Clone fees.JobFees (agegroup remap + FeeModifier year-delta shift) ──
            var sourceFees = await _feeRepo.GetJobFeesByJobAsync(request.SourceJobId, ct);
            if (sourceFees.Count > 0)
            {
                var feesCloned = 0;
                var modifiersCloned = 0;
                foreach (var sourceFee in sourceFees)
                {
                    // Remap agegroup ID if present
                    Guid? newAgegroupId = null;
                    if (sourceFee.AgegroupId.HasValue)
                    {
                        if (!agegroupIdMap.TryGetValue(sourceFee.AgegroupId.Value, out var mapped))
                            continue; // agegroup wasn't cloned (filtered out) — skip this fee row
                        newAgegroupId = mapped;
                    }

                    // Team-level fees are NOT cloned in LAD/no-LADT mode (teams aren't cloned).
                    // Phase D adds LADT mode that also clones teams + their fee rows with team remap.
                    if (sourceFee.TeamId.HasValue)
                        continue;

                    var newFeeId = Guid.NewGuid();
                    _feeRepo.Add(new JobFees
                    {
                        JobFeeId = newFeeId,
                        JobId = newJobId,
                        RoleId = sourceFee.RoleId,
                        AgegroupId = newAgegroupId,
                        TeamId = null,
                        Deposit = sourceFee.Deposit,
                        BalanceDue = sourceFee.BalanceDue,
                        Modified = now,
                        LebUserId = superUserId
                    });
                    feesCloned++;

                    // Clone modifiers for this fee row — shift window dates by year-delta.
                    if (sourceFee.FeeModifiers != null)
                    {
                        foreach (var mod in sourceFee.FeeModifiers)
                        {
                            _feeRepo.AddModifier(new FeeModifiers
                            {
                                FeeModifierId = Guid.NewGuid(),
                                JobFeeId = newFeeId,
                                ModifierType = mod.ModifierType,
                                Amount = mod.Amount,
                                StartDate = ShiftByYears(mod.StartDate, yearDelta),
                                EndDate = ShiftByYears(mod.EndDate, yearDelta),
                                Modified = now,
                                LebUserId = superUserId
                            });
                            modifiersCloned++;
                        }
                    }
                }
                summary.FeesCloned = feesCloned;
                _logger.LogInformation(
                    "Cloned {FeesCloned} fee rows + {ModifiersCloned} modifiers",
                    feesCloned, modifiersCloned);
            }

            // ── Flush + commit ──
            await _repo.SaveChangesAsync(ct);
            await _repo.CommitTransactionAsync(ct);

            _logger.LogInformation(
                "Job clone complete: {NewJobPath} ({NewJobId}) — {Summary}",
                request.JobPathTarget, newJobId, summary);

            return new JobCloneResponse
            {
                NewJobId = newJobId,
                NewJobPath = request.JobPathTarget,
                NewJobName = request.JobNameTarget,
                Summary = summary.Build(),
            };
        }
        catch
        {
            await _repo.RollbackTransactionAsync(ct);
            throw;
        }
    }

    // ══════════════════════════════════════════════════════════
    // Blank-job creation (new-customer onboarding)
    // ══════════════════════════════════════════════════════════

    public async Task<BlankJobResponse> CreateBlankJobAsync(
        BlankJobRequest request,
        string authorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        // Same-customer guard — authorCustomerId=null means SuperUser (can target any customer).
        if (authorCustomerId.HasValue && authorCustomerId.Value != request.CustomerId)
        {
            throw new UnauthorizedAccessException(
                "Cannot create a blank job for a different customer.");
        }

        if (await _repo.JobPathExistsAsync(request.JobPathTarget, ct))
            throw new InvalidOperationException($"Job path '{request.JobPathTarget}' already exists.");

        var now = DateTime.UtcNow;
        var newJobId = Guid.NewGuid();

        await _repo.BeginTransactionAsync(ct);
        try
        {
            _logger.LogInformation(
                "Creating blank job {TargetPath} for customer {CustomerId} (author {AuthorUserId})",
                request.JobPathTarget, request.CustomerId, authorUserId);

            var blankJob = BuildBlankJob(request, newJobId, authorUserId, now);
            _repo.AddJob(blankJob);

            // Author's own admin Registration — active (they need to configure the job).
            // Role inferred from controller gate: SuperUser-only endpoint means Superuser role.
            // Phase D opens this up; at that point the controller supplies the correct role.
            var authorReg = new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                JobId = newJobId,
                RoleId = RoleConstants.Superuser,
                UserId = authorUserId,
                BActive = true,
                BConfirmationSent = false,
                RegistrationTs = now,
                CustomerId = request.CustomerId,
                LebUserId = authorUserId,
                Modified = now,
                FeeBase = 0,
                FeeProcessing = 0,
                FeeDiscount = 0,
                FeeDiscountMp = 0,
                FeeDonation = 0,
                FeeLatefee = 0,
                FeeTotal = 0,
                OwedTotal = 0,
                PaidTotal = 0,
            };
            _repo.AddRegistrations(new[] { authorReg });

            await _repo.SaveChangesAsync(ct);
            await _repo.CommitTransactionAsync(ct);

            _logger.LogInformation(
                "Blank job created: {NewJobPath} ({NewJobId})",
                request.JobPathTarget, newJobId);

            return new BlankJobResponse
            {
                NewJobId = newJobId,
                NewJobPath = request.JobPathTarget,
                NewJobName = request.JobNameTarget,
            };
        }
        catch
        {
            await _repo.RollbackTransactionAsync(ct);
            throw;
        }
    }

    private static Jobs BuildBlankJob(BlankJobRequest req, Guid newJobId, string userId, DateTime now)
    {
        return new Jobs
        {
            JobId = newJobId,
            JobPath = req.JobPathTarget,
            JobName = req.JobNameTarget,
            JobDescription = req.JobNameTarget,
            Year = req.YearTarget,
            Season = req.SeasonTarget,
            DisplayName = req.DisplayName,
            CustomerId = req.CustomerId,
            BillingTypeId = req.BillingTypeId,
            JobTypeId = req.JobTypeId,
            SportId = req.SportId,
            ExpiryAdmin = req.ExpiryAdmin,
            ExpiryUsers = req.ExpiryUsers,
            RegFormFrom = req.RegFormFrom,
            Modified = now,
            LebUserId = userId,

            // Safe-by-default state (same contract as clone)
            BSuspendPublic = true,
            BClubRepAllowEdit = true,
            BClubRepAllowDelete = true,
            BClubRepAllowAdd = true,
            ProcessingFeePercent = FeeConstants.MinProcessingFeePercent,

            // Required strings — sensible defaults (wizard can override later)
            RegformNamePlayer = "Player Registration",
            RegformNameTeam = "Team Registration",
            RegformNameCoach = "Coach Registration",
            RegformNameClubRep = "Club Representative Registration",
        };
    }

    // ══════════════════════════════════════════════════════════
    // Clone preview (dry-run transforms; no writes)
    // ══════════════════════════════════════════════════════════

    public async Task<JobClonePreviewResponse> PreviewCloneAsync(
        JobCloneRequest request,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        var sourceJob = await _repo.GetSourceJobAsync(request.SourceJobId, ct)
            ?? throw new KeyNotFoundException($"Source job {request.SourceJobId} not found.");

        if (authorCustomerId.HasValue && authorCustomerId.Value != sourceJob.CustomerId)
        {
            throw new UnauthorizedAccessException(
                "Cross-customer cloning is not allowed. Use the Blank-job flow to onboard a new customer.");
        }

        var yearDelta = ComputeYearDelta(sourceJob.Year, request.YearTarget);

        // League name: use the author-entered value verbatim. The wizard seeds it from
        // the source league name (year-bumped when auto-advance is on), so whatever
        // reaches the server here is already what we want to persist.
        var sourceLeague = await _repo.GetSourceLeagueAsync(request.SourceJobId, ct);
        var inferredLeagueName = request.LeagueNameTarget;

        // Bulletins — year-delta shifted.
        var sourceBulletins = await _repo.GetSourceBulletinsAsync(request.SourceJobId, ct);
        var bulletinShifts = sourceBulletins.Select(b => new BulletinShiftDto
        {
            SourceBulletinId = b.BulletinId,
            Title = b.Title,
            CreateDate = new DateShiftDto { From = b.CreateDate, To = ShiftByYears(b.CreateDate, yearDelta) },
            StartDate = b.StartDate.HasValue
                ? new DateShiftDto { From = b.StartDate, To = ShiftByYears(b.StartDate, yearDelta) }
                : null,
            EndDate = b.EndDate.HasValue
                ? new DateShiftDto { From = b.EndDate, To = ShiftByYears(b.EndDate, yearDelta) }
                : null,
        }).ToList();

        // Agegroups — name/grad-year bumps (when UpAgegroupNamesByOne), DOB + fee-window shifts.
        var agegroupPreviews = new List<AgegroupPreviewDto>();
        var sourceAgegroupIds = new List<Guid>();
        if (sourceLeague != null)
        {
            var sourceAgegroups = await _repo.GetSourceAgegroupsAsync(sourceLeague.LeagueId, sourceJob.Season, ct);
            foreach (var ag in sourceAgegroups)
            {
                sourceAgegroupIds.Add(ag.AgegroupId);

                var newName = ag.AgegroupName;
                int? newGradMin = ag.GradYearMin;
                int? newGradMax = ag.GradYearMax;
                DateOnly? newDobMin = ag.DobMin;
                DateOnly? newDobMax = ag.DobMax;

                if (request.UpAgegroupNamesByOne)
                {
                    if (!string.IsNullOrEmpty(newName))
                        newName = IncrementYearsInName(newName);
                    if (newGradMin.HasValue) newGradMin = newGradMin.Value + 1;
                    if (newGradMax.HasValue) newGradMax = newGradMax.Value + 1;
                    newDobMin = ShiftByYears(newDobMin, 1);
                    newDobMax = ShiftByYears(newDobMax, 1);
                }

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
                    DiscountFeeStart = ShiftDto(ag.DiscountFeeStart, yearDelta),
                    DiscountFeeEnd = ShiftDto(ag.DiscountFeeEnd, yearDelta),
                    LateFeeStart = ShiftDto(ag.LateFeeStart, yearDelta),
                    LateFeeEnd = ShiftDto(ag.LateFeeEnd, yearDelta),
                });
            }
        }

        // FeeModifier windows — year-delta shifted.
        var feeModifierShifts = new List<FeeModifierShiftDto>();
        var sourceFees = await _feeRepo.GetJobFeesByJobAsync(request.SourceJobId, ct);
        foreach (var fee in sourceFees)
        {
            if (fee.FeeModifiers == null) continue;
            foreach (var mod in fee.FeeModifiers)
            {
                feeModifierShifts.Add(new FeeModifierShiftDto
                {
                    SourceFeeModifierId = mod.FeeModifierId,
                    ModifierType = mod.ModifierType,
                    Amount = mod.Amount,
                    StartDate = ShiftDto(mod.StartDate, yearDelta),
                    EndDate = ShiftDto(mod.EndDate, yearDelta),
                });
            }
        }

        // Admin deactivation counts — Director/SuperDirector → deactivated; Superuser → preserved.
        var sourceRegs = await _repo.GetSourceAdminRegistrationsAsync(request.SourceJobId, ct);
        var toDeactivate = sourceRegs.Count(r =>
            string.Equals(r.RoleId, RoleConstants.Director, StringComparison.OrdinalIgnoreCase)
            || string.Equals(r.RoleId, RoleConstants.SuperDirector, StringComparison.OrdinalIgnoreCase));
        var preserved = sourceRegs.Count - toDeactivate;

        return new JobClonePreviewResponse
        {
            YearDelta = yearDelta,
            InferredLeagueName = inferredLeagueName,
            CurrentProcessingFeePercent = FeeConstants.MinProcessingFeePercent,
            SourceProcessingFeePercent = sourceJob.ProcessingFeePercent,
            EventStartShift = ShiftDto(sourceJob.EventStartDate, yearDelta),
            EventEndShift = ShiftDto(sourceJob.EventEndDate, yearDelta),
            AdnArbStartShift = ShiftDto(sourceJob.AdnArbstartDate, yearDelta),
            AdminsToDeactivate = toDeactivate,
            AdminsPreserved = preserved,
            Bulletins = bulletinShifts,
            Agegroups = agegroupPreviews,
            FeeModifiers = feeModifierShifts,
        };
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
            From = from.HasValue ? from.Value.ToDateTime(TimeOnly.MinValue) : null,
            To = to.HasValue ? to.Value.ToDateTime(TimeOnly.MinValue) : null,
        };
    }

    // ══════════════════════════════════════════════════════════
    // Release ops (site toggle + admin activation)
    // ══════════════════════════════════════════════════════════

    public async Task<ReleaseResponse> ReleaseSiteAsync(
        Guid jobId,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        var job = await _repo.GetJobForUpdateAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        if (authorCustomerId.HasValue && authorCustomerId.Value != job.CustomerId)
            throw new UnauthorizedAccessException("Cannot release a job for a different customer.");

        var now = DateTime.UtcNow;
        job.BSuspendPublic = false;
        job.Modified = now;
        job.LebUserId = actorUserId;

        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Released site for job {JobId} (actor {ActorUserId})", jobId, actorUserId);

        return new ReleaseResponse
        {
            JobId = jobId,
            BSuspendPublic = false,
            AdminsActivated = 0,
        };
    }

    public async Task<ReleaseResponse> ReleaseAdminsAsync(
        Guid jobId,
        IList<Guid> registrationIds,
        string actorUserId,
        Guid? authorCustomerId = null,
        CancellationToken ct = default)
    {
        if (registrationIds == null || registrationIds.Count == 0)
            return new ReleaseResponse { JobId = jobId, BSuspendPublic = false, AdminsActivated = 0 };

        var job = await _repo.GetJobForUpdateAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        if (authorCustomerId.HasValue && authorCustomerId.Value != job.CustomerId)
            throw new UnauthorizedAccessException("Cannot release admins for a different customer's job.");

        // Repo filters on jobId — any reg IDs that don't belong to this job simply aren't returned.
        // That's the authorization boundary: you can only activate regs that belong to jobs you can access.
        var regs = await _repo.GetRegistrationsForUpdateAsync(jobId, registrationIds, ct);

        if (regs.Count != registrationIds.Count)
        {
            var found = regs.Select(r => r.RegistrationId).ToHashSet();
            var missing = registrationIds.Where(id => !found.Contains(id)).ToList();
            throw new UnauthorizedAccessException(
                $"Registration(s) not found on job {jobId}: {string.Join(", ", missing)}");
        }

        var now = DateTime.UtcNow;
        foreach (var r in regs)
        {
            r.BActive = true;
            r.Modified = now;
            r.LebUserId = actorUserId;
        }

        await _repo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Activated {Count} admins on job {JobId} (actor {ActorUserId})",
            regs.Count, jobId, actorUserId);

        return new ReleaseResponse
        {
            JobId = jobId,
            BSuspendPublic = job.BSuspendPublic,
            AdminsActivated = regs.Count,
        };
    }

    public Task<List<ReleasableAdminDto>> GetReleasableAdminsAsync(
        Guid jobId, CancellationToken ct = default)
    {
        return _repo.GetReleasableAdminsAsync(jobId, ct);
    }

    public Task<List<SuspendedJobDto>> GetSuspendedJobsAsync(
        Guid? authorCustomerId = null, CancellationToken ct = default)
    {
        return _repo.GetSuspendedJobsAsync(authorCustomerId, ct);
    }

    public Task<bool> JobPathExistsAsync(string jobPath, CancellationToken ct = default)
    {
        return _repo.JobPathExistsAsync(jobPath, ct);
    }

    public Task<bool> JobNameExistsAsync(string jobName, CancellationToken ct = default)
    {
        return _repo.JobNameExistsAsync(jobName, ct);
    }

    // ══════════════════════════════════════════════════════════
    // Private clone helpers
    // ══════════════════════════════════════════════════════════

    private static Jobs CloneJob(
        Jobs source, Guid newJobId, JobCloneRequest req, string userId, DateTime now, int yearDelta)
    {
        return new Jobs
        {
            JobId = newJobId,
            JobPath = req.JobPathTarget,
            JobName = req.JobNameTarget,
            JobDescription = req.JobNameTarget,
            Year = req.YearTarget,
            Season = req.SeasonTarget,
            ExpiryAdmin = req.ExpiryAdmin,
            ExpiryUsers = req.ExpiryUsers,
            DisplayName = req.DisplayName,
            CustomerId = source.CustomerId,
            RegFormFrom = req.RegFormFrom ?? source.RegFormFrom,
            Modified = now,
            LebUserId = userId,

            // ── SAFE-BY-DEFAULT STATE ──
            // Public suspended until author releases.
            BSuspendPublic = true,
            // ClubRep edit/delete/add forced ON (lifecycle reset — source may have them off
            // due to post-schedule lockdown; new clone starts in registration phase).
            BClubRepAllowEdit = true,
            BClubRepAllowDelete = true,
            BClubRepAllowAdd = true,
            // Processing rate reset to current floor (avoids stale-rate carryover).
            // Phase D wizard exposes Copy source / Use current / Custom.
            ProcessingFeePercent = FeeConstants.MinProcessingFeePercent,

            // ── Year-delta shifted date fields ──
            EventStartDate = ShiftByYears(source.EventStartDate, yearDelta),
            EventEndDate = ShiftByYears(source.EventEndDate, yearDelta),
            AdnArbstartDate = ShiftByYears(source.AdnArbstartDate, yearDelta),

            // ── Copy all other config from source ──
            BillingTypeId = source.BillingTypeId,
            JobTypeId = source.JobTypeId,
            SportId = source.SportId,
            BAllowRosterViewAdult = source.BAllowRosterViewAdult,
            BAllowRosterViewPlayer = source.BAllowRosterViewPlayer,
            BBannerIsCustom = source.BBannerIsCustom,
            PaymentMethodsAllowedCode = source.PaymentMethodsAllowedCode,
            BAddProcessingFees = source.BAddProcessingFees,
            Balancedueaspercent = source.Balancedueaspercent,
            BannerFile = source.BannerFile,
            JobTagline = source.JobTagline,
            MailTo = source.MailTo,
            MailinPaymentWarning = source.MailinPaymentWarning,
            PayTo = source.PayTo,
            PerMonthCharge = source.PerMonthCharge,
            PerPlayerCharge = source.PerPlayerCharge,
            PerSalesPercentCharge = source.PerSalesPercentCharge,
            PerTeamCharge = source.PerTeamCharge,
            SearchenginKeywords = source.SearchenginKeywords,
            SearchengineDescription = source.SearchengineDescription,
            PlayerRegConfirmationEmail = source.PlayerRegConfirmationEmail,
            PlayerRegConfirmationOnScreen = source.PlayerRegConfirmationOnScreen,
            PlayerRegRefundPolicy = source.PlayerRegRefundPolicy,
            PlayerRegReleaseOfLiability = source.PlayerRegReleaseOfLiability,
            PlayerRegCodeOfConduct = source.PlayerRegCodeOfConduct,
            PlayerRegMultiPlayerDiscountMin = source.PlayerRegMultiPlayerDiscountMin,
            PlayerRegMultiPlayerDiscountPercent = source.PlayerRegMultiPlayerDiscountPercent,
            AdultRegConfirmationEmail = source.AdultRegConfirmationEmail,
            AdultRegConfirmationOnScreen = source.AdultRegConfirmationOnScreen,
            AdultRegRefundPolicy = source.AdultRegRefundPolicy,
            AdultRegReleaseOfLiability = source.AdultRegReleaseOfLiability,
            AdultRegCodeOfConduct = source.AdultRegCodeOfConduct,
            RegformNamePlayer = source.RegformNamePlayer,
            RegformNameTeam = source.RegformNameTeam,
            RegformNameCoach = source.RegformNameCoach,
            RegformNameClubRep = source.RegformNameClubRep,
            RegFormBccs = source.RegFormBccs,
            RegFormCcs = source.RegFormCcs,
            JobNameQbp = source.JobNameQbp,
            BTeamsFullPaymentRequired = source.BTeamsFullPaymentRequired,
            BRestrictPlayerTeamsToAgerange = source.BRestrictPlayerTeamsToAgerange,
            Rescheduleemaillist = source.Rescheduleemaillist,
            Alwayscopyemaillist = source.Alwayscopyemaillist,
            BAllowMobileLogin = source.BAllowMobileLogin,
            JsonOptions = source.JsonOptions,
            CoreRegformPlayer = source.CoreRegformPlayer,
            RefereeRegConfirmationEmail = source.RefereeRegConfirmationEmail,
            RefereeRegConfirmationOnScreen = source.RefereeRegConfirmationOnScreen,
            RecruiterRegConfirmationEmail = source.RecruiterRegConfirmationEmail,
            RecruiterRegConfirmationOnScreen = source.RecruiterRegConfirmationOnScreen,
            BTeamPushDirectors = source.BTeamPushDirectors,
            BShowTeamNameOnlyInSchedules = source.BShowTeamNameOnlyInSchedules,
            UslaxNumberValidThroughDate = source.UslaxNumberValidThroughDate,
            MomLabel = source.MomLabel,
            DadLabel = source.DadLabel,
            BUseWaitlists = source.BUseWaitlists,
            BScheduleAllowPublicAccess = source.BScheduleAllowPublicAccess,
            BRegistrationAllowPlayer = source.BRegistrationAllowPlayer,
            BRegistrationAllowTeam = source.BRegistrationAllowTeam,
            BAllowRefundsInPriorMonths = source.BAllowRefundsInPriorMonths,
            BAllowCreditAll = source.BAllowCreditAll,
            PlayerRegCovid19Waiver = source.PlayerRegCovid19Waiver,
            AdnArb = source.AdnArb,
            AdnArbbillingOccurences = source.AdnArbbillingOccurences,
            AdnArbintervalLength = source.AdnArbintervalLength,
            AdnArbMinimunTotalCharge = source.AdnArbMinimunTotalCharge,
            MobileScoreHoursPastGameEligible = source.MobileScoreHoursPastGameEligible,
            BSignalRschedule = source.BSignalRschedule,
            BDisallowCcplayerConfirmations = source.BDisallowCcplayerConfirmations,
            JobCode = source.JobCode,
            BAllowMobileRegn = source.BAllowMobileRegn,
            BApplyProcessingFeesToTeamDeposit = source.BApplyProcessingFeesToTeamDeposit,
            BOfferPlayerRegsaverInsurance = source.BOfferPlayerRegsaverInsurance,
            BEnableTsicteams = source.BEnableTsicteams,
            BEnableMobileRsvp = source.BEnableMobileRsvp,
            BEnableStore = source.BEnableStore,
            MobileJobName = source.MobileJobName,
            StoreSalesTax = source.StoreSalesTax,
            StoreRefundPolicy = source.StoreRefundPolicy,
            StorePickupDetails = source.StorePickupDetails,
            StoreContactEmail = source.StoreContactEmail,
            BOfferTeamRegsaverInsurance = source.BOfferTeamRegsaverInsurance,
            BEnableMobileTeamChat = source.BEnableMobileTeamChat,
            PlayerProfileMetadataJson = source.PlayerProfileMetadataJson,
            BenableStp = source.BenableStp,
            StoreTsicrate = source.StoreTsicrate,
            // JobAi — auto-increment, let DB assign
            // UpdatedOn — rowversion, let DB assign
        };
    }

    private static JobDisplayOptions CloneDisplayOptions(
        JobDisplayOptions source, Guid newJobId, string? sourceYear, JobCloneRequest req, string userId, DateTime now)
    {
        var cloned = new JobDisplayOptions
        {
            JobId = newJobId,
            ParallaxBackgroundImage = source.ParallaxBackgroundImage,
            ParallaxSlideCount = source.ParallaxSlideCount,
            ParallaxSlide1Image = source.ParallaxSlide1Image,
            ParallaxSlide1Text1 = source.ParallaxSlide1Text1,
            ParallaxSlide1Text2 = source.ParallaxSlide1Text2,
            ParallaxSlide2Image = source.ParallaxSlide2Image,
            ParallaxSlide2Text1 = source.ParallaxSlide2Text1,
            ParallaxSlide2Text2 = source.ParallaxSlide2Text2,
            ParallaxSlide3Image = source.ParallaxSlide3Image,
            ParallaxSlide3Text1 = source.ParallaxSlide3Text1,
            ParallaxSlide3Text2 = source.ParallaxSlide3Text2,
            LogoHeader = source.LogoHeader,
            LogoFooter = source.LogoFooter,
            BlockRecentWorks = source.BlockRecentWorks,
            BlockRecentImage1 = source.BlockRecentImage1,
            BlockRecentImage2 = source.BlockRecentImage2,
            BlockRecentImage3 = source.BlockRecentImage3,
            BlockRecentImage4 = source.BlockRecentImage4,
            BlockPurchase = source.BlockPurchase,
            BlockService = source.BlockService,
            LebUserId = userId,
            Modified = now,
        };

        // Replace source year with target year in parallax text.
        // Phase D will make this conditional on the wizard's auto-advance toggle.
        if (!string.IsNullOrEmpty(sourceYear) && !string.IsNullOrEmpty(cloned.ParallaxSlide1Text1))
            cloned.ParallaxSlide1Text1 = cloned.ParallaxSlide1Text1.Replace(sourceYear, req.YearTarget);

        // NoParallaxSlide1 flag: clear slide 1
        if (req.NoParallaxSlide1)
        {
            cloned.ParallaxSlide1Image = null;
            cloned.ParallaxSlideCount = 0;
        }

        return cloned;
    }

    private static JobOwlImages CloneOwlImages(
        JobOwlImages source, Guid newJobId, string userId, DateTime now)
    {
        return new JobOwlImages
        {
            JobId = newJobId,
            Caption = source.Caption,
            OwlSlideCount = source.OwlSlideCount,
            OwlImage01 = source.OwlImage01,
            OwlImage02 = source.OwlImage02,
            OwlImage03 = source.OwlImage03,
            OwlImage04 = source.OwlImage04,
            OwlImage05 = source.OwlImage05,
            OwlImage06 = source.OwlImage06,
            OwlImage07 = source.OwlImage07,
            OwlImage08 = source.OwlImage08,
            OwlImage09 = source.OwlImage09,
            OwlImage10 = source.OwlImage10,
            LebUserId = userId,
            Modified = now,
        };
    }

    private static List<Bulletins> CloneBulletins(
        List<Bulletins> sources, Guid newJobId, string userId, DateTime now, int yearDelta)
    {
        // Year-delta shift — preserves seasonal cadence. DateTime.AddYears clamps Feb-29 to Feb-28 in non-leap years.
        return sources.Select(b => new Bulletins
        {
            BulletinId = Guid.NewGuid(),
            JobId = newJobId,
            Title = b.Title,
            Text = b.Text,
            Active = b.Active,
            ExpireHours = b.ExpireHours,
            CreateDate = ShiftByYears(b.CreateDate, yearDelta),
            StartDate = ShiftByYears(b.StartDate, yearDelta),
            EndDate = ShiftByYears(b.EndDate, yearDelta),
            Bcore = b.Bcore,
            LebUserId = userId,
            Modified = now,
        }).ToList();
    }

    private static List<JobAgeRanges> CloneAgeRanges(
        List<JobAgeRanges> sources, Guid newJobId, string userId, DateTime now)
    {
        return sources.Select(r => new JobAgeRanges
        {
            // AgeRangeId — auto-increment, let DB assign
            JobId = newJobId,
            RangeName = r.RangeName,
            RangeLeft = r.RangeLeft,
            RangeRight = r.RangeRight,
            LebUserId = userId,
            Modified = now,
        }).ToList();
    }

    private static (List<JobMenus> Menus, List<JobMenuItems> Items) CloneMenus(
        List<JobMenus> sourceMenus, Guid newJobId, string userId, DateTime now)
    {
        var clonedMenus = new List<JobMenus>();
        var clonedItems = new List<JobMenuItems>();

        foreach (var sourceMenu in sourceMenus)
        {
            var newMenuId = Guid.NewGuid();
            clonedMenus.Add(new JobMenus
            {
                MenuId = newMenuId,
                JobId = newJobId,
                MenuTypeId = sourceMenu.MenuTypeId,
                RoleId = sourceMenu.RoleId,
                Active = sourceMenu.Active,
                Tag = sourceMenu.Tag,
                LebUserId = userId,
                Modified = now,
            });

            // Build ID mapping for parent→child relationship
            var menuItemIdMap = new Dictionary<Guid, Guid>();

            // Clone top-level items first (ParentMenuItemId IS NULL)
            var topLevel = sourceMenu.JobMenuItems
                .Where(i => i.ParentMenuItemId == null)
                .ToList();

            foreach (var item in topLevel)
            {
                var newItemId = Guid.NewGuid();
                menuItemIdMap[item.MenuItemId] = newItemId;

                clonedItems.Add(CloneMenuItem(item, newItemId, newMenuId, null, userId, now));
            }

            // Clone child items (ParentMenuItemId IS NOT NULL)
            var children = sourceMenu.JobMenuItems
                .Where(i => i.ParentMenuItemId != null)
                .ToList();

            foreach (var child in children)
            {
                var newChildId = Guid.NewGuid();
                var newParentId = child.ParentMenuItemId.HasValue
                    && menuItemIdMap.TryGetValue(child.ParentMenuItemId.Value, out var mappedParent)
                    ? mappedParent
                    : (Guid?)null;

                clonedItems.Add(CloneMenuItem(child, newChildId, newMenuId, newParentId, userId, now));
            }
        }

        return (clonedMenus, clonedItems);
    }

    private static JobMenuItems CloneMenuItem(
        JobMenuItems source, Guid newItemId, Guid newMenuId, Guid? newParentId, string userId, DateTime now)
    {
        return new JobMenuItems
        {
            MenuItemId = newItemId,
            MenuId = newMenuId,
            ParentMenuItemId = newParentId,
            Text = source.Text,
            NavigateUrl = source.NavigateUrl,
            RouterLink = source.RouterLink,
            IconName = source.IconName,
            ImageUrl = source.ImageUrl,
            Action = source.Action,
            Controller = source.Controller,
            Target = source.Target,
            ReportName = source.ReportName,
            ReportExportTypeId = source.ReportExportTypeId,
            Active = source.Active,
            BCollapsed = source.BCollapsed,
            BTextWrap = source.BTextWrap,
            Index = source.Index,
            LebUserId = userId,
            Modified = now,
        };
    }

    private static List<Registrations> CloneAdminRegistrations(
        List<Registrations> sources, Guid newJobId, string userId, DateTime now)
    {
        // Safe-by-default admin lockdown:
        //   - Director + SuperDirector → forced BActive = false (customer admins locked until release)
        //   - Superuser → BActive unchanged (TSIC-central, must stay functional for clone/QA work)
        return sources.Select(r =>
        {
            bool isCustomerAdmin =
                string.Equals(r.RoleId, RoleConstants.Director, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.RoleId, RoleConstants.SuperDirector, StringComparison.OrdinalIgnoreCase);

            return new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                // RegistrationAi — auto-increment, let DB assign
                JobId = newJobId,
                RoleId = r.RoleId,
                UserId = r.UserId,
                FamilyUserId = r.FamilyUserId,
                BActive = isCustomerAdmin ? false : r.BActive,
                BConfirmationSent = false,
                RegistrationTs = now,
                RegistrationCategory = r.RegistrationCategory,
                RegistrationGroupId = r.RegistrationGroupId,
                RegistrationFormName = r.RegistrationFormName,
                CustomerId = r.CustomerId,
                LebUserId = userId,
                Modified = now,
                // Zero out fee fields for admin registrations
                FeeBase = 0,
                FeeProcessing = 0,
                FeeDiscount = 0,
                FeeDiscountMp = 0,
                FeeDonation = 0,
                FeeLatefee = 0,
                FeeTotal = 0,
                OwedTotal = 0,
                PaidTotal = 0,
            };
        }).ToList();
    }

    private static Leagues CloneLeague(
        Leagues source, Guid newLeagueId, JobCloneRequest req, string userId, DateTime now)
    {
        // League name is author-entered on the wizard (seeded from source, year-bumped
        // when auto-advance is on). Persist verbatim.
        return new Leagues
        {
            LeagueId = newLeagueId,
            LeagueName = req.LeagueNameTarget,
            SportId = source.SportId,
            BAllowCoachScoreEntry = source.BAllowCoachScoreEntry,
            BHideContacts = source.BHideContacts,
            BHideStandings = source.BHideStandings,
            BShowScheduleToTeamMembers = source.BShowScheduleToTeamMembers,
            BTakeAttendance = source.BTakeAttendance,
            BTrackPenaltyMinutes = source.BTrackPenaltyMinutes,
            BTrackSportsmanshipScores = source.BTrackSportsmanshipScores,
            RescheduleEmailsToAddon = source.RescheduleEmailsToAddon,
            StrLop = source.StrLop,
            StrGradYears = source.StrGradYears,
            PointsMethod = source.PointsMethod,
            StandingsSortProfileId = source.StandingsSortProfileId,
            LebUserId = userId,
            Modified = now,
        };
    }

    private static List<Agegroups> CloneAgegroups(
        List<Agegroups> sources, Guid newLeagueId, JobCloneRequest req,
        string userId, DateTime now, Dictionary<Guid, Guid> agegroupIdMap, int yearDelta)
    {
        return sources.Select(ag =>
        {
            var newId = Guid.NewGuid();
            agegroupIdMap[ag.AgegroupId] = newId;

            var name = ag.AgegroupName;
            var gradYearMin = ag.GradYearMin;
            var gradYearMax = ag.GradYearMax;
            var dobMin = ag.DobMin;
            var dobMax = ag.DobMax;

            // Age-bump: name (year tokens), GradYear min/max, and DOB min/max all shift by +1 year.
            // DOB fix vs. prior version: previously only names + grad-years bumped, leaving DOB
            // windows on the source's year → silent age-mismatch. Now DOB shifts in lockstep.
            if (req.UpAgegroupNamesByOne)
            {
                if (!string.IsNullOrEmpty(name))
                    name = IncrementYearsInName(name);
                if (gradYearMin.HasValue)
                    gradYearMin = gradYearMin.Value + 1;
                if (gradYearMax.HasValue)
                    gradYearMax = gradYearMax.Value + 1;
                dobMin = ShiftByYears(dobMin, 1);
                dobMax = ShiftByYears(dobMax, 1);
            }

            return new Agegroups
            {
                AgegroupId = newId,
                LeagueId = newLeagueId,
                AgegroupName = name,
                Season = req.SeasonTarget,
                Gender = ag.Gender,
                Color = ag.Color,
                SortAge = ag.SortAge,
                MaxTeams = ag.MaxTeams,
                MaxTeamsPerClub = ag.MaxTeamsPerClub,
                DobMin = dobMin,
                DobMax = dobMax,
                SchoolGradeMin = ag.SchoolGradeMin,
                SchoolGradeMax = ag.SchoolGradeMax,
                GradYearMin = gradYearMin,
                GradYearMax = gradYearMax,
                BAllowSelfRostering = ag.BAllowSelfRostering,
                BChampionsByDivision = ag.BChampionsByDivision,
                BHideStandings = ag.BHideStandings,
                BAllowApiRosterAccess = ag.BAllowApiRosterAccess,
                // Legacy per-agegroup fee-window fields — copy amounts; autoadvance window dates by year-delta.
                // (Current fee model is fees.JobFees; TeamFee/RosterFee/label/PlayerFeeOverride dropped — no runtime readers.)
                LateFee = ag.LateFee,
                LateFeeStart = ShiftByYears(ag.LateFeeStart, yearDelta),
                LateFeeEnd = ShiftByYears(ag.LateFeeEnd, yearDelta),
                DiscountFee = ag.DiscountFee,
                DiscountFeeStart = ShiftByYears(ag.DiscountFeeStart, yearDelta),
                DiscountFeeEnd = ShiftByYears(ag.DiscountFeeEnd, yearDelta),
                LebUserId = userId,
                Modified = now,
            };
        }).ToList();
    }

    private static List<Divisions> CloneDivisions(
        List<Divisions> sources, Dictionary<Guid, Guid> agegroupIdMap, string userId, DateTime now)
    {
        return sources
            .Where(d => agegroupIdMap.ContainsKey(d.AgegroupId))
            .Select(d => new Divisions
            {
                DivId = Guid.NewGuid(),
                AgegroupId = agegroupIdMap[d.AgegroupId],
                DivName = d.DivName,
                MaxRoundNumberToShow = d.MaxRoundNumberToShow,
                LebUserId = userId,
                Modified = now,
            }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // Clone summary
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Mutable builder to accumulate clone summary counts.
    /// </summary>
    private sealed class CloneSummaryBuilder
    {
        public int BulletinsCloned { get; set; }
        public int AgeRangesCloned { get; set; }
        public int MenusCloned { get; set; }
        public int MenuItemsCloned { get; set; }
        public int AdminRegistrationsCloned { get; set; }
        public int LeaguesCloned { get; set; }
        public int AgegroupsCloned { get; set; }
        public int DivisionsCloned { get; set; }
        public int FeesCloned { get; set; }

        public CloneSummary Build() => new()
        {
            BulletinsCloned = BulletinsCloned,
            AgeRangesCloned = AgeRangesCloned,
            MenusCloned = MenusCloned,
            MenuItemsCloned = MenuItemsCloned,
            AdminRegistrationsCloned = AdminRegistrationsCloned,
            LeaguesCloned = LeaguesCloned,
            AgegroupsCloned = AgegroupsCloned,
            DivisionsCloned = DivisionsCloned,
            FeesCloned = FeesCloned,
        };

        public override string ToString() =>
            $"Bulletins={BulletinsCloned}, AgeRanges={AgeRangesCloned}, Menus={MenusCloned}, " +
            $"Items={MenuItemsCloned}, Admins={AdminRegistrationsCloned}, Leagues={LeaguesCloned}, " +
            $"Agegroups={AgegroupsCloned}, Divisions={DivisionsCloned}";
    }
}
