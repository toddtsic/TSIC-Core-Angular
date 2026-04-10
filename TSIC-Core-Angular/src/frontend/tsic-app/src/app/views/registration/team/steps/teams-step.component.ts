import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { RegisteredTeamsGridComponent } from '../components/registered-teams-grid.component';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import { AgeGroupPickerModalComponent, type AgeGroupSelection } from './age-group-picker-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto, ClubTeamDto } from '@core/api';

interface AgePickerTeam {
    clubTeamId: number;
    clubTeamName: string;
    gradYear: string;
    levelOfPlay: string;
    currentAgeGroupId?: string;
}

/**
 * Teams step — single screen combining library management + event registration.
 * Assigning an age group IS the registration act. No separate review step needed.
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [CurrencyPipe, RegisteredTeamsGridComponent, TeamFormModalComponent, AgeGroupPickerModalComponent, ConfirmDialogComponent],
    template: `
    @if (loading()) {
      <div class="text-center py-4">
        <div class="spinner-border text-primary" role="status">
          <span class="visually-hidden">Loading teams...</span>
        </div>
      </div>
    } @else if (error()) {
      <div class="alert alert-danger">{{ error() }}</div>
    } @else {

      <!-- Welcome hero -->
      <div class="welcome-hero">
        <h4 class="welcome-title"><i class="bi bi-trophy-fill welcome-icon"></i> {{ jobName() }}</h4>
        <p class="hero-action">Register <span class="club-accent">{{ clubName() }}</span>'s teams for this event.</p>
      </div>

      <!-- Coach card — conditional based on library state -->
      @if (allLibraryTeams().length === 0) {
        <div class="coach-card coach-primary">
          <p class="coach-intro">Welcome! Your club's team library carries across all TeamSportsInfo events — enter your teams once, then register from the list at every future tournament.</p>
          <ul class="coach-list">
            <li>
              <i class="bi bi-plus-circle text-primary"></i>
              <span>Click <strong>Add Team</strong> to get started</span>
            </li>
          </ul>
        </div>
      } @else {
        <div class="coach-card coach-primary">
          <p class="coach-intro">Your library carries across all TeamSportsInfo events. Tap <strong>Register</strong> to enter a team, or <button type="button" class="wizard-callout-link" (click)="showAddModal.set(true)">Add Team</button> if one's missing.</p>
        </div>
      }

      <!-- ── Registered for this event (top section) ── -->
      @if (enteredTeams().length > 0) {
        <div class="step-card">
          <div class="section-header section-registered">
            <i class="bi bi-check-circle-fill me-1"></i>
            Registered ({{ enteredTeams().length }})
            <span class="registered-total">{{ totalFee() | currency }}</span>
          </div>

          <div style="padding: var(--space-2) var(--space-3)">
            <app-registered-teams-grid
              [teams]="enteredTeams()"
              [showDeposit]="showDepositColumns()"
              [showProcessing]="showProcessingColumn()"
              [showRemove]="true"
              [actionInProgress]="actionInProgress()"
              [frozenTeamCol]="true"
              [teamColWidth]="180"
              (removeTeam)="onRemoveTeam($event)" />
          </div>

          <div class="step-card-footer">
            <span></span>
            <button type="button" class="btn btn-sm btn-success fw-semibold"
                    (click)="proceedToPayment.emit()">
              Proceed to Payment <i class="bi bi-arrow-right ms-1"></i>
            </button>
          </div>
        </div>
      } @else {
        <div class="step-card">
          <div class="wizard-empty-state" style="padding: var(--space-6) var(--space-4)">
            <i class="bi bi-clipboard-plus"></i>
            <strong>No teams registered yet</strong>
            <span>Tap <strong>Register</strong> next to a team below to get started.</span>
          </div>
        </div>
      }

      <!-- ── Team Library (bottom section) ── -->
      <div class="step-card">

        <!-- Library header with stats -->
        <div class="lib-header">
          <div class="lib-stats">
            <span class="stat-number">{{ allLibraryTeams().length }}</span>
            <span class="stat-label">{{ allLibraryTeams().length === 1 ? 'team' : 'teams' }} in library</span>
          </div>
          <button type="button" class="btn btn-outline-primary btn-sm"
                  (click)="showAddModal.set(true)">
            <i class="bi bi-plus-circle me-1"></i>Add Team
          </button>
        </div>

        @if (allLibraryTeams().length === 0) {
          <div class="wizard-empty-state">
            <i class="bi bi-plus-circle-dotted"></i>
            <strong>Your library is empty</strong>
            <span>Every team your club fields should be added here.<br>They're saved permanently — add once, register in any event.</span>
            <button type="button" class="btn btn-primary btn-sm mt-2"
                    (click)="showAddModal.set(true)">
              <i class="bi bi-plus-circle me-1"></i>Add Your First Team
            </button>
          </div>
        } @else {
          <div class="scroll-list">
            @for (team of allLibraryTeamsSorted(); track team.clubTeamId) {
              <div class="lib-row" [class.lib-row-registered]="isEnteredTeam(team.clubTeamId)">
                <i class="bi bi-people-fill lib-icon"></i>
                <span class="lib-name">{{ team.clubTeamName }}</span>
                @if (isEnteredTeam(team.clubTeamId)) {
                  <span class="lib-badge"><i class="bi bi-check-circle-fill me-1"></i>Registered</span>
                } @else {
                  <button type="button" class="btn-register"
                          [disabled]="actionInProgress()"
                          (click)="openAgePicker({clubTeamId: team.clubTeamId, clubTeamName: team.clubTeamName, gradYear: team.clubTeamGradYear, levelOfPlay: team.clubTeamLevelOfPlay}, false)">
                    Register
                  </button>
                }
              </div>
            }
          </div>
        }

        <div class="step-card-footer">
          <span class="footer-hint">
            <i class="bi bi-shield-check me-1"></i>Your library is saved across all events
          </span>
        </div>
      </div>
    }

    <!-- ═══ MODALS ═══ -->
    @if (showAddModal()) {
      <app-team-form-modal
        [clubName]="clubName()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
    }

    @if (agePickerTeam(); as pickerTeam) {
      <app-age-group-picker-modal
        [teamName]="pickerTeam.clubTeamName"
        [eventName]="jobName()"
        [gradYear]="pickerTeam.gradYear"
        [levelOfPlay]="pickerTeam.levelOfPlay"
        [currentAgeGroupId]="pickerTeam.currentAgeGroupId ?? ''"
        [ageGroups]="ageGroups()"
        [lopOptions]="lopOptions()"
        (selected)="onModalAgeGroupSelected(pickerTeam, $event)"
        (closed)="agePickerTeam.set(null)" />
    }

    @if (pendingRemove()) {
      <confirm-dialog
        title="Remove Team"
        [message]="'Remove <strong>' + pendingRemove()!.teamName + '</strong> from this event?'"
        confirmLabel="Remove"
        confirmVariant="danger"
        (confirmed)="confirmRemove()"
        (cancelled)="cancelRemove()" />
    }

  `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-4); }

      /* ── Coach Card — detached contextual guidance ── */
      .coach-card {
        border-radius: var(--radius-md);
        border: 1px solid var(--border-color);
        border-left: 4px solid var(--bs-primary);
        padding: var(--space-4);
        box-shadow: var(--shadow-xs);
        background: rgba(var(--bs-info-rgb), 0.04);
      }

      .coach-primary { border-left-color: var(--bs-primary); }
      .coach-info    { border-left-color: var(--bs-info); }
      .coach-success { border-left-color: var(--bs-success); }

      .coach-intro {
        margin: 0 0 var(--space-3);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        line-height: var(--line-height-relaxed);
        color: var(--brand-text);

        &:last-child { margin-bottom: 0; }

        i {
          font-size: var(--font-size-sm);
          vertical-align: -1px;
        }
      }

      .coach-list {
        list-style: none;
        margin: 0;
        padding: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        line-height: var(--line-height-normal);
      }

      .coach-list li {
        display: flex;
        align-items: baseline;
        gap: var(--space-2);
      }

      .coach-list li i {
        flex-shrink: 0;
        font-size: var(--font-size-sm);
      }

      .coach-list li strong {
        color: var(--brand-text);
      }

      /* ── Library Header / Stats ──────────────────── */
      .lib-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-3);
        padding: var(--space-2) var(--space-4);
        border-bottom: 1px solid var(--border-color);
      }

      .lib-stats {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .stat-number {
        font-size: var(--font-size-xl);
        font-weight: var(--font-weight-bold);
        color: var(--bs-primary);
        line-height: 1;
      }

      .stat-label {
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      .stat-divider {
        width: 1px;
        height: 16px;
        background: var(--border-color);
      }

      .stat-registered {
        color: var(--bs-success);
        font-weight: var(--font-weight-semibold);
      }

      /* ── Year Group Headers ──────────────────────── */
      .year-group-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-1) var(--space-3);
        background: rgba(var(--bs-primary-rgb), 0.04);
        border-bottom: 1px solid rgba(var(--bs-primary-rgb), 0.08);
      }

      .year-label {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        color: var(--bs-primary);
        letter-spacing: 0.02em;

        > i {
          font-size: var(--font-size-sm);
          -webkit-text-stroke: 0.5px currentColor;
          width: 16px;
          text-align: center;
          flex-shrink: 0;
        }
      }

      .year-count {
        font-size: 10px;
        color: var(--brand-text-muted);
      }


      /* ── Step Card ───────────────────────────────── */
      .step-card {
        border: 1px solid var(--border-color);
        border-radius: var(--radius-lg);
        background: var(--brand-surface);
        overflow: hidden;
        box-shadow: var(--shadow-sm);
      }

      .step-card-footer {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: var(--space-2) var(--space-3);
        border-top: 1px solid var(--border-color);
        background: rgba(var(--bs-dark-rgb), 0.015);
      }

      .footer-hint {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      /* ── Step Hero Banner ────────────────────────── */
      .step-hero {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: linear-gradient(135deg, rgba(var(--bs-primary-rgb), 0.06) 0%, rgba(var(--bs-primary-rgb), 0.02) 100%);
        border-bottom: 1px solid rgba(var(--bs-primary-rgb), 0.1);
      }

      .step-hero-success {
        background: linear-gradient(135deg, rgba(var(--bs-success-rgb), 0.06) 0%, rgba(var(--bs-success-rgb), 0.02) 100%);
        border-bottom-color: rgba(var(--bs-success-rgb), 0.1);
      }

      .hero-icon {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 40px;
        height: 40px;
        min-width: 40px;
        border-radius: var(--radius-md);
        font-size: var(--font-size-lg);
        flex-shrink: 0;
      }

      .hero-icon-library {
        background: rgba(var(--bs-primary-rgb), 0.12);
        color: var(--bs-primary);
      }

      .hero-icon-select {
        background: rgba(var(--bs-info-rgb), 0.12);
        color: var(--bs-info);
      }

      .hero-icon-review {
        background: rgba(var(--bs-success-rgb), 0.12);
        color: var(--bs-success);
      }

      .hero-text {
        flex: 1;
        min-width: 0;
      }

      .hero-title {
        margin: 0;
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .hero-desc {
        margin: 2px 0 0;
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        line-height: var(--line-height-normal);
      }

      .hero-stat {
        display: inline-flex;
        align-items: center;
        margin-left: var(--space-1);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
      }

      .hero-cta {
        flex-shrink: 0;
        font-weight: var(--font-weight-semibold);
        align-self: center;
      }

      /* ── Scroll List (shared) ────────────────────── */
      .scroll-list {
        max-height: min(400px, 55vh);
        overflow-y: auto;
      }

      /* ── Step 1: Library Rows ────────────────────── */
      .lib-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-1) var(--space-3);
        min-height: 32px;
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);
        font-size: var(--font-size-xs);

        &:last-child { border-bottom: none; }
      }

      .lib-icon {
        color: rgba(var(--bs-primary-rgb), 0.4);
        font-size: var(--font-size-sm);
        flex-shrink: 0;
        width: 16px;
        text-align: center;
      }

      .lib-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
      }

      .lib-row-registered {
        background: rgba(var(--bs-success-rgb), 0.06);

        .lib-icon { color: var(--bs-success); }
        .lib-name { color: var(--bs-success); }
      }

      .lib-meta {
        color: var(--brand-text-muted);
        white-space: nowrap;
        margin-left: auto;
      }

      .lib-badge {
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-success-rgb), 0.1);
        color: var(--bs-success);
        white-space: nowrap;
        flex-shrink: 0;
        margin-left: auto;
      }

      /* ── Register button (library rows) ── */
      .btn-register {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        padding: var(--space-1) var(--space-3);
        border: 1.5px solid rgba(var(--bs-primary-rgb), 0.3);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-primary-rgb), 0.06);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        flex-shrink: 0;
        margin-left: auto;
        white-space: nowrap;
        transition: background-color 0.1s ease, border-color 0.1s ease;

        &:hover:not(:disabled) {
          background: rgba(var(--bs-primary-rgb), 0.14);
          border-color: var(--bs-primary);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.4; cursor: default; }
      }

      /* ── Step 2: Section Headers ──────────────────── */
      .section-header {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-2) var(--space-3);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.04em;
        text-transform: uppercase;
      }

      .section-registered {
        color: var(--bs-success);
        background: rgba(var(--bs-success-rgb), 0.06);
        border-bottom: 1px solid rgba(var(--bs-success-rgb), 0.12);
      }

      .section-available {
        color: var(--brand-text-muted);
        background: rgba(var(--bs-dark-rgb), 0.03);
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.08);
      }

      .registered-card {
        max-height: 220px;
        overflow-y: auto;
      }

      .registered-total {
        margin-left: auto;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
        text-transform: none;
        letter-spacing: 0;
      }

      /* ── Step 2: Row Wrapper (holds header + expansion panel) ── */
      .select-row-wrapper {
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);

        &:last-child { border-bottom: none; }
      }

      /* ── Step 2: Select Rows ─────────────────────── */
      .select-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-3);
        min-height: 40px;
        font-size: var(--font-size-xs);
        cursor: pointer;
        transition: background-color 0.1s ease;

        &:hover:not(.is-paid) {
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &:focus-visible {
          outline: none;
          box-shadow: inset 0 0 0 2px rgba(var(--bs-primary-rgb), 0.2);
        }

        &.is-checked {
          background: rgba(var(--bs-success-rgb), 0.04);
        }

        &.is-paid {
          opacity: 0.7;
          cursor: default;
        }
      }

      /* ── Visual checkbox indicator ── */
      .select-indicator {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 20px;
        height: 20px;
        min-width: 20px;
        border: 2px solid var(--neutral-300);
        border-radius: 4px;
        font-size: 11px;
        color: transparent;
        flex-shrink: 0;
        transition: all 0.1s ease;

        &.checked {
          background: var(--bs-success);
          border-color: var(--bs-success);
          color: var(--neutral-0);
        }

        &.locked {
          background: var(--neutral-400);
          border-color: var(--neutral-400);
          color: var(--neutral-0);
        }
      }

      .select-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
      }


      .select-lop {
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-dark-rgb), 0.06);
        color: var(--brand-text-muted);
        white-space: nowrap;
        flex-shrink: 0;
      }

      .select-age {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        padding: var(--space-1) var(--space-2);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        white-space: nowrap;
        flex-shrink: 0;
        margin-left: auto;
      }

      .select-fee {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        white-space: nowrap;
        flex-shrink: 0;
      }

      .paid-badge {
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-success-rgb), 0.1);
        color: var(--bs-success);
        white-space: nowrap;
        flex-shrink: 0;
      }

      /* ── Mobile ──────────────────────────────────── */
      @media (max-width: 575.98px) {
        .step-hero {
          flex-wrap: wrap;
          padding: var(--space-2) var(--space-3);
          gap: var(--space-2);
        }

        .hero-cta { width: 100%; }

        .coach-card {
          padding: var(--space-2) var(--space-3);
        }

        .lib-header {
          flex-wrap: wrap;
          padding: var(--space-2) var(--space-3);
          gap: var(--space-2);
        }

        .year-group-header {
          padding-left: var(--space-3);
          padding-right: var(--space-3);
        }

        .step-card-footer {
          padding: var(--space-2);
          flex-wrap: wrap;
          gap: var(--space-2);
        }

        .lib-row, .select-row {
          padding-left: var(--space-2);
          padding-right: var(--space-2);
        }

        .btn-register {
          min-height: 44px;
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .select-row, .select-indicator, .btn-register { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamTeamsStepComponent implements OnInit {
    readonly proceedToPayment = output<void>();

    private readonly state = inject(TeamWizardStateService);
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);
    private readonly destroyRef = inject(DestroyRef);

    readonly jobName = computed(() => this.jobService.currentJob()?.jobName ?? 'this event');

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal('your club');
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly lopOptions = signal<string[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);

    /** Conditional column visibility based on job config. */
    readonly showDepositColumns = signal(false);
    readonly showProcessingColumn = signal(false);

    /** Which team's age picker modal is open (null = closed). */
    readonly agePickerTeam = signal<AgePickerTeam | null>(null);

    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    /** All library teams: available + entered. */
    readonly allLibraryTeams = computed<ClubTeamDto[]>(() => {
        const available = this._clubTeams();
        const entered = this._registeredTeams();

        // Build pseudo-ClubTeamDto entries for entered teams that have a clubTeamId
        const enteredAsLibrary: ClubTeamDto[] = entered
            .filter(r => r.clubTeamId)
            .map(r => ({
                clubTeamId: r.clubTeamId!,
                clubTeamName: r.teamName,
                clubTeamGradYear: r.ageGroupName,
                clubTeamLevelOfPlay: r.levelOfPlay ?? '',
            }));

        // Merge: available first, then entered (deduplicated)
        const availableIds = new Set(available.map(t => t.clubTeamId));
        const enteredOnly = enteredAsLibrary.filter(t => !availableIds.has(t.clubTeamId));

        return [...available, ...enteredOnly];
    });

    /** Library teams sorted alphabetically (flat, no year grouping). */
    readonly allLibraryTeamsSorted = computed(() =>
        [...this.allLibraryTeams()].sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName)),
    );

    /** Entered teams (registered for this event). */
    readonly enteredTeams = computed(() => this._registeredTeams());

    readonly totalFee = computed(() => this._registeredTeams().reduce((s, t) => s + t.feeBase, 0));
    readonly totalOwed = computed(() => this._registeredTeams().reduce((s, t) => s + t.owedTotal, 0));

    ngOnInit(): void {
        this.loadTeamsMetadata(true);
    }

    isEnteredTeam(clubTeamId: number): boolean {
        return this._registeredTeams().some(r => r.clubTeamId === clubTeamId);
    }

    getEnteredInfo(clubTeamId: number): RegisteredTeamDto | null {
        return this._registeredTeams().find(r => r.clubTeamId === clubTeamId) ?? null;
    }

    /** Open age picker modal for a team. */
    openAgePicker(team: AgePickerTeam, paid: boolean): void {
        if (paid) return;
        this.agePickerTeam.set(team);
    }

    /** Handle age group + LOP selection from the modal. */
    onModalAgeGroupSelected(pickerTeam: AgePickerTeam, selection: AgeGroupSelection): void {
        this.agePickerTeam.set(null);

        // Build a ClubTeamDto-compatible object with the modal-selected LOP
        const team: ClubTeamDto = {
            clubTeamId: pickerTeam.clubTeamId,
            clubTeamName: pickerTeam.clubTeamName,
            clubTeamGradYear: pickerTeam.gradYear,
            clubTeamLevelOfPlay: selection.levelOfPlay || pickerTeam.levelOfPlay,
        };
        this.onSelectAgeGroup(team, selection.ageGroupId);
    }

    /** Register (or re-register) a team with the selected age group. */
    onSelectAgeGroup(team: ClubTeamDto, ageGroupId: string): void {
        const existing = this.getEnteredInfo(team.clubTeamId);

        // If already registered with same age group, no-op
        if (existing && existing.ageGroupId === ageGroupId) {
            return;
        }

        this.actionInProgress.set(true);

        const doRegister = () => {
            this.teamReg.registerTeamForEvent({
                clubTeamId: team.clubTeamId,
                ageGroupId,
                teamName: team.clubTeamName,
                clubTeamGradYear: team.clubTeamGradYear,
                levelOfPlay: team.clubTeamLevelOfPlay || undefined,
            })
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({
                    next: (resp) => {
                        this.actionInProgress.set(false);
                        if (!resp.success) {
                            this.toast.show(resp.message || 'Registration failed.', 'danger', 6000);
                            return;
                        }
                        const msg = resp.isWaitlisted
                            ? `${team.clubTeamName} waitlisted for ${resp.waitlistAgegroupName ?? ''}`
                            : `${team.clubTeamName} entered!`;
                        this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000);
                        this.loadTeamsMetadata();
                    },
                    error: () => {
                        this.actionInProgress.set(false);
                        this.toast.show('Failed to register team.', 'danger', 4000);
                        this.loadTeamsMetadata();
                    },
                });
        };

        // If changing age group, unregister first then re-register
        if (existing) {
            this.teamReg.unregisterTeamFromEvent(existing.teamId)
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({ next: doRegister, error: () => {
                    this.actionInProgress.set(false);
                    this.toast.show('Failed to change age group.', 'danger', 4000);
                    this.loadTeamsMetadata();
                }});
        } else {
            doRegister();
        }
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.loadTeamsMetadata();
    }

    pendingRemove = signal<RegisteredTeamDto | null>(null);

    onRemoveTeam(team: RegisteredTeamDto): void {
        if (team.paidTotal > 0) return;
        this.pendingRemove.set(team);
    }

    confirmRemove(): void {
        const team = this.pendingRemove();
        if (!team) return;
        this.pendingRemove.set(null);
        this.unregisterTeam(team.teamId, team.teamName);
    }

    cancelRemove(): void {
        this.pendingRemove.set(null);
    }

    // ── Private ─────────────────────────────────────────────────────

    private unregisterTeam(teamId: string, teamName: string): void {
        this.actionInProgress.set(true);

        this.teamReg.unregisterTeamFromEvent(teamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${teamName} removed.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: () => {
                    this.actionInProgress.set(false);
                    this.toast.show('Failed to remove team.', 'danger', 4000);
                    this.loadTeamsMetadata();
                },
            });
    }

    private loadTeamsMetadata(showSpinner = false): void {
        if (showSpinner) this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.loading.set(false);
                    this.clubName.set(meta.clubName || 'your club');
                    this._registeredTeams.set(meta.registeredTeams || []);
                    this._clubTeams.set(meta.clubTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    this.lopOptions.set(meta.lopOptions || []);
                    this.showDepositColumns.set(!(meta.bTeamsFullPaymentRequired ?? false));
                    this.showProcessingColumn.set(meta.bAddProcessingFees ?? false);
                    this.state.teamPayment.setTeams(meta.registeredTeams || []);
                    this.state.teamPayment.setJobPath(this.state.jobPath());
                    this.state.teamPayment.setPaymentConfig(
                        meta.paymentMethodsAllowedCode,
                        meta.bAddProcessingFees,
                        meta.bApplyProcessingFeesToTeamDeposit,
                        meta.payTo,
                        meta.mailTo,
                        meta.mailinPaymentWarning,
                    );
                    this.state.setHasActiveDiscountCodes(meta.hasActiveDiscountCodes);
                    this.state.setFullPaymentRequired(meta.bTeamsFullPaymentRequired ?? true);
                    this.state.setClubRepContact(meta.clubRepContactInfo ?? null);
                    this.state.setRefundPolicyHtml(meta.playerRegRefundPolicy ?? null);
                },
                error: () => {
                    this.loading.set(false);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }
}
