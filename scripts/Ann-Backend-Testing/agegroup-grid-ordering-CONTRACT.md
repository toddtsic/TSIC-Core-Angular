# Backend Contract — Age-Group grid default ordering

**Origin:** PL-031 (LADT sibling grids moved to native Syncfusion sort). When the grid stopped doing its own sorting, the age-group grid lost its default ordering (system/holding buckets no longer sink to the bottom; no longer alphabetical). The fix belongs at the endpoint, so the rows **arrive** in the right order and the grid stays purely native.

**Status:** proposed — review and execute if reasonable.

---

## Endpoint to change

`GET /agegroups/by-league/{leagueId}` → `AgegroupDetailDto[]`

Chain:
- Controller: `LadtController.GetAgegroupsByLeague` — [LadtController.cs:431](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/LadtController.cs#L431)
- Service: `LadtService.GetAgegroupsByLeagueAsync` — [LadtService.cs:1398](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/LadtService.cs#L1398)
- Repo (shared): `_agegroupRepo.GetByLeagueIdAsync`

## What's already correct (verified against the code)

The repo `GetByLeagueIdAsync` **already** orders the age-groups: `.OrderBy(a => a.SortAge).ThenBy(a => a.AgegroupName)` ([AgeGroupRepository.cs:48-56](../../TSIC-Core-Angular/src/backend/TSIC.Infrastructure/Repositories/AgeGroupRepository.cs#L48)). So the rows already arrive age-ordered (`SortAge` is the purpose-built key — alphabetical would mis-order U8/U10/U12). **Do not touch the repo** — it is shared by 6 callers and already correct.

## The only missing behavior

The grid lost **one** thing when it stopped self-sorting: the system/holding buckets (WAITLIST / Dropped / Registration) no longer sink to the bottom. By `SortAge` alone they fall wherever their sort value places them. So the fix is just: **push system buckets to the bottom, preserving the existing order otherwise.**

## Where to put it

The dedicated service method **`GetAgegroupsByLeagueAsync`** ([LadtService.cs:1398-1403](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/LadtService.cs#L1398)) — its **only caller** is the grid controller ([LadtController.cs:439](../../TSIC-Core-Angular/src/backend/TSIC.API/Controllers/LadtController.cs#L439)), so nothing else is affected. Add a **stable** partition after projection; LINQ `OrderBy` is a stable sort, so the repo's `SortAge`/name order is preserved within each band.

## Use the canonical helper (do not name-sniff)

`AgegroupConstants.IsSystemBucket(string? agegroupName)` — [AgegroupConstants.cs:43](../../TSIC-Core-Angular/src/backend/TSIC.Domain/Constants/AgegroupConstants.cs#L43). It encodes WAITLIST / Dropped / Registration and is meant for in-memory use (post-`ToList()`). `using TSIC.Domain.Constants;` is already imported in LadtService.

## Suggested implementation (one line added)

```csharp
public async Task<List<AgegroupDetailDto>> GetAgegroupsByLeagueAsync(
    Guid leagueId, Guid jobId, CancellationToken cancellationToken = default)
{
    await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
    var agegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, cancellationToken);
    return agegroups
        .Select(MapAgegroup)
        .OrderBy(a => AgegroupConstants.IsSystemBucket(a.AgegroupName))  // stable: real first, system buckets last
        .ToList();
}
```

## Corrections from the first draft (why this is smaller than it looked)

- **No `Active` tier** — age-groups have no active/inactive concept; neither `Agegroups` nor `AgegroupDetailDto` has an `Active` field. The original `.ThenByDescending(a => a.Active)` would not compile.
- **No name/SortAge re-sort** — the repo already does `SortAge` then name for everyone; re-doing it in the service is redundant. Only the system-bucket partition is new.

## Risk

One added line, presentation-only, single dedicated endpoint. No schema change, no write path, repo untouched, no other consumers affected. Header-click sorting still works natively on top of this default order.

## Front-end follow-up (only if this ships)

Once the endpoint returns ordered rows, no FE change is required — the grid renders in received order. The now-obsolete FE comments at [ladt.component.ts:686-689](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/ladt.component.ts#L686) ("always sorted to the bottom…") can be updated to say the ordering is server-supplied.
