import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';

/**
 * Legacy's `StoreFamily/Invoices` — the shopper's own purchase history, with the receipt for any
 * one of them (A-28, A-29).
 *
 * <p>Legacy rendered an EJ2 grid whose two toolbar buttons acted on the SELECTED row, and
 * auto-selected row 0 on databound (A-30) so the toolbar was never dead on arrival. The actions
 * live on each row here instead: it is the same two operations without the select-then-act
 * indirection, it removes the "please select a row first" alert as a state that can happen, and
 * it matches the card layout every other shopper surface in this port uses (see A-12). A-30 has
 * nothing to port as a result — auto-selection only existed to prime the toolbar.</p>
 */
@Component({
	selector: 'app-store-invoices',
	standalone: true,
	imports: [CommonModule, RouterLink],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './invoices.component.html',
	styleUrl: './invoices.component.scss',
})
export class StoreInvoicesComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	readonly rows = this.store.purchaseHistory;
	readonly isLoading = signal(true);
	readonly errorMessage = signal<string | null>(null);

	/** The batch whose receipt is being emailed, so only its own button shows the pending state. */
	readonly emailingBatchId = signal<number | null>(null);

	constructor() {
		this.store.loadPurchaseHistory().subscribe({
			next: () => this.isLoading.set(false),
			error: err => {
				this.errorMessage.set(err?.error?.message || 'Could not load your purchases.');
				this.isLoading.set(false);
			},
		});
	}

	downloadReceipt(storeCartBatchId: number): void {
		this.store.downloadReceipt(storeCartBatchId);
	}

	emailReceipt(storeCartBatchId: number): void {
		if (this.emailingBatchId() !== null) return;

		this.emailingBatchId.set(storeCartBatchId);
		this.store.emailReceipt(storeCartBatchId).subscribe({
			next: result => {
				this.emailingBatchId.set(null);
				// Legacy toasted "sent SUCCESSFULLY" whatever came back, including when the family
				// has no address on file and nothing left the building. The server says which.
				if (result.sent) {
					this.toast.show(`Receipt emailed to ${result.recipients.join(', ')}`, 'success');
				} else {
					this.toast.show(result.reason || 'No email address on file for this purchase.', 'warning');
				}
			},
			error: () => {
				this.emailingBatchId.set(null);
				this.toast.show('Could not send the receipt. Please try again.', 'danger');
			},
		});
	}

	formatCurrency(value: number): string {
		return '$' + value.toFixed(2);
	}
}
