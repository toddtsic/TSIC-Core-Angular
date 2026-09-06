import { DestroyRef, Injectable, computed, inject, linkedSignal, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { of } from 'rxjs';
import { catchError, distinctUntilChanged, map, switchMap } from 'rxjs/operators';

import { environment } from '@environments/environment';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import type { UsageAnalysisScopeDto, UsageClientFacetDto, UsageClientsDto } from '@core/api';
import {
	USAGE_SCOPE_OPTIONS,
	USAGE_SCOPE_ORDER,
	USAGE_REPORTS,
	type UsageQuery,
	type UsageScope,
	type UsageScopeOption,
	type UsageReportDef,
} from './usage-analysis.models';

/** Everything the client facet depends on: the query minus the client itself. */
interface FacetQuery {
	readonly scope: UsageScope;
	readonly windowDays: number;
	readonly excludeBots: boolean;
	readonly eventId: string | null;
}

/**
 * Page-scoped state for Usage Analysis — provided by the shell component, injected by
 * every report. Owns the controls every report fetches with (scope, event lens, client
 * lens, window, bots) and the resolved scope the server answered with.
 *
 * The role ceiling is mirrored here ONLY to decide what the scope dropdown offers. The
 * server enforces it: a scope above the ceiling is 403, never narrowed.
 */
@Injectable()
export class UsageAnalysisStateService {
	private readonly http = inject(HttpClient);
	private readonly auth = inject(AuthService);
	private readonly jobService = inject(JobService);
	private readonly destroyRef = inject(DestroyRef);
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

	/** Scopes the caller may pick — every scope up to the ceiling. */
	readonly scopeOptions = computed<readonly UsageScopeOption[]>(() => {
		const max = USAGE_SCOPE_ORDER.indexOf(this.ceiling());
		return USAGE_SCOPE_OPTIONS.filter(o => USAGE_SCOPE_ORDER.indexOf(o.scope) <= max);
	});

	/** Report slots this role sees, in dropdown order. */
	readonly reports = computed<readonly UsageReportDef[]>(() =>
		USAGE_REPORTS.filter(t => t.roles.includes(this.role())));

	/**
	 * Every role lands on the BROADEST scope it may hold (Todd, 2026-09-06): Superuser on
	 * All TSIC, SuperDirector on Customer, Director on This event. Narrower is one pick
	 * away. A linkedSignal on the ceiling, so a role change mid-session reseeds rather
	 * than leaving a scope the new role may not hold.
	 */
	readonly scope = linkedSignal<UsageScope, UsageScope>({
		source: this.ceiling,
		computation: ceiling => ceiling,
	});

	readonly windowDays = signal<number>(7);
	readonly excludeBots = signal(true);

	// Resolved scope — null while (re)loading, so nothing on screen can claim a scope
	// it was not fetched with.
	readonly scopeInfo = signal<UsageAnalysisScopeDto | null>(null);
	readonly isLoadingScope = signal(false);
	readonly scopeError = signal<string | null>(null);

	/** The job the caller is standing in, lower-cased for id comparison. */
	readonly currentJobId = computed(() =>
		this.jobService.currentJob()?.jobId?.toLowerCase() ?? null);

	/** Live events the lens can pick from — the resolved scope's list. */
	readonly eventOptions = computed(() => this.scopeInfo()?.jobs ?? []);

	/**
	 * The event lens. Null = every live event in the scope. Seeded whenever the scope
	 * resolves: the event the caller is STANDING IN if it is live and inside the scope
	 * (Todd, 2026-09-06 — a Superuser or SuperDirector opens on their own event, with the
	 * rest of the scope one pick away), else All events. Reseeding on scopeInfo also means
	 * a job from the old scope can never linger. The server refuses an id outside the
	 * resolved set, so this can only narrow.
	 */
	readonly eventId = linkedSignal<UsageAnalysisScopeDto | null, string | null>({
		source: this.scopeInfo,
		computation: info => {
			const current = this.currentJobId();
			if (!info || !current) return null;
			return info.jobs.find(j => j.jobId.toLowerCase() === current)?.jobId ?? null;
		},
	});

	/**
	 * The client lens. Null = every client. Offered only from the facet below — the clients
	 * that actually have rows in the current scope/window/bots/event — and snapped back to
	 * null the moment the chosen client drops out of it, so the stamp never names a client
	 * that contributed nothing.
	 */
	readonly clientId = signal<number | null>(null);

	/** What every report fetches with. A report refetches when this changes and never otherwise. */
	readonly query = computed<UsageQuery>(() => ({
		scope: this.scope(),
		windowDays: this.windowDays(),
		excludeBots: this.excludeBots(),
		eventId: this.eventId(),
		clientId: this.clientId(),
	}));

	readonly scopeLabel = computed(() =>
		USAGE_SCOPE_OPTIONS.find(o => o.scope === this.scope())?.label ?? this.scope());

	readonly jobCount = computed(() => this.scopeInfo()?.jobs.length ?? 0);

	readonly jobNames = computed(() => (this.scopeInfo()?.jobs ?? []).map(j => j.jobName));

	/** A Director has one event; the picker exists only where there is a set to narrow. */
	readonly showEventPicker = computed(() => this.scope() !== 'job' && this.eventOptions().length > 0);

	readonly eventLabel = computed(() => {
		const id = this.eventId();
		if (!id) return 'All events';
		return this.eventOptions().find(j => j.jobId === id)?.jobName ?? 'All events';
	});

	/**
	 * The row a scope-wide table should single out, lower-cased: the lens if set, else the
	 * event the caller is standing in — so at "All events" the reader can still find
	 * themselves in the list.
	 */
	readonly highlightJobId = computed(() => {
		const lens = this.eventId();
		if (lens) return lens.toLowerCase();
		return this.currentJobId();
	});

	// Client facet: what the Client dropdown may offer. Reloaded whenever the facet query
	// (everything but the client itself) changes.
	readonly clients = signal<readonly UsageClientFacetDto[]>([]);
	readonly isLoadingClients = signal(false);

	/** The dropdown appears only when at least one client has rows to offer. */
	readonly showClientPicker = computed(() => this.clients().length > 0);

	readonly clientLabel = computed(() => {
		const id = this.clientId();
		if (id === null) return 'All clients';
		return this.clients().find(c => c.appClientId === id)?.appClientName ?? 'All clients';
	});

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

	/** True when a report may run: scope resolved, data source present, at least one live event. */
	readonly canQuery = computed(() =>
		this.scopeInfo() !== null && !this.isUnavailable() && !this.isConcludedEvent() && this.jobCount() > 0);

	private readonly facetQuery = computed<FacetQuery | null>(() => {
		if (!this.canQuery()) return null;
		const q = this.query();
		return { scope: q.scope, windowDays: q.windowDays, excludeBots: q.excludeBots, eventId: q.eventId };
	});

	constructor() {
		// Facet subscription lives as long as the page. Created here, in the injection context.
		toObservable(this.facetQuery)
			.pipe(
				map(q => q ? JSON.stringify(q) : null),
				distinctUntilChanged(),
				switchMap(key => {
					if (!key) {
						this.clients.set([]);
						this.clientId.set(null);
						return of(null);
					}
					const q = JSON.parse(key) as FacetQuery;
					const params: Record<string, string | number | boolean> = {
						scope: q.scope, windowDays: q.windowDays, excludeBots: q.excludeBots,
					};
					if (q.eventId) params['eventId'] = q.eventId;
					this.isLoadingClients.set(true);
					return this.http.get<UsageClientsDto>(`${this.apiUrl}/clients`, { params })
						.pipe(catchError(() => of(null)));
				}),
				takeUntilDestroyed(this.destroyRef),
			)
			.subscribe(res => {
				this.isLoadingClients.set(false);
				if (res === null) return;
				this.clients.set(res.clients);
				// Snap back: a chosen client that no longer has rows is not a choice.
				const chosen = this.clientId();
				if (chosen !== null && !res.clients.some(c => c.appClientId === chosen)) this.clientId.set(null);
			});
	}

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
		// eventId reseeds itself when the new scope resolves; the client lens has no such anchor.
		this.clientId.set(null);
		this.loadScope();
	}

	setEvent(jobId: string | null): void {
		this.eventId.set(jobId);
	}

	setClient(appClientId: number | null): void {
		this.clientId.set(appClientId);
	}

	setWindow(days: number): void {
		this.windowDays.set(days);
	}

	setBots(exclude: boolean): void {
		this.excludeBots.set(exclude);
	}
}
