import { ChangeDetectionStrategy, Component, inject, signal, effect } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { JobService } from '@infrastructure/services/job.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Contacts step.
 * Collects parent 1 (primary) and parent 2 (secondary) contact info.
 * Syncs to state on blur so canContinue reacts.
 */
@Component({
    selector: 'app-fam-contacts-step',
    standalone: true,
    imports: [ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Family Contacts</h5>
      </div>
      <div class="card-body">
        <div [formGroup]="form" class="row g-4">
          <!-- Parent 1 -->
          <div class="col-12 col-xl-6">
            <h6 class="fw-semibold mb-2">{{ label1() }}'s Details (primary contact)</h6>
            <div class="row g-3">
              <div class="col-12">
                <label class="form-label" for="v2-p1First">First name</label>
                <input id="v2-p1First" type="text" formControlName="p1First" class="form-control"
                       [class.is-invalid]="touched() && form.controls.p1First.invalid" (blur)="syncToState()" />
                @if (touched() && form.controls.p1First.errors?.['required']) { <div class="invalid-feedback">Required</div> }
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p1Last">Last name</label>
                <input id="v2-p1Last" type="text" formControlName="p1Last" class="form-control"
                       [class.is-invalid]="touched() && form.controls.p1Last.invalid" (blur)="syncToState()" />
                @if (touched() && form.controls.p1Last.errors?.['required']) { <div class="invalid-feedback">Required</div> }
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p1Phone">Cellphone</label>
                <input id="v2-p1Phone" type="tel" inputmode="numeric" formControlName="p1Phone" class="form-control"
                       autocomplete="off" placeholder="Numbers only"
                       (input)="onDigitsOnly('p1Phone', $event)" (blur)="syncToState()" />
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p1Email">Email</label>
                <input id="v2-p1Email" type="email" formControlName="p1Email" class="form-control"
                       [class.is-invalid]="touched() && form.controls.p1Email.invalid" (blur)="syncToState()" />
                @if (touched() && form.controls.p1Email.errors) {
                  <div class="invalid-feedback">
                    @if (form.controls.p1Email.errors['required']) { <span>Required</span> }
                    @if (form.controls.p1Email.errors['email']) { <span>Invalid email</span> }
                  </div>
                }
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p1Email2">Confirm email</label>
                <input id="v2-p1Email2" type="email" formControlName="p1EmailConfirm" class="form-control"
                       [class.is-invalid]="touched() && (form.controls.p1EmailConfirm.invalid || form.errors?.['p1EmailMismatch'])" (blur)="syncToState()" />
                @if (touched() && (form.controls.p1EmailConfirm.errors || form.errors?.['p1EmailMismatch'])) {
                  <div class="invalid-feedback">
                    @if (form.controls.p1EmailConfirm.errors?.['required']) { <span>Required</span> }
                    @if (form.errors?.['p1EmailMismatch']) { <span>Emails do not match</span> }
                  </div>
                }
              </div>
            </div>
          </div>

          <!-- Parent 2 -->
          <div class="col-12 col-xl-6">
            <h6 class="fw-semibold mb-2">{{ label2() }}'s Details (secondary contact)</h6>
            <div class="row g-3">
              <div class="col-12">
                <label class="form-label" for="v2-p2First">First name</label>
                <input id="v2-p2First" type="text" formControlName="p2First" class="form-control" (blur)="syncToState()" />
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p2Last">Last name</label>
                <input id="v2-p2Last" type="text" formControlName="p2Last" class="form-control" (blur)="syncToState()" />
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p2Phone">Cellphone</label>
                <input id="v2-p2Phone" type="tel" inputmode="numeric" formControlName="p2Phone" class="form-control"
                       autocomplete="off" placeholder="Numbers only"
                       (input)="onDigitsOnly('p2Phone', $event)" (blur)="syncToState()" />
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p2Email">Email</label>
                <input id="v2-p2Email" type="email" formControlName="p2Email" class="form-control" (blur)="syncToState()" />
              </div>
              <div class="col-12">
                <label class="form-label" for="v2-p2Email2">Confirm email</label>
                <input id="v2-p2Email2" type="email" formControlName="p2EmailConfirm" class="form-control"
                       [class.is-invalid]="touched() && form.errors?.['p2EmailMismatch']" (blur)="syncToState()" />
                @if (touched() && form.errors?.['p2EmailMismatch']) {
                  <div class="invalid-feedback">Emails do not match</div>
                }
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContactsStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly jobService = inject(JobService);
    private readonly state = inject(FamilyStateService);

    readonly touched = signal(false);

    readonly form = this.fb.group({
        p1First: ['', [Validators.required]],
        p1Last: ['', [Validators.required]],
        p1Phone: ['', [Validators.pattern(/^\d*$/)]],
        p1Email: ['', [Validators.required, Validators.email]],
        p1EmailConfirm: ['', [Validators.required, Validators.email]],
        p2First: [''],
        p2Last: [''],
        p2Phone: ['', [Validators.pattern(/^\d*$/)]],
        p2Email: ['', [Validators.email]],
        p2EmailConfirm: ['', [Validators.email]],
    }, {
        validators: (group: AbstractControl): ValidationErrors | null => {
            const errors: Record<string, boolean> = {};
            const p1e = group.get('p1Email')?.value;
            const p1c = group.get('p1EmailConfirm')?.value;
            if (p1e && p1c && p1e !== p1c) errors['p1EmailMismatch'] = true;
            const p2e = group.get('p2Email')?.value;
            const p2c = group.get('p2EmailConfirm')?.value;
            if (p2e && p2c && p2e !== p2c) errors['p2EmailMismatch'] = true;
            return Object.keys(errors).length ? errors : null;
        },
    });

    constructor() {
        // Sync from state when profile loads asynchronously (edit mode)
        effect(() => {
            const p1 = this.state.parent1();
            const p2 = this.state.parent2();
            this.form.patchValue({
                p1First: p1.firstName, p1Last: p1.lastName, p1Phone: p1.phone,
                p1Email: p1.email, p1EmailConfirm: p1.emailConfirm,
                p2First: p2.firstName, p2Last: p2.lastName, p2Phone: p2.phone,
                p2Email: p2.email, p2EmailConfirm: p2.emailConfirm,
            }, { emitEvent: false });
        });
    }

    label1(): string {
        const l = this.jobService.currentJob()?.momLabel?.trim();
        return l && l.length > 0 ? l : 'Parent 1';
    }

    label2(): string {
        const l = this.jobService.currentJob()?.dadLabel?.trim();
        return l && l.length > 0 ? l : 'Parent 2';
    }

    syncToState(): void {
        this.touched.set(true);
        const v = this.form.value;
        this.state.setParent1({
            firstName: v.p1First ?? '', lastName: v.p1Last ?? '', phone: v.p1Phone ?? '',
            email: v.p1Email ?? '', emailConfirm: v.p1EmailConfirm ?? '',
        });
        this.state.setParent2({
            firstName: v.p2First ?? '', lastName: v.p2Last ?? '', phone: v.p2Phone ?? '',
            email: v.p2Email ?? '', emailConfirm: v.p2EmailConfirm ?? '',
        });
    }

    onDigitsOnly(controlName: string, ev: Event): void {
        const input = ev.target as HTMLInputElement;
        const digits = (input.value || '').replaceAll(/\D+/g, '');
        if (digits !== input.value) {
            this.form.get(controlName)?.setValue(digits, { emitEvent: false });
        }
    }
}
