import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import type { UserRequestsByRouteDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import { orderRoles, useUsageReportFetch, type UsageTile } from './usage-report-shared';
import { useRouteReport } from './usage-route-report';

/**
 * User Requests by Route. What signed-in people did: requests with a login on them against
 * the scoped live events in the window, keyed by event, grouped by API route. The exact
 * complement of Public Requests by Route, on the same shared columns, rows and chart.
 *
 * The Role dropdown (shell) narrows the FETCH to one role, so a director's handful of
 * requests is not buried under every family registering. Its choices come from this
 * report's own answer — roles are named server-side from TSICV5 — and are handed to the
 * shell as each answer lands.
 *
 * Role is the registration's where the request carried one. A login working without one is
 * "Family" when it is a family account (the player wizard never carries a registration) and
 * "No registration" otherwise (adults self-registering).
 *
 * Alongside requests, the answer counts the distinct PEOPLE behind them — the registration,
 * or the login when there was none — shown as a tile and in the chart's tooltip, so a route
 * hammered by one person reads differently from one used by hundreds.
 */
@Component({
	selector: 'app-usage-user-requests-by-route',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './user-requests-by-route.component.html',
})
export class UserRequestsByRouteComponent {
	private readonly state = inject(UsageAnalysisStateService);

	readonly fetch = useUsageReportFetch<UserRequestsByRouteDto>('user-requests-by-route', 'Unable to load signed-in requests.', {
		roleLens: true,
		onData: d => this.state.setRoleOptions(orderRoles(new Set(d.roles.map(r => r.roleName)))),
	});

	readonly report = useRouteReport(this.fetch.data);

	/** "Directors", "Family", … — the lens in words, for tiles and messages. */
	private readonly who = computed(() => {
		const role = this.fetch.data()?.role;
		return role ? `${role} requests` : 'Signed-in requests';
	});

	readonly tiles = computed<readonly UsageTile[]>(() => {
		const d = this.fetch.data();
		const word = this.state.jobCount() === 1 ? 'this event' : `${this.state.jobCount()} live events`;
		return [
			{ value: d?.totalRequests ?? 0, label: `${this.who()} on ${word}`, primary: true },
			{ value: d?.totalPeople ?? 0, label: 'People who made them' },
			{ value: d?.failedRequests ?? 0, label: 'Failed (4xx / 5xx), not counted below' },
		];
	});

	readonly chartSubtitle = computed(() => `${this.who().toLowerCase()} by API route, succeeded only`);
}
