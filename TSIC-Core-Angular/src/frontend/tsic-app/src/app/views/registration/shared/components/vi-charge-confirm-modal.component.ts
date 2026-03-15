import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

@Component({
  selector: 'app-vi-charge-confirm-modal',
  standalone: true,
  imports: [CurrencyPipe, TsicDialogComponent],
  styles: [`
      .vi-header { background: linear-gradient(90deg,var(--bs-primary),var(--brand-primary-dark, var(--bs-primary))); color: var(--bs-white); }
      .vi-header h5 { font-weight:600; letter-spacing:.5px; }
      .vi-body { background: var(--bs-body-bg); }
      .vi-section + .vi-section { border-top:1px solid var(--bs-border-color-translucent); margin-top:1rem; padding-top:1rem; }
      .vi-summary-list { list-style:none; margin:0; padding:0; }
      .vi-summary-list li { display:flex; gap:.75rem; align-items: flex-start; padding:.35rem 0; }
      .vi-summary-icon { width:1.25rem; text-align:center; opacity:.75; }
      .vi-total { font-size:1.05rem; font-weight:600; }
      .vi-brand { font-weight:600; }
      .vi-small { font-size:.825rem; color: var(--bs-secondary-color); }
    `],
  template: `
    <tsic-dialog [open]="true" (requestClose)="onCancel()">
      <div class="modal-content">
        <div class="modal-header vi-header">
          @if (viCcOnlyFlow) {
            <h5 class="modal-title">Confirm Insurance Purchase</h5>
          } @else {
            <h5 class="modal-title">Confirm Registration Payment + Insurance</h5>
          }
          <button type="button" class="btn-close btn-close-white" aria-label="Close" (click)="onCancel()"></button>
        </div>
        <div class="modal-body vi-body">
          <ul class="vi-summary-list" aria-label="Insurance purchase summary">
            <li>
              <span class="vi-summary-icon" aria-hidden="true">🧾</span>
              @if (viCcOnlyFlow) {
                <div>Insurance premium(s) for <strong>{{ quotedPlayers.join(', ') }}</strong> will be charged by <span class="vi-brand">VERTICAL INSURANCE</span>.</div>
              } @else {
                <div>The registration insurance premium(s) for <strong>{{ quotedPlayers.join(', ') }}</strong> will be charged by <span class="vi-brand">VERTICAL INSURANCE</span> (not <span class="vi-brand">TEAMSPORTSINFO.COM</span>).</div>
              }
            </li>
            <li>
              <span class="vi-summary-icon" aria-hidden="true">📧</span>
              <div>An email receipt will be sent to <strong>{{ email }}</strong> immediately after processing.</div>
            </li>
            <li class="vi-total">
              <span class="vi-summary-icon" aria-hidden="true">💵</span>
              @if (viCcOnlyFlow) {
                <div>Total Insurance Premium: <span>{{ premiumTotal | currency }}</span></div>
              } @else {
                <div>Total Insurance Premium (in addition to your TSIC payment): <span>{{ premiumTotal | currency }}</span></div>
              }
            </li>
            @if (!viCcOnlyFlow) {
              <li>
                <span class="vi-summary-icon" aria-hidden="true">💳</span>
                <div>Your TSIC registration payment will also be processed now.</div>
              </li>
            }
          </ul>
          <div class="vi-section vi-small">
            <div>By clicking <strong>Confirm</strong>, you authorize the charges listed above. Policies are issued by Vertical Insure; for questions, reply to the receipt email.</div>
          </div>
        </div>
        <div class="modal-footer justify-content-between">
          <button type="button" class="btn btn-outline-secondary" (click)="onCancel()">Back</button>
          <div class="d-flex gap-2">
            <button type="button" class="btn btn-secondary" (click)="onCancel()">Cancel</button>
            <button type="button" class="btn btn-primary" (click)="onConfirm()">Confirm</button>
          </div>
        </div>
      </div>
    </tsic-dialog>
    `,
    changeDetection: ChangeDetectionStrategy.OnPush
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
