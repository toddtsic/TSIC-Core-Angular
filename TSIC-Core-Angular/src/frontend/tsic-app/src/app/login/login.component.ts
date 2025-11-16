import { Component, AfterViewInit, ElementRef, ViewChild, OnDestroy, signal, effect, HostBinding, Input, OnInit } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { LoginRequest } from '../core/models/auth.models';
import { AutofillMonitor } from '@angular/cdk/text-field';
import { TextBoxModule } from '@syncfusion/ej2-angular-inputs';
import { ButtonModule } from '@syncfusion/ej2-angular-buttons';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [ReactiveFormsModule, CommonModule, TextBoxModule, ButtonModule, RouterModule],
  styleUrls: ['./login.component.scss'],
})
export class LoginComponent implements OnInit, AfterViewInit, OnDestroy {
  @ViewChild('usernameInput', { static: false }) usernameInput!: ElementRef<HTMLInputElement>;
  @ViewChild('passwordInput', { static: false }) passwordInput!: ElementRef<HTMLInputElement>;

  form!: FormGroup;
  submitted = signal(false);
  showPassword = signal(false);

  // Themed, reusable header content (overridable via inputs or query params)
  @Input() headerText = 'Welcome Back';
  @Input() subHeaderText = 'Sign in to continue';
  @Input() theme: 'login' | 'player' | 'family' | '' = '';
  // Optional client-provided return URL to prefer over query param
  @Input() returnUrl: string | null | undefined = undefined;

  // Escape route query params (switching themes while preserving context)
  public escapeQueryParams: Record<string, any> = {};
  public jobPathQuery: string | null = null;

  // Apply per-wizard theme class for gradient and primary accents
  @HostBinding('class.wizard-theme-login') get isLoginTheme() { return this.theme === 'login'; }
  @HostBinding('class.wizard-theme-player') get isPlayerTheme() { return this.theme === 'player'; }
  @HostBinding('class.wizard-theme-family') get isFamilyTheme() { return this.theme === 'family'; }


  constructor(
    private readonly authService: AuthService,
    private readonly router: Router,
    private readonly route: ActivatedRoute,
    private readonly fb: FormBuilder,
    private readonly autofill: AutofillMonitor
  ) {
    // Pre-fill username from localStorage if available
    const savedUsername = localStorage.getItem('last_username') || '';
    this.form = this.fb.group({
      username: [savedUsername, [Validators.required]],
      password: ['', [Validators.required]],
    });
  }

  // Expose auth signals to the template safely
  public get auth() { return this.authService; }

  ngOnInit() {
    // Allow theme and headers to be configured via query params when used as a reusable screen
    const qp = this.route.snapshot.queryParamMap;
    const theme = qp.get('theme');
    const header = qp.get('header');
    const sub = qp.get('subHeader');
    this.jobPathQuery = qp.get('jobPath');
    if (theme === 'login' || theme === 'player' || theme === 'family') this.theme = theme as any;
    if (header) this.headerText = header;
    if (sub) this.subHeaderText = sub;

    // If used as a routed page and no theme provided, default to 'login'
    if (!this.theme) {
      this.theme = 'login';
    }

    // Capture optional intent/jobPath for post-login auto-navigation
    this._intent = qp.get('intent');
    this._intentJobPath = qp.get('jobPath');
    this._returnUrlFromQuery = qp.get('returnUrl');

    // Build escape route query params so user can switch to generic login retaining original intent
    const effectiveReturnUrl = this.returnUrl?.trim() || this._returnUrlFromQuery || '';
    this.escapeQueryParams = {
      theme: 'login',
      ...(effectiveReturnUrl ? { returnUrl: effectiveReturnUrl } : {}),
      ...(this.jobPathQuery ? { jobPath: this.jobPathQuery } : {})
    };
  }

  ngAfterViewInit() {
    // One-time sync in case the browser autofilled without firing input events
    setTimeout(() => this.syncAutofillOnce(), 250);

    // Monitor ongoing autofill changes reliably
    if (this.usernameInput) {
      this.autofill.monitor(this.usernameInput)
        .subscribe(event => {
          if (event.isAutofilled && event.target instanceof HTMLInputElement) {
            const v = event.target.value;
            if (v && this.form.get('username')?.value !== v) {
              this.form.get('username')?.setValue(v);
            }
          }
        });
    }
    if (this.passwordInput) {
      this.autofill.monitor(this.passwordInput)
        .subscribe(event => {
          if (event.isAutofilled && event.target instanceof HTMLInputElement) {
            const v = event.target.value;
            if (v && this.form.get('password')?.value !== v) {
              this.form.get('password')?.setValue(v);
            }
          }
        });
    }
  }

  private syncAutofillOnce() {
    const u = this.usernameInput?.nativeElement.value;
    const p = this.passwordInput?.nativeElement.value;
    if (u && !this.form.get('username')?.value) {
      this.form.get('username')?.setValue(u);
    }
    if (p && !this.form.get('password')?.value) {
      this.form.get('password')?.setValue(p);
    }
  }

  onSubmit(event?: Event) {
    // Prevent default browser submission
    if (event) event.preventDefault();

    this.submitted.set(true);
    if (this.form.invalid) return;

    const credentials: LoginRequest = {
      username: this.form.get('username')?.value ?? '',
      password: this.form.get('password')?.value ?? ''
    };

    // Save username for future logins
    localStorage.setItem('last_username', String(credentials.username ?? ''));

    // Signals-driven login; navigation handled via effect below
    this.authService.loginCommand(credentials);
  }

  toggleShowPassword() {
    this.showPassword.set(!this.showPassword());
  }

  ngOnDestroy() {
    // Stop monitoring to avoid leaks
    if (this.usernameInput) this.autofill.stopMonitoring(this.usernameInput);
    if (this.passwordInput) this.autofill.stopMonitoring(this.passwordInput);
  }

  // Unified navigation effect with intent-aware sequencing.
  // For intent=player-register we delay navigating to the wizard returnUrl until after a registration
  // (regId/jobPath) has been selected, ensuring we don't arrive and then immediately lose context.
  // Note: This effect performs imperative commands (load registrations, select registration, navigate)
  // that write to signals inside AuthService. Angular requires allowSignalWrites when effects
  // trigger writes to avoid NG0600. We guard against infinite loops with early returns and a one-shot
  // navigation flag.
  private readonly _navEffect = effect(() => {
    if (this._navigated) return;
    const user = this.authService.getCurrentUser();
    if (this.authService.loginLoading() || !user) return;
    // Simplified: after login, always go to returnUrl (wizard) or role-selection fallback.
    this._navigateOnce(this._computeReturnUrl(user.jobPath));
  });

  private _computeReturnUrl(jobPathFromToken: string | undefined | null): string {
    // Prefer explicit input returnUrl
    const inputReturnUrlRaw = (this.returnUrl ?? '').trim();
    if (inputReturnUrlRaw) {
      const parsed = this._safeInternalUrl(inputReturnUrlRaw);
      if (parsed) return parsed;
    }
    // Prefer query returnUrl captured at init
    if (this._returnUrlFromQuery) {
      const parsed = this._safeInternalUrl(this._returnUrlFromQuery);
      if (parsed) return parsed;
      // Attempt secondary normalization: decode and strip leading double slash
      try {
        const raw = decodeURIComponent(this._returnUrlFromQuery);
        const normalized = raw.replace(/^\/\/+/, '/');
        const parsed2 = this._safeInternalUrl(normalized);
        if (parsed2) return parsed2;
      } catch { /* ignore */ }
    }
    // For player intent, fallback to Players step if jobPath known (Start step retired)
    if (this._intent === 'player-register' && jobPathFromToken) {
      return `/${jobPathFromToken}/register-player?step=players`;
    }
    // Default: role-selection
    return '/tsic/role-selection';
  }

  private _safeInternalUrl(candidate: string): string | null {
    try {
      const u = new URL(candidate, globalThis.location.origin);
      if (u.origin !== globalThis.location.origin) return null; // disallow external origins
      return `${u.pathname}${u.search}${u.hash}`;
    } catch {
      return null;
    }
  }

  private _navigateOnce(target: string): void {
    if (this._navigated) return;
    this._navigated = true;
    this.router.navigateByUrl(target);
  }

  // Internal intent metadata captured at init
  private _intent: string | null = null;
  private _intentJobPath: string | null = null; // retained for future use (may be removed later)
  private _returnUrlFromQuery: string | null = null;
  private _navigated = false;

}
