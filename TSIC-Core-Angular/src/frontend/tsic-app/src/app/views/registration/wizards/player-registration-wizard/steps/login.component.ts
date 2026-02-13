import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { RegistrationWizardService } from '../registration-wizard.service';
import { LoginComponent } from '../../../login/login.component';

@Component({
  selector: 'app-rw-login',
  standalone: true,
  imports: [CommonModule, FormsModule, LoginComponent],
  template: `
  <div class="card shadow border-0 card-rounded">
    <div class="card-header card-header-subtle border-0 py-3">
      <h5 class="mb-0 fw-semibold">Family Account Login</h5>
    </div>
    <div class="card-body">
      <app-login
        [theme]="'family'"
        [headerText]="'Family Account Login'"
        [subHeaderText]="'Sign in with your Family Account to continue.'"
        [returnUrl]="returnUrl()"
      />

      <div class="d-flex gap-2 mt-3">
        <button type="button" class="btn btn-outline-secondary" (click)="skip.emit()">Back</button>
      </div>

      <hr class="my-4" />
      <div>
        <p class="mb-2">Don't have a Family Account?</p>
        <button type="button" class="btn btn-link px-0" (click)="createAccount()">Create a Family Account</button>
      </div>
    </div>
  </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class LoginStepComponent {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly state = inject(RegistrationWizardService);
  @Output() next = new EventEmitter<void>();
  @Output() skip = new EventEmitter<void>();

  returnUrl(): string {
    const jobPath = this.state.jobPath();
    // After successful login, deep-link back into player wizard to the players step
    return `/${jobPath}/register-player?step=players`;
  }

  createAccount(): void {
    const jobPath = this.state.jobPath();
    const returnUrl = `/${jobPath}/register-player?mode=new&step=players`;
    this.router.navigate(['/tsic/family-account'], { queryParams: { returnUrl } });
  }
}
