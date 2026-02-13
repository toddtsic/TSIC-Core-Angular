import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, effect } from '@angular/core';

import { ReactiveFormsModule, FormBuilder, Validators, ValidationErrors, AbstractControl, FormGroup } from '@angular/forms';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { JobService } from '@infrastructure/services/job.service';

@Component({
  selector: 'app-fam-account-step-account',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Family Contacts</h5>
      </div>
      <div class="card-body">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="row g-4">
          <div class="col-12 col-xl-6">
            <h6 class="fw-semibold mb-2">{{ label1() }}'s Details (primary contact)</h6>
            <div class="row g-3">
              <div class="col-12">
                <label class="form-label" for="p1First">First name</label>
                <input id="p1First" type="text" formControlName="p1First" class="form-control" [class.is-invalid]="submitted && form.controls['p1First'].invalid" />
                @if (submitted && form.controls['p1First'].errors?.['required']) {
                  <div class="invalid-feedback">Required</div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p1Last">Last name</label>
                <input id="p1Last" type="text" formControlName="p1Last" class="form-control" [class.is-invalid]="submitted && form.controls['p1Last'].invalid" />
                @if (submitted && form.controls['p1Last'].errors?.['required']) {
                  <div class="invalid-feedback">Required</div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p1Phone">Cellphone</label>
                <input id="p1Phone" type="tel" inputmode="numeric" pattern="\\d*" formControlName="p1Phone" class="form-control"
                  autocomplete="off" autocorrect="off" autocapitalize="none" spellcheck="false"
                  title="Numbers only" placeholder="Numbers only"
                  (input)="onDigitsOnly('p1Phone', $event)"
                  [class.is-invalid]="submitted && form.controls['p1Phone'].errors?.['pattern']" />
                @if (submitted && form.controls['p1Phone'].errors?.['pattern']) {
                  <div class="invalid-feedback">Numbers only</div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p1Email">Email</label>
                <input id="p1Email" type="email" formControlName="p1Email" class="form-control" [class.is-invalid]="submitted && form.controls['p1Email'].invalid" />
                @if (submitted && form.controls['p1Email'].errors) {
                  <div class="invalid-feedback">
                    @if (form.controls['p1Email'].errors['required']) { <span>Required</span> }
                    @if (form.controls['p1Email'].errors['email']) { <span>Invalid email</span> }
                  </div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p1Email2">Confirm email</label>
                <input id="p1Email2" type="email" formControlName="p1EmailConfirm" class="form-control" [class.is-invalid]="submitted && (form.controls['p1EmailConfirm'].invalid || form.errors?.['p1EmailMismatch'])" />
                @if (submitted && (form.controls['p1EmailConfirm'].errors || form.errors?.['p1EmailMismatch'])) {
                  <div class="invalid-feedback">
                    @if (form.controls['p1EmailConfirm'].errors?.['required']) { <span>Required</span> }
                    @if (form.controls['p1EmailConfirm'].errors?.['email']) { <span>Invalid email</span> }
                    @if (form.errors?.['p1EmailMismatch']) { <span>Emails do not match</span> }
                  </div>
                }
              </div>
            </div>
          </div>

          <div class="col-12 col-xl-6">
            <h6 class="fw-semibold mb-2">{{ label2() }}'s Details (secondary contact)</h6>
            <div class="row g-3">
              <div class="col-12">
                <label class="form-label" for="p2First">First name</label>
                <input id="p2First" type="text" formControlName="p2First" class="form-control" [class.is-invalid]="submitted && form.controls['p2First'].invalid" />
                @if (submitted && form.controls['p2First'].errors?.['required']) {
                  <div class="invalid-feedback">Required</div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p2Last">Last name</label>
                <input id="p2Last" type="text" formControlName="p2Last" class="form-control" [class.is-invalid]="submitted && form.controls['p2Last'].invalid" />
                @if (submitted && form.controls['p2Last'].errors?.['required']) {
                  <div class="invalid-feedback">Required</div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p2Phone">Cellphone</label>
                <input id="p2Phone" type="tel" inputmode="numeric" pattern="\\d*" formControlName="p2Phone" class="form-control"
                  autocomplete="off" autocorrect="off" autocapitalize="none" spellcheck="false"
                  title="Numbers only" placeholder="Numbers only"
                  (input)="onDigitsOnly('p2Phone', $event)"
                  [class.is-invalid]="submitted && form.controls['p2Phone'].invalid" />
                @if (submitted && form.controls['p2Phone'].errors) {
                  <div class="invalid-feedback">
                    @if (form.controls['p2Phone'].errors['required']) { <span>Required</span> }
                    @if (form.controls['p2Phone'].errors['pattern']) { <span>Numbers only</span> }
                  </div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p2Email">Email</label>
                <input id="p2Email" type="email" formControlName="p2Email" class="form-control" [class.is-invalid]="submitted && form.controls['p2Email'].invalid" />
                @if (submitted && form.controls['p2Email'].errors) {
                  <div class="invalid-feedback">
                    @if (form.controls['p2Email'].errors['required']) { <span>Required</span> }
                    @if (form.controls['p2Email'].errors['email']) { <span>Invalid email</span> }
                  </div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="p2Email2">Confirm email</label>
                <input id="p2Email2" type="email" formControlName="p2EmailConfirm" class="form-control" [class.is-invalid]="submitted && (form.controls['p2EmailConfirm'].invalid || form.errors?.['p2EmailMismatch'])" />
                @if (submitted && (form.controls['p2EmailConfirm'].errors || form.errors?.['p2EmailMismatch'])) {
                  <div class="invalid-feedback">
                    @if (form.controls['p2EmailConfirm'].errors?.['required']) { <span>Required</span> }
                    @if (form.controls['p2EmailConfirm'].errors?.['email']) { <span>Invalid email</span> }
                    @if (form.errors?.['p2EmailMismatch']) { <span>Emails do not match</span> }
                  </div>
                }
              </div>
            </div>
          </div>

          <div class="rw-bottom-nav d-flex gap-2">
            <button type="submit" class="btn btn-primary">Continue</button>
          </div>
        </form>
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FamAccountStepAccountComponent {
  @Output() next = new EventEmitter<void>();
  private readonly fb = inject(FormBuilder);
  private readonly jobService = inject(JobService);
  public readonly state = inject(FamilyAccountWizardService);

  constructor() {
    this.form = this.fb.group({
      p1First: [this.state.parent1FirstName(), [Validators.required]],
      p1Last: [this.state.parent1LastName(), [Validators.required]],
      p1Phone: [this.state.parent1Phone(), [Validators.pattern(/^\d*$/)]],
      p1Email: [this.state.parent1Email(), [Validators.required, Validators.email]],
      p1EmailConfirm: [this.state.parent1EmailConfirm(), [Validators.required, Validators.email]],

      p2First: [this.state.parent2FirstName(), [Validators.required]],
      p2Last: [this.state.parent2LastName(), [Validators.required]],
      p2Phone: [this.state.parent2Phone(), [Validators.required, Validators.pattern(/^\d*$/)]],
      p2Email: [this.state.parent2Email(), [Validators.required, Validators.email]],
      p2EmailConfirm: [this.state.parent2EmailConfirm(), [Validators.required, Validators.email]]
    }, {
      validators: (group: AbstractControl): ValidationErrors | null => {
        const p1e = group.get('p1Email')?.value;
        const p1c = group.get('p1EmailConfirm')?.value;
        const p2e = group.get('p2Email')?.value;
        const p2c = group.get('p2EmailConfirm')?.value;
        const errors: any = {};
        if (p1e || p1c) {
          if (!p1e || !p1c || p1e !== p1c) errors.p1EmailMismatch = true;
        }
        if (p2e || p2c) {
          if (p2e && p2c && p2e !== p2c) errors.p2EmailMismatch = true;
        }
        return Object.keys(errors).length ? errors : null;
      }
    });

    // Keep form in sync if state gets populated asynchronously (e.g., fetched on parent init)
    effect(() => {
      this.form.patchValue({
        p1First: this.state.parent1FirstName(),
        p1Last: this.state.parent1LastName(),
        p1Phone: this.state.parent1Phone(),
        p1Email: this.state.parent1Email(),
        p1EmailConfirm: this.state.parent1Email(),
        p2First: this.state.parent2FirstName(),
        p2Last: this.state.parent2LastName(),
        p2Phone: this.state.parent2Phone(),
        p2Email: this.state.parent2Email(),
        p2EmailConfirm: this.state.parent2Email(),
      }, { emitEvent: false });
    });
  }

  submitted = false;

  form!: FormGroup;
  onDigitsOnly(controlName: string, ev: Event) {
    const input = ev.target as HTMLInputElement;
    const digits = (input.value || '').replaceAll(/\D+/g, '');
    if (digits !== input.value) {
      this.form.controls[controlName]?.setValue(digits, { emitEvent: false });
    }
  }

  // Dynamic labels derived from current job metadata; fall back to Parent 1/2
  label1(): string {
    const label = this.jobService.currentJob()?.momLabel?.trim();
    return label && label.length > 0 ? label : 'Parent 1';
  }
  label2(): string {
    const label = this.jobService.currentJob()?.dadLabel?.trim();
    return label && label.length > 0 ? label : 'Parent 2';
  }

  submit(): void {
    this.submitted = true;
    if (this.form.invalid) return;
    const v = this.form.value;
    this.state.parent1FirstName.set(v.p1First ?? '');
    this.state.parent1LastName.set(v.p1Last ?? '');
    this.state.parent1Phone.set(v.p1Phone ?? '');
    this.state.parent1Email.set(v.p1Email ?? '');
    this.state.parent1EmailConfirm.set(v.p1EmailConfirm ?? '');

    this.state.parent2FirstName.set(v.p2First ?? '');
    this.state.parent2LastName.set(v.p2Last ?? '');
    this.state.parent2Phone.set(v.p2Phone ?? '');
    this.state.parent2Email.set(v.p2Email ?? '');
    this.state.parent2EmailConfirm.set(v.p2EmailConfirm ?? '');
    this.next.emit();
  }
}
