import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { JobService } from '../core/services/job.service';

@Component({
    selector: 'app-registration-entry',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule],
    template: `
  <div class="container py-4">
    <div class="row justify-content-center">
      <div class="col-lg-8 col-xl-7">
        <div class="card shadow border-0 card-rounded mb-4">
          <div class="card-header gradient-header text-white py-4 border-0">
            <h2 class="mb-0">Registration</h2>
            <p class="mb-0 mt-1 opacity-75 small">Sign in and choose what you'd like to do.</p>
          </div>
        </div>

        <!-- Sign in form (shown when not signed in) -->
        <div class="card shadow-sm border-0 card-rounded mb-3" *ngIf="!isAuthenticated()">
          <div class="card-body">
            <h5 class="fw-semibold mb-3">I have a username/password</h5>
            <form [formGroup]="form" (ngSubmit)="signIn()" class="row g-3" autocomplete="on">
              <div class="col-12 col-md-6">
                <label class="form-label" for="username">Username</label>
                <input id="username" type="text" formControlName="username" class="form-control" autocomplete="username" [class.is-invalid]="submitted && form.controls.username.invalid">
                <div class="invalid-feedback" *ngIf="submitted && form.controls.username.errors?.['required']">Required</div>
              </div>
              <div class="col-12 col-md-6">
                <label class="form-label" for="password">Password</label>
                <input id="password" type="password" formControlName="password" class="form-control" autocomplete="current-password" [class.is-invalid]="submitted && form.controls.password.invalid">
                <div class="invalid-feedback" *ngIf="submitted && form.controls.password.errors?.['required']">Required</div>
              </div>
              <div class="col-12 d-flex align-items-center gap-2">
                <button type="submit" class="btn btn-primary" [disabled]="auth.loginLoading()">Sign in</button>
                <span class="text-danger small" *ngIf="auth.loginError()">{{ auth.loginError() }}</span>
              </div>
            </form>
          </div>
        </div>

        <!-- Choice card (shown once signed in) -->
        <div class="card shadow-sm border-0 card-rounded" *ngIf="isAuthenticated()">
          <div class="card-body">
            <h5 class="fw-semibold mb-2">What would you like to do?</h5>
            <p class="text-secondary small mb-3">You can go straight to Player Registration or review/update your Family Account first.</p>
            <div class="d-flex flex-column flex-sm-row gap-2">
              <button type="button" class="btn btn-success" (click)="goRegister()">Continue to Player Registration</button>
              <button type="button" class="btn btn-outline-primary" (click)="goFamilyReview()">Review/Update Family Account</button>
            </div>
          </div>
        </div>

      </div>
    </div>
  </div>
  `
})
export class RegistrationEntryComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);
    readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly fb = inject(FormBuilder);

    jobPath = '';
    submitted = false;

    form = this.fb.group({
        username: ['', [Validators.required]],
        password: ['', [Validators.required]]
    });

    ngOnInit(): void {
        this.jobPath = this.route.snapshot.paramMap.get('jobPath') ?? '';
        if (this.jobPath) {
            // Load job metadata so family wizard can use dynamic labels when routed next
            this.jobService.loadJobMetadata(this.jobPath);
        }
    }

    isAuthenticated(): boolean {
        return this.auth.isAuthenticated();
    }

    signIn(): void {
        this.submitted = true;
        if (this.form.invalid) return;
        const creds = { username: this.form.value.username ?? '', password: this.form.value.password ?? '' };
        this.auth.login(creds).subscribe({
            next: () => {
                // no-op: the template will reveal the choice card when authenticated
            },
            error: (err) => {
                // Error signal already set by interceptor/pipe in service in other flows; set a local message for safety
                this.auth.loginError.set(err?.error?.message || 'Login failed. Please check your credentials.');
            }
        });
    }

    goRegister(): void {
        if (!this.jobPath) return;
        this.router.navigate(['/', this.jobPath, 'register-player']);
    }

    goFamilyReview(): void {
        if (!this.jobPath) return;
        // Route to central Family Account wizard with next=register-player so it will send user to player reg afterward
        this.router.navigate(['/tsic/family-account'], { queryParams: { next: 'register-player' } });
    }
}
