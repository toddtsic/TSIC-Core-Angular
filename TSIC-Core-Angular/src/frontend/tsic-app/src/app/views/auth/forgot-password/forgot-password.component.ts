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

  form = this.fb.group({
    email: ['', [Validators.required, Validators.email]]
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

    const email = this.form.get('email')?.value ?? '';

    this.auth.forgotPassword(email).subscribe({
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
