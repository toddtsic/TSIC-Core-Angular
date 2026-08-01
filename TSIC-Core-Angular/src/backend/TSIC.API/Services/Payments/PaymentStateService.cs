using TSIC.Contracts.Payments;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Payments;

/// <summary>
/// Resolves <see cref="PaymentState"/> for a registration or team by reading
/// raw method-tagged sums from RegistrationAccounting and pairing them with
/// the job's processing-fee config.
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

    public PaymentStateService(
        IRegistrationAccountingRepository accounting,
        IJobRepository jobRepo)
    {
        _accounting = accounting;
        _jobRepo = jobRepo;
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
        Guid teamId, Guid jobId, CancellationToken ct = default, decimal procFreeBase = 0m)
    {
        var map = procFreeBase > 0m
            ? new Dictionary<Guid, decimal> { [teamId] = procFreeBase }
            : null;
        var dict = await ForTeamsAsync(new[] { teamId }, jobId, ct, map);
        return dict.TryGetValue(teamId, out var state)
            ? state
            : await BuildEmptyAsync(jobId, ct);
    }

    public async Task<Dictionary<Guid, PaymentState>> ForRegistrationsAsync(
        IReadOnlyCollection<Guid> registrationIds, Guid jobId, CancellationToken ct = default) =>
        await BuildBatchAsync(PaymentEntityKind.Registration, registrationIds, jobId, ct, null);

    public async Task<Dictionary<Guid, PaymentState>> ForTeamsAsync(
        IReadOnlyCollection<Guid> teamIds, Guid jobId, CancellationToken ct = default,
        IReadOnlyDictionary<Guid, decimal>? procFreeBaseByTeam = null) =>
        await BuildBatchAsync(PaymentEntityKind.Team, teamIds, jobId, ct, procFreeBaseByTeam);

    private async Task<Dictionary<Guid, PaymentState>> BuildBatchAsync(
        PaymentEntityKind kind, IReadOnlyCollection<Guid> entityIds, Guid jobId, CancellationToken ct,
        IReadOnlyDictionary<Guid, decimal>? procFreeBaseByEntity)
    {
        if (entityIds.Count == 0) return new();

        var (bAdd, ccRate, echeckRate) = await GetJobConfigAsync(jobId, ct);

        // Slice-aware path only when a caller supplied a nonzero proc-free base (teams on
        // proc-on-balance-only jobs). Everything else takes the totals path — identical
        // numbers to the pre-slice behavior.
        var wantsSlices = bAdd && procFreeBaseByEntity is not null
            && procFreeBaseByEntity.Values.Any(v => v > 0m);

        if (!wantsSlices)
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
            var procFreeBase = procFreeBaseByEntity!.GetValueOrDefault(entityId);

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

    private async Task<PaymentState> BuildEmptyAsync(Guid jobId, CancellationToken ct)
    {
        var (bAdd, ccRate, echeckRate) = await GetJobConfigAsync(jobId, ct);
        return PaymentState.Empty(bAdd, ccRate, echeckRate);
    }

    private async Task<(bool BAddProcessingFees, decimal CcRate, decimal EcheckRate)> GetJobConfigAsync(
        Guid jobId, CancellationToken ct)
    {
        var settings = await _jobRepo.GetJobFeeSettingsAsync(jobId, ct);
        var bAdd = settings?.BAddProcessingFees ?? false;
        var ccRaw = await _jobRepo.GetProcessingFeePercentAsync(jobId, ct);
        var echeckRaw = await _jobRepo.GetEcprocessingFeePercentAsync(jobId, ct);
        return (bAdd, ProcessingRateMath.ToCcMultiplier(ccRaw), ProcessingRateMath.ToEcheckMultiplier(echeckRaw));
    }
}
