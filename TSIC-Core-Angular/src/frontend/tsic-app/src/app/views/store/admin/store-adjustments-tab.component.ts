import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { StoreExportButtonComponent } from './store-export-button.component';
import type { StoreQuantityAdjustmentDto } from '@core/api';
import { dateKey, tableSort, textKey } from '../../../shared-ui/table-sort';

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
	imports: [CommonModule, FormsModule, StoreExportButtonComponent],
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

	readonly exportAdjustments = () => this.store.exportQuantityAdjustments();

	readonly visibleRows = computed(() => {
		const term = this.search().trim().toLowerCase();
		if (!term) return this.rows();
		return this.rows().filter(r =>
			r.skuLabel.toLowerCase().includes(term)
			|| r.familyUserName.toLowerCase().includes(term)
			|| r.email.toLowerCase().includes(term)
			|| `${r.parentFirstName ?? ''} ${r.parentLastName ?? ''}`.toLowerCase().includes(term));
	});

	/** Most recent cut first — the shopper still worth calling back. */
	readonly sort = tableSort<
		'adj' | 'sku' | 'from' | 'to' | 'login' | 'parent' | 'email' | 'when'
	>('when', { adj: 'desc', from: 'desc', to: 'desc', when: 'desc' });

	readonly sortedRows = this.sort.applyTo(this.visibleRows, (col, a, b) => {
		switch (col) {
			case 'adj':    return a.adjQuantity - b.adjQuantity;
			case 'sku':    return textKey(a.skuLabel, b.skuLabel);
			case 'from':   return a.fromQuantity - b.fromQuantity;
			case 'to':     return a.toQuantity - b.toQuantity;
			case 'login':  return textKey(a.familyUserName, b.familyUserName);
			case 'parent': return textKey(this.parentName(a), this.parentName(b));
			case 'email':  return textKey(a.email, b.email);
			case 'when':   return dateKey(a.whenChanged) - dateKey(b.whenChanged);
		}
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
