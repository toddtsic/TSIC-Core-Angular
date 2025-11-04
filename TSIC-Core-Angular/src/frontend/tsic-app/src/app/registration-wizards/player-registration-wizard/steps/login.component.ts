import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
    selector: 'app-rw-login',
    standalone: true,
    imports: [CommonModule, FormsModule],
    template: `
  <div class="card shadow border-0 card-rounded">
    <div class="card-header card-header-subtle border-0 py-3">
      <h5 class="mb-0 fw-semibold">Family Account Login</h5>
    </div>
    <div class="card-body">
      <p class="text-secondary">Sign in with your Family Account to continue.</p>

      <form (ngSubmit)="submit()" class="row g-3" autocomplete="on">
        <div class="col-12 col-md-6">
          <label for="rwLoginUsername" class="form-label">Username</label>
          <input id="rwLoginUsername" name="username" [(ngModel)]="username" class="form-control" required />
        </div>
        <div class="col-12 col-md-6">
          <label for="rwLoginPassword" class="form-label">Password</label>
          <input id="rwLoginPassword" name="password" type="password" [(ngModel)]="password" class="form-control" required />
        </div>
        <div class="col-12 d-flex gap-2 align-items-center">
          <button type="submit" class="btn btn-primary" [disabled]="auth.loginLoading()">Login</button>
          <button type="button" class="btn btn-outline-secondary" (click)="skip.emit()">Back</button>
          <span class="text-danger small" *ngIf="auth.loginError()">{{ auth.loginError() }}</span>
        </div>
      </form>

      <hr class="my-4" />
      <div>
        <p class="mb-2">Don't have a Family Account?</p>
        <button type="button" class="btn btn-link px-0" (click)="createAccount()">Create a Family Account</button>
      </div>
    </div>
  </div>
  `
})
export class LoginStepComponent {
    private readonly authService = inject(AuthService);
    private readonly router = inject(Router);
    private readonly state = inject(RegistrationWizardService);
    @Output() next = new EventEmitter<void>();
    @Output() skip = new EventEmitter<void>();

    username = '';
    password = '';

    get auth() { return this.authService; }

    submit(): void {
        if (!this.username || !this.password) return;
        this.authService.login({ username: this.username, password: this.password }).subscribe({
            next: () => this.next.emit(),
            error: () => { /* error surfaced via auth.loginError signal in command style, but here we ignore */ }
        });
    }

    createAccount(): void {
        const jobPath = this.state.jobPath();
        const returnUrl = `/${jobPath}/register-player?mode=new&step=players`;
        this.router.navigate(['/tsic/family-account'], { queryParams: { returnUrl } });
    }
}
