using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;

namespace TSIC.API.Services.Payments;

/// <summary>
/// Resolves <see cref="PaymentState"/> for a registration or team by reading
/// raw method-tagged sums from RegistrationAccounting and pairing them with
/// the job's processing-fee config.
///
/// Slice awareness is COMPUTED HERE, not supplied by callers: on a job with
/// BAddProcessingFees=1 and BApplyProcessingFeesToTeamDeposit=0 the deposit
/// slice of a team's bill is billed proc-free, so team hydration resolves each
/// team's effective deposit itself (fee cascade via IFeeRepository + modifier
/// columns via ITeamRepository — repository deps, so no cycle with
/// FeeResolutionService) and walks ledger rows oldest-first to split CC/eCheck
/// gross into proc-free vs grossed-up portions. Every consumer — recalc,
/// display, charge quoting, and any future call site — is slice-correct with
/// zero wiring. Every other job class (and all player hydration) computes a
/// proc-free base of 0 and takes the plain totals path, byte-identical to the
/// pre-slice behavior.
///
/// Basis note: the effective deposit is resolved against the team's CURRENT
/// agegroup — the historical basis the money was actually billed under. A
/// mid-swap caller stamping toward a different-deposit agegroup gets its
/// target-phase decision from the applier, not from this decomposition.
///
/// Single-job-per-call assumption: every entity in a batch belongs to the same
/// job (the rates and BAddProcessingFees are looked up once). Acceptable
/// because consumers (recalc, display, payment handlers) always operate on
/// one job at a time.
/// </summary>
public sealed class PaymentStateService : IPaymentStateService
{
    private readonly IRegistrationAccountingRepository _accounting;
    private readonly IJobRepository _jobRepo;
    private readonly IFeeRepository _feeRepo;
    private readonly ITeamRepository _teamRepo;

    public PaymentStateService(
        IRegistrationAccountingRepository accounting,
        IJobRepository jobRepo,
        IFeeRepository feeRepo,
        ITeamRepository teamRepo)
    {
        _accounting = accounting;
        _jobRepo = jobRepo;
        _feeRepo = feeRepo;
        _teamRepo = teamRepo;
    }

    public Task<PaymentState> ForJobAsync(Guid jobId, CancellationToken ct = default) =>
        BuildEmptyAsync(jobId, ct);

    public async Task<PaymentState> ForRegistrationAsync(
        Guid registrationId, Guid jobId, CancellationToken ct = default)
    {
        var dict = await ForRegistrationsAsync(new[] { registrationId }, jobId, ct);
        return dict.TryGetValue(registrationId, out var state)
            ? state
            : await BuildEmptyAsync(jobId, ct);
    }

    public async Task<PaymentState> ForTeamAsync(
        Guid teamId, Guid jobId, CancellationToken ct = default)
    {
        var dict = await ForTeamsAsync(new[] { teamId }, jobId, ct);
        return dict.TryGetValue(teamId, out var state)
            ? state
            : await BuildEmptyAsync(jobId, ct);
    }

    public async Task<Dictionary<Guid, PaymentState>> ForRegistrationsAsync(
        IReadOnlyCollection<Guid> registrationIds, Guid jobId, CancellationToken ct = default) =>
        await BuildBatchAsync(PaymentEntityKind.Registration, registrationIds, jobId, ct);

    public async Task<Dictionary<Guid, PaymentState>> ForTeamsAsync(
        IReadOnlyCollection<Guid> teamIds, Guid jobId, CancellationToken ct = default) =>
        await BuildBatchAsync(PaymentEntityKind.Team, teamIds, jobId, ct);

    private async Task<Dictionary<Guid, PaymentState>> BuildBatchAsync(
        PaymentEntityKind kind, IReadOnlyCollection<Guid> entityIds, Guid jobId, CancellationToken ct)
    {
        if (entityIds.Count == 0) return new();

        var (bAdd, applyToDeposit, ccRate, echeckRate) = await GetJobConfigAsync(jobId, ct);

        // Slice-aware path: teams on a proc-on-balance-only job. The flag is team-only, so
        // player hydration always takes the totals path.
        var procFreeBaseByEntity = kind == PaymentEntityKind.Team && bAdd && !applyToDeposit
            ? await ResolveProcFreeBasesAsync(jobId, entityIds, ct)
            : null;

        if (procFreeBaseByEntity is null || !procFreeBaseByEntity.Values.Any(v => v > 0m))
        {
            var totals = await _accounting.GetPaymentTotalsByEntityAsync(kind, entityIds, ct);

            var result = new Dictionary<Guid, PaymentState>(totals.Count);
            foreach (var (entityId, t) in totals)
            {
                result[entityId] = new PaymentState
                {
                    CcGrossPaid = t.CreditCard,
                    EcheckGrossPaid = t.Echeck,
                    CheckPaid = t.Check,
                    CashPaid = t.Cash,
                    CorrectionApplied = t.Correction,
                    BAddProcessingFees = bAdd,
                    CcRate = ccRate,
                    EcheckRate = echeckRate,
                };
            }
            return result;
        }

        // One rows query replaces the totals query (same filters/buckets, so summing the
        // rows reproduces the totals exactly) and additionally yields payment ORDER, which
        // the walk needs: billing is deposit-first, so the oldest dollars — whatever their
        // tender — paid the proc-free deposit slice. CC/eCheck money inside that slice was
        // charged WITHOUT proc and must not be grossed-up on decomposition.
        var rowsByEntity = await _accounting.GetPaymentRowsByEntityAsync(kind, entityIds, ct);

        var sliced = new Dictionary<Guid, PaymentState>(rowsByEntity.Count);
        foreach (var (entityId, rows) in rowsByEntity)
        {
            var procFreeBase = procFreeBaseByEntity.GetValueOrDefault(entityId);

            decimal cc = 0m, echeck = 0m, check = 0m, cash = 0m, correction = 0m;
            decimal ccFree = 0m, echeckFree = 0m;
            var remainingFree = procFreeBase;

            foreach (var row in rows) // already oldest-first (Createdate, AId)
            {
                // Negative rows (refunds, NSF returns, credit corrections) never consume
                // the free slice — take is clamped at 0 and the netting happens via the
                // bucket sums, same as the totals path.
                var take = Math.Min(Math.Max(row.Amount, 0m), Math.Max(remainingFree, 0m));
                switch (row.Bucket)
                {
                    case PaymentMethodBucket.CreditCard:
                        cc += row.Amount;
                        ccFree += take;
                        break;
                    case PaymentMethodBucket.Echeck:
                        echeck += row.Amount;
                        echeckFree += take;
                        break;
                    case PaymentMethodBucket.Check:
                        check += row.Amount;
                        break;
                    case PaymentMethodBucket.Cash:
                        cash += row.Amount;
                        break;
                    case PaymentMethodBucket.Correction:
                        correction += row.Amount;
                        break;
                }
                remainingFree -= take;
            }

            sliced[entityId] = new PaymentState
            {
                CcGrossPaid = cc,
                EcheckGrossPaid = echeck,
                CheckPaid = check,
                CashPaid = cash,
                CorrectionApplied = correction,
                BAddProcessingFees = bAdd,
                CcRate = ccRate,
                EcheckRate = echeckRate,
                ProcFreeBase = procFreeBase,
                CcProcFreeGross = ccFree,
                EcheckProcFreeGross = echeckFree,
            };
        }
        return sliced;
    }

    /// <summary>
    /// Effective deposit per team — deposit from the ClubRep fee cascade, adjusted by the
    /// SAME modifier set as the paid-past-deposit promotion (− discount + lateFee +
    /// donation), floored at 0. Teams with no configured fee resolve to 0 → totals path.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> ResolveProcFreeBasesAsync(
        Guid jobId, IReadOnlyCollection<Guid> teamIds, CancellationToken ct)
    {
        var ids = teamIds.ToList();
        var fees = await _feeRepo.GetResolvedFeesByTeamIdsAsync(jobId, RoleConstants.ClubRep, ids, ct);
        var mods = await _teamRepo.GetTeamFeeModifiersAsync(ids, ct);

        return ids.ToDictionary(
            id => id,
            id =>
            {
                var deposit = fees.GetValueOrDefault(id)?.EffectiveDeposit ?? 0m;
                var m = mods.GetValueOrDefault(id);
                return Math.Max(0m, deposit - (m?.Discount ?? 0m) + (m?.LateFee ?? 0m) + (m?.Donation ?? 0m));
            });
    }

    private async Task<PaymentState> BuildEmptyAsync(Guid jobId, CancellationToken ct)
    {
        var (bAdd, _, ccRate, echeckRate) = await GetJobConfigAsync(jobId, ct);
        return PaymentState.Empty(bAdd, ccRate, echeckRate);
    }

    private async Task<(bool BAddProcessingFees, bool ApplyToDeposit, decimal CcRate, decimal EcheckRate)> GetJobConfigAsync(
        Guid jobId, CancellationToken ct)
    {
        var settings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
        var bAdd = settings?.BAddProcessingFees ?? false;
        var applyToDeposit = settings?.BApplyProcessingFeesToTeamDeposit ?? false;
        var ccRaw = await _jobRepo.GetProcessingFeePercentAsync(jobId, ct);
        var echeckRaw = await _jobRepo.GetEcprocessingFeePercentAsync(jobId, ct);
        return (bAdd, applyToDeposit, ProcessingRateMath.ToCcMultiplier(ccRaw), ProcessingRateMath.ToEcheckMultiplier(echeckRaw));
    }
}
