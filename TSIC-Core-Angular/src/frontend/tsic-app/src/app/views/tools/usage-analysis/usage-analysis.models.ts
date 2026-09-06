import { Roles } from '@infrastructure/constants/roles.constants';

/**
 * Usage analysis page — the scaffold's shared vocabulary.
 *
 * Scope words are the wire values (`UsageAnalysisScopes` on the server): the same word
 * the dropdown shows, the URL carries, and every report endpoint receives. The server
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

/** The parameters every report fetches with. Changing any of them invalidates every report. */
export interface UsageQuery {
	readonly scope: UsageScope;
	readonly windowDays: number;
	readonly excludeBots: boolean;
	/** The event lens: one live job id inside the scope, or null for all of them. */
	readonly eventId: string | null;
}

export type UsageReportKey = 'report-01' | 'report-02' | 'report-03' | 'report-04' | 'report-05' | 'report-06';

export interface UsageReportDef {
	readonly key: UsageReportKey;
	readonly label: string;
	/** Roles that see the report. Nested by design: Director ⊂ SuperDirector ⊂ Superuser. */
	readonly roles: readonly string[];
}

const ALL_ADMINS = [Roles.Superuser, Roles.SuperDirector, Roles.Director] as const;
const CROSS_JOB = [Roles.Superuser, Roles.SuperDirector] as const;
const SUPERUSER = [Roles.Superuser] as const;

/**
 * DUMMY report slots, in dropdown order: four for a Director, five for a SuperDirector,
 * six for a Superuser. Every slot is the same report; what differs is the scope it is
 * queried with. Names are placeholders until the role/scope plumbing is trusted.
 */
export const USAGE_REPORTS: readonly UsageReportDef[] = [
	{ key: 'report-01', label: '01 · Users by Role', roles: ALL_ADMINS },
	{ key: 'report-02', label: 'Report-02', roles: ALL_ADMINS },
	{ key: 'report-03', label: 'Report-03', roles: ALL_ADMINS },
	{ key: 'report-04', label: 'Report-04', roles: ALL_ADMINS },
	{ key: 'report-05', label: 'Report-05', roles: CROSS_JOB },
	{ key: 'report-06', label: 'Report-06', roles: SUPERUSER },
];
