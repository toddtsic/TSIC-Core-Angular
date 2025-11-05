import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';

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
              <label class="list-group-item d-flex align-items-start gap-3 py-3 selectable">
                <input class="form-check-input mt-1" type="radio" name="rwStart" (change)="choose('new')" />
                <div>
                  <div class="fw-semibold">Start a new registration</div>
                  <div class="text-muted small">Begin a fresh registration for this event</div>
                </div>
              </label>

              <label class="list-group-item d-flex align-items-start gap-3 py-3 selectable">
                <input class="form-check-input mt-1" type="radio" name="rwStart" (change)="choose('edit')" />
                <div>
                  <div class="fw-semibold">Edit a previous registration</div>
                  <div class="text-muted small">Look up a prior submission to make changes</div>
                </div>
              </label>
            </div>
          </fieldset>
        </div>
      </div>
    `
})
export class StartChoiceComponent {
  @Output() selected = new EventEmitter<StartChoice>();
  choose(val: StartChoice): void { this.selected.emit(val); }
}
