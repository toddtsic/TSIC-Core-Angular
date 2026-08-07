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
/// "Authorized Rosters and Schedule Export" — the in-house replacement for the retired
/// SportsRecruits Basic-auth API (legacy ThirdPartyApis/RostersController.GetJobRosterPlayerData).
/// Same player dump (fixed contact/team columns + the job's dynamic player-form fields,
/// minus the legacy disallow list and waiver/upload fields), but HARD-gated to agegroups
/// flagged <c>BAllowApiRosterAccess</c> — the opt-in the legacy endpoint never enforced.
/// The sheet leads with a banner stating that scope, an "Included:" audit line, and the
/// per-agegroup release instruction, so the file explains its own conditioning — and names
/// the remedy — wherever it gets forwarded. Release is always the EVENT's act, never TSIC's.
/// (Schedule worksheet: ruled back IN 2026-08-06 — vendor compliance requirement.)
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

        // Schedule worksheet: exact legacy feed semantics (whole-job schedule, no agegroup
        // gate — public information). Fetched even when zero agegroups are released: the
        // roster tab renders its no-authorization message; the schedule is public either way.
        var games = await _reportingRepository.GetThirdPartyScheduleGamesAsync(jobId, cancellationToken);

        var fileBytes = BuildWorkbook(jobName, allowedAgegroups, players, dynamicFields, formValuesByReg, games);

        return new ReportExportResult
        {
            FileBytes = fileBytes,
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = "Authorized-Rosters-and-Schedule-Export.xlsx",
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
        Dictionary<Guid, IReadOnlyDictionary<string, JsonElement>> formValuesByReg,
        List<ThirdPartyScheduleGameDto> games)
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
        var banner = $"Authorized Rosters and Schedule Export — {jobName} — generated {DateTime.Now:MM/dd/yyyy}. " +
                     "Player rosters in this file contain ONLY the age groups this event has authorized for release. " +
                     "The Schedule tab is the complete event schedule (public information).";
        sheet.Range[1, 1].SetCellValue(banner);
        sheet.Range[1, 1, 1, columnCount].Merge();
        sheet.Range[1, 1].CellStyle.Font.Bold = true;

        // Row 2 — audit line: exactly which agegroups the flag admits, or the explicit
        // nothing-enabled message so an empty file reads as configuration, not a bug.
        var included = allowedAgegroups.Count > 0
            ? $"Authorized age groups: {string.Join(", ", allowedAgegroups)}"
            : "No age groups are currently authorized — this export contains no player data.";
        sheet.Range[2, 1].SetCellValue(included);
        sheet.Range[2, 1, 2, columnCount].Merge();
        sheet.Range[2, 1].CellStyle.Font.Italic = true;

        // Row 3 — the remedy, always present: authorization is per age group and only the
        // event can grant it. Names the exact toggle and where the event finds it, so a
        // recruiter holding this file knows who to ask and what to ask for — and an empty
        // or partial file never reads as a TSIC support ticket.
        var remedy = "Need an age group that isn't listed? Contact the event directly and ask them to enable " +
                     "\"Third-Party Roster Access\" for that age group (Teams & Rosters → L-A-D-T Editor → " +
                     "Age Group Details). Authorization is granted per age group, by the event only.";
        sheet.Range[3, 1].SetCellValue(remedy);
        sheet.Range[3, 1, 3, columnCount].Merge();
        sheet.Range[3, 1].CellStyle.Font.Italic = true;

        // Row 4 headers; data from row 5.
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

        BuildScheduleSheet(workbook, games);
        return workbook.ToByteArray();
    }

    /// <summary>
    /// Second worksheet — the complete event schedule, exact legacy feed format
    /// (ThirdPartyApis/SchedulesController.GetJobSchedule): same 13 columns under the
    /// legacy field names, ordered by game date then field, no agegroup gate.
    /// Schedules are public information; the release flag gates rosters only.
    /// </summary>
    private static void BuildScheduleSheet(IWorkbook workbook, List<ThirdPartyScheduleGameDto> games)
    {
        var sheet = workbook.Worksheets.Create("Schedule");

        string[] headers =
        {
            "Gid", "GDate", "AgegroupName", "DivName", "FName",
            "T1Type", "T1No", "T1Name", "T1Score",
            "T2Type", "T2No", "T2Name", "T2Score",
        };

        // Row 1 — scope note: this tab is deliberately NOT agegroup-gated.
        var note = "Complete event schedule — public information; not limited to authorized age groups.";
        sheet.Range[1, 1].SetCellValue(note);
        sheet.Range[1, 1, 1, headers.Length].Merge();
        sheet.Range[1, 1].CellStyle.Font.Italic = true;

        // Row 2 blank; row 3 headers; data from row 4.
        const int headerRow = 3;
        for (var col = 0; col < headers.Length; col++)
        {
            sheet.Range[headerRow, col + 1].SetCellValue(headers[col]);
            sheet.Range[headerRow, col + 1].CellStyle.Font.Bold = true;
        }

        for (var i = 0; i < games.Count; i++)
        {
            var g = games[i];
            var row = headerRow + 1 + i;
            var values = new object?[]
            {
                g.Gid, g.GDate, g.AgegroupName, g.DivName, g.FName,
                g.T1Type, g.T1No, g.T1Name, g.T1Score,
                g.T2Type, g.T2No, g.T2Name, g.T2Score,
            };
            for (var col = 0; col < values.Length; col++)
            {
                if (values[col] == null) continue;
                var target = sheet.Range[row, col + 1];
                target.SetCellValue(values[col]);
                // Game date keeps its TIME — mm/dd/yyyy alone would flatten kickoff times.
                if (values[col] is DateTime)
                {
                    target.NumberFormat = "mm/dd/yyyy hh:mm AM/PM";
                }
            }
        }

        sheet.UsedRange.AutofitColumns();
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
