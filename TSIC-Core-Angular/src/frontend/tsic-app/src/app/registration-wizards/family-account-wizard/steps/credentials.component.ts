import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors, FormGroup } from '@angular/forms';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { AuthService } from '@infrastructure/services/auth.service';

@Component({
    selector: 'app-fam-account-step-credentials',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Create Login</h5>
        <div class="text-secondary small">Choose a username and password for your new Family Account.</div>
      </div>
      <div class="card-body">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="row g-3">
          <div class="col-12 col-md-6">
            <label class="form-label" for="username">Username</label>
            <input id="username" type="text" formControlName="username" class="form-control" [disabled]="isAuthed" [class.is-invalid]="submitted && form.controls['username'].invalid && !isAuthed" />
            @if (submitted && form.controls['username'].errors && !isAuthed) {
              <div class="invalid-feedback">
                @if (form.controls['username'].errors['required']) { <span>Required</span> }
                @if (form.controls['username'].errors['minlength']) { <span>Min 3 characters</span> }
                @if (form.controls['username'].errors['pattern']) { <span>Letters, numbers, dot, underscore, hyphen only</span> }
              </div>
            }
            @if (isAuthed) { <div class="form-text">You are signed in; username is not required.</div> }
          </div>
          <div class="col-12 col-md-6"></div>

          <div class="col-12 col-md-6">
            <label class="form-label" for="password">Password</label>
            <input id="password" type="password" formControlName="password" class="form-control" [disabled]="isAuthed" [class.is-invalid]="submitted && form.controls['password'].invalid && !isAuthed" autocomplete="new-password" />
            @if (submitted && form.controls['password'].errors && !isAuthed) {
              <div class="invalid-feedback">
                @if (form.controls['password'].errors['required']) { <span>Required</span> }
                @if (form.controls['password'].errors['minlength']) { <span>Min 6 characters</span> }
              </div>
            }
          </div>
          <div class="col-12 col-md-6">
            <label class="form-label" for="confirm">Confirm password</label>
            <input id="confirm" type="password" formControlName="confirmPassword" class="form-control" [disabled]="isAuthed" [class.is-invalid]="submitted && (form.controls['confirmPassword'].invalid || form.errors?.['passwordMismatch']) && !isAuthed" autocomplete="new-password" />
            @if (submitted && !isAuthed && (form.controls['confirmPassword'].errors || form.errors?.['passwordMismatch'])) {
              <div class="invalid-feedback">
                @if (form.controls['confirmPassword'].errors?.['required']) { <span>Required</span> }
                @if (form.errors?.['passwordMismatch']) { <span>Passwords do not match</span> }
              </div>
            }
          </div>

          <div class="d-flex gap-2 mt-2">
            <button type="submit" class="btn btn-primary">Continue</button>
          </div>
        </form>
      </div>
    </div>
  `
})
export class FamAccountStepCredentialsComponent {
    @Output() next = new EventEmitter<void>();
    private readonly fb = inject(FormBuilder);
    private readonly auth = inject(AuthService);
    constructor(public state: FamilyAccountWizardService) {
        this.form = this.fb.group({
            username: [this.state.username(), [Validators.required, Validators.minLength(3), Validators.pattern(/^[A-Za-z0-9._-]+$/)]],
            password: [this.state.password(), [Validators.required, Validators.minLength(6)]],
            confirmPassword: ['', [Validators.required]]
        }, {
            validators: (group: AbstractControl): ValidationErrors | null => {
                const pwd = group.get('password')?.value;
                const cfm = group.get('confirmPassword')?.value;
                return pwd && cfm && pwd !== cfm ? { passwordMismatch: true } : null;
            }
        });
    }

    submitted = false;
    form!: FormGroup;
    get isAuthed(): boolean { return this.auth.isAuthenticated(); }

    submit(): void {
        this.submitted = true;
        if (this.isAuthed) {
            this.form.get('username')?.clearValidators();
            this.form.get('password')?.clearValidators();
            this.form.get('confirmPassword')?.clearValidators();
            this.form.get('username')?.updateValueAndValidity({ emitEvent: false });
            this.form.get('password')?.updateValueAndValidity({ emitEvent: false });
            this.form.get('confirmPassword')?.updateValueAndValidity({ emitEvent: false });
        }

        if (this.form.invalid) return;

        const v = this.form.value;
        if (!this.isAuthed) {
            this.state.username.set(v.username ?? '');
            this.state.password.set(v.password ?? '');
        }
        this.next.emit();
    }
}
