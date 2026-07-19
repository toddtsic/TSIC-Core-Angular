# 00 — Master Endpoint Map (TSIC-Teams → New Backend)

> **This is the authoritative handoff spec for the Mac-side rewire of TSIC-TEAMS-2025.**
> It maps every HTTP call the Teams app actually makes (**25 REST calls + 1 SignalR hub**,
> verified by code sweep 2026-07-17) to its final new-backend route. The Phase 4–7 docs remain
> the field-by-field shape reference, but **where a phase doc conflicts with this map, this map
> wins** (corrections at the bottom).
>
> Base URL (staging): `https://devapi.teamsportsinfo.com`
> SignalR hub (staging): `https://devapi.teamsportsinfo.com/hubs/chat`
> All new-backend JSON is **camelCase**. All routes below are relative to `/api`. Route matching
> is case-insensitive (`auth/...` == `Auth/...`).

---

## How this app differs from TSIC-Events (read first)

TSIC-Events is anonymous for almost everything. **TSIC-Teams is the opposite — every team call
is authenticated.** The flow the Mac side must implement:

1. `POST auth/quick-login` `{username, password}` → either an enriched token (single reg) or a
   minimal token + a `registrations` list of role groupings.
2. User picks a registration/role → `POST auth/select-registration` `{regId}` → **enriched JWT**
   (carries `sub`=userId, `username`, `regId`, `jobPath`, `role`, `jobLogo`).
3. Store `accessToken` + `refreshToken`; a bearer interceptor attaches the token to **all**
   `teams/*`, `files/*`, and `my-roster` calls, and to the SignalR connection via
   `accessTokenFactory`.

### ⚠️ The `teamId` problem — the single biggest blocker

Every team endpoint is `GET/POST api/teams/{teamId}/...`. The legacy app got `teamId` (plus
`headshotUrl`, `calendarId`, `bEnableMobileRsvp`, `bEnableMobileTeamChat`) straight off the
login response (`ITeam_User_RoleData`). **The new backend surfaces none of these.** Confirmed:

- `RegistrationDto` = `{ regId, displayText, jobLogo, jobPath? }` — **no `teamId`, no team flags**.
- Enriched JWT claims = `sub, username, regId, jobPath, role, jobLogo` — **no `teamId` claim**
  (`TokenService.GenerateEnrichedJwtToken`).

So today there is **no way for the Teams app to learn its own `teamId`** after login. This must
be resolved backend-side before the Mac rewire can call any `teams/{teamId}/...` route. This is
the Teams analogue of the `jobPath`-on-`EventListingDto` addition Events got on 2026-07-16.
See **Backend gaps → Gap 1** for the recommended fix (decision needed from Todd).

---

## The 25 calls

### Auth

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 1 | `POST login/login` `{username, password}` → `ITeam_User_RoleData_Grouped[]` | `POST auth/quick-login` | Body `{username, password, regId?}`. Response `{accessToken, refreshToken?, expiresIn?, requiresTosSignature, registrations?}`. `registrations` = `RegistrationRoleDto[]` (role groupings → `roleRegistrations: RegistrationDto[]`). If exactly one reg (or `regId` supplied), you get an enriched token directly. |
| 2 | `POST login/GetJwtByRegId` `{registrationId, pushToken}` → `{token}` | `POST auth/select-registration` | Body `{regId}` — **requires the phase-1 bearer token** (not a bare regId exchange). Returns `AuthTokenResponse {accessToken, refreshToken?, expiresIn?, requiresTosSignature}`. ⚠️ **No `headshotUrl` in the response** (legacy returned one). ⚠️ `pushToken` is NOT part of auth here — device registration is a separate call (#4). |
| — | `GET login/logout/{username}` | *(retire / use `auth/revoke`)* | Already **dead in the legacy app** — commented out in `auth.service.ts`. Nearest new is `POST auth/revoke` `{refreshToken}`. |

Role name/GUID constants match the legacy `ROLEIDS` map. Coaches map to the **`Staff`** role
(there is no distinct "Coach" role in the backend).

### Device / push registration (anonymous — device token is the identity)

> Controller route is **`device`** (singular), same as the Events app.

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 3 | `POST Firebase/JobDevice_Add` | `POST device/register` | Body `{deviceToken, jobId, deviceType}`. Send the **real** platform in `deviceType` (`'ios'`/`'android'`). |
| 4 | `POST Firebase/SwapPhone_DeviceTokens` | `POST device/swap-token` | Body `{oldDeviceToken, newDeviceToken}`. |

(The Events-only `device/subscribe-team` and `device/subscriptions/{jobId}` favorites endpoints
exist too but the Teams app never used team-favorite subscriptions — ignore them here.)

### Roster

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 5 | `GET TeamRoster/GetTeamRosterByTeamId/{teamId}` | `GET teams/{teamId}/roster` | Direct match. Returns `TeamRosterDetailDto {staff[], players[]}` (Phase-4 shape). ⚠️ **`headshotUrl` currently returns `null`** for every row — the roster projection defers headshot resolution (`TeamRepository` sets `HeadshotUrl = null`). See Gap 2. |
| 6 | `GET TeamRoster/GetTeamRosterByRegistrationId/{registrationId}` | `GET my-roster` **or** resolve `teamId` → #5 | `my-roster` resolves the roster from the **regId in the JWT** (no path param) but returns `MyRosterResponseDto` (a different shape). For parity with the legacy team-roster screen, resolve `teamId` (once Gap 1 is fixed) and call `teams/{teamId}/roster`. |

### Attendance (all on `TeamAttendanceController`, `teamId` now in the path)

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 7 | `POST TeamAttendance/GetTeamAttendanceEvents` `{roleName,userId,teamId,pagingParams}` | `GET teams/{teamId}/attendance/events` | **POST→GET, no body, no pagination.** Returns `AttendanceEventDto[]` (`present/notPresent/unknown` counts kept). |
| 8 | `POST TeamAttendance/AddAttendanceEvent` | `POST teams/{teamId}/attendance/events` | Body `CreateAttendanceEventRequest {comment, eventTypeId, eventDate, eventLocation}` (drop `teamId` from body — it's in the path). |
| 9 | `DELETE TeamAttendance/DeleteAttendanceEvent/{eventId}` | `DELETE teams/{teamId}/attendance/events/{eventId}` | `eventId` is `int`. |
| 10 | `GET TeamAttendance/GetTeamRosterAttendanceByEventId/{eventId}` | `GET teams/{teamId}/attendance/events/{eventId}/roster` | Returns `AttendanceRosterDto[]`. ⚠️ Per-player status is now `present: boolean` — legacy used a numeric `playerRsvpStatus` (tri-state incl. "unknown"). See Gap 4. |
| 11 | `POST TeamAttendance/UpdatePlayerRsvpStatus` `{eventId, playerId, present}` | `POST teams/{teamId}/attendance/events/{eventId}/rsvp` | Body `{playerId, present}` — `eventId` moves to the path. |
| 12 | `POST TeamAttendance/GetTeamRosterMemberAttendanceHistory` `{playerId, teamId}` | `GET teams/{teamId}/attendance/player/{userId}/history` | ⚠️ Path param is `{userId}` (the ApplicationUser id / roster `userId`), **not** the attendance `playerId`. Returns `AttendanceHistoryDto[] {eventDate, eventType, present}`. |
| 13 | `GET TeamAttendance/GetTeamAttendanceEventTypeOptions` | `GET teams/attendance/event-types` | Returns `AttendanceEventTypeDto[] {id, attendanceType}` (legacy consumed `JsonSelectListOption {value, text}` — remap fields). |

### Team links

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 14 | `POST TeamLinks/GetTeamLinks` `{roleName,teamId,pagingParams}` | `GET teams/{teamId}/links` | **POST→GET, no body, no pagination.** Returns `TeamLinkDto[]`. **Use the per-team route on `TeamManagementController`, NOT the AdminOnly `api/team-links` surface** (that one is job-wide admin management). |
| 15 | `POST TeamLinks/AddTeamLink` | `POST teams/{teamId}/links` | Body `AddTeamLinkRequest {label, docUrl, addAllTeams}`. `addAllTeams` replaces `bAddAllTeams`; drop `userId/roleName/requestTimestampString`. |
| 16 | `DELETE TeamLinks/DeleteTeamLink/{docId}` | `DELETE teams/{teamId}/links/{docId}` | `docId` is `guid`. |

### Team pushes

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 17 | `POST TeamPushes/GetTeamPushes` `{roleName,teamId,pagingParams}` | `GET teams/{teamId}/pushes` | **POST→GET, no body.** Returns `TeamPushDto[]`. |
| 18 | `POST TeamPushes/AddTeamPush` | `POST teams/{teamId}/pushes` | Body `SendTeamPushRequest {pushText, addAllTeams}`. |

### Chat — REST history + SignalR

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 19 | `POST Chat/GetTeamMessages` `{userId, teamId, pagingParams}` | `POST teams/{teamId}/chat` | Body `GetChatMessagesRequest {teamId, pageNumber, rowsPerPage}` → `GetChatMessagesResponse {messages: ChatMessageDto[], includesAll}`. |

**SignalR hub — path change `/ChatHub` → `/hubs/chat`.** Broadcast event names are unchanged;
the two client→server *write* methods changed from a single model arg to flat primitive args:

| Direction | Legacy | New | Notes |
|---|--------|-----|-------|
| C→S | `invoke('JoinGroup', teamId)` | `JoinGroup(teamId: Guid)` | Unchanged. |
| C→S | *(not used)* | `LeaveGroup(teamId: Guid)` | New — call on leaving a team room. |
| C→S | `invoke('AddTeamChatMessage', model)` | `AddTeamChatMessage(teamId: Guid, userId: string, message: string)` | ⚠️ **3 positional args, not a `{message, teamId, userId, requestTimestampString}` object.** Server stamps its own timestamp. |
| C→S | `invoke('DeleteTeamChatMessage', model)` | `DeleteTeamChatMessage(teamId: Guid, messageId: Guid)` | ⚠️ **2 positional args, not a `{messageId, teamId, userId}` object.** |
| S→C | `on('newmessage_{teamId}')` | `newmessage_{teamId}` → `ChatMessageDto` | Unchanged name. |
| S→C | `on('deletemessage_{teamId}')` | `deletemessage_{teamId}` → `messageId (Guid)` | Unchanged name. |

Connect with a token: `.withUrl(\`${base}/hubs/chat\`, { accessTokenFactory: () => accessToken })`.
The hub currently has **no `[Authorize]`** attribute server-side, so a token is not strictly
required today — but wire `accessTokenFactory` anyway so it keeps working if the hub is locked
down later. The dead `${API_URL}/api/Chat/DeleteTeamChatMessage` URL constant in the legacy
`chat.service.ts` (note the doubled `/api/`) was never hit — delete it.

### File upload (headshots)

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 20 | `POST FileUpload/UploadFile` multipart `{file, userId, regfieldName:'BUploadHeadshot'}` | `POST files/upload` | ⚠️ **Field mismatch + missing wiring.** New endpoint is a generic `[Authorize]` upload that accepts **only** the multipart `file` field and returns `{fileUrl}`. It does **not** accept `userId`/`regfieldName` and does **not** persist the URL to the registration's `HeadshotPath`. As-is it cannot replace the headshot feature. See Gap 2. |
| 21 | `POST FileUpload/DeleteFile` `{userId, regfieldName}` | `POST files/delete` | ⚠️ Deletes by `{fileUrl}`, not by `userId`+`regfieldName`. |

### Team calendar — **UNMIGRATED (and already broken in the legacy app)**

| # | Legacy | New | Notes |
|---|--------|-----|-------|
| 22 | `GET TeamCalendar/GetListAudienceIdOptions` | **none** | No calendar controller exists in the new backend. |
| 23 | `GET TeamCalendar/GetEventOwnerOptions` | **none** | — |
| 24 | `GET TeamCalendar/GetTeamGoogleCalendarId/{teamId}` | **none** | — |
| 25 | `DELETE TeamCalendarEvents/DeleteTeamEvent/{eventId}` | **none** | — |

⚠️ **These four already do not work in the shipped Teams app.** `team-events.service.ts` builds
its URLs from `` `${environment.apiUrl}/api/...` `` but the `environment` object has **no
`apiUrl` property** (only `production/firebaseConfig/appShellConfig`) — so every one of these
resolves to `undefined/api/...`. Treat the team-calendar feature as **out of scope** for the
rewire unless Todd wants it rebuilt (Gap 3).

---

## Backend gaps — decisions needed before/alongside the rewire

**Gap 1 — surface `teamId` + team flags after login (BLOCKER).** The app cannot address any
`teams/{teamId}/...` route without knowing its `teamId`. Recommended fix (mirrors the Events
`jobPath` addition): add `teamId`, `teamName`, `headshotUrl`, `bEnableMobileRsvp`,
`bEnableMobileTeamChat`, and `calendarId` to `RegistrationDto` so the app captures `teamId` at
registration selection. Alternative: a `GET teams/my-context` endpoint that resolves them from
the `regId` claim. **Recommendation: extend `RegistrationDto`** — one shape, no extra round-trip,
and the feature flags gate the RSVP/chat tabs the same way `ITeam_User_RoleData` did.

**Gap 2 — headshots (upload + display).** Two halves both missing: (a) `files/upload` doesn't
tie an upload to a registration's `HeadshotPath`; (b) the roster projection returns
`headshotUrl: null` for everyone. Until both are wired, headshots neither upload nor render.
Needs a headshot-specific endpoint (or a `regfieldName`/`userId`-aware upload) **and** headshot
resolution in the roster query.

**Gap 3 — team calendar.** Entirely unmigrated; also already dead client-side (see #22–25).
Decision: rebuild, or drop the calendar tab from the Teams app.

**Gap 4 — RSVP tri-state.** Legacy per-player status was numeric (present / not-present /
**unknown/no-response**). The new `rsvp` endpoint and `AttendanceRosterDto` use `present: boolean`
— there's no way to set/represent "no response". The event-level DTO still carries an `unknown`
count, so the aggregate survives, but the per-player UI loses the third state. Confirm this is
acceptable or add a tri-state to the request/DTO.

**Gap 5 — per-team authorization (tracked, not the Mac's job).** The `teams/{teamId}/*` routes
are `[Authorize]` only — no policy scopes a staff member to *their own* team (known P0 IDOR on
`TeamManagementController`). The app just sends the bearer; server-side ownership scoping is a
separate backend fix.

---

## Corrections to the Phase 4–7 docs

1. **Phase-4 roster (Teams)**: `GetTeamRosterByTeamId/{teamId}` → **`GET api/teams/{teamId}/roster`**
   (confirmed). `GetTeamRosterByRegistrationId` → `GET api/my-roster` (token-scoped, different DTO)
   or resolve `teamId` and reuse `/roster`.
2. **Phase-6 hub path**: the doc says `/hubs/chat` — correct. But it lists
   `AddTeamChatMessage(teamId, userId, messageText)` / `DeleteTeamChatMessage(teamId, messageId)`
   as "signatures unchanged" — they are **changed** from the legacy single-object-arg calls.
   Ports must switch to positional args.
3. **Phase-7 upload**: `files/upload` takes only `file` and is **generic** — it does not carry the
   `userId`/`regfieldName:'BUploadHeadshot'` the headshot flow needs, and does not persist
   `HeadshotPath`. Phase-7's "just swap the URL" is insufficient for headshots (Gap 2).
4. **Team links/pushes**: use the **per-team** `teams/{teamId}/links|pushes` routes, **not** the
   AdminOnly `api/team-links` / `api/push-notifications` job-wide admin surfaces.

---

## Client-side work checklist (Mac)

1. `environment.ts`: replace `API_URL='.../api/tsic_teams_2025'` with `API_URL='https://devapi.teamsportsinfo.com/api'` (RESTful per-area paths; drop the `tsic_teams_2025` suffix). Replace `CHAT_HUB_URL` target with `https://devapi.teamsportsinfo.com` + hub path `/hubs/chat`. Delete the dead `environment.apiUrl` references in `team-events.service.ts`.
2. **Auth**: `quick-login` → `select-registration` → store `accessToken`+`refreshToken`; add a bearer interceptor covering all `teams/*`, `files/*`, `my-roster`, and the SignalR `accessTokenFactory`.
3. **Capture `teamId`** (+ `bEnableMobileRsvp`/`bEnableMobileTeamChat`) at registration selection once Gap 1 lands; thread `teamId` into every team call.
4. Rewire the 8 services per this map; response models go camelCase (Phase 4–7 have field diffs). Reads that were POST-with-body are now GET-no-body (attendance, links, pushes).
5. **Chat**: switch hub write methods to positional args (`AddTeamChatMessage(teamId, userId, message)`, `DeleteTeamChatMessage(teamId, messageId)`); keep the `newmessage_/deletemessage_{teamId}` listeners as-is; add `LeaveGroup` on room exit.
6. Fix while touching: hardcoded `deviceType`; drop `userId/roleName/requestTimestampString` from link/push bodies; remap `bAddAllTeams`→`addAllTeams`.
7. **Blocked pending backend**: headshot upload + display (Gap 2), team calendar tab (Gap 3), RSVP "unknown" state (Gap 4). Don't rewire these until the backend decisions land.
</content>
</invoke>
