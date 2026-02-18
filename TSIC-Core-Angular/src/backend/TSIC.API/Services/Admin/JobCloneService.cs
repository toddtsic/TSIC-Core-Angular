using System.Text.RegularExpressions;
using TSIC.Contracts.Dtos.JobClone;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// Orchestrates job cloning — copies a source job and its related entities into a new job.
/// Follows dependency order: Job → DisplayOptions → OwlImages → Bulletins → AgeRanges →
/// Menus → MenuItems → Registrations → League → JobLeague → Agegroups → Divisions.
/// </summary>
public sealed class JobCloneService : IJobCloneService
{
    private readonly IJobCloneRepository _repo;
    private readonly ILogger<JobCloneService> _logger;

    public JobCloneService(IJobCloneRepository repo, ILogger<JobCloneService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<List<JobCloneSourceDto>> GetCloneableJobsAsync(CancellationToken ct = default)
    {
        return await _repo.GetCloneableJobsAsync(ct);
    }

    public async Task<JobCloneResponse> CloneJobAsync(
        JobCloneRequest request, string superUserId, CancellationToken ct = default)
    {
        // ── Validate ──
        var sourceJob = await _repo.GetSourceJobAsync(request.SourceJobId, ct)
            ?? throw new KeyNotFoundException($"Source job {request.SourceJobId} not found.");

        if (await _repo.JobPathExistsAsync(request.JobPathTarget, ct))
            throw new InvalidOperationException($"Job path '{request.JobPathTarget}' already exists.");

        var now = DateTime.UtcNow;
        var newJobId = Guid.NewGuid();
        var summary = new CloneSummaryBuilder();

        await _repo.BeginTransactionAsync(ct);
        try
        {
            // ── Step 1: Clone Jobs.Jobs ──
            _logger.LogInformation("Cloning job {SourceJobId} → {TargetPath}", request.SourceJobId, request.JobPathTarget);
            var clonedJob = CloneJob(sourceJob, newJobId, request, superUserId, now);
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

            // ── Step 4: Clone Jobs.Bulletins ──
            var sourceBulletins = await _repo.GetSourceBulletinsAsync(request.SourceJobId, ct);
            if (sourceBulletins.Count > 0)
            {
                var clonedBulletins = CloneBulletins(sourceBulletins, newJobId, superUserId, now);
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

            // ── Step 8: Clone admin Registrations ──
            var sourceRegs = await _repo.GetSourceAdminRegistrationsAsync(request.SourceJobId, ct);
            if (sourceRegs.Count > 0)
            {
                var clonedRegs = CloneAdminRegistrations(sourceRegs, newJobId, request, superUserId, now);
                _repo.AddRegistrations(clonedRegs);
                summary.AdminRegistrationsCloned = clonedRegs.Count;
            }

            // ── Steps 9–12: Clone LAD hierarchy ──
            var sourceLeague = await _repo.GetSourceLeagueAsync(request.SourceJobId, ct);
            if (sourceLeague != null)
            {
                var sourceSeasonForAgegroups = sourceJob.Season;
                var newLeagueId = Guid.NewGuid();

                // Step 9: Clone League
                var clonedLeague = CloneLeague(sourceLeague, newLeagueId, request, superUserId, now);
                _repo.AddLeague(clonedLeague);
                summary.LeaguesCloned = 1;

                // Step 10: Link Job ↔ League
                var jobLeague = new JobLeagues
                {
                    JobLeagueId = Guid.NewGuid(),
                    JobId = newJobId,
                    LeagueId = newLeagueId,
                    BIsPrimary = true,
                    LebUserId = superUserId,
                    Modified = now,
                };
                _repo.AddJobLeague(jobLeague);

                // Step 11: Clone Agegroups (with grad year logic)
                var sourceAgegroups = await _repo.GetSourceAgegroupsAsync(sourceLeague.LeagueId, sourceSeasonForAgegroups, ct);
                var agegroupIdMap = new Dictionary<Guid, Guid>(); // old → new

                if (sourceAgegroups.Count > 0)
                {
                    var clonedAgegroups = CloneAgegroups(
                        sourceAgegroups, newLeagueId, request, superUserId, now, agegroupIdMap);
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
    // Private clone helpers
    // ══════════════════════════════════════════════════════════

    private static Jobs CloneJob(
        Jobs source, Guid newJobId, JobCloneRequest req, string userId, DateTime now)
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

            // Copy all config from source
            BillingTypeId = source.BillingTypeId,
            JobTypeId = source.JobTypeId,
            SportId = source.SportId,
            BAllowRosterViewAdult = source.BAllowRosterViewAdult,
            BAllowRosterViewPlayer = source.BAllowRosterViewPlayer,
            BBannerIsCustom = source.BBannerIsCustom,
            BSuspendPublic = source.BSuspendPublic,
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
            BClubRepAllowEdit = source.BClubRepAllowEdit,
            BClubRepAllowDelete = source.BClubRepAllowDelete,
            BClubRepAllowAdd = source.BClubRepAllowAdd,
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
            ProcessingFeePercent = source.ProcessingFeePercent,
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
            AdnArbstartDate = source.AdnArbstartDate,
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
            EventStartDate = source.EventStartDate,
            EventEndDate = source.EventEndDate,
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

        // Replace source year with target year in parallax text
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
        List<Bulletins> sources, Guid newJobId, string userId, DateTime now)
    {
        // Shift bulletin dates forward so relative spacing is preserved
        var earliest = sources.Min(b => b.CreateDate);
        var offset = now - earliest;

        return sources.Select(b => new Bulletins
        {
            BulletinId = Guid.NewGuid(),
            JobId = newJobId,
            Title = b.Title,
            Text = b.Text,
            Active = b.Active,
            ExpireHours = b.ExpireHours,
            CreateDate = b.CreateDate + offset,
            StartDate = b.StartDate.HasValue ? b.StartDate.Value + offset : null,
            EndDate = b.EndDate.HasValue ? b.EndDate.Value + offset : null,
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
        List<Registrations> sources, Guid newJobId, JobCloneRequest req, string userId, DateTime now)
    {
        return sources.Select(r =>
        {
            var cloned = new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                // RegistrationAi — auto-increment, let DB assign
                JobId = newJobId,
                RoleId = r.RoleId,
                UserId = r.UserId,
                FamilyUserId = r.FamilyUserId,
                BActive = r.BActive,
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

            // SetDirectorsToInactive flag
            if (req.SetDirectorsToInactive
                && string.Equals(r.RoleId, RoleConstants.Director, StringComparison.OrdinalIgnoreCase))
            {
                cloned.BActive = false;
            }

            return cloned;
        }).ToList();
    }

    private static Leagues CloneLeague(
        Leagues source, Guid newLeagueId, JobCloneRequest req, string userId, DateTime now)
    {
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
            PlayerFeeOverride = source.PlayerFeeOverride,
            LebUserId = userId,
            Modified = now,
        };
    }

    private static List<Agegroups> CloneAgegroups(
        List<Agegroups> sources, Guid newLeagueId, JobCloneRequest req,
        string userId, DateTime now, Dictionary<Guid, Guid> agegroupIdMap)
    {
        return sources.Select(ag =>
        {
            var newId = Guid.NewGuid();
            agegroupIdMap[ag.AgegroupId] = newId;

            var name = ag.AgegroupName;
            var gradYearMin = ag.GradYearMin;
            var gradYearMax = ag.GradYearMax;

            // Grad year auto-increment
            if (req.UpAgegroupNamesByOne)
            {
                if (!string.IsNullOrEmpty(name))
                    name = IncrementYearsInName(name);
                if (gradYearMin.HasValue)
                    gradYearMin = gradYearMin.Value + 1;
                if (gradYearMax.HasValue)
                    gradYearMax = gradYearMax.Value + 1;
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
                TeamFee = ag.TeamFee,
                TeamFeeLabel = ag.TeamFeeLabel,
                RosterFee = ag.RosterFee,
                RosterFeeLabel = ag.RosterFeeLabel,
                DobMin = ag.DobMin,
                DobMax = ag.DobMax,
                SchoolGradeMin = ag.SchoolGradeMin,
                SchoolGradeMax = ag.SchoolGradeMax,
                GradYearMin = gradYearMin,
                GradYearMax = gradYearMax,
                LateFee = ag.LateFee,
                LateFeeStart = ag.LateFeeStart,
                LateFeeEnd = ag.LateFeeEnd,
                BAllowSelfRostering = ag.BAllowSelfRostering,
                DiscountFee = ag.DiscountFee,
                DiscountFeeStart = ag.DiscountFeeStart,
                DiscountFeeEnd = ag.DiscountFeeEnd,
                BChampionsByDivision = ag.BChampionsByDivision,
                BHideStandings = ag.BHideStandings,
                BAllowApiRosterAccess = ag.BAllowApiRosterAccess,
                PlayerFeeOverride = ag.PlayerFeeOverride,
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
    // Utility
    // ══════════════════════════════════════════════════════════

    /// <summary>
    /// Finds 4-digit year patterns (2020–2039) in a string and increments each by 1.
    /// E.g., "2025 Boys" → "2026 Boys", "Class of 2027" → "Class of 2028".
    /// </summary>
    private static string IncrementYearsInName(string name)
    {
        return Regex.Replace(name, @"\b(20[2-3]\d)\b", m =>
            (int.Parse(m.Value) + 1).ToString());
    }

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
        };

        public override string ToString() =>
            $"Bulletins={BulletinsCloned}, AgeRanges={AgeRangesCloned}, Menus={MenusCloned}, " +
            $"Items={MenuItemsCloned}, Admins={AdminRegistrationsCloned}, Leagues={LeaguesCloned}, " +
            $"Agegroups={AgegroupsCloned}, Divisions={DivisionsCloned}";
    }
}
