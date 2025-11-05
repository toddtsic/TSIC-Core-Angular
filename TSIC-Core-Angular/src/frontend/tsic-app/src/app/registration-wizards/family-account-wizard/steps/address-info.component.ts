import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { FormFieldDataService, SelectOption } from '../../../core/services/form-field-data.service';
import { FamilyAccountWizardService } from '../family-account-wizard.service';

@Component({
    selector: 'app-fam-account-step-address',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Address</h5>
      </div>
      <div class="card-body">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="row g-3">
          <div class="col-12">
            <label class="form-label" for="addr1">Street Address</label>
            <input id="addr1" type="text" formControlName="address1" class="form-control" [class.is-invalid]="submitted && form.controls.address1.invalid" />
            <div class="invalid-feedback" *ngIf="submitted && form.controls.address1.errors?.['required']">Required</div>
          </div>
          <div class="col-12 col-md-6">
            <label class="form-label" for="city">City</label>
            <input id="city" type="text" formControlName="city" class="form-control" [class.is-invalid]="submitted && form.controls.city.invalid" />
            <div class="invalid-feedback" *ngIf="submitted && form.controls.city.errors?.['required']">Required</div>
          </div>
          <div class="col-6 col-md-3">
            <label class="form-label" for="state">State</label>
            <select id="state" formControlName="state" class="form-select" [class.is-invalid]="submitted && form.controls.state.invalid">
              <option value="" disabled>Select a state</option>
              <option *ngFor="let s of statesOptions" [value]="s.value">{{ s.label }}</option>
            </select>
            <div class="invalid-feedback" *ngIf="submitted && form.controls.state.errors?.['required']">Required</div>
          </div>
          <div class="col-6 col-md-3">
            <label class="form-label" for="postal">ZIP Code</label>
            <input id="postal" type="text" formControlName="postalCode" class="form-control" [class.is-invalid]="submitted && form.controls.postalCode.invalid" />
            <div class="invalid-feedback" *ngIf="submitted && form.controls.postalCode.errors">
              <span *ngIf="form.controls.postalCode.errors['required']">Required</span>
              <span *ngIf="form.controls.postalCode.errors['pattern']">Invalid code</span>
            </div>
          </div>

          <div class="d-flex gap-2 mt-2">
            <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
            <button type="submit" class="btn btn-primary">Continue</button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class FamAccountStepAddressComponent {
    @Output() next = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();

    private readonly fb = inject(FormBuilder);
    private readonly state = inject(FamilyAccountWizardService);
    private readonly fieldData = inject(FormFieldDataService);

    submitted = false;
    statesOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('states');

    form = this.fb.group({
        address1: [this.state.address1(), [Validators.required]],
        city: [this.state.city(), [Validators.required]],
        state: [this.state.state(), [Validators.required]],
        postalCode: [this.state.postalCode(), [Validators.required, Validators.pattern(/^[A-Za-z0-9\-\s]{3,10}$/)]]
    });

    submit(): void {
        this.submitted = true;
        if (this.form.invalid) return;
        const v = this.form.value;
        this.state.address1.set(v.address1 ?? '');
        this.state.city.set(v.city ?? '');
        this.state.state.set(v.state ?? '');
        this.state.postalCode.set(v.postalCode ?? '');
        this.next.emit();
    }
}
