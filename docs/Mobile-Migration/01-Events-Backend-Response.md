# 01 — Backend Response to the Events-App Requests (docs 00–03)

> **Read this alongside `00-Endpoint-Map.md`.** The Events team delivered four docs
> (`00`–`03`, 2026-07-19) listing 12 backend requests/defects against `view-schedule/*`.
> This doc records how each was resolved: what the new backend now returns, what the Mac
> side must do client-side, and what was deliberately **not** changed.
>
> Base URL (staging): `https://devapi.teamsportsinfo.com` · JSON is **camelCase** ·
> routes relative to `/api`. All contract additions below are **live on staging** once the
> API is restarted (backend commits `66205381`, `5c411df0`, `abdb6712`, `9b965e18`,
> `31e29036`, `3b7f455d`, `a606f71f`).

---

## TL;DR for the Mac side

Most of these were **fields the backend already computed and threw away** — they are now on
the DTOs, so the Events app's existing models mostly just need the new properties filled in.
Three require client work: **item 9 favorites** (pass a `deviceToken`), **item 10 roster gate**
(read one capability), and **item 12 home/away** (pure client derivation). One field was
**removed** (item 4 `mobileScorerCanEdit`) — stop reading it.

---

## Item-by-item

### 1 — Bronze (3rd-place) match now reaches the client ✅
`POST view-schedule/brackets` → `DivisionBracketResponse.matches` now includes the bronze game
with **`roundType: "B"`**. It is **not** part of the ladder tree (it has no parent to advance
into). Render it as a standalone card **beside/below the ladder**, not inside the bracket
diagram. Round label ("Bronze") comes from `ScheduleTeamTypes` server-side; the web app groups
it under a "Bronze" heading in an *outside-the-ladder* strip — mirror that.

### 2 — `jobHasBrackets` no longer misses bronze-only jobs ✅
`GET view-schedule/filter-options` → `jobHasBrackets` is now `true` when a job has **only** a
bronze game (previously `false`, which hid the Brackets tab). Gate the Brackets tab on this flag
as before — it is now correct.

### 3 — Deterministic game ordering ✅
All schedule/team/bracket queries now order **`gDate` then `fName`**. Same-kickoff games no
longer reshuffle between calls. **Remove any client-side re-sort** you added to compensate — if
you still want a stable order, sort by `gDate` then `fName` to match the server.

### 4 — `mobileScorerCanEdit` REMOVED ⚠️ (action required)
This flag was never populated (always `false`) and has been **deleted from `ViewGameDto`**.
**Stop reading it.** Score-entry permission is enforced server-side by the `CanScore` policy and
by your own scorer session — a scorer token for job A is rejected on job B. Gate the pencil/score
UI on your scorer login state, exactly as the app already does; do not look for a per-game flag.

### 5 — Team's own record ✅
`GET view-schedule/team-results/{teamId}` → `TeamResultsResponse.teamRecord` (`"W-L-T"` string,
nullable). **Round-robin only** — bracket/bronze/consolation games never affect a record. Use
this instead of counting returned games (which include bracket play and gave a wrong record).

### 6 — Field address on filter-options ✅ (address, not coordinates)
`filter-options` → `FieldSummaryDto.fAddress` (nullable string, e.g.
`"123 Main St, Springfield, IL 62704"`). The product moved to **address-based directions** — we
did **not** add `latitude`/`longitude`. Open a maps URL with the address string
(`https://maps.google.com/?q=<encoded fAddress>`).

### 7 — Per-agegroup "has brackets" shortcut ✅
- `ViewGameDto.gameAgegroupHasBrackets`
- `DivisionStandingsDto.agegroupHasBrackets` (+ new `DivisionStandingsDto.agegroupId` so you can
  key off the ID, not the name)

Both are now populated (bronze-only agegroups count). Use them to show the "jump to this age
group's bracket" button on game rows and standings rows. The job-wide `jobHasBrackets` is
unchanged.

### 8 — Consolation games reachable in the brackets view ✅
`DivisionBracketResponse.consolationGames` → `ConsolationGameDto[]`:
```
{ gid, agegroupName, agegroupId, fName?, gDate?, t1Name, t2Name,
  t1Id?, t2Id?, t1Score?, t2Score?, fAddress? }
```
These are standalone placement games (5v6) — **never in the ladder tree**. Render them in the
same *outside-the-ladder* strip as bronze (item 1). The web app shows a "Consolation" group
there. Your existing consolation UI/model was dead because the old response had no channel for
these — this is that channel. Note: consolation carries **`fAddress`**, not a `fieldId`
(address-based directions per item 6).

### 9 — Discrete bracket dates + favorites + hide-scores ✅
**Dates:** `BracketMatchDto` now has discrete **`gDate`** and **`fName`** (in addition to the
existing pre-formatted `locationTime`, which is unchanged). Prefer `gDate`/`fName` when you want
to sort by time, group by day, or format yourself — no more parsing the em-dash out of
`locationTime`. **`gDate` is emitted as local wall-clock (no `Z`/offset)** — game times are in
the game's own timezone; parse as local, do **not** apply a UTC shift. (Verify against a live
payload.)

**Favorites (client action required):** pass your device push token as **`deviceToken`** on the
`ScheduleFilterRequest` body (the same anonymous token used for `device/*` registration — this is
**not** auth). When present, the backend fills:
- `ViewGameDto.t1IsSubscribed` / `t2IsSubscribed`
- `StandingsDto.isFavorited`

Omit `deviceToken` (as the web app does) and these stay `false`. No new endpoint — the favorite
write (`device/subscribe-team`) and push-on-score fanout are already wired; this only lights up
the **star state** on schedule/standings rows.

**Hide-scores:** `ViewGameDto.bHideScores` is now populated from the agegroup's
`bHideStandings` setting (youngest divisions). When `true`, suppress the score for that game.

### 10 — `restrictPublicRosters` in capabilities ✅ (action required)
`GET view-schedule/capabilities` → `ScheduleCapabilitiesDto.restrictPublicRosters` (bool). When
`true`, **hide the roster button** — the public-roster endpoint returns `[]` on these jobs, so
the button previously opened an empty card. No data ever leaked; this just stops promising a view
that isn't there.

### 11 — Games endpoint performance ✅ (no contract change)
The games and team-results responses were doing a **second, whole-job scan** on every request
just to build W-L-T strings. That is now a single server-side `GROUP BY` — **faster TTFB, same
response shape**. Nothing to change on the client.

**Paging:** we did **not** add `Skip`/`Take`. The endpoint returns the full set (the web grid
renders ~1,300 games instantly). **If the Events app wants server-side infinite scroll**, tell us
and we'll add opt-in `skip`/`take` on `ScheduleFilterRequest` + an `X-Total-Count` response header
— the web array shape stays untouched. Not built speculatively.

### 12 — Home/away (client-side only) ✅ (action required — no backend field)
**Rule: `T1` = home, `T2` = away. Always.** No sport-based inversion. We did **not** add a
`homeIsT2`/`opponentIsHome` field or any schema/DTO change — home/away is pure slot position and
you already have `t1Id`/`t2Id`/`t1Name`/`t2Name`.

On a **team schedule**, the app knows the viewing team's ID, so "am I home?" is "am I `t1`?":
```
const isHome = game.t1Id === viewingTeamId;
const label  = isHome ? `HOME vs ${game.t2Name}` : `AWAY vs ${game.t1Name}`;
```
Legacy's soccer inversion (`HomeIsT2 = SportName === "soccer"`) and its separate slot-only
`OpponentIsHome` mechanism are both **discarded** — legacy was internally inconsistent. Web needs
no change (its grid shows both slots side-by-side, so home/away is implicit); this is a
mobile-only render tweak.

---

## Deferred / not done (so you're not waiting on them)

- **Item 11 paging** — not built; opt-in, on request (see item 11 above).
- **Item 11 repo projection** — an internal standards cleanup (`GetFilteredGamesAsync` still
  returns entities); **no client impact**, tracked separately.
- **`filter-options` favorite stars on the age-group/team tree** (`CadtTeamNode.isFavorited`) —
  lower value, more invasive; deferred. Row-level stars (item 9) cover the common case.

## Housekeeping

- **API model regen:** the web app's generated models are updated. The generated-models **barrel
  (`index.ts`) export of `ConsolationGameDto` is not yet committed** (it is entangled with an
  unrelated in-flight change); a full regen will land it. No runtime impact — models are imported
  by relative path.
- **`swagger-output.json` at repo root is 0 bytes** — regenerate for an authoritative schema
  before deep client work.
