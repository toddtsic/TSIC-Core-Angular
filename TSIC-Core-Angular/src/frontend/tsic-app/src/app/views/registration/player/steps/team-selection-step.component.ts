import { ChangeDetectionStrategy, Component, inject, computed, signal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CurrencyPipe } from '@angular/common';
import { DropDownListModule } from '@syncfusion/ej2-angular-dropdowns';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService, type AvailableTeam } from '@views/registration/player/services/team.service';
import { colorClassForIndex } from '@views/registration/shared/utils/color-class.util';
import { JobService } from '@infrastructure/services/job.service';

const JOB_TYPE_TOURNAMENT = 2;

/**
 * Team Selection step — per-player team assignment via dropdowns.
 * Supports single-select (by constraint) or multi-select (no constraint).
 * Shows capacity info and locked-player detection.
 */
@Component({
    selector: 'app-prw-team-selection-step',
    standalone: true,
    imports: [FormsModule, CurrencyPipe, DropDownListModule],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      @if (state.jobCtx.isCacMode()) {
        <h4 class="welcome-title"><i class="bi bi-calendar-event welcome-icon" style="color: var(--bs-info)"></i> Select Event(s)</h4>
        <p class="welcome-desc">
          <i class="bi bi-check2-square me-1"></i>Check the camps or clinics for each player
        </p>
      } @else {
        <h4 class="welcome-title"><i class="bi bi-flag-fill welcome-icon" style="color: var(--bs-info)"></i> Assign Teams</h4>
        <p class="welcome-desc">
          <i class="bi bi-hand-index me-1"></i>Pick a team for each player
          <span class="desc-dot"></span>
          <i class="bi bi-bar-chart me-1"></i>Capacity shown in dropdown
          <span class="desc-dot"></span>
          <i class="bi bi-hourglass-split me-1"></i>Waitlist if full
        </p>
      }
    </div>

    <div class="card shadow border-0 card-rounded">
      <div class="card-body pt-3">
        @if (teamService.loading()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading teams...</span>
            </div>
          </div>
        } @else if (teamService.error()) {
          <div class="alert alert-danger">{{ teamService.error() }}</div>
        } @else {
          <!-- CAC with 2+ players: tabbed layout -->
          @if (state.jobCtx.isCacMode() && selectedPlayerIds().length > 1) {
            <div class="player-tabs">
              @for (pid of selectedPlayerIds(); track pid; let i = $index) {
                <button type="button" class="player-tab"
                        [class.is-active]="activePlayerTab() === i"
                        [class.is-incomplete]="shouldPulsePlayerTab(pid, i)"
                        (click)="activePlayerTab.set(i)">
                  <span class="player-tab-name">{{ getPlayerName(pid) }}</span>
                  @if (getSelectedTeamIds(pid).length > 0) {
                    <span class="player-tab-count has-selections">
                      {{ getSelectedTeamIds(pid).length }}
                    </span>
                  } @else {
                    <span class="player-tab-cta">select events</span>
                  }
                </button>
              }
            </div>
          }
          <div class="player-list">
            @for (pid of selectedPlayerIds(); track pid; let i = $index) {
              @if (!state.jobCtx.isCacMode() || selectedPlayerIds().length <= 1 || activePlayerTab() === i) {
              <div class="player-row" [class.is-set]="!!getSelectedTeamId(pid)"
                   [class.is-locked]="isPlayerLocked(pid)">
                <i class="bi player-icon"
                   [class.bi-person-fill]="!isPlayerLocked(pid)"
                   [class.bi-person-check-fill]="isPlayerLocked(pid)"></i>
                <div class="player-info">
                  <div class="player-header">
                    <span class="player-name">{{ getPlayerName(pid) }}</span>
                    @if (getPlayerEligibility(pid)) {
                      <span class="elig-pill">{{ getPlayerEligibility(pid) }}</span>
                    }
                    @if (isPlayerLocked(pid)) {
                      <span class="locked-badge">
                        <i class="bi bi-lock-fill me-1"></i>Registered
                      </span>
                    }
                  </div>

                  @if (isPlayerLocked(pid)) {
                    @if (getSelectedTeamName(pid)) {
                      <span class="team-assigned">{{ getSelectedTeamName(pid) }}</span>
                    }
                  } @else if (state.jobCtx.isCacMode()) {
                    <!-- CAC: Keyword filter -->
                    @if (getAvailableTeams(pid).length > 5) {
                      <div class="camp-filter">
                        <i class="bi bi-search camp-filter-icon"></i>
                        <input type="text" class="camp-filter-input"
                               placeholder="Filter events..."
                               [value]="getCampFilter(pid)"
                               (input)="setCampFilter(pid, $any($event.target).value)">
                        @if (getCampFilter(pid)) {
                          <button type="button" class="camp-filter-clear"
                                  (click)="setCampFilter(pid, '')"
                                  aria-label="Clear filter">×</button>
                        }
                      </div>
                    }
                    <!-- CAC: Selected camps (top section) -->
                    @if (getSelectedCamps(pid).length) {
                      <div class="camp-section-label camp-section-registered">
                        <i class="bi bi-check-circle-fill me-1"></i>Your Events
                      </div>
                      <div class="camp-list">
                        @for (team of filterCamps(getSelectedCamps(pid), getCampFilter(pid)); track team.teamId) {
                          <label class="camp-card"
                                 [class.is-checked]="true"
                                 [class.is-registered]="isCampAlreadyRegistered(pid, team.teamId)">
                            <input type="checkbox" checked
                                   [disabled]="isCampAlreadyRegistered(pid, team.teamId)"
                                   (change)="toggleCampSelection(pid, team.teamId)">
                            <div class="camp-info">
                              <span class="camp-name">{{ team.teamName }}</span>
                              @if (team.divisionName) {
                                <span class="camp-division">{{ team.divisionName }}</span>
                              }
                              <div class="camp-meta">
                                @if (team.startDate || team.endDate) {
                                  <span class="camp-dates">
                                    <i class="bi bi-calendar3 me-1"></i>{{ formatCampDates(team) }}
                                  </span>
                                }
                                @if (team.perRegistrantFee != null && team.perRegistrantFee > 0) {
                                  <span class="camp-fee">{{ team.perRegistrantFee | currency }}</span>
                                }
                              </div>
                              @if (isCampAlreadyRegistered(pid, team.teamId)) {
                                <span class="camp-registered-badge">
                                  <i class="bi bi-check-circle-fill me-1"></i>Registered
                                </span>
                              }
                            </div>
                          </label>
                        }
                      </div>
                    }
                    <!-- CAC: Available camps (unselected) -->
                    @if (getUnselectedCamps(pid).length) {
                      @if (getSelectedCamps(pid).length) {
                        <div class="camp-section-label camp-section-available">
                          <i class="bi bi-plus-circle me-1"></i>Add Events
                        </div>
                      }
                      <div class="camp-list">
                        @for (team of filterCamps(getUnselectedCamps(pid), getCampFilter(pid)); track team.teamId) {
                          <label class="camp-card"
                                 [class.is-full]="isTeamFull(team)">
                            <input type="checkbox"
                                   [checked]="false"
                                   (change)="toggleCampSelection(pid, team.teamId)"
                                   [disabled]="isTeamFull(team)">
                            <div class="camp-info">
                              <span class="camp-name">{{ team.teamName }}</span>
                              @if (team.divisionName) {
                                <span class="camp-division">{{ team.divisionName }}</span>
                              }
                              <div class="camp-meta">
                                @if (team.startDate || team.endDate) {
                                  <span class="camp-dates">
                                    <i class="bi bi-calendar3 me-1"></i>{{ formatCampDates(team) }}
                                  </span>
                                }
                                @if (team.perRegistrantFee != null && team.perRegistrantFee > 0) {
                                  <span class="camp-fee">{{ team.perRegistrantFee | currency }}</span>
                                }
                              </div>
                              @if (isTeamFull(team)) {
                                <span class="camp-full-badge">Full</span>
                              }
                            </div>
                          </label>
                        }
                      </div>
                    }
                  } @else {
                    <!-- PP: Team dropdown -->
                    <!-- Selected teams pills (multi-mode) -->
                    @if (getSelectedTeamIds(pid).length && isMultiTeamMode()) {
                      <div class="team-pills">
                        @for (tid of getSelectedTeamIds(pid); track tid) {
                          <span class="team-pill">
                            {{ getTeamName(tid) }}
                            <button type="button" class="team-pill-remove"
                                    (click)="removeTeam(pid, tid)"
                                    [attr.aria-label]="'Remove ' + getTeamName(tid)">×</button>
                          </span>
                        }
                      </div>
                    }

                    @if (isSelectedTeamWaitlisted(pid)) {
                      <div class="waitlist-alert">
                        <i class="bi bi-exclamation-triangle-fill"></i>
                        <div>
                          <strong>Waitlist Only</strong> —
                          This team is currently full. You will be placed on the waitlist
                          and notified if a spot becomes available.
                        </div>
                      </div>
                    }

                    @if (!getAvailableTeams(pid).length) {
                      <div class="no-teams-alert" role="alert">
                        <i class="bi bi-exclamation-octagon-fill"></i>
                        <div>
                          <strong>No teams available</strong> —
                          There are no teams in this event for {{ constraintLabel() }}
                          @if (getPlayerEligibility(pid)) {
                            <strong>{{ getPlayerEligibility(pid) }}</strong>
                          }
                          . Please go back and verify the {{ constraintLabel() }},
                          or contact the event director.
                        </div>
                      </div>
                    } @else {
                      <ejs-dropdownlist
                        [dataSource]="getTeamDropdownItems(pid)"
                        [fields]="teamDdlFields"
                        [value]="isMultiTeamMode() ? null : (getSelectedTeamId(pid) || null)"
                        (change)="onTeamDdlChange(pid, $event)"
                        [placeholder]="'— Select Team —'"
                        [allowFiltering]="false"
                        cssClass="team-ddl">
                      </ejs-dropdownlist>
                    }
                  }
                </div>
              </div>
              }
            }
          </div>
        }
      </div>
    </div>
  `,
    styles: [`
      /* ── CAC player tabs (pill chips) ── */
      .player-tabs {
        display: flex;
        gap: var(--space-2);
        margin-bottom: var(--space-3);
        overflow-x: auto;
        padding-bottom: var(--space-1);
      }

      .player-tab {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        padding: var(--space-2) var(--space-4);
        border: 1px solid var(--neutral-300);
        border-radius: var(--radius-full);
        background: var(--brand-surface);
        color: var(--brand-text-muted);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        cursor: pointer;
        white-space: nowrap;
        transition: all 0.15s ease;

        &:hover:not(.is-active) {
          border-color: var(--bs-primary);
          color: var(--brand-text);
          background: rgba(var(--bs-primary-rgb), 0.04);
        }

        &.is-active {
          background: var(--bs-primary);
          border-color: var(--bs-primary);
          color: var(--bs-light);
          font-weight: var(--font-weight-semibold);
          box-shadow: var(--shadow-sm);
        }

        &.is-incomplete:not(.is-active) {
          border-color: var(--bs-warning);
          color: var(--brand-text);
          animation: tab-pulse 1.8s ease-in-out infinite;
        }
      }

      @keyframes tab-pulse {
        0%, 100% { box-shadow: 0 0 0 0 rgba(var(--bs-warning-rgb), 0.45); }
        50%      { box-shadow: 0 0 0 6px rgba(var(--bs-warning-rgb), 0); }
      }

      @media (prefers-reduced-motion: reduce) {
        .player-tab.is-incomplete:not(.is-active) {
          animation: none;
          box-shadow: 0 0 0 2px rgba(var(--bs-warning-rgb), 0.35);
        }
      }

      .player-tab-name {
        .is-active & { color: var(--bs-light); }
      }

      .player-tab-count {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-width: 20px;
        height: 20px;
        padding: 0 var(--space-1);
        border-radius: var(--radius-full);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-bold);
        background: rgba(var(--bs-success-rgb), 0.2);
        color: var(--bs-success);

        .is-active & {
          background: rgba(255, 255, 255, 0.35);
          color: var(--bs-light);
        }
      }

      .player-tab-cta {
        font-size: var(--font-size-xs);
        font-style: italic;
        font-weight: var(--font-weight-medium);
        color: var(--bs-warning);
        text-transform: lowercase;

        &::before {
          content: '— ';
          font-style: normal;
        }

        .is-active & {
          color: var(--bs-light);
          opacity: 0.9;
        }
      }

      .player-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .player-row {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        border-radius: var(--radius-md);
        border: 1px solid var(--border-color);
        background: var(--brand-surface);
        transition: border-color 0.15s ease, background-color 0.15s ease;

        &.is-set {
          border-color: rgba(var(--bs-primary-rgb), 0.3);
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &.is-locked {
          border-color: rgba(var(--bs-success-rgb), 0.25);
          background: rgba(var(--bs-success-rgb), 0.05);
          opacity: 0.75;
        }
      }

      .player-icon {
        font-size: var(--font-size-xl);
        color: var(--neutral-400);
        margin-top: 2px;

        .is-set & { color: var(--bs-primary); }
        .is-locked & { color: var(--bs-success); }
      }

      .player-info {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: var(--space-1);
      }

      .player-header {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        flex-wrap: wrap;
      }

      .player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .elig-pill {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        padding: 1px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-info-rgb), 0.1);
        color: var(--bs-info-emphasis);
        border: 1px solid rgba(var(--bs-info-rgb), 0.2);
      }

      .locked-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
        white-space: nowrap;
        margin-left: auto;
      }

      .team-assigned {
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
      }

      .team-pills {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .team-pill {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        border: 1px solid rgba(var(--bs-primary-rgb), 0.2);
      }

      .team-pill-remove {
        background: none;
        border: none;
        padding: 0;
        margin: 0 0 0 2px;
        font-size: var(--font-size-sm);
        line-height: 1;
        color: rgba(var(--bs-primary-rgb), 0.5);
        cursor: pointer;

        &:hover { color: var(--bs-primary); }
      }

      .team-select {
        appearance: none;
        width: 100%;
        padding: var(--space-1) var(--space-3);
        padding-right: var(--space-8);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        background-color: var(--neutral-50);
        background-image: url("data:image/svg+xml,%3csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'%3e%3cpath fill='none' stroke='%2378716c' stroke-linecap='round' stroke-linejoin='round' stroke-width='2' d='m2 5 6 6 6-6'/%3e%3c/svg%3e");
        background-repeat: no-repeat;
        background-position: right var(--space-2) center;
        background-size: 14px 10px;
        border: 1px solid var(--border-color);
        border-radius: var(--radius-sm);
        transition: border-color 0.15s ease, box-shadow 0.15s ease, background-color 0.15s ease;

        &:hover:not(:focus) {
          border-color: var(--neutral-400);
        }

        &:focus {
          outline: none;
          border-color: var(--bs-primary);
          background-color: var(--brand-surface);
          box-shadow: var(--shadow-focus);
        }
      }

      /* Syncfusion dropdown item template */

      /* ── CAC camp filter ── */
      .camp-filter {
        position: relative;
        margin-bottom: var(--space-2);
      }

      .camp-filter-icon {
        position: absolute;
        left: var(--space-3);
        top: 50%;
        transform: translateY(-50%);
        color: var(--neutral-400);
        font-size: var(--font-size-sm);
        pointer-events: none;
      }

      .camp-filter-input {
        width: 100%;
        padding: var(--space-2) var(--space-3);
        padding-left: var(--space-8);
        font-size: var(--font-size-sm);
        border: 1px solid var(--border-color);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--brand-text);

        &:focus {
          outline: none;
          border-color: var(--bs-primary);
          box-shadow: var(--shadow-focus);
        }
      }

      .camp-filter-clear {
        position: absolute;
        right: var(--space-2);
        top: 50%;
        transform: translateY(-50%);
        background: none;
        border: none;
        padding: 0 var(--space-1);
        font-size: var(--font-size-lg);
        color: var(--neutral-400);
        cursor: pointer;
        line-height: 1;

        &:hover { color: var(--brand-text); }
      }

      /* ── CAC section labels ── */
      .camp-section-label {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        text-transform: uppercase;
        letter-spacing: 0.04em;
        padding: var(--space-2) 0 var(--space-1);
      }

      .camp-section-registered {
        color: var(--bs-success);
      }

      .camp-section-available {
        color: var(--bs-primary);
        margin-top: var(--space-3);
        padding-top: var(--space-3);
        border-top: 1px dashed var(--border-color);
      }

      /* ── CAC camp card styles ── */
      .camp-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .camp-card {
        display: flex;
        align-items: flex-start;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        border-radius: var(--radius-sm);
        border: 1px solid var(--border-color);
        border-left: 3px solid var(--neutral-300);
        background: var(--brand-surface);
        cursor: pointer;
        transition: border-color 0.15s ease, background-color 0.15s ease;

        &:hover:not(.is-full):not(.is-registered) {
          border-left-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &.is-checked {
          border-left-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.06);
        }

        &.is-registered {
          border-left-color: var(--bs-success);
          border-left-width: 4px;
          background: rgba(var(--bs-success-rgb), 0.08);
          cursor: default;
          opacity: 0.85;
        }

        &.is-full:not(.is-checked) {
          opacity: 0.5;
          cursor: not-allowed;
        }

        input[type="checkbox"] {
          margin-top: 3px;
          flex-shrink: 0;
          accent-color: var(--bs-primary);
        }
      }

      .camp-info {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .camp-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .camp-division {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .camp-meta {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        flex-wrap: wrap;
      }

      .camp-dates {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .camp-fee {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-primary);
      }

      .camp-registered-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
      }

      .camp-full-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-danger);
      }

      .waitlist-alert {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-3);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-warning-rgb), 0.12);
        border: 1px solid rgba(var(--bs-warning-rgb), 0.4);
        color: var(--bs-warning-emphasis);
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);

        i {
          font-size: var(--font-size-lg);
          flex-shrink: 0;
          margin-top: 1px;
        }
      }

      .no-teams-alert {
        display: flex;
        align-items: flex-start;
        gap: var(--space-2);
        padding: var(--space-3);
        border-radius: var(--radius-sm);
        background: rgba(var(--bs-danger-rgb), 0.10);
        border: 1px solid rgba(var(--bs-danger-rgb), 0.4);
        color: var(--bs-danger-text-emphasis);
        font-size: var(--font-size-sm);
        line-height: var(--line-height-normal);

        i {
          font-size: var(--font-size-lg);
          flex-shrink: 0;
          margin-top: 1px;
          color: var(--bs-danger);
        }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamSelectionStepComponent {
    readonly state = inject(PlayerWizardStateService);
    readonly teamService = inject(TeamService);
    private readonly jobService = inject(JobService);
    readonly advance = output<void>();

    readonly teamDdlFields = { text: 'text', value: 'value' };
    private readonly _campFilters = signal<Record<string, string>>({});
    readonly activePlayerTab = signal(0);
    readonly isTournament = computed(() => this.jobService.currentJob()?.jobTypeId === JOB_TYPE_TOURNAMENT);

    getTeamDropdownItems(playerId: string): { text: string; value: string; status: string }[] {
        const tournament = this.isTournament();
        return this.getAvailableTeams(playerId).map(team => {
            const clubPrefix = team.clubName?.trim() ? `${team.clubName.trim()}: ` : '';
            let label = clubPrefix + team.teamName;
            if (!tournament && team.divisionName) label += ` · ${team.divisionName}`;
            // Two-phase jobs: lead with all-in total, hint at deposit so parent knows
            // a payment plan exists. Single-phase: lone price. Backend collapses
            // deposit==fee → deposit=0 so checking deposit > 0 is sufficient.
            const deposit = Number(team.deposit ?? 0) || 0;
            const fee = Number(team.effectiveFee ?? 0) || 0;
            if (fee > 0 && deposit > 0 && deposit !== fee) {
                label += ` · ${this.formatCurrency(deposit + fee)} (${this.formatCurrency(deposit)} deposit)`;
            } else if (fee > 0) {
                label += ` (${this.formatCurrency(fee)})`;
            }

            let status = '';
            if (team.rosterIsFull && team.jobUsesWaitlists) {
                status = 'waitlist';
                label = '⚠ WAITLIST · ' + label;
            } else if (team.rosterIsFull) {
                status = 'full';
            } else {
                const remaining = team.maxRosterSize - team.currentRosterSize;
                if (remaining <= 5 && team.maxRosterSize > 0) status = 'almost-full';
            }

            return { text: label, value: team.teamId, status };
        });
    }

    private formatCurrency(amount: number): string {
        return amount.toLocaleString('en-US', { style: 'currency', currency: 'USD', minimumFractionDigits: 0, maximumFractionDigits: 0 });
    }

    onTeamDdlChange(playerId: string, event: any): void {
        const teamId = event.value as string;
        if (teamId) this.onTeamChange(playerId, teamId);
    }

    selectedPlayerIds(): string[] {
        return this.state.familyPlayers.selectedPlayerIds();
    }

    shouldPulsePlayerTab(pid: string, idx: number): boolean {
        if (this.activePlayerTab() === idx) return false;
        const teams = this.state.eligibility.selectedTeams();
        const mine = teams[pid];
        const mineEmpty = !Array.isArray(mine) || mine.length === 0;
        if (!mineEmpty) return false;
        for (const other of this.selectedPlayerIds()) {
            if (other === pid) continue;
            const v = teams[other];
            if (Array.isArray(v) && v.length > 0) return true;
        }
        return false;
    }

    getPlayerName(playerId: string): string {
        const p = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        return p ? `${p.firstName} ${p.lastName}`.trim() : playerId;
    }

    getPlayerBadgeClass(playerId: string): string {
        const idx = this.selectedPlayerIds().indexOf(playerId);
        return colorClassForIndex(idx);
    }

    getPlayerEligibility(playerId: string): string | undefined {
        return this.state.eligibility.getEligibilityForPlayer(playerId);
    }

    isPlayerLocked(playerId: string): boolean {
        // CAC: players are never fully locked — they can always add more camps
        if (this.state.jobCtx.isCacMode()) return false;
        return this.state.familyPlayers.isPlayerLocked(playerId);
    }

    constraintLabel(): string {
        const ct = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        if (ct === 'BYGRADYEAR') return 'graduation year';
        if (ct === 'BYAGEGROUP') return 'age group';
        if (ct === 'BYAGERANGE') return 'age range';
        if (ct === 'BYCLUBNAME') return 'club';
        return 'eligibility';
    }

    isMultiTeamMode(): boolean {
        return this.state.jobCtx.isCacMode();
    }

    getAvailableTeams(playerId: string): AvailableTeam[] {
        const eligValue = this.getPlayerEligibility(playerId) ?? null;
        const player = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        const teams = this.teamService.filterByEligibility(eligValue, player?.gender);

        // Hide full teams whose waitlist replacement exists in the list
        const teamIdsInList = new Set(teams.map(t => t.teamId));
        let filtered = teams.filter(t =>
            !(t.rosterIsFull && t.waitlistTeamId && teamIdsInList.has(t.waitlistTeamId)),
        );

        // CAC: sort by start date (earliest first), then by name
        if (this.state.jobCtx.isCacMode()) {
            filtered = [...filtered].sort((a, b) => {
                const da = a.startDate ? new Date(a.startDate).getTime() : Infinity;
                const db = b.startDate ? new Date(b.startDate).getTime() : Infinity;
                if (da !== db) return da - db;
                return a.teamName.localeCompare(b.teamName, undefined, { numeric: true, sensitivity: 'base' });
            });
        } else {
            // Non-CAC (tournament, season, etc.): natural alpha sort by club then team
            const cmp = (x: string, y: string) => x.localeCompare(y, undefined, { numeric: true, sensitivity: 'base' });
            filtered = [...filtered].sort((a, b) => {
                const c = cmp(a.clubName?.trim() || '', b.clubName?.trim() || '');
                return c !== 0 ? c : cmp(a.teamName, b.teamName);
            });
        }

        return filtered;
    }

    filterCamps(teams: AvailableTeam[], query: string): AvailableTeam[] {
        const q = (query || '').trim().toLowerCase();
        if (!q) return teams;
        return teams.filter(t =>
            t.teamName.toLowerCase().includes(q)
            || (t.divisionName || '').toLowerCase().includes(q)
            || (t.agegroupName || '').toLowerCase().includes(q),
        );
    }

    getSelectedTeamId(playerId: string): string | null {
        const sel = this.state.eligibility.selectedTeams()[playerId];
        if (!sel) return null;
        return Array.isArray(sel) ? sel[0] ?? null : sel;
    }

    getSelectedTeamIds(playerId: string): string[] {
        const sel = this.state.eligibility.selectedTeams()[playerId];
        if (!sel) return [];
        return Array.isArray(sel) ? sel : [sel];
    }

    getSelectedTeamName(playerId: string): string {
        const tid = this.getSelectedTeamId(playerId);
        if (!tid) return '';
        return this.getTeamName(tid);
    }

    getTeamName(teamId: string): string {
        const team = this.teamService.getTeamById(teamId);
        return team?.teamName || teamId;
    }

    isSelectedTeamWaitlisted(playerId: string): boolean {
        const tids = this.getSelectedTeamIds(playerId);
        if (!tids.length) return false;
        return tids.some(tid => {
            const team = this.teamService.getTeamById(tid);
            return team?.rosterIsFull && team?.jobUsesWaitlists;
        });
    }

    isTeamFull(team: AvailableTeam): boolean {
        return team.rosterIsFull && !team.jobUsesWaitlists;
    }

    isTeamAlreadySelected(playerId: string, teamId: string): boolean {
        return this.getSelectedTeamIds(playerId).includes(teamId);
    }

    getCapacityLabel(team: AvailableTeam): string {
        if (team.rosterIsFull && team.jobUsesWaitlists) return '· Waitlist';
        if (team.rosterIsFull) return '· Full';
        const remaining = team.maxRosterSize - team.currentRosterSize;
        if (remaining <= 5 && team.maxRosterSize > 0) return '· Almost full';
        return '';
    }

    onTeamChange(playerId: string, teamId: string): void {
        if (!teamId) return;
        const current = { ...this.state.eligibility.selectedTeams() };
        if (this.isMultiTeamMode()) {
            const existing = Array.isArray(current[playerId]) ? [...(current[playerId] as string[])] : current[playerId] ? [current[playerId] as string] : [];
            if (!existing.includes(teamId)) existing.push(teamId);
            current[playerId] = existing;
        } else {
            current[playerId] = teamId;
        }
        this.state.eligibility.setSelectedTeams(current);

        // Auto-advance when every player has a team (single-select mode only)
        if (!this.isMultiTeamMode()) {
            const allSet = this.selectedPlayerIds()
                .every(id => !!current[id]);
            if (allSet) this.advance.emit();
        }
    }

    removeTeam(playerId: string, teamId: string): void {
        const current = { ...this.state.eligibility.selectedTeams() };
        const existing = current[playerId];
        if (Array.isArray(existing)) {
            const filtered = existing.filter((t: string) => t !== teamId);
            if (filtered.length === 0) delete current[playerId];
            else current[playerId] = filtered;
        } else if (existing === teamId) {
            delete current[playerId];
        }
        this.state.eligibility.setSelectedTeams(current);
    }

    // ── CAC helpers ──────────────────────────────────────────────────

    getCampFilter(playerId: string): string {
        return this._campFilters()[playerId] || '';
    }

    setCampFilter(playerId: string, value: string): void {
        this._campFilters.set({ ...this._campFilters(), [playerId]: value });
    }

    toggleCampSelection(playerId: string, teamId: string): void {
        if (this.isTeamAlreadySelected(playerId, teamId)) {
            this.removeTeam(playerId, teamId);
        } else {
            this.onTeamChange(playerId, teamId);
        }
    }

    formatCampDates(team: AvailableTeam): string {
        const fmt = (iso: string) => new Date(iso).toLocaleDateString();
        const start = team.startDate ? fmt(team.startDate) : '';
        const end = team.endDate ? fmt(team.endDate) : '';
        if (start && end) return `${start} \u2013 ${end}`;
        return start || end;
    }

    isCampAlreadyRegistered(playerId: string, teamId: string): boolean {
        const player = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        if (!player?.registered) return false;
        return player.priorRegistrations?.some(r => r.assignedTeamId === teamId) ?? false;
    }

    getSelectedCamps(playerId: string): AvailableTeam[] {
        const selected = new Set(this.getSelectedTeamIds(playerId));
        return this.getAvailableTeams(playerId).filter(t => selected.has(t.teamId));
    }

    getUnselectedCamps(playerId: string): AvailableTeam[] {
        const selected = new Set(this.getSelectedTeamIds(playerId));
        return this.getAvailableTeams(playerId).filter(t => !selected.has(t.teamId));
    }
}
