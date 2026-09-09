import { Component, ChangeDetectionStrategy, OnDestroy, inject, signal, computed, Renderer2 } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { ConfirmDialogComponent } from '../../../shared-ui/components/confirm-dialog/confirm-dialog.component';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
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
 *
 * Both uploads — add and replace — go through one dialog rather than a hidden file input on the
 * tile. The dialog exists for the drop target: the photo tiles abut each other in a wrapped strip,
 * and a drop landing a tile off would silently overwrite a DIFFERENT product's photo, which the
 * server cannot detect (it writes over the same {storeId}-{itemId}-{instance}.jpg path) and no one
 * can undo. Inside the dialog there is exactly one drop target, it is named, and the chosen file is
 * previewed before it commits.
 */
@Component({
	selector: 'app-store-images-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, ConfirmDialogComponent, TsicDialogComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-images-tab.component.html',
	styleUrl: './store-images-tab.component.scss',
})
export class StoreImagesTabComponent implements OnDestroy {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);
	private readonly renderer = inject(Renderer2);

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

	ngOnDestroy(): void {
		this.releasePreview();
		this.removeDropGuard();
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
	//  UPLOAD DIALOG
	// ═══════════════════════════════════════

	/**
	 * The server's own gate, restated here.
	 *
	 * StoreImageService remains the enforcer; these two constants mirror its AllowedExtensions and
	 * MaxFileSize so a bad file is refused before it is uploaded. Extension, not MIME type: the
	 * server checks Path.GetExtension, and matching on the browser's guessed content type would
	 * refuse files the server would have taken.
	 */
	private static readonly AllowedExtensions = ['.jpg', '.jpeg', '.png', '.webp'];
	private static readonly MaxFileBytes = 5 * 1024 * 1024;

	readonly uploadMode = signal<'add' | 'replace' | null>(null);
	/** Add target. */
	readonly uploadGroup = signal<ItemImageGroup | null>(null);
	/** Replace target — carries the instance number and the photo being written over. */
	readonly uploadImage = signal<StoreItemImageDto | null>(null);

	readonly pendingFile = signal<File | null>(null);
	readonly pendingPreviewUrl = signal<string | null>(null);
	readonly uploadError = signal<string | null>(null);
	readonly isDragOver = signal(false);
	readonly isUploading = signal(false);

	/** The product the open dialog is acting on, whichever mode it is in. */
	readonly uploadItemName = computed(() =>
		this.uploadGroup()?.storeItemName ?? this.uploadImage()?.storeItemName ?? '');

	readonly uploadTitle = computed(() => {
		const image = this.uploadImage();
		if (this.uploadMode() === 'replace' && image) return `Replace photo ${image.instance}`;
		return (this.uploadGroup()?.images.length ?? 0) === 0 ? 'Add a photo' : 'Add another photo';
	});

	/** Accept list for the file picker. The drop path validates by extension instead. */
	readonly acceptTypes = 'image/jpeg,image/png,image/webp';

	openAdd(group: ItemImageGroup): void {
		if (this.isBusy(group.storeItemId) || this.isFull(group)) return;
		this.resetUploadState();
		this.uploadGroup.set(group);
		this.uploadMode.set('add');
		this.installDropGuard();
	}

	openReplace(image: StoreItemImageDto): void {
		if (this.isBusy(image.storeItemId)) return;
		this.resetUploadState();
		this.uploadImage.set(image);
		this.uploadMode.set('replace');
		this.installDropGuard();
	}

	closeUpload(): void {
		if (this.isUploading()) return;
		this.resetUploadState();
		this.removeDropGuard();
	}

	private resetUploadState(): void {
		this.releasePreview();
		this.uploadMode.set(null);
		this.uploadGroup.set(null);
		this.uploadImage.set(null);
		this.pendingFile.set(null);
		this.uploadError.set(null);
		this.isDragOver.set(false);
		this.dragDepth = 0;
	}

	// ── Choosing a file ──

	onPickFile(event: Event): void {
		const input = event.target as HTMLInputElement;
		const file = input.files?.[0] ?? null;
		// Clear the input so picking the SAME file again still fires a change event — the usual
		// "re-choose after a failure does nothing" trap.
		input.value = '';
		if (file) this.setPendingFile(file);
	}

	/** Open the picker from the drop zone itself, without the inner button opening it twice. */
	openPicker(picker: HTMLInputElement, event?: Event): void {
		event?.stopPropagation();
		if (this.isUploading()) return;
		picker.click();
	}

	onDragEnter(event: DragEvent): void {
		event.preventDefault();
		if (this.isUploading()) return;
		this.dragDepth++;
		this.isDragOver.set(true);
	}

	onDragOver(event: DragEvent): void {
		// Without this the drop never fires — the zone is not a drop target until the default
		// "you cannot drop here" is prevented on every dragover.
		event.preventDefault();
		if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
	}

	/**
	 * dragleave fires on every child element the pointer crosses, so a plain set(false) makes the
	 * highlight flicker as the pointer moves over the icon or the caption. Count enters against
	 * leaves and only drop the highlight when the pointer has actually left the zone.
	 */
	onDragLeave(event: DragEvent): void {
		event.preventDefault();
		this.dragDepth = Math.max(0, this.dragDepth - 1);
		if (this.dragDepth === 0) this.isDragOver.set(false);
	}

	onDrop(event: DragEvent): void {
		event.preventDefault();
		this.dragDepth = 0;
		this.isDragOver.set(false);
		if (this.isUploading()) return;

		const files = event.dataTransfer?.files;
		if (!files || files.length === 0) return;

		if (files.length > 1) {
			this.uploadError.set(
				`${files.length} files were dropped. Photos are added one at a time — ` +
				`"${files[0].name}" was taken.`);
			this.applyFile(files[0], false);
			return;
		}

		this.setPendingFile(files[0]);
	}

	private setPendingFile(file: File): void {
		this.applyFile(file, true);
	}

	/** `clearError` is false when the caller has already written a message worth keeping. */
	private applyFile(file: File, clearError: boolean): void {
		const problem = this.validateFile(file);
		if (problem) {
			this.releasePreview();
			this.pendingFile.set(null);
			this.uploadError.set(problem);
			return;
		}

		if (clearError) this.uploadError.set(null);
		this.releasePreview();
		this.pendingFile.set(file);
		this.pendingPreviewUrl.set(URL.createObjectURL(file));
	}

	private validateFile(file: File): string | null {
		const dot = file.name.lastIndexOf('.');
		const ext = dot >= 0 ? file.name.slice(dot).toLowerCase() : '';

		if (!StoreImagesTabComponent.AllowedExtensions.includes(ext)) {
			return `"${file.name}" is not a photo we can use. Choose a JPG, PNG, or WebP file.`;
		}

		if (file.size > StoreImagesTabComponent.MaxFileBytes) {
			const mb = (file.size / (1024 * 1024)).toFixed(1);
			return `That photo is ${mb} MB. Photos must be 5 MB or smaller.`;
		}

		return null;
	}

	private releasePreview(): void {
		const url = this.pendingPreviewUrl();
		if (url) URL.revokeObjectURL(url);
		this.pendingPreviewUrl.set(null);
	}

	/**
	 * A drop that misses the zone would otherwise be handled by the browser, which navigates away
	 * to the dropped file and abandons the page. Preventing dragover and drop at the window makes
	 * the whole page a target that does nothing; the zone's own handlers have already run by then.
	 * Installed only while the dialog is open.
	 */
	private dropGuards: Array<() => void> = [];
	private dragDepth = 0;

	private installDropGuard(): void {
		if (this.dropGuards.length > 0) return;
		const swallow = (event: DragEvent) => event.preventDefault();
		this.dropGuards.push(
			this.renderer.listen('window', 'dragover', swallow),
			this.renderer.listen('window', 'drop', swallow),
		);
	}

	private removeDropGuard(): void {
		for (const dispose of this.dropGuards) dispose();
		this.dropGuards = [];
	}

	// ═══════════════════════════════════════
	//  UPLOAD
	// ═══════════════════════════════════════

	/**
	 * Commit the previewed file. Deletion renumbers instances server-side, so every mutation
	 * re-fetches rather than patching the local list — the instance numbers a user is looking at
	 * can shift. A failure leaves the dialog open with the file still chosen, so a retry does not
	 * mean finding it again.
	 */
	executeUpload(): void {
		const file = this.pendingFile();
		const mode = this.uploadMode();
		if (!file || !mode || this.isUploading()) return;

		const image = this.uploadImage();
		const group = this.uploadGroup();
		const storeItemId = mode === 'replace' ? image?.storeItemId : group?.storeItemId;
		if (storeItemId === undefined) return;

		const request$ = mode === 'replace'
			? this.store.replaceItemImage(storeItemId, image!.instance, file)
			: this.store.addItemImage(storeItemId, file);

		this.isUploading.set(true);
		this.busyItemId.set(storeItemId);
		this.uploadError.set(null);

		request$.subscribe({
			next: () => {
				const name = this.uploadItemName();
				this.toast.show(
					mode === 'replace' ? 'Photo replaced' : `Photo added to ${name}`, 'success');
				this.isUploading.set(false);
				this.busyItemId.set(null);
				this.resetUploadState();
				this.removeDropGuard();
				this.load();
			},
			error: err => {
				const message = err?.error?.message
					|| (mode === 'replace' ? 'Failed to replace photo' : 'Failed to add photo');
				this.uploadError.set(message);
				this.toast.show(message, 'danger');
				this.isUploading.set(false);
				this.busyItemId.set(null);
			},
		});
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
