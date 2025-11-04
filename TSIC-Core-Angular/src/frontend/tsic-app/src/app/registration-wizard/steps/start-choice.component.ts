import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

export type StartChoice = 'new' | 'edit' | 'parent';

@Component({
    selector: 'app-rw-start-choice',
    standalone: true,
    imports: [CommonModule, FormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Start new or edit previous registration</h5>
      </div>
      <div class="card-body">
        <h3 class="fw-bold text-primary mb-4">START NEW or EDIT PREVIOUS Registration</h3>

        <div class="form-check mb-2">
          <input class="form-check-input" type="radio" name="rwStart" id="rwStartNew" [(ngModel)]="choice" value="new">
          <label class="form-check-label" for="rwStartNew">
            Start a NEW registration FOR THIS EVENT
          </label>
        </div>
        <div class="form-check mb-2">
          <input class="form-check-input" type="radio" name="rwStart" id="rwStartEdit" [(ngModel)]="choice" value="edit">
          <label class="form-check-label" for="rwStartEdit">
            EDIT a Previous Registration FOR THIS EVENT
          </label>
        </div>
        <div class="form-check mb-3">
          <input class="form-check-input" type="radio" name="rwStart" id="rwStartParent" [(ngModel)]="choice" value="parent">
          <label class="form-check-label" for="rwStartParent">
            FOR PARENTS: Update my Player's UNIFORM NUMBER and/or TEAM ASSIGNMENT or DELETE my Player's registration for this event
          </label>
        </div>

        <button type="button" class="btn btn-primary" [disabled]="!choice" (click)="onContinue()">Continue</button>
      </div>
    </div>
  `
})
export class StartChoiceComponent {
    @Output() selected = new EventEmitter<StartChoice>();
    choice: StartChoice | '' = '';

    onContinue(): void {
        if (this.choice) this.selected.emit(this.choice);
    }
}
