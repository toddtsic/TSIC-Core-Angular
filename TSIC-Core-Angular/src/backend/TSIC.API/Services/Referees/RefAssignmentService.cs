using Microsoft.AspNetCore.Identity;
using Syncfusion.XlsIO;
using TSIC.API.Utilities;
using TSIC.Contracts.Dtos.Referees;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.Identity;

namespace TSIC.API.Services.Referees;

/// <summary>
/// Service for referee assignment operations: search, assign, copy, import, seed, and calendar.
/// </summary>
public sealed class RefAssignmentService : IRefAssignmentService
{
    private readonly IRefAssignmentRepository _refRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly UserManager<ApplicationUser> _userManager;

    public RefAssignmentService(
        IRefAssignmentRepository refRepo,
        IRegistrationRepository registrationRepo,
        UserManager<ApplicationUser> userManager)
    {
        _refRepo = refRepo;
        _registrationRepo = registrationRepo;
        _userManager = userManager;
    }

    // ── Queries (delegate straight to repo) ──

    public Task<List<RefereeSummaryDto>> GetRefereesAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetRefereesForJobAsync(jobId, ct);

    public Task<RefScheduleFilterOptionsDto> GetFilterOptionsAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetRefScheduleFilterOptionsAsync(jobId, ct);

    public Task<List<RefScheduleGameDto>> SearchScheduleAsync(Guid jobId, RefScheduleSearchRequest request, CancellationToken ct = default)
        => _refRepo.SearchScheduleAsync(jobId, request, ct);

    public Task<List<GameRefAssignmentDto>> GetAllAssignmentsAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetAllAssignmentsForJobAsync(jobId, ct);

    public Task<List<RefGameDetailsDto>> GetGameRefDetailsAsync(int gid, Guid jobId, CancellationToken ct = default)
        => _refRepo.GetGameRefDetailsAsync(gid, jobId, ct);

    public Task<List<RefereeCalendarEventDto>> GetCalendarEventsAsync(Guid jobId, CancellationToken ct = default)
        => _refRepo.GetCalendarEventsAsync(jobId, ct);

    // ── Assign Refs ──

    public async Task AssignRefsToGameAsync(AssignRefsRequest request, string auditUserId, CancellationToken ct = default)
    {
        await _refRepo.ReplaceAssignmentsForGameAsync(request.Gid, request.RefRegistrationIds, auditUserId, ct);
    }

    // ── Copy Refs ──

    public async Task<List<int>> CopyGameRefsAsync(Guid jobId, CopyGameRefsRequest request, string auditUserId, CancellationToken ct = default)
    {
        // Get source game's assigned ref IDs
        var sourceAssignments = await _refRepo.GetAssignmentsForGameAsync(request.Gid, ct);
        var refIds = sourceAssignments
            .Where(a => a.RefRegistrationId != null)
            .Select(a => a.RefRegistrationId!.Value)
            .ToList();

        if (refIds.Count == 0)
            return [];

        // Find source game's field + date via a targeted search
        var allGames = await _refRepo.SearchScheduleAsync(jobId, new RefScheduleSearchRequest(), ct);
        var sourceGame = allGames.FirstOrDefault(g => g.Gid == request.Gid);
        if (sourceGame?.FieldId == null)
            return [];

        // Get all games on the same field for the same date, ordered by time
        var gamesOnField = await _refRepo.GetGamesOnFieldForDateAsync(
            sourceGame.FieldId.Value, sourceGame.GameDate, jobId, ct);

        var sourceIndex = gamesOnField.FindIndex(g => g.Gid == request.Gid);
        if (sourceIndex < 0)
            return [];

        // Walk in the requested direction, applying skip interval
        var affectedGids = new List<int>();
        var step = request.SkipInterval + 1;
        var collected = 0;

        if (request.CopyDown)
        {
            for (var i = sourceIndex + step; i < gamesOnField.Count && collected < request.NumberTimeslots; i += step)
            {
                affectedGids.Add(gamesOnField[i].Gid);
                collected++;
            }
        }
        else
        {
            for (var i = sourceIndex - step; i >= 0 && collected < request.NumberTimeslots; i -= step)
            {
                affectedGids.Add(gamesOnField[i].Gid);
                collected++;
            }
        }

        // Apply assignments to each target game (sequential — DbContext not thread-safe)
        foreach (var targetGid in affectedGids)
        {
            await _refRepo.ReplaceAssignmentsForGameAsync(targetGid, refIds, auditUserId, ct);
        }

        return affectedGids;
    }

    // ── Import Refs from an Excel (.xlsx) workbook ──
    // Columns are resolved by HEADER NAME (row 1), so column order/extra columns
    // don't matter — reordering can no longer silently misfile data as it could
    // with the old positional CSV parser.

    public async Task<ImportRefereesResult> ImportRefereesAsync(Guid jobId, Stream fileStream, string auditUserId, CancellationToken ct = default)
    {
        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        IWorkbook workbook = application.Workbooks.Open(fileStream);

        var ws = workbook.Worksheets.Count > 0 ? workbook.Worksheets[0] : null;
        if (ws == null)
        {
            errors.Add("No worksheet found in the uploaded file.");
            return new ImportRefereesResult { Imported = 0, Skipped = 0, Errors = errors };
        }

        var col = ResolveImportColumns(ws);
        if (col.FirstName == 0 || col.LastName == 0)
        {
            errors.Add("Missing required column header 'FirstName' and/or 'LastName' in row 1. Use the downloaded template.");
            return new ImportRefereesResult { Imported = 0, Skipped = 0, Errors = errors };
        }

        // Users already registered as referees in THIS job — keeps re-imports idempotent.
        // A referee login is REUSED across events, so de-dup is keyed on the user (not the
        // username) and a row is only skipped when that user already holds a ref reg here.
        var existingRefUserIds = await _refRepo.GetRefereeUserIdsForJobAsync(jobId, ct);

        var lastRow = ws.UsedRange.LastRow;
        for (var row = 2; row <= lastRow; row++)
        {
            string Cell(int c) => c > 0 ? (ws.Range[row, c].DisplayText?.Trim() ?? "") : "";

            var firstName = Cell(col.FirstName);
            var lastName = Cell(col.LastName);

            // Skip fully blank rows (Excel often reports trailing empties in UsedRange)
            if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName))
                continue;

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                errors.Add($"Row {row}: FirstName and LastName are required.");
                continue;
            }

            var email = Cell(col.Email);
            var cellPhone = NullIfEmpty(Cell(col.Cellphone));
            var street = NullIfEmpty(Cell(col.Street));
            var city = NullIfEmpty(Cell(col.City));
            var state = NullIfEmpty(Cell(col.State));
            var zip = NullIfEmpty(Cell(col.Zip));
            var dobStr = Cell(col.Dob);
            var gender = NullIfEmpty(Cell(col.Gender));
            var certNumber = NullIfEmpty(Cell(col.CertNumber));
            var certExpiryStr = Cell(col.CertExpiry);

            // Deterministic referee username (initial + last name).
            var username = $"Ref-{firstName[0]}{lastName}".Replace(" ", "");

            // Reuse an existing referee login across events; only create the user the first
            // time we ever see them. NOTE: identity is keyed on this username, so two
            // DIFFERENT people who reduce to the same initial+lastname share one login — a
            // pre-existing limitation of the username scheme, not introduced by reuse.
            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = username,
                    Email = !string.IsNullOrWhiteSpace(email) ? email : $"{username}@ref.local",
                    FirstName = firstName,
                    LastName = lastName,
                    Gender = !string.IsNullOrWhiteSpace(gender) ? gender : "U",
                    Cellphone = cellPhone,
                    StreetAddress = street,
                    City = city,
                    State = state,
                    PostalCode = zip,
                    Dob = DateTime.TryParse(dobStr, out var dob) ? dob : new DateTime(1980, 1, 1),
                    LebUserId = auditUserId,
                    Modified = DateTime.Now
                };

                var createResult = await _userManager.CreateAsync(user, username);
                if (!createResult.Succeeded)
                {
                    errors.Add($"Row {row}: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
                    continue;
                }
            }

            // Already a referee in THIS job? Then this row (or a duplicate of it) is already
            // imported — skip. HashSet.Add returns false when the id is already present, which
            // also collapses duplicate rows within the same file.
            if (!existingRefUserIds.Add(user.Id))
            {
                skipped++;
                continue;
            }

            _registrationRepo.Add(new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = RoleConstants.Referee,
                JobId = jobId,
                BActive = true,
                RegistrationTs = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = auditUserId,
                SportAssnId = certNumber,
                SportAssnIdexpDate = DateTime.TryParse(certExpiryStr, out var certExpiry) ? certExpiry : null
            });
            await _registrationRepo.SaveChangesAsync(ct);
            imported++;
        }

        return new ImportRefereesResult
        {
            Imported = imported,
            Skipped = skipped,
            Errors = errors
        };
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Resolve referee-import column indices from the header row (row 1) by name.</summary>
    private static ImportColumns ResolveImportColumns(IWorksheet ws)
    {
        var map = new ImportColumns();
        var lastCol = ws.UsedRange.LastColumn;
        for (var c = 1; c <= lastCol; c++)
        {
            var h = (ws.Range[1, c].DisplayText ?? "")
                .Trim().Replace(" ", "").Replace("_", "").ToLowerInvariant();
            switch (h)
            {
                case "firstname": map.FirstName = c; break;
                case "lastname": map.LastName = c; break;
                case "email": case "emailaddress": map.Email = c; break;
                case "cellphone": case "cell": case "phone": case "mobile": map.Cellphone = c; break;
                case "street": case "streetaddress": case "address": map.Street = c; break;
                case "city": map.City = c; break;
                case "state": map.State = c; break;
                case "zip": case "zipcode": case "postalcode": map.Zip = c; break;
                case "dob": case "dateofbirth": case "birthdate": map.Dob = c; break;
                case "gender": case "sex": map.Gender = c; break;
                case "certificationnumber": case "certnumber": case "certno": case "sportassnid": map.CertNumber = c; break;
                case "certificationexpiry": case "certexpiry": case "certificationexpiration": case "certexpiration": map.CertExpiry = c; break;
            }
        }
        return map;
    }

    private struct ImportColumns
    {
        public int FirstName, LastName, Email, Cellphone, Street, City, State, Zip, Dob, Gender, CertNumber, CertExpiry;
    }

    // ── Blank Excel (.xlsx) import template ──

    public byte[] GenerateImportTemplate()
    {
        using var excelEngine = new ExcelEngine();
        IApplication application = excelEngine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = application.Workbooks.Create(1);

        // ── Sheet 1: the fill-in sheet the importer reads (headers only) ──
        var ws = workbook.Worksheets[0];
        ws.Name = "Referees";

        var headers = new[]
        {
            "FirstName", "LastName", "Email", "Cellphone", "Street", "City",
            "State", "Zip", "DOB", "Gender", "CertificationNumber", "CertificationExpiry"
        };
        for (var c = 1; c <= headers.Length; c++)
            ws.Range[1, c].Text = headers[c - 1];

        var headerRange = ws.Range[1, 1, 1, headers.Length];
        headerRange.CellStyle.Font.Bold = true;
        headerRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(68, 114, 196);
        headerRange.CellStyle.Font.RGBColor = Syncfusion.Drawing.Color.White;

        // Highlight the two required columns (orange) so they read as mandatory.
        var requiredRange = ws.Range[1, 1, 1, 2];
        requiredRange.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(198, 89, 17);

        ws.UsedRange.AutofitColumns();

        // ── Sheet 2: human-readable instructions ──
        var info = workbook.Worksheets.Create("Instructions");
        var lines = new (string Col, string Required, string Notes)[]
        {
            ("Column", "Required?", "Notes / Format"),
            ("FirstName", "Yes", "Referee first name."),
            ("LastName", "Yes", "Referee last name."),
            ("Email", "No", "Used for login/notices; a placeholder is generated if left blank."),
            ("Cellphone", "No", "Digits only, e.g. 5551234567."),
            ("Street", "No", "Street address."),
            ("City", "No", ""),
            ("State", "No", "Two-letter state, e.g. NY."),
            ("Zip", "No", "Postal code."),
            ("DOB", "No", "Date of birth, MM/DD/YYYY."),
            ("Gender", "No", "M, F, or U (unspecified)."),
            ("CertificationNumber", "No", "Referee certification / association ID."),
            ("CertificationExpiry", "No", "Certification expiry date, MM/DD/YYYY."),
        };
        for (var r = 0; r < lines.Length; r++)
        {
            info.Range[r + 1, 1].Text = lines[r].Col;
            info.Range[r + 1, 2].Text = lines[r].Required;
            info.Range[r + 1, 3].Text = lines[r].Notes;
        }
        var infoHeader = info.Range[1, 1, 1, 3];
        infoHeader.CellStyle.Font.Bold = true;
        infoHeader.CellStyle.Color = Syncfusion.Drawing.Color.FromArgb(68, 114, 196);
        infoHeader.CellStyle.Font.RGBColor = Syncfusion.Drawing.Color.White;
        info.UsedRange.AutofitColumns();

        return workbook.ToByteArray();
    }

    // ── Seed Test Refs ──

    public async Task<List<RefereeSummaryDto>> SeedTestRefereesAsync(Guid jobId, int count, string auditUserId, CancellationToken ct = default)
    {
        // Usernames are GLOBAL in AspNetUsers, but a "test referee" belongs to ONE job.
        // Scope the generated username to the job — otherwise seeding a second job collides
        // with the first job's TestRef-NNN users and skips every row, creating nobody.
        var jobTag = jobId.ToString("N")[..8];

        for (var i = 1; i <= count; i++)
        {
            var paddedNum = i.ToString("D3");
            var username = $"TestRef-{jobTag}-{paddedNum}";

            if (await _userManager.FindByNameAsync(username) != null)
                continue;

            var user = new ApplicationUser
            {
                UserName = username,
                Email = $"{username}@test.local",
                FirstName = "Test",
                LastName = $"Referee {paddedNum}",
                Gender = "U",
                Dob = new DateTime(1990, 1, 1),
                LebUserId = auditUserId,
                Modified = DateTime.Now
            };

            var result = await _userManager.CreateAsync(user, username);
            if (!result.Succeeded)
                continue;

            _registrationRepo.Add(new Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = user.Id,
                RoleId = RoleConstants.Referee,
                JobId = jobId,
                BActive = true,
                RegistrationTs = DateTime.Now,
                Modified = DateTime.Now,
                LebUserId = auditUserId
            });
            await _registrationRepo.SaveChangesAsync(ct);
        }

        return await _refRepo.GetRefereesForJobAsync(jobId, ct);
    }

    // ── Purge All ──

    public async Task DeleteAllAsync(Guid jobId, CancellationToken ct = default)
    {
        await _refRepo.DeleteAllAssignmentsForJobAsync(jobId, ct);
        await _refRepo.DeleteAllRefereeRegistrationsForJobAsync(jobId, ct);
    }

}
