import { DestroyRef, type Signal, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { distinctUntilChanged, filter, map, switchMap } from 'rxjs/operators';
import { catchError, of } from 'rxjs';

import { environment } from '@environments/environment';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import type { UsageBucket, UsageTimeAxis } from '../usage-analysis.models';

/**
 * Shared by every usage report: the pivot row shape the layout renders, the fetch
 * plumbing every report runs, and the palette helpers the charts colour with.
 */

/** One table row: an event, the All rollup (id empty), or a time bucket. Cells are keyed by column. */
export interface UsagePivotRow {
	readonly id: string;
	readonly name: string;
	/** The Total column. */
	readonly total: number;
	readonly cells: ReadonlyMap<string, number>;
	/** True for a bucket the log did not exist for yet: not zero, no data. Rendered as such. */
	readonly noData?: boolean;
}

/** A headline number above the chart. */
export interface UsageTile {
	readonly value: number;
	readonly label: string;
	readonly primary?: boolean;
}

/**
 * Every role by its exact name, in a FIXED order, so a role keeps its colour and its
 * column position in every chart, every window, and every report: the customer's
 * people first, then the roles that run an event. No bucket — how much Directors are
 * using is vital information (Todd, 2026-09-06), and "Staff" is itself a role. Roles not
 * listed follow alphabetically.
 */
export const ROLE_ORDER: readonly string[] = [
	'Family', 'Player', 'Staff', 'Club Rep', 'Unassigned Adult', 'Referee', 'Scorer', 'Recruiter', 'Guest',
	'Director', 'SuperDirector', 'Superuser', 'Ref Assignor', 'Store Admin', 'STPAdmin', 'ApiAuthorized',
];

/** The role whose usage gets its own tile. Exact AspNetRoles name. */
export const DIRECTOR_ROLE = 'Director';

/** The roles present, in ROLE_ORDER then alphabetical — the column order every role report shares. */
export function orderRoles(present: ReadonlySet<string>): readonly string[] {
	const ordered = ROLE_ORDER.filter(r => present.has(r));
	const rest = [...present].filter(r => !ROLE_ORDER.includes(r)).sort((a, b) => a.localeCompare(b));
	return [...ordered, ...rest];
}

/** Read a CSS custom property from :root, with fallback. */
export function cssVar(v: string, fallback: string): string {
	return getComputedStyle(document.documentElement).getPropertyValue(v)?.trim() || fallback;
}

/** Series palette by column position. Palette-responsive: resolved from :root when called. */
const PALETTE_VARS: readonly [string, string][] = [
	['--bs-primary', '#0d6efd'],
	['--brand-accent', '#6f42c1'],
	['--bs-success', '#198754'],
	['--bs-warning', '#ffc107'],
	['--bs-info', '#0dcaf0'],
	['--bs-danger', '#dc3545'],
	['--bs-secondary', '#6c757d'],
	['--bs-indigo', '#6610f2'],
	['--bs-pink', '#d63384'],
	['--bs-orange', '#fd7e14'],
	['--bs-teal', '#20c997'],
	['--bs-dark', '#212529'],
];

export function resolvePalette(): readonly string[] {
	return PALETTE_VARS.map(([v, fb]) => cssVar(v, fb));
}

/** Chart text defaults, resolved once so the chart never receives post-init property changes. */
export interface UsageChartTheme {
	readonly muted: string;
	readonly border: string;
	readonly text: string;
	/** The chart card's background — what a hollow marker is filled with. */
	readonly surface: string;
	readonly fontFamily: string;
}

export function resolveChartTheme(): UsageChartTheme {
	return {
		muted: cssVar('--brand-text-muted', '#6c757d'),
		border: cssVar('--brand-border', 'rgba(0,0,0,0.1)'),
		text: cssVar('--brand-text', '#212529'),
		surface: cssVar('--brand-surface', '#ffffff'),
		// ej2 draws SVG text in its own default face; hand it the page font so the axes match the table.
		fontFamily: cssVar('--bs-body-font-family', 'system-ui, sans-serif'),
	};
}

/** Data label above (or past the end of) each bar. A short bar cannot carry its count inside. */
export function outerDataLabel(theme: UsageChartTheme) {
	return {
		dataLabel: {
			visible: true,
			position: 'Outer' as const,
			format: 'n0',
			font: { color: theme.text, size: '11px', fontWeight: '600', fontFamily: theme.fontFamily },
		},
	};
}

/** Category axis with no chrome. */
export function categoryAxis(theme: UsageChartTheme, extra: Record<string, unknown> = {}) {
	return {
		valueType: 'Category' as const,
		majorGridLines: { width: 0 },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: theme.muted, size: '11px', fontFamily: theme.fontFamily },
		...extra,
	};
}

/**
 * Whole-number value axis. Steps by 1 while the tallest bar is small; lets ej2 pick above
 * that, with the n0 format so it never labels 1.2 of anything.
 */
export function countAxis(theme: UsageChartTheme, max: number) {
	return {
		title: '',
		majorGridLines: { width: 0.5, color: theme.border, dashArray: '3,3' },
		majorTickLines: { width: 0 },
		lineStyle: { width: 0 },
		labelStyle: { color: theme.muted, size: '11px', fontFamily: theme.fontFamily },
		minimum: 0,
		interval: max <= 10 ? 1 : undefined,
		labelFormat: 'n0',
	};
}

/**
 * The row the chart draws: the lens event, or the All rollup. Null when a lens is set but
 * the event had no rows.
 */
export function chartRowFor(
	eventId: string | null,
	rows: readonly UsagePivotRow[],
	allRow: UsagePivotRow | null,
): UsagePivotRow | null {
	if (!eventId) return allRow;
	const id = eventId.toLowerCase();
	return rows.find(r => r.id.toLowerCase() === id) ?? null;
}

/** The label for the All rollup: the one event's name when there is only one, else the count. */
export function allRowName(jobCount: number, rows: readonly UsagePivotRow[]): string {
	return jobCount === 1 ? (rows[0]?.name ?? 'This event') : `All ${jobCount} live events`;
}

/**
 * What a report fetches with. The time span is the window OR the bucket, per the report's
 * axis. The event lens is in it only for a report whose rows are not events (03): a
 * per-event table is always the whole scope and the lens just moves the chart, but a
 * per-bucket table has nowhere to keep the other events, so the lens must narrow the fetch.
 */
interface FetchKey {
	readonly scope: string;
	readonly windowDays: number | null;
	readonly bucket: UsageBucket | null;
	readonly eventId: string | null;
	readonly clientId: number | null;
}

export interface UsageReportFetch<T> {
	readonly data: Signal<T | null>;
	readonly isLoading: Signal<boolean>;
	readonly error: Signal<string | null>;
}

export interface UsageReportFetchOptions {
	/** Which control spans the report: 'window' (default) or 'bucket'. */
	readonly timeAxis?: UsageTimeAxis;
	/** True when the event lens must narrow the FETCH, not just the chart — rows are not events. */
	readonly lensNarrowsFetch?: boolean;
}

/**
 * The fetch every report runs. Call from a field initializer or constructor (it injects).
 *
 * Refetches when scope, the time span, bots or client change and never otherwise; by
 * default the event lens only moves the chart and the highlight, both derived from the
 * same scope-wide answer. Emits null (clearing the report) whenever the scope is
 * unresolved or empty, so nothing lingers under a scope it was not fetched with.
 */
export function useUsageReportFetch<T>(
	endpoint: string,
	failureMessage: string,
	options: UsageReportFetchOptions = {},
): UsageReportFetch<T> {
	const http = inject(HttpClient);
	const destroyRef = inject(DestroyRef);
	const state = inject(UsageAnalysisStateService);

	const data = signal<T | null>(null);
	const isLoading = signal(false);
	const error = signal<string | null>(null);

	const bucketed = options.timeAxis === 'bucket';

	const fetchKey = computed<FetchKey | null>(() => {
		if (!state.canQuery()) return null;
		const q = state.query();
		return {
			scope: q.scope,
			windowDays: bucketed ? null : q.windowDays,
			bucket: bucketed ? q.bucket : null,
			eventId: options.lensNarrowsFetch ? q.eventId : null,
			clientId: q.clientId,
		};
	});

	toObservable(fetchKey)
		.pipe(
			map(q => q ? JSON.stringify(q) : null),
			distinctUntilChanged(),
			map(key => key ? (JSON.parse(key) as FetchKey) : null),
			filter((q): q is FetchKey => {
				if (q) return true;
				data.set(null);
				return false;
			}),
			switchMap(q => {
				isLoading.set(true);
				error.set(null);
				const params: Record<string, string | number | boolean> = { scope: q.scope };
				if (q.windowDays !== null) params['windowDays'] = q.windowDays;
				if (q.bucket !== null) params['bucket'] = q.bucket;
				if (q.eventId !== null) params['eventId'] = q.eventId;
				if (q.clientId !== null) params['clientId'] = q.clientId;
				return http.get<T>(`${environment.apiUrl}/usage-analysis/${endpoint}`, { params }).pipe(
					catchError(err => {
						error.set(err?.status === 403 ? 'That scope is not available to your role.' : failureMessage);
						return of(null);
					}),
				);
			}),
			takeUntilDestroyed(destroyRef),
		)
		.subscribe(d => {
			data.set(d);
			isLoading.set(false);
		});

	return { data, isLoading, error };
}
