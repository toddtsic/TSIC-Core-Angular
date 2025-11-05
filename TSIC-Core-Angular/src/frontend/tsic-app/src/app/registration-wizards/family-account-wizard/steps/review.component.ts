import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { JobService } from '../../../core/services/job.service';

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
        <div class="row g-3">
          <div class="col-12 col-md-6">
            <div class="border rounded p-3 h-100">
              <h6 class="fw-semibold mb-2">{{ label1() }} (primary)</h6>
              <div class="small text-secondary">Name</div>
              <div>{{ state.parent1FirstName() }} {{ state.parent1LastName() }}</div>
              <div class="small text-secondary mt-2">Cellphone</div>
              <div>{{ state.parent1Phone() }} @if (state.parent1Carrier()) { <span class="text-secondary">— {{ state.parent1Carrier() }}</span> }</div>
              <div class="small text-secondary mt-2">Email</div>
              <div>{{ state.parent1Email() }}</div>
              <div class="small text-secondary mt-2">Username</div>
              <div>{{ state.username() || '—' }}</div>
            </div>
          </div>
          <div class="col-12 col-md-6">
            <div class="border rounded p-3 h-100">
              <h6 class="fw-semibold mb-2">{{ label2() }} (secondary)</h6>
              <div class="small text-secondary">Name</div>
              <div>{{ state.parent2FirstName() }} {{ state.parent2LastName() }}</div>
              <div class="small text-secondary mt-2">Cellphone</div>
              <div>{{ state.parent2Phone() }} @if (state.parent2Carrier()) { <span class="text-secondary">— {{ state.parent2Carrier() }}</span> }</div>
              <div class="small text-secondary mt-2">Email</div>
              <div>{{ state.parent2Email() }}</div>
            </div>
          </div>

          <div class="col-12 mt-3">
            <div class="border rounded p-3">
              <h6 class="fw-semibold mb-2">Address</h6>
              <div>{{ state.address1() }}</div>
              @if (state.address2()) { <div>{{ state.address2() }}</div> }
              <div>{{ state.city() }}, {{ state.state() }} {{ state.postalCode() }}</div>
            </div>
          </div>
        </div>

        <div class="mt-3">
          <h6 class="fw-semibold mb-2">Children</h6>
          @if (state.children().length === 0) { <div class="text-secondary">No children added.</div> }
          @if (state.children().length > 0) {
            <ul class="list-group">
              @for (c of state.children(); track $index) {
                <li class="list-group-item">
                  <div class="fw-semibold">{{ c.firstName }} {{ c.lastName }}</div>
                  <div class="small text-secondary mt-1">Gender</div>
                  <div>{{ c.gender }}</div>
                  <div class="small text-secondary mt-1">DOB</div>
                  <div>{{ c.dob || '—' }}</div>
                  @if (c.email) {
                    <div class="small text-secondary mt-1">Email</div>
                    <div>{{ c.email }}</div>
                  }
                  @if (c.phone) {
                    <div class="small text-secondary mt-1">Cellphone</div>
                    <div>{{ c.phone }}</div>
                  }
                </li>
              }
            </ul>
          }
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
              <div class="col-12 col-md-4 d-flex align-items-end gap-2 flex-wrap">
                <button type="submit" class="btn btn-primary" [disabled]="auth.loginLoading()">Sign in and continue</button>
                <button type="button" class="btn btn-outline-secondary" (click)="completed.emit()">Return home</button>
                @if (auth.loginError()) { <span class="text-danger small ms-auto">{{ auth.loginError() }}</span> }
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
  private readonly jobService = inject(JobService);
  constructor(public state: FamilyAccountWizardService) { }

  username = '';
  password = '';

  get auth() { return this.authSvc; }

  // Dynamic labels (Mom/Dad etc.) or fallback
  label1(): string {
    const label = this.jobService.currentJob()?.momLabel?.trim();
    return label && label.length > 0 ? label : 'Parent 1';
  }
  label2(): string {
    const label = this.jobService.currentJob()?.dadLabel?.trim();
    return label && label.length > 0 ? label : 'Parent 2';
  }

  login(): void {
    if (!this.username || !this.password) return;
    this.authSvc.login({ username: this.username, password: this.password }).subscribe({
      next: () => this.completed.emit(),
      error: () => { /* error surfaced via auth.loginError signal */ }
    });
  }
}
