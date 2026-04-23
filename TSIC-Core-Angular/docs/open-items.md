# Open Items

Cross-cutting TODOs that don't belong to a single feature punchlist.

## Security

### Bulletin coach-link bypasses director approval
- **Seen**: STEPS 2026-04-13
- **Issue**: Adult bulletin link resolves to `role=coach`, skipping the director approval step that normally gates coach role assignment.
- **Impact**: An adult following the bulletin link becomes a coach without director review.
- **Next step**: Trace the bulletin link handler and add the same approval gate used elsewhere for coach role assignment.

## Legacy Migration

### Mobile suite parked for SuperUser migration
- **Blocker for**: SuperUser role migration
- **Unmigrated controllers** (4): `MobileJobMessages`, `MobileTeamLinks`, `MobileTeamMessages`, `MobileTeamSignups`
- **Reason parked**: SuperUser is the only role that still consumes these; migration deferred until SuperUser work kicks off.
- **Next step**: Migrate these four controllers as part of the SuperUser migration track.
