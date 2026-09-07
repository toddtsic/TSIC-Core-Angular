import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import type { SeriesModel } from '@syncfusion/ej2-angular-charts';

import type { UsersByRoleDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import {
	DIRECTOR_ROLE,
	allRowName,
	categoryAxis,
	chartRowFor,
	countAxis,
	orderRoles,
	outerDataLabel,
	resolveChartTheme,
	resolvePalette,
	useUsageReportFetch,
	type UsagePivotRow,
	type UsageTile,
} from './usage-report-shared';

/**
 * Report 01 — Users by Role. Distinct registrations that used the scoped live events in
 * the window, keyed by event, grouped by role. Columns are roles; the chart is one
 * column per role for the Event dropdown's pick, or the scope rolled up at All.
 *
 * The unit is the REGISTRATION, which is per event: a user in two events is two
 * registrations and counts in each. The All rollup dedups only a registration that made
 * requests about several events (mostly Superusers working across jobs).
 *
 * People only. Anonymous traffic is requests, not people, and has its own report (02).
 */
@Component({
	selector: 'app-usage-users-by-role',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './users-by-role.component.html',
})
export class UsersByRoleComponent {
	private readonly state = inject(UsageAnalysisStateService);
	readonly fetch = useUsageReportFetch<UsersByRoleDto>('users-by-role', 'Unable to load users by role.');

	private readonly palette = resolvePalette();
	private readonly theme = resolveChartTheme();

	readonly isEmpty = computed(() => (this.fetch.data()?.rows.length ?? 0) === 0);

	/** Every role present anywhere in the scope, in ROLE_ORDER then alphabetical. */
	readonly columns = computed<readonly string[]>(() =>
		orderRoles(new Set((this.fetch.data()?.rows ?? []).map(r => r.roleName))));

	/** Every event in the answer, busiest first. */
	readonly rows = computed<readonly UsagePivotRow[]>(() => {
		const byJob = new Map<string, { name: string; total: number; cells: Map<string, number> }>();
		for (const r of this.fetch.data()?.rows ?? []) {
			let e = byJob.get(r.jobId);
			if (!e) {
				e = { name: r.jobName, total: 0, cells: new Map() };
				byJob.set(r.jobId, e);
			}
			// A registration holds ONE role, so summing role counts within an event is still distinct registrations.
			e.total += r.users;
			e.cells.set(r.roleName, (e.cells.get(r.roleName) ?? 0) + r.users);
		}
		return [...byJob.entries()]
			.map(([id, e]) => ({ id, ...e }))
			.sort((a, b) => b.total - a.total || a.name.localeCompare(b.name));
	});

	/**
	 * The whole scope rolled up, from the server's distinct-per-role totals: distinct
	 * REGISTRATIONS per role and overall. Close to the column sums; differs only where one
	 * registration touched several events.
	 */
	readonly allRow = computed<UsagePivotRow | null>(() => {
		const d = this.fetch.data();
		if (!d) return null;
		const cells = new Map<string, number>();
		for (const t of d.totals) cells.set(t.roleName, (cells.get(t.roleName) ?? 0) + t.users);
		// The two server totals are each distinct and a registration is in exactly one tier, so their sum is distinct too.
		return { id: '', name: allRowName(this.state.jobCount(), this.rows()), total: d.customerUsers + d.adminUsers, cells };
	});

	readonly tiles = computed<readonly UsageTile[]>(() => {
		const word = this.state.jobCount() === 1 ? 'this event' : `${this.state.jobCount()} live events`;
		return [
			{ value: this.allRow()?.total ?? 0, label: `People using ${word}`, primary: true },
			{ value: this.allRow()?.cells.get(DIRECTOR_ROLE) ?? 0, label: `Directors using ${word}` },
		];
	});

	readonly chartRow = computed(() => chartRowFor(this.state.eventId(), this.rows(), this.allRow()));

	readonly chartSubtitle = computed(() => this.state.eventId() === null && this.state.showEventPicker()
		? 'counted by registration — a registration is per event, so a user in two events counts in each'
		: '');

	/** Roles present in the charted row, in column order. */
	private readonly chartColumns = computed(() => {
		const row = this.chartRow();
		return row ? this.columns().filter(c => (row.cells.get(c) ?? 0) > 0) : [];
	});

	/** One column series per role over a single category point, so the cluster is one event. */
	readonly chartSeries = computed<SeriesModel[]>(() => {
		const row = this.chartRow();
		if (!row) return [];
		const point: Record<string, string | number> = { x: row.name };
		this.chartColumns().forEach((role, i) => { point['r' + i] = row.cells.get(role) ?? 0; });
		const data = [point];
		const columns = this.columns();
		return this.chartColumns().map((role, i) => ({
			type: 'Column',
			dataSource: data,
			xName: 'x',
			yName: 'r' + i,
			name: role,
			// Colour by the role's position in the SCOPE-wide order, so Player is the same colour whichever event is charted.
			fill: this.palette[columns.indexOf(role) % this.palette.length],
			opacity: 0.85,
			// Fixed width: a proportional width lets one lone column fill the whole band.
			columnWidthInPixel: 36,
			cornerRadius: { topLeft: 3, topRight: 3 },
			marker: outerDataLabel(this.theme),
		}));
	});

	private readonly maxCell = computed(() => {
		const row = this.chartRow();
		return row ? Math.max(0, ...row.cells.values()) : 0;
	});

	readonly primaryXAxis = computed(() => categoryAxis(this.theme));
	readonly primaryYAxis = computed(() => countAxis(this.theme, this.maxCell()));
	readonly legendSettings = {
		visible: true,
		position: 'Top' as const,
		alignment: 'Far' as const,
		textStyle: { size: '11px', fontFamily: this.theme.fontFamily },
		padding: 4,
		margin: { top: 0, bottom: 4, left: 0, right: 0 },
	};
	readonly tooltip = { enable: true, shared: true };
}
