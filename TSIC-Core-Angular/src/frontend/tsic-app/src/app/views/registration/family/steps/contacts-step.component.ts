import { ChangeDetectionStrategy, Component, inject, signal, effect } from '@angular/core';
import { AutofocusDirective } from '@shared-ui/directives/autofocus.directive';
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
    imports: [ReactiveFormsModule, AutofocusDirective],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold">Family Contacts</h5>

        <div [formGroup]="form" class="row g-4">
          <!-- Parent 1 -->
          <div class="col-12 col-xl-6">
            <h6 class="fw-semibold mb-2">Parent/Contact 1 Details</h6>
            <div class="row g-3">
              <div class="col-12">
                <label class="field-label" for="v2-p1First">First name</label>
                <input id="v2-p1First" type="text" formControlName="p1First" class="field-input" appAutofocus
                       [class.is-required]="!form.controls.p1First.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p1First.invalid" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p1First.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p1Last">Last name</label>
                <input id="v2-p1Last" type="text" formControlName="p1Last" class="field-input"
                       [class.is-required]="!form.controls.p1Last.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p1Last.invalid" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p1Last.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p1Phone">Cellphone</label>
                <input id="v2-p1Phone" type="tel" inputmode="numeric" formControlName="p1Phone" class="field-input"
                       [class.is-required]="!form.controls.p1Phone.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p1Phone.invalid"
                       autocomplete="off" placeholder="Numbers only"
                       (input)="onDigitsOnly('p1Phone', $event); syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p1Phone.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p1Email">Email</label>
                <input id="v2-p1Email" type="email" formControlName="p1Email" class="field-input"
                       [class.is-required]="!form.controls.p1Email.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p1Email.invalid" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p1Email.errors) {
                  <div class="field-error">
                    @if (form.controls.p1Email.errors['required']) { <span>Required</span> }
                    @if (form.controls.p1Email.errors['email']) { <span>Invalid email</span> }
                  </div>
                }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p1Email2">Confirm email</label>
                <input id="v2-p1Email2" type="email" formControlName="p1EmailConfirm" class="field-input"
                       [class.is-required]="!form.controls.p1EmailConfirm.value?.trim()"
                       [class.is-invalid]="touched() && (form.controls.p1EmailConfirm.invalid || form.errors?.['p1EmailMismatch'])" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && (form.controls.p1EmailConfirm.errors || form.errors?.['p1EmailMismatch'])) {
                  <div class="field-error">
                    @if (form.controls.p1EmailConfirm.errors?.['required']) { <span>Required</span> }
                    @if (form.errors?.['p1EmailMismatch']) { <span>Emails do not match</span> }
                  </div>
                }
              </div>
            </div>
          </div>

          <!-- Parent 2 -->
          <div class="col-12 col-xl-6">
            <h6 class="fw-semibold mb-2">Parent/Contact 2 Details</h6>
            <div class="row g-3">
              <div class="col-12">
                <label class="field-label" for="v2-p2First">First name</label>
                <input id="v2-p2First" type="text" formControlName="p2First" class="field-input"
                       [class.is-required]="!form.controls.p2First.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p2First.invalid" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p2First.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p2Last">Last name</label>
                <input id="v2-p2Last" type="text" formControlName="p2Last" class="field-input"
                       [class.is-required]="!form.controls.p2Last.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p2Last.invalid" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p2Last.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p2Phone">Cellphone</label>
                <input id="v2-p2Phone" type="tel" inputmode="numeric" formControlName="p2Phone" class="field-input"
                       [class.is-required]="!form.controls.p2Phone.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p2Phone.invalid"
                       autocomplete="off" placeholder="Numbers only"
                       (input)="onDigitsOnly('p2Phone', $event); syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p2Phone.errors?.['required']) { <div class="field-error">Required</div> }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p2Email">Email</label>
                <input id="v2-p2Email" type="email" formControlName="p2Email" class="field-input"
                       [class.is-required]="!form.controls.p2Email.value?.trim()"
                       [class.is-invalid]="touched() && form.controls.p2Email.invalid" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && form.controls.p2Email.errors) {
                  <div class="field-error">
                    @if (form.controls.p2Email.errors['required']) { <span>Required</span> }
                    @if (form.controls.p2Email.errors['email']) { <span>Invalid email</span> }
                  </div>
                }
              </div>
              <div class="col-12">
                <label class="field-label" for="v2-p2Email2">Confirm email</label>
                <input id="v2-p2Email2" type="email" formControlName="p2EmailConfirm" class="field-input"
                       [class.is-required]="!form.controls.p2EmailConfirm.value?.trim()"
                       [class.is-invalid]="touched() && (form.controls.p2EmailConfirm.invalid || form.errors?.['p2EmailMismatch'])" (input)="syncToState()" (blur)="syncToState()" />
                @if (touched() && (form.controls.p2EmailConfirm.errors || form.errors?.['p2EmailMismatch'])) {
                  <div class="field-error">
                    @if (form.controls.p2EmailConfirm.errors?.['required']) { <span>Required</span> }
                    @if (form.errors?.['p2EmailMismatch']) { <span>Emails do not match</span> }
                  </div>
                }
              </div>
            </div>
          </div>
        </div>

        @if (state.mode() === 'edit') {
          <div class="d-flex align-items-center gap-2 justify-content-end mt-4">
            @if (state.profileSaved()) {
              <span class="text-success small"><i class="bi bi-check-circle-fill me-1"></i>Saved</span>
            }
            <button type="button" class="btn btn-outline-primary"
                    [disabled]="!canUpdate() || state.profileSaving()"
                    (click)="update()">
              @if (state.profileSaving()) {
                <span class="spinner-border spinner-border-sm me-1"></span>
              }
              Update Contacts
            </button>
          </div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ContactsStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly jobService = inject(JobService);
    readonly state = inject(FamilyStateService);

    readonly touched = signal(false);

    readonly form = this.fb.group({
        p1First: ['', [Validators.required]],
        p1Last: ['', [Validators.required]],
        p1Phone: ['', [Validators.required, Validators.pattern(/^\d+$/)]],
        p1Email: ['', [Validators.required, Validators.email]],
        p1EmailConfirm: ['', [Validators.required, Validators.email]],
        p2First: ['', [Validators.required]],
        p2Last: ['', [Validators.required]],
        p2Phone: ['', [Validators.required, Validators.pattern(/^\d+$/)]],
        p2Email: ['', [Validators.required, Validators.email]],
        p2EmailConfirm: ['', [Validators.required, Validators.email]],
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
        this.state.clearSavedFlag();
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

    canUpdate(): boolean {
        return this.state.hasValidParent1() && this.state.hasValidParent2();
    }

    update(): void {
        this.syncToState();
        this.state.persistProfile();
    }

    onDigitsOnly(controlName: string, ev: Event): void {
        const input = ev.target as HTMLInputElement;
        const digits = (input.value || '').replaceAll(/\D+/g, '');
        if (digits !== input.value) {
            this.form.get(controlName)?.setValue(digits, { emitEvent: false });
        }
    }
}
