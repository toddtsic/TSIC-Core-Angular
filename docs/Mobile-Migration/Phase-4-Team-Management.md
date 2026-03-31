# Phase 4 — Team Management Migration Script

> **Applies to**: Both TSIC-Events (roster only) and TSIC-Teams (roster, links, pushes)
> **Backend endpoints**: 6 new endpoints on `TeamManagementController`

---

## 1. Team Roster

**URL Change:**
```
OLD (Events): GET api/tsic_events_2025/JobSchedule/GetTeamRoster/{teamId}/{sortByUniformNo}
OLD (Teams):  GET api/tsic_teams_2025/TeamRoster/GetTeamRosterByTeamId/{teamId}
OLD (Teams):  GET api/tsic_teams_2025/TeamRoster/GetTeamRosterByRegistrationId/{registrationId}
NEW:          GET api/teams/{teamId}/roster
```

**Response Interface:**
```typescript
interface TeamRosterDetailDto {
  staff: TeamRosterStaffDto[];
  players: TeamRosterPlayerDto[];
}

interface TeamRosterStaffDto {
  firstName: string;
  lastName: string;
  cellphone?: string;
  email?: string;
  headshotUrl?: string;
  userName?: string;
  userId?: string;
}

interface TeamRosterPlayerDto {
  firstName: string;
  lastName: string;
  roleName?: string;
  cellphone?: string;
  email?: string;
  headshotUrl?: string;
  mom?: string;            // "FirstName LastName"
  momEmail?: string;
  momCellphone?: string;
  dad?: string;            // "FirstName LastName"
  dadEmail?: string;
  dadCellphone?: string;
  uniformNumber?: string;
  city?: string;
  school?: string;
  userName?: string;
  userId?: string;
  countPresent: number;
  countNotPresent: number;
}
```

**Migration Note for Teams app:** The `GetTeamRosterByRegistrationId` endpoint no longer exists. Resolve `teamId` from the registration data (available from the login response) and call `/roster` directly.

---

## 2. Team Links

**URL Changes:**
```
OLD: POST api/tsic_teams_2025/TeamLinks/GetTeamLinks  (body: { TeamId, PagingParams })
NEW: GET  api/teams/{teamId}/links

OLD: POST api/tsic_teams_2025/TeamLinks/AddTeamLink
NEW: POST api/teams/{teamId}/links

OLD: DELETE api/tsic_teams_2025/TeamLinks/DeleteTeamLink/{docId}
NEW: DELETE api/teams/{teamId}/links/{docId}
```

**Get Links — no request body needed** (was POST with body, now GET):
```typescript
// OLD
getTeamLinks(teamId: string, page: number, rows: number): Observable<TeamLink_ViewModel[]> {
  return this.http.post<TeamLink_ViewModel[]>(
    `${this.baseUrl}/api/tsic_teams_2025/TeamLinks/GetTeamLinks`,
    { TeamId: teamId, PagingParams: { PageNumber: page, RowsPerPage: rows } }
  );
}

// NEW — no pagination (returns all, typically small lists)
getTeamLinks(teamId: string): Observable<TeamLinkDto[]> {
  return this.http.get<TeamLinkDto[]>(
    `${this.baseUrl}/api/teams/${teamId}/links`
  );
}
```

**Add Link:**
```typescript
// OLD
addTeamLink(teamId: string, label: string, docUrl: string, addAllTeams: boolean): Observable<TeamLink_ViewModel> {
  return this.http.post<TeamLink_ViewModel>(
    `${this.baseUrl}/api/tsic_teams_2025/TeamLinks/AddTeamLink`,
    { TeamId: teamId, Label: label, DocUrl: docUrl, BAddAllTeams: addAllTeams }
  );
}

// NEW
addTeamLink(teamId: string, label: string, docUrl: string, addAllTeams: boolean): Observable<TeamLinkDto> {
  return this.http.post<TeamLinkDto>(
    `${this.baseUrl}/api/teams/${teamId}/links`,
    { label, docUrl, addAllTeams }
  );
}
```

---

## 3. Team Pushes

**URL Changes:**
```
OLD: POST api/tsic_teams_2025/TeamPushes/GetTeamPushes  (body: { TeamId, PagingParams })
NEW: GET  api/teams/{teamId}/pushes

OLD: POST api/tsic_teams_2025/TeamPushes/AddTeamPush
NEW: POST api/teams/{teamId}/pushes
```

**Get Pushes:**
```typescript
// NEW
getTeamPushes(teamId: string): Observable<TeamPushDto[]> {
  return this.http.get<TeamPushDto[]>(
    `${this.baseUrl}/api/teams/${teamId}/pushes`
  );
}
```

**Send Push:**
```typescript
// OLD
sendTeamPush(teamId: string, pushText: string, addAllTeams: boolean): Observable<TeamPush_ViewModel> {
  return this.http.post<TeamPush_ViewModel>(
    `${this.baseUrl}/api/tsic_teams_2025/TeamPushes/AddTeamPush`,
    { TeamId: teamId, PushText: pushText, BAddAllTeams: addAllTeams }
  );
}

// NEW
sendTeamPush(teamId: string, pushText: string, addAllTeams: boolean): Observable<TeamPushDto> {
  return this.http.post<TeamPushDto>(
    `${this.baseUrl}/api/teams/${teamId}/pushes`,
    { pushText, addAllTeams }
  );
}
```

---

## Summary

| # | Old URL | New URL | App |
|---|---------|---------|-----|
| 1 | `GET JobSchedule/GetTeamRoster/{teamId}` | `GET api/teams/{teamId}/roster` | Events |
| 2 | `GET TeamRoster/GetTeamRosterByTeamId/{teamId}` | `GET api/teams/{teamId}/roster` | Teams |
| 3 | `POST TeamLinks/GetTeamLinks` | `GET api/teams/{teamId}/links` | Teams |
| 4 | `POST TeamLinks/AddTeamLink` | `POST api/teams/{teamId}/links` | Teams |
| 5 | `DELETE TeamLinks/{docId}` | `DELETE api/teams/{teamId}/links/{docId}` | Teams |
| 6 | `POST TeamPushes/GetTeamPushes` | `GET api/teams/{teamId}/pushes` | Teams |
| 7 | `POST TeamPushes/AddTeamPush` | `POST api/teams/{teamId}/pushes` | Teams |

All endpoints require `[Authorize]` — JWT token must be in the Authorization header.

Key patterns:
- RESTful routes with `teamId` in the path (no body for reads)
- `addAllTeams` replaces `BAddAllTeams` (camelCase, no `B` prefix)
- No pagination — lists are typically small enough to return all at once
