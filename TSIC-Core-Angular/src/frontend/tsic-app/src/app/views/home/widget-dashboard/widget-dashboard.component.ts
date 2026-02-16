import { Component, computed, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { WidgetDashboardService } from './services/widget-dashboard.service';
import { AuthService } from '@infrastructure/services/auth.service';
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
	templateUrl: './widget-dashboard.component.html',
	styleUrl: './widget-dashboard.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class WidgetDashboardComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);
	private readonly auth = inject(AuthService);
	private readonly router = inject(Router);
	private readonly route = inject(ActivatedRoute);

	readonly dashboard = signal<WidgetDashboardResponse | null>(null);
	readonly isLoading = signal(false);
	readonly hasError = signal(false);

	readonly roleName = computed(() => this.auth.currentUser()?.role || '');

	private readonly jobPath = computed(() => {
		const user = this.auth.currentUser();
		return user?.jobPath || this.route.parent?.snapshot.paramMap.get('jobPath') || '';
	});

	private configCache = new Map<number, WidgetConfig>();

	ngOnInit(): void {
		this.loadDashboard();
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

	sectionLabel(section: string): string {
		switch (section) {
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
			this.router.navigate(['/', this.jobPath(), ...config.route.split('/')]);
		}
	}
}
