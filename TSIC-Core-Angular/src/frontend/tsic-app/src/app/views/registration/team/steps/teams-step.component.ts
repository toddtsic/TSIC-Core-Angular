import { ChangeDetectionStrategy, Component, OnInit, inject, output, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CurrencyPipe } from '@angular/common';
import { TeamWizardStateService } from '../state/team-wizard-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { ToastService } from '@shared-ui/toast.service';
import { TeamFormModalComponent } from './team-form-modal.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
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
    imports: [CurrencyPipe, TeamFormModalComponent, ConfirmDialogComponent],
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
          <h4 class="welcome-title"><i class="bi bi-trophy-fill welcome-icon"></i> Welcome to <span class="club-accent">{{ clubName() }}</span>'s Team Library!</h4>
          <p class="hero-action">Add any missing teams, then hit Select Teams.</p>
          <p class="welcome-desc">
            <i class="bi bi-arrow-repeat me-1"></i>Add your teams once
            <span class="desc-dot"></span>
            <i class="bi bi-calendar-event me-1"></i>Register in any event
            <span class="desc-dot"></span>
            <i class="bi bi-x-circle me-1"></i>No re-entering info, ever
          </p>
        </div>

        <!-- Coach card — contextual guidance, separate from data -->
        <div class="coach-card coach-primary">
          <p class="coach-intro">Make sure your club's team library is up to date before you register. Every team you add here is saved permanently — set it up once and reuse it for any event.</p>
          <ul class="coach-list">
            <li>
              <i class="bi bi-check2-circle text-success"></i>
              <span>Confirm <strong>every team</strong> you plan to register is listed below</span>
            </li>
            <li>
              <i class="bi bi-plus-circle text-primary"></i>
              <span>Missing a team? <button type="button" class="wizard-callout-link" (click)="showAddModal.set(true)">Add it now</button> — takes 10 seconds</span>
            </li>
            <li>
              <i class="bi bi-arrow-repeat text-info"></i>
              <span>Your library is <strong>permanent</strong> — add once, register for any event</span>
            </li>
          </ul>
        </div>

        <div class="step-card">

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
              @for (group of libraryByYear(); track group.year) {
                <div class="year-group-header">
                  <span class="year-label">{{ group.year }}</span>
                  <span class="year-count">{{ group.teams.length }} {{ group.teams.length === 1 ? 'team' : 'teams' }}</span>
                </div>
                @for (team of group.teams; track team.clubTeamId) {
                  <div class="lib-row">
                    <i class="bi bi-people-fill lib-icon"></i>
                    <span class="lib-name">{{ team.clubTeamName }}</span>
                    <span class="lib-level">{{ team.clubTeamLevelOfPlay ? 'LOP ' + team.clubTeamLevelOfPlay : '' }}</span>
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

        <!-- Centered hero -->
        <div class="welcome-hero">
          <h4 class="welcome-title"><i class="bi bi-check2-square welcome-icon" style="color: var(--bs-info)"></i> Select Your Teams</h4>
          <p class="hero-action">Tap a team, pick an age group — done.</p>
          <p class="welcome-desc">
            <i class="bi bi-hand-index me-1"></i>Tap a team
            <span class="desc-dot"></span>
            <i class="bi bi-diagram-3 me-1"></i>Pick its age group
            <span class="desc-dot"></span>
            <i class="bi bi-lightning me-1"></i>Instant confirmation
          </p>
        </div>

        <!-- Coach card — live progress -->
        <div class="coach-card coach-info">
          @if (enteredTeams().length === 0) {
            <p class="coach-intro"><i class="bi bi-hand-index-thumb"></i> Tap any team below to expand it and choose an age group. Registration is instant.</p>
          } @else {
            <p class="coach-intro"><i class="bi bi-check-circle-fill text-success"></i> <strong class="text-success">{{ enteredTeams().length }}</strong> {{ enteredTeams().length === 1 ? 'team' : 'teams' }} registered <span class="desc-dot"></span> {{ unregisteredTeams().length }} remaining</p>
          }
        </div>

        <!-- ── Registered card (always visible, never scrolls away) ── -->
        @if (enteredTeams().length > 0) {
          <div class="step-card registered-card">
            <div class="section-header section-registered">
              <i class="bi bi-check-circle-fill me-1"></i>
              Registered ({{ enteredTeams().length }})
              <span class="registered-total">{{ totalFee() | currency }}</span>
            </div>
            @for (team of enteredTeams(); track team.teamId) {
              @let paid = team.paidTotal > 0;
              @let isExpanded = expandedTeamId() === team.clubTeamId;
              <div class="select-row-wrapper" [class.is-expanded]="isExpanded">
                <div class="select-row is-checked" role="button" tabindex="0"
                     [class.is-paid]="paid"
                     [attr.aria-expanded]="isExpanded"
                     (click)="team.clubTeamId ? onRowClick(team.clubTeamId, paid, $event) : null"
                     (keydown.enter)="team.clubTeamId ? onRowClick(team.clubTeamId, paid, $event) : null"
                     (keydown.space)="team.clubTeamId ? onRowClick(team.clubTeamId, paid, $event) : null; $event.preventDefault()">
                  <span class="select-indicator checked" [class.locked]="paid">
                    @if (paid) { <i class="bi bi-lock-fill"></i> }
                    @else { <i class="bi bi-check-lg"></i> }
                  </span>
                  <span class="select-name">{{ team.teamName }}</span>
                  @if (!isExpanded) {
                    <span class="select-age">{{ team.ageGroupName }}</span>
                    <span class="select-fee">{{ team.feeTotal | currency }}</span>
                  }
                  @if (paid) {
                    <span class="paid-badge"><i class="bi bi-lock-fill me-1"></i>Paid</span>
                  }
                  @if (!paid) {
                    <i class="bi select-chevron" [class.bi-chevron-down]="!isExpanded" [class.bi-chevron-up]="isExpanded"></i>
                  }
                  @if (!paid && !isExpanded) {
                    <button type="button" class="btn-inline-remove"
                            [disabled]="actionInProgress()"
                            (click)="onRemoveTeam(team); $event.stopPropagation()"
                            title="Remove {{ team.teamName }}">
                      <i class="bi bi-x-lg"></i>
                    </button>
                  }
                </div>
                @if (isExpanded && !paid && team.clubTeamId) {
                  <div class="age-picker-panel">
                    <div class="age-picker-label">Change age group for <strong>{{ team.teamName }}</strong></div>
                    <div class="age-pill-row" role="radiogroup" [attr.aria-label]="'Age groups for ' + team.teamName">
                      @for (ag of agePills(); track ag.ageGroupId) {
                        <button type="button" class="age-pill" role="radio"
                                [class.is-recommended]="ag.ageGroupId === bestMatchForTeam(team.ageGroupName)"
                                [class.is-selected]="team.ageGroupId === ag.ageGroupId"
                                [class.is-full]="ag.isFull"
                                [class.is-almost-full]="ag.isAlmostFull && !ag.isFull"
                                [attr.aria-checked]="team.ageGroupId === ag.ageGroupId"
                                [attr.aria-label]="ag.ageGroupName + ' — ' + (ag.fee | currency) + ' — ' + (ag.isFull ? 'Waitlist' : ag.spotsLeft + ' spots left')"
                                [disabled]="actionInProgress()"
                                (click)="onSelectAgeGroup({clubTeamId: team.clubTeamId!, clubTeamName: team.teamName, clubTeamGradYear: team.ageGroupName, clubTeamLevelOfPlay: team.levelOfPlay ?? ''}, ag.ageGroupId); $event.stopPropagation()">
                          <span class="pill-name">
                            {{ ag.ageGroupName }}
                            @if (ag.ageGroupId === bestMatchForTeam(team.ageGroupName)) { <i class="bi bi-star-fill pill-star"></i> }
                            @if (team.ageGroupId === ag.ageGroupId) { <i class="bi bi-check-circle-fill pill-current"></i> }
                          </span>
                          <span class="pill-fee">{{ ag.fee | currency }}</span>
                          <span class="pill-spots" [class.text-warning]="ag.isAlmostFull && !ag.isFull" [class.text-danger]="ag.isFull">
                            @if (ag.isFull) { <i class="bi bi-exclamation-circle me-1"></i>Waitlist }
                            @else { {{ ag.spotsLeft }} {{ ag.spotsLeft === 1 ? 'spot' : 'spots' }} }
                          </span>
                        </button>
                      }
                    </div>
                  </div>
                }
              </div>
            }
          </div>
        }

        <!-- ── Available card (scrollable workhorse) ── -->
        <div class="step-card">
          <div class="section-header section-available">
            <i class="bi bi-list-ul me-1"></i>
            Available ({{ unregisteredTeams().length }})
          </div>

          @if (unregisteredTeams().length === 0) {
            <div class="wizard-empty-state" style="padding: var(--space-6) var(--space-4)">
              <i class="bi bi-check-circle-dotted"></i>
              <strong>All teams registered!</strong>
              <span>Every team in your library has been assigned an age group.</span>
            </div>
          } @else {
            <div class="scroll-list">
              @for (group of unregisteredByYear(); track group.year) {
                <div class="year-group-header">
                  <span class="year-label">{{ group.year }}</span>
                  <span class="year-count">{{ group.teams.length }} {{ group.teams.length === 1 ? 'team' : 'teams' }}</span>
                </div>
                @for (team of group.teams; track team.clubTeamId) {
                  @let isExpanded = expandedTeamId() === team.clubTeamId;
                  <div class="select-row-wrapper" [class.is-expanded]="isExpanded">
                    <div class="select-row" role="button" tabindex="0"
                         [attr.aria-expanded]="isExpanded"
                         (click)="onRowClick(team.clubTeamId, false, $event)"
                         (keydown.enter)="onRowClick(team.clubTeamId, false, $event)"
                         (keydown.space)="onRowClick(team.clubTeamId, false, $event); $event.preventDefault()">
                      <span class="select-name">{{ team.clubTeamName }}</span>
                      <span class="select-meta">{{ team.clubTeamLevelOfPlay ? 'LOP ' + team.clubTeamLevelOfPlay : '' }}</span>
                      <i class="bi select-chevron" [class.bi-chevron-down]="!isExpanded" [class.bi-chevron-up]="isExpanded"></i>
                    </div>
                    @if (isExpanded) {
                      <div class="age-picker-panel">
                        <div class="age-picker-tip">
                          <i class="bi bi-hand-index-thumb"></i>
                          Tap an age group to register <strong>{{ team.clubTeamName }}</strong> instantly
                          <span class="tip-legend"><i class="bi bi-star-fill"></i> = best match for grad year</span>
                        </div>
                        <div class="age-pill-row" role="radiogroup" [attr.aria-label]="'Age groups for ' + team.clubTeamName">
                          @for (ag of agePills(); track ag.ageGroupId) {
                            <button type="button" class="age-pill" role="radio"
                                    [class.is-recommended]="ag.ageGroupId === bestMatchForTeam(team.clubTeamGradYear)"
                                    [class.is-full]="ag.isFull"
                                    [class.is-almost-full]="ag.isAlmostFull && !ag.isFull"
                                    [attr.aria-checked]="false"
                                    [attr.aria-label]="ag.ageGroupName + ' — ' + (ag.fee | currency) + ' — ' + (ag.isFull ? 'Waitlist' : ag.spotsLeft + ' spots left')"
                                    [disabled]="actionInProgress()"
                                    (click)="onSelectAgeGroup(team, ag.ageGroupId); $event.stopPropagation()">
                              <span class="pill-name">
                                {{ ag.ageGroupName }}
                                @if (ag.ageGroupId === bestMatchForTeam(team.clubTeamGradYear)) { <i class="bi bi-star-fill pill-star"></i> }
                              </span>
                              <span class="pill-fee">{{ ag.fee | currency }}</span>
                              <span class="pill-spots" [class.text-warning]="ag.isAlmostFull && !ag.isFull" [class.text-danger]="ag.isFull">
                                @if (ag.isFull) { <i class="bi bi-exclamation-circle me-1"></i>Waitlist }
                                @else { {{ ag.spotsLeft }} {{ ag.spotsLeft === 1 ? 'spot' : 'spots' }} }
                              </span>
                            </button>
                          }
                        </div>
                      </div>
                    }
                  </div>
                }
              }
            </div>
          }

          <div class="step-card-footer">
            <button type="button" class="btn btn-sm btn-outline-secondary"
                    (click)="goToMiniStep('library')">
              <i class="bi bi-arrow-left me-1"></i>Back to Library
            </button>
            <button type="button" class="btn btn-sm btn-primary"
                    [disabled]="enteredTeams().length === 0"
                    (click)="goToMiniStep('summary')">
              Review {{ enteredTeams().length }} {{ enteredTeams().length === 1 ? 'Team' : 'Teams' }} <i class="bi bi-arrow-right ms-1"></i>
            </button>
          </div>
        </div>
      }

      <!-- ═══ STEP 3: REVIEW ═══ -->
      @if (currentMiniStep() === 'summary') {

        <!-- Centered hero -->
        <div class="welcome-hero">
          <h4 class="welcome-title"><i class="bi bi-clipboard-check welcome-icon" style="color: var(--bs-success)"></i> You're All Set!</h4>
          <p class="hero-action">Double-check your teams, then proceed to payment.</p>
          <p class="welcome-desc">
            <strong class="text-primary">{{ enteredTeams().length }} {{ enteredTeams().length === 1 ? 'team' : 'teams' }}</strong> registered
            @if (totalOwed() > 0) {
              <span class="desc-dot"></span>
              <strong class="text-danger">{{ totalOwed() | currency }}</strong> due
            } @else {
              <span class="desc-dot"></span>
              <span class="text-success"><i class="bi bi-check-circle-fill me-1"></i>Fully paid</span>
            }
          </p>
        </div>

        <!-- Coach card — review guidance -->
        <div class="coach-card coach-success">
          <p class="coach-intro"><i class="bi bi-info-circle"></i> Review your teams below. When you're ready, hit <strong>Proceed to Payment</strong> in the top bar.</p>
        </div>

        <div class="step-card">

          <div class="summary-table-wrap">
            <table class="summary-table">
              <thead>
                <tr>
                  <th><i class="bi bi-people-fill me-1"></i>Team</th>
                  <th><i class="bi bi-diagram-3 me-1"></i>Age Group</th>
                  <th class="text-end">Fee</th>
                  <th class="text-end">Paid</th>
                  <th class="text-end">Owed</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                @for (team of enteredTeams(); track team.teamId) {
                  <tr [class.is-paid]="team.paidTotal > 0">
                    <td>
                      <span class="fw-semibold">{{ team.teamName }}</span>
                    </td>
                    <td>
                      <span class="summary-age">{{ team.ageGroupName }}</span>
                    </td>
                    <td class="text-end">{{ team.feeTotal | currency }}</td>
                    <td class="text-end">
                      @if (team.paidTotal > 0) {
                        <span class="text-success"><i class="bi bi-check-circle me-1"></i>{{ team.paidTotal | currency }}</span>
                      } @else {
                        <span class="text-muted">{{ team.paidTotal | currency }}</span>
                      }
                    </td>
                    <td class="text-end fw-semibold" [class.text-danger]="team.owedTotal > 0">
                      {{ team.owedTotal | currency }}
                    </td>
                    <td class="text-end">
                      @if (team.paidTotal === 0) {
                        <button type="button"
                                class="btn btn-sm btn-outline-danger border-0 px-2 py-0"
                                [disabled]="actionInProgress()"
                                (click)="onRemoveTeam(team)"
                                title="Remove {{ team.teamName }}">
                          <i class="bi bi-x-lg"></i>
                        </button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr class="totals-row">
                  <td colspan="2">
                    <strong>{{ enteredTeams().length }} {{ enteredTeams().length === 1 ? 'team' : 'teams' }}</strong>
                  </td>
                  <td class="text-end fw-bold">{{ totalFee() | currency }}</td>
                  <td class="text-end fw-semibold">{{ totalPaid() | currency }}</td>
                  <td class="text-end">
                    @if (totalOwed() > 0) {
                      <span class="total-owed"><i class="bi bi-exclamation-circle me-1"></i>{{ totalOwed() | currency }}</span>
                    } @else {
                      <span class="total-paid"><i class="bi bi-check-circle-fill me-1"></i>$0.00</span>
                    }
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
            <span class="footer-hint">
              <i class="bi bi-shield-check me-1"></i>You can always come back to add more teams
            </span>
          </div>
        </div>
      }
    }

    <!-- ═══ MODALS ═══ -->
    @if (showAddModal()) {
      <app-team-form-modal
        [clubName]="clubName()"
        (saved)="onTeamAdded()"
        (closed)="showAddModal.set(false)" />
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

        &.is-expanded {
          border-left: 3px solid var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.02);
          margin: var(--space-1) 0;
          border-radius: var(--radius-sm);
          box-shadow: var(--shadow-xs);
        }
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

      .select-meta {
        color: var(--brand-text-muted);
        white-space: nowrap;
        margin-left: auto;
      }

      .select-age {
        font-size: 10px;
        font-weight: var(--font-weight-semibold);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
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

      .select-chevron {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        flex-shrink: 0;
        transition: transform 0.15s ease;
      }

      .select-row-wrapper.is-expanded .select-chevron {
        color: var(--bs-primary);
      }

      .btn-inline-remove {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border: none;
        background: transparent;
        color: var(--brand-text-muted);
        border-radius: var(--radius-sm);
        cursor: pointer;
        font-size: var(--font-size-xs);
        flex-shrink: 0;
        transition: color 0.1s ease, background-color 0.1s ease;

        &:hover { color: var(--bs-danger); background: rgba(var(--bs-danger-rgb), 0.08); }
        &:disabled { opacity: 0.4; cursor: default; }
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

      /* ── Step 2: Inline Age Picker Panel ─────────── */
      .age-picker-panel {
        padding: var(--space-2) var(--space-3) var(--space-3);
        border-top: 1px solid rgba(var(--bs-primary-rgb), 0.08);
        animation: panelSlideIn 0.15s ease-out;
      }

      .age-picker-tip {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        margin-bottom: var(--space-2);
        line-height: var(--line-height-normal);

        > i { color: var(--bs-primary); }
      }

      .tip-legend {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        margin-left: var(--space-2);
        font-size: 10px;
        color: var(--neutral-400);

        i {
          font-size: 8px;
          color: var(--bs-primary);
        }
      }

      .age-pill-row {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-2);
      }

      .age-pill {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 2px;
        padding: var(--space-1) var(--space-3);
        border-radius: var(--radius-md);
        border: 1.5px solid var(--border-color);
        background: var(--brand-surface);
        cursor: pointer;
        min-width: 80px;
        min-height: 44px;
        font-size: var(--font-size-xs);
        transition: all 0.12s ease;

        &:hover:not(:disabled) {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.04);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:active:not(:disabled) { transform: scale(0.96); }

        &.is-recommended {
          /* Subtle nudge only — star icon does the work, not the pill chrome */
        }

        &.is-selected {
          border-color: var(--bs-success);
          background: rgba(var(--bs-success-rgb), 0.08);
        }

        &.is-full {
          opacity: 0.5;
        }

        &.is-almost-full .pill-spots {
          font-weight: var(--font-weight-semibold);
        }

        &:disabled { cursor: default; }
      }

      .pill-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        display: flex;
        align-items: center;
        gap: var(--space-1);
      }

      .pill-star {
        font-size: 8px;
        color: var(--bs-primary);
      }

      .pill-current {
        font-size: 10px;
        color: var(--bs-success);
      }

      .pill-fee {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
      }

      .pill-spots {
        font-size: 10px;
        color: var(--brand-text-muted);
        white-space: nowrap;
      }

      @keyframes panelSlideIn {
        from { opacity: 0; transform: translateY(-6px); }
        to   { opacity: 1; transform: translateY(0); }
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

      /* ── Step 3: Totals Row ──────────────────────── */
      .totals-row td {
        border-top: 2px solid var(--border-color);
        border-bottom: none;
        padding-top: var(--space-2);
      }

      .total-owed {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--bs-danger);
      }

      .total-paid {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-bold);
        color: var(--bs-success);
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

        .age-pill-row {
          flex-direction: column;
        }

        .age-pill {
          flex-direction: row;
          justify-content: space-between;
          width: 100%;
          min-height: 48px;
          padding: var(--space-2) var(--space-3);
        }

        .select-fee { display: none; }

        .btn-inline-remove {
          min-width: 44px;
          min-height: 44px;
        }

        .summary-table th,
        .summary-table td {
          padding: var(--space-1) var(--space-2);
        }
      }

      @media (prefers-reduced-motion: reduce) {
        .select-row, .mini-step-item, .select-indicator, .select-chevron, .age-pill, .btn-inline-remove { transition: none; }
        .age-picker-panel { animation: none; }
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
    readonly clubName = signal('your club');
    readonly ageGroups = signal<AgeGroupDto[]>([]);
    readonly actionInProgress = signal(false);
    readonly showAddModal = signal(false);

    /** Which mini-step is active. */
    readonly currentMiniStep = signal<MiniStep>('library');

    /** Which team row is expanded in Step 2 (null = all collapsed). */
    readonly expandedTeamId = signal<number | null>(null);

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
    /** How many library teams are not yet entered. */
    readonly availableCount = computed(() => this._clubTeams().length);

    readonly totalFee = computed(() => this._registeredTeams().reduce((s, t) => s + t.feeTotal, 0));
    readonly totalPaid = computed(() => this._registeredTeams().reduce((s, t) => s + t.paidTotal, 0));
    readonly totalOwed = computed(() => this._registeredTeams().reduce((s, t) => s + t.owedTotal, 0));

    /** Unregistered library teams for Step 2 "Available" section. */
    readonly unregisteredTeams = computed(() => {
        const enteredIds = new Set(this._registeredTeams().filter(r => r.clubTeamId).map(r => r.clubTeamId!));
        return this._clubTeams().filter(t => !enteredIds.has(t.clubTeamId));
    });

    /** Unregistered teams grouped by grad year for Step 2 display. */
    readonly unregisteredByYear = computed(() => {
        const teams = this.unregisteredTeams();
        const map = new Map<string, ClubTeamDto[]>();
        for (const t of teams) {
            const year = t.clubTeamGradYear || 'Other';
            if (!map.has(year)) map.set(year, []);
            map.get(year)!.push(t);
        }
        return Array.from(map, ([year, yearTeams]) => ({ year, teams: yearTeams }));
    });

    /** Age group pills with computed display data. */
    readonly agePills = computed(() => {
        return this.ageGroups().map(ag => {
            const spotsLeft = Math.max(0, ag.maxTeams - ag.registeredCount);
            return {
                ageGroupId: ag.ageGroupId,
                ageGroupName: ag.ageGroupName,
                fee: (ag.deposit || 0) + (ag.balanceDue || 0),
                spotsLeft,
                isFull: spotsLeft === 0,
                isAlmostFull: spotsLeft > 0 && spotsLeft <= 2,
            };
        });
    });

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

    /** Step 2: best-match age group for a given grad year. */
    bestMatchForTeam(gradYear: string): string {
        if (!gradYear || !this.ageGroups().length) return '';
        const groups = this.ageGroups();
        const exact = groups.find(ag => ag.ageGroupName === gradYear);
        if (exact) return exact.ageGroupId;
        const contains = groups.find(ag => ag.ageGroupName.includes(gradYear));
        if (contains) return contains.ageGroupId;
        return '';
    }

    /** Step 2: toggle row expansion and scroll into view. */
    onRowClick(clubTeamId: number, paid: boolean, event?: Event): void {
        if (paid) return;
        const expanding = this.expandedTeamId() !== clubTeamId;
        this.expandedTeamId.set(expanding ? clubTeamId : null);

        if (expanding && event) {
            const wrapper = (event.target as HTMLElement).closest('.select-row-wrapper');
            if (wrapper) {
                setTimeout(() => wrapper.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
            }
        }
    }

    /** Step 2: register (or re-register) a team with the selected age group. */
    onSelectAgeGroup(team: ClubTeamDto, ageGroupId: string): void {
        const existing = this.getEnteredInfo(team.clubTeamId);

        // If already registered with same age group, just collapse
        if (existing && existing.ageGroupId === ageGroupId) {
            this.expandedTeamId.set(null);
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
                        const msg = resp.isWaitlisted
                            ? `${team.clubTeamName} waitlisted for ${resp.waitlistAgegroupName ?? ''}`
                            : `${team.clubTeamName} entered!`;
                        this.toast.show(msg, resp.isWaitlisted ? 'warning' : 'success', 3000);
                        this.expandedTeamId.set(null);
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

    private loadTeamsMetadata(): void {
        this.loading.set(true);
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
