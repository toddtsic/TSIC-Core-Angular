import { Component, inject, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { InsuranceStateService } from '../services/insurance-state.service';

/**
 * VerticalInsure Confirmation Modal
 * Implements explicit opt-in/opt-out flow for RegSaver insurance prior to payment.
 * Purchase flow is decoupled from fee payment; we only gather user intent here.
 */
@Component({
  selector: 'app-vi-confirm-modal',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule],
  template: `
    <div class="modal-backdrop fade show" (click)="close(false)"></div>
    <div class="modal d-block" tabindex="-1" role="dialog" aria-modal="true">
      <div class="modal-dialog modal-lg modal-dialog-centered">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title">RegSaver Player Insurance</h5>
            <button type="button" mat-icon-button aria-label="Close" (click)="close(false)">
              <mat-icon>close</mat-icon>
            </button>
          </div>
          <div class="modal-body">
            <p class="mb-3">RegSaver player registration insurance is optional coverage offered for eligible events. It can reimburse certain fees under covered circumstances (e.g., injury, illness). Review the quote details below and choose whether to purchase. Declining will not affect your ability to continue registration.</p>
            <div id="viQuoteContainer" class="border rounded p-3 mb-3">
              <!-- VerticalInsure widget will render into this container via existing Payment component initialization logic -->
              <div class="text-center text-muted" *ngIf="!ready">Loading insurance options…</div>
            </div>
            <div *ngIf="error" class="alert alert-warning small">{{ error }}</div>
            <div *ngIf="quotes?.length" class="mb-3">
              <h6 class="fw-semibold">Available Quotes</h6>
              <ul class="list-unstyled small mb-0">
                <li *ngFor="let q of quotes">• {{ q?.planName || q?.name || 'Plan' }} – {{ q?.price | currency }}</li>
              </ul>
            </div>
          </div>
          <div class="modal-footer d-flex flex-column flex-sm-row gap-2">
            <button type="button" mat-stroked-button color="primary" class="w-100 w-sm-auto" (click)="decline()">Decline Insurance</button>
            <button type="button" mat-raised-button color="primary" class="w-100 w-sm-auto" [disabled]="!canConfirm()" (click)="confirm()">Confirm Purchase</button>
          </div>
          <div class="px-3 pb-3 small text-muted">By confirming purchase you agree to the insurance provider's terms. A policy number will be returned after successful processing.</div>
        </div>
      </div>
    </div>
  `
})
export class ViConfirmModalComponent {
  readonly insuranceState = inject(InsuranceStateService);

  @Input() quotes: any[] | null = null;
  @Input() ready: boolean = false;
  @Input() error: string | null = null;
  @Output() confirmed = new EventEmitter<{ policyNumber: string | null; policyCreateDate: string | null; quotes: any[] }>();
  @Output() declined = new EventEmitter<void>();
  @Output() closed = new EventEmitter<void>();

  canConfirm(): boolean {
    // For MVP require at least one quote OR valid verticalInsure widget indicating user selected purchase.
    // This will be refined when we integrate deep widget state querying.
    return (this.quotes?.length ?? 0) > 0 || this.insuranceState.verticalInsureConfirmed();
  }

  confirm(): void {
    // Policy number will be populated later by purchase flow; we pass placeholders now.
    this.confirmed.emit({ policyNumber: this.insuranceState.viConsent()?.policyNumber ?? null, policyCreateDate: this.insuranceState.viConsent()?.policyCreateDate ?? null, quotes: this.quotes || [] });
  }

  decline(): void {
    this.declined.emit();
  }

  close(emitted: boolean): void {
    if (!emitted) this.closed.emit();
  }
}