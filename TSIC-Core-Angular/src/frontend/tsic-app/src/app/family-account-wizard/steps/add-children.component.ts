import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';

@Component({
    selector: 'app-fam-account-step-children',
    standalone: true,
    imports: [CommonModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Add Children</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Add your players to the family account. (Placeholder UI)</p>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" (click)="next.emit()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class FamAccountStepChildrenComponent {
    @Output() next = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();
    constructor(public state: FamilyAccountWizardService) { }
}
