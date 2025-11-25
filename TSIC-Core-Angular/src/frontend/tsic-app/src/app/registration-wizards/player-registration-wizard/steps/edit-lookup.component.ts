import { Component, EventEmitter, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';

@Component({
    selector: 'app-rw-edit-lookup',
    standalone: true,
    imports: [CommonModule, MatButtonModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Find Previous Registration</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Search for your previous registration using email or confirmation code. (Placeholder UI)</p>
        <div class="d-flex gap-2">
          <button type="button" mat-stroked-button color="primary" (click)="back.emit()">Back</button>
          <button type="button" mat-raised-button color="primary" (click)="next.emit()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class EditLookupComponent {
    @Output() next = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();
}
