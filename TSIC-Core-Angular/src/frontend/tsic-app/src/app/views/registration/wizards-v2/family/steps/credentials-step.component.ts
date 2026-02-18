import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { AuthService } from '@infrastructure/services/auth.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Credentials step.
 * Collects username + password for new account creation.
 * Skipped entirely in edit mode (shell handles step filtering).
 * Writes to FamilyStateService on every valid change.
 */
@Component({
    selector: 'app-fam-credentials-step',
    standalone: true,
    imports: [ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Create Login</h5>
        <div class="text-muted small">Choose a username and password for your new Family Account.</div>
      </div>
      <div class="card-body">
        <div [formGroup]="form" class="row g-3">
          <div class="col-12 col-md-6">
            <label class="form-label" for="v2-cred-username">Username</label>
            <input id="v2-cred-username" type="text" formControlName="username" class="form-control"
                   [class.is-invalid]="touched() && form.controls.username.invalid"
                   (blur)="syncToState()" />
            @if (touched() && form.controls.username.errors) {
              <div class="invalid-feedback">
                @if (form.controls.username.errors['required']) { <span>Required</span> }
                @if (form.controls.username.errors['minlength']) { <span>Min 3 characters</span> }
                @if (form.controls.username.errors['pattern']) { <span>Letters, numbers, dot, underscore, hyphen only</span> }
              </div>
            }
          </div>
          <div class="col-12 col-md-6"></div>

          <div class="col-12 col-md-6">
            <label class="form-label" for="v2-cred-password">Password</label>
            <input id="v2-cred-password" type="password" formControlName="password" class="form-control"
                   autocomplete="new-password"
                   [class.is-invalid]="touched() && form.controls.password.invalid"
                   (blur)="syncToState()" />
            @if (touched() && form.controls.password.errors) {
              <div class="invalid-feedback">
                @if (form.controls.password.errors['required']) { <span>Required</span> }
                @if (form.controls.password.errors['minlength']) { <span>Min 6 characters</span> }
              </div>
            }
          </div>
          <div class="col-12 col-md-6">
            <label class="form-label" for="v2-cred-confirm">Confirm password</label>
            <input id="v2-cred-confirm" type="password" formControlName="confirmPassword" class="form-control"
                   autocomplete="new-password"
                   [class.is-invalid]="touched() && (form.controls.confirmPassword.invalid || form.errors?.['passwordMismatch'])"
                   (blur)="syncToState()" />
            @if (touched() && (form.controls.confirmPassword.errors || form.errors?.['passwordMismatch'])) {
              <div class="invalid-feedback">
                @if (form.controls.confirmPassword.errors?.['required']) { <span>Required</span> }
                @if (form.errors?.['passwordMismatch']) { <span>Passwords do not match</span> }
              </div>
            }
          </div>
        </div>
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CredentialsStepComponent {
    private readonly fb = inject(FormBuilder);
    private readonly auth = inject(AuthService);
    private readonly state = inject(FamilyStateService);

    readonly touched = signal(false);

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

    /** Push form values into state service on blur (so canContinue reacts). */
    syncToState(): void {
        this.touched.set(true);
        const v = this.form.value;
        if (!this.auth.isAuthenticated()) {
            this.state.setCredentials(v.username ?? '', v.password ?? '');
        }
    }
}
