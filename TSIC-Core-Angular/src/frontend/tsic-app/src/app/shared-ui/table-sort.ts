import { computed, signal, type Signal } from '@angular/core';

export type SortDir = 'asc' | 'desc';

/**
 * Column-heading sort state for a plain `<table>`.
 *
 * Extracted from `AccountingLedgerComponent`, which owns the original of this pattern and keeps
 * its own copy — it carries bucket rules (active vs waitlist-dropped never merge) that do not
 * generalise. This helper is the state machine only: which column, which direction, and the
 * three accessors a heading needs. The comparator stays with the table, because only the table
 * knows what its columns mean.
 *
 * Usage in a component:
 *
 *     private readonly rows = signal<Row[]>([]);
 *     readonly sort = tableSort<'name' | 'paid'>('name', { paid: 'desc' });
 *     readonly sortedRows = this.sort.applyTo(this.rows, (col, a, b) =>
 *         col === 'paid' ? a.paid - b.paid : a.name.localeCompare(b.name));
 *
 * and in the template:
 *
 *     <th [attr.aria-sort]="sort.aria('name')">
 *       <button type="button" class="sort-th" (click)="sort.toggle('name')" title="Sort by name">
 *         Name <i class="bi" [class]="sort.icon('name')" [class.active]="sort.column() === 'name'"></i>
 *       </button>
 *     </th>
 */
export interface TableSort<C extends string> {
	readonly column: Signal<C>;
	readonly dir: Signal<SortDir>;
	/** Same column flips direction; a new column adopts its natural default. */
	toggle(col: C): void;
	/**
	 * Sort on `col` outright, without the flip. For a control OUTSIDE the header row that picks
	 * an order — an order selector above the table — so it drives the same state the headings do
	 * rather than becoming a second, competing one.
	 */
	set(col: C, dir?: SortDir): void;
	/** `aria-sort` for a heading — 'none' unless it is the active column. */
	aria(col: C): 'ascending' | 'descending' | 'none';
	/** Caret class. Inactive carets stay in the DOM (dimmed by CSS) so headings do not reflow. */
	icon(col: C): string;
	/**
	 * Sorted view of `rows`. `compare` is written in ASCENDING terms for every column; direction
	 * is applied here, so a comparator never has to know which way the caret points. Returns a
	 * new array — the source signal is never mutated.
	 */
	applyTo<T>(rows: Signal<readonly T[]>, compare: (col: C, a: T, b: T) => number): Signal<T[]>;
}

/**
 * @param initial      column sorted on first render
 * @param defaultDirs  per-column natural direction; omitted columns open ascending. Money, counts
 *                     and dates generally want 'desc' — the largest or most recent first is what
 *                     someone clicking that heading is looking for.
 */
export function tableSort<C extends string>(
	initial: C,
	defaultDirs: Partial<Record<C, SortDir>> = {}
): TableSort<C> {
	const column = signal<C>(initial);
	const dir = signal<SortDir>(defaultDirs[initial] ?? 'asc');

	return {
		column: column.asReadonly(),
		dir: dir.asReadonly(),

		toggle(col: C): void {
			if (column() === col) {
				dir.set(dir() === 'asc' ? 'desc' : 'asc');
				return;
			}
			column.set(col);
			dir.set(defaultDirs[col] ?? 'asc');
		},

		set(col: C, explicit?: SortDir): void {
			column.set(col);
			dir.set(explicit ?? defaultDirs[col] ?? 'asc');
		},

		aria(col: C) {
			if (column() !== col) return 'none';
			return dir() === 'asc' ? 'ascending' : 'descending';
		},

		icon(col: C): string {
			if (column() !== col) return 'bi-arrow-down-up';
			return dir() === 'asc' ? 'bi-sort-down-alt' : 'bi-sort-down';
		},

		applyTo<T>(rows: Signal<readonly T[]>, compare: (col: C, a: T, b: T) => number): Signal<T[]> {
			return computed(() => {
				const col = column();
				const sign = dir() === 'asc' ? 1 : -1;
				return [...rows()].sort((a, b) => compare(col, a, b) * sign);
			});
		}
	};
}

/**
 * Epoch for a date-ish value, for use inside a comparator. Absent and unparseable dates sort
 * LAST in both directions rather than clumping at one end as a fake "oldest".
 */
export function dateKey(value: string | Date | null | undefined): number {
	if (!value) return Number.NEGATIVE_INFINITY;
	const t = value instanceof Date ? value.getTime() : Date.parse(value);
	return Number.isNaN(t) ? Number.NEGATIVE_INFINITY : t;
}

/** Case- and accent-insensitive text comparison, with embedded numbers ordered numerically. */
export function textKey(a: string | null | undefined, b: string | null | undefined): number {
	return (a ?? '').localeCompare(b ?? '', undefined, { numeric: true, sensitivity: 'base' });
}
