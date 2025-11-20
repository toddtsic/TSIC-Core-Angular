import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-vi-charge-confirm-modal',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="modal fade show d-block" tabindex="-1" role="dialog" style="background: rgba(0,0,0,0.5)">
      <div class="modal-dialog" role="document">
        <div class="modal-content">
          <div class="modal-header">
            <h5 class="modal-title" *ngIf="viCcOnlyFlow; else combinedTitle">Confirm Insurance Purchase</h5>
            <ng-template #combinedTitle><h5 class="modal-title">Confirm Registration Payment + Insurance</h5></ng-template>
            <button type="button" class="btn-close" aria-label="Close" (click)="onCancel()"></button>
          </div>
          <div class="modal-body">
            <p>The premium(s) for {{ quotedPlayers.join(', ') }} will be charged by <strong>VERTICAL INSURANCE</strong> and not by <strong>TEAMSPORTSINFO.COM</strong>.</p>
            <p>You will receive an email at <strong>{{ email }}</strong> from <strong>VERTICAL INSURANCE</strong> immediately upon processing.</p>
            <p class="mb-2"><strong>Total Insurance Premium:</strong> {{ premiumTotal | currency }}</p>
            <hr class="my-2" *ngIf="!viCcOnlyFlow" />
            <p class="mb-0" *ngIf="!viCcOnlyFlow">Your TSIC payment will also be processed as selected.</p>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn btn-secondary" (click)="onCancel()">CANCEL</button>
            <button type="button" class="btn btn-primary" (click)="onConfirm()">OK</button>
          </div>
        </div>
      </div>
    </div>
  `
})
export class ViChargeConfirmModalComponent {
    @Input() quotedPlayers: string[] = [];
    @Input() premiumTotal = 0;
    @Input() email = '';
    @Input() viCcOnlyFlow = false;
    @Output() cancelled = new EventEmitter<void>();
    @Output() confirmed = new EventEmitter<void>();

    onCancel(): void { this.cancelled.emit(); }
    onConfirm(): void { this.confirmed.emit(); }
}
