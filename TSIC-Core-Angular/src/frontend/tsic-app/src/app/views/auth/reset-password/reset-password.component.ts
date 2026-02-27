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
  private email = '';

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
    const params = this.route.snapshot.queryParamMap;
    this.token = params.get('token') ?? '';
    this.email = params.get('email') ?? '';

    if (!this.token || !this.email) {
      this.missingParams.set(true);
    }
  }

  onSubmit() {
    this.submitted.set(true);
    if (this.form.invalid) return;

    this.isLoading.set(true);
    this.errorMessage.set(null);

    const newPassword = this.form.get('newPassword')?.value ?? '';

    this.auth.resetPassword(this.email, this.token, newPassword).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.success.set(true);
        // Auto-redirect to login after 3 seconds
        setTimeout(() => this.router.navigateByUrl('/tsic/login'), 3000);
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
