using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using static TSIC.API.Services.Admin.JobCloneTransforms;

// Alias the entity type to disambiguate from TSIC.API.Services.Teams namespace, which is in scope
// elsewhere in the assembly and otherwise causes CS0118 ambiguity at every Teams usage below.
using TeamsEntity = TSIC.Domain.Entities.Teams;

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
///   - ProcessingFeePercent / EcprocessingFeePercent: wizard Step 5 (Copy source / Use current floor / Custom)
///   - BEnableEcheck: wizard Step 5 (Off / Copy source); BEnableStore: wizard Step 5 (Disable / Keep)
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

    // ══════════════════════════════════════════════════════════
    // Choice validation + resolution (Step 4 + Step 5 wizard inputs)
    // ══════════════════════════════════════════════════════════

    private static readonly HashSet<string> ValidLadtScopes =
        new(StringComparer.OrdinalIgnoreCase) { "none", "lad", "ladt" };
    private static readonly HashSet<string> ValidFeeChoices =
        new(StringComparer.OrdinalIgnoreCase) { "source", "current", "custom" };
    private static readonly HashSet<string> ValidEnableEcheckChoices =
        new(StringComparer.OrdinalIgnoreCase) { "off", "source" };
    private static readonly HashSet<string> ValidStoreChoices =
        new(StringComparer.OrdinalIgnoreCase) { "keep", "disable" };

    private static void ValidateChoices(JobCloneRequest req)
    {
        if (!ValidLadtScopes.Contains(req.LadtScope))
            throw new ArgumentException($"Invalid LadtScope '{req.LadtScope}'. Expected: none, lad, ladt.");
        if (!ValidFeeChoices.Contains(req.ProcessingFeeChoice))
            throw new ArgumentException($"Invalid ProcessingFeeChoice '{req.ProcessingFeeChoice}'. Expected: source, current, custom.");
        if (!ValidFeeChoices.Contains(req.EcheckProcessingFeeChoice))
            throw new ArgumentException($"Invalid EcheckProcessingFeeChoice '{req.EcheckProcessingFeeChoice}'. Expected: source, current, custom.");
        if (!ValidEnableEcheckChoices.Contains(req.EnableEcheckChoice))
            throw new ArgumentException($"Invalid EnableEcheckChoice '{req.EnableEcheckChoice}'. Expected: off, source.");
        if (!ValidStoreChoices.Contains(req.StoreChoice))
            throw new ArgumentException($"Invalid StoreChoice '{req.StoreChoice}'. Expected: keep, disable.");

        // Custom choice requires a value in [Min, Max]; FeeConstants ranges enforced here so the
        // server is the single source of truth (FE may also validate but cannot be trusted).
        if (string.Equals(req.ProcessingFeeChoice, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (req.CustomProcessingFeePercent is not decimal v
                || v < FeeConstants.MinProcessingFeePercent
                || v > FeeConstants.MaxProcessingFeePercent)
            {
                throw new ArgumentException(
                    $"CustomProcessingFeePercent must be in [{FeeConstants.MinProcessingFeePercent}, {FeeConstants.MaxProcessingFeePercent}].");
            }
        }
        if (string.Equals(req.EcheckProcessingFeeChoice, "custom", StringComparison.OrdinalIgnoreCase))
        {
            if (req.CustomEcheckProcessingFeePercent is not decimal v
                || v < FeeConstants.MinEcprocessingFeePercent
                || v > FeeConstants.MaxEcprocessingFeePercent)
            {
                throw new ArgumentException(
                    $"CustomEcheckProcessingFeePercent must be in [{FeeConstants.MinEcprocessingFeePercent}, {FeeConstants.MaxEcprocessingFeePercent}].");
            }
        }
    }

    /// <summary>
    /// Resolves a fee-choice triple (choice / custom / source) into a final percent value,
    /// clamped to [min, max]. Used for both CC and eCheck rates.
    /// </summary>
    private static decimal ResolveProcessingFeePercent(
        string choice, decimal? custom, decimal? sourcePercent, decimal min, decimal max)
    {
        decimal raw = choice.ToLowerInvariant() switch
        {
            "source" => sourcePercent ?? min,
            "custom" => custom ?? min, // ValidateChoices already enforced the range
            _ => min, // "current"
        };
        return Math.Clamp(raw, min, max);
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

        // Validate + resolve user-driven defaults (Step 4 + Step 5 wizard inputs).
        ValidateChoices(request);

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
            // Track the executing actor's new Superuser registration so the response can drive
            // the post-clone JWT re-mint (FE log-into-new-job flow).
            Guid? actorNewRegistrationId = null;
            var sourceRegs = await _repo.GetSourceAdminRegistrationsAsync(request.SourceJobId, ct);
            List<Registrations> clonedRegs = [];
            if (sourceRegs.Count > 0)
            {
                clonedRegs = CloneAdminRegistrations(sourceRegs, newJobId, superUserId, now);
                _repo.AddRegistrations(clonedRegs);
                summary.AdminRegistrationsCloned = clonedRegs.Count;

                // Find the actor's row by UserId (their source registration that just got cloned).
                actorNewRegistrationId = clonedRegs
                    .FirstOrDefault(r => string.Equals(r.UserId, superUserId, StringComparison.OrdinalIgnoreCase))
                    ?.RegistrationId;
            }

            // If the actor had no Registration on the source job (Superusers are global by policy
            // — many source jobs won't list them as registered admins), create a fresh active
            // Superuser Registration for them on the new job. Guarantees the FE always has a
            // regId to switch into.
            if (actorNewRegistrationId == null)
            {
                var actorReg = new Registrations
                {
                    RegistrationId = Guid.NewGuid(),
                    JobId = newJobId,
                    RoleId = RoleConstants.Superuser,
                    UserId = superUserId,
                    BActive = true,
                    BConfirmationSent = false,
                    RegistrationTs = now,
                    CustomerId = sourceJob.CustomerId,
                    LebUserId = superUserId,
                    Modified = now,
                    FeeBase = 0, FeeProcessing = 0, FeeDiscount = 0, FeeDiscountMp = 0,
                    FeeDonation = 0, FeeLatefee = 0, FeeTotal = 0, OwedTotal = 0, PaidTotal = 0,
                };
                _repo.AddRegistrations([actorReg]);
                actorNewRegistrationId = actorReg.RegistrationId;
                summary.AdminRegistrationsCloned += 1;
            }

            // ── Steps 9–12: Clone LAD hierarchy ──
            // LadtScope="none" skips this entirely (author will configure post-release).
            // LadtScope="lad" / "ladt" both clone League/Agegroups/Divisions; "ladt" additionally
            // clones Teams (Step 12b below).
            var agegroupIdMap = new Dictionary<Guid, Guid>();
            var divisionIdMap = new Dictionary<Guid, Guid>();
            Guid? clonedLeagueId = null;
            var skipLad = string.Equals(request.LadtScope, "none", StringComparison.OrdinalIgnoreCase);
            var sourceLeague = skipLad ? null : await _repo.GetSourceLeagueAsync(request.SourceJobId, ct);
            if (sourceLeague != null)
            {
                var sourceSeasonForAgegroups = sourceJob.Season;
                var newLeagueId = Guid.NewGuid();
                clonedLeagueId = newLeagueId;

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

                // Step 12: Clone Divisions (remapping AgegroupId; building divisionIdMap)
                if (agegroupIdMap.Count > 0)
                {
                    var sourceAgegroupIds = agegroupIdMap.Keys.ToList();
                    var sourceDivisions = await _repo.GetSourceDivisionsAsync(sourceAgegroupIds, ct);
                    if (sourceDivisions.Count > 0)
                    {
                        var clonedDivisions = CloneDivisions(
                            sourceDivisions, agegroupIdMap, divisionIdMap, superUserId, now);
                        _repo.AddDivisions(clonedDivisions);
                        summary.DivisionsCloned = clonedDivisions.Count;
                    }
                }
            }

            // ── Step 12b: LADT — Clone Teams (filtered) ──
            // Filter rules per business: exclude teams with ClubrepRegistrationid set ("paid")
            // and teams whose Agegroup name contains WAITLIST or DROPPED status tokens.
            // ClubRep + Player Registrations are NOT cloned. Team-level JobFees are remapped below.
            var teamIdMap = new Dictionary<Guid, Guid>();
            if (string.Equals(request.LadtScope, "ladt", StringComparison.OrdinalIgnoreCase)
                && clonedLeagueId.HasValue && agegroupIdMap.Count > 0)
            {
                var sourceTeams = await _repo.GetSourceTeamsAsync(request.SourceJobId, ct);
                var eligible = sourceTeams
                    .Where(s => s.Team.ClubrepRegistrationid == null
                                && !IsTeamWaitlistOrDropped(s.AgegroupName))
                    .Select(s => s.Team)
                    .ToList();

                if (eligible.Count > 0)
                {
                    var clonedTeams = CloneTeams(
                        eligible, newJobId, clonedLeagueId.Value, request,
                        superUserId, now, yearDelta,
                        agegroupIdMap, divisionIdMap, teamIdMap);
                    _repo.AddTeams(clonedTeams);
                    summary.TeamsCloned = clonedTeams.Count;
                    _logger.LogInformation(
                        "LADT cloned {TeamsCloned} of {SourceTeams} source teams (filtered: paid={Paid}, waitlist/dropped={WD})",
                        clonedTeams.Count, sourceTeams.Count,
                        sourceTeams.Count(s => s.Team.ClubrepRegistrationid != null),
                        sourceTeams.Count(s => IsTeamWaitlistOrDropped(s.AgegroupName)));
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

                    // Team-level fees: clone only when LADT scope cloned the team. In LAD/none modes
                    // teams aren't cloned, so a team-level fee row would have no team to point at — skip.
                    Guid? newTeamId = null;
                    if (sourceFee.TeamId.HasValue)
                    {
                        if (!teamIdMap.TryGetValue(sourceFee.TeamId.Value, out var mappedTeam))
                            continue;
                        newTeamId = mappedTeam;
                    }

                    var newFeeId = Guid.NewGuid();
                    _feeRepo.Add(new JobFees
                    {
                        JobFeeId = newFeeId,
                        JobId = newJobId,
                        RoleId = sourceFee.RoleId,
                        AgegroupId = newAgegroupId,
                        TeamId = newTeamId,
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
                NewSuperUserRegistrationId = actorNewRegistrationId!.Value,
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
            EcprocessingFeePercent = FeeConstants.MinEcprocessingFeePercent,
            BEnableEcheck = false,

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

        // Same validation as submit so preview surfaces bad choice strings early.
        // "ladt" is allowed at preview (lets the wizard show TeamsToClone) but rejected at CloneJobAsync.
        ValidateChoices(request);

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

        // Load source fees once; build per-agegroup modifier lookup for the agegroup preview
        // hints (early-bird / late-fee window under each agegroup). Scope matches legacy:
        // Player role, agegroup-scoped (TeamId IS NULL). Picks the first EarlyBird-or-Discount
        // modifier for the discount hint and the first LateFee modifier for the late-fee hint —
        // the full list (all roles, all scopes) is surfaced separately in feeModifierShifts.
        var sourceFees = await _feeRepo.GetJobFeesByJobAsync(request.SourceJobId, ct);
        var agegroupModifiers = sourceFees
            .Where(f => f.RoleId == RoleConstants.Player && f.TeamId == null && f.AgegroupId.HasValue)
            .GroupBy(f => f.AgegroupId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(f => f.FeeModifiers ?? Enumerable.Empty<FeeModifiers>()).ToList());

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

                var mods = agegroupModifiers.TryGetValue(ag.AgegroupId, out var m) ? m : new List<FeeModifiers>();
                var earlyBird = mods.FirstOrDefault(x =>
                    x.ModifierType == FeeConstants.ModifierEarlyBird
                    || x.ModifierType == FeeConstants.ModifierDiscount);
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
        }

        // FeeModifier windows — year-delta shifted.
        var feeModifierShifts = new List<FeeModifierShiftDto>();
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

        // Team counts (always computed so the wizard's Step 4 banner shows the impact regardless
        // of selected scope — only LADT scope actually clones them).
        var sourceTeamRows = await _repo.GetSourceTeamsAsync(request.SourceJobId, ct);
        var teamsExcludedPaid = sourceTeamRows.Count(s => s.Team.ClubrepRegistrationid != null);
        var teamsExcludedWaitlistDropped = sourceTeamRows.Count(s =>
            s.Team.ClubrepRegistrationid == null // don't double-count paid teams that also match status
            && IsTeamWaitlistOrDropped(s.AgegroupName));
        var teamsToClone = sourceTeamRows.Count - teamsExcludedPaid - teamsExcludedWaitlistDropped;

        return new JobClonePreviewResponse
        {
            YearDelta = yearDelta,
            InferredLeagueName = inferredLeagueName,
            CurrentProcessingFeePercent = FeeConstants.MinProcessingFeePercent,
            SourceProcessingFeePercent = sourceJob.ProcessingFeePercent,
            CurrentEcheckProcessingFeePercent = FeeConstants.MinEcprocessingFeePercent,
            SourceEcheckProcessingFeePercent = sourceJob.EcprocessingFeePercent,
            SourceBEnableEcheck = sourceJob.BEnableEcheck,
            SourceBEnableStore = sourceJob.BEnableStore ?? false,
            EventStartShift = ShiftDto(sourceJob.EventStartDate, yearDelta),
            EventEndShift = ShiftDto(sourceJob.EventEndDate, yearDelta),
            AdnArbStartShift = ShiftDto(sourceJob.AdnArbstartDate, yearDelta),
            AdminsToDeactivate = toDeactivate,
            AdminsPreserved = preserved,
            TeamsToClone = teamsToClone,
            TeamsExcludedPaid = teamsExcludedPaid,
            TeamsExcludedWaitlistDropped = teamsExcludedWaitlistDropped,
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
        // Resolve user-driven Step 5 defaults. ValidateChoices() ran upstream so values are sane.
        var ccRate = ResolveProcessingFeePercent(
            req.ProcessingFeeChoice, req.CustomProcessingFeePercent, source.ProcessingFeePercent,
            FeeConstants.MinProcessingFeePercent, FeeConstants.MaxProcessingFeePercent);
        var ecRate = ResolveProcessingFeePercent(
            req.EcheckProcessingFeeChoice, req.CustomEcheckProcessingFeePercent, source.EcprocessingFeePercent,
            FeeConstants.MinEcprocessingFeePercent, FeeConstants.MaxEcprocessingFeePercent);
        var enableEcheck = string.Equals(req.EnableEcheckChoice, "source", StringComparison.OrdinalIgnoreCase)
            ? source.BEnableEcheck
            : false; // "off"
        var enableStore = string.Equals(req.StoreChoice, "keep", StringComparison.OrdinalIgnoreCase)
            ? source.BEnableStore
            : false; // "disable"

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
            // CC + eCheck processing rates: wizard Step 5 (Copy source / Use current floor / Custom).
            ProcessingFeePercent = ccRate,
            EcprocessingFeePercent = ecRate,
            BEnableEcheck = enableEcheck,

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
            BPlayersFullPaymentRequired = source.BPlayersFullPaymentRequired,
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
            BEnableStore = enableStore,
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
                LebUserId = userId,
                Modified = now,
            };
        }).ToList();
    }

    private static List<Divisions> CloneDivisions(
        List<Divisions> sources, Dictionary<Guid, Guid> agegroupIdMap,
        Dictionary<Guid, Guid> divisionIdMap, string userId, DateTime now)
    {
        return sources
            .Where(d => agegroupIdMap.ContainsKey(d.AgegroupId))
            .Select(d =>
            {
                var newDivId = Guid.NewGuid();
                divisionIdMap[d.DivId] = newDivId;
                return new Divisions
                {
                    DivId = newDivId,
                    AgegroupId = agegroupIdMap[d.AgegroupId],
                    DivName = d.DivName,
                    MaxRoundNumberToShow = d.MaxRoundNumberToShow,
                    LebUserId = userId,
                    Modified = now,
                };
            }).ToList();
    }

    // ══════════════════════════════════════════════════════════
    // LADT — Team cloning
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Status tokens encoded inside Agegroups.AgegroupName indicating a team should be excluded
    /// from LADT cloning. Match is case-insensitive substring against the agegroup name.
    /// </summary>
    private static readonly string[] TeamExcludeStatusTokens = ["WAITLIST", "DROPPED"];

    private static bool IsTeamWaitlistOrDropped(string? agegroupName) =>
        agegroupName is not null
        && TeamExcludeStatusTokens.Any(t => agegroupName.Contains(t, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Clones eligible source teams into the new job. ClubRep + financial state are NOT carried
    /// forward (per business rule: clone produces unclaimed, fresh shells that re-register each season).
    /// AgegroupId is remapped via agegroupIdMap; DivId via divisionIdMap (or null if division wasn't cloned).
    /// Date windows shift by year-delta. Standings + ADN subscription + insurance state cleared.
    /// </summary>
    private static List<TeamsEntity> CloneTeams(
        IEnumerable<TeamsEntity> sources, Guid newJobId, Guid newLeagueId, JobCloneRequest req,
        string userId, DateTime now, int yearDelta,
        Dictionary<Guid, Guid> agegroupIdMap,
        Dictionary<Guid, Guid> divisionIdMap,
        Dictionary<Guid, Guid> teamIdMap)
    {
        var cloned = new List<TeamsEntity>();
        foreach (var t in sources)
        {
            // Skip if agegroup wasn't cloned (different season, etc.) — a team without an
            // agegroup mapping has nowhere to land.
            if (!agegroupIdMap.TryGetValue(t.AgegroupId, out var newAgegroupId))
                continue;

            Guid? newDivId = t.DivId.HasValue && divisionIdMap.TryGetValue(t.DivId.Value, out var mappedDiv)
                ? mappedDiv : (Guid?)null;

            var newTeamId = Guid.NewGuid();
            teamIdMap[t.TeamId] = newTeamId;

            cloned.Add(new TeamsEntity
            {
                TeamId = newTeamId,
                JobId = newJobId,
                LeagueId = newLeagueId,
                AgegroupId = newAgegroupId,
                DivId = newDivId,
                CustomerId = t.CustomerId,

                // Identity carried forward
                TeamName = t.TeamName,
                TeamFullName = t.TeamFullName,
                TeamComments = t.TeamComments,
                TeamNumber = t.TeamNumber,
                Color = t.Color,
                Gender = t.Gender,
                AgegroupRequested = t.AgegroupRequested,
                DivisionRequested = t.DivisionRequested,
                District = t.District,
                Dow = t.Dow,
                Dow2 = t.Dow2,
                LevelOfPlay = t.LevelOfPlay,
                Year = req.YearTarget,
                Season = req.SeasonTarget,
                MaxCount = t.MaxCount,
                BHideRoster = t.BHideRoster,
                Active = t.Active,

                // ── Date windows shifted by year-delta ──
                Effectiveasofdate = ShiftByYears(t.Effectiveasofdate, yearDelta),
                Expireondate = ShiftByYears(t.Expireondate, yearDelta),
                Startdate = ShiftByYears(t.Startdate, yearDelta),
                Enddate = ShiftByYears(t.Enddate, yearDelta),
                LateFeeStart = ShiftByYears(t.LateFeeStart, yearDelta),
                LateFeeEnd = ShiftByYears(t.LateFeeEnd, yearDelta),
                DiscountFeeStart = ShiftByYears(t.DiscountFeeStart, yearDelta),
                DiscountFeeEnd = ShiftByYears(t.DiscountFeeEnd, yearDelta),

                // ── DOB / grade windows shifted when agegroup names advance by one year ──
                DobMin = req.UpAgegroupNamesByOne ? ShiftByYears(t.DobMin, 1) : t.DobMin,
                DobMax = req.UpAgegroupNamesByOne ? ShiftByYears(t.DobMax, 1) : t.DobMax,
                SchoolGradeMin = t.SchoolGradeMin,
                SchoolGradeMax = t.SchoolGradeMax,
                GradYearMin = req.UpAgegroupNamesByOne && t.GradYearMin.HasValue ? t.GradYearMin + 1 : t.GradYearMin,
                GradYearMax = req.UpAgegroupNamesByOne && t.GradYearMax.HasValue ? t.GradYearMax + 1 : t.GradYearMax,

                // ── Per-team fee config — preserved (admin can edit on new job) ──
                LateFee = t.LateFee,
                DiscountFee = t.DiscountFee,
                PerRegistrantFee = t.PerRegistrantFee,
                PerRegistrantDeposit = t.PerRegistrantDeposit,
                BAllowSelfRostering = t.BAllowSelfRostering,

                // ── ClubRep — NEVER carry forward (per business rule) ──
                ClubrepId = null,
                ClubrepRegistrationid = null,

                // ── Financial state — fresh slate (no club rep, no payments yet) ──
                FeeBase = 0m,
                FeeProcessing = 0m,
                FeeDiscount = 0m,
                FeeDiscountMp = 0m,
                FeeDonation = 0m,
                FeeLatefee = 0m,
                FeeTotal = 0m,
                OwedTotal = 0m,
                PaidTotal = 0m,

                // ── Standings — reset; new season starts at 0-0 ──
                Games = 0, Wins = 0, Losses = 0, Ties = 0, Points = 0,
                GoalsFor = 0, GoalsVs = 0, GoalDiff9 = 0,
                StandingsRank = null,
                LastLeagueRecord = null,

                // ── ADN subscription state — does NOT carry forward ──
                AdnSubscriptionId = null,
                AdnSubscriptionStatus = null,
                AdnSubscriptionStartDate = null,
                AdnSubscriptionBillingOccurences = null,
                AdnSubscriptionAmountPerOccurence = null,
                AdnSubscriptionIntervalLength = null,

                // ── Insurance policy — per-season, does NOT carry forward ──
                ViPolicyId = null,
                ViPolicyClubRepRegId = null,
                ViPolicyCreateDate = null,

                // ── Field assignments — admin reschedules per season ──
                FieldId1 = null,
                FieldId2 = null,
                FieldId3 = null,

                // ── Discount code — admin re-applies if needed ──
                DiscountCodeId = null,

                // ── Lineage flags ──
                BnewTeam = true,
                BnewCoach = true,
                PrevTeamId = t.TeamId,
                LastSeasonYear = t.Year,
                OldCoach = null,
                OldTeamName = null,
                NoReturningPlayers = null,

                Createdate = now,
                Modified = now,
                LebUserId = userId,
            });
        }
        return cloned;
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
            // Re-run predicate inside the txn so a row inserted between status fetch and
            // delete can't slip through.
            var counts = await _repo.GetDevUndoCountsAsync(jobId, ct);
            var reasons = BuildUndoBlockReasons(counts);
            if (reasons.Count > 0)
            {
                throw new InvalidOperationException(
                    "Cannot delete cloned job: " + string.Join("; ", reasons));
            }

            // Resolve the cloned league (if any) so we can decide whether to delete it.
            // Only safe to delete the Leagues row if no other JobLeagues references it.
            Guid? clonedLeagueIdToDelete = null;
            var jobLeague = await _repo.GetJobLeagueForJobAsync(jobId, ct);
            if (jobLeague != null)
            {
                var exclusive = await _repo.IsLeagueExclusivelyOwnedByJobAsync(
                    jobId, jobLeague.LeagueId, ct);
                if (exclusive)
                {
                    clonedLeagueIdToDelete = jobLeague.LeagueId;
                }
                else
                {
                    _logger.LogWarning(
                        "DevUndo: cloned Leagues {LeagueId} is referenced by another job; preserving it.",
                        jobLeague.LeagueId);
                }
            }

            await _repo.CascadeDeleteJobAsync(jobId, clonedLeagueIdToDelete, ct);
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
            reasons.Add($"{c.AncillaryRows} ancillary row(s) exist (calendar, email, schedule, store, etc.) — job has been used");
        return reasons;
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
        public int TeamsCloned { get; set; }
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
            TeamsCloned = TeamsCloned,
            FeesCloned = FeesCloned,
        };

        public override string ToString() =>
            $"Bulletins={BulletinsCloned}, AgeRanges={AgeRangesCloned}, Menus={MenusCloned}, " +
            $"Items={MenuItemsCloned}, Admins={AdminRegistrationsCloned}, Leagues={LeaguesCloned}, " +
            $"Agegroups={AgegroupsCloned}, Divisions={DivisionsCloned}, Teams={TeamsCloned}";
    }
}
