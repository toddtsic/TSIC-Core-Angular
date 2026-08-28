import { Component, ChangeDetectionStrategy, inject, input, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { StoreService } from '../../infrastructure/services/store.service';
import type { StoreFrontInfoDto } from '@core/api';

/**
 * The director's store copy — Pickup, Refund Policy, Contact — shown to the shopper.
 *
 * LEGACY (A-04): `StoreFamily/Index` rendered these as a Pickup · Return Policy · Contact tab
 * strip inside EVERY item card, and `StoreFamily/Checkout` again as three labelled lines. The
 * three strings are JOB-level (`Jobs.StorePickupDetails` / `StoreRefundPolicy` /
 * `StoreContactEmail`), so the tab strip repeated identical text once per product — twelve items
 * meant twelve copies of the same pickup instructions.
 *
 * We render it ONCE per surface. Same three pieces of copy, same two places a shopper meets them,
 * without the duplication. `variant` picks the presentation: a collapsible panel while browsing,
 * open lines at checkout where the shopper is about to pay and should not have to click to find
 * the refund policy.
 *
 * Legacy labelled the same field "Return Policy" on the tab and "Refund Policy" at checkout. One
 * field, one name: it is `StoreRefundPolicy`, and Refund Policy is what it says everywhere here.
 *
 * The copy is PLAIN TEXT — job config collects it in `textarea`s and legacy rendered it through
 * Razor's HTML-encoding interpolation. Interpolated, never `[innerHTML]`; `white-space: pre-line`
 * keeps the line breaks the director typed.
 */
@Component({
	selector: 'app-store-front-info',
	standalone: true,
	imports: [CommonModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-front-info.component.html',
	styleUrl: './store-front-info.component.scss',
})
export class StoreFrontInfoComponent {
	private readonly store = inject(StoreService);

	/** 'panel' — collapsible, for the storefront. 'inline' — always open, for checkout. */
	readonly variant = input<'panel' | 'inline'>('panel');

	readonly info = signal<StoreFrontInfoDto | null>(null);
	readonly isOpen = signal(false);

	constructor() {
		// A failure here is silent by design: this is supporting copy, and a shopper who cannot
		// see the pickup note should still be able to buy. Nothing renders and nothing shouts.
		this.store.getStoreFrontInfo().subscribe({
			next: info => this.info.set(info),
			error: () => this.info.set(null),
		});
	}

	toggle(): void {
		this.isOpen.set(!this.isOpen());
	}
}
