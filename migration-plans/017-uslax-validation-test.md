# 017 — US Lax Validation Test

> **Status**: Complete
> **Date**: 2026-02-22
> **Legacy reference**: `reference/TSIC-Unify-2024/TSIC-Unify/Controllers/ValidationRemoteTestController.cs`
> **Legacy route**: `ValidationRemoteTest/Index`

---

## 1. Problem Statement

Directors need a quick way to test a USA Lacrosse membership number against the live USALax API — verify a number is active, check the member's name/DOB, and compare the expiry against the job's valid-through date. The legacy page dumps raw JSON in a `<pre>` block, which works but is hard to scan.

**Goal**: Replace with a clean Angular component that calls the existing `GET /api/validation/uslax` endpoint and presents results in a structured, readable card — same functionality, better UX.

---

## 2. Scope

### In Scope
- Single input field for membership number
- Call existing `GET /api/validation/uslax?number={n}` proxy endpoint
- Display structured result: status, name, DOB, gender, expiry, involvement, email, state/zip
- Show job's `usLaxNumberValidThroughDate` alongside membership expiry for comparison
- Valid/invalid/error status indicator
- Help links (USALax lookup + membership pages)
- Director-and-above access only (admin route guard)

### Out of Scope
- Modifying the `UsLaxService` or `ValidationController` backend (already complete)
- Batch validation of multiple numbers
- Integration with player registration flow (already handled by wizards)

---

## 3. Existing Backend (No Changes Needed)

| Layer | File | Notes |
|---|---|---|
| Service interface | `Services/Shared/UsLax/IUsLaxService.cs` | `GetMemberRawJsonAsync(membershipId)` |
| Service impl | `Services/Shared/UsLax/UsLaxService.cs` | OAuth2 token caching, auto-refresh, MemberPing proxy |
| Controller | `Controllers/ValidationController.cs` | `GET /api/validation/uslax?number=` — returns raw USALax JSON |
| Config | `UsLaxSettings.cs` | Credentials from appsettings or env vars |

### USALax API Response Shape

```json
{
  "status_code": 200,
  "output": {
    "membership_id": "000012345678",
    "mem_status": "Active",
    "exp_date": "08/31/2026",
    "firstname": "Jane",
    "lastname": "Doe",
    "birthdate": "03/15/2010",
    "gender": "Female",
    "age_verified": "true",
    "email": "jane@example.com",
    "postalcode": "21201",
    "state": "MD",
    "involvement": ["Player"]
  }
}
```

---

## 4. Frontend

### Component
`views/admin/uslax-test/uslax-test.component.ts` — standalone, OnPush, signals

### Design Improvements Over Legacy
- Structured key/value display instead of raw JSON dump
- Color-coded status badge (Active = green, other = red)
- Expiry date comparison: warning if membership expires before job date
- Involvement array rendered as badges
- Help links in a collapsible footer section

### Routes

| Route | Purpose |
|---|---|
| `/:jobPath/admin/uslax-test` | Primary route |
| `/:jobPath/validationremotetest/index` | Legacy compatibility |

---

## 5. Files

### Created (3)
| File | Layer |
|---|---|
| `views/admin/uslax-test/uslax-test.component.ts` | Component |
| `views/admin/uslax-test/uslax-test.component.html` | Template |
| `views/admin/uslax-test/uslax-test.component.scss` | Styles |

### Modified (1)
| File | Change |
|---|---|
| `app.routes.ts` | Add `admin/uslax-test` route + legacy redirect |

### No Backend Changes
Backend is fully implemented — `UsLaxService`, `ValidationController`, and `JobService` metadata all exist.
