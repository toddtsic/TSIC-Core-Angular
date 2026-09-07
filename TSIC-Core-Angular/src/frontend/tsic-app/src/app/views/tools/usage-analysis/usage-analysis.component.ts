import { ChangeDetectionStrategy, Component, OnInit, computed, inject } from '@angular/core';

import { AuthService } from '@infrastructure/services/auth.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AdminNavPillComponent } from '@shared-ui/components/admin-nav-pill.component';
import { UsageAnalysisStateService } from './usage-analysis-state.service';
import { USAGE_BUCKETS, USAGE_WINDOWS, type UsageBucket, type UsageReportKey, type UsageScope } from './usage-analysis.models';
import { UsageReportDebugComponent } from './reports/usage-report-debug.component';
import { UsersByRoleComponent } from './reports/users-by-role.component';
import { PublicRequestsByRouteComponent } from './reports/public-requests-by-route.component';
import { UsersByRoleOverTimeComponent } from './reports/users-by-role-over-time.component';

/**
 * Usage Analysis — the drill behind the UsageStatsPerJob widget's glance.
 *
 * The shell owns the dropdowns (report, scope, event lens, client lens, window OR bucket)
 * and the audit stamp; an unclaimed slot is rendered by the debug component. To
 * claim one, add a case for its key in the template with a real report component that
 * injects UsageAnalysisStateService and fetches from `query()`. A report on the bucket
 * axis (03) swaps the Window dropdown for the Bucket dropdown while it is on screen: the
 * bucket is its window.
 *
 * Rules the shell encodes:
 *  - Scope is a WORD sent to the server; the job set is resolved from the token and
 *    always constrained to live jobs (ExpiryUsers > now).
 *  - The scope dropdown offers only scopes up to the role's ceiling. Director sees a
 *    static label, not a one-option dropdown.
 *  - Changing scope drops everything on screen until the server answers, so numbers
 *    never linger under a scope they were not fetched with.
 */
@Component({
	selector: 'app-usage-analysis',
	standalone: true,
	imports: [AdminNavPillComponent, UsageReportDebugComponent, UsersByRoleComponent, PublicRequestsByRouteComponent, UsersByRoleOverTimeComponent],
	providers: [UsageAnalysisStateService],
	templateUrl: './usage-analysis.component.html',
	styleUrl: './usage-analysis.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UsageAnalysisComponent implements OnInit {
	readonly state = inject(UsageAnalysisStateService);
	private readonly auth = inject(AuthService);
	private readonly pulseService = inject(JobPulseService);

	readonly windows = USAGE_WINDOWS;
	readonly buckets = USAGE_BUCKETS;

	/** Reciprocal of the widget's link out. Same pulse gate as the other dashboard doors. */
	readonly showDashboardLink = computed(() =>
		this.pulseService.pulse()?.myHasDashboardWidgets === true);

	readonly dashboardLink = computed(() =>
		['/', this.auth.currentUser()?.jobPath ?? '', 'dashboard']);

	readonly activeReport = this.state.activeReport;

	ngOnInit(): void {
		this.state.loadScope();
	}

	// Native <select> handlers. Each option value is one of the offered choices by
	// construction, so the casts narrow what the DOM already guarantees.

	onReportChange(event: Event): void {
		this.state.setReport(selected(event) as UsageReportKey);
	}

	onBucketChange(event: Event): void {
		this.state.setBucket(selected(event) as UsageBucket);
	}

	onScopeChange(event: Event): void {
		this.state.setScope(selected(event) as UsageScope);
	}

	onEventChange(event: Event): void {
		this.state.setEvent(selected(event) || null);
	}

	onClientChange(event: Event): void {
		const v = selected(event);
		this.state.setClient(v === '' ? null : Number(v));
	}

	onWindowChange(event: Event): void {
		this.state.setWindow(Number(selected(event)));
	}
}

function selected(event: Event): string {
	return (event.target as HTMLSelectElement).value;
}
