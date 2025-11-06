import { Component, EventEmitter, Output, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { FamilyService, FamilyRegistrationRequest, FamilyUpdateRequest } from '../../../core/services/family.service';
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
              <div>{{ state.parent1Phone() }}</div>
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
              <div>{{ state.parent2Phone() }}</div>
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

        <div class="mt-3 d-flex flex-wrap gap-2 align-items-center">
          @if (creating) { <span class="text-secondary small d-inline-flex align-items-center gap-2"><span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Saving your family account…</span> }
          @if (!creating && createSuccess) { <span class="text-success small">Family account saved.</span> }
          @if (!creating && createError) {
            <span class="text-danger small">{{ createError }}</span>
            <button type="button" class="btn btn-sm btn-outline-danger" (click)="autoSave()">Retry</button>
          }
        </div>

        <div class="mt-3">
          <h6 class="fw-semibold mb-2">Children</h6>
          @if (state.children().length === 0) { <div class="text-secondary">No children added.</div> }
          @if (state.children().length > 0) {
            <div class="table-responsive">
              <table class="table align-middle table-sm">
                <thead class="table-light">
                  <tr>
                    <th scope="col">Name</th>
                    <th scope="col">Gender</th>
                    <th scope="col">DOB</th>
                    <th scope="col">Email</th>
                    <th scope="col">Cellphone</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of state.children(); track $index) {
                    <tr>
                      <td class="fw-semibold">{{ c.firstName }} {{ c.lastName }}</td>
                      <td>{{ c.gender }}</td>
                      <td>{{ c.dob || '—' }}</td>
                      <td>{{ c.email || '—' }}</td>
                      <td>{{ c.phone || '—' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>

        

        <hr class="my-4" />
        <div class="card border rounded bg-body-tertiary">
          <div class="card-body">
            <h6 class="fw-semibold">Sign in now (optional)</h6>
            <p class="text-secondary small mb-3">Already created your Family Account? You can sign in here to head straight back to Player Registration.</p>
            <form class="row g-2 align-items-end" (ngSubmit)="login()" autocomplete="on">
              <div class="col-12 col-md-4">
                <label class="form-label" for="famLoginUsername">Username</label>
                <input id="famLoginUsername" name="username" [(ngModel)]="username" class="form-control" />
              </div>
              <div class="col-12 col-md-4">
                <label class="form-label" for="famLoginPassword">Password</label>
                <input id="famLoginPassword" type="password" name="password" [(ngModel)]="password" class="form-control" />
              </div>
              <div class="col-12 col-md-4 d-grid d-sm-flex align-items-end gap-2">
                <button type="submit" class="btn btn-primary flex-grow-1" [disabled]="auth.loginLoading()">Sign in and continue</button>
                <button type="button" class="btn btn-outline-secondary flex-grow-1" (click)="completed.emit('home')">Return home</button>
                @if (auth.loginError()) { <span class="text-danger small ms-sm-auto mt-2 mt-sm-0">{{ auth.loginError() }}</span> }
              </div>
            </form>
          </div>
        </div>
      </div>
    </div>
  `
})
export class FamAccountStepReviewComponent implements OnInit {
  @Output() completed = new EventEmitter<'home' | 'register'>();
  @Output() back = new EventEmitter<void>();
  private readonly authSvc = inject(AuthService);
  private readonly familyService = inject(FamilyService);
  private readonly jobService = inject(JobService);
  constructor(public state: FamilyAccountWizardService) { }

  username = '';
  password = '';

  get auth() { return this.authSvc; }

  creating = false;
  createError: string | null = null;
  createSuccess = false;
  private autoSaveAttempted = false;

  // Dynamic labels (Mom/Dad etc.) or fallback
  label1(): string {
    const label = this.jobService.currentJob()?.momLabel?.trim();
    return label && label.length > 0 ? label : 'Parent 1';
  }
  label2(): string {
    const label = this.jobService.currentJob()?.dadLabel?.trim();
    return label && label.length > 0 ? label : 'Parent 2';
  }

  ngOnInit(): void {
    // Auto-create or auto-update when entering the Review step
    this.autoSave();
  }

  login(): void {
    if (!this.username || !this.password) return;
    // Drive UI signals so button state/error message work reliably
    this.auth.loginLoading.set(true);
    this.auth.loginError.set(null);
    this.authSvc.login({ username: this.username, password: this.password }).subscribe({
      next: () => {
        this.auth.loginLoading.set(false);
        this.completed.emit('register');
      },
      error: (error) => {
        this.auth.loginLoading.set(false);
        this.auth.loginError.set(error?.error?.message || 'Login failed. Please check your credentials.');
      }
    });
  }

  createFamily(): void {
    this.createError = null;
    this.createSuccess = false;
    this.creating = true;
    const req: FamilyRegistrationRequest = {
      username: this.state.username(),
      password: this.state.password(),
      primary: {
        firstName: this.state.parent1FirstName(),
        lastName: this.state.parent1LastName(),
        cellphone: this.state.parent1Phone(),
        email: this.state.parent1Email()
      },
      secondary: {
        firstName: this.state.parent2FirstName(),
        lastName: this.state.parent2LastName(),
        cellphone: this.state.parent2Phone(),
        email: this.state.parent2Email()
      },
      address: {
        streetAddress: this.state.address1(),
        city: this.state.city(),
        state: this.state.state(),
        postalCode: this.state.postalCode()
      },
      children: this.state.children().map(c => ({
        firstName: c.firstName,
        lastName: c.lastName,
        gender: c.gender,
        dob: c.dob,
        email: c.email,
        phone: c.phone
      }))
    };

    this.familyService.registerFamily(req).subscribe({
      next: (res) => {
        this.creating = false;
        if (res?.success) {
          this.createSuccess = true;
          // Pre-fill login form with chosen credentials
          this.username = req.username;
          this.password = req.password;
        } else {
          this.createError = res?.message || 'Unable to create Family Account';
        }
      },
      error: (err) => {
        this.creating = false;
        this.createError = err?.error?.message || 'Unable to create Family Account';
      }
    });
  }

  updateFamily(): void {
    this.createError = null;
    this.createSuccess = false;
    this.creating = true;
    const uname = this.state.username() || this.authSvc.getCurrentUser()?.username || '';
    const req: FamilyUpdateRequest = {
      username: uname,
      primary: {
        firstName: this.state.parent1FirstName(),
        lastName: this.state.parent1LastName(),
        cellphone: this.state.parent1Phone(),
        email: this.state.parent1Email()
      },
      secondary: {
        firstName: this.state.parent2FirstName(),
        lastName: this.state.parent2LastName(),
        cellphone: this.state.parent2Phone(),
        email: this.state.parent2Email()
      },
      address: {
        streetAddress: this.state.address1(),
        city: this.state.city(),
        state: this.state.state(),
        postalCode: this.state.postalCode()
      },
      children: this.state.children().map(c => ({
        firstName: c.firstName,
        lastName: c.lastName,
        gender: c.gender,
        dob: c.dob,
        email: c.email,
        phone: c.phone
      }))
    };

    this.familyService.updateFamily(req).subscribe({
      next: (res) => {
        this.creating = false;
        if (res?.success) {
          this.createSuccess = true;
        } else {
          this.createError = res?.message || 'Unable to update Family Account';
        }
      },
      error: (err) => {
        this.creating = false;
        this.createError = err?.error?.message || 'Unable to update Family Account';
      }
    });
  }

  autoSave(): void {
    if (this.autoSaveAttempted) return;
    // Require at least one child before auto-saving to avoid server 400
    if (this.state.children().length === 0) return;
    this.autoSaveAttempted = true;
    if (this.state.mode() === 'edit') {
      this.updateFamily();
    } else {
      this.createFamily();
    }
  }
}
