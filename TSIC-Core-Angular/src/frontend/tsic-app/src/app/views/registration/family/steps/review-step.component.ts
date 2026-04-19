import { ChangeDetectionStrategy, Component, OnInit, inject, output } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';
import { FamilyStateService } from '../state/family-state.service';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';

/**
 * Family wizard v2 — Review step.
 * Displays a read-only summary of all entered data.
 * Auto-saves on init (create or update), shows login panel for new accounts.
 * This is the only step that emits an output — (completed) for post-wizard navigation.
 */
@Component({
    selector: 'app-fam-review-step',
    standalone: true,
    imports: [PhonePipe],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        <h5 class="mb-1 fw-semibold d-flex align-items-center flex-wrap gap-2">
          <span>Review & Save</span>
          @if (state.username()) {
            <span class="username-chip">
              <i class="bi bi-person-badge"></i>
              <span class="username-chip-label">Family Username</span>
              <span class="username-chip-value">{{ state.username() }}</span>
            </span>
          }
          @if (!state.submitting() && state.submitSuccess()) {
            <span class="saved-chip ms-auto">
              <i class="bi bi-check-circle-fill"></i>
              <span>{{ state.mode() === 'edit' ? 'Changes saved' : 'Family account saved' }}</span>
            </span>
          }
        </h5>
        <p class="wizard-tip">Please verify your information below. Your account will be saved automatically.</p>
        <!-- Parent summary -->
        <div class="row g-3">
          <div class="col-12 col-md-6">
            <div class="border rounded p-3 h-100">
              <h6 class="fw-semibold mb-2">{{ label1() }} (primary)</h6>
              <div class="small text-muted">Name</div>
              <div>{{ state.parent1().firstName }} {{ state.parent1().lastName }}</div>
              <div class="small text-muted mt-2">Cellphone</div>
              <div>{{ state.parent1().phone | phone }}</div>
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
              <div>{{ state.parent2().phone | phone }}</div>
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
          @if (!state.submitting() && state.submitError()) {
            <span class="text-danger small">{{ state.submitError() }}</span>
            <button type="button" class="btn btn-sm btn-outline-danger" (click)="state.submit()">Retry</button>
          }
        </div>

        <!-- Players table -->
        <div class="mt-3">
          <h6 class="fw-semibold mb-2">Players</h6>
          @if (state.children().length === 0) {
            <div class="text-muted">No players added.</div>
          }
          @if (state.children().length > 0) {
            <div class="table-responsive">
              <table class="tsic-grid">
                <thead>
                  <tr>
                    <th class="tsic-grid-header">Name</th>
                    <th class="tsic-grid-header">Gender</th>
                    <th class="tsic-grid-header">DOB</th>
                    <th class="tsic-grid-header">Email</th>
                    <th class="tsic-grid-header">Cellphone</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of state.children(); track $index) {
                    <tr>
                      <td class="tsic-grid-cell fw-semibold">{{ c.firstName }} {{ c.lastName }}</td>
                      <td class="tsic-grid-cell">{{ c.gender }}</td>
                      <td class="tsic-grid-cell">{{ formatDob(c.dob) }}</td>
                      <td class="tsic-grid-cell">{{ c.email || '—' }}</td>
                      <td class="tsic-grid-cell">{{ c.phone | phone }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>

        <!-- Edit mode: return buttons -->
        @if (state.mode() === 'edit') {
          <div class="d-flex gap-2 mt-3 justify-content-end">
            <button type="button" class="btn btn-outline-secondary" (click)="completed.emit('home')">Return to Job Home</button>
            <button type="button" class="btn btn-primary" (click)="completed.emit('register')">Continue to Player Registration</button>
          </div>
        }

      </div>
    </div>
  `,
    styles: [`
      .username-chip {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-1) var(--space-3);
        border-radius: 999px;
        background: linear-gradient(135deg,
            rgba(var(--bs-primary-rgb), 0.12),
            rgba(var(--bs-primary-rgb), 0.06));
        border: 1px solid rgba(var(--bs-primary-rgb), 0.25);
        box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
        font-size: 0.875rem;
        line-height: 1.2;

        i {
          color: var(--bs-primary);
          font-size: 0.95rem;
        }
      }

      .username-chip-label {
        color: var(--neutral-600);
        font-weight: 500;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        font-size: 0.7rem;
      }

      .saved-chip {
        display: inline-flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-1) var(--space-3);
        border-radius: 999px;
        background: rgba(var(--bs-success-rgb), 0.10);
        border: 1px solid rgba(var(--bs-success-rgb), 0.30);
        color: var(--bs-success);
        font-size: 0.8rem;
        font-weight: 600;
        line-height: 1.2;

        i { font-size: 0.95rem; }
      }

      .username-chip-value {
        color: var(--bs-primary);
        font-weight: 700;
        font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewStepComponent implements OnInit {
    readonly completed = output<'home' | 'register'>();

    private readonly jobService = inject(JobService);
    readonly state = inject(FamilyStateService);

    private autoSaveAttempted = false;

    label1(): string {
        const l = this.jobService.currentJob()?.momLabel?.trim();
        return l && l.length > 0 ? l : 'Parent 1';
    }

    label2(): string {
        const l = this.jobService.currentJob()?.dadLabel?.trim();
        return l && l.length > 0 ? l : 'Parent 2';
    }

    formatDob(dob: string | null | undefined): string {
        if (!dob) return '—';
        const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(dob);
        return m ? `${m[2]}/${m[3]}/${m[1]}` : dob;
    }

    ngOnInit(): void {
        // Auto-save on entering the review step (require at least one child to avoid 400)
        if (!this.autoSaveAttempted && this.state.children().length > 0) {
            this.autoSaveAttempted = true;
            this.state.submit();
        }
    }
}
