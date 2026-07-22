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

## Required ordering (default, ascending)

1. **Real playing age-groups first; system/holding buckets last.** System bucket = WAITLIST / Dropped / Registration.
2. **Active before inactive.**
3. Within each band, **alphabetical by `AgegroupName`**.

This restores the behavior the front end still documents at [ladt.component.ts:686-689](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/ladt.component.ts#L686) and mirrors the tree's own sort at [ladt.component.ts:350-357](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/ladt.component.ts#L350).

## Where to put it — and where NOT to

Put the ordering in the **service method `GetAgegroupsByLeagueAsync`**, applied **in-memory after projection** ([LadtService.cs:1402](../../TSIC-Core-Angular/src/backend/TSIC.API/Services/Admin/LadtService.cs#L1402), the `.Select(MapAgegroup).ToList()`).

**Do NOT** put it in the repo `GetByLeagueIdAsync` — it is **shared by 6 callers** (LadtService lines 84, 362, 830, 1006, 1398, 1600). Ordering there would ripple to consumers that don't want it. The service method above is dedicated to this grid endpoint.

## Use the canonical helper (do not name-sniff)

`AgegroupConstants.IsSystemBucket(string? agegroupName)` — [AgegroupConstants.cs:43](../../TSIC-Core-Angular/src/backend/TSIC.Domain/Constants/AgegroupConstants.cs#L43). It already encodes WAITLIST / Dropped / Registration and is explicitly meant for **in-memory** use (its doc-comment notes EF can't translate it to SQL — which is fine here, we order after `ToList()`).

Note: the FE's ad-hoc check only looked at WAITLIST + Dropped; the canonical helper also includes **Registration**, which is correct — use the helper.

## Suggested implementation

```csharp
public async Task<List<AgegroupDetailDto>> GetAgegroupsByLeagueAsync(
    Guid leagueId, Guid jobId, CancellationToken cancellationToken = default)
{
    await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
    var agegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, cancellationToken);
    return agegroups
        .Select(MapAgegroup)
        .OrderBy(a => AgegroupConstants.IsSystemBucket(a.AgegroupName))  // false (real) before true (system)
        .ThenByDescending(a => a.Active)                                 // active before inactive
        .ThenBy(a => a.AgegroupName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
```

## Precondition to confirm

`AgegroupDetailDto` must expose **`AgegroupName`** and **`Active`** for the sort keys (the grid already renders both, so this is expected — confirm `MapAgegroup` carries them).

## Risk

Additive, presentation-only, single dedicated endpoint. No schema change, no write path, no other consumers touched. Header-click sorting in the grid still works natively on top of this default order.

## Front-end follow-up (only if this ships)

Once the endpoint returns ordered rows, no FE change is required — the grid renders in received order. The now-obsolete FE comments at [ladt.component.ts:686-689](../../TSIC-Core-Angular/src/frontend/tsic-app/src/app/views/ladt/editor/ladt.component.ts#L686) ("always sorted to the bottom…") can be updated to say the ordering is server-supplied.
