import { Component, ChangeDetectionStrategy, inject, signal, computed, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { StoreService } from '../../../infrastructure/services/store.service';
import { ToastService } from '../../../shared-ui/toast.service';
import { EmailBodyEditorComponent } from '@shared-ui/components/email-body-editor/email-body-editor.component';
import type {
	StoreCampaignKind,
	StoreCampaignSetupDto,
	StoreAbandonedCartDto,
	EmailBatchJobStatus,
} from '@core/api';

/**
 * Store email campaigns — port of legacy StoreEmailAbandondedCarts,
 * StoreEmailFamiliesThatNeverUsed and StoreEmailFamiliesThatOrdered.
 *
 * Legacy gave each its own menu item and its own near-identical page. They are one screen here
 * with an audience selector, because the only thing a director actually chooses between them is
 * who gets the mail — the compose box, the tokens and the send button are the same three times.
 */
@Component({
	selector: 'app-store-campaigns-tab',
	standalone: true,
	imports: [CommonModule, FormsModule, EmailBodyEditorComponent],
	changeDetection: ChangeDetectionStrategy.OnPush,
	templateUrl: './store-campaigns-tab.component.html',
	styleUrl: './store-campaigns-tab.component.scss',
})
export class StoreCampaignsTabComponent {
	private readonly store = inject(StoreService);
	private readonly toast = inject(ToastService);

	/**
	 * The generated `StoreCampaignKind` is a bare `number` (the API serializes the enum by value),
	 * so the wire values are named here rather than re-declaring the enum as a local type.
	 */
	readonly ABANDONED_CARTS = 0;
	readonly NEVER_ORDERED = 1;
	readonly HAVE_ORDERED = 2;

	private readonly editor = viewChild(EmailBodyEditorComponent);

	readonly kind = signal<StoreCampaignKind>(this.ABANDONED_CARTS);
	readonly setup = signal<StoreCampaignSetupDto | null>(null);
	readonly isLoading = signal(false);
	readonly isSending = signal(false);
	readonly errorMessage = signal<string | null>(null);

	readonly subject = signal('');
	readonly body = signal('');

	/** Ticked cart batch ids. Legacy checked every row on load; so does this. */
	readonly selectedBatchIds = signal<Set<number>>(new Set());
	readonly expandedBatchId = signal<number | null>(null);

	readonly lastResult = signal<EmailBatchJobStatus | null>(null);

	// ═══════════════════════════════════════
	//  DERIVED
	// ═══════════════════════════════════════

	readonly isAbandoned = computed(() => this.kind() === this.ABANDONED_CARTS);

	readonly carts = computed(() => this.setup()?.abandonedCarts ?? []);

	readonly tokens = computed(() => this.setup()?.tokens ?? []);

	/** Who this send would actually reach right now. */
	readonly recipientCount = computed(() =>
		this.isAbandoned() ? this.selectedBatchIds().size : (this.setup()?.recipientCount ?? 0));

	readonly canSend = computed(() =>
		!this.isSending()
		&& !this.isLoading()
		&& this.recipientCount() > 0
		&& this.subject().trim().length > 0
		&& this.body().trim().length > 0);

	readonly audienceLabel = computed(() => {
		switch (this.kind()) {
			case this.NEVER_ORDERED: return 'families that have never ordered';
			case this.HAVE_ORDERED: return 'families that have ordered';
			default: return 'abandoned carts';
		}
	});

	constructor() {
		this.load();
	}

	// ═══════════════════════════════════════
	//  LOAD
	// ═══════════════════════════════════════

	selectKind(kind: StoreCampaignKind): void {
		if (this.kind() === kind || this.isSending()) return;
		this.kind.set(kind);
		this.lastResult.set(null);
		this.load();
	}

	/** Age-window change reloads the cart list, exactly as legacy's dropdowns reloaded the page. */
	setAgeWindow(min: number, max: number): void {
		const current = this.setup();
		if (!current) return;
		this.load(min, max);
	}

	onMinAgeChange(value: string): void {
		const current = this.setup();
		if (!current) return;
		this.setAgeWindow(Number(value), current.maxAgeHours);
	}

	onMaxAgeChange(value: string): void {
		const current = this.setup();
		if (!current) return;
		this.setAgeWindow(current.minAgeHours, Number(value));
	}

	load(minAgeHours?: number, maxAgeHours?: number): void {
		this.isLoading.set(true);
		this.errorMessage.set(null);

		this.store.getCampaignSetup(this.kind(), minAgeHours, maxAgeHours).subscribe({
			next: setup => {
				this.setup.set(setup);
				// Re-seed the composer from the server's template. The director's own edits are
				// deliberately discarded on an audience switch: the seeded body differs per
				// campaign, and carrying "you left items in your cart" onto the pickup blast
				// would be worse than losing a draft.
				this.subject.set(setup.defaultSubject);
				this.body.set(setup.defaultBody);
				this.selectedBatchIds.set(new Set(setup.abandonedCarts.map(c => c.batchId)));
				this.expandedBatchId.set(null);
				this.isLoading.set(false);
			},
			error: err => {
				this.errorMessage.set(err?.error?.message ?? 'Could not load this campaign.');
				this.isLoading.set(false);
			},
		});
	}

	// ═══════════════════════════════════════
	//  SELECTION
	// ═══════════════════════════════════════

	isSelected(batchId: number): boolean {
		return this.selectedBatchIds().has(batchId);
	}

	toggleCart(batchId: number): void {
		const next = new Set(this.selectedBatchIds());
		if (!next.delete(batchId)) next.add(batchId);
		this.selectedBatchIds.set(next);
	}

	readonly allSelected = computed(() =>
		this.carts().length > 0 && this.selectedBatchIds().size === this.carts().length);

	toggleAll(): void {
		this.selectedBatchIds.set(
			this.allSelected() ? new Set() : new Set(this.carts().map(c => c.batchId)));
	}

	toggleExpanded(batchId: number): void {
		this.expandedBatchId.set(this.expandedBatchId() === batchId ? null : batchId);
	}

	trackCart = (_: number, cart: StoreAbandonedCartDto) => cart.batchId;

	// ═══════════════════════════════════════
	//  COMPOSE + SEND
	// ═══════════════════════════════════════

	insertToken(token: string): void {
		this.editor()?.insertToken(token);
	}

	send(): void {
		if (!this.canSend()) return;

		this.isSending.set(true);
		this.errorMessage.set(null);
		this.lastResult.set(null);

		this.store.sendCampaignAndAwait(this.kind(), {
			subject: this.subject().trim(),
			body: this.body().trim(),
			batchIds: this.isAbandoned() ? [...this.selectedBatchIds()] : null,
		}).subscribe({
			next: status => {
				this.isSending.set(false);
				this.lastResult.set(status);

				const failed = status.failedAddresses?.length ?? 0;
				const optedOut = status.optedOut > 0 ? `, ${status.optedOut} opted out` : '';
				const message = `Sent ${status.sent} of ${status.totalRecipients}${optedOut}`;

				if (failed > 0) {
					this.toast.show(`${message}. ${failed} failed.`, 'warning', 6000);
				} else {
					this.toast.show(message, 'success', 4000);
				}

				// An abandoned-carts blast changes nothing about the carts, but the window may have
				// moved on while the batch ran — reload so the grid matches what was just mailed.
				if (this.isAbandoned()) this.load(this.setup()?.minAgeHours, this.setup()?.maxAgeHours);
			},
			error: err => {
				this.isSending.set(false);
				this.errorMessage.set(err?.error?.message ?? 'The campaign could not be sent.');
			},
		});
	}
}
