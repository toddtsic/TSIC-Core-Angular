import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';

@Component({
    selector: 'app-fam-account-step-account',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Create Parent Account</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Enter your details to create a family account. (Placeholder UI)</p>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-primary" (click)="next.emit()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class FamAccountStepAccountComponent {
    @Output() next = new EventEmitter<void>();
    constructor(public state: FamilyAccountWizardService) { }
}
