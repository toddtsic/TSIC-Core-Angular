using TSIC.Contracts.Dtos.Ladt;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Fees;

/// <summary>
/// Builds the canonical fee-resolution map for the LADT editor grids: per node
/// (league / agegroup / team) × role (Player, Club Rep), the effective amounts, phase,
/// and modifier winners with the SOURCE tier of each, plus a downward summary of what
/// more-specific scopes override.
///
/// DISPLAY-ONLY twin of the charging path. The cascade semantics here mirror
/// <c>FeeRepository.GetResolvedFeeAsync</c> exactly — league → agegroup → team,
/// most-specific non-null wins PER FIELD, team rows matched by (AgegroupId, TeamId)
/// pair, job-level rows never a base-fee source — and the equivalence suite
/// (<c>LadtFeeResolutionMapTests</c>) pins the two together. Phase follows
/// <c>ResolvedFee.ResolveFullPaymentPhase</c>: most-specific stamp ?? false; the legacy
/// Jobs.b*FullPaymentRequired columns are never consulted. Modifier winners follow
/// <c>FeeResolutionService.EvaluateModifiersAsync</c>: per type, the most-specific
/// scope carrying the type wins outright (scopes do not sum); the DTO carries both the
/// window-ignored configured winner (grid display) and the active-now winner (the
/// charging number).
///
/// This helper has no I/O — pure computation over rows already in memory, easy to test.
/// </summary>
public static class LadtFeeResolutionMapBuilder
{
    /// <summary>Tree shape the resolver needs — nothing more. Divisions are elided
    /// (teams carry AgegroupId directly; fees.JobFees has no division scope).</summary>
    public sealed record TreeInput
    {
        public required IReadOnlyList<LeagueInput> Leagues { get; init; }
    }

    public sealed record LeagueInput
    {
        public required Guid LeagueId { get; init; }
        public required IReadOnlyList<AgegroupInput> Agegroups { get; init; }
    }

    public sealed record AgegroupInput
    {
        public required Guid AgegroupId { get; init; }
        /// <summary>For <see cref="AgegroupConstants.IsSystemBucket"/> — bucket scopes are
        /// excluded from below-summaries and TwoPhase, but still emit their own node entry.</summary>
        public string? AgegroupName { get; init; }
        /// <summary>All teams under the agegroup (direct or via a division), inactive included.</summary>
        public IReadOnlyList<Guid> TeamIds { get; init; } = [];
    }

    private const string TierLeague = "league";
    private const string TierAgegroup = "agegroup";
    private const string TierTeam = "team";

    private static readonly string[] Roles = [RoleConstants.Player, RoleConstants.ClubRep];

    // ── Internal working shapes (private — off the OpenAPI surface, positional is fine) ──

    /// <summary>One cascade tier: the scope's own rows for one role.</summary>
    private sealed record Tier(string Name, List<JobFees> Rows);

    /// <summary>A descendant scope for below-summaries. Team scopes carry the agegroup they
    /// hang under so their own rows are looked up by the same (AgegroupId, TeamId) pair the
    /// charge path uses — a stale row under a different agegroup never counts.</summary>
    private readonly record struct Scope(Guid Id, Guid? AgegroupIdForTeam);

    private sealed record ModifierWin(decimal Amount, string Source, bool Active, decimal? ActiveAmount, string? ActiveSource);

    private sealed record Resolution(
        bool FeeConfigured,
        decimal? Deposit, string? DepositSource,
        decimal? BalanceDue, string? BalanceDueSource,
        bool? PhaseStamp, string? PhaseSource,
        ModifierWin? EarlyBird, ModifierWin? LateFee)
    {
        public bool FullPayment => PhaseStamp ?? false;
        public bool HasTwoPhaseAmounts => (Deposit ?? 0m) > 0m && (BalanceDue ?? 0m) > 0m;
    }

    public static LadtFeeResolutionMapDto Build(
        IReadOnlyList<JobFees> jobFees, TreeInput tree, DateTime asOfNow)
    {
        // Index rows by scope, mirroring GetResolvedFeeAsync's WHERE branches verbatim:
        //   league row   = LeagueId set, AgegroupId null, TeamId null
        //   agegroup row = AgegroupId set, TeamId null            (LeagueId not consulted)
        //   team row     = (AgegroupId, TeamId) pair              (a row whose AgegroupId
        //                  no longer matches the team's agegroup never resolves)
        //   job row      = all scope ids null → in NO chain (not a base-fee source).
        var leagueRows = new Dictionary<(string RoleId, Guid LeagueId), List<JobFees>>();
        var agRows = new Dictionary<(string RoleId, Guid AgegroupId), List<JobFees>>();
        var teamRows = new Dictionary<(string RoleId, Guid AgegroupId, Guid TeamId), List<JobFees>>();

        foreach (var jf in jobFees)
        {
            if (jf.TeamId != null)
            {
                if (jf.AgegroupId != null)
                    Add(teamRows, (jf.RoleId, jf.AgegroupId.Value, jf.TeamId.Value), jf);
            }
            else if (jf.AgegroupId != null)
            {
                Add(agRows, (jf.RoleId, jf.AgegroupId.Value), jf);
            }
            else if (jf.LeagueId != null)
            {
                Add(leagueRows, (jf.RoleId, jf.LeagueId.Value), jf);
            }
        }

        // ── Pass 1: resolve every scope per role (teams keyed by team id) ──
        var resolutions = new Dictionary<(string RoleId, Guid NodeId), Resolution>();

        foreach (var role in Roles)
        {
            foreach (var league in tree.Leagues)
            {
                var leagueTier = TierOf(TierLeague, leagueRows, (role, league.LeagueId));
                resolutions[(role, league.LeagueId)] = Resolve([leagueTier], asOfNow);

                foreach (var ag in league.Agegroups)
                {
                    var agTier = TierOf(TierAgegroup, agRows, (role, ag.AgegroupId));
                    resolutions[(role, ag.AgegroupId)] = Resolve([agTier, leagueTier], asOfNow);

                    foreach (var teamId in ag.TeamIds)
                    {
                        var teamTier = TierOf(TierTeam, teamRows, (role, ag.AgegroupId, teamId));
                        resolutions[(role, teamId)] = Resolve([teamTier, agTier, leagueTier], asOfNow);
                    }
                }
            }
        }

        // ── Pass 2: emit node entries with TwoPhase + below-summaries ──
        var nodes = new List<LadtFeeNodeResolutionDto>();

        foreach (var league in tree.Leagues)
        {
            // System buckets (WAITLIST/Dropped/Registration) and their teams are not
            // scopes a director set — excluded from downward disclosure, like every
            // other fee surface. Their own node entries still emit below.
            var realAgegroups = league.Agegroups
                .Where(a => !AgegroupConstants.IsSystemBucket(a.AgegroupName))
                .ToList();

            var leagueDescendants = new List<Scope>();
            foreach (var ag in realAgegroups)
            {
                leagueDescendants.Add(new Scope(ag.AgegroupId, null));
                foreach (var teamId in ag.TeamIds)
                    leagueDescendants.Add(new Scope(teamId, ag.AgegroupId));
            }

            nodes.Add(NodeEntry(league.LeagueId, level: 0, TierLeague,
                leagueDescendants, resolutions, agRows, teamRows));

            foreach (var ag in league.Agegroups)
            {
                var isBucket = AgegroupConstants.IsSystemBucket(ag.AgegroupName);
                var agDescendants = isBucket
                    ? new List<Scope>()
                    : ag.TeamIds.Select(t => new Scope(t, ag.AgegroupId)).ToList();

                nodes.Add(NodeEntry(ag.AgegroupId, level: 1, TierAgegroup,
                    agDescendants, resolutions, agRows, teamRows));

                foreach (var teamId in ag.TeamIds)
                {
                    nodes.Add(NodeEntry(teamId, level: 3, TierTeam,
                        descendants: [], resolutions, agRows, teamRows));
                }
            }
        }

        return new LadtFeeResolutionMapDto { Nodes = nodes };
    }

    // ── Node/role entry assembly ──

    private static LadtFeeNodeResolutionDto NodeEntry(
        Guid nodeId, int level, string ownTier,
        IReadOnlyList<Scope> descendants,
        Dictionary<(string, Guid), Resolution> resolutions,
        Dictionary<(string, Guid), List<JobFees>> agRows,
        Dictionary<(string, Guid, Guid), List<JobFees>> teamRows)
        => new()
        {
            NodeId = nodeId,
            Level = level,
            Player = RoleEntry(RoleConstants.Player, nodeId, ownTier, descendants, resolutions, agRows, teamRows),
            ClubRep = RoleEntry(RoleConstants.ClubRep, nodeId, ownTier, descendants, resolutions, agRows, teamRows)
        };

    private static LadtFeeRoleResolutionDto RoleEntry(
        string roleId, Guid nodeId, string ownTier,
        IReadOnlyList<Scope> descendants,
        Dictionary<(string, Guid), Resolution> resolutions,
        Dictionary<(string, Guid), List<JobFees>> agRows,
        Dictionary<(string, Guid, Guid), List<JobFees>> teamRows)
    {
        var res = resolutions[(roleId, nodeId)];

        // TwoPhase: deposit AND balance resolve > 0 at this node or any non-bucket
        // descendant — the verified "is Deposit/PIF meaningful here" verdict.
        var twoPhase = res.HasTwoPhaseAmounts
            || descendants.Any(d => resolutions[(roleId, d.Id)].HasTwoPhaseAmounts);

        return new LadtFeeRoleResolutionDto
        {
            RoleId = roleId,
            FeeConfigured = res.FeeConfigured,
            Deposit = res.Deposit,
            DepositSource = res.DepositSource,
            BalanceDue = res.BalanceDue,
            BalanceDueSource = res.BalanceDueSource,
            FullPayment = res.FullPayment,
            PhaseSource = res.PhaseSource,
            TwoPhase = twoPhase,
            EarlyBird = ToDto(res.EarlyBird),
            LateFee = ToDto(res.LateFee),
            Below = ownTier == TierTeam ? null : BelowSummary(roleId, res, descendants, resolutions, agRows, teamRows)
        };
    }

    private static LadtFeeModifierResolutionDto? ToDto(ModifierWin? win) => win == null ? null
        : new LadtFeeModifierResolutionDto
        {
            Amount = win.Amount,
            Source = win.Source,
            Active = win.Active,
            ActiveAmount = win.ActiveAmount,
            ActiveSource = win.ActiveSource
        };

    // ── Downward summaries ──

    private static LadtFeeBelowSummaryDto BelowSummary(
        string roleId, Resolution nodeRes,
        IReadOnlyList<Scope> descendants,
        Dictionary<(string, Guid), Resolution> resolutions,
        Dictionary<(string, Guid), List<JobFees>> agRows,
        Dictionary<(string, Guid, Guid), List<JobFees>> teamRows)
    {
        // A descendant "overrides" a family when its OWN rows set a local value for it
        // (mirrors the fly-in's overrideInfoFrom). Its reported value is its RESOLVED
        // value — what that scope actually charges/displays, not just the raw local row.
        var amountScopes = new List<Resolution>();
        var phaseScopes = new List<Resolution>();
        var earlyBirdScopes = new List<Resolution>();
        var lateFeeScopes = new List<Resolution>();

        foreach (var d in descendants)
        {
            var own = d.AgegroupIdForTeam is Guid agId
                ? teamRows.GetValueOrDefault((roleId, agId, d.Id))
                : agRows.GetValueOrDefault((roleId, d.Id));
            if (own == null || own.Count == 0) continue;
            var dRes = resolutions[(roleId, d.Id)];

            if (own.Any(r => r.Deposit != null || r.BalanceDue != null)) amountScopes.Add(dRes);
            if (own.Any(r => r.BFullPaymentRequired != null)) phaseScopes.Add(dRes);
            if (own.Any(r => r.FeeModifiers.Any(m => m.ModifierType == FeeConstants.ModifierEarlyBird)))
                earlyBirdScopes.Add(dRes);
            if (own.Any(r => r.FeeModifiers.Any(m => m.ModifierType == FeeConstants.ModifierLateFee)))
                lateFeeScopes.Add(dRes);
        }

        return new LadtFeeBelowSummaryDto
        {
            Amounts = new LadtFeeBelowAmountsDto
            {
                OverrideCount = amountScopes.Count,
                Agrees = amountScopes.All(r => r.Deposit == nodeRes.Deposit && r.BalanceDue == nodeRes.BalanceDue),
                DistinctValues = amountScopes
                    .Select(r => (r.Deposit, r.BalanceDue))
                    .Distinct()
                    .Select(p => new LadtFeeAmountPairDto { Deposit = p.Deposit, BalanceDue = p.BalanceDue })
                    .ToList()
            },
            Phase = new LadtFeeBelowPhaseDto
            {
                OverrideCount = phaseScopes.Count,
                Agrees = phaseScopes.All(r => r.FullPayment == nodeRes.FullPayment),
                DistinctValues = phaseScopes.Select(r => r.FullPayment).Distinct().ToList()
            },
            EarlyBird = BelowModifier(earlyBirdScopes, r => r.EarlyBird, nodeRes.EarlyBird),
            LateFee = BelowModifier(lateFeeScopes, r => r.LateFee, nodeRes.LateFee)
        };
    }

    private static LadtFeeBelowModifierDto BelowModifier(
        List<Resolution> scopes, Func<Resolution, ModifierWin?> pick, ModifierWin? nodeWin)
        => new()
        {
            OverrideCount = scopes.Count,
            Agrees = scopes.All(r => pick(r)?.Amount == nodeWin?.Amount),
            DistinctValues = scopes.Select(r => pick(r)?.Amount ?? 0m).Distinct().ToList()
        };

    // ── Cascade core ──

    /// <summary>Resolve one role along a chain of tiers, MOST-SPECIFIC FIRST. Per-field
    /// ??= exactly like GetResolvedFeeAsync; FeeConfigured = any row in the chain.</summary>
    private static Resolution Resolve(Tier[] chain, DateTime asOfNow)
    {
        var feeConfigured = false;
        decimal? deposit = null; string? depositSource = null;
        decimal? balanceDue = null; string? balanceDueSource = null;
        bool? phaseStamp = null; string? phaseSource = null;

        foreach (var tier in chain)
        {
            if (tier.Rows.Count > 0) feeConfigured = true;
            foreach (var row in tier.Rows)
            {
                if (deposit == null && row.Deposit != null) { deposit = row.Deposit; depositSource = tier.Name; }
                if (balanceDue == null && row.BalanceDue != null) { balanceDue = row.BalanceDue; balanceDueSource = tier.Name; }
                if (phaseStamp == null && row.BFullPaymentRequired != null) { phaseStamp = row.BFullPaymentRequired; phaseSource = tier.Name; }
            }
        }

        return new Resolution(
            feeConfigured,
            deposit, depositSource,
            balanceDue, balanceDueSource,
            phaseStamp, phaseSource,
            ResolveModifier(chain, FeeConstants.ModifierEarlyBird, asOfNow),
            ResolveModifier(chain, FeeConstants.ModifierLateFee, asOfNow));
    }

    private static ModifierWin? ResolveModifier(Tier[] chain, string type, DateTime asOfNow)
    {
        // Configured winner: most-specific tier carrying the type at all (windows
        // ignored — the GetActiveModifiersForCascadeAsync(asOfDate: null) semantics,
        // and what the grid displays today). Amount sums within the winning tier only;
        // scopes never sum across tiers.
        Tier? configuredTier = null;
        foreach (var tier in chain)
        {
            if (tier.Rows.Any(r => r.FeeModifiers.Any(m => m.ModifierType == type)))
            {
                configuredTier = tier;
                break;
            }
        }
        if (configuredTier == null) return null;

        var configuredMods = configuredTier.Rows
            .SelectMany(r => r.FeeModifiers)
            .Where(m => m.ModifierType == type)
            .ToList();
        var amount = configuredMods.Sum(m => m.Amount);
        var active = configuredMods.Any(m => IsActive(m, asOfNow));

        // Active-now winner: most-specific tier with an ACTIVE modifier of the type —
        // the EvaluateModifiersAsync(now) charging number. Sits at a broader tier than
        // the configured winner when the specific tier's window has expired.
        decimal? activeAmount = null;
        string? activeSource = null;
        foreach (var tier in chain)
        {
            var activeSum = 0m;
            var found = false;
            foreach (var m in tier.Rows.SelectMany(r => r.FeeModifiers))
            {
                if (m.ModifierType == type && IsActive(m, asOfNow)) { activeSum += m.Amount; found = true; }
            }
            if (found)
            {
                activeAmount = activeSum;
                activeSource = tier.Name;
                break;
            }
        }

        return new ModifierWin(amount, configuredTier.Name, active, activeAmount, activeSource);
    }

    /// <summary>StartDate &lt;= asOf &lt;= EndDate, NULLs unbounded — mirrors
    /// GetActiveModifiersAsync's window test.</summary>
    private static bool IsActive(FeeModifiers m, DateTime asOfNow) =>
        (m.StartDate == null || m.StartDate <= asOfNow)
        && (m.EndDate == null || m.EndDate >= asOfNow);

    // ── Small helpers ──

    private static void Add<TKey>(Dictionary<TKey, List<JobFees>> dict, TKey key, JobFees row)
        where TKey : notnull
    {
        if (!dict.TryGetValue(key, out var list)) dict[key] = list = [];
        list.Add(row);
    }

    private static Tier TierOf<TKey>(string name, Dictionary<TKey, List<JobFees>> dict, TKey key)
        where TKey : notnull
        => new(name, dict.TryGetValue(key, out var rows) ? rows : []);
}
