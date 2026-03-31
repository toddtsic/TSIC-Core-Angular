# Phase 5 — Team Attendance Migration Script

> **Applies to**: TSIC-Teams Ionic app
> **Backend endpoints**: 7 new endpoints on `TeamAttendanceController`

---

## 1. Get Attendance Events

```
OLD: POST api/tsic_teams_2025/TeamAttendance/GetTeamAttendanceEvents  (body: { TeamId, PagingParams })
NEW: GET  api/teams/{teamId}/attendance/events
```

```typescript
// NEW — no body, no pagination
interface AttendanceEventDto {
  eventId: number;
  teamId: string;
  comment?: string;
  eventTypeId: number;
  eventType?: string;       // e.g., "Practice", "Game"
  eventDate: string;        // ISO DateTime
  eventLocation?: string;
  present: number;          // count of players marked present
  notPresent: number;       // count of players marked not present
  unknown: number;          // count of unresponded
  creatorUserId?: string;
}
```

---

## 2. Create Attendance Event

```
OLD: POST api/tsic_teams_2025/TeamAttendance/AddAttendanceEvent
NEW: POST api/teams/{teamId}/attendance/events
```

```typescript
interface CreateAttendanceEventRequest {
  comment: string;
  eventTypeId: number;
  eventDate: string;        // ISO DateTime
  eventLocation: string;
}

// Response: AttendanceEventDto (same as above)
```

---

## 3. Delete Attendance Event

```
OLD: DELETE api/tsic_teams_2025/TeamAttendance/DeleteAttendanceEvent/{attendanceEventId}
NEW: DELETE api/teams/{teamId}/attendance/events/{eventId}
```

---

## 4. Get Event Roster (player attendance for a specific event)

```
OLD: GET api/tsic_teams_2025/TeamAttendance/GetTeamRosterAttendanceByEventId/{eventId}
NEW: GET api/teams/{teamId}/attendance/events/{eventId}/roster
```

```typescript
interface AttendanceRosterDto {
  attendanceId: number;
  playerId: string;
  playerFirstName?: string;
  playerLastName?: string;
  present: boolean;
  uniformNo?: string;
  headshotUrl?: string;
}
```

---

## 5. Update Player RSVP

```
OLD: POST api/tsic_teams_2025/TeamAttendance/UpdatePlayerRsvpStatus  (body: { EventId, PlayerId, Present })
NEW: POST api/teams/{teamId}/attendance/events/{eventId}/rsvp
```

```typescript
interface UpdateRsvpRequest {
  playerId: string;
  present: boolean;
}
```

**Change:** `eventId` moves from request body to URL path.

---

## 6. Get Player Attendance History

```
OLD: POST api/tsic_teams_2025/TeamAttendance/GetTeamRosterMemberAttendanceHistory  (body: { TeamId, PlayerId })
NEW: GET  api/teams/{teamId}/attendance/player/{userId}/history
```

```typescript
interface AttendanceHistoryDto {
  eventDate: string;
  eventType?: string;
  present: boolean;
}
```

---

## 7. Get Event Type Options

```
OLD: GET api/tsic_teams_2025/TeamAttendance/GetTeamAttendanceEventTypeOptions
NEW: GET api/teams/attendance/event-types
```

```typescript
interface AttendanceEventTypeDto {
  id: number;
  attendanceType: string;   // e.g., "Practice", "Game", "Meeting"
}
```

---

## Summary

| # | Old URL | New URL |
|---|---------|---------|
| 1 | `POST TeamAttendance/GetTeamAttendanceEvents` | `GET api/teams/{teamId}/attendance/events` |
| 2 | `POST TeamAttendance/AddAttendanceEvent` | `POST api/teams/{teamId}/attendance/events` |
| 3 | `DELETE TeamAttendance/{eventId}` | `DELETE api/teams/{teamId}/attendance/events/{eventId}` |
| 4 | `GET TeamAttendance/GetTeamRosterAttendanceByEventId/{eventId}` | `GET api/teams/{teamId}/attendance/events/{eventId}/roster` |
| 5 | `POST TeamAttendance/UpdatePlayerRsvpStatus` | `POST api/teams/{teamId}/attendance/events/{eventId}/rsvp` |
| 6 | `POST TeamAttendance/GetTeamRosterMemberAttendanceHistory` | `GET api/teams/{teamId}/attendance/player/{userId}/history` |
| 7 | `GET TeamAttendance/GetTeamAttendanceEventTypeOptions` | `GET api/teams/attendance/event-types` |

Key patterns:
- All reads changed from POST-with-body to GET (RESTful)
- `teamId` and `eventId` in URL paths, not request body
- No pagination — event lists are team-scoped (typically small)
- All endpoints require `[Authorize]`
