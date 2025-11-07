import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';

export type StartChoice = 'new' | 'edit' | 'parent';

@Component({
  selector: 'app-rw-start-choice',
  standalone: true,
  imports: [CommonModule],
  styleUrls: ['./start-choice.component.scss'],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header gradient-header border-0 py-4 text-center text-white">
        <h5 class="mb-1 fw-semibold">Get started with Player registration</h5>
        <p class="mb-0 small opacity-75">Choose the path that fits what you want to do</p>
      </div>
        <div class="card-body">
          <fieldset role="radiogroup" aria-labelledby="rwStartLegend" class="d-block">
            <legend id="rwStartLegend" class="visually-hidden">Registration start options</legend>

            <div class="list-group list-group-flush">
              @if (canShowNew()) {
                <label class="list-group-item d-flex align-items-start gap-3 py-3 selectable">
                  <input class="form-check-input mt-1" type="radio" name="rwStart" (change)="choose('new')" />
                  <div>
                    <div class="fw-semibold">Start a new registration</div>
                    <div class="text-muted small">Begin a fresh registration for this event</div>
                  </div>
                </label>
              }

              @if (canShowEdit()) {
                <label class="list-group-item d-flex align-items-start gap-3 py-3 selectable">
                  <input class="form-check-input mt-1" type="radio" name="rwStart" (change)="choose('edit')" />
                  <div>
                    <div class="fw-semibold">Edit a previous registration</div>
                    <div class="text-muted small">Look up a prior submission to make changes</div>
                  </div>
                </label>
              }

              <label class="list-group-item d-flex align-items-start gap-3 py-3 selectable">
                <input class="form-check-input mt-1" type="radio" name="rwStart" (change)="choose('parent')" />
                <div>
                  <div class="fw-semibold">Review/Update Family Account</div>
                  <div class="text-muted small">Open your Family Account to review or update parent/child info, then return here</div>
                </div>
              </label>
            </div>
          </fieldset>
        </div>
      </div>
    `
})
export class StartChoiceComponent {
  readonly state = inject(RegistrationWizardService);
  @Output() selected = new EventEmitter<StartChoice>();
  choose(val: StartChoice): void { this.selected.emit(val); }

  // Visibility rules:
  // - If existingRegistrationAvailable === true, prefer Edit (hide New)
  // - If === false, prefer New (hide Edit)
  // - If null/unknown, show both
  canShowNew(): boolean {
    const v = this.state.existingRegistrationAvailable();
    return (v === null) || (v === false);
  }

  canShowEdit(): boolean {
    const v = this.state.existingRegistrationAvailable();
    return (v === null) || (v === true);
  }
}
