# 00 — Master Endpoint Map (TSIC-Events → New Backend)

> **This is the authoritative handoff spec for the Mac-side rewire.** It maps every HTTP call
> the Events app actually makes (24 unique endpoints, verified by code sweep 2026-07-16) to its
> final new-backend route. The Phase 1–7 docs remain the field-by-field shape reference, but
> **where a phase doc conflicts with this map, this map wins** (corrections listed at the bottom).
>
> Base URL (staging): `https://devapi.teamsportsinfo.com`
> All new-backend JSON is **camelCase**. All routes below are relative to `/api`.

## How jobPath works (read first)

The mobile app is anonymous for everything except scorer login + score entry. The anonymous
`view-schedule/*` endpoints identify the event by a **`?jobPath=` query parameter** — NOT jobId.
`GET /api/events` now returns `jobPath` on each `EventListingDto` (added 2026-07-16): capture it
at event selection alongside `jobId` and pass it to every view-schedule call. The `events/*` and
`device/*` endpoints stay jobId-keyed. The four deep-link lookups need neither — they resolve
the job server-side from the gid/teamId.

## The 24 calls

### Auth (scorer only)

| # | Legacy | New | Notes |
|---|---|---|---|
| 1 | `POST loginas/login` | `POST auth/quick-login` | Body `{username, password, regId?}`. Response includes `accessToken`, `refreshToken`, and — when the user has multiple registrations — a `registrations` array; pick the **Scorer** registration for the selected job, then `POST auth/select-registration` with its `regId`. See Phase-1 §1. |
| 2 | `GET logout/logout/{username}` | `POST auth/revoke` | Body `{refreshToken}`. |

Scoring authorization is the `CanScore` policy (Superuser/Director/SuperDirector/**Scorer**),
added 2026-07-16. The score write validates the game belongs to the JWT's job — a scorer token
for job A gets 400 on job B's games.

### Event browse (anonymous, jobId-keyed)

| # | Legacy | New | Notes |
|---|---|---|---|
| 3 | `GET customers/GetCustomerJobs` | `GET events` | `EventListingDto[]` — now includes `jobPath` (required for view-schedule calls). `jobLogoUrl` is a **filename**; prepend `https://statics.teamsportsinfo.com/BannerFiles/`. |
| 13 | `GET JobSchedule/GetActiveGamesData/{jobId}[/{dt}]` | `GET events/{jobId}/active-games?preferredGameDate=` | Same DTO shape (countdown clock). |
| 18 | `GET GameClock/GetGameClockData/{jobId}` | `GET events/{jobId}/game-clock` | Wrapper removed — flat config object (Phase-3 §4). |
| 20 | `GET Alerts/GetJobMobileAlerts/{jobId}` | `GET events/{jobId}/alerts` | Serves UTC — **delete the client's hardcoded 7-hour AZ offset**. |
| 21 | `GET JobDocs/Get/{jobId}` | `GET events/{jobId}/docs` | Phase-3 §3. |

### Schedule / standings / brackets (anonymous, jobPath-keyed)

| # | Legacy | New | Notes |
|---|---|---|---|
| 4 | `GET SearchParams/GetJobSearchOptions/{jobId}/{devTok?}` | `GET view-schedule/filter-options?jobPath=` **+** `GET device/subscriptions/{jobId}?deviceToken=` | Favorites are no longer embedded in search options — merge the two responses client-side (`isFavorited = subscribedTeamIds.includes(teamId)`). Also call `GET view-schedule/capabilities?jobPath=` once on init for `canScore`/`sportName`. |
| 5 | `GET SearchParams/GetTeamsOnlyJobSearchOptions/...` | *(derive client-side)* | Flatten teams from the CADT tree of filter-options (helpers in Phase-1 §4) + subscriptions. |
| 6 | `POST JobSchedule/GetJobScheduleFromUserPreferences` | `POST view-schedule/games?jobPath=` | Flat `ScheduleFilterRequest`, response `ViewGameDto[]` unwrapped, **no pagination**. Phase-1 §2. |
| 8 | `GET JobSchedule/GetTeamScheduleSummary/{teamId}` | `GET view-schedule/team-results/{teamId}?jobPath=` | Phase-1 §6. |
| 9 | `POST JobSchedule/GetJobBracketsFromUserPreferences` | `POST view-schedule/brackets?jobPath=` | Response `DivisionBracketResponse[]`. Phase-1 §5. |
| 10 | `GET JobSchedule/GetJobBracketsFromTeamId/{teamId}` | `GET view-schedule/brackets/by-team/{teamId}` | NEW (2026-07-16). No jobPath needed. |
| 11 | `GET JobSchedule/GetJobBracketsFromGid/{gid}` | `GET view-schedule/brackets/by-game/{gid}` | NEW (2026-07-16). |
| 12 | `GET JobSchedule/GetJobBracketsFromAgegroupDivName/...` | *(retire)* | Navigate with real IDs from the standings response instead of formatted names — by-team/by-game cover both entry points. |
| 14 | `POST JobStandings/GetJobStandingsFromUserPreferences` | `POST view-schedule/standings?jobPath=` | Phase-1 §3 (`GoalsVs` → `goalsAgainst` etc.). |
| 15 | `GET JobStandings/GetGamePoolStandings/{gid}` | `GET view-schedule/standings/by-game/{gid}` | NEW (2026-07-16). |
| 16 | `GET JobStandings/GetGamePoolStandingsFromTeamId/...` | `GET view-schedule/standings/by-team/{teamId}` | NEW (2026-07-16). deviceToken param dropped — merge favorites from subscriptions. |
| 17 | `GET JobStandings/GetAgegroupStandings/{jobId}/{agId}` | `POST view-schedule/standings?jobPath=` with body `{agegroupIds: [agId]}` | No dedicated endpoint needed. |

### Roster

| # | Legacy | New | Notes |
|---|---|---|---|
| 7 | `GET JobSchedule/GetTeamRoster/{teamId}/true` | `GET public-rosters/team/{teamId}?jobPath=` | **NOT** `teams/{teamId}/roster` (that surface is authed admin). Honors `BRestrictPublicRosters` (empty list when restricted — render "roster not available"). Response `PublicRosterPlayerDto[]`. |

### Devices / favorites / push registration (anonymous, token = identity)

> Controller route is **`device`** (singular) — Phase-2 doc says `devices`, which is wrong.

| # | Legacy | New | Notes |
|---|---|---|---|
| 22 | `POST Firebase/JobDevice_Add` | `POST device/register` | Body `{deviceToken, jobId, deviceType}`. **Fix the client bug: `deviceType` is hardcoded `'ios'` — send the real platform.** |
| 23 | `POST Firebase/ToggleDevice_TeamSubscription` | `POST device/subscribe-team?jobId={jobId}` | Body `{deviceToken, teamId, deviceType}`. Returns `{subscribedTeamIds: Guid[]}` — NOT enriched team objects; update local state from the ID list. |
| 24 | `POST Firebase/SwapPhone_DeviceTokens` | `POST device/swap-token` | Body `{oldDeviceToken, newDeviceToken}`. |
| — | *(new)* | `GET device/subscriptions/{jobId}?deviceToken=` | Returns `Guid[]`. Call on job selection to restore favorites. |

### Score entry (authed — Scorer or admin JWT)

| # | Legacy | New | Notes |
|---|---|---|---|
| 19 | `POST UpdateGameScore/update` | `POST view-schedule/quick-score` | Body `{gid, t1Score, t2Score, gStatusCode?}` (omitted status defaults to 6 = final). `Authorization: Bearer` header required; policy = `CanScore`. Annotations/refCount dropped — legacy fields `t1Ann/t2Ann/refCount` are not part of quick-score. |

Push-on-score is restored server-side (2026-07-16): after a score write, devices subscribed to
either team receive an FCM push whose `data` payload carries the **same keys the app's toast
already reads** — `jobName, agegroupName, divName, firstTeam, secondTeam, firstScore,
secondScore, jobLogoUrl` (winner first). No client change needed. Sends are Production-only
unless `Firebase:SendInSandbox` is set in the API's appsettings overlay (flip it in Staging
for E2E).

## Corrections to the phase docs

1. **Phase-2 route**: `api/devices/*` → **`api/device/*`** (singular). `subscriptions` returns a bare `Guid[]`, not wrapped.
2. **Phase-1 roster row** (`api/teams/{teamId}/roster`): mobile uses **`api/public-rosters/team/{teamId}`** instead — the teams/* surface is authenticated admin.
3. **Phase-1 games note** ("jobPath resolved from JWT"): true only for authed users. Anonymous mobile passes `?jobPath=` from the event listing.
4. **Phase-1 missing endpoints**: pool/agegroup standings (#15–17) and brackets deep links (#10–12) were absent from all phase docs — covered above.
5. **`GetActiveGamesData`** was listed in Phase-1's summary as "use games + date filter" — wrong; use `GET events/{jobId}/active-games` (it returns the countdown-clock DTO, not a game list).

## Client-side work checklist (Mac)

1. `environment.ts` / new staging env: `API_URL = 'https://devapi.teamsportsinfo.com/api'` (per-area paths above; drop the `/tsic_events_2025` suffix pattern).
2. Rewire the 7 services per this map; models go camelCase (Phase docs have field-by-field diffs).
3. Capture `jobPath` from `EventListingDto` at event selection; thread it into schedule calls.
4. Favorites: replace embedded `favoritedTeams` with `device/subscriptions` + client merge.
5. Auth: quick-login/select-registration/revoke; store `accessToken` + `refreshToken`; keep the bearer interceptor.
6. Fix while touching: hardcoded `deviceType:'ios'`; delete the 7-hour alerts offset.
7. `STATICS_URL` constant is currently dead — it becomes live again for `jobLogoUrl` filenames.
