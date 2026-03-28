import { ChangeDetectionStrategy, Component, inject, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService, type AvailableTeam } from '@views/registration/player/services/team.service';
import { colorClassForIndex } from '@views/registration/shared/utils/color-class.util';

/**
 * Team Selection step — per-player team assignment via dropdowns.
 * Supports single-select (by constraint) or multi-select (no constraint).
 * Shows capacity info and locked-player detection.
 */
@Component({
    selector: 'app-prw-team-selection-step',
    standalone: true,
    imports: [FormsModule],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-2">
        <h5 class="mb-0 fw-semibold" style="font-size: var(--font-size-base)">Team Selection</h5>
      </div>
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
          <p class="wizard-tip">
            @if (isMultiTeamMode()) {
              Select one or more teams for each player.
            } @else {
              Select a team for each player based on their {{ constraintLabel() }}.
            }
          </p>
          <div class="player-list">
            @for (pid of selectedPlayerIds(); track pid) {
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
                  } @else {
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

                    <select class="team-select" [id]="'team-' + pid"
                            [ngModel]="isMultiTeamMode() ? '' : (getSelectedTeamId(pid) || '')"
                            (ngModelChange)="onTeamChange(pid, $event)">
                      <option value="">— Select Team —</option>
                      @for (team of getAvailableTeams(pid); track team.teamId) {
                        <option [value]="team.teamId"
                                [disabled]="isTeamFull(team) || isTeamAlreadySelected(pid, team.teamId)">
                          {{ team.teamName }}
                          @if (team.divisionName) { · {{ team.divisionName }} }
                          {{ getCapacityLabel(team) }}
                        </option>
                      }
                    </select>
                  }
                </div>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
    styles: [`
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
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TeamSelectionStepComponent {
    readonly state = inject(PlayerWizardStateService);
    readonly teamService = inject(TeamService);

    selectedPlayerIds(): string[] {
        return this.state.familyPlayers.selectedPlayerIds();
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
        const ct = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        return !ct || ct === 'BYCLUBNAME';
    }

    getAvailableTeams(playerId: string): AvailableTeam[] {
        const eligValue = this.getPlayerEligibility(playerId) ?? null;
        const player = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        const teams = this.teamService.filterByEligibility(eligValue, player?.gender);

        // Hide full teams whose waitlist replacement exists in the list
        const teamIdsInList = new Set(teams.map(t => t.teamId));
        return teams.filter(t =>
            !(t.rosterIsFull && t.waitlistTeamId && teamIdsInList.has(t.waitlistTeamId)),
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
}
