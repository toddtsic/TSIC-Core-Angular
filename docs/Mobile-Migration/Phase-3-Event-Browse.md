# Phase 3 — Event Browse Migration Script

> **Applies to**: TSIC-Events Ionic app only
> **Backend endpoints**: 4 new endpoints on `EventBrowseController`

---

## 1. List Active Events (Browse)

**URL Change:**
```
OLD: GET api/tsic_events_2025/Customers/GetCustomerJobs
NEW: GET api/events
```

**Response Interface:**
```typescript
// OLD
interface ActiveJob {
  JobId: string;
  JobName: string;
  JobLogoUrl: string;      // full URL
  City: string;
  State: string;
  FirstGameDay?: string;   // DateTime
  LastGameDay?: string;    // DateTime
}

// NEW
interface EventListingDto {
  jobId: string;           // camelCase
  jobName: string;         // uses MobileJobName if set, falls back to JobName
  jobLogoUrl?: string;     // filename only — prepend statics base URL
  city?: string;
  state?: string;
  sportName?: string;      // NEW — e.g., "Soccer", "Lacrosse"
  firstGameDay?: string;
  lastGameDay?: string;
}
```

**Service Method:**
```typescript
// OLD
getCustomerJobs(): Observable<ActiveJob[]> {
  return this.http.get<ActiveJob[]>(
    `${this.baseUrl}/api/tsic_events_2025/Customers/GetCustomerJobs`
  );
}

// NEW
getActiveEvents(): Observable<EventListingDto[]> {
  return this.http.get<EventListingDto[]>(
    `${this.baseUrl}/api/events`
  );
}
```

**Migration Note:** `jobLogoUrl` is now just the filename (e.g., `"abc123_logoheader.png"`). Prepend your statics base URL: `https://statics.teamsportsinfo.com/BannerFiles/${dto.jobLogoUrl}`.

---

## 2. Event Alerts

**URL Change:**
```
OLD: GET api/tsic_events_2025/Alerts/GetJobMobileAlerts/{jobId}
NEW: GET api/events/{jobId}/alerts
```

**Response Interface:**
```typescript
// OLD
interface AlertViewModel {
  SentWhen: string;    // DateTime
  PushText: string;
}

// NEW
interface EventAlertDto {
  sentWhen: string;    // camelCase
  pushText: string;
}
```

---

## 3. Event Documents/Links

**URL Change:**
```
OLD: GET api/tsic_events_2025/JobDocs/Get/{jobId}
NEW: GET api/events/{jobId}/docs
```

**Response Interface:**
```typescript
// OLD
interface TeamLink_ViewModel {
  DocId: string;
  TeamId?: string;
  JobId?: string;
  Label: string;
  CreateDate: string;
  UserId: string;
  User: string;
  DocUrl: string;
}

// NEW
interface EventDocDto {
  docId: string;       // camelCase
  jobId?: string;
  label: string;
  docUrl: string;
  user?: string;       // "FirstName LastName"
  createDate: string;
}
```

**Changes:** `TeamId` and `UserId` removed from response (not needed for display).

---

## 4. Game Clock Config

**URL Change:**
```
OLD: GET api/tsic_events_2025/GameClock/GetGameClockData/{jobId}
NEW: GET api/events/{jobId}/game-clock
```

**Response Interface:**
```typescript
// OLD — wrapped
interface GameClockDataDto {
  Intervals: GameClockParamsDto;
}

// NEW — flat (no wrapper)
interface GameClockConfigDto {
  utcoffsetHours?: number;
  halfMinutes: number;
  halfTimeMinutes: number;
  quarterMinutes?: number;
  quarterTimeMinutes?: number;
  transitionMinutes: number;
  playoffMinutes: number;
  playoffHalfMinutes?: number;
  playoffHalfTimeMinutes?: number;
}
```

**Changes:** Wrapper object removed — response is the config object directly. Access `response.halfMinutes` instead of `response.Intervals.HalfMinutes`.

---

## Summary

| # | Old URL | New URL |
|---|---------|---------|
| 1 | `GET Customers/GetCustomerJobs` | `GET api/events` |
| 2 | `GET Alerts/GetJobMobileAlerts/{jobId}` | `GET api/events/{jobId}/alerts` |
| 3 | `GET JobDocs/Get/{jobId}` | `GET api/events/{jobId}/docs` |
| 4 | `GET GameClock/GetGameClockData/{jobId}` | `GET api/events/{jobId}/game-clock` |

All endpoints are `[AllowAnonymous]` — no authentication required.
