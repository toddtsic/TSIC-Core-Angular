import { Component, inject, signal, computed, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';

import { WidgetDashboardService } from '@widgets/services/widget-dashboard.service';
import type { TeamsAppUsageDto, TeamsAppUsageFrequencyBandDto, TeamsAppUsageTeamRowDto } from '@core/api';

/** Windows offered by the selector. Server clamps to 1–365 regardless. */
const WINDOWS = [
	{ days: 7, label: '7d' },
	{ days: 30, label: '30d' },
	{ days: 90, label: '90d' },
] as const;

/**
 * TSIC-TEAMS app usage for the current event, for Directors and above.
 *
 * CLIENT-FACING: the only view of our usage logs a client ever sees, so it shows
 * aggregates in plain language and nothing of the log itself — no link to the internal
 * Usage Analysis page, no routes, versions or platforms.
 *
 * People, teams and days — never requests. Breadth (active users, teams reached) sits
 * beside depth (returning users, days of use) because either alone misleads.
 */
@Component({
	selector: 'app-teams-app-usage',
	standalone: true,
	imports: [DatePipe],
	templateUrl: './teams-app-usage.component.html',
	styleUrl: './teams-app-usage.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamsAppUsageComponent implements OnInit {
	private readonly svc = inject(WidgetDashboardService);

	readonly data = signal<TeamsAppUsageDto | null>(null);
	readonly hasError = signal(false);
	readonly isLoading = signal(false);

	readonly windows = WINDOWS;
	readonly windowDays = signal<number>(30);

	readonly isUnavailable = computed(() => {
		const d = this.data();
		return d !== null && !d.usageLoggingAvailable;
	});

	readonly isNotEnabled = computed(() => {
		const d = this.data();
		return d !== null && d.usageLoggingAvailable && !d.teamsAppEnabled;
	});

	/** Logging began inside the window: the uncovered days are no data, not no use. */
	readonly isPartialWindow = computed(() => {
		const d = this.data();
		return d !== null && d.daysCovered < d.windowDays;
	});

	/** Bars in "How often" are scaled to the largest band so the shape reads at a glance. */
	private readonly maxBandUsers = computed(() =>
		Math.max(1, ...(this.data()?.frequency ?? []).map(b => b.users)));

	bandLabel(b: TeamsAppUsageFrequencyBandDto): string {
		if (b.maxDays == null) return `${b.minDays}+ days`;
		if (b.minDays === b.maxDays) return b.minDays === 1 ? '1 day' : `${b.minDays} days`;
		return `${b.minDays}–${b.maxDays} days`;
	}

	bandWidth(b: TeamsAppUsageFrequencyBandDto): number {
		return Math.round((b.users / this.maxBandUsers()) * 100);
	}

	/** Share of a roster, as a bar width only. The figures beside it carry the reading. */
	shareWidth(active: number, rostered: number): number {
		return rostered > 0 ? Math.round((active / rostered) * 100) : 0;
	}

	/** Age group is shown only when the team name does not already say it ("2029" / "2029 Team"). */
	showAgegroup(t: TeamsAppUsageTeamRowDto): boolean {
		return !!t.agegroupName
			&& !t.teamName.toLowerCase().startsWith(t.agegroupName.toLowerCase());
	}

	setWindow(days: number): void {
		if (days === this.windowDays()) return;
		this.windowDays.set(days);
		this.load();
	}

	/** Explicit callback, never an effect(): loading is a response to a user choice. */
	load(): void {
		this.isLoading.set(true);
		this.hasError.set(false);

		this.svc.getTeamsAppUsage(this.windowDays()).subscribe({
			next: (d) => {
				this.data.set(d);
				this.isLoading.set(false);
			},
			error: () => {
				this.hasError.set(true);
				this.isLoading.set(false);
			},
		});
	}

	ngOnInit(): void {
		this.load();
	}
}
