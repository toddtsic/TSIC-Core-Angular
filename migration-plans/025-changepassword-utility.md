# Migration Plan: ChangePassword/Index → Change Password Utility

## Context

The legacy TSIC-Unify-2024 project has a `ChangePassword/Index` page — a **SuperUser-only admin tool**
that bundles user account management into one screen. Despite the name, it's a full-featured utility:

1. **Cross-job user search** — by name, email, phone, username, role, customer, job
2. **Admin password reset** — for player accounts and family accounts
3. **Email editing** — user email, family email, mom/dad emails
4. **Username merge** — reassign registrations from duplicate user accounts

**Not to be confused with**: The self-service "forgot password" flow (email-based reset via
`forgot-password` / `reset-password` endpoints in AuthController — separate feature).

---

## 1. Legacy Pain Points

- **No result cap** — queries could return thousands of rows, freezing the browser
- **Tightly coupled MVC** — search, reset, merge all in one massive controller action
- **No family/user separation** — password reset modals for user vs family were confusingly similar
- **Username merge had no confirmation** — irreversible operation with no warning
- **No role-based filter visibility** — family username field shown even for non-Player searches

## 2. Modern Vision

A clean admin utility page with:
- **Role-filtered search** — dropdown defaults to Player, filters adapt (family fields shown only for Player)
- **Expandable row detail panel** — click a result row to expand inline actions
- **Inline email editing** — edit and save emails directly in the expanded panel
- **Password reset modal** — `TsicDialogComponent` with visible plaintext (admin-set, not self-service)
- **Merge modal with warning** — loads candidates automatically, shows irreversibility warning
- **200-result cap** — with "narrow your search" warning when hit

## 3. Architecture

```
ChangePasswordController [SuperUserOnly]
  └─ IChangePasswordService
       ├─ IChangePasswordRepository  (cross-job search, email updates, merge ops)
       └─ UserManager<ApplicationUser> (admin password reset via Identity)
```

Cross-job tool — no `IJobLookupService` or jobId resolution needed. SuperUser is exempt.

## 4. Files Created

| Layer | File |
|-------|------|
| DTOs | `TSIC.Contracts/Dtos/ChangePassword/ChangePasswordDtos.cs` |
| Repo interface | `TSIC.Contracts/Repositories/IChangePasswordRepository.cs` |
| Repo impl | `TSIC.Infrastructure/Repositories/ChangePasswordRepository.cs` |
| Service interface | `TSIC.Contracts/Services/IChangePasswordService.cs` |
| Service impl | `TSIC.API/Services/Admin/ChangePasswordService.cs` |
| Controller | `TSIC.API/Controllers/ChangePasswordController.cs` |
| FE service | `views/admin/change-password/services/change-password.service.ts` |
| FE component TS | `views/admin/change-password/change-password.component.ts` |
| FE component HTML | `views/admin/change-password/change-password.component.html` |
| FE component SCSS | `views/admin/change-password/change-password.component.scss` |

| Modified | Change |
|----------|--------|
| `TSIC.API/Program.cs` | 2 DI registrations |
| `app.routes.ts` | 1 admin route (`admin/change-password`, requireSuperUser) |
| `NavEditorService.cs` | 1 legacy route mapping (`search/changepassword` → `admin/change-password`) |

## 5. API Endpoints

```
[Authorize(Policy = "SuperUserOnly")]
[Route("api/change-password")]
```

| Verb | Path | Purpose |
|------|------|---------|
| GET | `role-options` | Dropdown options |
| POST | `search` | Search registrations |
| POST | `{regId}/reset-password` | Reset user's password |
| POST | `{regId}/reset-family-password` | Reset family account password |
| PUT | `{regId}/user-email` | Update user email |
| PUT | `{regId}/family-emails` | Update family emails |
| GET | `{regId}/merge-candidates` | Get user merge candidates |
| GET | `{regId}/family-merge-candidates` | Get family merge candidates |
| POST | `{regId}/merge-username` | Merge user registrations |
| POST | `{regId}/merge-family-username` | Merge family registrations |

## 6. DTOs

| DTO | Purpose |
|-----|---------|
| `ChangePasswordSearchRequest` | Filters: RoleId (required), CustomerName?, JobName?, LastName?, FirstName?, Email?, Phone?, UserName?, FamilyUserName? |
| `ChangePasswordSearchResultDto` | Per-registration row with user + family fields |
| `ChangePasswordRoleOptionDto` | RoleId + RoleName (for dropdown) |
| `AdminResetPasswordRequest` | UserName + NewPassword |
| `UpdateUserEmailRequest` | Email |
| `UpdateFamilyEmailsRequest` | FamilyEmail?, MomEmail?, DadEmail? |
| `MergeCandidateDto` | UserName + UserId |
| `MergeUsernameRequest` | TargetUserName |

## 7. Key Design Decisions

1. **NormalizedEmail sync** — when updating email directly, always set `NormalizedEmail = email.ToUpperInvariant()` (ASP.NET Identity requirement)
2. **Max 200 results** — legacy had no cap. New implementation caps at 200 with "narrow your search" message
3. **Password as plain text input** — admin-reset, not self-service. SuperUser needs to see what they're setting
4. **Merge confirmation** — irreversible operation. Danger-styled warning in modal
5. **Role-adaptive UI** — family fields (FamilyUserName, FamilyEmail, mom/dad) shown only when Player role selected
6. **Password reset uses Identity** — `UserManager.GeneratePasswordResetTokenAsync()` + `ResetPasswordAsync()` stays in service (not repo) because it uses Identity

## 8. Verification Checklist

- [ ] `dotnet build` — 0 errors
- [ ] No `SqlDbContext` in controller or service files
- [ ] Swagger: POST `api/change-password/search` with Player roleId + lastName — returns results
- [ ] Reset a test user's password, then login with new password
- [ ] Update email, verify NormalizedEmail also updated in DB
- [ ] Merge: find duplicate users, merge, verify registrations reassigned
- [ ] Non-SuperUser gets 403 on all endpoints
- [ ] UI: test all 8 palettes — no hardcoded colors

---

**Status**: COMPLETE
**Legacy path**: `ChangePassword/Index`
**New route**: `/:jobPath/admin/change-password`
