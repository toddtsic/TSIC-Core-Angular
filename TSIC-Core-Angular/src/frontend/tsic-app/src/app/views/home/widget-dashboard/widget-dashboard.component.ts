import { Component, computed, effect, inject, input, signal, ChangeDetectionStrategy } from '@angular/core';
import { Router, ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { WidgetDashboardService } from './services/widget-dashboard.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import { ClientBannerComponent } from '@layouts/components/client-banner/client-banner.component';
import { BulletinsComponent } from '@shared-ui/bulletins/bulletins.component';
import { PlayerTrendWidgetComponent } from './player-trend-widget/player-trend-widget.component';
import { TeamTrendWidgetComponent } from './team-trend-widget/team-trend-widget.component';
import { AgegroupDistributionWidgetComponent } from './agegroup-distribution-widget/agegroup-distribution-widget.component';
import { EventContactWidgetComponent } from './event-contact-widget/event-contact-widget.component';
import { YearOverYearWidgetComponent } from './year-over-year-widget/year-over-year-widget.component';
import type { DashboardMetricsDto, WidgetCategoryGroupDto, WidgetDashboardResponse, WidgetItemDto } from '@core/api';

interface WidgetConfig {
	route?: string;
	endpoint?: string;
	label?: string;
	icon?: string;
	format?: string;
}

@Component({
	selector: 'app-widget-dashboard',
	standalone: true,
	imports: [ClientBannerComponent, BulletinsComponent, PlayerTrendWidgetComponent, TeamTrendWidgetComponent, AgegroupDistributionWidgetComponent, EventContactWidgetComponent, YearOverYearWidgetComponent],
	templateUrl: './widget-dashboard.component.html',
	styleUrl: './widget-dashboard.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class WidgetDashboardComponent {
	private readonly svc = inject(WidgetDashboardService);
	private readonly auth = inject(AuthService);
	private readonly jobService = inject(JobService);
	private readonly router = inject(Router);
	private readonly route = inject(ActivatedRoute);

	/** 'authenticated' = reads job/role from JWT; 'public' = anonymous, needs jobPath input */
	readonly mode = input<'authenticated' | 'public'>('authenticated');

	/** Job path for public mode (ignored in authenticated mode) */
	readonly publicJobPath = input<string>('', { alias: 'jobPath' });

	readonly dashboard = signal<WidgetDashboardResponse | null>(null);
	readonly metrics = signal<DashboardMetricsDto | null>(null);
	readonly isLoading = signal(false);
	readonly hasError = signal(false);

	/** Active workspace key when in spoke mode (empty = hub mode) */
	readonly activeWorkspaceKey = signal('');

	readonly roleName = computed(() =>
		this.mode() === 'public' ? '' : (this.auth.currentUser()?.role || ''));

	readonly username = computed(() =>
		this.auth.currentUser()?.username || '');

	readonly jobName = computed(() =>
		this.jobService.currentJob()?.jobName || '');

	// ── Hero branding signals ──

	readonly heroBannerBgUrl = computed(() => {
		const path = this.jobService.currentJob()?.jobBannerBackgroundPath;
		return path ? buildAssetUrl(path) : '';
	});

	readonly heroLogoUrl = computed(() => {
		const path = this.jobService.currentJob()?.jobBannerPath;
		if (!path) return '';
		let url = buildAssetUrl(path);
		if (url.toLowerCase().endsWith('.pdf')) url = url.slice(0, -4) + '.jpg';
		return url;
	});

	readonly heroHasBanner = computed(() => !!this.heroBannerBgUrl());

	/** True when job type supports teams (League, Tournament) */
	readonly isTeamJob = computed(() => {
		const typeName = this.jobService.currentJob()?.jobTypeName?.toLowerCase();
		return typeName === 'league' || typeName === 'tournament';
	});

	readonly isPublic = computed(() => this.mode() === 'public');

	/** Resolved job path — from input (public) or JWT/route (authenticated) */
	readonly activeJobPath = computed(() => {
		if (this.mode() === 'public') return this.publicJobPath();
		const user = this.auth.currentUser();
		if (user?.jobPath) return user.jobPath;
		// Traverse route tree upward to find :jobPath (works in both hub and spoke mode)
		let r: ActivatedRouteSnapshot | null = this.route.snapshot;
		while (r) {
			const jp = r.paramMap.get('jobPath');
			if (jp) return jp;
			r = r.parent;
		}
		return '';
	});

	// Bulletin data (used by content widgets)
	readonly bulletins = computed(() => this.jobService.bulletins());
	readonly bulletinsLoading = computed(() => this.jobService.bulletinsLoading());
	readonly bulletinsError = computed(() => this.jobService.bulletinsError());

	// ── Derived metric displays ──

	readonly roleAccentClass = computed(() => {
		const role = this.roleName().toLowerCase().replace(/\s+/g, '-');
		return role ? `role-${role}` : '';
	});

	readonly schedulePercent = computed(() => {
		const m = this.metrics();
		if (!m || m.scheduling.totalAgegroups === 0) return 0;
		return Math.round((m.scheduling.agegroupsScheduled / m.scheduling.totalAgegroups) * 100);
	});

	readonly isSuperuser = computed(() => this.roleName() === 'Superuser');

	// ── Hub/Spoke computed signals ──

	/** True when rendering a single workspace (spoke mode) */
	readonly isSpokeMode = computed(() => !!this.activeWorkspaceKey());

	/** The 'dashboard' workspace from the API response (hub content) */
	readonly dashboardWorkspace = computed(() => {
		const db = this.dashboard();
		if (!db) return null;
		return db.workspaces.find(ws => ws.workspace === 'dashboard') ?? null;
	});

	/** Dashboard categories for the hub, excluding bulletins for admin roles */
	readonly hubCategories = computed(() => {
		const ws = this.dashboardWorkspace();
		if (!ws) return [];
		if (!this.auth.isAdmin()) return ws.categories;
		return ws.categories.filter(cat =>
			!cat.widgets.some(w => w.componentKey === 'bulletins')
		);
	});

	/** Non-dashboard, non-public workspaces — these become spoke navigation cards */
	readonly spokeWorkspaces = computed(() => {
		const db = this.dashboard();
		if (!db) return [];
		return db.workspaces.filter(ws =>
			ws.workspace !== 'dashboard' && ws.workspace !== 'public'
		);
	});

	/** The workspace to render when in spoke mode */
	readonly activeWorkspace = computed(() => {
		const key = this.activeWorkspaceKey();
		const db = this.dashboard();
		if (!key || !db) return null;
		return db.workspaces.find(ws => ws.workspace === key) ?? null;
	});

	/** Display label for the active spoke workspace */
	readonly activeWorkspaceLabel = computed(() => {
		const key = this.activeWorkspaceKey();
		return key ? this.workspaceLabel(key) : '';
	});

	private configCache = new Map<number, WidgetConfig>();

	/** Track the last loaded identity to avoid redundant reloads */
	private lastLoadedKey = '';

	constructor() {
		// Detect spoke mode from route param
		const key = this.route.snapshot.paramMap.get('workspaceKey') || '';
		this.activeWorkspaceKey.set(key);

		// Reactive reload: fires when auth state changes (job switch, role switch)
		effect(() => {
			const mode = this.mode();
			const user = this.auth.currentUser();
			const jobPath = this.activeJobPath();
			// regId is unique per role+job combination — changes on every role switch
			const regId = user?.regId || '';
			const loadKey = `${mode}:${jobPath}:${regId}`;

			if (!jobPath || loadKey === this.lastLoadedKey) return;
			this.lastLoadedKey = loadKey;

			if (mode === 'public') {
				this.loadPublicData();
			} else {
				// Refresh job metadata for hero branding + header bar
				this.jobService.fetchJobMetadata(jobPath).subscribe();
				this.loadDashboard();
				if (!this.activeWorkspaceKey()) {
					this.loadMetrics();
				}
			}
		});
	}

	/** Public mode: load job metadata + bulletins + widget config */
	private loadPublicData(): void {
		const jobPath = this.publicJobPath();
		if (!jobPath) return;

		this.isLoading.set(true);
		this.hasError.set(false);
		this.configCache.clear();

		// Load job metadata (for banner) and bulletins
		this.jobService.fetchJobMetadata(jobPath).subscribe({
			next: (job) => {
				this.jobService.setJob(job);
				this.jobService.loadBulletins(jobPath);
			}
		});

		// Load widget config
		this.svc.getPublicDashboard(jobPath).subscribe({
			next: (data) => {
				this.dashboard.set(data);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			}
		});
	}

	loadDashboard(): void {
		this.isLoading.set(true);
		this.hasError.set(false);
		this.configCache.clear();

		this.svc.getDashboard().subscribe({
			next: (data) => {
				this.dashboard.set(data);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			}
		});
	}

	private loadMetrics(): void {
		this.svc.getMetrics().subscribe({
			next: (data) => this.metrics.set(data),
			error: () => { /* metrics are optional — hero degrades gracefully */ }
		});
	}

	/** Returns true if a workspace renders full-width content components (banner, bulletins, charts) */
	isContentWorkspace(workspace: string): boolean {
		return workspace === 'public' || workspace === 'dashboard';
	}

	/** Returns true if a category contains only chart-type widgets (for tile grid layout) */
	isChartCategory(category: WidgetCategoryGroupDto): boolean {
		return category.widgets.length > 0 && category.widgets.every(w => w.widgetType === 'chart');
	}

	/** Returns true if a category contains only status-card type widgets */
	isStatusCategory(category: WidgetCategoryGroupDto): boolean {
		return category.widgets.length > 0 && category.widgets.every(w => w.widgetType === 'status-card');
	}

	workspaceLabel(workspace: string): string {
		switch (workspace) {
			case 'public': return '';
			case 'dashboard': return '';
			case 'job-config': return 'Event Setup';
			case 'player-reg': return 'Player Registration';
			case 'team-reg': return 'Team Registration';
			case 'scheduling': return 'Scheduling';
			case 'fin-per-job': return 'Customer Finances';
			case 'fin-per-customer': return 'Job Finances';
			default: return workspace;
		}
	}

	workspaceIcon(workspace: string): string {
		switch (workspace) {
			case 'job-config': return 'bi-gear-fill';
			case 'player-reg': return 'bi-person-lines-fill';
			case 'team-reg': return 'bi-people-fill';
			case 'scheduling': return 'bi-calendar-range';
			case 'fin-per-job': return 'bi-cash-stack';
			case 'fin-per-customer': return 'bi-wallet2';
			default: return 'bi-grid';
		}
	}

	workspaceDescription(workspace: string): string {
		switch (workspace) {
			case 'job-config': return 'Event setup, LADT editor, fee structures';
			case 'player-reg': return 'Player registration search and management';
			case 'team-reg': return 'Team and club registration management';
			case 'scheduling': return 'Scheduling pipeline, pools, and game management';
			case 'fin-per-job': return 'Revenue, balances, and payment summaries';
			case 'fin-per-customer': return 'Your payment history and balances';
			default: return '';
		}
	}

	getConfig(widget: WidgetItemDto): WidgetConfig {
		const cached = this.configCache.get(widget.widgetId);
		if (cached) return cached;

		let config: WidgetConfig = {};
		try { config = JSON.parse(widget.config || '{}'); }
		catch { /* use empty */ }

		this.configCache.set(widget.widgetId, config);
		return config;
	}

	getIcon(widget: WidgetItemDto): string {
		return this.getConfig(widget).icon || 'bi-square';
	}

	getLabel(widget: WidgetItemDto): string {
		return this.getConfig(widget).label || widget.name;
	}

	onWidgetClick(widget: WidgetItemDto, event: Event): void {
		event.preventDefault();
		const config = this.getConfig(widget);
		if (config.route) {
			const wsKey = this.activeWorkspaceKey();
			this.router.navigate(
				['/', this.activeJobPath(), ...config.route.split('/')],
				wsKey ? { queryParams: { from: wsKey } } : {},
			);
		}
	}

	navigateToWorkspace(workspaceKey: string): void {
		this.router.navigate(['/', this.activeJobPath(), 'workspace', workspaceKey]);
	}

	navigateToHub(): void {
		this.router.navigate(['/', this.activeJobPath()]);
	}
}
