import { Component, ChangeDetectionStrategy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { TsicDialogComponent } from '../../../shared-ui/components/tsic-dialog/tsic-dialog.component';
import { StoreAdjustmentsTabComponent } from './store-adjustments-tab.component';
import type {
	StoreSaleLineDto,
	StoreSwapOptionDto,
	StoreBatchSettledStatusDto,
} from '@core/api';

/**
 * Sales operations — what a director does to a sale after the money has moved.
 * Port of legacy StoreSales/Index and StoreSalesWalkup/Index with their row commands.
 *
 * Legacy shipped these as two screens differing only by a filter; here it is one grid with a
 * walk-up toggle, which is also how a director thinks about it at the table.
 */
@Component({
	selector: 'app-store-sales-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, TsicDialogComponent, StoreAdjustmentsTabComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-sales-tab.component.html',
	styleUrl: './store-sales-tab.component.scss',
})
export class StoreSalesTabComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly isLoading = signal(false);
	readonly isSaving = signal(false);
	readonly errorMessage = signal<string | null>(null);
	readonly lines = signal<StoreSaleLineDto[]>([]);

	/**
	 * Which of legacy's "Sales Tables" screens is on show. Refunds are a row command here rather
	 * than a third screen, so the group is these two.
	 */
	readonly view = signal<'sales' | 'adjustments'>('sales');

	/** Legacy's StoreSalesWalkup screen, as a filter. */
	readonly walkUpOnly = signal(false);
	readonly search = signal('');

	// ── Swap dialog ──
	readonly showSwapModal = signal(false);
	readonly swapLine = signal<StoreSaleLineDto | null>(null);
	readonly swapOptions = signal<StoreSwapOptionDto[]>([]);
	readonly isLoadingSwapOptions = signal(false);
	readonly swapTargetSkuId = signal<number | null>(null);
	readonly swapQuantity = signal(1);

	// ── Refund dialog ──
	readonly showRefundModal = signal(false);
	readonly refundLine = signal<StoreSaleLineDto | null>(null);
	readonly batchStatus = signal<StoreBatchSettledStatusDto | null>(null);
	readonly isLoadingStatus = signal(false);
	readonly refundAmount = signal(0);
	readonly refundRestockCount = signal(0);
	readonly refundReason = signal('');
	readonly voidEntireBatch = signal(false);

	// ═══════════════════════════════════════
	//  DERIVED
	// ═══════════════════════════════════════

	readonly visibleLines = computed(() => {
		const term = this.search().trim().toLowerCase();
		if (!term) return this.lines();

		return this.lines().filter(l =>
			l.skuLabel.toLowerCase().includes(term)
			|| l.familyUserName.toLowerCase().includes(term)
			|| `${l.directToFirstName ?? ''} ${l.directToLastName ?? ''}`.toLowerCase().includes(term)
			|| (l.directToTeam ?? '').toLowerCase().includes(term)
			|| (l.directToClub ?? '').toLowerCase().includes(term));
	});

	readonly totalPaid = computed(() =>
		this.visibleLines().reduce((sum, l) => sum + l.paid, 0));

	readonly totalRefunded = computed(() =>
		this.visibleLines().reduce((sum, l) => sum + l.refunded, 0));

	readonly totalUnits = computed(() =>
		this.visibleLines().reduce((sum, l) => sum + l.quantity, 0));

	/**
	 * An unsettled charge can only be VOIDED in full — Authorize.Net has no partial void — so the
	 * dialog offers the void and hides the amount box rather than promising a partial that the
	 * gateway would silently turn into a full reversal.
	 */
	readonly mustVoid = computed(() => {
		const status = this.batchStatus();
		return status !== null && status.hasCardPayment && !status.isSettled;
	});

	readonly canRefund = computed(() => {
		if (this.isSaving() || this.isLoadingStatus()) return false;
		const line = this.refundLine();
		if (!line) return false;
		if (this.voidEntireBatch() || this.mustVoid()) return true;
		return this.refundAmount() > 0 && this.refundAmount() <= line.maxCanRefund;
	});

	readonly canSwap = computed(() => {
		const target = this.swapOptions().find(o => o.storeSkuId === this.swapTargetSkuId());
		if (!target || this.isSaving()) return false;
		const line = this.swapLine();
		if (!line) return false;
		return this.swapQuantity() >= 1
			&& this.swapQuantity() <= line.quantity
			&& this.swapQuantity() <= target.availableCount;
	});

	constructor() {
		this.load();
	}

	// ═══════════════════════════════════════
	//  DATA
	// ═══════════════════════════════════════

	load(): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);
		this.store.getSaleLines(this.walkUpOnly()).subscribe({
			next: lines => {
				this.lines.set(lines);
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Failed to load sales');
				this.isLoading.set(false);
			},
		});
	}

	toggleWalkUpOnly(): void {
		this.walkUpOnly.set(!this.walkUpOnly());
		this.load();
	}

	// ═══════════════════════════════════════
	//  SWAP
	// ═══════════════════════════════════════

	openSwap(line: StoreSaleLineDto): void {
		this.swapLine.set(line);
		this.swapTargetSkuId.set(null);
		this.swapQuantity.set(line.quantity);
		this.swapOptions.set([]);
		this.isLoadingSwapOptions.set(true);
		this.showSwapModal.set(true);

		this.store.getSwapOptions(line.storeCartBatchSkuId).subscribe({
			next: options => {
				this.swapOptions.set(options);
				this.isLoadingSwapOptions.set(false);
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Failed to load exchange options', 'danger');
				this.isLoadingSwapOptions.set(false);
			},
		});
	}

	confirmSwap(): void {
		const line = this.swapLine();
		const targetId = this.swapTargetSkuId();
		if (!line || targetId === null || !this.canSwap()) return;

		this.isSaving.set(true);
		this.store.swapCartSku({
			storeCartBatchSkuId: line.storeCartBatchSkuId,
			newStoreSkuId: targetId,
			quantity: this.swapQuantity(),
		}).subscribe({
			next: () => {
				this.toast.show('Exchange recorded', 'success');
				this.showSwapModal.set(false);
				this.isSaving.set(false);
				this.load();
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Exchange failed', 'danger');
				this.isSaving.set(false);
			},
		});
	}

	// ═══════════════════════════════════════
	//  REFUND / VOID
	// ═══════════════════════════════════════

	openRefund(line: StoreSaleLineDto): void {
		this.refundLine.set(line);
		this.refundAmount.set(line.maxCanRefund);
		this.refundRestockCount.set(line.maxCanRestock);
		this.refundReason.set('');
		this.voidEntireBatch.set(false);
		this.batchStatus.set(null);
		this.isLoadingStatus.set(true);
		this.showRefundModal.set(true);

		// LEGACY GetCartBatchHasSettledStatus — asked before the dialog is usable, because the
		// answer changes what may be offered.
		this.store.getBatchSettledStatus(line.storeCartBatchId).subscribe({
			next: status => {
				this.batchStatus.set(status);
				this.isLoadingStatus.set(false);
			},
			error: err => {
				this.toast.show(
					err?.error?.message || 'Could not check the payment status', 'danger');
				this.isLoadingStatus.set(false);
			},
		});
	}

	confirmRefund(): void {
		const line = this.refundLine();
		if (!line || !this.canRefund()) return;

		const isVoid = this.voidEntireBatch() || this.mustVoid();

		this.isSaving.set(true);
		this.store.refundSale({
			storeCartBatchSkuId: line.storeCartBatchSkuId,
			voidEntireBatch: isVoid,
			refundAmount: isVoid ? 0 : this.refundAmount(),
			restockCount: isVoid ? 0 : this.refundRestockCount(),
			reason: this.refundReason().trim() || null,
		}).subscribe({
			next: result => {
				this.isSaving.set(false);
				// A gateway refusal arrives as success:false, not an HTTP error — show it and
				// leave the dialog open so the director can adjust.
				this.toast.show(result.message, result.success ? 'success' : 'danger');
				if (result.success) {
					this.showRefundModal.set(false);
					this.load();
				}
			},
			error: err => {
				this.toast.show(err?.error?.message || 'Refund failed', 'danger');
				this.isSaving.set(false);
			},
		});
	}

	// ═══════════════════════════════════════
	//  TEMPLATE HELPERS
	// ═══════════════════════════════════════

	formatCurrency(value: number): string {
		return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(value);
	}

	directToName(line: StoreSaleLineDto): string {
		const name = `${line.directToFirstName ?? ''} ${line.directToLastName ?? ''}`.trim();
		return name || line.familyUserName;
	}

	/** A line is fully reversed when nothing paid remains on it. */
	isFullyRefunded(line: StoreSaleLineDto): boolean {
		return line.maxCanRefund <= 0;
	}
}
