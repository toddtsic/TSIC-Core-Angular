import { Component, EventEmitter, Input, Output, computed, inject, CUSTOM_ELEMENTS_SCHEMA, ViewChildren, QueryList, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';
import { PlayerStateService } from '../services/player-state.service';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
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
              @for (p of selectedPlayers(); track p.userId; let idx = $index) {
                <div class="player-team-row p-3 rounded" style="border-width: 1px; border-style: solid;" [ngClass]="colorClassForIndex(idx)">
                  <div class="d-flex flex-column flex-md-row justify-content-between gap-3">
                    <div class="flex-grow-1">
                      <div class="fw-semibold mb-1 d-flex align-items-center gap-2">
                        <span class="badge rounded-pill px-3 py-2" [ngClass]="textColorClassForIndex(idx)">{{ p.name }}</span>
                        @if (isPlayerFullyLocked(p.userId)) {
                          <span class="badge bg-secondary" title="All prior registrations are paid; team changes may be limited by the director">Locked</span>
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
                              <span class="name">{{ nameForTeam(id) }} @if (priceForTeam(id) != null) { ({{ priceForTeam(id) | currency }}) }</span>
                              @if (canRemoveTeam(p.userId, id)) {
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
                        @if (isCAC()) {
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
                                            [enabled]="!(showEligibilityBadge() && !eligibilityFor(p.userId)) && filteredTeamsFor(p.userId).length>0 && !(isRegistered(p.userId) && !hasAlternativeOpenTeam(p.userId))"
                                            [value]="selectedTeams()[p.userId] || null"
                                            [valueTemplate]="singleValueTemplate"
                                            [itemTemplate]="singleItemTemplate"
                                            (change)="onSyncSingleChange(p.userId, $event)">
                            <ng-template #singleValueTemplate let-data>
                              <span class="rw-item">
                                <span class="name">{{ data?.teamName }} @if (data?.perRegistrantFee != null) { ({{ data?.perRegistrantFee | currency }}) }</span>
                              </span>
                            </ng-template>
                            <ng-template #singleItemTemplate let-data>
                              <span class="rw-item" [title]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 'Team is full and cannot be selected.' : ''">
                                <span class="name"
                                      [style.text-decoration]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 'line-through' : null"
                                      [style.opacity]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 0.6 : null">{{ data.teamName }} @if (data.perRegistrantFee != null) { ({{ data.perRegistrantFee | currency }}) }</span>
                                <span class="capacity-badge badge rounded-pill"
                                      [ngClass]="{ 'bg-danger-subtle text-danger-emphasis border border-danger-subtle': (data.rosterIsFull || baseRemaining(data.teamId) === 0),
                                                    'bg-warning-subtle text-warning-emphasis border border-warning-subtle': !(data.rosterIsFull || baseRemaining(data.teamId) === 0) }">
                                  @if (data.rosterIsFull || baseRemaining(data.teamId) === 0) { FULL }
                                  @else {
                                    @if (showRemaining(data.teamId)) { {{ baseRemaining(data.teamId) }} spots left }
                                  }
                                </span>
                                @if (data.rosterIsFull || baseRemaining(data.teamId) === 0) { <span class="text-danger ms-2 small">(Cannot select)</span> }
                              </span>
                            </ng-template>
                          </ejs-dropdownlist>
                        } @else {
                          <!-- PP fallback: native select without typeahead -->
                          <select class="form-select" [ngModel]="selectedTeams()[p.userId] || ''"
                                  (ngModelChange)="onNativeSingleChange(p.userId, $event)"
                                  [disabled]="!(showEligibilityBadge() && !eligibilityFor(p.userId)) && filteredTeamsFor(p.userId).length>0 && !(isRegistered(p.userId) && !hasAlternativeOpenTeam(p.userId)) ? false : true">
                            <option value="" disabled>Select team...</option>
                            @for (t of filteredTeamsFor(p.userId); track t.teamId) {
                              <option [value]="t.teamId" [disabled]="t.rosterIsFull || baseRemaining(t.teamId)===0">
                                {{ t.teamName }}
                                @if (t.perRegistrantFee != null) { ({{ t.perRegistrantFee | currency }}) }
                                @if (t.rosterIsFull || baseRemaining(t.teamId)===0) { - FULL }
                              </option>
                            }
                          </select>
                        }
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
                                       [valueTemplate]="multiValueTemplate"
                                       [itemTemplate]="multiItemTemplate"
                                       (filtering)="onFiltering($event)"
                                       (beforeSelect)="onMsBeforeSelect(p.userId, $event)"
                                       (select)="onMsSelect(p.userId, $event)"
                                       (removed)="onMsRemoved(p.userId, $event)"
                                       [enabled]="!(showEligibilityBadge() && !eligibilityFor(p.userId)) && !(isRegistered(p.userId) && !hasAlternativeOpenTeam(p.userId))"
                                       [value]="selectedArrayFor(p.userId)"
                                       (change)="onSyncMultiChange(p.userId, $event)">
                        <!-- Selected chip/value template to also display price when present -->
                        <ng-template #multiValueTemplate let-data>
                          <span class="name">{{ data?.teamName }} @if (data?.perRegistrantFee != null) { ({{ data?.perRegistrantFee | currency }}) }</span>
                        </ng-template>
                        <ng-template #multiItemTemplate let-data>
                          <span class="rw-item">
                            <span class="name"
                                  [style.text-decoration]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 'line-through' : null"
                                  [style.opacity]="(data.rosterIsFull || baseRemaining(data.teamId) === 0) ? 0.6 : null">{{ data.teamName }} @if (data.perRegistrantFee != null) { ({{ data.perRegistrantFee | currency }}) }</span>
                            <span class="capacity-badge badge rounded-pill"
                                  [ngStyle]="{ marginLeft: '.35rem', paddingLeft: '.35rem', paddingRight: '.35rem' }"
                                  [ngClass]="{ 'bg-danger-subtle text-danger-emphasis border border-danger-subtle': (data.rosterIsFull || baseRemaining(data.teamId) === 0),
                                                'bg-warning-subtle text-warning-emphasis border border-warning-subtle': !(data.rosterIsFull || baseRemaining(data.teamId) === 0) }">
                              @if (data.rosterIsFull || baseRemaining(data.teamId) === 0) { FULL }
                              @else { @if (showRemaining(data.teamId)) { {{ baseRemaining(data.teamId) }} spots left } }
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
  private readonly playerState: PlayerStateService = inject(PlayerStateService);
  private readonly teamService: TeamService = inject(TeamService);
  private readonly profileMigration = inject(ProfileMigrationService);
  @ViewChildren('ddRef') private readonly ddLists!: QueryList<DropDownListComponent>;
  @ViewChildren('msRef') private readonly msLists!: QueryList<MultiSelectComponent>;
  private didAutoOpen = false;

  // expose signals for template
  loading = this.teamService.loading;
  error = this.teamService.error;
  selectedPlayers = () => this.wizard.familyPlayers().filter(p => p.selected || p.registered).map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
  selectedTeams = () => this.playerState.selectedTeams();
  // readonly mode removed; selection is locked only for players with prior registrations
  // Syncfusion field mapping (disable selection for FULL teams)
  syncFields = { text: 'teamName', value: 'teamId', disabled: 'rosterIsFull' } as any;

  constructor() {
    // Auto-open the first unassigned player's selector once data is loaded and view is ready
    effect(() => {
      // Touch signals to subscribe
      this.teamService.loading();
      const fam = this.wizard.familyPlayers();
      if (this.teamService.loading()) return;
      if (!fam || fam.length === 0) return;
      // New: if a player has exactly one selectable team, auto-select it
      this.ensureAutoSelectSingleOptions();
      // Then, if nothing is selected for the first unassigned player, auto-open their selector once
      if (this.didAutoOpen) return;
      // Small timeout to allow view children to render after data arrives
      queueMicrotask(() => this.tryOpenFirstUnassigned());
    });
  }

  isRegistered(playerId: string): boolean {
    try {
      return !!this.wizard.familyPlayers().find(p => p.playerId === playerId)?.registered;
    } catch { return false; }
  }

  /** True only when every prior registration for the player has a positive paidTotal (fully paid set). */
  isPlayerFullyLocked(playerId: string): boolean {
    try {
      const fp = this.wizard.familyPlayers().find(p => p.playerId === playerId);
      if (!fp?.priorRegistrations?.length) return false;
      return fp.priorRegistrations.every(r => (r.financials?.paidTotal ?? 0) > 0);
    } catch { return false; }
  }

  /** Determine if a team pill can be removed (unpaid registration or in-session selection). */
  canRemoveTeam(playerId: string, teamId: string): boolean {
    try {
      // If the player isn't registered at all, always removable (in-session only)
      if (!this.isRegistered(playerId)) return true;
      const fp = this.wizard.familyPlayers().find(p => p.playerId === playerId);
      if (!fp) return true;
      // In-session selections (not yet part of priorRegistrations) are removable
      const prior = (fp.priorRegistrations || []).filter(r => !!r.assignedTeamId);
      const isPrior = prior.some(r => r.assignedTeamId === teamId);
      if (!isPrior) return true;
      // Prior registration: allow removal only if unpaid
      const reg = prior.find(r => r.assignedTeamId === teamId);
      const paid = (reg?.financials?.paidTotal ?? 0) > 0;
      return !paid; // removable when not paid
    } catch { return false; }
  }

  allPlayersAssigned = computed(() => {
    const players = this.selectedPlayers();
    const map = this.selectedTeams();
    if (players.length === 0) return false;
    return players.every(p => !!map[p.userId]);
  });

  filteredTeamsFor(playerId: string) {
    const elig = this.playerState.getEligibilityForPlayer(playerId);
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
  // Determine registration model: prefer server profileType prefix (CAC*), fallback to multiSelect heuristic
  isCAC = computed(() => {
    try {
      const pt = this.profileMigration.currentJobProfileConfig()?.profileType || '';
      if (pt) return pt.toUpperCase().startsWith('CAC');
      return !!this.multiSelect; // heuristic fallback
    } catch { return !!this.multiSelect; }
  });
  // Native select fallback for PP (non-CAC) single-select mode
  onNativeSingleChange(playerId: string, value: any) {
    const val = (value == null || value === '') ? '' : String(value);
    const current = { ...this.selectedTeams() } as Record<string, string | string[]>;
    if (val) current[playerId] = val; else delete current[playerId];
    this.playerState.setSelectedTeams(current);
  }
  eligibilityFor(playerId: string) {
    return this.playerState.getEligibilityForPlayer(playerId);
  }
  onSyncSingleChange(playerId: string, e: any) {
    // no readonly mode; only registered players are locked via isRegistered
    const val = e?.itemData?.teamId || e?.value || '';
    const current = { ...this.selectedTeams() } as Record<string, string | string[]>;
    if (val) current[playerId] = String(val); else delete current[playerId];
    this.playerState.setSelectedTeams(current);
  }
  // Adapter for native select change
  onSingleChange(playerId: string, value: any) { /* legacy adapter unused in template */ }
  onSyncMultiChange(playerId: string, e: any) {
    // Allow changes even for previously registered players; server enforces paid/locked rules.
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
    this.playerState.setSelectedTeams(map as any);
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
  onMultiChange(playerId: string, event: Event) { /* legacy adapter unused in template */ }
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
  /** Determine if there is any alternative team (same age group) with remaining capacity besides the player's current selection. */
  hasAlternativeOpenTeam(playerId: string): boolean {
    try {
      // Derive the player's current team (single-select scenario) from selectedArrayFor
      const currentIds = this.selectedArrayFor(playerId);
      const currentId = currentIds.length > 0 ? currentIds[0] : null;
      if (!currentId) return false; // No current selection → treat as locked when registered
      const allEligible = this.filteredTeamsFor(playerId) || [];
      const currentTeam = allEligible.find(t => t.teamId === currentId);
      if (!currentTeam) return false;
      // Look for another team in same age group with capacity
      for (const t of allEligible) {
        if (t.teamId === currentId) continue; // skip current
        if (t.agegroupId !== currentTeam.agegroupId) continue; // must match age group
        if (t.rosterIsFull || this.baseRemaining(t.teamId) === 0) continue; // must have capacity
        return true; // Found an alternative open team
      }
      return false; // none found
    } catch { return false; }
  }
  nameForTeam(id: string): string {
    const all = this.teamService.filterByEligibility(null);
    return all.find(t => t.teamId === id)?.teamName || id;
  }
  priceForTeam(id: string): number | null {
    const all = this.teamService.filterByEligibility(null);
    const fee = all.find(t => t.teamId === id)?.perRegistrantFee as number | undefined;
    return fee ?? null;
  }
  removeTeam(playerId: string, teamId: string) {
    // Only registered players are locked via isRegistered
    const current = this.selectedArrayFor(playerId).filter(x => x !== teamId);
    this.onSyncMultiChange(playerId, { value: current });
  }
  /**
   * When a player has exactly one selectable eligible team, pre-select it.
   * A team is considered selectable when it's not full and has base remaining capacity > 0.
   */
  private ensureAutoSelectSingleOptions(): void {
    try {
      const players = this.selectedPlayers();
      if (!players || players.length === 0) return;
      const map = { ...this.selectedTeams() } as Record<string, string | string[]>;
      let changed = false;
      for (const p of players) {
        // Skip if there's already a selection (explicit or derived from prior registrations)
        if (this.selectedArrayFor(p.userId).length > 0) continue;
        const teams = this.filteredTeamsFor(p.userId) || [];
        // Filter to teams that are actually selectable
        const selectable = teams.filter((t: any) => !t.rosterIsFull && this.baseRemaining(t.teamId) > 0);
        if (selectable.length === 1) {
          const t = selectable[0];
          // Guard against exceeding capacity when near limits
          if (this.wouldExceedCapacity(p.userId, { teamId: t.teamId, maxRosterSize: t.maxRosterSize, rosterIsFull: t.rosterIsFull })) {
            continue;
          }
          map[p.userId] = this.multiSelect ? [String(t.teamId)] : String(t.teamId);
          changed = true;
        }
      }
      if (changed) this.playerState.setSelectedTeams(map as any);
    } catch { /* no-op */ }
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

  // Deterministic color per player index
  colorClassForIndex(idx: number): string {
    const palette = ['bg-primary-subtle border-primary-subtle', 'bg-success-subtle border-success-subtle', 'bg-info-subtle border-info-subtle', 'bg-warning-subtle border-warning-subtle', 'bg-secondary-subtle border-secondary-subtle'];
    return palette[idx % palette.length];
  }

  // Coordinated text badge color per player index
  textColorClassForIndex(idx: number): string {
    const palette = ['bg-primary-subtle text-primary-emphasis border border-primary-subtle', 'bg-success-subtle text-success-emphasis border border-success-subtle', 'bg-info-subtle text-info-emphasis border border-info-subtle', 'bg-warning-subtle text-warning-emphasis border border-warning-subtle', 'bg-secondary-subtle text-secondary-emphasis border border-secondary-subtle'];
    return palette[idx % palette.length];
  }
}
