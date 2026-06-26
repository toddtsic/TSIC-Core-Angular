import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Smart marker — the ✨ corner badge that flags a bulletin as system-generated
 * ("smart"), with a hover/focus popover that explains the concept on demand
 * instead of parking permanent chrome on the page. Drops into any relatively-
 * positioned smart surface (the compound panels, the store card).
 *
 * Brand-consistent glyph: the same `bi-stars` used by the Smart Bulletins nav
 * entry + editor, so nav → editor → panel all speak one visual language.
 */
@Component({
	selector: 'app-smart-marker',
	standalone: true,
	templateUrl: './smart-marker.component.html',
	styleUrl: './smart-marker.component.scss',
	changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SmartMarkerComponent {
	/** Popover body — defaults to the generic "writes itself" explanation, but a
	 *  host can tailor it (e.g. "This schedule updates itself as games are played"). */
	readonly text = input<string>('This bulletin writes itself from live event data — always current, never typed by hand.');
}
