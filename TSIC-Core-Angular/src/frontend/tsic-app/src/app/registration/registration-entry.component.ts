import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';
import { LoginComponent } from '../views/auth/login/login.component';

@Component({
  selector: 'app-registration-entry',
  standalone: true,
  imports: [CommonModule, WizardThemeDirective, LoginComponent],
  host: {},
  template: `
  <div class="container py-4" [wizardTheme]="'player'">
    <div class="row justify-content-center">
      <div class="col-lg-8 col-xl-7">
        <div class="card shadow border-0 card-rounded mb-4" *ngIf="isAuthenticated()">
          <div class="card-header gradient-header text-white py-4 border-0">
            <h2 class="mb-0">Registration</h2>
            <p class="mb-0 mt-1 opacity-75 small">Sign in and choose what you'd like to do.</p>
          </div>
        </div>

        <!-- Unified Login (shown when not signed in) -->
        <app-login *ngIf="!isAuthenticated()"
          [theme]="'player'"
          [headerText]="'Registration'"
          [subHeaderText]="subHeaderReg">
        </app-login>

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
  subHeaderReg = "Sign in and choose what you'd like to do.";

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

  // Login is handled by <app-login>

  goRegister(): void {
    if (!this.jobPath) return;
    this.router.navigate(['/', this.jobPath, 'register-player']);
  }

  goFamilyReview(): void {
    if (!this.jobPath) return;
    // Route to Family Account wizard with both next and a concrete returnUrl back to player wizard
    const returnUrl = `/${this.jobPath}/register-player?step=players`;
    this.router.navigate(['/tsic/family-account'], { queryParams: { next: 'register-player', returnUrl } });
  }
}
