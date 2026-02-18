import { ChangeDetectionStrategy, Component, inject, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';
import { TeamService, type AvailableTeam } from '@views/registration/wizards/player-registration-wizard/team.service';
import { colorClassForIndex } from '@views/registration/wizards/shared/utils/color-class.util';

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
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Team Selection</h5>
      </div>
      <div class="card-body">
        @if (teamService.loading()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading teams...</span>
            </div>
          </div>
        } @else if (teamService.error()) {
          <div class="alert alert-danger">{{ teamService.error() }}</div>
        } @else {
          <p class="text-muted small mb-3">
            @if (isMultiTeamMode()) {
              Select one or more teams for each player.
            } @else {
              Select a team for each player based on their eligibility.
            }
          </p>
          @for (pid of selectedPlayerIds(); track pid) {
            <div class="mb-4 p-3 rounded-3 border">
              <div class="d-flex align-items-center gap-2 mb-2">
                <span class="badge" [class]="getPlayerBadgeClass(pid)">
                  {{ getPlayerName(pid) }}
                </span>
                @if (isPlayerLocked(pid)) {
                  <span class="badge bg-secondary">Locked</span>
                }
                @if (getPlayerEligibility(pid)) {
                  <span class="badge bg-info-subtle text-info-emphasis border border-info-subtle">
                    {{ getPlayerEligibility(pid) }}
                  </span>
                }
              </div>

              @if (isPlayerLocked(pid)) {
                <!-- Show locked team info -->
                @if (getSelectedTeamName(pid)) {
                  <div class="d-flex align-items-center gap-2">
                    <span class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle">
                      {{ getSelectedTeamName(pid) }}
                    </span>
                    <span class="text-muted small">(Registered)</span>
                  </div>
                }
              } @else {
                <!-- Selected teams pills -->
                @if (getSelectedTeamIds(pid).length) {
                  <div class="d-flex flex-wrap gap-1 mb-2">
                    @for (tid of getSelectedTeamIds(pid); track tid) {
                      <span class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle d-inline-flex align-items-center gap-1">
                        {{ getTeamName(tid) }}
                        <button type="button" class="btn-close btn-close-sm" style="font-size: 0.6em"
                                (click)="removeTeam(pid, tid)"
                                [attr.aria-label]="'Remove ' + getTeamName(tid)"></button>
                      </span>
                    }
                  </div>
                }

                <!-- Team dropdown -->
                <select class="form-select" [id]="'team-' + pid"
                        [ngModel]="isMultiTeamMode() ? '' : (getSelectedTeamId(pid) || '')"
                        (ngModelChange)="onTeamChange(pid, $event)">
                  <option value="">— Select Team —</option>
                  @for (team of getAvailableTeams(pid); track team.teamId) {
                    <option [value]="team.teamId"
                            [disabled]="isTeamFull(team) || isTeamAlreadySelected(pid, team.teamId)">
                      {{ team.teamName }}
                      @if (team.divisionName) { ({{ team.divisionName }}) }
                      {{ getCapacityLabel(team) }}
                    </option>
                  }
                </select>
              }
            </div>
          }
        }
      </div>
    </div>
  `,
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

    isMultiTeamMode(): boolean {
        const ct = (this.state.eligibility.teamConstraintType() || '').toUpperCase();
        return !ct || ct === 'BYCLUBNAME';
    }

    getAvailableTeams(playerId: string): AvailableTeam[] {
        const eligValue = this.getPlayerEligibility(playerId) ?? null;
        return this.teamService.filterByEligibility(eligValue);
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
        const remaining = team.maxRosterSize - team.currentRosterSize;
        if (team.rosterIsFull) return '[FULL]';
        if (remaining <= 5 && team.maxRosterSize > 0) return `[${remaining} spots]`;
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
