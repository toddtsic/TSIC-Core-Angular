import { Injectable, computed, inject, linkedSignal, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

import { environment } from '@environments/environment';
import { AuthService } from '@infrastructure/services/auth.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import type { UsageAnalysisScopeDto } from '@core/api';
import {
	USAGE_SCOPE_OPTIONS,
	USAGE_SCOPE_ORDER,
	USAGE_TABS,
	type UsageQuery,
	type UsageScope,
	type UsageScopeOption,
	type UsageTabDef,
} from './usage-analysis.models';

/**
 * Page-scoped state for Usage Analysis — provided by the shell component, injected by
 * every tab. Owns the three controls every tab fetches with (scope, window, bots) and
 * the resolved scope the server answered with.
 *
 * The role ceiling is mirrored here ONLY to decide what the segment offers. The server
 * enforces it: a scope above the ceiling is 403, never narrowed.
 */
@Injectable()
export class UsageAnalysisStateService {
	private readonly http = inject(HttpClient);
	private readonly auth = inject(AuthService);
	private readonly apiUrl = `${environment.apiUrl}/usage-analysis`;

	readonly role = computed(() => this.auth.currentUser()?.role ?? '');

	/** Director = job, SuperDirector = customer, Superuser = tsic. Same table as the server. */
	readonly ceiling = computed<UsageScope>(() => {
		switch (this.role()) {
			case Roles.Superuser: return 'tsic';
			case Roles.SuperDirector: return 'customer';
			default: return 'job';
		}
	});

	/** Segments the caller may pick — every scope up to the ceiling. */
	readonly scopeOptions = computed<readonly UsageScopeOption[]>(() => {
		const max = USAGE_SCOPE_ORDER.indexOf(this.ceiling());
		return USAGE_SCOPE_OPTIONS.filter(o => USAGE_SCOPE_ORDER.indexOf(o.scope) <= max);
	});

	/** Tab slots this role sees, in display order. */
	readonly tabs = computed<readonly UsageTabDef[]>(() =>
		USAGE_TABS.filter(t => t.roles.includes(this.role())));

	/**
	 * Lands on the customer view for anyone who may hold it, and on the one event for
	 * a Director. Not tsic: a Superuser opening the page wants their customer first,
	 * and the platform-wide pass is one click away. Reseeds if the role changes.
	 */
	readonly scope = linkedSignal<UsageScope, UsageScope>({
		source: this.ceiling,
		computation: ceiling => ceiling === 'tsic' ? 'customer' : ceiling,
	});

	readonly windowDays = signal<number>(7);
	readonly excludeBots = signal(true);

	/** What every tab fetches with. A tab refetches when this changes and never otherwise. */
	readonly query = computed<UsageQuery>(() => ({
		scope: this.scope(),
		windowDays: this.windowDays(),
		excludeBots: this.excludeBots(),
	}));

	// Resolved scope — null while (re)loading, so nothing on screen can claim a scope
	// it was not fetched with.
	readonly scopeInfo = signal<UsageAnalysisScopeDto | null>(null);
	readonly isLoadingScope = signal(false);
	readonly scopeError = signal<string | null>(null);

	readonly scopeLabel = computed(() =>
		USAGE_SCOPE_OPTIONS.find(o => o.scope === this.scope())?.label ?? this.scope());

	readonly jobCount = computed(() => this.scopeInfo()?.jobs.length ?? 0);

	readonly jobNames = computed(() => (this.scopeInfo()?.jobs ?? []).map(j => j.jobName));

	/** TSICLogs not configured on this server — a missing data source, not "no traffic". */
	readonly isUnavailable = computed(() => {
		const info = this.scopeInfo();
		return info !== null && !info.usageLoggingAvailable;
	});

	/**
	 * Standing in a concluded event with the job scope selected: the live-jobs rule
	 * makes the scope empty by definition. Said out loud, not shown as an empty chart.
	 */
	readonly isConcludedEvent = computed(() => {
		const info = this.scopeInfo();
		return info !== null && this.scope() === 'job' && !info.currentJobIsLive;
	});

	/** True when a tab may run: scope resolved, data source present, at least one live event. */
	readonly canQuery = computed(() =>
		this.scopeInfo() !== null && !this.isUnavailable() && !this.isConcludedEvent() && this.jobCount() > 0);

	loadScope(): void {
		this.isLoadingScope.set(true);
		this.scopeError.set(null);
		this.scopeInfo.set(null);

		const requested = this.scope();
		this.http.get<UsageAnalysisScopeDto>(`${this.apiUrl}/scope`, { params: { scope: requested } })
			.subscribe({
				next: info => {
					// A response for a scope the user has since moved off is stale; drop it.
					if (this.scope() !== requested) return;
					this.scopeInfo.set(info);
					this.isLoadingScope.set(false);
				},
				error: err => {
					if (this.scope() !== requested) return;
					this.scopeError.set(err?.status === 403
						? 'That scope is not available to your role.'
						: 'Unable to resolve the report scope.');
					this.isLoadingScope.set(false);
				},
			});
	}

	setScope(scope: UsageScope): void {
		if (this.scope() === scope) return;
		this.scope.set(scope);
		this.loadScope();
	}

	setWindow(days: number): void {
		this.windowDays.set(days);
	}

	toggleBots(): void {
		this.excludeBots.set(!this.excludeBots());
	}
}
