import { Component, computed, effect, inject, input, signal, isDevMode, ChangeDetectionStrategy, Type } from '@angular/core';
import { NgComponentOutlet } from '@angular/common';
import { ActivatedRoute, ActivatedRouteSnapshot } from '@angular/router';
import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import { WIDGET_REGISTRY } from '@widgets/widget-registry';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { ToastService } from '@shared-ui/toast.service';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { MenuStateService } from '../../../layouts/services/menu-state.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import type { AvailableWidgetDto, DashboardMetricsDto, SaveUserWidgetsRequest, WidgetCategoryGroupDto, WidgetDashboardResponse, WidgetItemDto } from '@core/api';

interface WidgetConfig {
	endpoint?: string;
	displayStyle?: string;
}

/** Mutable row for the customization UI */
interface CustomizeRow {
	widgetId: number;
	categoryId: number;
	name: string;
	widgetType: string;
	componentKey: string;
	description: string | null;
	categoryName: string;
	isVisible: boolean;
	displayOrder: number;
	_dirty: boolean;
}

@Component({
	selector: 'app-widget-dashboard',
	standalone: true,
	imports: [NgComponentOutlet, TsicDialogComponent, ConfirmDialogComponent],
	templateUrl: './widget-dashboard.component.html',
	styleUrl: './widget-dashboard.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class WidgetDashboardComponent {
	private readonly svc = inject(WidgetDashboardService);
	private readonly auth = inject(AuthService);
	private readonly jobService = inject(JobService);
	private readonly toast = inject(ToastService);
	private readonly route = inject(ActivatedRoute);
	private readonly menuState = inject(MenuStateService);

	/** 'authenticated' = reads job/role from JWT; 'public' = anonymous, needs jobPath input */
	readonly mode = input<'authenticated' | 'public'>('authenticated');

	/** Job path for public mode (ignored in authenticated mode) */
	readonly publicJobPath = input<string>('', { alias: 'jobPath' });

	readonly dashboard = signal<WidgetDashboardResponse | null>(null);
	readonly metrics = signal<DashboardMetricsDto | null>(null);
	readonly isLoading = signal(false);
	readonly hasError = signal(false);

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

	/** Tracks whether the hero overlay logo loaded as a real image (not a tiny placeholder) */
	readonly heroLogoValid = signal(true);

	onHeroLogoLoad(event: Event) {
		const img = event.target as HTMLImageElement;
		this.heroLogoValid.set(img.naturalWidth >= 150 && img.naturalHeight >= 150);
	}

	onHeroLogoError() {
		this.heroLogoValid.set(false);
	}

	readonly isPublic = computed(() => this.mode() === 'public');

	/** Resolved job path — from input (public) or JWT/route (authenticated) */
	readonly activeJobPath = computed(() => {
		if (this.mode() === 'public') return this.publicJobPath();
		const user = this.auth.currentUser();
		if (user?.jobPath) return user.jobPath;
		let r: ActivatedRouteSnapshot | null = this.route.snapshot;
		while (r) {
			const jp = r.paramMap.get('jobPath');
			if (jp) return jp;
			r = r.parent;
		}
		return '';
	});

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

	// ── Dashboard computed signals ──

	/** The 'dashboard' workspace from the API response */
	readonly dashboardWorkspace = computed(() => {
		const db = this.dashboard();
		if (!db) return null;
		return db.workspaces.find(ws => ws.workspace === 'dashboard') ?? null;
	});

	/** Dashboard categories, excluding bulletins for admin roles */
	readonly hubCategories = computed(() => {
		const ws = this.dashboardWorkspace();
		if (!ws) return [];
		if (!this.auth.isAdmin()) return ws.categories;
		return ws.categories.filter(cat =>
			!cat.widgets.some(w => w.componentKey === 'bulletins')
		);
	});

	private configCache = new Map<number, WidgetConfig>();
	private warnedKeys = new Set<string>();

	/** Track the last loaded identity to avoid redundant reloads */
	private lastLoadedKey = '';

	constructor() {
		// Reactive reload: fires when auth state changes (job switch, role switch)
		effect(() => {
			const mode = this.mode();
			const user = this.auth.currentUser();
			const jobPath = this.activeJobPath();
			const regId = user?.regId || '';
			const loadKey = `${mode}:${jobPath}:${regId}`;

			if (!jobPath || loadKey === this.lastLoadedKey) return;
			this.lastLoadedKey = loadKey;

			if (mode === 'public') {
				this.loadPublicData();
			} else {
				this.jobService.fetchJobMetadata(jobPath).subscribe();
				this.loadDashboard();
				this.loadMetrics();
			}
		});

		// Listen for customize requests from the header dropdown
		effect(() => {
			if (this.menuState.customizeDashboardRequested()) {
				this.menuState.ackCustomizeDashboard();
				this.openCustomize();
			}
		});
	}

	/** Resolve a componentKey to its Angular component class, or null if unknown */
	resolveWidget(componentKey: string): Type<unknown> | null {
		const cmp = WIDGET_REGISTRY[componentKey] ?? null;
		if (!cmp && isDevMode() && !this.warnedKeys.has(componentKey)) {
			this.warnedKeys.add(componentKey);
			queueMicrotask(() => this.toast.show(
				`Widget "${componentKey}" has no registry entry. Add it to widgets/widget-registry.ts.`,
				'warning', 8000,
			));
		}
		return cmp;
	}

	/** Public mode: load job metadata + bulletins + widget config */
	private loadPublicData(): void {
		const jobPath = this.publicJobPath();
		if (!jobPath) return;

		this.isLoading.set(true);
		this.hasError.set(false);
		this.configCache.clear();

		this.jobService.fetchJobMetadata(jobPath).subscribe({
			next: (job) => {
				this.jobService.setJob(job);
				this.jobService.loadBulletins(jobPath);
			}
		});

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

	/** Returns true if a category contains only chart-tile type widgets (for tile grid layout) */
	isChartCategory(category: WidgetCategoryGroupDto): boolean {
		return category.widgets.length > 0 && category.widgets.every(w => w.widgetType === 'chart-tile');
	}

	/** Returns true if a category contains only status-tile type widgets */
	isStatusCategory(category: WidgetCategoryGroupDto): boolean {
		return category.widgets.length > 0 && category.widgets.every(w => w.widgetType === 'status-tile');
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

	getDisplayStyle(widget: WidgetItemDto): string {
		return this.getConfig(widget).displayStyle || 'standard';
	}

	// ── Customization Dialog ──

	readonly showCustomizeDialog = signal(false);
	readonly showResetConfirm = signal(false);
	readonly customizeRows = signal<CustomizeRow[]>([]);
	readonly customizeLoading = signal(false);
	readonly customizeSaving = signal(false);

	/** True when user has made changes in the dialog */
	readonly customizeDirty = computed(() => {
		const rows = this.customizeRows();
		return rows.some(r => r._dirty);
	});

	openCustomize(): void {
		this.customizeLoading.set(true);
		this.showCustomizeDialog.set(true);

		this.svc.getAvailableWidgets().subscribe({
			next: (available) => {
				const rows: CustomizeRow[] = available.map((w, i) => ({
					widgetId: w.widgetId,
					categoryId: w.categoryId,
					name: w.name,
					widgetType: w.widgetType,
					componentKey: w.componentKey,
					description: w.description,
					categoryName: w.categoryName,
					isVisible: w.isVisible,
					displayOrder: i,
					_dirty: false,
				}));
				this.customizeRows.set(rows);
				this.customizeLoading.set(false);
			},
			error: () => {
				this.toast.show('Failed to load available widgets', 'danger');
				this.customizeLoading.set(false);
				this.showCustomizeDialog.set(false);
			}
		});
	}

	closeCustomize(): void {
		this.showCustomizeDialog.set(false);
		this.customizeRows.set([]);
	}

	toggleWidgetVisibility(row: CustomizeRow): void {
		this.customizeRows.update(rows =>
			rows.map(r => r.widgetId === row.widgetId
				? { ...r, isVisible: !r.isVisible, _dirty: true }
				: r
			)
		);
	}

	moveWidget(row: CustomizeRow, direction: -1 | 1): void {
		this.customizeRows.update(rows => {
			const idx = rows.findIndex(r => r.widgetId === row.widgetId);
			const targetIdx = idx + direction;
			if (targetIdx < 0 || targetIdx >= rows.length) return rows;

			const updated = [...rows];
			[updated[idx], updated[targetIdx]] = [updated[targetIdx], updated[idx]];
			return updated.map((r, i) => ({ ...r, displayOrder: i, _dirty: true }));
		});
	}

	saveCustomizations(): void {
		const rows = this.customizeRows();
		const request: SaveUserWidgetsRequest = {
			entries: rows.map(r => ({
				widgetId: r.widgetId,
				categoryId: r.categoryId,
				displayOrder: r.displayOrder,
				isHidden: !r.isVisible,
			}))
		};

		this.customizeSaving.set(true);
		this.svc.saveMyWidgets(request).subscribe({
			next: () => {
				this.customizeSaving.set(false);
				this.closeCustomize();
				this.toast.show('Dashboard customized', 'success');
				this.lastLoadedKey = '';  // force reload
				this.loadDashboard();
			},
			error: () => {
				this.customizeSaving.set(false);
				this.toast.show('Failed to save customizations', 'danger');
			}
		});
	}

	confirmResetCustomizations(): void {
		this.showResetConfirm.set(true);
	}

	resetCustomizations(): void {
		this.showResetConfirm.set(false);
		this.customizeSaving.set(true);

		this.svc.resetMyWidgets().subscribe({
			next: () => {
				this.customizeSaving.set(false);
				this.closeCustomize();
				this.toast.show('Dashboard reset to defaults', 'success');
				this.lastLoadedKey = '';
				this.loadDashboard();
			},
			error: () => {
				this.customizeSaving.set(false);
				this.toast.show('Failed to reset customizations', 'danger');
			}
		});
	}

	getWidgetTypeIcon(widgetType: string): string {
		switch (widgetType) {
			case 'chart-tile': return 'bi-bar-chart-line';
			case 'status-tile': return 'bi-speedometer2';
			case 'content': return 'bi-layout-text-window';
			default: return 'bi-grid';
		}
	}
}
