// Centralized role name constants to avoid magic strings in the client.
// These correspond to RoleConstants.Names.* on the server (name, not GUID).
export const Roles = {
    Superuser: 'Superuser',
    Director: 'Director',
    SuperDirector: 'SuperDirector',
    RefAssignor: 'Ref Assignor',
    StoreAdmin: 'Store Admin',
    Staff: 'Staff',
    Family: 'Family',
    Player: 'Player',
    UnassignedAdult: 'Unassigned Adult',
    ClubRep: 'Club Rep'
} as const;

export type RoleName = typeof Roles[keyof typeof Roles];

// Role IDs (GUIDs) — the single client-side source, mirroring RoleConstants.* on the server.
// These are the stable identifiers carried in DTOs and search-filter values; prefer them over the
// display name for any equality check. Compare case-insensitively — backend GUID casing varies.
export const RoleIds = {
    Player: 'DAC0C570-94AA-4A88-8D73-6034F1F72F3A',
    ClubRep: '6A26171F-4D94-4928-94FA-2FEFD42C3C3E'
} as const;

// Synthetic role-filter sentinels — mirror RoleConstants.{PlayerNotWaitlisted,ClubRepActiveTeams}.
// Not real role IDs; the registration search emits them as extra role-filter options
// ("Player (NOT WAITLISTED)", "Club Rep (ACTIVE, NOT WAITLISTED)") whose `value` is the sentinel.
export const RoleFilterSentinels = {
    PlayerNotWaitlisted: 'PLAYER_NOT_WAITLISTED',
    ClubRepActiveTeams: 'CLUBREP_ACTIVE_TEAMS'
} as const;

// Helper predicates (tree‑shake friendly if using build optimizations)
export const isTeamMember = (roles: RoleName[]) => roles.some(r => r === Roles.Staff || r === Roles.Family || r === Roles.Player);
export const STORE_ELIGIBLE_ROLES: ReadonlySet<RoleName> = new Set([Roles.Family, Roles.Player]);
export const isStoreEligible = (role?: string) => !!role && STORE_ELIGIBLE_ROLES.has(role as RoleName);
