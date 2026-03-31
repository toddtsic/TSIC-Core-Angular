# Phase 1 — Events App Migration Script

> **Backend changes**: DTO field extensions (additive, non-breaking) + `POST /api/auth/quick-login` endpoint.
> **Ionic-side work**: Update HTTP service URLs, request shapes, and response interfaces.

---

## 1. Authentication

### Login (single-call)

**URL Change:**
```
OLD: POST api/tsic_events_2025/LoginAs/Login
NEW: POST api/auth/quick-login
```

**Request Interface:**
```typescript
// OLD
interface LoginRequest {
  Username: string;
  Password: string;
  JSeg: string;       // job segment identifier
}

// NEW
interface QuickLoginRequest {
  username: string;    // camelCase (ASP.NET Core default serialization)
  password: string;
  regId?: string;      // optional — if provided, returns enriched token directly
}
```

**Response Interface:**
```typescript
// OLD
interface LoginResponse {
  Id: string;
  UserName: string;
  Email: string;
  Token: string;
  RegistrationId?: number;
  JSeg: string;
}

// NEW
interface QuickLoginResponse {
  accessToken: string;          // was Token
  refreshToken?: string;        // NEW — for token refresh flow
  expiresIn?: number;           // NEW — seconds until expiry
  requiresTosSignature: boolean; // NEW — ToS acceptance required
  registrations?: RegistrationRoleDto[];  // populated only if regId was omitted and user has multiple roles
}

interface RegistrationRoleDto {
  roleName: string;
  roleRegistrations: RegistrationDto[];
}

interface RegistrationDto {
  regId: string;
  displayText: string;
  jobLogo: string;
  jobPath?: string;
}
```

**Service Method:**
```typescript
// OLD
login(username: string, password: string, jseg: string): Observable<LoginResponse> {
  return this.http.post<LoginResponse>(
    `${this.baseUrl}/api/tsic_events_2025/LoginAs/Login`,
    { Username: username, Password: password, JSeg: jseg }
  );
}

// NEW
login(username: string, password: string, regId?: string): Observable<QuickLoginResponse> {
  return this.http.post<QuickLoginResponse>(
    `${this.baseUrl}/api/auth/quick-login`,
    { username, password, regId }
  );
}
```

**Migration Notes:**
- `JSeg` is no longer sent — the backend resolves job context from `regId`
- If the user has exactly one registration, `quick-login` auto-selects it (no role selection needed)
- If multiple registrations exist, the response includes `registrations` array — display a picker, then call `POST /api/auth/select-registration` with the chosen `regId`
- Store `accessToken` and `refreshToken` in local storage
- The `expiresIn` field enables proactive token refresh

### Logout

**URL Change:**
```
OLD: GET api/tsic_events_2025/Logout/Logout/{username}
NEW: POST api/auth/revoke
```

**Request:**
```typescript
// OLD: username in URL path, no body
// NEW: refresh token in body
{ refreshToken: string }
```

---

## 2. Schedule (Games Tab)

### Get Schedule

**URL Change:**
```
OLD: POST api/tsic_events_2025/JobSchedule/GetJobScheduleFromUserPreferences
NEW: POST api/view-schedule/games
```

**Request Interface:**
```typescript
// OLD
interface ScheduleRequest {
  JobId: string;                    // Guid
  DeviceToken: string;
  UserPreferences: {
    ClubPreferences: string[];
    AgegroupPreferences: string[];  // Guid[]
    AgegroupDivisionPreferences: string[];
    TeamPreferences: string[];      // Guid[]
    DivPreferences: string[];
    LocationPreferences: string[];
    GameDayPreferences: string[];   // DateTime[]
    GameTimePreferences: string[];
    SingleTeamId: string;
    UnscoredOnly: boolean;
    AllGamesInStandings: boolean;
  };
  PagingParams: { PageNumber: number; RowsPerPage: number; };
}

// NEW — flattened, no wrapper object
interface ScheduleFilterRequest {
  clubNames?: string[];       // was ClubPreferences
  agegroupIds?: string[];     // was AgegroupPreferences (Guid strings)
  divisionIds?: string[];     // was DivPreferences
  teamIds?: string[];         // was TeamPreferences
  gameDays?: string[];        // was GameDayPreferences (ISO date strings)
  fieldIds?: string[];        // was LocationPreferences (Guid strings)
  times?: string[];           // NEW — "HH:mm" format
  unscoredOnly?: boolean;     // was in UserPreferences
}
```

**Response Interface:**
```typescript
// OLD — wrapped
interface ScheduleResponse {
  UseGameclock: boolean;
  HideStandingsPoints: boolean;
  HomeIsT2: boolean;
  UserCanScore: boolean;
  ListAvailableGameStatusCodesJoined: string;
  GameData: GameData_ViewModel[];
}

// NEW — array directly (metadata via separate GET /api/view-schedule/capabilities?jobPath=)
type ScheduleResponse = ViewGameDto[];
```

**ViewGameDto field mapping:**
```typescript
interface ViewGameDto {
  gid: number;               // same
  gDate: string;             // same (ISO DateTime)
  fName: string;             // same
  fieldId: string;           // same (Guid)
  latitude?: number;         // same
  longitude?: number;        // same
  fAddress?: string;         // NEW — pre-formatted address for Maps link
  agDiv: string;             // same — "U10:Gold"
  t1Name: string;            // same
  t2Name: string;            // same
  t1Id?: string;             // same
  t2Id?: string;             // same
  t1Score?: number;          // same
  t2Score?: number;          // same
  t1Type: string;            // same
  t2Type: string;            // same
  t1Ann?: string;            // same
  t2Ann?: string;            // same
  rnd?: number;              // same
  gStatusCode?: number;      // same
  color?: string;            // NEW — agegroup color
  t1Record?: string;         // same
  t2Record?: string;         // same
  divName?: string;          // NEW — division name standalone
  t1IsSubscribed: boolean;   // NEW — false until device endpoints (Phase 2) exist
  t2IsSubscribed: boolean;   // NEW — same
  gameAgegroupHasBrackets: boolean; // NEW — false until populated
  mobileScorerCanEdit: boolean;     // NEW — false until populated
  bHideScores: boolean;      // NEW — false until populated
}
```

**REMOVED from response** (now via `GET /api/view-schedule/capabilities?jobPath=`):
- `UseGameclock` → `capabilities.canScore` (different concept, check if needed)
- `HideStandingsPoints` → not exposed yet, file a request if needed
- `HomeIsT2` → not exposed yet
- `UserCanScore` → `capabilities.canScore`
- `ListAvailableGameStatusCodesJoined` → not exposed yet

**REMOVED from response** (no longer available):
- `RefCount` — referee count not tracked in new system
- `T1StatusClass` / `T2StatusClass` — compute client-side from scores/status

**Service Method:**
```typescript
// OLD
getSchedule(jobId: string, deviceToken: string, prefs: UserPreferences, page: PagingParams): Observable<ScheduleResponse> {
  return this.http.post<ScheduleResponse>(
    `${this.baseUrl}/api/tsic_events_2025/JobSchedule/GetJobScheduleFromUserPreferences`,
    { JobId: jobId, DeviceToken: deviceToken, UserPreferences: prefs, PagingParams: page }
  );
}

// NEW
getSchedule(filters: ScheduleFilterRequest): Observable<ViewGameDto[]> {
  return this.http.post<ViewGameDto[]>(
    `${this.baseUrl}/api/view-schedule/games`,
    filters
  );
}
```

**Note:** `jobPath` is resolved from the JWT token by the backend. No need to send it in the request body.

### Get Capabilities (new — call once on init)

```
NEW: GET api/view-schedule/capabilities?jobPath={jobPath}
```

```typescript
interface ScheduleCapabilitiesDto {
  canScore: boolean;       // replaces UserCanScore
  hideContacts: boolean;   // controls contacts tab visibility
  isPublicAccess: boolean; // public vs authenticated
  sportName: string;       // "Soccer", "Lacrosse", etc.
}
```

---

## 3. Standings

**URL Change:**
```
OLD: POST api/tsic_events_2025/JobStandings/GetJobStandingsFromUserPreferences
NEW: POST api/view-schedule/standings
```

**Request:** Same `ScheduleFilterRequest` as games (see above).

**Response Interface:**
```typescript
// OLD
interface StandingsResponse {
  UseGameClock: boolean;
  ListAgegroupDivisions: {
    AgegroupDivisionName: string;  // formatted "Agegroup:Division"
    AgegroupHasBrackets?: boolean;
    ListDivTeamRecords: Standings_ViewModel[];
  }[];
}

// NEW
interface StandingsByDivisionResponse {
  divisions: DivisionStandingsDto[];
  sportName: string;
}

interface DivisionStandingsDto {
  divId: string;
  agegroupName: string;              // was part of AgegroupDivisionName
  divName: string;                   // was part of AgegroupDivisionName
  agegroupHasBrackets: boolean;      // NEW (was on wrapper in legacy)
  teams: StandingsDto[];
}

interface StandingsDto {
  teamId: string;
  teamName: string;
  agegroupName: string;
  divName: string;
  divId: string;
  games: number;           // was nullable, now required
  wins: number;            // was nullable, now required
  losses: number;          // was nullable, now required
  ties: number;            // was nullable, now required
  goalsFor: number;        // was nullable, now required
  goalsAgainst: number;    // was GoalsVs → renamed
  goalDiffMax9: number;    // was nullable, now required
  points: number;          // was nullable, now required
  pointsPerGame: number;
  rankOrder?: number;
  tiePoints: number;       // NEW — raw tie point contribution
  isFavorited?: boolean;   // NEW — null until device endpoints exist
}
```

**Key Changes:**
- `GoalsVs` renamed to `goalsAgainst`
- Nullable number fields → required (always populated, default 0)
- `AgegroupDivisionName` (formatted string) → separate `agegroupName` + `divName`
- `UseGameClock` removed from standings response

---

## 4. Search / Filter Options

**URL Change (6 endpoints collapse to 1):**
```
OLD: GET api/tsic_events_2025/SearchParams/GetJobSearchOptions/{jobId}/{deviceToken?}
OLD: GET api/tsic_events_2025/SearchParams/GetTeamsOnlyJobSearchOptions/{jobId}/{deviceToken?}
OLD: GET api/tsic_events_2025/SearchParams/GetJobAgegroupOptions/{jobId}
OLD: GET api/tsic_events_2025/SearchParams/GetJobClubOptions/{jobId}
OLD: GET api/tsic_events_2025/SearchParams/GetJobLocationOptions/{jobId}
OLD: GET api/tsic_events_2025/SearchParams/GetJobGameDateOptions/{jobId}
NEW: GET api/view-schedule/filter-options?jobPath={jobPath}
```

**Response Interface:**
```typescript
// NEW — hierarchical CADT tree replaces flat lists
interface ScheduleFilterOptionsDto {
  clubs: CadtClubNode[];          // replaces ClubOptions flat list
  agegroups: LadtAgegroupNode[];  // LADT tree (no club level)
  gameDays: string[];             // ISO date strings
  times: string[];                // "HH:mm" format
  fields: FieldSummaryDto[];      // replaces LocationOptions
  jobHasBrackets: boolean;        // NEW — was in SearchParamOptions root
  jobHasLinks: boolean;           // NEW — was in SearchParamOptions root
}

interface CadtClubNode {
  clubName: string;
  teamCount: number;
  playerCount: number;
  agegroups: CadtAgegroupNode[];
}

interface CadtAgegroupNode {
  agegroupId: string;
  agegroupName: string;
  color?: string;
  teamCount: number;
  playerCount: number;
  divisions: CadtDivisionNode[];
}

interface CadtDivisionNode {
  divId: string;
  divName: string;
  teamCount: number;
  playerCount: number;
  teams: CadtTeamNode[];
}

interface CadtTeamNode {
  teamId: string;
  teamName: string;
  playerCount: number;
  isFavorited?: boolean;    // null until device endpoints exist
}
```

**Migration approach for flat list consumers:**
```typescript
// Helper: extract flat agegroup list from CADT tree
function extractAgegroups(clubs: CadtClubNode[]): { id: string; name: string }[] {
  const seen = new Set<string>();
  const result: { id: string; name: string }[] = [];
  for (const club of clubs) {
    for (const ag of club.agegroups) {
      if (!seen.has(ag.agegroupId)) {
        seen.add(ag.agegroupId);
        result.push({ id: ag.agegroupId, name: ag.agegroupName });
      }
    }
  }
  return result;
}

// Helper: extract flat club list
function extractClubs(clubs: CadtClubNode[]): { name: string }[] {
  return clubs.map(c => ({ name: c.clubName }));
}

// Helper: extract flat team list
function extractTeams(clubs: CadtClubNode[]): { id: string; name: string; club: string }[] {
  const result: { id: string; name: string; club: string }[] = [];
  for (const club of clubs) {
    for (const ag of club.agegroups) {
      for (const div of ag.divisions) {
        for (const team of div.teams) {
          result.push({ id: team.teamId, name: team.teamName, club: club.clubName });
        }
      }
    }
  }
  return result;
}
```

---

## 5. Brackets

**URL Change:**
```
OLD: POST api/tsic_events_2025/JobSchedule/GetJobBracketsFromUserPreferences
OLD: GET  api/tsic_events_2025/JobSchedule/GetJobBracketsFromTeamId/{teamId}
OLD: GET  api/tsic_events_2025/JobSchedule/GetJobBracketsFromGid/{gid}
OLD: GET  api/tsic_events_2025/JobSchedule/GetJobBracketsFromAgegroupDivName/{jobId}/{agegroupDivName}
NEW: POST api/view-schedule/brackets
```

**Request:** Same `ScheduleFilterRequest` as games.

**Response:**
```typescript
// NEW — array of per-division bracket responses
type BracketsResponse = DivisionBracketResponse[];

interface DivisionBracketResponse {
  agegroupName: string;
  divName: string;
  champion?: string;             // NEW — winner of Finals
  matches: BracketMatchDto[];
}

interface BracketMatchDto {
  gid: number;
  t1Name: string;
  t2Name: string;
  t1Id?: string;
  t2Id?: string;
  t1Score?: number;
  t2Score?: number;
  t1Css: string;               // "winner", "loser", "pending"
  t2Css: string;
  locationTime?: string;       // was LocTime
  fieldId?: string;            // NEW — for field directions link
  roundType: string;           // Z, Y, X, Q, S, F
  parentGid?: number;          // NEW — tree structure for bracket rendering
}
```

---

## 6. Team Schedule Summary

**URL Change:**
```
OLD: GET api/tsic_events_2025/JobSchedule/GetTeamScheduleSummary/{teamId}
NEW: GET api/view-schedule/team-results/{teamId}
```

**Response:**
```typescript
// NEW
interface TeamResultsResponse {
  teamName: string;
  agegroupName: string;
  clubName?: string;
  games: TeamResultDto[];
}

interface TeamResultDto {
  gid: number;
  gDate: string;                // was When
  location: string;             // was Location (same)
  opponentName: string;         // same
  opponentTeamId?: string;      // same
  teamScore?: number;           // same
  opponentScore?: number;       // same
  outcome?: string;             // "W", "L", "T"
  gameType: string;             // "Pool Play" or bracket round name
  opponentRecord?: string;      // NEW — opponent's W-L-T
  latitude?: number;            // NEW — field coordinates for maps
  longitude?: number;           // NEW
  gStatusCode?: number;         // NEW — game status
}
```

**REMOVED (compute client-side or not needed):**
- `TeamGameType` / `OpponentGameType` — use `gameType`
- `TeamName` — in wrapper, not per game
- `TeamRecord` — in wrapper context
- `OpponentIsHome` — compute from game data
- `AgDiv` — in wrapper context
- `OpponentIsSubscribed` — deferred to device endpoints
- `UseGameClock` — from capabilities endpoint

---

## 7. Score Update

**URL Change:**
```
OLD: POST api/tsic_events_2025/UpdateGameScore/Update
NEW: POST api/view-schedule/quick-score
```

**Request:**
```typescript
// OLD
interface UpdateGameScoreRequest {
  Gid: number;
  T1Score: number;
  T2Score: number;
  GameStatusCode?: number;
}

// NEW
interface EditScoreRequest {
  gid: number;         // same
  t1Score: number;     // same
  t2Score: number;     // same
  gStatusCode?: number; // was GameStatusCode
}
```

**Note:** Requires `[Authorize]` — JWT token must be in Authorization header.

---

## Summary of URL Changes

| # | Old URL | New URL | Notes |
|---|---------|---------|-------|
| 1 | `POST LoginAs/Login` | `POST api/auth/quick-login` | Single-call auth |
| 2 | `GET Logout/Logout/{username}` | `POST api/auth/revoke` | Body: `{ refreshToken }` |
| 3 | `POST JobSchedule/GetJobScheduleFromUserPreferences` | `POST api/view-schedule/games` | Flat filter request |
| 4 | `POST JobSchedule/GetJobBrackets*` (4 endpoints) | `POST api/view-schedule/brackets` | Single endpoint |
| 5 | `GET JobSchedule/GetActiveGamesData/{jobId}` | `POST api/view-schedule/games` | Use date filter |
| 6 | `GET JobSchedule/GetTeamScheduleSummary/{teamId}` | `GET api/view-schedule/team-results/{teamId}` | |
| 7 | `GET JobSchedule/GetTeamRoster/{teamId}` | `GET api/teams/{teamId}/roster` | Phase 4 |
| 8 | `POST JobStandings/*` (4 endpoints) | `POST api/view-schedule/standings` | Single endpoint |
| 9 | `GET SearchParams/*` (6 endpoints) | `GET api/view-schedule/filter-options?jobPath=` | CADT tree |
| 10 | `POST UpdateGameScore/Update` | `POST api/view-schedule/quick-score` | Minor renames |
| — | (new) | `GET api/view-schedule/capabilities?jobPath=` | Call once on init |
