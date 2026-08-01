import { ChangeDetectionStrategy, Component, OnInit, signal, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators, AbstractControl, ValidationErrors } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';

@Component({
  selector: 'app-reset-password',
  templateUrl: './reset-password.component.html',
  standalone: true,
  imports: [ReactiveFormsModule, RouterModule],
  styleUrls: ['./reset-password.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ResetPasswordComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private token = '';
  private userId = '';

  /**
   * Where "Back to Sign In" / "Sign In Now" / the post-reset redirect send the user. Same rule as
   * forgot-password: this is a top-level route, so the job rides in on ?jobPath=. The emailed reset
   * link does not carry one today, so this falls back to /tsic/login exactly as before — wiring it
   * here means the link starts working the moment the reset URL carries the job.
   */
  loginLink = '/tsic/login';

  /**
   * AM-056 re-open: without an explicit-intent param the auth guard's last-job
   * convenience bounce (meant for bare /login hits) redirects a logged-out click
   * to the last-visited job's HOME — these links never reached a login page.
   * `force` is on the guard's whitelist and is read by nothing else.
   */
  readonly loginQueryParams = { force: 1 };

  form = this.fb.group({
    newPassword: ['', [Validators.required, Validators.minLength(6)]],
    confirmPassword: ['', [Validators.required]]
  }, { validators: [this.passwordsMatchValidator] });

  submitted = signal(false);
  isLoading = signal(false);
  success = signal(false);
  errorMessage = signal<string | null>(null);
  showPassword = signal(false);
  missingParams = signal(false);

  ngOnInit() {
    // The emailed link is keyed by userId, never email — one email can own several accounts.
    const params = this.route.snapshot.queryParamMap;
    this.token = params.get('token') ?? '';
    this.userId = params.get('userId') ?? '';
    this.loginLink = `/${params.get('jobPath') || 'tsic'}/login`;

    if (!this.token || !this.userId) {
      this.missingParams.set(true);
    }
  }

  onSubmit() {
    this.submitted.set(true);
    if (this.form.invalid) return;

    this.isLoading.set(true);
    this.errorMessage.set(null);

    const newPassword = this.form.get('newPassword')?.value ?? '';

    this.auth.resetPassword(this.userId, this.token, newPassword).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.success.set(true);
        // Auto-redirect to login after 3 seconds
        setTimeout(() => this.router.navigate([this.loginLink], { queryParams: this.loginQueryParams }), 3000);
      },
      error: (err) => {
        this.isLoading.set(false);
        const msg = err?.error?.Error || 'Something went wrong. Please try again.';
        this.errorMessage.set(msg);
      }
    });
  }

  toggleShowPassword() {
    this.showPassword.set(!this.showPassword());
  }

  private passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
    const password = control.get('newPassword');
    const confirm = control.get('confirmPassword');
    if (password && confirm && password.value !== confirm.value) {
      confirm.setErrors({ passwordMismatch: true });
      return { passwordMismatch: true };
    }
    return null;
  }
}
