import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import type { StoreQuantityAdjustmentDto } from '@core/api';

/**
 * Quantity Adjustments — port of legacy `StoreCartQuantityAdjustments/Index`.
 *
 * Every row is a cart the checkout re-check had to cut back: the shopper added stock that was
 * gone by the time they reached the payment page. It is the record of a disappointed customer,
 * which is why the contact details sit right next to the item.
 *
 * Legacy's column order and headers, kept: AdjQty · Sku · FromQty · ToQty · F-Username ·
 * FNParent · LNParent · Email · When. Two things it got wrong are corrected server-side and
 * noted on `StoreQuantityAdjustmentDto`.
 */
@Component({
	selector: 'app-store-adjustments-tab',
	standalone: true,
	imports: [CommonModule, FormsModule],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-adjustments-tab.component.html',
	styleUrl: './store-adjustments-tab.component.scss',
})
export class StoreAdjustmentsTabComponent {
	private readonly store = inject(StoreService);

	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly rows = signal<StoreQuantityAdjustmentDto[]>([]);
	readonly search = signal('');

	readonly visibleRows = computed(() => {
		const term = this.search().trim().toLowerCase();
		if (!term) return this.rows();
		return this.rows().filter(r =>
			r.skuLabel.toLowerCase().includes(term)
			|| r.familyUserName.toLowerCase().includes(term)
			|| r.email.toLowerCase().includes(term)
			|| `${r.parentFirstName ?? ''} ${r.parentLastName ?? ''}`.toLowerCase().includes(term));
	});

	/** Units the shoppers in view did not get. */
	readonly totalUnitsLost = computed(() =>
		this.visibleRows().reduce((sum, r) => sum + r.adjQuantity, 0));

	constructor() {
		this.load();
	}

	load(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.store.getQuantityAdjustments().subscribe({
			next: rows => {
				this.rows.set(rows);
				this.isLoading.set(false);
			},
			error: (err: { error?: { message?: string } }) => {
				this.errorMessage.set(err?.error?.message ?? 'Could not load the adjustments log.');
				this.isLoading.set(false);
			},
		});
	}

	parentName(row: StoreQuantityAdjustmentDto): string {
		return [row.parentFirstName, row.parentLastName]
			.filter(p => p && p.trim())
			.join(' ');
	}
}
