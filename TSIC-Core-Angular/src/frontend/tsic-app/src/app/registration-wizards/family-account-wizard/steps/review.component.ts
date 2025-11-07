import { Component, EventEmitter, Output, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FamilyAccountWizardService } from '../family-account-wizard.service';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../core/services/auth.service';
import { LoginComponent } from '../../../login/login.component';
import { FamilyService, FamilyRegistrationRequest, FamilyUpdateRequest } from '../../../core/services/family.service';
import { JobService } from '../../../core/services/job.service';

@Component({
  selector: 'app-fam-account-step-review',
  standalone: true,
  imports: [CommonModule, FormsModule, LoginComponent],
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

        <!-- Bottom navigation for edit mode: allow returning to home or back to Player Registration when applicable -->
        @if (state.mode() === 'edit') {
          <div class="rw-bottom-nav d-flex gap-2 mt-3">
            <button type="button" class="btn btn-outline-secondary" (click)="completed.emit('home')">Return home</button>
            @if (showReturnToRegistration) {
              <button type="button" class="btn btn-primary" (click)="completed.emit('register')">Return to Player Registration</button>
            }
          </div>
        }


        @if (showLoginPanel) {
          <hr class="my-4" />
          <div class="card border rounded bg-body-tertiary">
            <div class="card-body">
              <app-login
                [theme]="'family'"
                [headerText]="'Sign in now (optional)'"
                [subHeaderText]="'Already created your Family Account? Sign in to head straight back to Player Registration.'"
                [returnUrl]="loginReturnUrl()"
              />
              <div class="d-flex gap-2 mt-3">
                <button type="button" class="btn btn-outline-secondary" (click)="completed.emit('home')">Return home</button>
              </div>
            </div>
          </div>
        }
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

  // Centralized login component handles its own credentials, errors, and loading state

  creating = false;
  createError: string | null = null;
  createSuccess = false;
  private autoSaveAttempted = false;
  // Show login panel only when user is not authenticated yet (e.g., after creating account in create mode)
  get showLoginPanel(): boolean {
    // In edit mode the user is already authenticated (redirect enforced earlier), so hide.
    if (this.state.mode() === 'edit') return false;
    return !this.authSvc.isAuthenticated();
  }
  // Show 'Return to Player Registration' only when next=register-player is present
  get showReturnToRegistration(): boolean {
    try {
      const qp = (globalThis as any)?.location?.search || '';
      const params = new URLSearchParams(qp);
      return params.get('next') === 'register-player';
    } catch {
      return false;
    }
  }

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

  loginReturnUrl(): string {
    const jobPath = this.jobService.getCurrentJob()?.jobPath;
    if (jobPath) return `/${jobPath}/register-player`;
    // Fallback
    return '/tsic/role-selection';
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
