import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import { AgeGroupPickerModalComponent } from './age-group-picker-modal.component';
import type { TeamsMetadataResponse, AgeGroupDto, RegisteredTeamDto, ClubTeamDto } from '@core/api';

type MiniStep = 'library' | 'select' | 'summary';

/**
 * Teams step — mini-wizard with three micro-steps:
 *   1. Update Club Team Library  (add/remove from permanent library)
 *   2. Select Teams to Register  (checkbox, age group modal on check)
 *   3. Teams to Register         (summary table with fees)
 */
@Component({
    selector: 'app-trw-teams-step',
    standalone: true,
    imports: [CurrencyPipe, TeamFormModalComponent, AgeGroupPickerModalComponent],
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

      <!-- ═══ MINI-STEP INDICATOR ═══ -->
      <div class="mini-steps">
        @for (s of miniStepDefs; track s.id; let i = $index) {
          <button type="button" class="mini-step-item"
                  [class.active]="currentMiniStep() === s.id"
                  [class.completed]="miniStepIndex() > i"
                  [disabled]="i > miniStepIndex()"
                  (click)="goToMiniStep(s.id)">
            <span class="mini-step-num">{{ i + 1 }}</span>
            <span class="mini-step-label">{{ s.label }}</span>
          </button>
          @if (i < miniStepDefs.length - 1) {
            <span class="mini-step-line" [class.completed]="miniStepIndex() > i"></span>
          }
        }
      </div>

      <!-- ═══ STEP 1: UPDATE CLUB TEAM LIBRARY ═══ -->
      @if (currentMiniStep() === 'library') {

        <!-- Welcome hero — centered, no card, commanding -->
        <div class="welcome-hero">
          <h4 class="welcome-title"><i class="bi bi-trophy-fill welcome-icon"></i> Welcome to Your TSIC Team Library!</h4>
          <p class="welcome-desc">
            <i class="bi bi-arrow-repeat me-1"></i>Add your teams once
            <span class="desc-dot"></span>
            <i class="bi bi-calendar-event me-1"></i>Register in any event
            <span class="desc-dot"></span>
            <i class="bi bi-x-circle me-1"></i>No re-entering info, ever
          </p>
        </div>

        <div class="step-card">

          <!-- Action callout -->
          <div class="callout">
            <div class="callout-icon"><i class="bi bi-exclamation-triangle-fill"></i></div>
            <div class="callout-body">
              <strong>Before you continue:</strong> make sure every team you plan to register is listed below.<br>
              Missing a team? <button type="button" class="callout-link" (click)="showAddModal.set(true)"><i class="bi bi-plus-circle me-1"></i>Add it now</button> — takes 10 seconds.
            </div>
          </div>

          <!-- Library header with stats -->
          <div class="lib-header">
            <div class="lib-stats">
              <span class="stat-number">{{ allLibraryTeams().length }}</span>
              <span class="stat-label">{{ allLibraryTeams().length === 1 ? 'team' : 'teams' }} in library</span>
              @if (enteredTeams().length > 0) {
                <span class="stat-divider"></span>
                <span class="stat-registered"><i class="bi bi-check-circle-fill me-1"></i>{{ enteredTeams().length }} already registered</span>
              }
            </div>
            <button type="button" class="btn btn-primary btn-sm"
                    (click)="showAddModal.set(true)">
              <i class="bi bi-plus-circle me-1"></i>Add Team
            </button>
          </div>

          @if (allLibraryTeams().length === 0) {
            <div class="empty-state">
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
              @for (group of libraryByYear(); track group.year) {
                <div class="year-group-header">
                  <span class="year-label">{{ group.year }}</span>
                  <span class="year-count">{{ group.teams.length }} {{ group.teams.length === 1 ? 'team' : 'teams' }}</span>
                </div>
                @for (team of group.teams; track team.clubTeamId) {
                  <div class="lib-row">
                    <i class="bi bi-people-fill lib-icon"></i>
                    <span class="lib-name">{{ team.clubTeamName }}</span>
                    <span class="lib-level">{{ team.clubTeamLevelOfPlay ? 'Lvl ' + team.clubTeamLevelOfPlay : '' }}</span>
                    @if (isEnteredTeam(team.clubTeamId)) {
                      <span class="lib-badge"><i class="bi bi-check-circle-fill me-1"></i>Registered</span>
                    }
                  </div>
                }
              }
            </div>
          }

          <div class="step-card-footer">
            <span class="footer-hint">
              <i class="bi bi-shield-check me-1"></i>Your library is saved across all events
            </span>
            <button type="button" class="btn btn-sm btn-primary"
                    [disabled]="allLibraryTeams().length === 0"
                    (click)="goToMiniStep('select')">
              Looks Good — Select Teams <i class="bi bi-arrow-right ms-1"></i>
            </button>
          </div>
        </div>
      }

      <!-- ═══ STEP 2: SELECT TEAMS TO REGISTER ═══ -->
      @if (currentMiniStep() === 'select') {
        <div class="step-card">
          <div class="step-hero">
            <div class="hero-icon hero-icon-select"><i class="bi bi-check2-square"></i></div>
            <div class="hero-text">
              <h6 class="hero-title">Which teams are playing in this event?</h6>
              <p class="hero-desc">
                Check each team to register it.
                You'll pick an <strong class="text-primary">age group</strong> as you go.
                @if (enteredTeams().length > 0) {
                  <span class="hero-stat"><i class="bi bi-check-circle-fill text-success me-1"></i>{{ enteredTeams().length }} selected so far</span>
                }
              </p>
            </div>
          </div>

          <div class="scroll-list">
            @for (team of allLibraryTeams(); track team.clubTeamId) {
              @let entered = getEnteredInfo(team.clubTeamId);
              @let paid = entered !== null && entered.paidTotal > 0;
              <label class="select-row"
                     [class.is-checked]="entered !== null"
                     [class.is-paid]="paid">
                <input type="checkbox" class="select-check"
                       [checked]="entered !== null"
                       [disabled]="paid || actionInProgress()"
                       (change)="onToggleTeam(team, $event)" />
                <span class="select-name">{{ team.clubTeamName }}</span>
                <span class="select-meta">{{ team.clubTeamGradYear }}{{ team.clubTeamLevelOfPlay ? ' · Lvl ' + team.clubTeamLevelOfPlay : '' }}</span>
                @if (entered) {
                  <span class="select-age"><i class="bi bi-diagram-3 me-1"></i>{{ entered.ageGroupName }}</span>
                }
                @if (paid) {
                  <span class="paid-badge"><i class="bi bi-lock-fill me-1"></i>Paid</span>
                }
              </label>
            }
          </div>

          <div class="step-card-footer">
            <button type="button" class="btn btn-sm btn-outline-secondary"
                    (click)="goToMiniStep('library')">
              <i class="bi bi-arrow-left me-1"></i>Library
            </button>
            <button type="button" class="btn btn-sm btn-primary"
                    [disabled]="enteredTeams().length === 0"
                    (click)="goToMiniStep('summary')">
              Review {{ enteredTeams().length }} {{ enteredTeams().length === 1 ? 'Team' : 'Teams' }} <i class="bi bi-arrow-right ms-1"></i>
            </button>
          </div>
        </div>
      }

      <!-- ═══ STEP 3: TEAMS TO REGISTER (SUMMARY) ═══ -->
      @if (currentMiniStep() === 'summary') {
        <div class="step-card">
          <div class="step-hero step-hero-success">
            <div class="hero-icon hero-icon-review"><i class="bi bi-clipboard-check"></i></div>
            <div class="hero-text">
              <h6 class="hero-title">Ready for Payment</h6>
              <p class="hero-desc">
                <strong class="text-primary">{{ enteredTeams().length }} {{ enteredTeams().length === 1 ? 'team' : 'teams' }}</strong> registered.
                @if (totalOwed() > 0) {
                  Total due: <strong class="text-danger">{{ totalOwed() | currency }}</strong>.
                } @else {
                  All fees paid!
                }
                Review below, then proceed to payment.
              </p>
            </div>
          </div>

          <div class="summary-table-wrap">
            <table class="summary-table">
              <thead>
                <tr>
                  <th>Team</th>
                  <th>Age Group</th>
                  <th class="text-end">Fee</th>
                  <th class="text-end">Paid</th>
                  <th class="text-end">Owed</th>
                </tr>
              </thead>
              <tbody>
                @for (team of enteredTeams(); track team.teamId) {
                  <tr [class.is-paid]="team.paidTotal > 0">
                    <td>
                      <span class="fw-semibold">{{ team.teamName }}</span>
                    </td>
                    <td>
                      <span class="summary-age"><i class="bi bi-diagram-3 me-1"></i>{{ team.ageGroupName }}</span>
                    </td>
                    <td class="text-end">{{ team.feeTotal | currency }}</td>
                    <td class="text-end">
                      @if (team.paidTotal > 0) {
                        <span class="text-success">{{ team.paidTotal | currency }}</span>
                      } @else {
                        {{ team.paidTotal | currency }}
                      }
                    </td>
                    <td class="text-end fw-semibold" [class.text-danger]="team.owedTotal > 0">
                      {{ team.owedTotal | currency }}
                    </td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr>
                  <td colspan="2" class="fw-semibold">
                    Total
                  </td>
                  <td class="text-end fw-semibold">{{ totalFee() | currency }}</td>
                  <td class="text-end">{{ totalPaid() | currency }}</td>
                  <td class="text-end fw-bold" [class.text-danger]="totalOwed() > 0">
                    {{ totalOwed() | currency }}
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>

          <div class="step-card-footer">
            <button type="button" class="btn btn-sm btn-outline-secondary"
                    (click)="goToMiniStep('select')">
              <i class="bi bi-arrow-left me-1"></i>Change Selections
            </button>
            <span></span>
          </div>
        </div>
      }
    }

    <!-- ═══ MODALS ═══ -->
    @if (showAddModal()) {
      <app-team-form-modal
        [ageGroups]="ageGroups()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
    }

    @if (ageGroupPickerTeam()) {
      <app-age-group-picker-modal
        [teamName]="ageGroupPickerTeam()!.clubTeamName"
        [gradYear]="ageGroupPickerTeam()!.clubTeamGradYear"
        [ageGroups]="ageGroups()"
        (selected)="onAgeGroupSelected($event)"
        (closed)="onAgeGroupPickerCancelled()" />
    }
  `,
    styles: [`
      :host { display: flex; flex-direction: column; gap: var(--space-3); }

      /* ── Mini-Step Indicator ──────────────────────── */
      .mini-steps {
        display: flex;
        align-items: center;
        gap: 0;
        padding: var(--space-1) 0;
      }

      .mini-step-item {
        display: flex;
        align-items: center;
        gap: var(--space-1);
        padding: var(--space-1) var(--space-2);
        border: none;
        background: transparent;
        cursor: pointer;
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        white-space: nowrap;
        transition: color 0.15s ease;

        &:disabled { cursor: default; opacity: 0.5; }

        &.active {
          color: var(--bs-primary);
          font-weight: var(--font-weight-semibold);
          .mini-step-num {
            background: var(--bs-primary);
            color: var(--neutral-0);
          }
        }

        &.completed {
          color: var(--bs-success);
          .mini-step-num {
            background: var(--bs-success);
            color: var(--neutral-0);
          }
        }
      }

      .mini-step-num {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 20px;
        height: 20px;
        border-radius: var(--radius-full);
        border: 1.5px solid var(--border-color);
        font-size: 10px;
        font-weight: var(--font-weight-bold);
        flex-shrink: 0;
      }

      .mini-step-line {
        flex: 0 0 16px;
        height: 1px;
        background: var(--border-color);

        &.completed { background: var(--bs-success); }
      }

      /* ── Welcome Hero (centered, no card) ──────── */
      .welcome-hero {
        display: flex;
        flex-direction: column;
        align-items: center;
        text-align: center;
        padding: var(--space-4) var(--space-4) var(--space-3);
      }

      .welcome-icon {
        font-size: var(--font-size-2xl);
        color: var(--bs-primary);
      }

      .welcome-title {
        margin: 0;
        font-size: var(--font-size-2xl);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .welcome-desc {
        margin: var(--space-2) 0 0;
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        line-height: var(--line-height-relaxed);

        i { color: var(--bs-primary); }
      }

      .desc-dot {
        display: inline-block;
        width: 4px;
        height: 4px;
        border-radius: var(--radius-full);
        background: var(--neutral-300);
        vertical-align: middle;
        margin: 0 var(--space-2);
      }

      /* ── Action Callout (inside card) ────────────── */
      .callout {
        display: flex;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        background: rgba(var(--bs-danger-rgb), 0.06);
        border-bottom: 2px solid rgba(var(--bs-danger-rgb), 0.2);
        font-size: var(--font-size-xs);
        color: var(--brand-text);
        line-height: var(--line-height-normal);
      }

      .callout-icon {
        font-size: var(--font-size-lg);
        color: var(--bs-danger);
        flex-shrink: 0;
        margin-top: 1px;
      }

      .callout-body strong {
        color: var(--bs-danger);
      }

      .callout-link {
        background: none;
        border: none;
        padding: 0;
        color: var(--bs-primary);
        font-weight: var(--font-weight-bold);
        cursor: pointer;
        text-decoration: underline;
        text-underline-offset: 2px;
        font-size: inherit;

        &:hover { text-decoration: none; }
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
        padding: var(--space-1) var(--space-4);
        background: rgba(var(--bs-primary-rgb), 0.04);
        border-bottom: 1px solid rgba(var(--bs-primary-rgb), 0.08);
      }

      .year-label {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        color: var(--bs-primary);
        letter-spacing: 0.02em;
      }

      .year-count {
        font-size: 10px;
        color: var(--brand-text-muted);
      }

      .lib-level {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        white-space: nowrap;
        margin-left: auto;
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
        max-height: 320px;
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
      }

      .lib-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
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
      }

      /* ── Step 2: Select Rows ─────────────────────── */
      .select-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-1) var(--space-3);
        min-height: 34px;
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);
        font-size: var(--font-size-xs);
        cursor: pointer;
        transition: background-color 0.1s ease;

        &:last-child { border-bottom: none; }

        &:hover:not(.is-paid) {
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &.is-checked {
          background: rgba(var(--bs-primary-rgb), 0.05);
        }

        &.is-paid {
          opacity: 0.7;
          cursor: default;
        }
      }

      .select-check {
        appearance: none;
        width: 18px;
        height: 18px;
        min-width: 18px;
        border: 2px solid var(--neutral-300);
        border-radius: 4px;
        background: var(--brand-surface);
        cursor: pointer;
        position: relative;
        transition: border-color 0.1s ease, background-color 0.1s ease;
        flex-shrink: 0;

        &:checked {
          background: var(--bs-primary);
          border-color: var(--bs-primary);
          &::after {
            content: '';
            position: absolute;
            top: 2px; left: 4px;
            width: 5px; height: 9px;
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

      .select-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        white-space: nowrap;
        overflow: hidden;
        text-overflow: ellipsis;
        min-width: 0;
      }

      .select-meta {
        color: var(--brand-text-muted);
        white-space: nowrap;
      }

      .select-age {
        margin-left: auto;
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
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

      /* ── Step 3: Summary Table ───────────────────── */
      .summary-table-wrap {
        overflow-x: auto;
      }

      .summary-table {
        width: 100%;
        border-collapse: collapse;
        font-size: var(--font-size-xs);

        th {
          padding: var(--space-1) var(--space-3);
          font-weight: var(--font-weight-semibold);
          color: var(--brand-text-muted);
          text-transform: uppercase;
          letter-spacing: 0.03em;
          font-size: 10px;
          border-bottom: 2px solid var(--border-color);
          white-space: nowrap;
        }

        td {
          padding: var(--space-1) var(--space-3);
          color: var(--brand-text);
          border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);
          white-space: nowrap;
        }

        tbody tr.is-paid {
          opacity: 0.7;
        }

        tfoot td {
          border-top: 2px solid var(--border-color);
          border-bottom: none;
          padding-top: var(--space-2);
        }
      }

      .summary-age {
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 1px var(--space-1);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
      }

      /* ── Empty State ─────────────────────────────── */
      .empty-state {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-8) var(--space-4);
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        text-align: center;

        i {
          font-size: 40px;
          color: rgba(var(--bs-primary-rgb), 0.2);
        }

        strong { color: var(--brand-text); }
      }

      /* ── Mobile ──────────────────────────────────── */
      @media (max-width: 575.98px) {
        .mini-step-label { display: none; }

        .step-hero {
          flex-wrap: wrap;
          padding: var(--space-2) var(--space-3);
          gap: var(--space-2);
        }

        .hero-cta { width: 100%; }

        .welcome-hero {
          padding: var(--space-4) var(--space-3) var(--space-3);
        }

        .welcome-title {
          font-size: var(--font-size-xl);
        }

        .desc-dot { display: none; }
        .welcome-desc i { display: none; }
        .welcome-desc {
          font-size: var(--font-size-xs);
        }

        .callout {
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

        .summary-table th,
        .summary-table td {
          padding: var(--space-1) var(--space-2);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .select-row, .mini-step-item, .select-check { transition: none; }
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
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);

    /** Which mini-step is active. */
    readonly currentMiniStep = signal<MiniStep>('library');

    /** Team awaiting age group selection (opens the picker modal). */
    readonly ageGroupPickerTeam = signal<ClubTeamDto | null>(null);

    private readonly _registeredTeams = signal<RegisteredTeamDto[]>([]);
    private readonly _clubTeams = signal<ClubTeamDto[]>([]);

    readonly miniStepDefs: { id: MiniStep; label: string }[] = [
        { id: 'library', label: 'Team Library' },
        { id: 'select', label: 'Select Teams' },
        { id: 'summary', label: 'Review' },
    ];

    readonly miniStepIndex = computed(() =>
        this.miniStepDefs.findIndex(s => s.id === this.currentMiniStep()),
    );

    /** All library teams: available + entered (for Steps 1 & 2). */
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

    /** Library grouped by grad year for Step 1 display. */
    readonly libraryByYear = computed(() => {
        const teams = this.allLibraryTeams();
        const groups: { year: string; teams: ClubTeamDto[] }[] = [];
        const map = new Map<string, ClubTeamDto[]>();
        for (const t of teams) {
            const year = t.clubTeamGradYear || 'Other';
            if (!map.has(year)) map.set(year, []);
            map.get(year)!.push(t);
        }
        for (const [year, yearTeams] of map) {
            groups.push({ year, teams: yearTeams });
        }
        return groups;
    });

    /** Entered teams (for Step 2 checks and Step 3 table). */
    readonly enteredTeams = computed(() => this._registeredTeams());

    /** Summary totals for Step 3. */
    readonly totalFee = computed(() => this._registeredTeams().reduce((s, t) => s + t.feeTotal, 0));
    readonly totalPaid = computed(() => this._registeredTeams().reduce((s, t) => s + t.paidTotal, 0));
    readonly totalOwed = computed(() => this._registeredTeams().reduce((s, t) => s + t.owedTotal, 0));

    ngOnInit(): void {
        this.loadTeamsMetadata();
    }

    goToMiniStep(step: MiniStep): void {
        this.currentMiniStep.set(step);
    }

    isEnteredTeam(clubTeamId: number): boolean {
        return this._registeredTeams().some(r => r.clubTeamId === clubTeamId);
    }

    getEnteredInfo(clubTeamId: number): RegisteredTeamDto | null {
        return this._registeredTeams().find(r => r.clubTeamId === clubTeamId) ?? null;
    }

    /** Step 2: toggle a team's participation. Check → open age group picker. Uncheck → unregister. */
    onToggleTeam(team: ClubTeamDto, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        if (checked) {
            // Open age group picker modal
            this.ageGroupPickerTeam.set(team);
        } else {
            // Unregister
            const entered = this.getEnteredInfo(team.clubTeamId);
            if (!entered) return;
            this.unregisterTeam(entered.teamId, team.clubTeamName);
        }
    }

    /** Picker cancelled — reload to reset any optimistic checkbox state. */
    onAgeGroupPickerCancelled(): void {
        this.ageGroupPickerTeam.set(null);
        this.loadTeamsMetadata();
    }

    /** Age group selected in the picker modal → register the team. */
    onAgeGroupSelected(ageGroupId: string): void {
        const team = this.ageGroupPickerTeam();
        if (!team) return;

        this.ageGroupPickerTeam.set(null);
        this.actionInProgress.set(true);

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
    }

    onTeamAdded(): void {
        this.showAddModal.set(false);
        this.loadTeamsMetadata();
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

    private loadTeamsMetadata(): void {
        this.loading.set(true);
        this.error.set(null);

        this.teamReg.getTeamsMetadata()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (meta: TeamsMetadataResponse) => {
                    this.loading.set(false);
                    this._registeredTeams.set(meta.registeredTeams || []);
                    this._clubTeams.set(meta.clubTeams || []);
                    this.ageGroups.set(meta.ageGroups || []);
                    this.state.teamPayment.setTeams(meta.registeredTeams || []);
                    this.state.teamPayment.setJobPath(this.state.jobPath());
                    this.state.teamPayment.setPaymentConfig(
                        meta.paymentMethodsAllowedCode,
                        meta.bAddProcessingFees,
                        meta.bApplyProcessingFeesToTeamDeposit,
                    );
                    this.state.setHasActiveDiscountCodes(meta.hasActiveDiscountCodes);
                },
                error: () => {
                    this.loading.set(false);
                    this.error.set('Failed to load team registration data.');
                },
            });
    }
}
