import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, OnInit, signal, ViewChild, ElementRef, AfterViewChecked } from '@angular/core';
import { Roles } from '@infrastructure/constants/roles.constants';
import { firstValueFrom } from 'rxjs';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { RegistrationWizardService } from '../registration-wizard.service';
import { AuthService } from '@infrastructure/services/auth.service';

@Component({
  selector: 'app-rw-family-check',
  standalone: true,
  imports: [FormsModule],
  template: `
  <div class="card shadow-lg border-0 card-rounded">
    <div class="card-body bg-surface px-4 pb-4 pt-3">
        <div class="alert alert-info mb-4 border-info border-2 shadow-sm" role="note">
          <div class="d-flex align-items-start gap-2">
            <i class="bi bi-shield-check fs-4 shrink-0"></i>
            <div>
              <strong class="d-block mb-1">Player Account Security:</strong>
              This Family/Player account is for viewing YOUR CHILD'S TEAM ONLY and can be safely shared with your child.
              If you plan to coach or volunteer, you'll need a separate Coach account to protect other families' privacy.
            </div>
          </div>
        </div>
        <h5 class="mb-2 fw-semibold pt-3">Do you have a current <strong class="text-primary">FAMILY</strong> username/password?</h5>
        <p class="text-body-secondary small">Use the credentials for your Family Account only. Do not use a coach or director login.</p>

        <fieldset role="radiogroup" aria-labelledby="famCheckLegend" style="margin-top: var(--space-8);">
          <legend id="famCheckLegend" class="visually-hidden">Family account availability</legend>
          <div class="list-group list-group-flush">
            <label class="list-group-item d-flex align-items-center gap-3 py-3 selectable border-2 rounded mb-2"
                   [class.border-info]="hasAccount === 'yes'"
                   [class.bg-info]="hasAccount === 'yes'"
                   [class.bg-opacity-10]="hasAccount === 'yes'">
              <input class="form-check-input mt-0" type="radio" name="famHasAccount" [(ngModel)]="hasAccount" [value]="'yes'" />
              <div class="grow">
                <div class="fw-semibold">Yes — I have a FAMILY login</div>
                <div class="text-muted small">Enter your credentials below once, then choose what you want to do.</div>
              </div>
            </label>

            @if (hasAccount === 'yes') {
            <!-- Shared credentials panel -->
            <div class="pt-2 pb-3 ps-5" style="animation: slideIn 0.3s ease-out;">
              <div class="border-2 rounded p-3 shadow-sm">
                <div class="row g-2 mb-2">
                  <div class="col-12 col-sm-6 col-md-4">
                    <label for="famUsername" class="form-label small mb-1">Username</label>
                    <input id="famUsername" name="famUsername" class="form-control form-control-sm" type="text" [(ngModel)]="username" autocomplete="username" (keyup.enter)="signInThenProceed()" />
                  </div>
                  <div class="col-12 col-sm-6 col-md-4">
                    <label for="famPassword" class="form-label small mb-1">Password</label>
                    <input #famPasswordInput id="famPassword" name="famPassword" class="form-control form-control-sm" type="password" [(ngModel)]="password" autocomplete="current-password" (keyup.enter)="signInThenProceed()" />
                  </div>
                </div>
                @if (inlineError()) { <div class="alert alert-danger py-2 mb-2" role="alert">{{ inlineError() }}</div> }
                <div class="text-body-secondary small mb-3">Enter credentials once, then choose an action below.</div>

                <!-- Action buttons -->
                <div class="d-flex flex-column gap-2">
                  <div>
                    <button type="button"
                            class="btn btn-primary me-2"
                            [disabled]="submitting() || !username || !password"
                            (click)="signInThenProceed()">
                      @if (submitting() && submittingAction() === 'proceed') { <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span> }
                      <span>{{ submitting() && submittingAction() === 'proceed' ? 'Signing in…' : 'Sign in & Continue Registration' }}</span>
                    </button>
                    <span class="text-body-secondary small">Authenticate and jump straight to selecting players.</span>
                  </div>
                  <div>
                    <button type="button"
                            class="btn btn-success me-2"
                            [disabled]="submitting() || !username || !password"
                            (click)="signInThenGoFamilyAccount()">
                      @if (submitting() && submittingAction() === 'manage') { <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span> }
                      <span>{{ submitting() && submittingAction() === 'manage' ? 'Signing in…' : 'Sign in & Manage Family' }}</span>
                    </button>
                    <span class="text-body-secondary small">Authenticate then review / update your family before registering.</span>
                  </div>
                </div>
              </div>
            </div>
            }

            <label class="list-group-item d-flex align-items-center gap-3 py-3 selectable border-2 rounded mb-2"
                   [class.border-info]="hasAccount === 'no'"
                   [class.bg-info]="hasAccount === 'no'"
                   [class.bg-opacity-10]="hasAccount === 'no'">
              <input class="form-check-input mt-0" type="radio" name="famHasAccount" [(ngModel)]="hasAccount" [value]="'no'" />
              <div class="grow">
                <div class="fw-semibold">No — I need to create one</div>
                <div class="text-muted small">We'll help you create a Family Account before continuing.</div>
              </div>
            </label>

            <!-- CTA appears directly under the NO option -->
            @if (hasAccount === 'no') {
            <div class="pt-2 pb-3 ps-5" style="animation: slideIn 0.3s ease-out;">
              <div class="border-2 rounded p-3 shadow-sm">
                <div class="text-muted small mb-2">We'll guide you through a quick setup. Takes about 1–2 minutes.</div>
                <button type="button" class="btn btn-primary" (click)="createAccount()">
                  <i class="bi bi-person-plus-fill me-2"></i>Create a FAMILY ACCOUNT
                </button>
              </div>
            </div>
            }
          </div>
        </fieldset>
    </div>
  </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FamilyCheckStepComponent implements OnInit, AfterViewChecked {
  private readonly state = inject(RegistrationWizardService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  @Output() next = new EventEmitter<void>();

  get hasAccount(): 'yes' | 'no' | null { return this.state.hasFamilyAccount(); }
  set hasAccount(v: 'yes' | 'no' | null) {
    const prev = this.state.hasFamilyAccount();
    this.state.setHasFamilyAccount(v);
    if (v === 'yes' && prev !== 'yes') {
      this._pendingFocusPassword = true;
    }
  }

  username = '';
  password = '';
  readonly submitting = signal(false);
  readonly submittingAction = signal<'proceed' | 'manage' | null>(null);
  readonly inlineError = signal<string | null>(null);
  @ViewChild('famPasswordInput') famPasswordInput?: ElementRef<HTMLInputElement>;
  private _pendingFocusPassword = false;

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
      this.state.setHasFamilyAccount(null);
    }

    // Pre-select "Yes" if user is already authenticated with Family role
    if (currentUser) {
      const roles = currentUser?.roles || (currentUser?.role ? [currentUser.role] : []);
      if (roles.includes(Roles.Family) && this.state.hasFamilyAccount() === null) {
        this.state.setHasFamilyAccount('yes');
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

  private async doInlineLogin(): Promise<'ok' | 'tos'> {
    this.inlineError.set(null);
    if (!this.username || !this.password || this.submitting()) {
      return 'ok';
    }
    this.submitting.set(true);
    try {
      const response = await firstValueFrom(
        this.auth.login({ username: this.username.trim(), password: this.password })
      );
      this.submitting.set(false);
      // Check TOS requirement before proceeding
      if (this.auth.checkAndNavigateToTosIfRequired(response, this.router, this.router.url)) {
        return 'tos';
      }
      return 'ok';
    } catch (err: unknown) {
      this.submitting.set(false);
      const httpErr = err as { error?: { message?: string } };
      this.inlineError.set(httpErr?.error?.message || 'Login failed. Please check your username and password.');
      throw err;
    }
  }

  async signInThenProceed(): Promise<void> {
    // Distinguish which button initiated request
    this.submittingAction.set('proceed');
    if (!this.username || !this.password) {
      // focus first invalid
      if (!this.username) {
        try { document.getElementById('famUsername')?.focus(); } catch { }
      } else if (!this.password) {
        try { document.getElementById('famPassword')?.focus(); } catch { }
      }
      this.submittingAction.set(null);
      return;
    }
    try {
      const result = await this.doInlineLogin();
      if (result !== 'ok') {
        return;
      }
      if (!this.inlineError()) {
        // Upgrade Phase 1 token to job-scoped token (adds jobPath claim)
        const jobPath = this.state.jobPath() || this.resolveJobPath();
        await firstValueFrom(this.state.setWizardContext(jobPath));

        this.state.resetForFamilySwitch();
        this.state.setHasFamilyAccount('yes');
        this.next.emit();
      }
    } catch (err: unknown) {
      // Error is already displayed via this.inlineError set in doInlineLogin
      console.warn('Login failed:', err);
    } finally {
      this.submittingAction.set(null);
    }
  }

  async signInThenGoFamilyAccount(): Promise<void> {
    this.submittingAction.set('manage');
    if (!this.ensureCredentialsOrFocus()) { this.submittingAction.set(null); return; }
    try {
      const result = await this.doInlineLogin();
      if (result !== 'ok') {
        return;
      }
      if (this.inlineError()) return;
      const jobPath = this.resolveJobPath();
      const playersReturn = jobPath ? `/${jobPath}/register-player?step=players` : `/register-player?step=players`;
      const familyWizardUrl = `/tsic/family-account?mode=edit&next=register-player&jobPath=${encodeURIComponent(jobPath)}&returnUrl=${encodeURIComponent(playersReturn)}`;
      this.router.navigateByUrl(familyWizardUrl);
    } catch (err: unknown) {
      // Error is already displayed via this.inlineError set in doInlineLogin
      console.warn('Login failed:', err);
    } finally { this.submittingAction.set(null); }
  }

  private ensureCredentialsOrFocus(): boolean {
    if (this.username && this.password) return true;
    if (!this.username) { try { document.getElementById('famUsername')?.focus(); } catch { } }
    else if (!this.password) { try { document.getElementById('famPassword')?.focus(); } catch { } }
    return false;
  }

  private resolveJobPath(): string {
    let jobPath = (this.state.jobPath() || '').trim();
    if (!jobPath) jobPath = this.auth.getJobPath() || '';
    return jobPath;
  }
}
