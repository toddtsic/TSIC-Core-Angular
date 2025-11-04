import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
    selector: 'app-rw-payment',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Payment</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Payment UI will appear here (Pay In Full or Deposit).</p>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" (click)="submit()">Submit Registration</button>
        </div>
      </div>
    </div>
  `
})
export class PaymentComponent {
    @Output() back = new EventEmitter<void>();
    @Output() submitted = new EventEmitter<void>();
    constructor(public state: RegistrationWizardService) { }

    submit(): void {
        // Placeholder: emit completion
        this.submitted.emit();
    }
}
