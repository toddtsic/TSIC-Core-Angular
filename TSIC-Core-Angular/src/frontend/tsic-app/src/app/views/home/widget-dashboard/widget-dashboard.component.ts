import { Component, computed, inject, input, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { WidgetDashboardService } from './services/widget-dashboard.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { ClientBannerComponent } from '@layouts/components/client-banner/client-banner.component';
import { BulletinsComponent } from '@shared-ui/bulletins/bulletins.component';
import type { WidgetDashboardResponse, WidgetItemDto } from '@core/api';

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
	imports: [ClientBannerComponent, BulletinsComponent],
	templateUrl: './widget-dashboard.component.html',
	styleUrl: './widget-dashboard.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class WidgetDashboardComponent implements OnInit {
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
	readonly isLoading = signal(false);
	readonly hasError = signal(false);

	readonly roleName = computed(() =>
		this.mode() === 'public' ? '' : (this.auth.currentUser()?.role || ''));

	readonly isPublic = computed(() => this.mode() === 'public');

	/** Resolved job path — from input (public) or JWT/route (authenticated) */
	readonly activeJobPath = computed(() => {
		if (this.mode() === 'public') return this.publicJobPath();
		const user = this.auth.currentUser();
		return user?.jobPath || this.route.parent?.snapshot.paramMap.get('jobPath') || '';
	});

	// Bulletin data (used by content widgets)
	readonly bulletins = computed(() => this.jobService.bulletins());
	readonly bulletinsLoading = computed(() => this.jobService.bulletinsLoading());
	readonly bulletinsError = computed(() => this.jobService.bulletinsError());

	private configCache = new Map<number, WidgetConfig>();

	ngOnInit(): void {
		if (this.mode() === 'public') {
			this.loadPublicData();
		} else {
			this.loadDashboard();
		}
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

	/** Returns true if a section contains only content widgets */
	isContentSection(section: string): boolean {
		return section === 'content';
	}

	sectionLabel(section: string): string {
		switch (section) {
			case 'content': return '';
			case 'health': return 'Status';
			case 'action': return 'Tools';
			case 'insight': return 'Insights';
			default: return section;
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
			this.router.navigate(['/', this.activeJobPath(), ...config.route.split('/')]);
		}
	}
}
