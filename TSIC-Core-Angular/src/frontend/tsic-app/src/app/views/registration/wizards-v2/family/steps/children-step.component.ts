import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, ValidatorFn, AbstractControl } from '@angular/forms';
import { FormFieldDataService, type SelectOption } from '@infrastructure/services/form-field-data.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Children step.
 * Manages a dynamic list of children. Each child is added via a form,
 * then stored in the state service. Navigation handled by shell.
 */
@Component({
    selector: 'app-fam-children-step',
    standalone: true,
    imports: [ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Add Children</h5>
      </div>
      <div class="card-body">
        <!-- Children list -->
        <div class="mb-4">
          @if (state.children().length === 0) {
            <div class="alert alert-info mb-3">Add at least one child to continue.</div>
          }
          @if (state.children().length > 0) {
            <div class="border border-primary rounded p-3 bg-primary-subtle">
              <h6 class="fw-semibold mb-3">Children added</h6>
              <ul class="list-group mb-0">
                @for (c of state.children(); track $index) {
                  <li class="list-group-item d-flex justify-content-between align-items-center">
                    <div>
                      <div class="fw-semibold">{{ c.firstName }} {{ c.lastName }}</div>
                      @if (c.dob) { <div class="text-muted small">DOB: {{ c.dob }}</div> }
                      @if (c.email) { <div class="text-muted small">Email: {{ c.email }}</div> }
                      @if (c.phone) { <div class="text-muted small">Cell: {{ c.phone }}</div> }
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-danger" (click)="remove($index)">Remove</button>
                  </li>
                }
              </ul>
            </div>
          }
        </div>

        <!-- Add child form -->
        <div class="card bg-body-tertiary border-0 mb-3">
          <div class="card-body">
            <form [formGroup]="form" (ngSubmit)="addChild()" class="row g-3">
              <div class="col-12 col-md-3">
                <label class="form-label" for="v2-childFirst">First name</label>
                <input id="v2-childFirst" type="text" formControlName="firstName" class="form-control"
                       [class.is-invalid]="submitted() && form.controls.firstName.invalid" />
                @if (submitted() && form.controls.firstName.errors?.['required']) { <div class="invalid-feedback">Required</div> }
              </div>
              <div class="col-12 col-md-3">
                <label class="form-label" for="v2-childLast">Last name</label>
                <input id="v2-childLast" type="text" formControlName="lastName" class="form-control"
                       [class.is-invalid]="submitted() && form.controls.lastName.invalid" />
                @if (submitted() && form.controls.lastName.errors?.['required']) { <div class="invalid-feedback">Required</div> }
              </div>
              <div class="col-12 col-md-3">
                <label class="form-label" for="v2-childGender">Gender</label>
                <select id="v2-childGender" formControlName="gender" class="form-select"
                        [class.is-invalid]="submitted() && form.controls.gender.invalid">
                  <option value="" disabled>Select</option>
                  @for (g of genderOptions; track g.value) { <option [value]="g.value">{{ g.label }}</option> }
                </select>
                @if (submitted() && form.controls.gender.errors?.['required']) { <div class="invalid-feedback">Required</div> }
              </div>
              <div class="col-12 col-md-3">
                <label class="form-label" for="v2-childDob">Date of birth</label>
                <input id="v2-childDob" type="date" formControlName="dob" class="form-control"
                       [attr.min]="minDob" [attr.max]="maxDob"
                       [class.is-invalid]="submitted() && form.controls.dob.invalid" />
                @if (submitted() && form.controls.dob.errors?.['required']) { <div class="invalid-feedback">Required</div> }
                @if (submitted() && (form.controls.dob.errors?.['ageTooYoung'] || form.controls.dob.errors?.['ageTooOld'])) {
                  <div class="invalid-feedback">Age must be between 2 and 99 years.</div>
                }
              </div>
              <div class="col-12 col-md-6">
                <label class="form-label" for="v2-childEmail">Email <span class="text-muted small">(optional)</span></label>
                <input id="v2-childEmail" type="email" formControlName="email" class="form-control"
                       [class.is-invalid]="submitted() && form.controls.email.errors?.['email']" />
                @if (submitted() && form.controls.email.errors?.['email']) { <div class="invalid-feedback">Invalid email</div> }
              </div>
              <div class="col-12 col-md-6">
                <label class="form-label" for="v2-childPhone">Cellphone <span class="text-muted small">(optional)</span></label>
                <input id="v2-childPhone" type="tel" inputmode="numeric" formControlName="phone" class="form-control"
                       autocomplete="off" placeholder="Numbers only"
                       (input)="onDigitsOnly($event)"
                       [class.is-invalid]="submitted() && form.controls.phone.errors?.['pattern']" />
                @if (submitted() && form.controls.phone.errors?.['pattern']) { <div class="invalid-feedback">Numbers only</div> }
              </div>
              <div class="col-12">
                <button type="submit" class="btn btn-outline-primary">Add child</button>
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ChildrenStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly fieldData = inject(FormFieldDataService);
    readonly state = inject(FamilyStateService);

    readonly submitted = signal(false);
    readonly genderOptions: SelectOption[] = this.fieldData.getOptionsForDataSource('genders');

    readonly form = this.fb.group({
        firstName: ['', [Validators.required]],
        lastName: ['', [Validators.required]],
        gender: ['', [Validators.required]],
        dob: ['', [Validators.required, this.ageRangeValidator(2, 99)]],
        email: ['', [Validators.email]],
        phone: ['', [Validators.pattern(/^\d*$/)]],
    });

    get maxDob(): string {
        const d = new Date();
        d.setFullYear(d.getFullYear() - 2);
        return d.toISOString().slice(0, 10);
    }

    get minDob(): string {
        const d = new Date();
        d.setFullYear(d.getFullYear() - 99);
        return d.toISOString().slice(0, 10);
    }

    addChild(): void {
        this.submitted.set(true);
        if (this.form.invalid) return;
        const v = this.form.value;
        this.state.addChild({
            firstName: v.firstName ?? '',
            lastName: v.lastName ?? '',
            dob: v.dob || undefined,
            gender: v.gender ?? '',
            email: v.email || undefined,
            phone: v.phone || undefined,
        });
        this.form.reset();
        this.submitted.set(false);
    }

    remove(index: number): void {
        this.state.removeChildAt(index);
    }

    onDigitsOnly(ev: Event): void {
        const input = ev.target as HTMLInputElement;
        const digits = (input.value || '').replaceAll(/\D+/g, '');
        if (digits !== input.value) {
            this.form.controls.phone.setValue(digits, { emitEvent: false });
        }
    }

    private ageRangeValidator(minYears: number, maxYears: number): ValidatorFn {
        return (control: AbstractControl) => {
            const val: string | null = control.value;
            if (!val) return null;
            const dob = new Date(val);
            if (Number.isNaN(dob.getTime())) return null;
            const today = new Date();
            let age = today.getFullYear() - dob.getFullYear();
            const m = today.getMonth() - dob.getMonth();
            if (m < 0 || (m === 0 && today.getDate() < dob.getDate())) age--;
            if (age < minYears) return { ageTooYoung: true };
            if (age > maxYears) return { ageTooOld: true };
            return null;
        };
    }
}
