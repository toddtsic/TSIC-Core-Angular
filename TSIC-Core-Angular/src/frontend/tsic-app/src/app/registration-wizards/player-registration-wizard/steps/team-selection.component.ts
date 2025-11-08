import { Component, EventEmitter, Output, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { TeamService } from '../team.service';

@Component({
  selector: 'app-rw-team-selection',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Teams</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Assign a team to each player. Team lists are filtered by that player's eligibility selection.</p>
        <ng-container *ngIf="loading(); else bodyBlock">
          <div class="text-muted small">Loading teams...</div>
        </ng-container>
        <ng-template #bodyBlock>
          <div *ngIf="error()" class="alert alert-danger py-2 px-3">{{ error() }}</div>
          <div *ngIf="selectedPlayers().length === 0" class="alert alert-info">Select players first to assign teams.</div>
          <div class="vstack gap-3" *ngIf="selectedPlayers().length > 0">
            <div class="player-team-row p-3 border rounded" *ngFor="let p of selectedPlayers(); trackBy: trackPlayer">
              <div class="d-flex flex-column flex-md-row justify-content-between gap-3">
                <div class="flex-grow-1">
                  <div class="fw-semibold mb-1">{{ p.name }}</div>
                  <div class="small text-muted" *ngIf="!eligibilityFor(p.userId)">Eligibility not selected; go back to set it.</div>
                  <div class="small" *ngIf="eligibilityFor(p.userId)">
                    Eligibility: <span class="badge bg-primary-subtle text-dark">{{ eligibilityFor(p.userId) }}</span>
                  </div>
                </div>
                <div class="flex-grow-1">
                  <label class="form-label small fw-semibold">Team</label>
                  <select class="form-select" [disabled]="!eligibilityFor(p.userId) || filteredTeamsFor(p.userId).length===0" (change)="onTeamSelect(p.userId, $event)">
                    <option value="">Select team</option>
                    <ng-container *ngFor="let t of filteredTeamsFor(p.userId)">
                      <option [value]="t.teamId" [disabled]="t.rosterIsFull">{{ t.teamName }} ({{ t.currentRosterSize }}/{{ t.maxRosterSize }}{{ t.rosterIsFull ? ' FULL' : '' }})</option>
                    </ng-container>
                  </select>
                  <div class="form-text" *ngIf="filteredTeamsFor(p.userId).length===0 && eligibilityFor(p.userId)">
                    No teams match eligibility.
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div *ngIf="missingAssignments().length && selectedPlayers().length > 0" class="invalid-feedback d-block mt-2">
            Assign a team for: {{ missingAssignments().join(', ') }}.
          </div>
        </ng-template>
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
    const target = ev.target as HTMLSelectElement | null;
    if (!target) return;
    const val = target.value || '';
    const current = { ...this.selectedTeams() };
    if (val) current[playerId] = val; else delete current[playerId];
    this.wizard.selectedTeams.set(current);
  }
  missingAssignments() {
    const players = this.selectedPlayers();
    const map = this.selectedTeams();
    return players.filter(p => !map[p.userId]).map(p => p.name);
  }
  trackPlayer = (_: number, p: { userId: string }) => p.userId;
}
