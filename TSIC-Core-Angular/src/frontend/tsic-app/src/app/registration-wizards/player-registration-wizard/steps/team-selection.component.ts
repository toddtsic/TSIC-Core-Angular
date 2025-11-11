import { Component, EventEmitter, Input, Output, computed, inject, CUSTOM_ELEMENTS_SCHEMA, ViewChildren, QueryList, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';
import { TeamService } from '../team.service';
import { DropDownListModule, MultiSelectModule, CheckBoxSelectionService, DropDownListComponent, MultiSelectComponent } from '@syncfusion/ej2-angular-dropdowns';

@Component({
  selector: 'app-rw-team-selection',
  standalone: true,
  // Use Syncfusion components for typeahead and checkbox MultiSelect UX
  imports: [CommonModule, FormsModule, DropDownListModule, MultiSelectModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  providers: [CheckBoxSelectionService],
  styles: [`
  :host ::ng-deep .rw-teams-ms .e-checkbox-wrapper { display:inline-flex;align-items:center;margin-right:.5rem;flex:0 0 auto; }
  :host ::ng-deep .rw-teams-ms .capacity-badge { font-size:.75rem; }
  /* Force popup list items into a flex row so checkbox + text + badge share one line */
  :host ::ng-deep .e-popup .e-list-item { display:flex !important; align-items:center; gap:.5rem; }
  :host ::ng-deep .e-popup .e-list-item .e-checkbox-wrapper { display:inline-flex; align-items:center; margin-right:.5rem; }
  :host ::ng-deep .e-popup .e-list-item .rw-item { flex:1 1 auto; display:flex; align-items:center; gap:.5rem; min-width:0; }
  /* Let name shrink for ellipsis, but don't grow to consume all free space */
  :host ::ng-deep .e-popup .e-list-item .rw-item .name { flex:0 1 auto; min-width:0; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  :host ::ng-deep .e-popup .e-list-item .rw-item .capacity-badge { margin-left:.4rem; font-size:.65rem; padding:0 .45rem; line-height:1.15; display:inline-flex; align-items:center; }
  :host ::ng-deep .e-popup { z-index:1200 !important; }
  /* Hide built-in truncated selected value tokens inside MultiSelect; we'll render our own list below */
  :host ::ng-deep .rw-teams-ms .e-delim-values { display:none !important; }
  :host ::ng-deep .rw-teams-ms .e-multi-select-wrapper { padding-top:.25rem; padding-bottom:.25rem; }
  /* Input aesthetic (neutral after removing temporary highlight) */
  :host ::ng-deep .rw-teams-single .e-input-group input.e-input,
  :host ::ng-deep .rw-teams-ms .e-multi-select-wrapper input.e-input { background: transparent !important; }
    /* Search icon & padding */
  /* Removed pseudo-element search icon due to build escape issue; can re-add later safely */
  :host ::ng-deep .rw-teams-ms .e-multi-select-wrapper input.e-input { padding-left: .75rem !important; }
  :host ::ng-deep .rw-teams-single .e-input-group input.e-input { padding-left: .75rem !important; }
    /* Placeholder clarity */
    :host ::ng-deep .rw-teams-ms .e-multi-select-wrapper input.e-input::placeholder,
    :host ::ng-deep .rw-teams-single .e-input-group input.e-input::placeholder { color: #555 !important; opacity: .75; }
  `],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Teams</h5>
      </div>
      <div class="card-body">
  <p class="text-secondary mb-3">{{ helperText() }}</p>
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
                        @if (isRegistered(p.userId)) {
                          <span class="badge bg-secondary" title="Already registered; team locked">Locked</span>
                        }
                      </div>
                      @if (showEligibilityBadge()) {
                        @if (!eligibilityFor(p.userId)) {
                          <div class="small text-muted">Eligibility not selected; go back to set it.</div>
                        } @else {
                          <div class="small">
                            Eligibility: <span class="badge bg-primary-subtle text-dark">{{ eligibilityFor(p.userId) }}</span>
                          </div>
                        }
                      }
                      <!-- Selected teams pills above dropdown; for registered players, fall back to prior registrations if selectedTeams is empty -->
                      <div class="small text-secondary mt-2">
                        <div class="fw-semibold mb-1">Selected teams</div>
                        <ul class="list-unstyled d-flex flex-wrap gap-2 m-0">
                          @for (id of selectedArrayFor(p.userId); track id) {
                            <li class="badge bg-primary-subtle text-dark border border-primary-subtle">
                              {{ nameForTeam(id) }}
                              @if (!isRegistered(p.userId)) {
                                <button type="button" class="btn btn-sm btn-link text-decoration-none ms-1 p-0 align-baseline"
                                        (click)="removeTeam(p.userId, id)">
                                  ×
                                </button>
                              }
                            </li>
                          }
                          @if (selectedArrayFor(p.userId).length === 0) {
                            <li class="text-muted">None</li>
                          }
                        </ul>
                      </div>
                    </div>
                    <div class="flex-grow-1">
                      @if (!multiSelect) {
                        <ejs-dropdownlist #ddRef [dataSource]="filteredTeamsFor(p.userId)"
                                          [fields]="syncFields"
                                          [placeholder]="'Search and select team...'"
                                          [allowFiltering]="true"
                                          [filterBarPlaceholder]="'Type to search teams...'"
                                          [filterType]="'Contains'"
                                          [popupHeight]="'320px'"
                                          [popupWidth]="'100%'"
                                          [zIndex]="1200"
                                          [cssClass]="'rw-teams-single'"
                                          [enabled]="!(isRegistered(p.userId) || (showEligibilityBadge() && !eligibilityFor(p.userId)) || filteredTeamsFor(p.userId).length===0)"
                                          [value]="selectedTeams()[p.userId] || null"
                                          (change)="onSyncSingleChange(p.userId, $event)">
                          <ng-template #itemTemplate let-data>
                            <span class="rw-item" [title]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 'Team is full and cannot be selected.' : ''">
                              <span class="name"
                                    [style.text-decoration]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 'line-through' : null"
                                    [style.opacity]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 0.6 : null">{{ data.teamName }}</span>
                              <span class="capacity-badge badge rounded-pill"
                                    [ngClass]="{ 'bg-danger-subtle text-danger-emphasis border border-danger-subtle': (data.rosterIsFull || baseRemaining(data.teamId) === 0),
                                                  'bg-warning-subtle text-warning-emphasis border border-warning-subtle': !(data.rosterIsFull || baseRemaining(data.teamId) === 0) }">
                                @if (data.rosterIsFull || baseRemaining(data.teamId) === 0) { FULL }
                                @else { {{ baseRemaining(data.teamId) }} spots left }
                              </span>
                              @if (data.rosterIsFull || baseRemaining(data.teamId) === 0) { <span class="text-danger ms-2 small">(Cannot select)</span> }
                            </span>
                          </ng-template>
                        </ejs-dropdownlist>
                      }
                      @if (filteredTeamsFor(p.userId).length===0 && eligibilityFor(p.userId) && showEligibilityBadge()) {
                        <div class="form-text">No teams match eligibility.</div>
                      }
                    </div>
                  </div>
                  @if (multiSelect) {
                    <div class="mt-2">
                      <ejs-multiselect #msRef [dataSource]="filteredTeamsFor(p.userId)"
                                       [fields]="syncFields"
                                       [mode]="'CheckBox'"
                                       [showDropDownIcon]="true"
                                       [showSelectAll]="true"
                                       [closePopupOnSelect]="false"
                                       [enableSelectionOrder]="true"
                                       [placeholder]="'Search and select teams...'"
                                       [allowFiltering]="true"
                                       [filterBarPlaceholder]="'Type to search teams...'"
                                       [filterType]="'Contains'"
                                       [popupHeight]="'320px'"
                                       [popupWidth]="'100%'"
                                       [zIndex]="1200"
                                       [cssClass]="'rw-teams-ms'"
                                       (filtering)="onFiltering($event)"
                                       (beforeSelect)="onMsBeforeSelect(p.userId, $event)"
                                       (select)="onMsSelect(p.userId, $event)"
                                       (removed)="onMsRemoved(p.userId, $event)"
                                       [enabled]="!(isRegistered(p.userId) || (showEligibilityBadge() && !eligibilityFor(p.userId)))"
                                       [value]="selectedArrayFor(p.userId)"
                                       (change)="onSyncMultiChange(p.userId, $event)">
                        <ng-template #itemTemplate let-data>
                          <span class="rw-item">
                            <span class="name"
                                  [style.text-decoration]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 'line-through' : null"
                                  [style.opacity]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 0.6 : null">{{ data.teamName }}</span>
                            <span class="capacity-badge badge rounded-pill"
                                  [ngStyle]="{ marginLeft: '.35rem', paddingLeft: '.35rem', paddingRight: '.35rem' }"
                                  [ngClass]="{ 'bg-danger-subtle text-danger-emphasis border border-danger-subtle': (data.rosterIsFull || baseRemaining(data.teamId) === 0),
                                                'bg-warning-subtle text-warning-emphasis border border-warning-subtle': !(data.rosterIsFull || baseRemaining(data.teamId) === 0) }">
                              @if (data.rosterIsFull || baseRemaining(data.teamId) === 0) { FULL }
                              @else { {{ baseRemaining(data.teamId) }} spots left }
                            </span>
                          </span>
                        </ng-template>
                      </ejs-multiselect>
                    </div>
                  }
                </div>
              }
            </div>
          }
          @if (missingAssignments().length && selectedPlayers().length > 0) {
            <div class="invalid-feedback d-block mt-2">Assign one or more teams for: {{ missingAssignments().join(', ') }}.</div>
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
  @ViewChildren('ddRef') private readonly ddLists!: QueryList<DropDownListComponent>;
  @ViewChildren('msRef') private readonly msLists!: QueryList<MultiSelectComponent>;
  private didAutoOpen = false;

  // expose signals for template
  loading = this.teamService.loading;
  error = this.teamService.error;
  selectedPlayers = () => this.wizard.familyPlayers().filter(p => p.selected || p.registered).map(p => ({ userId: p.playerId, name: `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim() }));
  selectedTeams = this.wizard.selectedTeams;
  eligibilityMap = this.wizard.eligibilityByPlayer;
  // readonly mode removed; selection is locked only for players with prior registrations
  // Syncfusion field mapping (disable selection for FULL teams)
  syncFields = { text: 'teamName', value: 'teamId', disabled: 'rosterIsFull' } as any;

  constructor() {
    // Auto-open the first unassigned player's selector once data is loaded and view is ready
    effect(() => {
      // Touch signals to subscribe
      this.teamService.loading();
      const fam = this.wizard.familyPlayers();
      if (this.teamService.loading() || this.didAutoOpen) return;
      if (!fam || fam.length === 0) return;
      // Small timeout to allow view children to render after data arrives
      queueMicrotask(() => this.tryOpenFirstUnassigned());
    });
  }

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
  private tryOpenFirstUnassigned() {
    if (this.didAutoOpen) return;
    const players = this.selectedPlayers();
    if (!players || players.length === 0) return;
    let idx = -1;
    for (let i = 0; i < players.length; i++) {
      const p = players[i];
      // Skip registered (locked)
      if (this.isRegistered(p.userId)) continue;
      // Skip if no eligible teams or disabled by eligibility flow
      const disabled = (this.showEligibilityBadge() && !this.eligibilityFor(p.userId)) || this.filteredTeamsFor(p.userId).length === 0;
      if (disabled) continue;
      // Needs selection?
      if (this.selectedArrayFor(p.userId).length === 0) { idx = i; break; }
    }
    if (idx === -1) return;
    // Defer to ensure QueryLists are populated
    setTimeout(() => {
      try {
        if (this.multiSelect) {
          const comp = this.msLists?.toArray?.()[idx];
          comp?.focusIn?.();
          comp?.showPopup?.();
        } else {
          const comp = this.ddLists?.toArray?.()[idx];
          comp?.focusIn?.();
          comp?.showPopup?.();
        }
        this.didAutoOpen = true;
      } catch { /* ignore */ }
    }, 0);
  }
  selectedArrayFor(playerId: string): string[] {
    // Prefer explicit selections; if none and player is registered, derive from prior registrations
    const val = this.selectedTeams()[playerId];
    let derived: string[] = [];
    if (!val && this.isRegistered(playerId)) {
      try {
        const fp = this.wizard.familyPlayers().find(p => p.playerId === playerId);
        const ids = (fp?.priorRegistrations || [])
          .map(r => r.assignedTeamId)
          .filter((id: any): id is string => typeof id === 'string' && !!id);
        // Deduplicate
        const uniq: string[] = [];
        for (const id of ids) if (!uniq.includes(id)) uniq.push(id);
        derived = uniq;
      } catch { /* ignore */ }
    }
    if (!val) return derived;
    return Array.isArray(val) ? val : [val];
  }
  showEligibilityBadge(): boolean {
    // Only show eligibility messaging when a constraint type was explicitly configured.
    return !!this.wizard.teamConstraintType();
  }
  helperText(): string {
    if (this.multiSelect) {
      return this.showEligibilityBadge()
        ? "Assign one or more teams to each player. Lists are filtered by the player's eligibility. Type to search by team name or year."
        : 'Assign one or more teams to each player. Type to search by team name or year.';
    }
    return this.showEligibilityBadge()
      ? "Assign a team to each player. Lists are filtered by the player's eligibility. Type to search by team name or year."
      : 'Assign a team to each player. Type to search by team name or year.';
  }
  eligibilityFor(playerId: string) {
    return this.wizard.getEligibilityForPlayer(playerId);
  }
  onSyncSingleChange(playerId: string, e: any) {
    // no readonly mode; only registered players are locked via isRegistered
    const val = e?.itemData?.teamId || e?.value || '';
    const current = { ...this.selectedTeams() } as Record<string, string | string[]>;
    if (val) current[playerId] = String(val); else delete current[playerId];
    this.wizard.selectedTeams.set(current);
  }
  // Adapter for native select change
  onSingleChange(playerId: string, value: any) {
    this.onSyncSingleChange(playerId, { value });
  }
  onSyncMultiChange(playerId: string, e: any) {
    // readonlyMode removed; only skip if player already registered
    if (this.isRegistered(playerId)) return;
    let values: string[] = Array.isArray(e?.value) ? e.value.map(String) : [];
    // If Syncfusion gives null/undefined fallback to empty array
    if (!values) values = [];
    // Capacity enforcement: drop selections that would exceed capacity
    const filtered = values.filter(id => {
      const all = this.teamService.filterByEligibility(null);
      const team = all.find(t => t.teamId === id);
      if (!team) return false;
      if (team.rosterIsFull) return false;
      return !this.wouldExceedCapacity(playerId, { teamId: id, maxRosterSize: team.maxRosterSize, rosterIsFull: team.rosterIsFull });
    });
    const before = this.selectedTeams()[playerId];
    let prevArr: string[] = [];
    if (Array.isArray(before)) {
      prevArr = before;
    } else if (before) {
      prevArr = [before];
    }
    const same = prevArr.length === filtered.length && prevArr.every(v => filtered.includes(v));
    if (same) return; // avoid redundant state updates that can trigger loops
    const map = { ...this.selectedTeams() } as Record<string, string | string[]>;
    if (filtered.length === 0) delete map[playerId]; else map[playerId] = filtered;
    this.wizard.selectedTeams.set(map as any);
  }
  onFiltering(e: any) {
    // No-op: using Syncfusion default filtering
  }
  onMsBeforeSelect(playerId: string, e: any) {
    // Cancel selection if FULL or would exceed capacity when near limits
    const id = e?.itemData?.teamId ?? e?.itemData?.value ?? e?.value;
    if (!id) return;
    const all = this.teamService.filterByEligibility(null);
    const team = all.find(t => t.teamId === id);
    if (!team) return;
    if (team.rosterIsFull || this.wouldExceedCapacity(playerId, { teamId: id, maxRosterSize: team.maxRosterSize, rosterIsFull: team.rosterIsFull })) {
      e.cancel = true;
    }
  }
  onMsSelect(playerId: string, e: any) {
    // Only registered players are locked via isRegistered
    const id = e?.itemData?.teamId ?? e?.itemData?.value ?? e?.value;
    if (!id) return;
    const current = this.selectedArrayFor(playerId);
    if (current.includes(id)) return;
    const next = [...current, String(id)];
    // Reuse change handler pathway for consistency
    this.onSyncMultiChange(playerId, { value: next });
  }
  onMsRemoved(playerId: string, e: any) {
    // Only registered players are locked via isRegistered
    const id = e?.itemData?.teamId ?? e?.itemData?.value ?? e?.value;
    if (!id) return;
    const current = this.selectedArrayFor(playerId);
    if (!current.includes(id)) return;
    const next = current.filter(x => x !== id);
    this.onSyncMultiChange(playerId, { value: next });
  }
  // Adapter for native multi-select change
  onMultiChange(playerId: string, event: Event) {
    const target = event.target as HTMLSelectElement;
    if (!target) return;
    const vals: string[] = Array.from(target.selectedOptions).map(o => String(o.value));
    this.onSyncMultiChange(playerId, { value: vals });
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
  nameForTeam(id: string): string {
    const all = this.teamService.filterByEligibility(null);
    return all.find(t => t.teamId === id)?.teamName || id;
  }
  removeTeam(playerId: string, teamId: string) {
    // Only registered players are locked via isRegistered
    const current = this.selectedArrayFor(playerId).filter(x => x !== teamId);
    this.onSyncMultiChange(playerId, { value: current });
  }
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
