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

/**
 * The time unit a bucketed report groups by. The wire word the server parses. A bucket is
 * the grouping AND the window (Todd, 2026-09-07): the server owns each bucket's span, and
 * the description here only says it out loud in the dropdown.
 */
export type UsageBucket = 'day' | 'week' | 'month';

export const USAGE_BUCKETS: readonly { readonly bucket: UsageBucket; readonly label: string; readonly span: string }[] = [
	{ bucket: 'day',   label: 'Daily',   span: 'last 30 days' },
	{ bucket: 'week',  label: 'Weekly',  span: 'last 12 weeks' },
	{ bucket: 'month', label: 'Monthly', span: 'last 12 months' },
];

/** Which control sets a report's time span: the Window dropdown, or the Bucket dropdown that stands in for it. */
export type UsageTimeAxis = 'window' | 'bucket';

/** The parameters every report fetches with. Changing any of them invalidates every report. */
export interface UsageQuery {
	readonly scope: UsageScope;
	readonly windowDays: number;
	/** The bucket a bucketed report groups by. Ignored by reports on the window axis. */
	readonly bucket: UsageBucket;
	/** The event lens: one live job id inside the scope, or null for all of them. */
	readonly eventId: string | null;
	/** The client lens: a logs.AppClients id offered by the facet, or null for every client. */
	readonly clientId: number | null;
	/** The role lens: a role name the report on screen offered, or null for every role. Read only by a report with `roleLens`. */
	readonly role: string | null;
}

/**
 * Who a report is about. The dropdown groups by this (Todd, 2026-09-16): people signed in,
 * or the public. Every request lands in exactly one — signed-in means a login on the request.
 */
export type UsageReportGroup = 'users' | 'public';

export const USAGE_REPORT_GROUPS: readonly { readonly group: UsageReportGroup; readonly label: string }[] = [
	{ group: 'users', label: 'Signed-in users' },
	{ group: 'public', label: 'Public (not signed in)' },
];

export type UsageReportKey = 'report-01' | 'report-02' | 'report-03' | 'report-04' | 'report-05' | 'report-06';

export interface UsageReportDef {
	readonly key: UsageReportKey;
	readonly label: string;
	/** The dropdown group the report sits under. */
	readonly group: UsageReportGroup;
	/** True when the report offers the Role dropdown (its roles come from its own answer). */
	readonly roleLens?: boolean;
	/** Roles that see the report. Nested by design: Director ⊂ SuperDirector ⊂ Superuser. */
	readonly roles: readonly string[];
	/** False for a reserved slot with no report behind it yet. Hidden from the dropdown; the key stays reserved. */
	readonly built: boolean;
	/** 'bucket' swaps the Window dropdown for the Bucket dropdown while the report is on screen. */
	readonly timeAxis: UsageTimeAxis;
}

const ALL_ADMINS = [Roles.Superuser, Roles.SuperDirector, Roles.Director] as const;
const CROSS_JOB = [Roles.Superuser, Roles.SuperDirector] as const;
const SUPERUSER = [Roles.Superuser] as const;

/**
 * Report slots, in dropdown order within each group. Only BUILT reports are offered (Todd,
 * 2026-09-06: hide the placeholders so the page can be published). Labels carry no numbers
 * (Todd, 2026-09-16): the group says who a report is about. Keys are internal slot ids and
 * never shown, so a key's number says nothing about where the report sits.
 */
export const USAGE_REPORTS: readonly UsageReportDef[] = [
	{ key: 'report-01', label: 'Users by Role', group: 'users', roles: ALL_ADMINS, built: true, timeAxis: 'window' },
	{ key: 'report-03', label: 'Users by Role over Time', group: 'users', roles: ALL_ADMINS, built: true, timeAxis: 'bucket' },
	{ key: 'report-04', label: 'User Requests by Route', group: 'users', roleLens: true, roles: ALL_ADMINS, built: true, timeAxis: 'window' },
	{ key: 'report-02', label: 'Public Requests by Route', group: 'public', roles: ALL_ADMINS, built: true, timeAxis: 'window' },
	{ key: 'report-05', label: 'Report-05', group: 'users', roles: CROSS_JOB, built: false, timeAxis: 'window' },
	{ key: 'report-06', label: 'Report-06', group: 'users', roles: SUPERUSER, built: false, timeAxis: 'window' },
];
