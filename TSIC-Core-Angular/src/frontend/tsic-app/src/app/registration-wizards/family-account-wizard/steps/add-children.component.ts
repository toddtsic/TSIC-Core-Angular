import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, ValidatorFn, AbstractControl } from '@angular/forms';
import { FormFieldDataService, SelectOption } from '../../../core/services/form-field-data.service';
import { FamilyAccountWizardService } from '../family-account-wizard.service';

@Component({
  selector: 'app-fam-account-step-children',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Add Children</h5>
      </div>
      <div class="card-body">
        <form [formGroup]="form" (ngSubmit)="addChild()" class="row g-3">
          <div class="col-12 col-md-3">
            <label class="form-label" for="childFirst">First name</label>
            <input id="childFirst" type="text" formControlName="firstName" class="form-control" [class.is-invalid]="submitted && form.controls.firstName.invalid" />
            @if (submitted && form.controls.firstName.errors?.['required']) { <div class="invalid-feedback">Required</div> }
          </div>
          <div class="col-12 col-md-3">
            <label class="form-label" for="childLast">Last name</label>
            <input id="childLast" type="text" formControlName="lastName" class="form-control" [class.is-invalid]="submitted && form.controls.lastName.invalid" />
            @if (submitted && form.controls.lastName.errors?.['required']) { <div class="invalid-feedback">Required</div> }
          </div>
          <div class="col-12 col-md-3">
            <label class="form-label" for="childGender">Gender</label>
            <select id="childGender" formControlName="gender" class="form-select" [class.is-invalid]="submitted && form.controls.gender.invalid">
              <option value="" disabled>Select</option>
              @for (g of genderOptions; track g.value) { <option [value]="g.value">{{ g.label }}</option> }
            </select>
            @if (submitted && form.controls.gender.errors?.['required']) { <div class="invalid-feedback">Required</div> }
          </div>
          <div class="col-12 col-md-3">
            <label class="form-label" for="childDob">Date of birth</label>
            <input id="childDob" type="date" formControlName="dob" class="form-control"
                   [attr.min]="minDob" [attr.max]="maxDob"
                   [class.is-invalid]="submitted && form.controls.dob.invalid" />
            @if (submitted && form.controls.dob.errors?.['required']) { <div class="invalid-feedback">Required</div> }
            @if (submitted && (form.controls.dob.errors?.['ageTooYoung'] || form.controls.dob.errors?.['ageTooOld'])) {
              <div class="invalid-feedback">Age must be between 2 and 99 years.</div>
            }
          </div>

          <div class="col-12 col-md-6">
            <label class="form-label" for="childEmail">Email <span class="text-secondary small">(optional)</span></label>
            <input id="childEmail" type="email" formControlName="email" class="form-control" [class.is-invalid]="submitted && form.controls.email.errors?.['email']" />
            @if (submitted && form.controls.email.errors?.['email']) { <div class="invalid-feedback">Invalid email</div> }
          </div>
          <div class="col-12 col-md-6">
            <label class="form-label" for="childPhone">Cellphone <span class="text-secondary small">(optional)</span></label>
            <input id="childPhone" type="tel" inputmode="numeric" pattern="\\d*" formControlName="phone" class="form-control"
              autocomplete="off" autocorrect="off" autocapitalize="none" spellcheck="false"
              title="Numbers only" placeholder="Numbers only"
              (input)="onDigitsOnly('phone', $event)"
              [class.is-invalid]="submitted && form.controls.phone.errors?.['pattern']" />
            @if (submitted && form.controls.phone.errors?.['pattern']) { <div class="invalid-feedback">Numbers only</div> }
          </div>
          <div class="col-12">
            <button type="submit" class="btn btn-outline-primary">Add child</button>
          </div>
        </form>

        <hr />

  @if (state.children().length === 0) { <div class="text-secondary">No children added yet.</div> }

        @if (state.children().length > 0) {
          <ul class="list-group mb-3">
            @for (c of state.children(); track $index) {
              <li class="list-group-item d-flex justify-content-between align-items-center">
                <div>
                  <div class="fw-semibold">{{ c.firstName }} {{ c.lastName }}</div>
                  @if (c.dob) { <div class="text-secondary small">DOB: {{ c.dob }}</div> }
                </div>
                <button type="button" class="btn btn-sm btn-outline-danger" (click)="remove($index)">Remove</button>
              </li>
            }
          </ul>
        }

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
  private readonly fb = inject(FormBuilder);
  private readonly fieldData = inject(FormFieldDataService);
  submitted = false;
  constructor(public state: FamilyAccountWizardService) { }

  genderOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('genders');

  form = this.fb.group({
    firstName: ['', [Validators.required]],
    lastName: ['', [Validators.required]],
    gender: ['', [Validators.required]],
    dob: ['', [Validators.required, this.ageRangeValidator(2, 99)]],
    email: ['', [Validators.email]],
    phone: ['', [Validators.pattern(/^\d*$/)]]
  });

  // Limit selection to reasonable DOB range in the date picker
  get maxDob(): string {
    const d = new Date();
    d.setFullYear(d.getFullYear() - 2); // must be older than 1 => at least 2 years old
    return d.toISOString().slice(0, 10);
  }

  get minDob(): string {
    const d = new Date();
    d.setFullYear(d.getFullYear() - 99); // must be younger than 100 => at most 99 years old
    return d.toISOString().slice(0, 10);
  }

  // Custom validator ensuring age is within [min,max] inclusive for years
  private ageRangeValidator(minYears: number, maxYears: number): ValidatorFn {
    return (control: AbstractControl) => {
      const val: string | null = control.value;
      if (!val) return null; // required handled separately
      const dob = new Date(val);
      if (isNaN(dob.getTime())) return null; // let browser handle format issues
      const today = new Date();

      // Compute age as full years
      let age = today.getFullYear() - dob.getFullYear();
      const m = today.getMonth() - dob.getMonth();
      if (m < 0 || (m === 0 && today.getDate() < dob.getDate())) {
        age--;
      }

      if (age < minYears) return { ageTooYoung: true };
      if (age > maxYears) return { ageTooOld: true };
      return null;
    };
  }

  addChild(): void {
    this.submitted = true;
    if (this.form.invalid) return;
    const v = this.form.value;
    this.state.addChild({
      firstName: v.firstName ?? '',
      lastName: v.lastName ?? '',
      dob: v.dob ?? undefined,
      gender: v.gender ?? '',
      email: v.email ?? undefined,
      phone: v.phone ?? undefined
    });
    this.form.reset();
    this.submitted = false;
  }

  remove(index: number): void {
    this.state.removeChildAt(index);
  }

  onDigitsOnly(controlName: string, ev: Event) {
    const input = ev.target as HTMLInputElement;
    const digits = (input.value || '').replaceAll(/\D+/g, '');
    if (digits !== input.value) {
      this.form.get(controlName)?.setValue(digits, { emitEvent: false });
    }
  }
}
