using TSIC.Contracts.Dtos.ThirdPartyAccess;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Admin;

/// <summary>
/// "3rd Party Data Access" console (SU + SuperDirector): a customer administers, itself,
/// which vendor login (ApiAuthorized) may pull its rosters/schedule export — the event
/// releases the data directly and at its own risk. Reuse-only: this screen never mints a
/// first-time vendor account (that stays on the Administrators page); it re-applies the
/// customer's existing vendor history across its open-window jobs.
/// </summary>
public sealed class ThirdPartyAccessService : IThirdPartyAccessService
{
    // Mirrors the seeded reporting.JobReports row for the export (see the 08-01 base seed) —
    // the grant chokepoint upserts this row so a granted login can actually download.
    private const string ExportAction = "ThirdPartyRosterExport";
    private const string ExportTitle = "Authorized Rosters and Schedule Export";
    private const string ExportIcon = "file-earmark-spreadsheet";
    private const string ExportController = "Reporting";
    private const string ExportKind = "CrystalReport";
    private const string ExportGroupLabel = "Recruiting";
    private const int ExportSortOrder = 50;

    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IJobCloneRepository _jobCloneRepo; // sole owner of GetCustomerNameAsync
    private readonly IReportingRepository _reportingRepo;

    public ThirdPartyAccessService(
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo,
        IJobCloneRepository jobCloneRepo,
        IReportingRepository reportingRepo)
    {
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
        _jobCloneRepo = jobCloneRepo;
        _reportingRepo = reportingRepo;
    }

    public async Task<ThirdPartyAccessOverviewDto> GetOverviewAsync(
        Guid callerJobId, CancellationToken ct = default)
    {
        var customerId = await _jobRepo.GetCustomerIdAsync(callerJobId, ct)
            ?? throw new KeyNotFoundException($"Job '{callerJobId}' not found.");

        // Sequential awaits — repos share the scoped DbContext (no Task.WhenAll).
        var customerName = await _jobCloneRepo.GetCustomerNameAsync(customerId, ct) ?? string.Empty;
        var vendors = await _registrationRepo.GetThirdPartyVendorHistoryByCustomerAsync(customerId, ct);
        var jobs = await _registrationRepo.GetThirdPartyJobRowsByCustomerAsync(customerId, ct);

        return new ThirdPartyAccessOverviewDto
        {
            CustomerName = customerName,
            Vendors = vendors,
            Jobs = jobs
        };
    }

    public async Task<ThirdPartyAccessOverviewDto> GrantAsync(
        Guid callerJobId, Guid targetJobId, string userId, string currentUserId, CancellationToken ct = default)
    {
        var customerId = await ResolveCustomerScopeAsync(callerJobId, targetJobId, ct);

        // Reuse-only wall (server-side; the DDL is not the only gate): the account must
        // already hold ApiAuthorized history with THIS customer.
        if (!await _registrationRepo.HasApiAuthorizedHistoryWithCustomerAsync(customerId, userId, ct))
            throw new ArgumentException(
                "That account has no third-party authorization history with this organization. " +
                "First-time vendor logins are not created here.");

        var userRegs = await _registrationRepo.GetRegsByJobAndUserTrackedAsync(targetJobId, userId, ct);

        // Vendor alias accounts are single-purpose by design — any non-ApiAuthorized
        // registration on this job means this is NOT a vendor account. Refuse.
        if (userRegs.Any(r => !string.Equals(r.RoleId, RoleConstants.ApiAuthorized, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "That account holds other registrations on this job and cannot be a third-party export login.");

        // Cardinality invariant: at most one ACTIVE vendor login per job. A different
        // vendor must be disabled explicitly before a new one is granted — never silently
        // swapped (the disable is the deliberate act the audit trail should show).
        var jobApiRegs = await _registrationRepo.GetApiAuthorizedRegsForJobTrackedAsync(targetJobId, ct);
        if (jobApiRegs.Any(r => r.BActive == true && r.UserId != userId))
            throw new InvalidOperationException(
                "Another third party already has access on this job. Disable it first.");

        var existing = jobApiRegs.FirstOrDefault(r => r.UserId == userId);
        if (existing != null)
        {
            // Reactivate — never stack a second row (same rule as the Administrators grid).
            existing.BActive = true;
            existing.Modified = DateTime.Now;
            existing.LebUserId = currentUserId;
        }
        else
        {
            // Mirror the Administrators-page mint shape (admin registrations are fee-free).
            _registrationRepo.Add(new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                BActive = true,
                JobId = targetJobId,
                LebUserId = currentUserId,
                Modified = DateTime.Now,
                RegistrationCategory = "Director",
                RegistrationTs = DateTime.Now,
                RoleId = RoleConstants.ApiAuthorized,
                UserId = userId,
                FeeBase = 0,
                FeeDiscount = 0,
                FeeDiscountMp = 0,
                FeeDonation = 0,
                FeeLatefee = 0,
                FeeProcessing = 0,
                PaidTotal = 0
            });
        }

        await EnsureExportEntitlementRowAsync(targetJobId, currentUserId, ct);

        // One flush for both mutations — repos share the scoped DbContext.
        await _registrationRepo.SaveChangesAsync(ct);

        return await GetOverviewAsync(callerJobId, ct);
    }

    public async Task<ThirdPartyAccessOverviewDto> DisableAsync(
        Guid callerJobId, Guid targetJobId, string currentUserId, CancellationToken ct = default)
    {
        await ResolveCustomerScopeAsync(callerJobId, targetJobId, ct);

        // Disable ALL active vendor logins on the job (one by invariant, but a defensive
        // sweep costs nothing). Idempotent: nothing active is a no-op, not an error.
        // Rows are never deleted — history feeds the reuse-only picker.
        var jobApiRegs = await _registrationRepo.GetApiAuthorizedRegsForJobTrackedAsync(targetJobId, ct);
        foreach (var reg in jobApiRegs.Where(r => r.BActive == true))
        {
            reg.BActive = false;
            reg.Modified = DateTime.Now;
            reg.LebUserId = currentUserId;
        }

        await _registrationRepo.SaveChangesAsync(ct);

        return await GetOverviewAsync(callerJobId, ct);
    }

    /// <summary>
    /// Both jobs must exist and belong to the same customer — the console's authority is
    /// customer-wide but never crosses customers (SuperUser included: the screen operates
    /// from within a job, so the same wall applies). Returns the customer id.
    /// </summary>
    private async Task<Guid> ResolveCustomerScopeAsync(Guid callerJobId, Guid targetJobId, CancellationToken ct)
    {
        var callerCustomerId = await _jobRepo.GetCustomerIdAsync(callerJobId, ct)
            ?? throw new KeyNotFoundException($"Job '{callerJobId}' not found.");
        var targetCustomerId = await _jobRepo.GetCustomerIdAsync(targetJobId, ct)
            ?? throw new KeyNotFoundException($"Job '{targetJobId}' not found.");

        if (callerCustomerId != targetCustomerId)
            throw new InvalidOperationException("That job belongs to a different organization.");

        return callerCustomerId;
    }

    /// <summary>
    /// A granted login with no reporting.JobReports ApiAuthorized row would 403 on the
    /// export — deny-by-default entitlement. Upsert the row here so the grant is complete:
    /// create if missing, reactivate if present-but-inactive. Deliberately NOT reversed on
    /// disable: the row grants nothing without an active login.
    /// </summary>
    private async Task EnsureExportEntitlementRowAsync(Guid jobId, string currentUserId, CancellationToken ct)
    {
        var row = await _reportingRepo.GetCrystalActionRowTrackedAsync(
            jobId, RoleConstants.ApiAuthorized, ExportAction, ct);

        if (row == null)
        {
            await _reportingRepo.AddJobReportAsync(new JobReports
            {
                JobReportId = Guid.NewGuid(),
                JobId = jobId,
                RoleId = RoleConstants.ApiAuthorized,
                Title = ExportTitle,
                IconName = ExportIcon,
                Controller = ExportController,
                Action = ExportAction,
                Kind = ExportKind,
                GroupLabel = ExportGroupLabel,
                SortOrder = ExportSortOrder,
                Active = true,
                Modified = DateTime.Now,
                LebUserId = currentUserId
            }, ct);
        }
        else if (!row.Active)
        {
            row.Active = true;
            row.Modified = DateTime.Now;
            row.LebUserId = currentUserId;
        }
    }
}
