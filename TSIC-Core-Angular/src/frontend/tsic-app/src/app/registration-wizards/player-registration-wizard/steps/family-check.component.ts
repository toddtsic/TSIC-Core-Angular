import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { RegistrationWizardService } from '../registration-wizard.service';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-rw-family-check',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
  <div class="card shadow border-0 card-rounded allow-overflow">
    <div class="card-header gradient-header border-0 py-4 text-center text-white">
      <h5 class="mb-1 fw-semibold">Family Account</h5>
    </div>
    <div class="card-body">
        <p class="mb-2">Do you have a current <strong>FAMILY</strong> username/password?</p>
        <p class="text-secondary small mb-3">Use the credentials for your Family Account only. Do not use a coach or director login.</p>

        <fieldset role="radiogroup" aria-labelledby="famCheckLegend">
          <legend id="famCheckLegend" class="visually-hidden">Family account availability</legend>
          <div class="list-group list-group-flush">
            <label class="list-group-item d-flex align-items-center gap-3 py-3 selectable">
              <input class="form-check-input" type="radio" name="famHasAccount" [(ngModel)]="hasAccount" [value]="'yes'" />
              <div>
                <div class="fw-semibold">Yes — I have a FAMILY login</div>
                <div class="text-muted small">Enter your credentials below to continue.</div>
              </div>
            </label>

            <!-- Inline login appears directly under the YES option -->
            <div class="list-group-item border-0 pt-0 pb-3" *ngIf="hasAccount === 'yes'">
              <div class="rw-accent-panel">
                <div class="d-flex align-items-start gap-3">
                  <i class="bi bi-shield-lock-fill rw-accent-icon" aria-hidden="true"></i>
                  <form class="row g-3 flex-grow-1" (ngSubmit)="submitLogin()" autocomplete="on" aria-label="Family account sign in">
                    <div class="col-12 col-md-6">
                      <label for="famUsername" class="form-label">Username</label>
                      <input id="famUsername" name="username" [(ngModel)]="username" class="form-control" required />
                    </div>
                    <div class="col-12 col-md-6">
                      <label for="famPassword" class="form-label">Password</label>
                      <input id="famPassword" name="password" type="password" [(ngModel)]="password" class="form-control" required />
                    </div>
                    <div class="col-12 d-flex align-items-center gap-2">
                      <button type="submit" class="btn btn-primary">Sign in</button>
                      <span class="text-danger small" *ngIf="loginError">{{ loginError }}</span>
                    </div>
                  </form>
                </div>
              </div>
            </div>

            <label class="list-group-item d-flex align-items-center gap-3 py-3 selectable">
              <input class="form-check-input" type="radio" name="famHasAccount" [(ngModel)]="hasAccount" [value]="'no'" />
              <div>
                <div class="fw-semibold">No — I need to create one</div>
                <div class="text-muted small">We’ll help you create a Family Account before continuing.</div>
              </div>
            </label>

            <!-- CTA appears directly under the NO option -->
            <div class="list-group-item border-0 pt-0 pb-3" *ngIf="hasAccount === 'no'">
              <div class="rw-accent-panel-neutral">
                <div class="d-flex align-items-center gap-3">
                  <i class="bi bi-person-plus-fill rw-accent-icon-neutral" aria-hidden="true"></i>
                  <button type="button" class="btn btn-primary pulsing-button apply-pulse" (click)="createAccount()">OK, Lets create a FAMILY ACCOUNT for you</button>
                </div>
              </div>
            </div>
          </div>
        </fieldset>
    </div>
  </div>
  `
})
export class FamilyCheckStepComponent {
  private readonly state = inject(RegistrationWizardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  @Output() next = new EventEmitter<void>();

  get hasAccount(): 'yes' | 'no' | null { return this.state.hasFamilyAccount(); }
  set hasAccount(v: 'yes' | 'no' | null) { this.state.hasFamilyAccount.set(v); }

  username = '';
  password = '';
  loginError: string | null = null;

  submitLogin(): void {
    this.loginError = null;
    if (!this.username || !this.password) {
      this.loginError = 'Please enter both username and password.';
      return;
    }
    this.auth.login({ username: this.username, password: this.password }).subscribe({
      next: () => this.next.emit(),
      error: (err) => {
        const msg = err?.error?.message || 'Login failed. Please check your credentials and try again.';
        this.loginError = msg;
      }
    });
  }

  createAccount(): void {
    const jobPath = this.state.jobPath();
    const returnUrl = `/${jobPath}/register-player?step=start`;
    this.router.navigate(['/tsic/family-account'], { queryParams: { returnUrl } });
  }
}
