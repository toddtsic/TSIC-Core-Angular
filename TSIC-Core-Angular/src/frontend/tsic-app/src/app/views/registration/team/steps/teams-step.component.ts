import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RegisteredTeamsGridComponent } from '../components/registered-teams-grid.component';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import { AddAndRegisterTeamModalComponent } from './add-and-register-team-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { LibraryFlyinComponent, type RegisterRequest } from '../components/library-flyin.component';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto, ClubTeamDto } from '@core/api';

/**
 * Teams step — single screen combining library management + event registration.
 * Assigning an age group IS the registration act. No separate review step needed.
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [RegisteredTeamsGridComponent, TeamFormModalComponent, AddAndRegisterTeamModalComponent, ConfirmDialogComponent, LibraryFlyinComponent],
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

      <!-- ── Registered teams for THIS event (single primary card) ── -->
      <div class="step-card step-card-registered">
        @if (enteredTeams().length === 0) {
          <div class="section-header section-registered">
            <i class="bi bi-check-circle-fill me-1"></i>
            Registered Teams
          </div>
        } @else {
          <div class="section-titlebar section-titlebar-registered">
            <i class="bi bi-trophy-fill section-titlebar-icon" aria-hidden="true"></i>
            <h3 class="section-titlebar-title">
              <span class="section-titlebar-tail">Registered Teams</span>
            </h3>
            <span class="phase-badge">
              <span class="phase-badge__label">Payment Phase</span>
              <span class="phase-badge__value">{{ phaseBadgeLabel() }}</span>
            </span>
          </div>
        }

        @if (enteredTeams().length === 0) {
          @if (allLibraryTeams().length === 0) {
            <div class="two-step-hero">
              <div class="two-step-eyebrow">
                <span>How Team Registration Works</span>
              </div>
              <h3 class="two-step-headline">
                Two steps to get your teams into the <span class="event-name">{{ eventName() }}</span>
              </h3>

              <div class="two-step-cards">
                <div class="step-mini step-mini-library">
                  <div class="step-mini-head">
                    <span class="step-mini-num">1</span>
                    <i class="bi bi-collection-fill" aria-hidden="true"></i>
                  </div>
                  <strong>Build your Club Library</strong>
                  <span>
                    Add each team to your library &mdash; anytime, one at a time.
                    Once it's in, it's there for <em>every</em> future TSIC event &mdash; no re-entry.
                  </span>
                </div>

                <div class="two-step-arrow" aria-hidden="true">
                  <i class="bi bi-arrow-right"></i>
                </div>

                <div class="step-mini step-mini-event">
                  <div class="step-mini-head">
                    <span class="step-mini-num">2</span>
                    <i class="bi bi-trophy-fill" aria-hidden="true"></i>
                  </div>
                  <strong>Register for this event</strong>
                  <span>
                    Pick teams from your library to register to play in the
                    <strong>{{ eventName() }}</strong>.
                  </span>
                </div>
              </div>

              <button type="button" class="btn btn-success btn-lg cta-empty cta-empty-library"
                      (click)="showAddAndRegisterModal.set(true)">
                <i class="bi bi-trophy-fill me-2"></i>
                Register Your First Team for this Event
                <i class="bi bi-arrow-right ms-2 cta-empty-arrow"></i>
              </button>
            </div>
          } @else {
            <div class="wizard-empty-state cta-empty-wrap" style="padding: var(--space-6) var(--space-4)">
              <i class="bi bi-clipboard-plus"></i>
              <strong>{{ allLibraryTeams().length }} library
                {{ allLibraryTeams().length === 1 ? 'team' : 'teams' }}
                ready &mdash; none registered for the {{ eventName() }} yet</strong>
              <span>Pick from your library to register to play in the <strong>{{ eventName() }}</strong>.</span>
              <button type="button" class="btn btn-success btn-lg cta-empty cta-empty-event"
                      (click)="openLibraryFlyin()">
                <i class="bi bi-trophy-fill me-2"></i>
                Register Your First Team for this Event
                <i class="bi bi-arrow-right ms-2 cta-empty-arrow"></i>
              </button>
            </div>
          }
        } @else {
          <div style="padding: var(--space-2) var(--space-3)">
            <app-registered-teams-grid
              [teams]="enteredTeams()"
              [showStructure]="true"
              [showDeposit]="false"
              [showBalance]="false"
              [showOwed]="false"
              [showPaid]="false"
              [showProcessing]="false"
              [showCcOwed]="false"
              [showCkOwed]="false"
              [showRegDate]="false"
              [showLop]="true"
              [showRemove]="canRemoveTeam()"
              [actionInProgress]="actionInProgress()"
              [frozenTeamCol]="false"
              [teamColWidth]="120"
              [gridHeight]="'auto'"
              (removeTeam)="onRemoveTeam($event)" />
          </div>

          <div class="step-card-footer">
            <div class="action-segments" role="group" aria-label="What's next?">
              <button type="button" class="action-segment action-segment-stay"
                      (click)="openLibraryFlyin()">
                <i class="bi bi-plus-circle-fill action-segment-icon" aria-hidden="true"></i>
                <span class="action-segment-content">
                  <span class="action-segment-title">Register Another Team</span>
                  <span class="action-segment-sub">Go to Club Team Library</span>
                </span>
              </button>

              <button type="button" class="action-segment action-segment-advance"
                      (click)="proceedToPayment.emit()">
                <span class="action-segment-content">
                  <span class="action-segment-title">Continue to Payment</span>
                  <span class="action-segment-sub">All my teams are registered</span>
                </span>
                <i class="bi bi-currency-dollar action-segment-icon" aria-hidden="true"></i>
              </button>
            </div>
          </div>
        }
      </div>

      <!-- ── Library fly-in (right-side drawer; shows on demand) ── -->
      <app-library-flyin
        [isOpen]="showLibraryFlyin()"
        [clubTeams]="allLibraryTeams()"
        [clubName]="clubName()"
        [canRegister]="canRegisterTeam()"
        [canEdit]="canEditTeam()"
        [actionInProgress]="actionInProgress()"
        [ageGroups]="ageGroups()"
        [enteredTeams]="enteredTeamsMap()"
        [droppedTeams]="droppedTeams()"
        (closed)="closeLibraryFlyin()"
        (register)="onFlyinRegister($event)"
        (addNew)="showAddModal.set(true)"
        (edit)="openEditModal($event)"
        (archive)="askArchiveTeam($event)"
        (delete)="askDeleteTeam($event)"
        (restore)="askRestoreTeam($event)" />
    }

    <!-- ═══ MODALS ═══ -->
    @if (showAddModal()) {
      <app-team-form-modal
        [clubName]="clubName()"
        [existingTeams]="allLibraryTeams()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
    }

    @if (showAddAndRegisterModal()) {
      <app-add-and-register-team-modal
        [clubName]="clubName()"
        [eventName]="eventName()"
        [ageGroups]="ageGroups()"
        (saved)="onAddAndRegisterSaved()"
        (closed)="showAddAndRegisterModal.set(false)" />
    }

    @if (editingTeam(); as editing) {
      <app-team-form-modal
        [clubName]="clubName()"
        [editingTeam]="editing"
        [existingTeams]="allLibraryTeams()"
        (saved)="onTeamEdited()"
        (closed)="editingTeam.set(null)" />
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

    @if (pendingDelete()) {
      <confirm-dialog
        title="Delete Team"
        [message]="'Permanently delete <strong>' + pendingDelete()!.clubTeamName + '</strong> from your library? This cannot be undone.'"
        confirmLabel="Delete"
        confirmVariant="danger"
        (confirmed)="confirmDelete()"
        (cancelled)="cancelDelete()" />
    }

    @if (pendingArchive()) {
      <confirm-dialog
        title="Archive Team"
        [message]="'Archive <strong>' + pendingArchive()!.clubTeamName + '</strong>? It will disappear from your library but keep its event history. You can restore it from the Archived section.'"
        confirmLabel="Archive"
        confirmVariant="primary"
        (confirmed)="confirmArchive()"
        (cancelled)="cancelArchive()" />
    }

    @if (pendingRestore()) {
      <confirm-dialog
        title="Restore Team"
        [message]="'Restore <strong>' + pendingRestore()!.clubTeamName + '</strong> to your active library?'"
        confirmLabel="Restore"
        confirmVariant="primary"
        (confirmed)="confirmRestore()"
        (cancelled)="cancelRestore()" />
    }

  `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-4); }

      /* .step-card / .step-card-registered live in styles/_wizard-globals.scss
         so payment-step shares the same outer chrome. */

      /* Footer = decision fork rendered as a segmented control. Two halves
         joined by a single divider, sharing one outer border. Each segment
         carries its accent color at rest (subtle tint + 3px top accent bar)
         so the fork is visible without hover. Equal visual weight — neither
         side dominates; the rep picks based on which subtext is true. */
      .step-card-footer {
        padding: var(--space-3);
        border-top: 1px solid var(--border-color);
        background: rgba(var(--bs-dark-rgb), 0.015);
      }

      .action-segments {
        display: grid;
        grid-template-columns: 1fr 1fr;
        border-radius: var(--radius-md);
        overflow: hidden;
        box-shadow: var(--shadow-sm);
      }

      .action-segment {
        position: relative;
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-4) var(--space-4);
        border: none;
        border-radius: 0;
        font-family: inherit;
        text-align: left;
        cursor: pointer;
        transition: background 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;

        &:focus-visible {
          outline: none;
          box-shadow: inset 0 0 0 2px currentColor;
        }
      }

      .action-segment-icon {
        font-size: 2rem;
        flex-shrink: 0;
        line-height: 1;
      }

      .action-segment-content {
        display: flex;
        flex-direction: column;
        gap: 2px;
        min-width: 0;
        flex: 1;
      }

      .action-segment-title {
        font-size: var(--font-size-base);
        font-weight: var(--font-weight-bold);
        line-height: var(--line-height-tight);
        color: currentColor;
      }

      .action-segment-sub {
        font-size: var(--font-size-xs);
        font-style: italic;
        line-height: var(--line-height-normal);
        color: color-mix(in srgb, currentColor 80%, var(--brand-text));
      }

      /* Each option owns its color zone from rest — two distinct tinted
         backgrounds split the bar, the contrast between them reads as the
         divider. Hover deepens the same tint and lifts a hair. Light/dark
         compatible: color-mix to transparent layers over whatever surface
         the parent provides. */
      .action-segment-stay {
        color: var(--amber-700);
        background: color-mix(in srgb, var(--amber-500) 18%, transparent);

        &:hover {
          background: color-mix(in srgb, var(--amber-500) 32%, transparent);
          box-shadow: inset 0 -2px 0 var(--amber-700);
        }
        &:active { transform: translateY(1px); }
      }

      .action-segment-advance {
        color: var(--emerald-600);
        background: color-mix(in srgb, var(--emerald-600) 14%, transparent);

        &:hover {
          background: color-mix(in srgb, var(--emerald-600) 26%, transparent);
          box-shadow: inset 0 -2px 0 var(--emerald-600);
        }
        &:active { transform: translateY(1px); }
      }

      @media (prefers-reduced-motion: reduce) {
        .action-segment { transition: none !important; }
        .action-segment:active { transform: none; }
      }

      /* ── Section banner header ── */
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

      /* .section-titlebar / .section-titlebar-* / .phase-badge live in
         styles/_wizard-globals.scss — see those files for shared chrome. */

      /* Compact action button nested inside a .section-header banner */
      .section-action {
        margin-left: auto;
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: none;
        letter-spacing: 0;
      }

      /* ── Two-step hero (library=0, registered=0) ────────────────────
         Library → Event story, told visually as two side-by-side cards
         with an arrow between. Color-coded: library = primary, event = success
         (matches the green border of the parent Registered card). */
      .two-step-hero {
        display: flex;
        flex-direction: column;
        align-items: center;
        text-align: center;
        gap: var(--space-3);
        padding: var(--space-6) var(--space-4);
      }

      .two-step-eyebrow {
        display: inline-flex;
        align-items: center;
        gap: var(--space-3);
        font-size: 11px;
        font-weight: var(--font-weight-bold);
        letter-spacing: 0.1em;
        text-transform: uppercase;
        color: var(--brand-text-muted);

        &::before,
        &::after {
          content: '';
          display: block;
          width: 56px;
          height: 1px;
        }

        &::before {
          background: linear-gradient(to right,
            transparent,
            rgba(var(--bs-success-rgb), 0.45));
        }

        &::after {
          background: linear-gradient(to left,
            transparent,
            rgba(var(--bs-success-rgb), 0.45));
        }

        > span {
          display: inline-flex;
          align-items: center;
          gap: var(--space-2);
        }

        > span::before,
        > span::after {
          content: '';
          display: inline-block;
          width: 4px;
          height: 4px;
          border-radius: 50%;
          background: rgba(var(--bs-success-rgb), 0.55);
        }
      }

      .two-step-headline {
        margin: 0 0 var(--space-2);
        font-size: var(--font-size-lg);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        line-height: var(--line-height-tight);
        max-width: 640px;

        .event-name { color: var(--bs-success); white-space: nowrap; }
      }

      .two-step-cards {
        display: flex;
        align-items: stretch;
        justify-content: center;
        gap: var(--space-3);
        width: 100%;
        max-width: 720px;
      }

      .step-mini {
        flex: 1 1 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
        padding: var(--space-4) var(--space-3);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        text-align: center;
        box-shadow: var(--shadow-xs);

        strong {
          color: var(--brand-text);
          font-size: var(--font-size-base);
          line-height: var(--line-height-tight);
        }

        > span {
          font-size: var(--font-size-sm);
          color: var(--brand-text-muted);
          line-height: var(--line-height-normal);
        }

        em { font-style: italic; }
      }

      .step-mini-library {
        border-top: 3px solid var(--bs-primary);

        .step-mini-num { background: var(--bs-primary); }
        .step-mini-head > i { color: var(--bs-primary); }
      }

      .step-mini-event {
        border-top: 3px solid var(--bs-success);

        .step-mini-num { background: var(--bs-success); }
        .step-mini-head > i { color: var(--bs-success); }
      }

      .step-mini-head {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-2);
      }

      .step-mini-num {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 50%;
        color: var(--neutral-0);
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-xs);
        line-height: 1;
        flex-shrink: 0;
      }

      .step-mini-head > i { font-size: var(--font-size-xl); }

      .two-step-arrow {
        display: flex;
        align-items: center;
        color: var(--brand-text-muted);
        opacity: 0.6;

        i { font-size: var(--font-size-2xl); }
      }

      /* ── Empty-state primary CTA ────────────────────────────────────
         Replaces the "tap above" pointer. Lives where the eye is. */
      .cta-empty {
        display: inline-flex;
        align-items: center;
        margin-top: var(--space-3);
        padding: var(--space-2) var(--space-5);
        font-weight: var(--font-weight-semibold);
        font-size: var(--font-size-base);
        box-shadow: var(--shadow-md);
        transition: transform 0.12s ease, box-shadow 0.12s ease;

        &:hover { transform: translateY(-1px); box-shadow: var(--shadow-lg); }
        &:active { transform: translateY(0); }
      }

      .cta-empty-arrow { transition: transform 0.18s ease; }
      .cta-empty:hover .cta-empty-arrow { transform: translateX(3px); }

      .cta-empty-wrap {
        align-items: center;
      }

      @media (prefers-reduced-motion: reduce) {
        .cta-empty,
        .cta-empty-arrow { transition: none !important; }
        .cta-empty:hover { transform: none; }
      }

      @media (max-width: 575.98px) {
        .two-step-cards { flex-direction: column; }
        .two-step-arrow { transform: rotate(90deg); margin: 0 auto; }
        .two-step-headline { font-size: var(--font-size-base); }
      }

      /* ── Mobile ── */
      @media (max-width: 575.98px) {
        .step-card-footer {
          padding: var(--space-2);
        }

        .action-segments {
          grid-template-columns: 1fr;
        }

        /* Stacked: divider becomes horizontal. */
        .action-segment + .action-segment {
          border-left: none;
          border-top: 1px solid var(--border-color);
        }
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

    /** Clean event name with the org-prefix and colon stripped — same split as
        team.component.ts page hero, so child components see only the headline
        portion ("Carolina Clash 2026" not "Top Threat Tournaments:Carolina Clash 2026"). */
    readonly eventName = computed(() => {
        const raw = this.jobService.currentJob()?.jobName ?? 'this event';
        const idx = raw.indexOf(':');
        return idx > 0 ? raw.substring(idx + 1).trim() : raw;
    });

    /** Capability flags from job pulse — gate the Register/Remove/Edit UI controls. */
    readonly canRegisterTeam = this.state.canRegisterTeam;
    readonly canRemoveTeam = this.state.canRemoveTeam;
    readonly canEditTeam = this.state.canEditTeam;

    /** Job-config flag — the phase baseline (default before any team is entered). */
    readonly fullPaymentRequired = this.state.fullPaymentRequired;

    /**
     * Phase badge label derived PER-ROW from the entered teams' server-resolved
     * fullPaymentRequired — NOT the single job flag, since a club-rep cart can span scopes
     * that differ in phase. 'Mixed' when the entered teams disagree. Falls back to the job
     * baseline before any team is entered.
     */
    readonly phaseBadgeLabel = computed(() => {
        const teams = this._registeredTeams();
        if (!teams.length) return this.state.fullPaymentRequired() ? 'Final Balance Due' : 'Deposit Only';
        const full = teams.filter(t => t.fullPaymentRequired).length;
        if (full === 0) return 'Deposit Only';
        if (full === teams.length) return 'Final Balance Due';
        return 'Mixed';
    });

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal('your club');
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);
    /** Combined add+register modal — only used for the empty-empty first-team flow. */
    readonly showAddAndRegisterModal = signal(false);
    /** When set, the edit modal is open for this team. */
    readonly editingTeam = signal<ClubTeamDto | null>(null);
    /** When set, the delete-confirm dialog is open for this team. */
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    /** When set, the archive-confirm dialog is open for this team. */
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    /** When set, the restore-confirm dialog is open for this team. */
    readonly pendingRestore = signal<ClubTeamDto | null>(null);
    /** Library fly-in open state. Opens only on explicit user action — never auto-opened. */
    readonly showLibraryFlyin = signal(false);

    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    /** Teams a director moved into a "DROPPED" age group — read-only history fed
     *  straight to the library fly-in's muted Dropped section. */
    private readonly _droppedTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    readonly droppedTeams = computed(() => this._droppedTeams());

    /** All library teams. Backend now returns the full library in `clubTeams`
     *  (previously filtered to "not yet registered"); the registration map for
     *  this event is exposed separately via `enteredTeams`. */
    readonly allLibraryTeams = computed<ClubTeamDto[]>(() => this._clubTeams());

    /** Entered teams (registered for this event). */
    readonly enteredTeams = computed(() => this._registeredTeams());

    /**
     * Map of clubTeamId → registration info — flyin uses this to mark rows as
     * Registered AND to display *which* age group + LOP each is registered as.
     */
    readonly enteredTeamsMap = computed(() => {
        const map = new Map<number, { ageGroupName: string; levelOfPlay: string }>();
        for (const r of this._registeredTeams()) {
            if (r.clubTeamId != null) {
                map.set(r.clubTeamId, {
                    ageGroupName: r.ageGroupName ?? '',
                    levelOfPlay: r.levelOfPlay ?? '',
                });
            }
        }
        return map;
    });

    readonly anyPaid = computed(() => this._registeredTeams().some(t => t.paidTotal > 0));
    readonly anyOwed = computed(() => this._registeredTeams().some(t => t.owedTotal > 0));

    ngOnInit(): void {
        this.loadTeamsMetadata(true);
    }

    isEnteredTeam(clubTeamId: number): boolean {
        return this._registeredTeams().some(r => r.clubTeamId === clubTeamId);
    }

    getEnteredInfo(clubTeamId: number): RegisteredTeamDto | null {
        return this._registeredTeams().find(r => r.clubTeamId === clubTeamId) ?? null;
    }

    /** Library fly-in open/close. */
    openLibraryFlyin(): void { this.showLibraryFlyin.set(true); }
    closeLibraryFlyin(): void { this.showLibraryFlyin.set(false); }

    /** Flyin emits {team, ageGroupId, levelOfPlay} from its inline-expand picker. */
    onFlyinRegister(req: RegisterRequest): void {
        // Build a ClubTeamDto-compatible object with the picker-selected LOP so
        // downstream registerTeamForEvent gets the rep's adjusted value rather
        // than the library team's stored default.
        const team: ClubTeamDto = {
            ...req.team,
            clubTeamLevelOfPlay: req.levelOfPlay || req.team.clubTeamLevelOfPlay,
        };
        this.onSelectAgeGroup(team, req.ageGroupId);
    }

    /** Register (or re-register) a team with the selected age group. */
    onSelectAgeGroup(team: ClubTeamDto, ageGroupId: string): void {
        const existing = this.getEnteredInfo(team.clubTeamId);

        // If already registered with the SAME age group AND the same LOP, no-op.
        // (Compare LOP too — an edit that changes only the Level of Play keeps the
        // age group, and must still go through the unregister/re-register path.)
        if (existing
            && existing.ageGroupId === ageGroupId
            && (existing.levelOfPlay ?? '') === (team.clubTeamLevelOfPlay ?? '')) {
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
                            : `${team.clubTeamName} registered for the event!`;
                        this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000);
                        // Keep the flyin open: the row transitions in-place to a green "Registered"
                        // badge, reinforcing that the library team is now ALSO an event registration.
                        // Lets the rep register multiple teams in a row without re-opening the drawer.
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

    /** Combined add+register modal succeeded — close it and refresh state. */
    onAddAndRegisterSaved(): void {
        this.showAddAndRegisterModal.set(false);
        this.loadTeamsMetadata();
    }

    /** Open the shared modal in edit mode for a library team. */
    openEditModal(team: ClubTeamDto): void {
        if (team.bHasBeenScheduled) return;
        this.editingTeam.set(team);
    }

    onTeamEdited(): void {
        this.editingTeam.set(null);
        this.loadTeamsMetadata();
    }

    askDeleteTeam(team: ClubTeamDto): void {
        if (team.bHasBeenScheduled) return;
        this.pendingDelete.set(team);
    }

    confirmDelete(): void {
        const team = this.pendingDelete();
        if (!team) return;
        this.pendingDelete.set(null);
        this.actionInProgress.set(true);

        this.teamReg.deleteClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${team.clubTeamName} deleted from library.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to delete team.', 'danger', 5000);
                },
            });
    }

    cancelDelete(): void {
        this.pendingDelete.set(null);
    }

    askArchiveTeam(team: ClubTeamDto): void {
        if (!team.bHasBeenScheduled || this.isEnteredTeam(team.clubTeamId)) return;
        this.pendingArchive.set(team);
    }

    confirmArchive(): void {
        const team = this.pendingArchive();
        if (!team) return;
        this.pendingArchive.set(null);
        this.actionInProgress.set(true);

        this.teamReg.archiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${team.clubTeamName} archived from library.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to archive team.', 'danger', 5000);
                },
            });
    }

    cancelArchive(): void {
        this.pendingArchive.set(null);
    }

    askRestoreTeam(team: ClubTeamDto): void {
        if (!team.bArchived) return;
        this.pendingRestore.set(team);
    }

    confirmRestore(): void {
        const team = this.pendingRestore();
        if (!team) return;
        this.pendingRestore.set(null);
        this.actionInProgress.set(true);

        this.teamReg.unarchiveClubTeam(team.clubTeamId)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: () => {
                    this.actionInProgress.set(false);
                    this.toast.show(`${team.clubTeamName} restored to library.`, 'success', 3000);
                    this.loadTeamsMetadata();
                },
                error: (err: unknown) => {
                    this.actionInProgress.set(false);
                    const httpErr = err as { error?: { message?: string } };
                    this.toast.show(httpErr?.error?.message || 'Failed to restore team.', 'danger', 5000);
                },
            });
    }

    cancelRestore(): void {
        this.pendingRestore.set(null);
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
                    this.toast.show(`${teamName} removed from event.`, 'success', 3000);
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
                    this._droppedTeams.set(meta.droppedTeams || []);
                    this._clubTeams.set(meta.clubTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    this.state.applyTeamsMetadata(meta);
                },
                error: () => {
                    this.loading.set(false);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }
}
