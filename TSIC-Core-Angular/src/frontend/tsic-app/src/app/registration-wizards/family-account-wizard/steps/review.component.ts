import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';

@Component({
    selector: 'app-fam-account-step-review',
    standalone: true,
    imports: [CommonModule, FormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Review & Save</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Confirm your account and player details. (Placeholder UI)</p>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-success" (click)="completed.emit()">Finish</button>
        </div>

        <hr class="my-4" />
        <div class="card border-0 bg-light-subtle">
          <div class="card-body">
            <h6 class="fw-semibold">Sign in now (optional)</h6>
            <p class="text-secondary small mb-3">Already created your Family Account? You can sign in here to head straight back to Player Registration.</p>
            <form class="row g-2" (ngSubmit)="login()" autocomplete="on">
              <div class="col-12 col-md-4">
                <label class="form-label" for="famLoginUsername">Username</label>
                <input id="famLoginUsername" name="username" [(ngModel)]="username" class="form-control" />
              </div>
              <div class="col-12 col-md-4">
                <label class="form-label" for="famLoginPassword">Password</label>
                <input id="famLoginPassword" type="password" name="password" [(ngModel)]="password" class="form-control" />
              </div>
              <div class="col-12 col-md-4 d-flex align-items-end gap-2">
                <button type="submit" class="btn btn-primary" [disabled]="auth.loginLoading()">Sign in and continue</button>
                <span class="text-danger small" *ngIf="auth.loginError()">{{ auth.loginError() }}</span>
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  `
})
export class FamAccountStepReviewComponent {
    @Output() completed = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();
    private readonly authSvc = inject(AuthService);
    constructor(public state: FamilyAccountWizardService) { }

    username = '';
    password = '';

    get auth() { return this.authSvc; }

    login(): void {
        if (!this.username || !this.password) return;
        this.authSvc.login({ username: this.username, password: this.password }).subscribe({
            next: () => this.completed.emit(),
            error: () => { /* error surfaced via auth.loginError signal */ }
        });
    }
}
