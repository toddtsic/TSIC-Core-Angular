import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AutofocusDirective } from '@shared-ui/directives/autofocus.directive';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Account step (unified create/edit).
 * New users: enter username + password + confirm password → create flow.
 * Returning users: enter existing username + password → edit flow (data prefilled on advance).
 */
@Component({
    selector: 'app-fam-credentials-step',
    standalone: true,
    imports: [ReactiveFormsModule, AutofocusDirective],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold">Create Family Account</h5>
        <p class="wizard-tip">Choose a username and password for your NEW account.<br>Already have an account? Select <strong>Back</strong> below to login.</p>

        @if (validationError()) {
          <div class="alert alert-danger d-flex align-items-start gap-2 mb-3" role="alert">
            <i class="bi bi-exclamation-triangle-fill mt-1"></i>
            <span>{{ validationError() }}</span>
          </div>
        }

        <div [formGroup]="form" class="row g-3">
          <div class="col-12 col-md-6">
            <label class="field-label" for="v2-cred-username">Username</label>
            <input id="v2-cred-username" type="text" formControlName="username" class="field-input" appAutofocus
                   [class.is-required]="!form.controls.username.value?.trim()"
                   [class.is-invalid]="touched() && form.controls.username.invalid"
                   (input)="syncToState()" (blur)="syncToState()" />
            @if (touched() && form.controls.username.errors) {
              <div class="field-error">
                @if (form.controls.username.errors['required']) { <span>Required</span> }
                @if (form.controls.username.errors['minlength']) { <span>Min 3 characters</span> }
                @if (form.controls.username.errors['pattern']) { <span>Letters, numbers, dot, underscore, hyphen only</span> }
              </div>
            }
          </div>
          <div class="col-12 col-md-6"></div>

          <div class="col-12 col-md-6">
            <label class="field-label" for="v2-cred-password">Password</label>
            <input id="v2-cred-password" type="password" formControlName="password" class="field-input"
                   autocomplete="new-password"
                   [class.is-required]="!form.controls.password.value"
                   [class.is-invalid]="touched() && form.controls.password.invalid"
                   (input)="syncToState()" (blur)="syncToState()" />
            @if (touched() && form.controls.password.errors) {
              <div class="field-error">
                @if (form.controls.password.errors['required']) { <span>Required</span> }
                @if (form.controls.password.errors['minlength']) { <span>Min 6 characters</span> }
              </div>
            }
          </div>

          @if (!state.accountExists()) {
          <div class="col-12 col-md-6">
            <label class="field-label" for="v2-cred-confirm">Confirm password</label>
            <input id="v2-cred-confirm" type="password" formControlName="confirmPassword" class="field-input"
                   autocomplete="new-password"
                   [class.is-required]="!form.controls.confirmPassword.value"
                   [class.is-invalid]="touched() && (form.controls.confirmPassword.invalid || form.errors?.['passwordMismatch'])"
                   (input)="syncToState()" (blur)="syncToState()" />
            @if (touched() && (form.controls.confirmPassword.errors || form.errors?.['passwordMismatch'])) {
              <div class="field-error">
                @if (form.controls.confirmPassword.errors?.['required']) { <span>Required</span> }
                @if (form.errors?.['passwordMismatch']) { <span>Passwords do not match</span> }
              </div>
            }
          </div>
          }
        </div>
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CredentialsStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly auth = inject(AuthService);
    readonly state = inject(FamilyStateService);

    readonly touched = signal(false);
    readonly validationError = signal<string | null>(null);

    readonly form = this.fb.group({
        username: [this.state.username(), [Validators.required, Validators.minLength(3), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
        password: [this.state.password(), [Validators.required, Validators.minLength(6)]],
        confirmPassword: ['', [Validators.required]],
    }, {
        validators: (group: AbstractControl): ValidationErrors | null => {
            const pwd = group.get('password')?.value;
            const cfm = group.get('confirmPassword')?.value;
            return pwd && cfm && pwd !== cfm ? { passwordMismatch: true } : null;
        },
    });

    /** Push form values into state service on every keystroke. */
    syncToState(): void {
        this.touched.set(true);
        this.validationError.set(null);
        const v = this.form.value;
        this.state.setCredentials(v.username ?? '', v.password ?? '', v.confirmPassword ?? '');
    }
}
