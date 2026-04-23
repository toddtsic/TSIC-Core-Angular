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

      <!-- Coach card — only shown when library is empty; the populated-library
           variant migrated to a wizard-tip inside the Library card header. -->
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
      }

      <!-- ── Registered for this event (top section) ── -->
      @if (enteredTeams().length > 0) {
        <div class="step-card step-card-registered">
          <div class="section-header section-registered">
            <i class="bi bi-check-circle-fill me-1"></i>
            Registered Teams ({{ enteredTeams().length }})
            <span class="registered-total">{{ totalFee() | currency }}</span>
          </div>

          <div style="padding: var(--space-2) var(--space-3)">
            <app-registered-teams-grid
              [teams]="enteredTeams()"
              [showDeposit]="true"
              [showProcessing]="showProcessingColumn()"
              [showCcOwed]="allowsCc()"
              [showCkOwed]="showProcessingColumn()"
              [showLop]="true"
              [showRemove]="canRemoveTeam()"
              [actionInProgress]="actionInProgress()"
              [frozenTeamCol]="true"
              [teamColWidth]="140"
              [gridHeight]="180"
              (removeTeam)="onRemoveTeam($event)" />
          </div>

          <div class="step-card-footer">
            <span></span>
            <button type="button" class="btn btn-sm btn-success fw-semibold"
                    (click)="proceedToPayment.emit()">
              {{ proceedButtonLabel() }} <i class="bi bi-arrow-right ms-1"></i>
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
      <div class="step-card step-card-library">

        <!-- Library header — mirrors Registered section banner -->
        <div class="section-header section-library">
          <i class="bi bi-collection-fill me-1"></i>
          Library Teams ({{ activeLibraryTeams().length }})
          <button type="button" class="btn btn-outline-primary btn-sm ms-auto section-action"
                  (click)="showAddModal.set(true)">
            <i class="bi bi-plus-circle me-1"></i>Add Team
          </button>
        </div>

        @if (activeLibraryTeams().length > 0) {
          <div class="wizard-tip library-tip">
            Your library is permanent — teams you add here carry to every TSIC event. Click <strong>Register</strong> next to a team to enter it in this event, or <button type="button" class="wizard-callout-link" (click)="showAddModal.set(true)">Add Team</button> if the one you need isn't in the library yet.
          </div>
        }

        @if (activeLibraryTeams().length === 0 && archivedLibraryTeams().length === 0) {
          <div class="wizard-empty-state">
            <i class="bi bi-plus-circle-dotted"></i>
            <strong>Build your Team Library</strong>
            <span>Add every team your club runs — they stay saved, so you add once and register in any event.</span>
            <button type="button" class="btn btn-primary btn-sm mt-2"
                    (click)="showAddModal.set(true)">
              <i class="bi bi-plus-circle me-1"></i>Add Your First Team
            </button>
          </div>
        } @else if (activeLibraryTeams().length === 0) {
          <div class="wizard-empty-state">
            <i class="bi bi-archive"></i>
            <strong>All teams are archived</strong>
            <span>Restore a team below, or <button type="button" class="wizard-callout-link" (click)="showAddModal.set(true)">add a new one</button> to start fresh.</span>
          </div>
        } @else {
          <div class="scroll-list">
            @for (team of activeLibraryTeams(); track team.clubTeamId) {
              <div class="lib-row" [class.lib-row-registered]="isEnteredTeam(team.clubTeamId)">
                <i class="bi bi-people-fill lib-icon"></i>
                <span class="lib-name">{{ team.clubTeamName }}</span>
                <span class="lib-actions">
                  @if (team.bHasBeenScheduled) {
                    @if (!isEnteredTeam(team.clubTeamId)) {
                      <button type="button" class="lib-icon-btn" title="Archive — hide from library, keep history"
                              [disabled]="actionInProgress()"
                              (click)="askArchiveTeam(team)">
                        <i class="bi bi-box-arrow-in-down"></i>
                      </button>
                    }
                    <i class="bi bi-lock-fill lib-lock" title="Locked — this team has event history"></i>
                  } @else {
                    @if (!isEnteredTeam(team.clubTeamId)) {
                      <button type="button" class="lib-icon-btn lib-icon-danger" title="Delete team"
                              [disabled]="actionInProgress()"
                              (click)="askDeleteTeam(team)">
                        <i class="bi bi-trash"></i>
                      </button>
                    }
                    <button type="button" class="lib-icon-btn" title="Edit team"
                            [disabled]="actionInProgress()"
                            (click)="openEditModal(team)">
                      <i class="bi bi-pencil"></i>
                    </button>
                  }
                </span>
                @if (isEnteredTeam(team.clubTeamId)) {
                  <span class="lib-badge"><i class="bi bi-check-circle-fill me-1"></i>Registered</span>
                } @else if (canRegisterTeam()) {
                  <button type="button" class="btn-register"
                          [disabled]="actionInProgress()"
                          (click)="openAgePicker({clubTeamId: team.clubTeamId, clubTeamName: team.clubTeamName, gradYear: team.clubTeamGradYear, levelOfPlay: team.clubTeamLevelOfPlay}, false)">
                    Register
                  </button>
                } @else {
                  <span class="lib-badge lib-badge-muted"><i class="bi bi-lock-fill me-1"></i>Closed</span>
                }
              </div>
            }
          </div>
        }

        <!-- ── Archived sub-section (collapsible) ── -->
        @if (archivedLibraryTeams().length > 0) {
          <button type="button" class="archived-header" (click)="showArchived.set(!showArchived())">
            <i class="bi" [class.bi-chevron-right]="!showArchived()" [class.bi-chevron-down]="showArchived()"></i>
            <i class="bi bi-archive-fill"></i>
            Archived ({{ archivedLibraryTeams().length }})
            <span class="archived-hint">teams hidden from registration</span>
          </button>
          @if (showArchived()) {
            <div class="scroll-list archived-list">
              @for (team of archivedLibraryTeams(); track team.clubTeamId) {
                <div class="lib-row lib-row-archived">
                  <i class="bi bi-archive-fill lib-icon"></i>
                  <span class="lib-name">{{ team.clubTeamName }}</span>
                  <span class="lib-actions">
                    <i class="bi bi-lock-fill lib-lock" title="Locked — archived team retains event history"></i>
                    <button type="button" class="lib-icon-btn" title="Restore to library"
                            [disabled]="actionInProgress()"
                            (click)="askRestoreTeam(team)">
                      <i class="bi bi-arrow-counterclockwise"></i>
                    </button>
                  </span>
                  <span class="lib-badge lib-badge-muted"><i class="bi bi-archive me-1"></i>Archived</span>
                </div>
              }
            </div>
          }
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

    @if (editingTeam(); as editing) {
      <app-team-form-modal
        [clubName]="clubName()"
        [editingTeam]="editing"
        (saved)="onTeamEdited()"
        (closed)="editingTeam.set(null)" />
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
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 96px;
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-success-rgb), 0.1);
        color: var(--bs-success);
        white-space: nowrap;
        flex-shrink: 0;
      }

      .lib-badge-muted {
        background: rgba(var(--bs-secondary-rgb), 0.1);
        color: var(--brand-text-muted);
      }

      /* ── Library row actions (edit / delete / archive / lock) ──
         Fixed width so that rows with only a lock align column-wise with rows
         that also have an archive or edit/delete. Lock pins to the right. */
      .lib-actions {
        display: inline-flex;
        align-items: center;
        justify-content: flex-end;
        gap: 2px;
        width: 64px;
        margin-left: auto;
        flex-shrink: 0;
      }

      .lib-icon-btn {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        padding: 0;
        border: 1px solid transparent;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        cursor: pointer;
        transition: background-color 0.1s ease, color 0.1s ease, border-color 0.1s ease;

        &:hover:not(:disabled) {
          background: rgba(var(--bs-primary-rgb), 0.08);
          border-color: rgba(var(--bs-primary-rgb), 0.2);
          color: var(--bs-primary);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:disabled { opacity: 0.3; cursor: default; }
      }

      .lib-icon-danger:hover:not(:disabled) {
        background: rgba(var(--bs-danger-rgb), 0.08);
        border-color: rgba(var(--bs-danger-rgb), 0.25);
        color: var(--bs-danger);
      }

      .lib-lock {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 28px;
        height: 28px;
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        opacity: 0.55;
      }

      /* ── Card border distinction (Registered vs Library) ── */
      .step-card-registered { border-left: 4px solid var(--bs-success); }
      .step-card-library    { border-left: 4px solid var(--bs-primary); }

      /* ── Archived sub-section ── */
      .archived-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        background: rgba(var(--bs-dark-rgb), 0.03);
        border-top: 1px solid var(--border-color);
        border-bottom: 1px solid var(--border-color);
        color: var(--brand-text-muted);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        cursor: pointer;
        transition: background-color 0.1s ease;

        &:hover { background: rgba(var(--bs-dark-rgb), 0.05); }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .archived-hint {
        margin-left: auto;
        text-transform: none;
        letter-spacing: 0;
        font-weight: var(--font-weight-normal);
        font-size: 11px;
        opacity: 0.75;
      }

      .archived-list {
        background: rgba(var(--bs-dark-rgb), 0.015);
      }

      .lib-row-archived {
        opacity: 0.75;
        .lib-name { color: var(--brand-text-muted); font-style: italic; }
        .lib-icon { color: var(--brand-text-muted); }
      }

      @media (prefers-reduced-motion: reduce) {
        .lib-icon-btn { transition: none; }
      }

      /* ── Register button (library rows) ── */
      .btn-register {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 96px;
        padding: var(--space-1) var(--space-3);
        border: 1.5px solid rgba(var(--bs-primary-rgb), 0.3);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-primary-rgb), 0.06);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        flex-shrink: 0;
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

      .section-library {
        color: var(--bs-primary);
        background: rgba(var(--bs-primary-rgb), 0.06);
        border-bottom: 1px solid rgba(var(--bs-primary-rgb), 0.12);
      }

      /* Action button nested inside a .section-header banner — compact, matches banner height */
      .section-action {
        margin-left: auto;
        padding: 2px var(--space-2);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: none;
        letter-spacing: 0;
      }

      /* Library tip directly under the banner header */
      .library-tip {
        margin: var(--space-2) var(--space-3);
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

    /** Capability flags from job pulse — gate the Register/Remove UI controls. */
    readonly canRegisterTeam = this.state.canRegisterTeam;
    readonly canRemoveTeam = this.state.canRemoveTeam;

    readonly loading = signal(true);
    readonly error = signal<string | null>(null);
    readonly clubName = signal('your club');
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly lopOptions = signal<string[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);
    /** When set, the edit modal is open for this team. */
    readonly editingTeam = signal<ClubTeamDto | null>(null);
    /** When set, the delete-confirm dialog is open for this team. */
    readonly pendingDelete = signal<ClubTeamDto | null>(null);
    /** When set, the archive-confirm dialog is open for this team. */
    readonly pendingArchive = signal<ClubTeamDto | null>(null);
    /** When set, the restore-confirm dialog is open for this team. */
    readonly pendingRestore = signal<ClubTeamDto | null>(null);
    /** Collapsible archived sub-section. Default collapsed. */
    readonly showArchived = signal(false);

    /** Conditional column visibility based on job config. */
    readonly showProcessingColumn = signal(false);

    // paymentMethodsAllowedCode: 1=CC only, 2=CC or Check, 3=Check only
    readonly allowsCc = computed(() => this.state.teamPayment.paymentMethodsAllowedCode() !== 3);
    readonly allowsCheck = computed(() => this.state.teamPayment.paymentMethodsAllowedCode() >= 2);

    /** Which team's age picker modal is open (null = closed). */
    readonly agePickerTeam = signal<AgePickerTeam | null>(null);

    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    /** All library teams: available + entered. */
    readonly allLibraryTeams = computed<ClubTeamDto[]>(() => {
        const available = this._clubTeams();
        const entered = this._registeredTeams();

        const enteredAsLibrary: ClubTeamDto[] = entered
            .filter(r => r.clubTeamId)
            .map(r => ({
                clubTeamId: r.clubTeamId!,
                clubTeamName: r.teamName,
                clubTeamGradYear: r.ageGroupName,
                clubTeamLevelOfPlay: r.levelOfPlay ?? '',
                bHasBeenScheduled: r.bHasBeenScheduled,
                // Registered-for-this-event rows are by definition not archived on display.
                bArchived: false,
            }));

        // Merge: available first, then entered (deduplicated)
        const availableIds = new Set(available.map(t => t.clubTeamId));
        const enteredOnly = enteredAsLibrary.filter(t => !availableIds.has(t.clubTeamId));

        return [...available, ...enteredOnly];
    });

    /** Active (non-archived) library teams, sorted alphabetically. */
    readonly activeLibraryTeams = computed(() =>
        this.allLibraryTeams()
            .filter(t => !t.bArchived)
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName)),
    );

    /** Archived library teams, sorted alphabetically. */
    readonly archivedLibraryTeams = computed(() =>
        this.allLibraryTeams()
            .filter(t => t.bArchived)
            .sort((a, b) => a.clubTeamName.localeCompare(b.clubTeamName)),
    );

    /** Entered teams (registered for this event). */
    readonly enteredTeams = computed(() => this._registeredTeams());

    readonly totalFee = computed(() => this._registeredTeams().reduce((s, t) => s + t.feeBase, 0));
    readonly totalOwed = computed(() => this._registeredTeams().reduce((s, t) => s + t.owedTotal, 0));

    /** Count of teams not yet paid for — drives the Proceed-to-Payment label. */
    readonly newTeamsCount = computed(() => this._registeredTeams().filter(t => t.paidTotal === 0).length);

    readonly proceedButtonLabel = computed(() => {
        const n = this.newTeamsCount();
        if (n === 0) return 'Proceed to Payment';
        const noun = n === 1 ? 'team' : 'teams';
        return `Submit the ${n} new ${noun} and Proceed to Payment`;
    });

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

        // Build a ClubTeamDto-compatible object with the modal-selected LOP.
        // bHasBeenScheduled / bArchived are irrelevant here (registration path, not edit/delete).
        const team: ClubTeamDto = {
            clubTeamId: pickerTeam.clubTeamId,
            clubTeamName: pickerTeam.clubTeamName,
            clubTeamGradYear: pickerTeam.gradYear,
            clubTeamLevelOfPlay: selection.levelOfPlay || pickerTeam.levelOfPlay,
            bHasBeenScheduled: false,
            bArchived: false,
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
                    this.toast.show(`${team.clubTeamName} archived.`, 'success', 3000);
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
                    this.toast.show(`${team.clubTeamName} restored.`, 'success', 3000);
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
                    this.showProcessingColumn.set(meta.bAddProcessingFees ?? false);
                    this.state.applyTeamsMetadata(meta);
                },
                error: () => {
                    this.loading.set(false);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }
}
