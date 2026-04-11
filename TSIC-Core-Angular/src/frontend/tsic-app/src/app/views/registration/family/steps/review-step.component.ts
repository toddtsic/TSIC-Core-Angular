import { ChangeDetectionStrategy, Component, OnInit, inject, output } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { JobService } from '@infrastructure/services/job.service';
import { FamilyStateService } from '../state/family-state.service';

/**
 * Family wizard v2 — Review step.
 * Displays a read-only summary of all entered data.
 * Auto-saves on init (create or update), shows login panel for new accounts.
 * This is the only step that emits an output — (completed) for post-wizard navigation.
 */
@Component({
    selector: 'app-fam-review-step',
    standalone: true,
    imports: [],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold">Review & Save</h5>
        <p class="wizard-tip">Please verify your information below. Your account will be saved automatically.</p>
        <!-- Parent summary -->
        <div class="row g-3">
          <div class="col-12 col-md-6">
            <div class="border rounded p-3 h-100">
              <h6 class="fw-semibold mb-2">{{ label1() }} (primary)</h6>
              <div class="small text-muted">Name</div>
              <div>{{ state.parent1().firstName }} {{ state.parent1().lastName }}</div>
              <div class="small text-muted mt-2">Cellphone</div>
              <div>{{ state.parent1().phone || '—' }}</div>
              <div class="small text-muted mt-2">Email</div>
              <div>{{ state.parent1().email }}</div>
              @if (state.mode() === 'create') {
                <div class="small text-muted mt-2">Username</div>
                <div>{{ state.username() || '—' }}</div>
              }
            </div>
          </div>
          <div class="col-12 col-md-6">
            <div class="border rounded p-3 h-100">
              <h6 class="fw-semibold mb-2">{{ label2() }} (secondary)</h6>
              <div class="small text-muted">Name</div>
              <div>{{ state.parent2().firstName }} {{ state.parent2().lastName }}</div>
              <div class="small text-muted mt-2">Cellphone</div>
              <div>{{ state.parent2().phone || '—' }}</div>
              <div class="small text-muted mt-2">Email</div>
              <div>{{ state.parent2().email || '—' }}</div>
            </div>
          </div>

          <div class="col-12 mt-3">
            <div class="border rounded p-3">
              <h6 class="fw-semibold mb-2">Address</h6>
              <div>{{ state.address().address1 }}</div>
              <div>{{ state.address().city }}, {{ state.address().state }} {{ state.address().postalCode }}</div>
            </div>
          </div>
        </div>

        <!-- Submission status -->
        <div class="mt-3 d-flex flex-wrap gap-2 align-items-center">
          @if (state.submitting()) {
            <span class="text-muted small d-inline-flex align-items-center gap-2">
              <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
              Saving your family account…
            </span>
          }
          @if (!state.submitting() && state.submitSuccess()) {
            <span class="text-success small">Family account saved.</span>
          }
          @if (!state.submitting() && state.submitError()) {
            <span class="text-danger small">{{ state.submitError() }}</span>
            <button type="button" class="btn btn-sm btn-outline-danger" (click)="state.submit()">Retry</button>
          }
        </div>

        <!-- Children table -->
        <div class="mt-3">
          <h6 class="fw-semibold mb-2">Children</h6>
          @if (state.children().length === 0) {
            <div class="text-muted">No children added.</div>
          }
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

        <!-- Edit mode: return buttons -->
        @if (state.mode() === 'edit') {
          <div class="d-flex gap-2 mt-3">
            <button type="button" class="btn btn-outline-secondary" (click)="completed.emit('home')">Return home</button>
            @if (showReturnToRegistration) {
              <button type="button" class="btn btn-primary" (click)="completed.emit('register')">Return to Player Registration</button>
            }
          </div>
        }

      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewStepComponent implements OnInit {
    readonly completed = output<'home' | 'register'>();

    private readonly jobService = inject(JobService);
    private readonly route = inject(ActivatedRoute);
    readonly state = inject(FamilyStateService);

    private autoSaveAttempted = false;

    get showReturnToRegistration(): boolean {
        return this.route.snapshot.queryParamMap.get('next') === 'register-player';
    }

    label1(): string {
        const l = this.jobService.currentJob()?.momLabel?.trim();
        return l && l.length > 0 ? l : 'Parent 1';
    }

    label2(): string {
        const l = this.jobService.currentJob()?.dadLabel?.trim();
        return l && l.length > 0 ? l : 'Parent 2';
    }

    ngOnInit(): void {
        // Auto-save on entering the review step (require at least one child to avoid 400)
        if (!this.autoSaveAttempted && this.state.children().length > 0) {
            this.autoSaveAttempted = true;
            this.state.submit();
        }
    }
}
