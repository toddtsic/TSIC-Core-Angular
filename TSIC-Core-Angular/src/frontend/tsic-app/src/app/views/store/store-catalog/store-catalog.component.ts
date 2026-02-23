import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import type { StoreItemSummaryDto } from '@core/api';

@Component({
	selector: 'app-store-catalog',
	standalone: true,
	imports: [CommonModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-catalog.component.html',
	styleUrl: './store-catalog.component.scss',
})
export class StoreCatalogComponent {
	private readonly store = inject(StoreService);
	private readonly router = inject(Router);
	private readonly route = inject(ActivatedRoute);

	readonly items = signal<StoreItemSummaryDto[]>([]);
	readonly isLoading = signal(true);
	readonly errorMessage = signal<string | null>(null);

	// Cart state from shared service
	readonly cartCount = this.store.cartCount;

	constructor() {
		this.loadItems();
		this.store.loadCart().subscribe();
	}

	private loadItems(): void {
		this.isLoading.set(true);
		this.store.getItems().subscribe({
			next: items => {
				// Only show active items with at least one active SKU
				this.items.set(items.filter(i => i.active && i.activeSkuCount > 0));
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load store items');
				this.isLoading.set(false);
			}
		});
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}

	viewItem(item: StoreItemSummaryDto): void {
		// Navigate from /:jobPath/store → /:jobPath/store/item/:id
		this.router.navigate(['../store/item', item.storeItemId], { relativeTo: this.route });
	}
}
