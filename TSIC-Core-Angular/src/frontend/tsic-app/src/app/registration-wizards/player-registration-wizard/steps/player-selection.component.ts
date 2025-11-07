import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
  selector: 'app-rw-player-selection',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Players</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">This is a placeholder for player selection. We'll list family members here.</p>
        <div class="rw-bottom-nav d-flex gap-2">
          <button type="button" class="btn btn-primary" (click)="next.emit()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class PlayerSelectionComponent {
  @Output() next = new EventEmitter<void>();
  constructor(public state: RegistrationWizardService) { }
}
