import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { StoreItemImageDto } from '@core/api';

/** One item and the photos it currently has. Placeholder rows collapse to an empty list. */
interface ItemImageGroup {
	storeItemId: number;
	storeItemName: string;
	images: StoreItemImageDto[];
}

/**
 * Product photos, ported from legacy StoreImagesController.
 *
 * Legacy presented a flat EJ2 grid of every image in the job with an item-id filter. Grouping by
 * item instead is the same information in the shape the director actually works in — the question
 * is always "does THIS product have a picture", and legacy answered it by emitting a
 * missing-image.jpg row per item, which is exactly a group with no photos.
 */
@Component({
	selector: 'app-store-images-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, ConfirmDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-images-tab.component.html',
	styleUrl: './store-images-tab.component.scss',
})
export class StoreImagesTabComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	/** Legacy MAX_IMAGES_PER_ITEM. The server enforces it; this only disables the control. */
	readonly maxImagesPerItem = 10;

	readonly isLoading = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly rows = signal<StoreItemImageDto[]>([]);

	/** Item id currently uploading, so only that card shows a spinner. */
	readonly busyItemId = signal<number | null>(null);

	/** Show only items with no photo — the working list when filling gaps before a season. */
	readonly missingOnly = signal(false);

	// ── Delete confirmation ──
	readonly showDeleteConfirm = signal(false);
	readonly deleteTarget = signal<StoreItemImageDto | null>(null);

	readonly groups = computed<ItemImageGroup[]>(() => {
		const byItem = new Map<number, ItemImageGroup>();

		for (const row of this.rows()) {
			let group = byItem.get(row.storeItemId);
			if (!group) {
				group = { storeItemId: row.storeItemId, storeItemName: row.storeItemName, images: [] };
				byItem.set(row.storeItemId, group);
			}
			// A placeholder row means "this item has no file" — it is not one of its photos.
			if (!row.isPlaceholder) group.images.push(row);
		}

		return [...byItem.values()].sort((a, b) => a.storeItemName.localeCompare(b.storeItemName));
	});

	readonly visibleGroups = computed(() =>
		this.missingOnly() ? this.groups().filter(g => g.images.length === 0) : this.groups());

	readonly missingCount = computed(() => this.groups().filter(g => g.images.length === 0).length);

	readonly photoCount = computed(() =>
		this.groups().reduce((total, g) => total + g.images.length, 0));

	constructor() {
		this.load();
	}

	load(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.store.getStoreImages().subscribe({
			next: rows => {
				this.rows.set(rows);
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load store images');
				this.isLoading.set(false);
			},
		});
	}

	// ═══════════════════════════════════════
	//  UPLOAD
	// ═══════════════════════════════════════

	/**
	 * Add a photo. Deletion renumbers instances server-side, so every mutation re-fetches rather
	 * than patching the local list — the instance numbers a user is looking at can shift.
	 */
	onAddFile(event: Event, group: ItemImageGroup): void {
		const file = this.takeFile(event);
		if (!file) return;

		this.busyItemId.set(group.storeItemId);
		this.store.addItemImage(group.storeItemId, file).subscribe({
			next: () => {
				this.toast.show(`Photo added to ${group.storeItemName}`, 'success');
				this.busyItemId.set(null);
				this.load();
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to add photo', 'danger');
				this.busyItemId.set(null);
			},
		});
	}

	onReplaceFile(event: Event, image: StoreItemImageDto): void {
		const file = this.takeFile(event);
		if (!file) return;

		this.busyItemId.set(image.storeItemId);
		this.store.replaceItemImage(image.storeItemId, image.instance, file).subscribe({
			next: () => {
				this.toast.show('Photo replaced', 'success');
				this.busyItemId.set(null);
				this.load();
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to replace photo', 'danger');
				this.busyItemId.set(null);
			},
		});
	}

	/**
	 * Pull the file off the input and clear the input's value, so picking the SAME file again
	 * still fires a change event — the usual "re-upload after a failure does nothing" trap.
	 */
	private takeFile(event: Event): File | null {
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0] ?? null;
		input.value = '';
		return file;
	}

	// ═══════════════════════════════════════
	//  DELETE
	// ═══════════════════════════════════════

	confirmDelete(image: StoreItemImageDto): void {
		this.deleteTarget.set(image);
		this.showDeleteConfirm.set(true);
	}

	cancelDelete(): void {
		this.showDeleteConfirm.set(false);
		this.deleteTarget.set(null);
	}

	executeDelete(): void {
		const image = this.deleteTarget();
		if (!image) return;

		this.showDeleteConfirm.set(false);
		this.busyItemId.set(image.storeItemId);

		this.store.deleteItemImage(image.storeItemId, image.instance).subscribe({
			next: () => {
				this.toast.show('Photo deleted', 'success');
				this.deleteTarget.set(null);
				this.busyItemId.set(null);
				this.load();
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to delete photo', 'danger');
				this.deleteTarget.set(null);
				this.busyItemId.set(null);
			},
		});
	}

	// ── Template helpers ──

	isBusy(storeItemId: number): boolean {
		return this.busyItemId() === storeItemId;
	}

	isFull(group: ItemImageGroup): boolean {
		return group.images.length >= this.maxImagesPerItem;
	}
}
