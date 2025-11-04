import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';

@Component({
    selector: 'app-fam-account-step-review',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Review & Save</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Confirm your account and player details. (Placeholder UI)</p>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-success" (click)="completed.emit()">Finish</button>
        </div>
      </div>
    </div>
  `
})
export class FamAccountStepReviewComponent {
    @Output() completed = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();
    constructor(public state: FamilyAccountWizardService) { }
}
