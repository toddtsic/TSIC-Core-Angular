import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto, ClubTeamDto } from '@core/api';

/** Unified row combining library ClubTeams + already-registered Teams. */
interface TeamRow {
    clubTeamId: number;
    clubTeamName: string;
    gradYear: string;
    levelOfPlay: string;
    /** Null if not yet entered for this event. */
    registration: RegisteredTeamDto | null;
    /** Selected age group ID (from dropdown). */
    selectedAgeGroupId: string;
}

/**
 * Teams step — ClubTeams library-first UX.
 * Shows all club teams with checkboxes + age group dropdowns.
 * Mirrors the player wizard's "Select Players" pattern.
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [CurrencyPipe, FormsModule, TeamFormModalComponent],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-2 d-flex align-items-center">
        <h5 class="mb-0 fw-semibold" style="font-size: var(--font-size-base)">Enter Teams</h5>
      </div>
      <div class="card-body">
        @if (loading()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading teams...</span>
            </div>
          </div>
        } @else if (error()) {
          <div class="alert alert-danger">{{ error() }}</div>
        } @else {
          @if (clubName()) {
            <div class="alert alert-info border-0 mb-3">
              <div class="fw-semibold">{{ clubName() }}</div>
              <div class="small text-muted">Select teams from your library to enter in this event.</div>
            </div>
          }

          @if (teamRows().length === 0) {
            <div class="alert alert-warning mb-3">
              No teams in your club library yet.
            </div>
          } @else {
            <p class="wizard-tip">
              Check the teams you want to register. Choose the correct age group for each.
            </p>
            <div class="team-list">
              @for (row of teamRows(); track row.clubTeamId) {
                <label class="team-row"
                       [class.is-entered]="!!row.registration"
                       [class.is-paid]="isPaid(row)">

                  <!-- Checkbox -->
                  <input type="checkbox" class="team-check"
                         [checked]="!!row.registration"
                         [disabled]="isPaid(row) || actionInProgress()"
                         (change)="onToggle(row, $event)"
                         [attr.aria-label]="'Enter ' + row.clubTeamName" />

                  <!-- Team info -->
                  <div class="team-info">
                    <span class="team-name">{{ row.clubTeamName }}</span>
                    <span class="team-meta">{{ row.gradYear }}{{ row.levelOfPlay ? ' · Level ' + row.levelOfPlay : '' }}</span>
                  </div>

                  <!-- Age group dropdown -->
                  <select class="form-select form-select-sm age-select"
                          [ngModel]="row.selectedAgeGroupId"
                          (ngModelChange)="onAgeGroupChange(row, $event)"
                          [disabled]="isPaid(row)"
                          (click)="$event.stopPropagation()">
                    <option value="">Select age group</option>
                    @for (ag of ageGroups(); track ag.ageGroupId) {
                      <option [value]="ag.ageGroupId">{{ ag.ageGroupName }}</option>
                    }
                  </select>

                  <!-- Fee -->
                  <span class="team-fee">
                    @if (getFee(row); as fee) {
                      {{ fee | currency }}
                    }
                  </span>

                  <!-- Status -->
                  @if (isPaid(row)) {
                    <span class="badge bg-success">Paid</span>
                  } @else if (row.registration) {
                    <span class="badge bg-primary-subtle text-primary-emphasis">Entered</span>
                  }
                </label>
              }
            </div>
          }

          <button type="button" class="btn btn-outline-primary btn-sm mt-3" (click)="showAddModal.set(true)">
            <i class="bi bi-plus-circle me-1"></i>Add Team
          </button>
        }
      </div>
    </div>

    @if (showAddModal()) {
      <app-team-form-modal
        [ageGroups]="ageGroups()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
    }
  `,
    styles: [`
      .team-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .team-row {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-2) var(--space-3);
        border-radius: var(--radius-md);
        border: 1px solid var(--border-color);
        background: var(--brand-surface);
        cursor: pointer;
        transition: border-color 0.15s ease, background-color 0.15s ease;

        &:hover:not(.is-paid) {
          border-color: rgba(var(--bs-primary-rgb), 0.4);
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &.is-entered {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.06);
        }

        &.is-paid {
          border-color: rgba(var(--bs-success-rgb), 0.25);
          background: rgba(var(--bs-success-rgb), 0.05);
          cursor: default;
          opacity: 0.85;
        }
      }

      .team-check {
        appearance: none;
        width: 20px;
        height: 20px;
        min-width: 20px;
        border: 2px solid var(--neutral-300);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        cursor: pointer;
        position: relative;
        transition: border-color 0.15s ease, background-color 0.15s ease;

        &:checked {
          background: var(--bs-primary);
          border-color: var(--bs-primary);
          &::after {
            content: '';
            position: absolute;
            top: 2px; left: 5px;
            width: 6px; height: 10px;
            border: solid var(--neutral-0);
            border-width: 0 2px 2px 0;
            transform: rotate(45deg);
          }
        }

        &:disabled:checked {
          background: var(--bs-success);
          border-color: var(--bs-success);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .team-info {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: 1px;
      }

      .team-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .team-meta {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .age-select {
        width: auto;
        min-width: 140px;
        max-width: 200px;
        flex-shrink: 0;
      }

      .team-fee {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        white-space: nowrap;
        min-width: 70px;
        text-align: right;
      }

      @media (max-width: 575.98px) {
        .team-row {
          flex-wrap: wrap;
          gap: var(--space-2);
        }
        .age-select { min-width: 120px; }
        .team-fee { min-width: auto; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamTeamsStepComponent implements OnInit {
    readonly proceedToPayment = output<void>();

    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly destroyRef = inject(DestroyRef);

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal<string | null>(null);
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);

    // Raw data from backend
    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    // Mutable map of clubTeamId → selected ageGroupId (for unregistered teams)
    private readonly _ageGroupSelections = signal<Record<number, string>>({});

    /** Unified list: registered teams first, then available library teams. */
    readonly teamRows = computed<TeamRow[]>(() => {
        const registered = this._registeredTeams();
        const available = this._clubTeams();
        const ageGrps = this.ageGroups();
        const selections = this._ageGroupSelections();

        const rows: TeamRow[] = [];

        // Registered teams (entered for this event)
        for (const reg of registered) {
            rows.push({
                clubTeamId: reg.clubTeamId ?? 0,
                clubTeamName: reg.teamName,
                gradYear: reg.ageGroupName,
                levelOfPlay: reg.levelOfPlay ?? '',
                registration: reg,
                selectedAgeGroupId: reg.ageGroupId,
            });
        }

        // Available ClubTeams (not yet registered)
        for (const ct of available) {
            const bestMatch = selections[ct.clubTeamId] ?? this.bestMatchAgeGroup(ct.clubTeamGradYear, ageGrps);
            rows.push({
                clubTeamId: ct.clubTeamId,
                clubTeamName: ct.clubTeamName,
                gradYear: ct.clubTeamGradYear,
                levelOfPlay: ct.clubTeamLevelOfPlay ?? '',
                registration: null,
                selectedAgeGroupId: bestMatch,
            });
        }

        return rows;
    });

    ngOnInit(): void {
        this.loadTeamsMetadata();
    }

    isPaid(row: TeamRow): boolean {
        return !!row.registration && row.registration.paidTotal > 0;
    }

    getFee(row: TeamRow): number | null {
        if (row.registration) return row.registration.feeTotal;
        const ag = this.ageGroups().find(a => a.ageGroupId === row.selectedAgeGroupId);
        if (!ag) return null;
        return (ag.deposit || 0) + (ag.balanceDue || 0);
    }

    onAgeGroupChange(row: TeamRow, ageGroupId: string): void {
        row.selectedAgeGroupId = ageGroupId;
        this._ageGroupSelections.update(s => ({ ...s, [row.clubTeamId]: ageGroupId }));
    }

    onToggle(row: TeamRow, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        if (checked) {
            this.registerTeam(row);
        } else {
            this.unregisterTeam(row);
        }
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.loadTeamsMetadata();
    }

    // ── Private ─────────────────────────────────────────────────────

    private loadTeamsMetadata(): void {
        this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.loading.set(false);
                    this.clubName.set(meta.clubName || null);
                    this._registeredTeams.set(meta.registeredTeams || []);
                    this._clubTeams.set(meta.clubTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    // Propagate to payment service
                    this.state.teamPayment.setTeams(meta.registeredTeams || []);
                    this.state.teamPayment.setJobPath(this.state.jobPath());
                    this.state.teamPayment.setPaymentConfig(
                        meta.paymentMethodsAllowedCode,
                        meta.bAddProcessingFees,
                        meta.bApplyProcessingFeesToTeamDeposit,
                    );
                    this.state.setHasActiveDiscountCodes(meta.hasActiveDiscountCodes);
                },
                error: (err: unknown) => {
                    this.loading.set(false);
                    console.error('[TeamsStep] Metadata load failed', err);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }

    private registerTeam(row: TeamRow): void {
        if (!row.selectedAgeGroupId) {
            this.toast.show('Please select an age group first.', 'warning', 3000);
            return;
        }
        this.actionInProgress.set(true);
        this.teamReg.registerTeamForEvent({
            clubTeamId: row.clubTeamId,
            ageGroupId: row.selectedAgeGroupId,
            teamName: row.clubTeamName,
            clubTeamGradYear: row.gradYear,
            levelOfPlay: row.levelOfPlay || undefined,
        })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (resp) => {
                    this.actionInProgress.set(false);
                    const msg = resp.isWaitlisted
                        ? `Team placed on waitlist for ${resp.waitlistAgegroupName ?? ''}`
                        : `${row.clubTeamName} entered!`;
                    this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    console.error('[TeamsStep] Registration failed', err);
                    this.toast.show('Failed to register team.', 'danger', 4000);
                    this.loadTeamsMetadata(); // re-sync UI
                },
            });
    }

    private unregisterTeam(row: TeamRow): void {
        if (!row.registration) return;
        this.actionInProgress.set(true);
        this.teamReg.unregisterTeamFromEvent(row.registration.teamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${row.clubTeamName} removed.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    console.error('[TeamsStep] Unregister failed', err);
                    this.toast.show('Failed to remove team.', 'danger', 4000);
                    this.loadTeamsMetadata();
                },
            });
    }

    private bestMatchAgeGroup(gradYear: string, ageGroups: AgeGroupDto[]): string {
        if (!gradYear || !ageGroups.length) return '';
        // Exact match
        const exact = ageGroups.find(ag => ag.ageGroupName === gradYear);
        if (exact) return exact.ageGroupId;
        // Contains match (e.g., "2034/2035/2036" contains "2034")
        const contains = ageGroups.find(ag => ag.ageGroupName.includes(gradYear));
        if (contains) return contains.ageGroupId;
        return '';
    }
}
