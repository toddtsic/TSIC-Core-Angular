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

        var (bAdd, ccRate, echeckRate) = await GetJobConfigAsync(jobId, ct);

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
