# Migration Plan: UploadJobUniformNumbers → Uniform Number Upload

## Context

The legacy TSIC-Unify-2024 project has an `UploadJobUniformNumbers/Index` page that allows admins to
bulk-update player uniform numbers and day groups by uploading an Excel spreadsheet. The legacy
implementation requires admins to manually construct a `.xlsx` file with raw `RegistrationId` GUIDs —
error-prone and unfriendly. We're modernizing this with a **download-template → edit → re-upload**
workflow that pre-populates the spreadsheet with player names and teams.

**Legacy path**: `UploadJobUniformNumbers/Index`
**New route**: `/:jobPath/admin/uniform-upload`
**Auth**: `AdminOnly` (Director, SuperDirector, SuperUser)

### What Already Exists

- **Entity fields**: `Registrations.UniformNo` (string?) and `Registrations.DayGroup` (string?) — already in the DB
- **EPPlus**: Already installed in `TSIC.API.csproj` with non-commercial license
- **EPPlus usage pattern**: `ReportingService.BuildExcelFromDataReader()` — sets license, creates `ExcelPackage`
- **IRegistrationRepository**: 40+ methods, includes `GetByIdsAsync()`, `Update()`, `SaveChangesAsync()`
- **Image upload component**: `image-upload.component.ts` — drag-drop pattern reference (but for images, not spreadsheets)
- **jobId resolution**: `User.GetJobIdFromRegistrationAsync(_jobLookupService)` via `IJobLookupService`

---

## 1. Legacy Pain Points

- **Manual GUID entry** — Admin must know/copy RegistrationId GUIDs into Excel
- **No template** — No way to get a pre-populated file; must build from scratch
- **Silent failures** — Errors swallowed in catch block, returns count only
- **No validation** — Doesn't verify registrations belong to the current job
- **No feedback** — Just a number; admin doesn't know which rows failed or why

## 2. Modern Vision

A two-step workflow:

1. **Download Template** — Click button, get `.xlsx` with all players for the current job (RegistrationId, FirstName, LastName, TeamName, current UniformNo, current DayGroup). Admin fills in/edits the last two columns.
2. **Upload** — Drag-drop or browse for the edited `.xlsx`. Server validates every row, applies valid updates, returns a detailed result report.

**Result report** shows:
- Total rows processed
- Rows updated successfully (count)
- Rows skipped with per-row reasons (invalid GUID, registration not found, wrong job, no changes)
- Rows with errors

## 3. Architecture

```
UniformUploadController [AdminOnly]
  ├─ GET  template     → IUniformUploadService.GenerateTemplateAsync(jobId)  → .xlsx bytes
  └─ POST upload       → IUniformUploadService.ProcessUploadAsync(jobId, stream)  → UniformUploadResultDto
       └─ IRegistrationRepository (existing)
            ├─ new: GetPlayerRosterForTemplateAsync(jobId)  → List<UniformTemplateRow>
            ├─ existing: GetByIdsAsync(registrationIds)
            ├─ existing: Update(registration)
            └─ existing: SaveChangesAsync()
```

No new repository class needed — extend `IRegistrationRepository` with one new query method.

---

## 4. Files

### Created

| Layer | File | Purpose |
|-------|------|---------|
| DTOs | `TSIC.Contracts/Dtos/UniformUpload/UniformUploadDtos.cs` | Request/response records |
| Service interface | `TSIC.Contracts/Services/IUniformUploadService.cs` | Service contract |
| Service impl | `TSIC.API/Services/Admin/UniformUploadService.cs` | EPPlus template gen + upload parse + update logic |
| Controller | `TSIC.API/Controllers/UniformUploadController.cs` | 2 endpoints (GET template, POST upload) |
| FE component TS | `views/admin/uniform-upload/uniform-upload.component.ts` | Angular component |
| FE component HTML | `views/admin/uniform-upload/uniform-upload.component.html` | Template |
| FE component SCSS | `views/admin/uniform-upload/uniform-upload.component.scss` | Styles |

### Modified

| File | Change |
|------|--------|
| `TSIC.Contracts/Repositories/IRegistrationRepository.cs` | Add `GetPlayerRosterForTemplateAsync()` + `UniformTemplateRow` record |
| `TSIC.Infrastructure/Repositories/RegistrationRepository.cs` | Implement `GetPlayerRosterForTemplateAsync()` |
| `TSIC.API/Program.cs` | 1 DI registration for `IUniformUploadService` |
| `app.routes.ts` | 1 admin route (`admin/uniform-upload`) |
| `NavEditorService.cs` | 1 legacy route mapping (`uploadjobuniformnumbers/index` → `admin/uniform-upload`) |

---

## 5. Backend Detail

### DTOs (`UniformUploadDtos.cs`)

```csharp
public record UniformUploadResultDto
{
    public required int TotalRows { get; init; }
    public required int UpdatedCount { get; init; }
    public required int SkippedCount { get; init; }
    public required int ErrorCount { get; init; }
    public required List<UniformUploadRowError> Errors { get; init; }
}

public record UniformUploadRowError
{
    public required int Row { get; init; }
    public required string RegistrationId { get; init; }
    public required string Reason { get; init; }
}
```

### New Repository Method

`GetPlayerRosterForTemplateAsync(Guid jobId)` — AsNoTracking query joining Registrations → Users → Teams, filtered to Player role, returning:

```csharp
public record UniformTemplateRow
{
    public required Guid RegistrationId { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string TeamName { get; init; }
    public string? UniformNo { get; init; }
    public string? DayGroup { get; init; }
}
```

### Service Logic

**`GenerateTemplateAsync(jobId)`**:
1. Call `_registrationRepo.GetPlayerRosterForTemplateAsync(jobId)`
2. Build `.xlsx` with EPPlus: header row + data rows
3. Columns: RegistrationId (read-only styling), FirstName (read-only), LastName (read-only), TeamName (read-only), UniformNo (editable), DayGroup (editable)
4. Lock the read-only columns (gray background), leave editable columns white
5. Return `byte[]`

**`ProcessUploadAsync(jobId, stream)`**:
1. Parse `.xlsx` with EPPlus — read all rows
2. For each row:
   - Validate `RegistrationId` is a parseable GUID → skip if not
   - Collect all valid GUIDs
3. Batch-load all registrations: `_registrationRepo.GetByIdsAsync(validGuids)`
4. Validate each belongs to `jobId` → skip if wrong job
5. Apply updates where values changed (UniformNo and/or DayGroup)
6. `SaveChangesAsync()` once
7. Return `UniformUploadResultDto` with per-row error details

**Partial success**: Valid rows applied, invalid rows reported individually.

### Controller

```
[ApiController]
[Route("api/uniform-upload")]
[Authorize(Policy = "AdminOnly")]
```

| Verb | Path | Returns |
|------|------|---------|
| GET | `template` | `FileContentResult` (.xlsx) |
| POST | `upload` | `UniformUploadResultDto` |

Both resolve `jobId` via `User.GetJobIdFromRegistrationAsync(_jobLookupService)`.

---

## 6. Frontend Detail

### Component: `uniform-upload.component.ts`

**Signals**:
- `isDownloading = signal(false)` — template download in progress
- `isUploading = signal(false)` — file upload in progress
- `uploadResult = signal<UniformUploadResultDto | null>(null)` — result after upload
- `selectedFile = signal<File | null>(null)` — file chosen but not yet uploaded
- `errorMessage = signal('')` — general error

**UX Flow**:
1. **Card 1 — Download Template**: Button to download `.xlsx`. Shows spinner while generating.
2. **Card 2 — Upload**: Drag-drop zone (similar pattern to image-upload) accepting `.xlsx` only. Shows file name once selected. "Upload" button to submit.
3. **Card 3 — Results** (shown after upload): Summary counts + error table if any rows failed.

**HTTP calls** (inline, no separate service file — only 2 endpoints):
- `GET /api/uniform-upload/template` → blob download
- `POST /api/uniform-upload/upload` → FormData with file → JSON result

### Styling

- CSS variables only (all 8 palettes)
- Reuse `.drop-zone` pattern from image-upload
- Bootstrap cards, tables for error report
- Design system spacing tokens

---

## 7. Verification

1. **Backend build**: `dotnet build` — 0 errors
2. **Download template**: Hit `GET /api/uniform-upload/template` → opens valid .xlsx in Excel with player data
3. **Upload unchanged template**: Should return `UpdatedCount: 0, SkippedCount: N` (no changes detected)
4. **Upload with edits**: Modify UniformNo/DayGroup for a few rows → `UpdatedCount` matches edited rows
5. **Upload with bad data**: Include invalid GUID row, row for wrong job → reported in Errors array
6. **Frontend**: Navigate to `/:jobPath/admin/uniform-upload`, download template, upload file, verify result display
7. **Regenerate API models**: Run `.\scripts\2-Regenerate-API-Models.ps1` after backend is done, before frontend coding
