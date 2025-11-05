import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
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
            <input id="childDob" type="date" formControlName="dob" class="form-control" />
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
    dob: [''],
    email: ['', [Validators.email]],
    phone: ['', [Validators.pattern(/^\d*$/)]]
  });

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
