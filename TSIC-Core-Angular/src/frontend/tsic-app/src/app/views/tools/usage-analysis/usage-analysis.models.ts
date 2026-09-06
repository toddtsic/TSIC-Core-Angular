import { Roles } from '@infrastructure/constants/roles.constants';

/**
 * Usage analysis page — the scaffold's shared vocabulary.
 *
 * Scope words are the wire values (`UsageAnalysisScopes` on the server): the same word
 * the segment shows, the URL carries, and every tab endpoint receives. The server
 * resolves a word to a live-job set from the token and refuses anything above the
 * caller's ceiling; nothing here ever names a job or customer id.
 */
export type UsageScope = 'job' | 'customer' | 'tsic';

/** Ceiling order. A role may hold every scope up to and including its ceiling. */
export const USAGE_SCOPE_ORDER: readonly UsageScope[] = ['job', 'customer', 'tsic'];

export interface UsageScopeOption {
	readonly scope: UsageScope;
	readonly label: string;
	readonly icon: string;
	readonly description: string;
}

export const USAGE_SCOPE_OPTIONS: readonly UsageScopeOption[] = [
	{ scope: 'job',      label: 'This event', icon: 'bi-bullseye',  description: 'The event you are standing in' },
	{ scope: 'customer', label: 'Customer',   icon: 'bi-building',  description: 'Every live event of this customer' },
	{ scope: 'tsic',     label: 'All TSIC',   icon: 'bi-globe2',    description: 'Every live event on the platform' },
];

/** Windows offered by the selector. The server clamps to 1–365 regardless. */
export const USAGE_WINDOWS = [
	{ days: 1, label: '24h' },
	{ days: 7, label: '7d' },
	{ days: 30, label: '30d' },
] as const;

/** The parameters every tab fetches with. Changing any of them invalidates every tab. */
export interface UsageQuery {
	readonly scope: UsageScope;
	readonly windowDays: number;
	readonly excludeBots: boolean;
}

export type UsageTabKey = 'activity' | 'audience' | 'clients' | 'endpoints' | 'events' | 'health';

export interface UsageTabDef {
	readonly key: UsageTabKey;
	readonly label: string;
	readonly icon: string;
	/** Roles that see the tab. Nested by design: Director ⊂ SuperDirector ⊂ Superuser. */
	readonly roles: readonly string[];
	/** The question the slot is reserved for — provisional until a chart lands on it. */
	readonly hint: string;
}

const ALL_ADMINS = [Roles.Superuser, Roles.SuperDirector, Roles.Director] as const;
const CROSS_JOB = [Roles.Superuser, Roles.SuperDirector] as const;
const SUPERUSER = [Roles.Superuser] as const;

/**
 * Tab slots, in display order. Six for Superuser, five for SuperDirector, four for
 * Director. Labels and hints are provisional: a slot is claimed by replacing its
 * placeholder in the shell template with a real tab component, and renamed then.
 */
export const USAGE_TABS: readonly UsageTabDef[] = [
	{ key: 'activity',  label: 'Activity',  icon: 'bi-graph-up',        roles: ALL_ADMINS, hint: 'Requests over time' },
	{ key: 'audience',  label: 'Audience',  icon: 'bi-people',          roles: ALL_ADMINS, hint: 'Who: roles, signed-in vs anonymous' },
	{ key: 'clients',   label: 'Clients',   icon: 'bi-phone',           roles: ALL_ADMINS, hint: 'App client, platform, device, browser' },
	{ key: 'endpoints', label: 'Endpoints', icon: 'bi-diagram-3',       roles: ALL_ADMINS, hint: 'Controller and action, by volume' },
	{ key: 'events',    label: 'Events',    icon: 'bi-calendar3-range', roles: CROSS_JOB,  hint: 'Event-to-event comparison' },
	{ key: 'health',    label: 'Health',    icon: 'bi-heart-pulse',     roles: SUPERUSER,  hint: 'Status codes, app versions, bot share' },
];
