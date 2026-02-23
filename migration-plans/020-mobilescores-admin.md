# 020 — Mobile Scorers Admin

> **Status**: Design Spec — Ready for Review
> **Date**: 2026-02-23
> **Keyword**: `CWCC Implement MOBILE-SCORERS-ADMIN`
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Mobile/MobileScorersController.cs`
> **Legacy route**: `MobileScorers/Index`

---

## 1. Problem Statement

The legacy MobileScorers/Index page lets directors create and manage scorer accounts for a mobile app that allows on-field score entry. Scorers are simple user accounts with a convention of password = username, registered with the `Scorer` role under the current job.

The legacy implementation suffers from:

| Issue | Detail |
|---|---|
| **Repository violation** | Controller uses `SqlDbContext` directly — zero separation |
| **No DTO layer** | Uses anonymous types returned as JSON |
| **No API surface** | MVC-only — no REST endpoints for Angular |
| **Hardcoded defaults** | Gender = "F", DOB = 1/1/1980 as throwaway values for required fields |
| **No delete** | Legacy can't remove scorer accounts |
| **Inconsistent edits** | Edit only updates `BActive`, `Email`, `Cellphone` — username/name locked after creation |

**Goal**: Clean Angular + Web API implementation. Simple CRUD table for scorer accounts with inline add/edit.

---

## 2. Scope

### In Scope
- List scorers for the current job (name, username, email, cellphone, active status)
- Add new scorer (creates ASP.NET Identity user + Registration with Scorer role)
- Edit scorer (active toggle, email, cellphone)
- Delete scorer registration (deactivate + option to remove)
- Director+ access (AdminOnly policy)

### Out of Scope
- The mobile scoring app itself (separate codebase / native app)
- Score entry or game data (handled by mobile app via existing Schedule.T1Score/T2Score fields)
- Password reset for scorers (directors just recreate — password = username convention)
- The `MobileUserData` table (generic mobile profile store, not scorer-specific)

---

## 3. Database Schema

All tables already exist. **No migrations needed.**

| Table | Schema | Relationship | Notes |
|---|---|---|---|
| `AspNetUsers` | `dbo` | User identity | Scorer user account (UserName, FirstName, LastName, Email, Cellphone) |
| `Registrations` | `dbo` | Scorer-to-job link | `RoleId = Scorer`, `JobId = current job` |

### Relevant Entity Fields

**AspNetUsers** (for scorer):

| Column | Editable | Notes |
|---|---|---|
| `UserName` | Create only | Also used as password (convention) |
| `FirstName` | Create only | Scorer's first name |
| `LastName` | Create only | Scorer's last name |
| `Email` | Yes | Contact email |
| `Cellphone` | Yes | Contact phone |
| `Gender` | No | Set to placeholder "U" on create |
| `Dob` | No | Set to placeholder 1980-01-01 on create |

**Registrations** (for scorer):

| Column | Value | Notes |
|---|---|---|
| `RegistrationId` | `Guid.NewGuid()` | PK |
| `UserId` | FK to AspNetUsers.Id | Link to scorer user |
| `JobId` | Current job | From auth context |
| `RoleId` | `RoleConstants.Scorer` | Fixed to Scorer role |
| `BActive` | `true` on create | Toggleable via edit |
| `AssignedTeamId` | `null` | Scorers aren't on a team |
| `FamilyUserId` | `null` | Scorers don't have family accounts |
| `RegistrationFormName` | `null` | No registration form |

---

## 4. Design Decisions

### 4a. UI Pattern — Table + Modal

| Option | Pro | Con |
|---|---|---|
| **Table with Add button + modal** (chosen) | Consistent with other admin pages (015, 014) | Extra click |
| Inline add row (legacy style) | Fast data entry for batch creation | Breaks consistency with other admin pages |

**Decision**: Data table + modal for add/edit. Same pattern as CustomerConfigure (015) and CustomerGroups (014). Directors creating 5-10 scorers per job is fine with a modal workflow.

### 4b. Password Convention

Legacy sets password = username via `UserManager.CreateAsync(user, password: Username)`. This is intentional — scorers are temporary accounts used only for the mobile scoring app.

**Decision**: Preserve the password = username convention. No password field in the UI. Display a note: "Password is set to the username." Show the username in the table so directors can quickly tell scorers their credentials. This is acceptable because these are non-privileged, job-scoped temporary accounts.

### 4c. Delete vs Deactivate

Legacy has no delete. Edit only toggles `BActive`.

**Decision**: Keep the active toggle as the primary action (matches legacy). Add a **hard delete** option (remove registration + optionally the user if they have no other registrations). The delete button shows only when the scorer is already inactive, reducing accidental deletions.

### 4d. Duplicate Username Prevention

`UserManager.CreateAsync` already enforces unique usernames. The service catches `IdentityError` and returns a meaningful error message.

### 4e. Name/Username Locked After Creation

Legacy allows editing only email, cellphone, and active status — not username or name. This is intentional because changing a username would break the password = username convention (scorer wouldn't know new password).

**Decision**: Preserve this. Edit modal shows username and name as readonly display fields. Only email, cellphone, and active status are editable.

---

## 5. Backend Architecture

### 5a. DTOs

**File**: `TSIC.Contracts/Dtos/Scoring/MobileScorerDtos.cs`

```csharp
// --- Response DTOs ---

public record MobileScorerDto
{
    public required Guid RegistrationId { get; init; }
    public required string Username { get; init; }
    public required string? FirstName { get; init; }
    public required string? LastName { get; init; }
    public required string? Email { get; init; }
    public required string? Cellphone { get; init; }
    public required bool BActive { get; init; }
}

// --- Request DTOs ---

public record CreateMobileScorerRequest
{
    public required string Username { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
}

public record UpdateMobileScorerRequest
{
    public string? Email { get; init; }
    public string? Cellphone { get; init; }
    public required bool BActive { get; init; }
}
```

### 5b. Repository Interface

**File**: `TSIC.Contracts/Repositories/IMobileScorerRepository.cs`

```csharp
public interface IMobileScorerRepository
{
    Task<List<MobileScorerDto>> GetScorersForJobAsync(Guid jobId, CancellationToken ct = default);
    Task<Registrations?> GetScorerRegistrationAsync(Guid registrationId, CancellationToken ct = default);
    Task<int> GetUserRegistrationCountAsync(string userId, CancellationToken ct = default);
    void AddRegistration(Registrations registration);
    void RemoveRegistration(Registrations registration);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
```

### 5c. Service Interface

**File**: `TSIC.Contracts/Services/IMobileScorerService.cs`

```csharp
public interface IMobileScorerService
{
    Task<List<MobileScorerDto>> GetScorersAsync(Guid jobId, CancellationToken ct);
    Task<MobileScorerDto> CreateScorerAsync(Guid jobId, CreateMobileScorerRequest request, string currentUserId, CancellationToken ct);
    Task UpdateScorerAsync(Guid registrationId, UpdateMobileScorerRequest request, string currentUserId, CancellationToken ct);
    Task DeleteScorerAsync(Guid registrationId, CancellationToken ct);
}
```

The **controller** resolves `jobId` and `userId` from JWT claims via `User.GetJobIdFromRegistrationAsync()` and `ClaimTypes.NameIdentifier`, then passes them as parameters. Services never touch `IHttpContextAccessor`.

### 5d. Service Implementation

**File**: `TSIC.API/Services/Admin/MobileScorerService.cs`

**Dependencies**: `IMobileScorerRepository`, `UserManager<ApplicationUser>`

Key logic:

- **Create**:
  1. Create `ApplicationUser` via `UserManager.CreateAsync(user, password: request.Username)`
  2. If Identity fails (e.g., duplicate username), throw `InvalidOperationException` with Identity error message
  3. Create `Registrations` record with `RoleId = RoleConstants.Scorer`, `JobId` (passed from controller), `BActive = true`
  4. Placeholder fields: `Gender = "U"`, `Dob = new DateTime(1980, 1, 1)`
  5. Set `LebUserId` (passed from controller) and `Modified = DateTime.UtcNow`
  6. Return mapped `MobileScorerDto`

- **Update**: Load registration (tracked), load user (tracked), update `BActive`, `Email`, `Cellphone`, set `LebUserId` and `Modified`

- **Delete**: Load registration. Remove registration. Check if user has other registrations — if zero remaining, delete the user account too via `UserManager.DeleteAsync`.

### 5e. Controller

**File**: `TSIC.API/Controllers/MobileScorerController.cs`

```
[ApiController]
[Route("api/mobile-scorers")]
[Authorize(Policy = "AdminOnly")]
```

| Verb | Route | Purpose |
|---|---|---|
| `GET` | `/` | List all scorers for the job |
| `POST` | `/` | Create new scorer (user + registration) |
| `PUT` | `/{registrationId:guid}` | Update scorer (active, email, cellphone) |
| `DELETE` | `/{registrationId:guid}` | Delete scorer registration (+ user if orphaned) |

**4 endpoints total.** No `{jobId}` in routes. Controller resolves `jobId` via `User.GetJobIdFromRegistrationAsync(_jobLookupService)` and `userId` via `User.FindFirst(ClaimTypes.NameIdentifier)`, then passes both to the service as parameters.

---

## 6. Frontend Architecture

### 6a. File Structure

```
src/app/views/admin/mobile-scorers/
├── mobile-scorers.component.ts       // Shell — table, state, CRUD orchestration
├── mobile-scorers.component.html     // Template
├── mobile-scorers.component.scss     // Styles
└── scorer-dialog/
    ├── scorer-dialog.component.ts    // Add/edit modal form
    ├── scorer-dialog.component.html
    └── scorer-dialog.component.scss
```

### 6b. State Management

```typescript
// mobile-scorers.component.ts
readonly scorers = signal<MobileScorerDto[]>([]);
readonly isLoading = signal(false);
readonly dialogOpen = signal(false);
readonly editingScorer = signal<MobileScorerDto | null>(null); // null = add mode
```

### 6c. Table Design

| Column | Source | Format |
|---|---|---|
| Active | `bActive` | Toggle switch or Yes/No badge |
| Username | `username` | Text (also = password) |
| First Name | `firstName` | Text |
| Last Name | `lastName` | Text |
| Email | `email` | Text |
| Cellphone | `cellphone` | Text |
| Actions | — | Edit / Delete icon buttons |

Sorted by `lastName`, `firstName` ascending. "No scorers yet — add one to get started" empty state.

Info banner at top: "Scorers log in to the mobile app with their username. The password is the same as the username."

### 6d. Add Modal

Uses `TsicDialogComponent` wrapper. Form fields:

| Field | Control | Validation |
|---|---|---|
| Username | `<input type="text">` | Required, min 6 chars |
| First Name | `<input type="text">` | Required |
| Last Name | `<input type="text">` | Required |
| Email | `<input type="email">` | Optional |
| Cellphone | `<input type="tel">` | Optional |

Note below username field: "This will also be the scorer's password."

### 6e. Edit Modal

Same `TsicDialogComponent`. Fields:

| Field | Control | Editable |
|---|---|---|
| Username | Readonly display | No |
| First Name | Readonly display | No |
| Last Name | Readonly display | No |
| Email | `<input type="email">` | Yes |
| Cellphone | `<input type="tel">` | Yes |
| Active | Toggle / checkbox | Yes |

### 6f. Delete Confirmation

`TsicDialogComponent` confirmation: "Remove scorer '{firstName} {lastName}' ({username})? This will delete their scorer access for this job." Cancel / Delete buttons.

### 6g. Routing

```typescript
{
    path: 'mobile-scorers',
    loadComponent: () => import('./views/admin/mobile-scorers/mobile-scorers.component')
        .then(m => m.MobileScorersComponent),
    data: { title: 'Mobile Scorers' }
}
```

Under the `admin` parent route (which uses `requireAdmin: true`).

### 6h. Styling

- Glass-surface card for the table container (matches widget editor, customer configure)
- CSS variables only (no hardcoded colors/spacing)
- 8px spacing grid (`var(--space-N)`)
- Responsive — table scrolls horizontally on narrow viewports
- Active toggle uses success/muted badge styling

---

## 7. Implementation Phases

### Phase 1 — Backend DTOs & Contracts

**Goal**: Define data shapes and interfaces.

| # | Task | File |
|---|---|---|
| 1 | Create DTOs | `Contracts/Dtos/Scoring/MobileScorerDtos.cs` |
| 2 | Create repository interface | `Contracts/Repositories/IMobileScorerRepository.cs` |
| 3 | Create service interface | `Contracts/Services/IMobileScorerService.cs` |

### Phase 2 — Backend Implementation

**Goal**: Repository, service, controller — full API working.

| # | Task | File |
|---|---|---|
| 4 | Create repository implementation | `Infrastructure/Repositories/MobileScorerRepository.cs` |
| 5 | Create service implementation | `API/Services/Admin/MobileScorerService.cs` |
| 6 | Create controller | `API/Controllers/MobileScorerController.cs` |
| 7 | Register DI in Program.cs | `API/Program.cs` |

**Deliverable**: All 4 endpoints callable via Swagger.

### Phase 3 — Frontend

**Goal**: Angular component — table + add/edit/delete.

| # | Task | File |
|---|---|---|
| 8 | Run `.\scripts\2-Regenerate-API-Models.ps1` | — |
| 9 | Create shell component + table | `views/admin/mobile-scorers/mobile-scorers.component.*` |
| 10 | Create scorer dialog component | `views/admin/mobile-scorers/scorer-dialog/scorer-dialog.component.*` |
| 11 | Add route in `app.routes.ts` under admin children | `app.routes.ts` |
| 12 | Wire up all CRUD operations | — |
| 13 | Loading states, error handling, empty state | — |
| 14 | Palette test across all 8 themes | — |

**Deliverable**: Full feature, ready for testing.

---

## 8. Error Handling

| Condition | Exception | HTTP Status |
|---|---|---|
| Job not found / mismatch | `KeyNotFoundException` | 404 |
| Registration not found | `KeyNotFoundException` | 404 |
| Duplicate username | `InvalidOperationException` (from Identity) | 409 Conflict |
| Username too short | `ArgumentException` | 400 |
| Empty first/last name | `ArgumentException` | 400 |
| Identity password rules fail | `InvalidOperationException` | 400 |

---

## 9. Constraints & Conventions

| Rule | Detail |
|---|---|
| Repository pattern | All data access via `IMobileScorerRepository` — zero `SqlDbContext` in service/controller |
| Sequential awaits | No `Task.WhenAll` — DbContext is not thread-safe |
| DTO pattern | `required` + `init` properties, object initializer syntax |
| No positional records | Use `{ get; init; }` style for all DTOs |
| Auto-generated models | Run `2-Regenerate-API-Models.ps1` after backend is complete |
| CSS variables only | No hardcoded colors, spacing, or shadows |
| Signals for state | `signal<T>()` for component state, observables for HTTP only |
| Relative routerLinks | Never absolute — preserve `:jobPath` prefix |
| `AsNoTracking()` | All read-only repository queries |
| Standalone components | No NgModules, `@if`/`@for` control flow, OnPush change detection |
| `TsicDialogComponent` | All modals wrap the gold-standard dialog |
| `UserManager` for Identity ops | Never manipulate AspNetUsers directly for password/create |

---

## 10. Risk & Mitigation

| Risk | Impact | Mitigation |
|---|---|---|
| ASP.NET Identity password policy rejects short usernames | Create fails if username < 6 chars or lacks special chars | Enforce min 6 chars in UI; consider relaxing Identity password rules for scorer accounts only if needed |
| Deleting user who has registrations in other jobs | Data loss | Check `GetUserRegistrationCountAsync` — only delete user if zero remaining registrations |
| Username collision across jobs | Same username used for scorers in different jobs | This is fine — same user, different registration per job. `UserManager` handles uniqueness at user level |
| Scorer can't log in to mobile app | Support issue | Display credentials clearly in the table so directors can verify |

---

## 11. Success Criteria

- [ ] All 4 API endpoints return correct data and status codes
- [ ] Scorer list shows all scorers for the current job
- [ ] Add creates both ASP.NET Identity user and Registration with Scorer role
- [ ] Password = username convention preserved
- [ ] Edit updates active status, email, cellphone only
- [ ] Delete removes registration (and user if orphaned)
- [ ] Duplicate username returns meaningful error message
- [ ] Zero `SqlDbContext` references outside the repository
- [ ] All DTOs use `required` + `init` pattern
- [ ] CSS uses only design system variables
- [ ] AdminOnly access enforced
- [ ] All 8 color palettes render correctly

---

## 12. Files Summary

### New Files (7)
1. `TSIC.Contracts/Dtos/Scoring/MobileScorerDtos.cs`
2. `TSIC.Contracts/Repositories/IMobileScorerRepository.cs`
3. `TSIC.Contracts/Services/IMobileScorerService.cs`
4. `TSIC.Infrastructure/Repositories/MobileScorerRepository.cs`
5. `TSIC.API/Services/Admin/MobileScorerService.cs`
6. `TSIC.API/Controllers/MobileScorerController.cs`
7. `src/frontend/tsic-app/src/app/views/admin/mobile-scorers/` (shell + dialog components)

### Modified Files (2)
1. `TSIC.API/Program.cs` — DI registration for repository + service
2. `src/frontend/tsic-app/src/app/app.routes.ts` — Add admin child route
