import { Component, EventEmitter, Output, inject, OnInit, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { Roles } from '../../../core/models/roles.constants';
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
                <div class="text-muted small">Enter your credentials below once, then choose what you want to do.</div>
              </div>
            </label>

            @if (hasAccount === 'yes') {
            <!-- Shared credentials panel (single source of truth for both actions) -->
            <div class="list-group-item border-0 pt-0 pb-3">
              <div class="rw-accent-panel-neutral">
                <div class="d-flex align-items-start gap-3">
                  <i class="bi bi-person-lock rw-accent-icon-neutral" aria-hidden="true"></i>
                  <div class="flex-grow-1">
                    <div class="row g-2 align-items-end mb-2">
                      <div class="col-12 col-md-5">
                        <label for="famUsername" class="form-label small text-muted">Family username</label>
                        <input id="famUsername" name="famUsername" class="form-control" type="text" [(ngModel)]="username" autocomplete="username" (keyup.enter)="signInThenProceed()" />
                      </div>
                      <div class="col-12 col-md-5">
                        <label for="famPassword" class="form-label small text-muted">Password</label>
                        <input #famPasswordInput id="famPassword" name="famPassword" class="form-control" type="password" [(ngModel)]="password" autocomplete="current-password" (keyup.enter)="signInThenProceed()" />
                      </div>
                    </div>
                    @if (inlineError) { <div class="alert alert-danger py-2 mb-2" role="alert">{{ inlineError }}</div> }
                    <div class="text-secondary small">Enter credentials once, then choose an action below.</div>
                  </div>
                </div>
              </div>
            </div>
            }

            <!-- Action panels using shared credentials -->
            @if (hasAccount === 'yes') {
              <div class="list-group-item border-0 pt-0 pb-3">
                <div class="rw-accent-panel">
                  <div class="d-flex align-items-start gap-3">
                    <i class="bi bi-play-fill rw-accent-icon" aria-hidden="true"></i>
                    <div class="flex-grow-1 d-flex flex-column flex-sm-row align-items-start gap-2 w-100">
                      <button type="button"
                              class="btn btn-primary"
                              [disabled]="submitting || !username || !password"
                              (click)="signInThenProceed()">
                        @if (submitting) { <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span> }
                        <span>{{ submitting ? 'Signing in…' : 'Sign in & Continue Registration' }}</span>
                      </button>
                      <span class="text-secondary small">Authenticate and jump straight to selecting players.</span>
                    </div>
                  </div>
                </div>
              </div>
              <div class="list-group-item border-0 pt-0 pb-3">
                <div class="rw-accent-panel bg-success-subtle">
                  <div class="d-flex align-items-start gap-3">
                    <i class="bi bi-people-fill rw-accent-icon text-success" aria-hidden="true"></i>
                    <div class="flex-grow-1 d-flex flex-column flex-sm-row align-items-start gap-2 w-100">
                      <button type="button"
                              class="btn btn-success"
                              [disabled]="submitting || !username || !password"
                              (click)="signInThenGoFamilyAccount()">
                        @if (submitting) { <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span> }
                        <span>{{ submitting ? 'Signing in…' : 'Sign in & Manage Family' }}</span>
                      </button>
                      <span class="text-secondary small">Authenticate then review / update your family before registering.</span>
                    </div>
                  </div>
                </div>
              </div>
            }

            <label class="list-group-item d-flex align-items-center gap-3 py-3 selectable">
              <input class="form-check-input" type="radio" name="famHasAccount" [(ngModel)]="hasAccount" [value]="'no'" />
              <div>
                <div class="fw-semibold">No — I need to create one</div>
                <div class="text-muted small">We’ll help you create a Family Account before continuing.</div>
              </div>
            </label>

            <!-- CTA appears directly under the NO option -->
            @if (hasAccount === 'no') {
            <div class="list-group-item border-0 pt-0 pb-3">
              <div class="rw-accent-panel-neutral">
                <div class="d-flex flex-column flex-md-row align-items-start gap-3">
                  <i class="bi bi-person-plus-fill rw-accent-icon-neutral" aria-hidden="true"></i>
                  <div class="flex-grow-1">
                    <div class="text-muted small mb-2">We'll guide you through a quick setup. Takes about 1–2 minutes.</div>
                    <button type="button" class="btn btn-primary pulsing-button apply-pulse" (click)="createAccount()">OK, Let's create a FAMILY ACCOUNT for you</button>
                  </div>
                </div>
              </div>
            </div>
            }
          </div>
        </fieldset>
    </div>
  </div>
  `
})
export class FamilyCheckStepComponent implements OnInit, AfterViewChecked {
  private readonly state = inject(RegistrationWizardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  @Output() next = new EventEmitter<void>();

  get hasAccount(): 'yes' | 'no' | null { return this.state.hasFamilyAccount(); }
  set hasAccount(v: 'yes' | 'no' | null) {
    const prev = this.state.hasFamilyAccount();
    this.state.hasFamilyAccount.set(v);
    if (v === 'yes' && prev !== 'yes') {
      this._pendingFocusPassword = true;
    }
  }

  username = '';
  password = '';
  loginError: string | null = null;
  submitting = false; // legacy overall submitting flag (network in-flight)
  submittingAction: 'proceed' | 'manage' | null = null; // which button initiated login
  inlineError: string | null = null;
  @ViewChild('famPasswordInput') famPasswordInput?: ElementRef<HTMLInputElement>;
  private _pendingFocusPassword = false;
  usernameTouched = false;
  passwordTouched = false;

  ngOnInit(): void { this.initialize(); }

  // Re-run focus attempt after each view check until it succeeds once (then _pendingFocusPassword resets)
  ngAfterViewChecked(): void { this.attemptFocusPassword(); }

  // Focus password field after the panel first appears (radio toggled to 'yes').
  // Using microtask ensures it runs after Angular finishes rendering the conditional block.
  private attemptFocusPassword(): void {
    if (!this._pendingFocusPassword || !this.famPasswordInput?.nativeElement) return;
    this._pendingFocusPassword = false;
    try { queueMicrotask(() => this.famPasswordInput?.nativeElement?.focus()); } catch { /* no-op */ }
  }

  private initialize(): void {
    // If user logged out but state still says 'yes', clear it.
    const currentUser = this.auth.getCurrentUser();
    if (!currentUser && this.state.hasFamilyAccount() === 'yes') {
      this.state.hasFamilyAccount.set(null);
    }

    // Pre-select "Yes" if user is already authenticated with Family role
    if (currentUser) {
      const roles = currentUser?.roles || (currentUser?.role ? [currentUser.role] : []);
      if (roles.includes(Roles.Family) && this.state.hasFamilyAccount() === null) {
        this.state.hasFamilyAccount.set('yes');
      }
      // Prefill username from JWT
      if (currentUser.username && !this.username) {
        this.username = currentUser.username;
      }
    }

    // If panel already visible (hasAccount === 'yes') attempt to focus immediately.
    this.attemptFocusPassword();
  }

  // Use centralized login screen with theming and a safe returnUrl back to this wizard
  goToLogin(): void {
    // Derive jobPath robustly: prefer wizard state, fallback to token claim, then URL first segment
    let jobPath = (this.state.jobPath() || '').trim();
    if (!jobPath) {
      jobPath = this.auth.getJobPath() || '';
    }
    if (!jobPath) {
      const url = (this.router.url || '').split('?')[0].split('#')[0];
      const segs = url.split('/').filter(s => !!s);
      if (segs.length > 0 && segs[0].toLowerCase() !== 'tsic') {
        jobPath = segs[0];
      }
    }
    // Build a normalized returnUrl (avoid double slashes)
    // We now deep-link directly to the Players step (skipping Start) after successful family login.
    const returnUrl = jobPath ? `/${jobPath}/register-player?step=players` : `/register-player?step=players`;
    // Pass intent and jobPath so login can auto-select Player role and redirect back here
    this.router.navigate(['/tsic/login'], {
      queryParams: {
        returnUrl,
        intent: 'player-register',
        jobPath,
        theme: 'family',
        header: 'Family Account Login',
        subHeader: 'Sign in to continue',
        force: 1
      }
    });
  }

  createAccount(): void {
    let jobPath = this.state.jobPath() || this.auth.getJobPath() || '';
    const returnUrl = `/${jobPath}/register-player?step=players`;
    this.router.navigate(['/tsic/family-account'], { queryParams: { returnUrl } });
  }

  goToFamilyAccount(): void {
    // Authenticate, then advance directly to the Family Account wizard.
    // After the family flow, we still want to resume the player wizard's Players step.
    let jobPath = (this.state.jobPath() || '').trim();
    if (!jobPath) {
      jobPath = this.auth.getJobPath() || '';
    }
    if (!jobPath) {
      const url = (this.router.url || '').split('?')[0].split('#')[0];
      const segs = url.split('/').filter(s => !!s);
      if (segs.length > 0 && segs[0].toLowerCase() !== 'tsic') {
        jobPath = segs[0];
      }
    }
    const playersReturn = jobPath ? `/${jobPath}/register-player?step=players` : `/register-player?step=players`;
    // Include jobPath and force edit mode so the wizard skips Credentials and loads data.
    // Provide next=register-player as a hint, though returnUrl alone is sufficient.
    const familyWizardUrl = `/tsic/family-account?mode=edit&next=register-player&jobPath=${encodeURIComponent(jobPath)}&returnUrl=${encodeURIComponent(playersReturn)}`;
    this.router.navigate(['/tsic/login'], {
      queryParams: {
        returnUrl: familyWizardUrl,
        theme: 'family',
        intent: 'family-account',
        header: 'Family Account Login',
        subHeader: 'Sign in to continue',
        force: 1
      }
    });
  }

  private doInlineLogin(): Promise<void> {
    this.inlineError = null;
    if (!this.username || !this.password || this.submitting) {
      // mark touched so validation surfaces if user attempted action
      if (!this.username) this.usernameTouched = true;
      if (!this.password) this.passwordTouched = true;
      return Promise.resolve();
    }
    this.submitting = true;
    return new Promise((resolve, reject) => {
      this.auth.login({ username: this.username.trim(), password: this.password }).subscribe({
        next: () => {
          this.submitting = false;
          resolve();
        },
        error: (err) => {
          this.submitting = false;
          this.inlineError = err?.error?.message || 'Login failed. Please check your username and password.';
          reject(err);
        }
      });
    });
  }

  async signInThenProceed(): Promise<void> {
    // Distinguish which button initiated request
    this.submittingAction = 'proceed';
    if (!this.username || !this.password) {
      // focus first invalid
      if (!this.username) {
        const el = document.getElementById('famUsername');
        try { el?.focus(); } catch { }
      } else if (!this.password) {
        const el = document.getElementById('famPassword');
        try { el?.focus(); } catch { }
      }
      this.usernameTouched = true;
      this.passwordTouched = true;
      this.submittingAction = null;
      return;
    }
    try {
      await this.doInlineLogin();
      if (!this.inlineError) {
        this.state.hasFamilyAccount.set('yes');
        this.next.emit();
      }
    } finally {
      this.submittingAction = null;
    }
  }

  async signInThenGoFamilyAccount(): Promise<void> {
    this.submittingAction = 'manage';
    if (!this.ensureCredentialsOrFocus()) { this.submittingAction = null; return; }
    try {
      await this.doInlineLogin();
      if (this.inlineError) return;
      const jobPath = this.resolveJobPath();
      const playersReturn = jobPath ? `/${jobPath}/register-player?step=players` : `/register-player?step=players`;
      const familyWizardUrl = `/tsic/family-account?mode=edit&next=register-player&jobPath=${encodeURIComponent(jobPath)}&returnUrl=${encodeURIComponent(playersReturn)}`;
      this.router.navigateByUrl(familyWizardUrl);
    } finally { this.submittingAction = null; }
  }

  private ensureCredentialsOrFocus(): boolean {
    if (this.username && this.password) return true;
    if (!this.username) { try { document.getElementById('famUsername')?.focus(); } catch { } }
    else if (!this.password) { try { document.getElementById('famPassword')?.focus(); } catch { } }
    this.usernameTouched = true; this.passwordTouched = true; return false;
  }

  private resolveJobPath(): string {
    let jobPath = (this.state.jobPath() || '').trim();
    if (!jobPath) jobPath = this.auth.getJobPath() || '';
    return jobPath;
  }
}
