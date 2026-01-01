using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Contracts.Repositories;
using TSIC.API.Services.Shared.Jobs;

namespace TSIC.API.Services.Shared.Registration;

public sealed class RegistrationQueryService : IRegistrationQueryService
{
    private readonly IJobLookupService _jobLookupService;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IUserRepository _userRepo;
    private static readonly string IsoDate = "yyyy-MM-dd";

    public RegistrationQueryService(
        IJobLookupService jobLookupService,
        IRegistrationRepository registrationRepo,
        ITeamRepository teamRepo,
        IUserRepository userRepo)
    {
        _jobLookupService = jobLookupService;
        _registrationRepo = registrationRepo;
        _teamRepo = teamRepo;
        _userRepo = userRepo;
    }

    public async Task<object> GetExistingRegistrationAsync(string jobPath, string familyUserId, string callerId)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId is null)
        {
            throw new KeyNotFoundException($"Job not found: {jobPath}");
        }

        var regsAll = await _registrationRepo.GetByJobAndFamilyUserIdAsync(
            jobId.Value,
            familyUserId,
            activePlayersOnly: true);

        var latestByPlayer = regsAll
            .GroupBy(r => r.UserId!)
            .ToDictionary(g => g.Key, g => g.First());

        var teams = new Dictionary<string, object>();
        foreach (var grp in regsAll.GroupBy(r => r.UserId!))
        {
            var playerId = grp.Key;
            var teamIds = grp
                .Where(r => r.AssignedTeamId.HasValue && r.AssignedTeamId.Value != Guid.Empty)
                .Select(r => r.AssignedTeamId!.Value.ToString())
                .Distinct()
                .ToList();
            if (teamIds.Count == 1)
            {
                teams[playerId] = teamIds[0];
            }
            else if (teamIds.Count > 1)
            {
                teams[playerId] = teamIds;
            }
        }

        var values = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var kv in latestByPlayer)
        {
            var pid = kv.Key;
            var reg = kv.Value;
            var map = BuildValuesMap(reg);
            values[pid] = map;
        }

        return new { teams, values };
    }

    public async Task<IEnumerable<FamilyRegistrationItemDto>> GetFamilyRegistrationsAsync(string jobPath, string familyUserId, string callerId)
    {
        var jobId = await _jobLookupService.GetJobIdByPathAsync(jobPath);
        if (jobId is null) throw new KeyNotFoundException($"Job not found: {jobPath}");

        var regs = await _registrationRepo.GetByJobAndFamilyUserIdAsync(
            jobId.Value,
            familyUserId,
            activePlayersOnly: true);

        if (regs.Count == 0) return Array.Empty<FamilyRegistrationItemDto>();

        var teamMap = await BuildTeamNameMap(jobId.Value, regs);
        var userMap = await BuildUserNameMap(regs);
        return regs.Select(r => ProjectFamilyRegistration(r, userMap, teamMap, jobPath)).ToList();
    }

    private async Task<Dictionary<Guid, string>> BuildTeamNameMap(Guid jobId, List<Registrations> regs)
    {
        var teamIds = regs.Where(r => r.AssignedTeamId.HasValue).Select(r => r.AssignedTeamId!.Value).Distinct().ToList();
        if (teamIds.Count == 0) return new();
        return await _teamRepo.GetTeamNameMapAsync(jobId, teamIds);
    }

    private async Task<Dictionary<string, (string? First, string? Last)>> BuildUserNameMap(List<Registrations> regs)
    {
        var playerIds = regs.Select(r => r.UserId!).Distinct().ToList();
        var nameMap = await _userRepo.GetUserNameMapAsync(playerIds);
        return nameMap.ToDictionary(kv => kv.Key, kv => (kv.Value.FirstName, kv.Value.LastName));
    }

    private static FamilyRegistrationItemDto ProjectFamilyRegistration(
        Registrations r,
        Dictionary<string, (string? First, string? Last)> userMap,
        Dictionary<Guid, string> teamMap,
        string jobPath) => new()
        {
            RegistrationId = r.RegistrationId,
            PlayerId = r.UserId!,
            PlayerFirstName = userMap.TryGetValue(r.UserId!, out var u) ? u.First : null,
            PlayerLastName = userMap.TryGetValue(r.UserId!, out var u2) ? u2.Last : null,
            JobId = r.JobId,
            JobPath = jobPath,
            AssignedTeamId = r.AssignedTeamId,
            AssignedTeamName = r.AssignedTeamId.HasValue && teamMap.TryGetValue(r.AssignedTeamId.Value, out var tn) ? tn : null,
            Modified = r.Modified,
            GradYear = r.GradYear,
            SportAssnId = r.SportAssnId,
            FeeBase = r.FeeBase,
            FeeDiscount = r.FeeDiscount,
            FeeDiscountMp = r.FeeDiscountMp,
            FeeDonation = r.FeeDonation,
            FeeLatefee = r.FeeLatefee,
            FeeProcessing = r.FeeProcessing,
            FeeTotal = r.FeeTotal,
            OwedTotal = r.OwedTotal,
            PaidTotal = r.PaidTotal
        };

    private static Dictionary<string, object?> BuildValuesMap(Registrations reg)
    {
        var map = new Dictionary<string, object?>();
        var props = typeof(Registrations).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        foreach (var p in props)
        {
            if (!ShouldIncludeProperty(p)) continue;
            var val = p.GetValue(reg);
            if (val is null) continue;
            map[p.Name] = NormalizeSimpleValue(val);
        }
        return map;
    }

    private static bool ShouldIncludeProperty(System.Reflection.PropertyInfo p)
    {
        if (!p.CanRead || p.GetIndexParameters().Length > 0) return false;
        var name = p.Name;
        var type = p.PropertyType;

        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type) && type != typeof(string))
        {
            return false;
        }

        static bool IsSimple(Type t)
        {
            var u = Nullable.GetUnderlyingType(t) ?? t;
            return u.IsPrimitive
                || u.IsEnum
                || u == typeof(string)
                || u == typeof(decimal)
                || u == typeof(DateTime)
                || u == typeof(Guid);
        }
        if (!IsSimple(type)) return false;
        if (name is nameof(Registrations.RegistrationAi)
            or nameof(Registrations.RegistrationId)
            or nameof(Registrations.RegistrationTs)
            or nameof(Registrations.RoleId)
            or nameof(Registrations.UserId)
            or nameof(Registrations.FamilyUserId)
            or nameof(Registrations.BActive)
            or nameof(Registrations.BConfirmationSent)
            or nameof(Registrations.JobId)
            or nameof(Registrations.LebUserId)
            or nameof(Registrations.Modified)
            or nameof(Registrations.RegistrationFormName)
            or nameof(Registrations.PaymentMethodChosen)
            or nameof(Registrations.FeeProcessing)
            or nameof(Registrations.FeeBase)
            or nameof(Registrations.FeeDiscount)
            or nameof(Registrations.FeeDiscountMp)
            or nameof(Registrations.FeeDonation)
            or nameof(Registrations.FeeLatefee)
            or nameof(Registrations.FeeTotal)
            or nameof(Registrations.OwedTotal)
            or nameof(Registrations.PaidTotal)
            or nameof(Registrations.CustomerId)
            or nameof(Registrations.DiscountCodeId)
            or nameof(Registrations.AssignedTeamId)
            or nameof(Registrations.AssignedAgegroupId)
            or nameof(Registrations.AssignedCustomerId)
            or nameof(Registrations.AssignedDivId)
            or nameof(Registrations.AssignedLeagueId)
            or nameof(Registrations.RegformId)
            or nameof(Registrations.AccountingApplyToSummaries))
        {
            return false;
        }
        if (!string.Equals(name, nameof(Registrations.SportAssnId), StringComparison.Ordinal) &&
            (name.EndsWith("Id", StringComparison.Ordinal) || name.EndsWith("ID", StringComparison.Ordinal)))
        {
            return false;
        }
        if (name.StartsWith("BWaiverSigned", StringComparison.Ordinal) || name.StartsWith("BUploaded", StringComparison.Ordinal))
        {
            return false;
        }
        if (name.StartsWith("Adn", StringComparison.Ordinal) || name.StartsWith("Regsaver", StringComparison.Ordinal))
        {
            return false;
        }
        return true;
    }

    private static object NormalizeSimpleValue(object val)
    {
        return val switch
        {
            DateTime dt => dt.ToString(IsoDate),
            Guid g => g.ToString(),
            _ => val
        };
    }
}
