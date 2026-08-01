using System.Reflection;
using System.Text.Json;
using Syncfusion.XlsIO;
using TSIC.API.Services.Metadata;
using TSIC.API.Services.Shared.Utilities;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Reporting;

public interface IThirdPartyRosterExportService
{
    Task<ReportExportResult> GenerateAsync(Guid jobId, CancellationToken cancellationToken = default);
}

/// <summary>
/// "Third-Party Roster Export" — the in-house replacement for the retired SportsRecruits
/// Basic-auth API (legacy ThirdPartyApis/RostersController.GetJobRosterPlayerData).
/// Same player dump (fixed contact/team columns + the job's dynamic player-form fields,
/// minus the legacy disallow list and waiver/upload fields), but HARD-gated to agegroups
/// flagged <c>BAllowApiRosterAccess</c> — the opt-in the legacy endpoint never enforced.
/// The sheet leads with a banner stating that scope and an "Included:" audit line so the
/// file explains its own conditioning wherever it gets forwarded. No schedule export
/// exists or should be added here — retiring that feed was the point.
/// </summary>
public class ThirdPartyRosterExportService : IThirdPartyRosterExportService
{
    // Legacy disallow list (uniform/assn fields never shipped to the vendor), matched
    // against both the metadata field name and its DB column.
    private static readonly HashSet<string> DisallowedFormFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "JerseySize", "ShortsSize", "TShirt", "Kilt", "SportAssnIdexpDate",
    };

    private static readonly string[] FixedHeaders =
    {
        "Registration Id", "Registered", "Last Modified",
        "First Name", "Last Name", "Email",
        "Club", "Team", "Age Group", "Pool",
        "Mom First Name", "Mom Last Name", "Mom Email",
        "Dad First Name", "Dad Last Name", "Dad Email",
    };

    private readonly IReportingRepository _reportingRepository;
    private readonly IJobRepository _jobRepository;
    private readonly IProfileMetadataService _profileMetadataService;

    public ThirdPartyRosterExportService(
        IReportingRepository reportingRepository,
        IJobRepository jobRepository,
        IProfileMetadataService profileMetadataService)
    {
        _reportingRepository = reportingRepository;
        _jobRepository = jobRepository;
        _profileMetadataService = profileMetadataService;
    }

    public async Task<ReportExportResult> GenerateAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var context = await _reportingRepository.GetThirdPartyRosterContextAsync(jobId, cancellationToken);
        var jobName = context?.JobName ?? string.Empty;
        var allowedAgegroups = context?.AllowedAgegroupNames ?? new List<string>();

        var players = allowedAgegroups.Count == 0
            ? new List<ThirdPartyRosterPlayerDto>()
            : await _reportingRepository.GetThirdPartyRosterPlayersAsync(jobId, cancellationToken);

        // Dynamic columns: the job's player-form fields (migrated metadata JSON), minus
        // the legacy disallow list, waiver fields, and upload markers. Only fields that
        // resolve to a real Registrations column are kept — identity fields the form
        // shares with the fixed block (firstName/email live on AspNetUsers) drop out
        // here rather than rendering as permanently blank columns.
        var dynamicFields = await BuildDynamicFieldListAsync(jobId, cancellationToken);

        // Values are read off untracked Registrations entities by reflection — the same
        // FormValueMapper path the registration detail panel uses.
        var formValuesByReg = new Dictionary<Guid, IReadOnlyDictionary<string, JsonElement>>();
        if (dynamicFields.Count > 0 && players.Count > 0)
        {
            var mapped = dynamicFields.Select(f => (f.Name, f.DbColumn)).ToList();
            var entities = await _reportingRepository.GetRegistrationsForFormFieldReadAsync(
                players.Select(p => p.RegistrationId).ToList(), cancellationToken);
            foreach (var reg in entities)
            {
                formValuesByReg[reg.RegistrationId] = FormValueMapper.BuildFormValuesDictionary(reg, mapped);
            }
        }

        var fileBytes = BuildWorkbook(jobName, allowedAgegroups, players, dynamicFields, formValuesByReg);

        return new ReportExportResult
        {
            FileBytes = fileBytes,
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = "Third-Party-Roster-Export.xlsx",
        };
    }

    private sealed record DynamicField(string Name, string DbColumn, string Header);

    private async Task<List<DynamicField>> BuildDynamicFieldListAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var meta = await _jobRepository.GetJobMetadataAsync(jobId, cancellationToken);
        if (meta == null || string.IsNullOrWhiteSpace(meta.PlayerProfileMetadataJson))
            return new List<DynamicField>();

        var parsed = _profileMetadataService.Parse(meta.PlayerProfileMetadataJson, meta.JsonOptions);
        var waiverNames = new HashSet<string>(parsed.WaiverFieldNames, StringComparer.OrdinalIgnoreCase);
        var regType = typeof(Registrations);
        var result = new List<DynamicField>();
        var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in parsed.TypedFields.OrderBy(t => t.Order))
        {
            if (string.IsNullOrWhiteSpace(f.Name)) continue;
            var dbColumn = string.IsNullOrWhiteSpace(f.DbColumn) ? f.Name : f.DbColumn;

            // Legacy exclusions: uniform/assn disallow list, waiver fields, upload markers.
            if (DisallowedFormFields.Contains(f.Name) || DisallowedFormFields.Contains(dbColumn)) continue;
            if (waiverNames.Contains(f.Name)) continue;
            if (f.Name.StartsWith("BWaiver", StringComparison.OrdinalIgnoreCase)
                || dbColumn.StartsWith("BWaiver", StringComparison.OrdinalIgnoreCase)) continue;
            if (dbColumn.StartsWith("BUploaded", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(f.InputType, "FILE", StringComparison.OrdinalIgnoreCase)) continue;

            // Must be a real Registrations column (FormValueMapper reads by reflection).
            var prop = regType.GetProperty(dbColumn, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) continue;

            if (!seenColumns.Add(prop.Name)) continue;

            var header = string.IsNullOrWhiteSpace(f.DisplayName) ? f.Name : f.DisplayName;
            result.Add(new DynamicField(f.Name, dbColumn, header));
        }

        return result;
    }

    private static byte[] BuildWorkbook(
        string jobName,
        IReadOnlyList<string> allowedAgegroups,
        List<ThirdPartyRosterPlayerDto> players,
        List<DynamicField> dynamicFields,
        Dictionary<Guid, IReadOnlyDictionary<string, JsonElement>> formValuesByReg)
    {
        using var excelEngine = new ExcelEngine();
        var application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        var workbook = application.Workbooks.Create(1);
        var sheet = workbook.Worksheets[0];
        sheet.Name = "Players";

        var headers = FixedHeaders.Concat(dynamicFields.Select(f => f.Header)).ToList();
        var columnCount = Math.Max(headers.Count, 1);

        // Row 1 — scope banner. The file travels; it must state its own conditioning.
        var banner = $"Third-Party Roster Export — {jobName} — generated {DateTime.Now:MM/dd/yyyy}. " +
                     "Includes ONLY age groups with \"Third-Party Roster Access\" enabled " +
                     "(LADT → Age Group settings). Age groups not flagged are excluded.";
        sheet.Range[1, 1].SetCellValue(banner);
        sheet.Range[1, 1, 1, columnCount].Merge();
        sheet.Range[1, 1].CellStyle.Font.Bold = true;

        // Row 2 — audit line: exactly which agegroups the flag admits, or the explicit
        // nothing-enabled message so an empty file reads as configuration, not a bug.
        var included = allowedAgegroups.Count > 0
            ? $"Included: {string.Join(", ", allowedAgegroups)}"
            : "No age groups have \"Third-Party Roster Access\" enabled for this event — no player data included.";
        sheet.Range[2, 1].SetCellValue(included);
        sheet.Range[2, 1, 2, columnCount].Merge();
        sheet.Range[2, 1].CellStyle.Font.Italic = true;

        // Row 3 blank; row 4 headers; data from row 5.
        const int headerRow = 4;
        for (var col = 0; col < headers.Count; col++)
        {
            sheet.Range[headerRow, col + 1].SetCellValue(headers[col]);
            sheet.Range[headerRow, col + 1].CellStyle.Font.Bold = true;
        }

        for (var i = 0; i < players.Count; i++)
        {
            var p = players[i];
            var row = headerRow + 1 + i;
            var fixedValues = new object?[]
            {
                p.RegistrationId.ToString(), p.RegistrationTimestamp, p.LastModifiedTimestamp,
                p.FirstName, p.LastName, p.Email,
                p.TeamClubName, p.TeamName, p.AgegroupName, p.PoolName,
                p.MomFirstName, p.MomLastName, p.MomEmail,
                p.DadFirstName, p.DadLastName, p.DadEmail,
            };
            for (var col = 0; col < fixedValues.Length; col++)
            {
                SetCell(sheet, row, col + 1, fixedValues[col]);
            }

            formValuesByReg.TryGetValue(p.RegistrationId, out var formValues);
            for (var d = 0; d < dynamicFields.Count; d++)
            {
                object? value = null;
                if (formValues != null && formValues.TryGetValue(dynamicFields[d].Name, out var el))
                {
                    value = JsonElementToCell(el);
                }
                SetCell(sheet, row, fixedValues.Length + d + 1, value);
            }
        }

        sheet.UsedRange.AutofitColumns();
        return workbook.ToByteArray();
    }

    /// <summary>Date formatting mirrors ReportingService's SP-Excel render (mm/dd/yyyy).</summary>
    private static void SetCell(IWorksheet sheet, int row, int col, object? value)
    {
        if (value == null) return;
        var target = sheet.Range[row, col];
        if (value is DateTime)
        {
            target.SetCellValue(value);
            target.NumberFormat = "mm/dd/yyyy";
        }
        else
        {
            target.SetCellValue(value);
        }
    }

    private static object? JsonElementToCell(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.Number => el.TryGetDecimal(out var dec) ? dec : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => el.GetString(),
        _ => el.ToString(),
    };
}
