import { Component, EventEmitter, Input, Output, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';
import { TeamService } from '../team.service';

@Component({
  selector: 'app-rw-team-selection',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Teams</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Assign a team to each player. Team lists are filtered by that player's eligibility selection.</p>
        @if (loading()) {
          <div class="text-muted small">Loading teams...</div>
        } @else {
          @if (error()) { <div class="alert alert-danger py-2 px-3">{{ error() }}</div> }
          @if (selectedPlayers().length === 0) {
            <div class="alert alert-info">Select players first to assign teams.</div>
          }
          @if (selectedPlayers().length > 0) {
            <div class="vstack gap-3">
              @for (p of selectedPlayers(); track p.userId) {
                <div class="player-team-row p-3 border rounded">
                  <div class="d-flex flex-column flex-md-row justify-content-between gap-3">
                    <div class="flex-grow-1">
                      <div class="fw-semibold mb-1 d-flex align-items-center gap-2">
                        <span>{{ p.name }}</span>
                        @if (readonlyMode() || isRegistered(p.userId)) {
                          <span class="badge bg-secondary" title="Already registered; team locked">Locked</span>
                        }
                      </div>
                      @if (!eligibilityFor(p.userId)) {
                        <div class="small text-muted">Eligibility not selected; go back to set it.</div>
                      } @else {
                        <div class="small">
                          Eligibility: <span class="badge bg-primary-subtle text-dark">{{ eligibilityFor(p.userId) }}</span>
                        </div>
                      }
                    </div>
                    <div class="flex-grow-1">
                      <label class="form-label small fw-semibold">Team</label>
                      <select class="form-select"
                              [disabled]="readonlyMode() || isRegistered(p.userId) || !eligibilityFor(p.userId) || filteredTeamsFor(p.userId).length===0"
                              (change)="onTeamSelect(p.userId, $event)"
                              [ngModel]="selectedTeams()[p.userId] || ''">
                        <option value="">Select team</option>
                        @for (t of filteredTeamsFor(p.userId); track t.teamId) {
                          @if (!multiSelect) {
                            <option [value]="t.teamId" [disabled]="t.rosterIsFull || wouldExceedCapacity(p.userId, t)">
                              {{ t.teamName }}
                              @if (t.rosterIsFull || wouldExceedCapacity(p.userId, t)) { <span> FULL</span> }
                              @if (showRemaining(t.teamId)) {
                                <span class="text-warning"> — only {{ baseRemaining(t.teamId) }} {{ s(baseRemaining(t.teamId)) }} of {{ baseMax(t.teamId) }} remaining!</span>
                              }
                            </option>
                          }
                        }
                      </select>
                      @if (filteredTeamsFor(p.userId).length===0 && eligibilityFor(p.userId)) {
                        <div class="form-text">No teams match eligibility.</div>
                      }
                    </div>
                  </div>
                  @if (multiSelect) {
                    <div class="mt-2">
                      <div class="dropdown" [class.disabled]="readonlyMode() || isRegistered(p.userId) || !eligibilityFor(p.userId)">
                        <button class="btn btn-sm btn-outline-primary dropdown-toggle" type="button" data-bs-toggle="dropdown" [disabled]="readonlyMode() || isRegistered(p.userId) || !eligibilityFor(p.userId) || filteredTeamsFor(p.userId).length===0">
                          {{ multiSelectLabel(p.userId) }}
                        </button>
                        <ul class="dropdown-menu p-2" style="max-height:260px;overflow:auto;min-width:320px">
                          @if (filteredTeamsFor(p.userId).length===0) {
                            <li class="text-muted small px-2 py-1">No teams match eligibility.</li>
                          }
                          @for (t of filteredTeamsFor(p.userId); track t.teamId) {
                            <li>
                              <label class="form-check d-flex align-items-center gap-2 small mb-1">
                                <input type="checkbox"
                                       class="form-check-input"
                                       [checked]="isTeamChecked(p.userId, t.teamId)"
                                       [disabled]="readonlyMode() || isRegistered(p.userId) || t.rosterIsFull || wouldExceedCapacity(p.userId, t)"
                                       (change)="onMultiToggle(p.userId, t.teamId, $event)" />
                                <span>{{ t.teamName }}</span>
                                @if (showRemaining(t.teamId)) {
                                  <span class="ms-auto text-muted small">only {{ baseRemaining(t.teamId) }} {{ s(baseRemaining(t.teamId)) }} of {{ baseMax(t.teamId) }} remaining!</span>
                                }
                              </label>
                            </li>
                          }
                        </ul>
                      </div>
                    </div>
                  }
                </div>
              }
            </div>
          }
          @if (missingAssignments().length && selectedPlayers().length > 0) {
            <div class="invalid-feedback d-block mt-2">Assign a team for: {{ missingAssignments().join(', ') }}.</div>
          }
        }
        <div class="border-top pt-3 mt-4">
          <div class="rw-bottom-nav d-flex gap-2">
            <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
            <button type="button" class="btn btn-primary" [disabled]="!allPlayersAssigned()" (click)="next.emit()">Continue</button>
          </div>
        </div>
      </div>
    </div>
  `
})
export class TeamSelectionComponent {
  // Configurable threshold: only apply tentative capacity guard when base remaining <= this value
  @Input() capacityGuardThreshold = 10;
  @Input() multiSelect = false; // when true, players can choose multiple teams
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  private readonly wizard: RegistrationWizardService = inject(RegistrationWizardService);
  private readonly teamService: TeamService = inject(TeamService);

  // expose signals for template
  loading = this.teamService.loading;
  error = this.teamService.error;
  selectedPlayers = this.wizard.selectedPlayers;
  selectedTeams = this.wizard.selectedTeams;
  eligibilityMap = this.wizard.eligibilityByPlayer;
  readonlyMode = computed(() => this.wizard.startMode() === 'edit');

  isRegistered(playerId: string): boolean {
    try {
      return !!this.wizard.familyPlayers().find(p => p.playerId === playerId)?.registered;
    } catch { return false; }
  }

  allPlayersAssigned = computed(() => {
    const players = this.selectedPlayers();
    const map = this.selectedTeams();
    if (players.length === 0) return false;
    return players.every(p => !!map[p.userId]);
  });

  filteredTeamsFor(playerId: string) {
    const elig = this.wizard.getEligibilityForPlayer(playerId);
    return this.teamService.filterByEligibility(elig);
  }
  eligibilityFor(playerId: string) {
    return this.wizard.getEligibilityForPlayer(playerId);
  }
  onTeamSelect(playerId: string, ev: Event) {
    if (this.readonlyMode()) return; // prevent changes in edit mode
    const target = ev.target as HTMLSelectElement | null;
    if (!target) return;
    const val = target.value || '';
    const current = { ...this.selectedTeams() };
    if (val) current[playerId] = val; else delete current[playerId];
    this.wizard.selectedTeams.set(current);
  }
  onMultiToggle(playerId: string, teamId: string, ev: Event) {
    if (this.readonlyMode()) return; // prevent changes in edit mode
    const checked = (ev.target as HTMLInputElement).checked;
    const map = { ...this.selectedTeams() } as Record<string, string | string[]>;
    const existing = map[playerId];
    let arr: string[] = [];
    if (Array.isArray(existing)) {
      arr = [...existing];
    } else if (typeof existing === 'string' && existing) {
      arr = [existing];
    }
    const idx = arr.indexOf(teamId);
    if (checked && idx === -1) arr.push(teamId);
    if (!checked && idx > -1) arr.splice(idx, 1);
    if (arr.length === 0) delete map[playerId]; else map[playerId] = arr;
    this.wizard.selectedTeams.set(map as any);
  }
  isTeamChecked(playerId: string, teamId: string): boolean {
    const val = this.selectedTeams()[playerId];
    if (Array.isArray(val)) return val.includes(teamId);
    return val === teamId;
  }
  multiSelectLabel(playerId: string): string {
    const val = this.selectedTeams()[playerId];
    if (!val) return 'Select teams';
    if (Array.isArray(val)) {
      const n = val.length;
      if (n === 0) return 'Select teams';
      return `${n} team${n > 1 ? 's' : ''} selected`;
    }
    return '1 team selected';
  }
  // Calculate tentative roster including current selections in this session
  tentativeRoster(teamId: string): number {
    // Access through public filtered collection plus original roster size
    // Use the union of all loaded teams (teamService.filteredTeams may omit some, but capacity matters only for currently selectable teams)
    const all = this.teamService.filterByEligibility(null); // returns all teams when value null
    const base = all.find(t => t.teamId === teamId)?.currentRosterSize ?? 0;
    const selections = this.selectedTeams();
    const additional = Object.values(selections).filter(id => id === teamId).length;
    return base + additional;
  }
  baseRemaining(teamId: string): number {
    const all = this.teamService.filterByEligibility(null);
    const team = all.find(t => t.teamId === teamId);
    if (!team) return 0;
    const max = team.maxRosterSize || 0;
    if (max <= 0) return 0;
    return Math.max(0, max - team.currentRosterSize);
  }
  showRemaining(teamId: string): boolean {
    const remaining = this.baseRemaining(teamId);
    return remaining > 0 && remaining <= this.capacityGuardThreshold;
  }
  baseMax(teamId: string): number {
    const all = this.teamService.filterByEligibility(null);
    return all.find(t => t.teamId === teamId)?.maxRosterSize || 0;
  }
  s(n: number): string { return n === 1 ? 'spot' : 'spots'; }
  wouldExceedCapacity(playerId: string, team: { teamId: string; maxRosterSize: number; rosterIsFull?: boolean }): boolean {
    const currentChoice = this.selectedTeams()[playerId];
    const max = team.maxRosterSize || 0;
    if (max <= 0) return false; // treat 0/undefined as unlimited
    // Base remaining capacity without considering in-session picks
    const all = this.teamService.filterByEligibility(null);
    const base = all.find(t => t.teamId === team.teamId)?.currentRosterSize ?? 0;
    const baseRemaining = max - base;
    // Only engage tentative guard when remaining slots are at or below threshold
    if (baseRemaining > this.capacityGuardThreshold) return false;
    // If the player is already assigned to this team, don't block
    if (currentChoice === team.teamId) return false;
    const tentative = this.tentativeRoster(team.teamId);
    return tentative >= max;
  }
  missingAssignments() {
    const players = this.selectedPlayers();
    const map = this.selectedTeams();
    return players.filter(p => !map[p.userId] || (Array.isArray(map[p.userId]) && map[p.userId].length === 0)).map(p => p.name);
  }
  trackPlayer = (_: number, p: { userId: string }) => p.userId;
  readOnlyTeams(playerId: string): string {
    const val = this.selectedTeams()[playerId];
    const all = this.teamService.filterByEligibility(null);
    const nameFor = (id: string) => all.find(t => t.teamId === id)?.teamName || id;
    if (!val) return '—';
    if (Array.isArray(val)) return val.map(nameFor).join(', ');
    return nameFor(val);
  }
}
