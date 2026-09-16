import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';

import type { PublicRequestsByRouteDto } from '@core/api';
import { UsageAnalysisStateService } from '../usage-analysis-state.service';
import { UsagePivotReportComponent } from './usage-pivot-report.component';
import { useUsageReportFetch, type UsageTile } from './usage-report-shared';
import { useRouteReport } from './usage-route-report';

/**
 * Public Requests by Route. What the public asked for without signing in: requests with no
 * login on them against the scoped live events in the window, keyed by event, grouped by
 * the API route they hit. The exact complement of User Requests by Route. Columns, rows and
 * chart come from `useRouteReport`, shared with that report.
 *
 * Same layout as Users by Role so the eye learns once — but a different UNIT. That report
 * counts registrations; this counts requests, because nothing in the log can turn an
 * anonymous request into a visitor and the report never pretends otherwise.
 *
 * Routes are Controller/Action — the API endpoint, not the page. One public page fires
 * several endpoints, and every page load fires the chrome (job metadata, theme, nav)
 * signed in or not. Shown raw on purpose until the data has been looked at; an
 * exclusion list or a content-only toggle is a decision for after that.
 */
@Component({
	selector: 'app-usage-public-requests-by-route',
	standalone: true,
	imports: [UsagePivotReportComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './public-requests-by-route.component.html',
})
export class PublicRequestsByRouteComponent {
	private readonly state = inject(UsageAnalysisStateService);
	readonly fetch = useUsageReportFetch<PublicRequestsByRouteDto>('public-requests-by-route', 'Unable to load public requests.');
	readonly report = useRouteReport(this.fetch.data);

	readonly tiles = computed<readonly UsageTile[]>(() => {
		const d = this.fetch.data();
		const word = this.state.jobCount() === 1 ? 'this event' : `${this.state.jobCount()} live events`;
		return [
			{ value: d?.totalRequests ?? 0, label: `Public requests on ${word}`, primary: true },
			{ value: d?.failedRequests ?? 0, label: 'Failed (4xx / 5xx), not counted below' },
		];
	});
}
