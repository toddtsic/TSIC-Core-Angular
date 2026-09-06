import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';

import { AuthService } from '@infrastructure/services/auth.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AdminNavPillComponent } from '@shared-ui/components/admin-nav-pill.component';
import { UsageAnalysisStateService } from './usage-analysis-state.service';
import { USAGE_WINDOWS, type UsageScope, type UsageTabKey } from './usage-analysis.models';
import { UsageTabPlaceholderComponent } from './tabs/usage-tab-placeholder.component';

/**
 * Usage Analysis — the drill behind the UsageStatsPerJob widget's glance.
 *
 * SCAFFOLD. The shell owns the controls (scope segment, window, bots), the audit
 * stamp, and the tab strip; every tab is an unclaimed slot. To claim one, replace its
 * placeholder in the template with a real tab component that injects
 * UsageAnalysisStateService and fetches from `query()`.
 *
 * Rules the shell encodes:
 *  - Scope is a WORD sent to the server; the job set is resolved from the token and
 *    always constrained to live jobs (ExpiryUsers > now).
 *  - The segment shows only scopes up to the role's ceiling. Director sees no segment.
 *  - Changing scope drops everything on screen until the server answers, so numbers
 *    never linger under a scope they were not fetched with.
 */
@Component({
	selector: 'app-usage-analysis',
	standalone: true,
	imports: [AdminNavPillComponent, UsageTabPlaceholderComponent],
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

	/** Reciprocal of the widget's link out. Same pulse gate as the other dashboard doors. */
	readonly showDashboardLink = computed(() =>
		this.pulseService.pulse()?.myHasDashboardWidgets === true);

	readonly dashboardLink = computed(() =>
		['/', this.auth.currentUser()?.jobPath ?? '', 'dashboard']);

	private readonly requestedTab = signal<UsageTabKey>('activity');

	/** The requested tab if this role has it, else the first slot — never an empty pane. */
	readonly activeTab = computed(() => {
		const tabs = this.state.tabs();
		return tabs.find(t => t.key === this.requestedTab()) ?? tabs[0];
	});

	readonly windowLabel = computed(() =>
		this.state.windowDays() === 1 ? '24h' : `${this.state.windowDays()}d`);

	ngOnInit(): void {
		this.state.loadScope();
	}

	setTab(key: UsageTabKey): void {
		this.requestedTab.set(key);
	}

	setScope(scope: UsageScope): void {
		this.state.setScope(scope);
	}
}
