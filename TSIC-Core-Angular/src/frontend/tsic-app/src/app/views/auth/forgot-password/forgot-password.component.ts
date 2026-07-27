import { ChangeDetectionStrategy, Component, signal, inject } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';

@Component({
  selector: 'app-forgot-password',
  templateUrl: './forgot-password.component.html',
  standalone: true,
  imports: [ReactiveFormsModule, RouterModule],
  styleUrls: ['./forgot-password.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ForgotPasswordComponent {
  private readonly auth = inject(AuthService);
  private readonly fb = inject(FormBuilder);

  // Username OR email in one field (legacy semantics) — no email validator.
  form = this.fb.group({
    usernameOrEmail: ['', [Validators.required]]
  });

  submitted = signal(false);
  isLoading = signal(false);
  sent = signal(false);
  errorMessage = signal<string | null>(null);

  onSubmit() {
    this.submitted.set(true);
    if (this.form.invalid) return;

    this.isLoading.set(true);
    this.errorMessage.set(null);

    const usernameOrEmail = this.form.get('usernameOrEmail')?.value ?? '';

    this.auth.forgotPassword(usernameOrEmail).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.sent.set(true);
      },
      error: () => {
        this.isLoading.set(false);
        // Still show success to avoid account enumeration
        this.sent.set(true);
      }
    });
  }
}
