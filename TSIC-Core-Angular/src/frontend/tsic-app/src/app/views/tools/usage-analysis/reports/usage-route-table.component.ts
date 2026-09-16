import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';

import type { UsageRouteLine } from './usage-route-report';

type SortKey = 'route' | 'requests' | 'people' | 'failed';

/**
 * Every route under the event lens, in a table — the full list the chart deliberately does
 * not draw. Sortable by any column; opens busiest first. Shared by both requests-by-route
 * reports; the People column appears only where the report can count people.
 */
@Component({
	selector: 'app-usage-route-table',
	standalone: true,
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './usage-route-table.component.html',
	styleUrl: './usage-route-table.component.scss',
})
export class UsageRouteTableComponent {
	readonly routes = input.required<readonly UsageRouteLine[]>();
	/** The heading's subject: the charted event's name, or the All row's. */
	readonly heading = input.required<string>();
	readonly showPeople = input(false);

	readonly sortKey = signal<SortKey>('requests');
	/** Numbers open high-to-low, names A-Z. */
	readonly sortDesc = signal(true);

	readonly sorted = computed<readonly UsageRouteLine[]>(() => {
		const key = this.sortKey();
		const dir = this.sortDesc() ? -1 : 1;
		return [...this.routes()].sort((a, b) => {
			const c = key === 'route'
				? a.route.localeCompare(b.route)
				: (a[key] ?? 0) - (b[key] ?? 0);
			return c * dir || a.route.localeCompare(b.route);
		});
	});

	sortBy(key: SortKey): void {
		if (this.sortKey() === key) {
			this.sortDesc.set(!this.sortDesc());
		} else {
			this.sortKey.set(key);
			this.sortDesc.set(key !== 'route');
		}
	}

	ariaSort(key: SortKey): 'ascending' | 'descending' | 'none' {
		if (this.sortKey() !== key) return 'none';
		return this.sortDesc() ? 'descending' : 'ascending';
	}

	count(n: number | undefined): string {
		return !n ? '·' : n.toLocaleString();
	}
}
