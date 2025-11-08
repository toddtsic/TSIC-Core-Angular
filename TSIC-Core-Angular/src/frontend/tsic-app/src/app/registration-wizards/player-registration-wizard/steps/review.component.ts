import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
  selector: 'app-rw-review',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Review & Submit</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Summary of selections will show here. Payment step comes next.</p>

        @if (state.waiverDefinitions().length > 0) {
          <div class="mb-3 p-3 border rounded bg-body-tertiary">
            <div class="d-flex justify-content-between align-items-center mb-2">
              <div class="fw-semibold">Waivers</div>
              <span class="badge" [ngClass]="state.allRequiredWaiversAccepted() ? 'bg-success' : 'bg-warning text-dark'">
                {{ state.allRequiredWaiversAccepted() ? 'All required accepted' : 'Pending acceptance' }}
              </span>
            </div>
            <ul class="small mb-0">
              @for (w of state.waiverDefinitions(); track w.id) {
                <li>
                  <i class="bi" [ngClass]="state.isWaiverAccepted(w.id) ? 'bi-check-circle text-success' : 'bi-exclamation-circle text-warning' "></i>
                  <span class="ms-1">{{ w.title }}</span>
                </li>
              }
            </ul>
            @if (state.requireSignature()) {
              <div class="small mt-2 text-muted">
                Signature: <strong>{{ state.signatureName() || '—' }}</strong>
                <span class="ms-2">(Role: {{ state.signatureRole() || '—' }})</span>
              </div>
            }
          </div>
        }
        <div class="rw-bottom-nav d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" (click)="next.emit()">Proceed to Payment</button>
        </div>
      </div>
    </div>
  `
})
export class ReviewComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  constructor(public state: RegistrationWizardService) { }
}
