import { Component, AfterViewInit, ElementRef, ViewChild, OnDestroy, signal, effect, HostBinding, Input, OnInit } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, FormGroup } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { LoginRequest } from '../core/models/auth.models';
import { AutofillMonitor } from '@angular/cdk/text-field';
import { TextBoxModule } from '@syncfusion/ej2-angular-inputs';
import { ButtonModule } from '@syncfusion/ej2-angular-buttons';

@Component({
  selector: 'app-login',
  templateUrl: './login.component.html',
  standalone: true,
  imports: [ReactiveFormsModule, CommonModule, TextBoxModule, ButtonModule],
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
    if (theme === 'login' || theme === 'player' || theme === 'family') this.theme = theme as any;
    if (header) this.headerText = header;
    if (sub) this.subHeaderText = sub;

    // If used as a routed page and no theme provided, default to 'login'
    if (!this.theme) {
      this.theme = 'login';
    }
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
    localStorage.setItem('last_username', credentials.username);

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

  // Navigate to role selection after a successful login
  // We watch for currentUser to be set and loginLoading to be false
  // to avoid navigating on initial token presence without user action
  private readonly _navEffect = effect(() => {
    const loading = this.authService.loginLoading();
    const user = this.authService.getCurrentUser();
    if (!loading && user) {
      // Prefer explicit returnUrl Input when provided (e.g., embedded usage)
      const inputReturnUrl = (this.returnUrl ?? '').trim();
      if (inputReturnUrl) {
        try {
          const u = new URL(inputReturnUrl, globalThis.location.origin);
          const internalPath = `${u.pathname}${u.search}${u.hash}`;
          this.router.navigateByUrl(internalPath);
          return;
        } catch {
          // fall through to query param or default
        }
      }

      // Next, prefer returnUrl from query string when used as a full-screen page
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      if (returnUrl) {
        try {
          const u = new URL(returnUrl, globalThis.location.origin);
          const internalPath = `${u.pathname}${u.search}${u.hash}`;
          this.router.navigateByUrl(internalPath);
          return;
        } catch {
          // Fall back to role selection if provided URL is invalid or external
        }
      }
      this.router.navigate(['/tsic/role-selection']);
    }
  });
}
