import { Component, input, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';

@Component({
	selector: 'app-collapsible-chart-card',
	standalone: true,
	templateUrl: './collapsible-chart-card.component.html',
	styleUrl: './collapsible-chart-card.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class CollapsibleChartCardComponent implements OnInit {
	/** Unique key for localStorage persistence (e.g., 'player-trend') */
	readonly storageKey = input.required<string>();

	/** Display title in the header */
	readonly title = input.required<string>();

	/** Optional icon class (Bootstrap Icons) */
	readonly icon = input('bi-bar-chart-line');

	/** Whether the card is collapsed */
	readonly collapsed = signal(false);

	ngOnInit(): void {
		const saved = localStorage.getItem(`chart-collapsed-${this.storageKey()}`);
		if (saved === 'true') this.collapsed.set(true);
	}

	toggle(): void {
		const next = !this.collapsed();
		this.collapsed.set(next);
		localStorage.setItem(`chart-collapsed-${this.storageKey()}`, String(next));
	}
}
