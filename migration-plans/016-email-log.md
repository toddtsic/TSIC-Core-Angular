# 016 — Email Log

> **Status**: Complete
> **Date**: 2026-02-22
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/Admin/JobEmailsController.cs`
> **Legacy route**: `JobEmails/Index`

---

## 1. Problem Statement

The legacy JobEmails/Index page shows a read-only audit log of all emails sent from a job. It uses jqGrid + CKEditor for display, with direct `SqlDbContext` access in the controller. No REST API exists for the Angular frontend.

**Goal**: Replace with a clean Angular + Web API implementation — repository pattern, DTOs, modern master/detail UI.

---

## 2. Scope

### In Scope
- List all sent emails for current job (date, sender, recipient count, subject)
- Click-to-view detail: full recipient list + HTML email body
- Sortable table columns
- Admin-only access (Phase2 auth)

### Out of Scope
- Email sending/composing (handled by other services)
- Email failure tracking (`EmailFailures` entity — future feature)
- Editing or deleting email records (read-only audit log)

---

## 3. Database

**Table**: `Jobs.emailLogs`

| Column | Type | Notes |
|---|---|---|
| emailID | int (PK) | Not identity — manually assigned |
| JobID | uniqueidentifier (FK) | References Jobs.Jobs |
| sendTS | datetime | Default: getdate() |
| sendFrom | nvarchar | Sender email address |
| sendTo | nvarchar | Semicolon-delimited recipient list |
| subject | nvarchar | Email subject line |
| msg | nvarchar | HTML email body |
| count | int | Recipient count |
| senderUserID | nvarchar(450) | FK to AspNetUsers |

Entity already scaffolded: `TSIC.Domain.Entities.EmailLogs`

---

## 4. Backend

### DTOs (`TSIC.Contracts/Dtos/EmailLogDtos.cs`)

```csharp
public record EmailLogSummaryDto
{
    public required int EmailId { get; init; }
    public required DateTime SendTs { get; init; }
    public string? SendFrom { get; init; }
    public int? Count { get; init; }
    public string? Subject { get; init; }
}

public record EmailLogDetailDto
{
    public required int EmailId { get; init; }
    public string? SendTo { get; init; }
    public string? Msg { get; init; }
}
```

### Repository

- **Interface**: `TSIC.Contracts/Repositories/IEmailLogRepository.cs`
- **Implementation**: `TSIC.Infrastructure/Repositories/EmailLogRepository.cs`
- Methods: `GetByJobIdAsync`, `GetDetailAsync` (scoped to jobId for security)

### Controller (`TSIC.API/Controllers/EmailLogController.cs`)

| Method | Route | Auth | Returns |
|---|---|---|---|
| GET | `/api/email-log` | AdminOnly | `List<EmailLogSummaryDto>` |
| GET | `/api/email-log/{emailId}` | AdminOnly | `EmailLogDetailDto` |

No service layer — pure read-only data, controller calls repository directly.

---

## 5. Frontend

### Service
`views/admin/email-log/services/email-log.service.ts`

### Component
`views/admin/email-log/email-log.component.ts` — standalone, OnPush, signals

### UI Design
- **Master/detail layout**: table (60%) + detail panel (40%) on desktop; stacked on mobile
- **Table**: sortable columns — Sent, Sender, Recipients (badge), Subject
- **Detail panel**: recipient badges + HTML body rendered via `[innerHTML]`
- Default sort: newest first
- Click row to select → lazy-loads detail

### Routes
- `/:jobPath/admin/email-log` (child of admin parent — inherits requireAdmin guard)
- `/:jobPath/jobemails/index` (legacy compatibility)

---

## 6. Files

### Created (8)
| File | Layer |
|---|---|
| `TSIC.Contracts/Dtos/EmailLogDtos.cs` | DTO |
| `TSIC.Contracts/Repositories/IEmailLogRepository.cs` | Interface |
| `TSIC.Infrastructure/Repositories/EmailLogRepository.cs` | Repository |
| `TSIC.API/Controllers/EmailLogController.cs` | Controller |
| `views/admin/email-log/services/email-log.service.ts` | Frontend service |
| `views/admin/email-log/email-log.component.ts` | Component |
| `views/admin/email-log/email-log.component.html` | Template |
| `views/admin/email-log/email-log.component.scss` | Styles |

### Modified (2)
| File | Change |
|---|---|
| `TSIC.API/Program.cs` | Added `IEmailLogRepository` DI registration |
| `app.routes.ts` | Added `configure/email-log` + `jobemails/index` routes |
