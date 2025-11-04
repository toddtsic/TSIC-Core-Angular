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
        <div class="d-flex gap-2">
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
