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

// Helper predicates (tree‑shake friendly if using build optimizations)
export const isAdminish = (roles: RoleName[]) => roles.some(r => r === Roles.Superuser || r === Roles.Director || r === Roles.SuperDirector);
export const isTeamMember = (roles: RoleName[]) => roles.some(r => r === Roles.Staff || r === Roles.Family || r === Roles.Player);
export const STORE_ELIGIBLE_ROLES: ReadonlySet<RoleName> = new Set([Roles.Family, Roles.Player]);
export const isStoreEligible = (role?: string) => !!role && STORE_ELIGIBLE_ROLES.has(role as RoleName);
