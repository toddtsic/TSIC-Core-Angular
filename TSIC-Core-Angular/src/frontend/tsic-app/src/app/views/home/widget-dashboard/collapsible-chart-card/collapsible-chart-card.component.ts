import { Component, input, model, ChangeDetectionStrategy } from '@angular/core';

@Component({
	selector: 'app-collapsible-chart-card',
	standalone: true,
	templateUrl: './collapsible-chart-card.component.html',
	styleUrl: './collapsible-chart-card.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush
})
export class CollapsibleChartCardComponent {
	/** Display title in the header */
	readonly title = input.required<string>();

	/** Optional icon class (Bootstrap Icons) */
	readonly icon = input('bi-bar-chart-line');

	/** Whether the card is collapsed — two-way bindable via [(collapsed)]. Always starts collapsed. */
	readonly collapsed = model(true);

	toggle(): void {
		this.collapsed.set(!this.collapsed());
	}
}
