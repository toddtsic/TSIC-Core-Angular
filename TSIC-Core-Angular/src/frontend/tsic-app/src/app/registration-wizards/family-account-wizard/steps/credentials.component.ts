import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors, FormGroup } from '@angular/forms';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { AuthService } from '../../../core/services/auth.service';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';

@Component({
  selector: 'app-fam-account-step-credentials',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Create Login</h5>
        <div class="text-secondary small">Choose a username and password for your new Family Account.</div>
      </div>
      <div class="card-body">
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="row g-3">
          <div class="col-12 col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Username</mat-label>
              <input id="username" matInput type="text" formControlName="username" [disabled]="isAuthed" />
              @if (submitted && form.controls['username'].errors && !isAuthed) {
                <mat-error>
                  @if (form.controls['username'].errors['required']) { <span>Required</span> }
                  @if (form.controls['username'].errors['minlength']) { <span>Min 3 characters</span> }
                  @if (form.controls['username'].errors['pattern']) { <span>Letters, numbers, dot, underscore, hyphen only</span> }
                </mat-error>
              }
            </mat-form-field>
            @if (isAuthed) { <div class="form-text">You are signed in; username is not required.</div> }
          </div>
          <div class="col-12 col-md-6"></div>

          <div class="col-12 col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Password</mat-label>
              <input id="password" matInput type="password" formControlName="password" [disabled]="isAuthed" autocomplete="new-password" />
              @if (submitted && form.controls['password'].errors && !isAuthed) {
                <mat-error>
                  @if (form.controls['password'].errors['required']) { <span>Required</span> }
                  @if (form.controls['password'].errors['minlength']) { <span>Min 6 characters</span> }
                </mat-error>
              }
            </mat-form-field>
          </div>
          <div class="col-12 col-md-6">
            <mat-form-field appearance="outline" class="w-100">
              <mat-label>Confirm password</mat-label>
              <input id="confirm" matInput type="password" formControlName="confirmPassword" [disabled]="isAuthed" autocomplete="new-password" />
              @if (submitted && !isAuthed && (form.controls['confirmPassword'].errors || form.errors?.['passwordMismatch'])) {
                <mat-error>
                  @if (form.controls['confirmPassword'].errors?.['required']) { <span>Required</span> }
                  @if (form.errors?.['passwordMismatch']) { <span>Passwords do not match</span> }
                </mat-error>
              }
            </mat-form-field>
          </div>

          <div class="d-flex gap-2 mt-2">
            <button type="submit" mat-raised-button color="primary">Continue</button>
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
