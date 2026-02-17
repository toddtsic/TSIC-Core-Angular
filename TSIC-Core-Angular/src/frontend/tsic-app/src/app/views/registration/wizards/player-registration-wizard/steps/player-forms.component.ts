import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject } from '@angular/core';
import { NgClass, CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService, PlayerProfileFieldSchema } from '../registration-wizard.service';
import type { PreSubmitValidationErrorDto } from '@core/api';
import { UsLaxService } from '../uslax.service';
import { TeamService } from '../team.service';
import { UsLaxValidatorDirective } from '../uslax-validator.directive';
import { WizardModalComponent } from '../../shared/wizard-modal/wizard-modal.component';
import { colorClassForIndex, textColorClassForIndex } from '../../shared/utils/color-class.util';

@Component({
  selector: 'app-rw-player-forms',
  standalone: true,
  imports: [NgClass, CurrencyPipe, FormsModule, UsLaxValidatorDirective, WizardModalComponent],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Player Forms</h5>
        <!-- Server-side validation summary (appears after a PreSubmit attempt that returned errors) -->
        @if (serverErrors().length) {
          <div class="alert alert-danger mt-3 mb-0" role="alert">
            <div class="fw-semibold mb-1">Please fix the following before continuing:</div>
            <ul class="mb-0 ps-3">
              @for (err of serverErrors(); track trackErr($index, err)) {
                <li>
                  <strong>{{ nameForPlayer(err.playerId) }}</strong> – <span class="text-nowrap">{{ err.field }}</span>: {{ err.message }}
                </li>
              }
            </ul>
          </div>
        }
        <div class="mt-2">
          <div class="fw-semibold mb-1">Selected teams</div>
          <ul class="list-unstyled d-flex flex-wrap gap-2 m-0">
            @for (id of selectedPlayersWithTeams[0]?.teamIds ?? []; track id) {
              <li class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle">
                <span class="name">{{ nameForTeam(id) }} @if (priceForTeam(id) != null) { ({{ priceForTeam(id) | currency }}) }</span>
              </li>
            }
            @if (!(selectedPlayersWithTeams[0]?.teamIds?.length)) {
              <li class="text-muted">None</li>
            }
          </ul>
        </div>
      </div>
      <div class="card-body">
        @for (player of selectedPlayersWithTeams; track player.userId; let i = $index) {
          <div class="mb-4">
            <div class="card card-rounded shadow-sm" style="border-width: 1px; border-style: solid;">
              <div class="card-header border-bottom-0" [ngClass]="colorClassForIndex(i)">
                <div class="d-flex align-items-center justify-content-between">
                  <div class="d-flex align-items-center gap-2">
                    <span class="badge rounded-pill px-3 py-2" [ngClass]="textColorClassForIndex(i)">
                      {{ player.name }}
                    </span>
                    @if (isRegistered(player.userId)) {
                      <span class="badge bg-success">Registered</span>
                    }
                  </div>
                  <!-- Remove duplicate team pills here -->
                </div>
              </div>
              <div class="card-body" [ngClass]="colorClassForIndex(i)">
                <!-- Per-player compact required-fields summary -->
                @if (missingRequiredLabels(player.userId).length > 0) {
                  <div class="alert alert-warning border-0 py-1 px-2 mb-2 req-mini" role="alert">
                    <div class="title">Please complete:</div>
                    <ul>
                      @for (label of missingRequiredLabels(player.userId); track label) {
                        <li>{{ label }}</li>
                      }
                    </ul>
                  </div>
                }
                
                <!-- Only show the first USA Lacrosse # field per player -->
                @let usLaxField = firstUsLaxField();
                @if (usLaxField) {
                  <div class="mb-2">
                    <div class="uslax-field-group">
                      <label class="form-label small mb-1 d-flex align-items-center gap-2" [for]="helpId(player.userId, usLaxField.name)">
                        <span>{{ usLaxField.label || 'USA Lacrosse Number' }}</span>
                        @if (usLaxField.required) {
                          @if (isFieldFilled(player.userId, usLaxField.name)) {
                            <span class="text-success d-inline-flex align-items-center" aria-label="Completed">
                              <i class="bi bi-check-circle-fill" aria-hidden="true" style="font-size: 1rem; line-height: 1;"></i>
                            </span>
                          } @else {
                            <span class="badge bg-danger text-white">Required</span>
                          }
                        }
                      </label>
                      <input type="text" class="form-control form-control-sm"
                             #uslax="ngModel"
                             [required]="usLaxField.required"
                             [attr.aria-required]="usLaxField.required || null"
                             [id]="helpId(player.userId, usLaxField.name)"
                             [ngModel]="value(player.userId, usLaxField.name)"
                             (ngModelChange)="setValue(player.userId, usLaxField.name, $event)"
                             [usLaxValidator]="player.userId"
                             placeholder="Enter USA Lacrosse #" />
                      <div class="mt-1 small">
                        @if (uslax?.pending) {
                          <span class="text-muted">Validating...</span>
                        } @else if (uslax?.invalid && (uslax?.dirty || uslax?.touched)) {
                          @if (usLaxStatus(player.userId).membership !== undefined) {
                            <button type="button"
                                    class="btn btn-warning btn-sm fw-semibold mt-2 mb-2 pt-1 pb-1 px-3 me-2 shadow-sm"
                                    (click)="openUsLaxDetails(player.userId)">
                              Click here for error details
                            </button>
                          }
                          @if (isGenericGuidance(uslax?.errors?.['uslax']?.message)) {
                            <div class="card border-0 mb-2 mt-2" role="alert" aria-live="polite" style="border-left: 4px solid var(--bs-warning);">
                              <div class="card-body py-3 px-3 bg-danger-subtle rounded">
                                <div class="fw-semibold text-danger-emphasis mb-1">USA Lacrosse guidance</div>
                                <span class="text-danger-emphasis small d-block" [innerHTML]="uslax?.errors?.['uslax']?.message"></span>
                              </div>
                            </div>
                          } @else {
                            <span class="text-danger d-inline-block" [innerHTML]="uslax?.errors?.['uslax']?.message || 'Invalid number'"></span>
                          }
                        } @else if ((value(player.userId, usLaxField.name) || '').toString().trim().length > 0) {
                          <span class="text-success">Valid membership ✔</span>
                        } @else {
                          <span class="text-muted">Type or edit value to validate automatically.</span>
                        }
                      </div>
                    </div>
                    @if (usLaxField.helpText) {
                      <div class="form-text" [id]="helpId(player.userId, usLaxField.name)">{{ usLaxField.helpText }}</div>
                    }
                  </div>
                }

                <!-- Render all other visible, non-waiver fields with labels -->
                <div class="row g-2">
                @for (field of schemas(); track trackField($index, field)) {
                  @if (!isUsLaxField(field) && isFieldVisible(player.userId, field)) {
                    <div class="col-12 col-md-6">
                      <label class="form-label small mb-1 d-flex align-items-center gap-2" [for]="helpId(player.userId, field.name)">
                        <span>{{ field.label }}</span>
                        @if (field.required) {
                          @if (isFieldFilled(player.userId, field.name)) {
                            <span class="text-success d-inline-flex align-items-center" aria-label="Completed">
                              <i class="bi bi-check-circle-fill" aria-hidden="true" style="font-size: 1rem; line-height: 1;"></i>
                            </span>
                          } @else {
                            <span class="badge bg-danger text-white">Required</span>
                          }
                        } @else { <span class="badge bg-body-secondary text-body-secondary border">Optional</span> }
                      </label>
                      @switch (field.type) {
                        @case ('text') {
           <input type="text" class="form-control form-control-sm"
                                 [id]="helpId(player.userId, field.name)"
                                 [required]="field.required"
                                 [attr.aria-required]="field.required || null"
                                 [attr.aria-invalid]="isFieldInvalid(player.userId, field.name) || null"
                                 autocomplete="off"
                                 [ngModel]="value(player.userId, field.name)"
                                 (ngModelChange)="setValue(player.userId, field.name, $event)" />
                        }
                        @case ('number') {
           <input type="number" class="form-control form-control-sm"
                                 [id]="helpId(player.userId, field.name)"
                                 [required]="field.required"
                                 [attr.aria-required]="field.required || null"
                                 [attr.aria-invalid]="isFieldInvalid(player.userId, field.name) || null"
                                 inputmode="numeric"
                                 [ngModel]="value(player.userId, field.name)"
                                 (ngModelChange)="setValue(player.userId, field.name, $event)" />
                        }
                        @case ('date') {
           <input type="date" class="form-control form-control-sm"
                                 [id]="helpId(player.userId, field.name)"
                                 [required]="field.required"
                                 [attr.aria-required]="field.required || null"
                                 [attr.aria-invalid]="isFieldInvalid(player.userId, field.name) || null"
                                 [ngModel]="value(player.userId, field.name)"
                                 (ngModelChange)="setValue(player.userId, field.name, $event)" />
                        }
                        @case ('select') {
        <select class="form-select form-select-sm"
                                  [id]="helpId(player.userId, field.name)"
                                  [required]="field.required"
                                  [attr.aria-required]="field.required || null"
                                  [attr.aria-invalid]="isFieldInvalid(player.userId, field.name) || null"
                                  [ngModel]="value(player.userId, field.name)"
                                  (ngModelChange)="setValue(player.userId, field.name, $event)">
                            <option [ngValue]="null">-- Select {{ field.label }} --</option>
                            @for (opt of field.options; track trackOpt($index, opt)) {
                              <option [ngValue]="opt">{{ opt }}</option>
                            }
                          </select>
                        }
                        @case ('multiselect') {
                          <div class="d-flex flex-wrap gap-2">
                            @for (opt of field.options; track trackOpt($index, opt)) {
                              <div class="form-check me-3">
                                <input class="form-check-input" type="checkbox"
                                       [id]="helpId(player.userId, field.name) + '-' + opt"
                                       [checked]="isMultiChecked(player.userId, field.name, opt)"
                                       (change)="toggleMulti(player.userId, field.name, opt, $event)" />
                                <label class="form-check-label" [for]="helpId(player.userId, field.name) + '-' + opt">{{ opt }}</label>
                              </div>
                            }
                          </div>
                        }
                        @case ('checkbox') {
                          <div class="form-check d-flex align-items-center gap-2">
                            <input class="form-check-input" type="checkbox"
                                   [id]="helpId(player.userId, field.name)"
                                   [checked]="!!value(player.userId, field.name)"
                                   (change)="onCheckboxChange(player.userId, field.name, $event)" />
                            <label class="form-check-label" [for]="helpId(player.userId, field.name)">{{ field.label }}</label>
                            @if (field.required) {
                              @if (isFieldFilled(player.userId, field.name)) {
                                <span class="text-success d-inline-flex align-items-center" aria-label="Completed">
                                  <i class="bi bi-check-circle-fill" aria-hidden="true" style="font-size: 1rem; line-height: 1;"></i>
                                </span>
                              } @else {
                                <span class="badge bg-danger text-white">Required</span>
                              }
                            }
                          </div>
                        }
                        @default {
                          <input type="text" class="form-control form-control-sm"
                                 [id]="helpId(player.userId, field.name)"
                                 [ngModel]="value(player.userId, field.name)"
                                 (ngModelChange)="setValue(player.userId, field.name, $event)" />
                        }
                      }
                      @if (field.helpText) {
                        <div class="form-text">{{ field.helpText }}</div>
                      }
                    </div>
                  }
                }
                </div>
              </div>
            </div>
          </div>
        }
        
      </div>
    </div>

@if (modalOpen) {
  <app-wizard-modal title="USA Lacrosse API Details" size="md" (closed)="closeModal()">
    <div modal-body>
      <p class="small text-muted mb-2">Your membership didn't validate. Recommend calling USA Lacrosse at call 410-235-6882. Please share the following information with them.</p>
      @if (modalData) {
        <div class="table-responsive mb-3">
          <table class="table table-sm mb-0 align-middle">
            <tbody>
              <tr><th class="text-nowrap">Member #</th><td>{{ modalData?.membernumber || modalData?.mem_number || '—' }}</td></tr>
              <tr><th class="text-nowrap">First Name</th><td>{{ modalData?.firstname || '—' }}</td></tr>
              <tr><th class="text-nowrap">Last Name</th><td>{{ modalData?.lastname || '—' }}</td></tr>
              <tr><th class="text-nowrap">DOB</th><td>{{ modalData?.birthdate || '—' }}</td></tr>
              <tr><th class="text-nowrap">Status</th><td>{{ modalData?.mem_status || '—' }}</td></tr>
              <tr><th class="text-nowrap">Expires</th><td>{{ modalData?.exp_date || '—' }}</td></tr>
            </tbody>
          </table>
        </div>
        <div class="mb-2 d-flex gap-2">
          <button type="button" class="btn btn-sm btn-outline-primary" (click)="copyModalJson()">Copy JSON</button>
        </div>
        <pre class="bg-body-secondary border rounded p-2 small" style="max-height: 320px; overflow: auto">{{ prettyJson(modalData) }}</pre>
      } @else {
        <div class="alert alert-warning small">No membership details were returned by the API.</div>
      }
    </div>
    <div modal-footer>
      <button type="button" class="btn btn-secondary btn-sm" (click)="closeModal()">Done</button>
    </div>
  </app-wizard-modal>
}
  `,
  styles: [
    `
    /* Compact per-player required list */
    .req-mini { font-size: .85rem; line-height: 1.1; }
    .req-mini .title { font-weight: 600; margin-bottom: .15rem; }
    .req-mini ul { margin: 0; padding-left: 1rem; }
    .req-mini li { margin: 0; }
    `
  ],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class PlayerFormsComponent {
  /** Returns selected players (from familyPlayers flags) with teamIds property for pill rendering. */
  get selectedPlayersWithTeams() {
    const list = this.state.familyPlayers().filter(p => p.selected || p.registered);
    return list.map(p => ({
      userId: p.playerId,
      name: `${p.firstName} ${p.lastName}`.trim(),
      teamIds: (() => {
        // When eligibility step exists we show team selections elsewhere (Teams step), keep pills minimal here.
        // When no eligibility step, Teams tab is skipped and team selection happens here in Forms; still expose current selections as pills.
        const sel = this.state.selectedTeams()[p.playerId];
        if (!sel) return [];
        return Array.isArray(sel) ? sel : [sel];
      })(),
      // Flag indicating we should render inline team selection controls for this player (only when Teams step skipped)
      inlineTeams: false // Inline team selection disabled; Teams tab always present
    }));
  }
  /** Returns an array of team IDs for a player, for pill rendering. */
  teamIds(playerId: string): string[] {
    const sel = this.state.selectedTeams()[playerId];
    if (!sel) return [];
    return Array.isArray(sel) ? sel : [sel];
  }
  /** Returns registrationId for a player if present in familyPlayers. */
  getRegistrationId(playerId: string): string | null {
    const p = this.state.familyPlayers().find(fp => fp.playerId === playerId);
    if (!p) return null;
    // Return first prior registration's ID if present (active preferred)
    const activeFirst = [...(p.priorRegistrations || [])].sort(r => r.active ? -1 : 1);
    return activeFirst.length ? (activeFirst[0].registrationId ?? null) : null;
  }
  /**
   * Lookup a team by name from the TeamService filtered list.
   * Used for TeamId select field feedback (full/disabled logic).
   */
  getTeam(teamId: string) {
    return this.teams.filterByEligibility(null).find(t => t.teamId === teamId);
  }
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  public readonly state = inject(RegistrationWizardService);
  /** Returns true if the wizard is in readonly mode (edit mode or any registered player selected). */
  public readonlyMode(): boolean {
    // Readonly only when any selected or registered player is registered (edit scenario)
    return this.state.familyPlayers().some(p => (p.selected || p.registered) && p.registered);
  }

  /** Returns true if the player is registered. */
  public isRegistered(playerId: string): boolean {
    return !!this.state.familyPlayers().find(p => p.playerId === playerId)?.registered;
  }
  private readonly teams = inject(TeamService);
  private readonly uslax = inject(UsLaxService);

  schemas = () => this.state.profileFieldSchemas();
  selectedPlayers = () => this.state.familyPlayers().filter(p => p.selected || p.registered).map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
  jobId = () => this.state.jobId();
  jobPath = () => this.state.jobPath();
  colorClassForIndex = colorClassForIndex;
  textColorClassForIndex = textColorClassForIndex;

  value(playerId: string, field: string) { return this.state.getPlayerFieldValue(playerId, field); }
  setValue(playerId: string, field: string, val: any) { this.state.setPlayerFieldValue(playerId, field, val); }
  isMultiChecked(playerId: string, field: string, opt: string) {
    const v = this.value(playerId, field);
    return Array.isArray(v) && v.includes(opt);
  }
  toggleMulti(playerId: string, field: string, opt: string, ev: Event) {
    const checked = (ev.target as HTMLInputElement).checked;
    let v = this.value(playerId, field);
    if (!Array.isArray(v)) v = [];
    const arr = [...v];
    const idx = arr.indexOf(opt);
    if (checked && idx === -1) arr.push(opt);
    if (!checked && idx > -1) arr.splice(idx, 1);
    this.setValue(playerId, field, arr);
  }
  helpId(playerId: string, field: string) { return `help-${playerId}-${field}`; }
  onCheckboxChange(playerId: string, field: string, ev: Event) {
    const target = ev.target as HTMLInputElement | null;
    this.setValue(playerId, field, !!target?.checked);
  }
  trackField = (_: number, f: PlayerProfileFieldSchema) => f.name;
  trackOpt = (_: number, o: string) => o;
  isUsLaxField(field: PlayerProfileFieldSchema): boolean {
    const lname = field.name.toLowerCase();
    const llabel = field.label.toLowerCase();
    return lname === 'sportassnid' || llabel.includes('lacrosse');
  }
  firstUsLaxField(): PlayerProfileFieldSchema | undefined {
    return this.schemas().find(f => this.isUsLaxField(f));
  }
  isFieldFilled(playerId: string, fieldName: string): boolean {
    const raw = this.value(playerId, fieldName);
    if (raw === null || raw === undefined) return false;
    if (typeof raw === 'string') return raw.trim().length > 0;
    if (Array.isArray(raw)) return raw.length > 0;
    if (typeof raw === 'boolean') return raw === true;
    return String(raw).trim().length > 0;
  }
  isFieldInvalid(playerId: string, fieldName: string): boolean {
    const errs = this.state.validateAllSelectedPlayers();
    return !!errs[playerId]?.[fieldName];
  }
  usLaxStatus(playerId: string) {
    return this.state.usLaxStatus()[playerId] || { value: '', status: 'idle' };
  }
  // Server-side validation errors (captured from preSubmit) exposed for template
  serverErrors = () => this.state.getServerValidationErrors();
  nameForPlayer(playerId?: string | null): string {
    const p = this.state.familyPlayers().find(fp => fp.playerId === (playerId ?? ''));
    return p ? `${p.firstName} ${p.lastName}`.trim() : (playerId ?? '');
  }
  trackErr = (_: number, e: PreSubmitValidationErrorDto) => `${e.playerId ?? ''}|${e.field ?? ''}`;
  labelForField(fieldName: string): string {
    const f = this.schemas().find(s => s.name.toLowerCase() === fieldName.toLowerCase());
    return f?.label || fieldName;
  }
  // Per-player required list
  missingRequiredLabels(playerId: string): string[] {
    const errs = this.state.validateAllSelectedPlayers();
    const row = errs[playerId] || {} as Record<string, string>;
    const labels: string[] = [];
    for (const [fname, msg] of Object.entries(row)) {
      if (msg === 'Required') labels.push(this.labelForField(fname));
    }
    // sort by label
    return labels.sort((a, b) => a.localeCompare(b));
  }
  // --- Modal state for viewing raw API details ---
  modalOpen = false;
  modalData: any = null;
  openUsLaxDetails(playerId: string) {
    const entry = this.state.usLaxStatus()[playerId];
    this.modalData = entry ? (entry.membership ?? null) : null;
    this.modalOpen = true;
  }
  closeModal() { this.modalOpen = false; this.modalData = null; }
  prettyJson(obj: any): string {
    try { return JSON.stringify(obj, null, 2); } catch { return String(obj); }
  }
  // Determine whether the message is the full guidance block (heuristic: contains ordered list markup and multiple help links)
  isGenericGuidance(msg: string | undefined): boolean {
    if (!msg) return false;
    return /<ol>/i.test(msg) && /membership@usalacrosse\.com/i.test(msg);
  }
  copyModalJson() {
    try {
      const txt = this.prettyJson(this.modalData);
      if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        navigator.clipboard.writeText(txt);
      }
    } catch {
      // no-op
    }
  }
  canValidateUsLax(playerId: string, field: string) {
    const v = String(this.value(playerId, field) ?? '').trim();
    return v.length > 0;
  }
  // US Lax validation handled by async validator directive; component keeps state only
  teamsLabel(playerId: string): string | null {
    const sel = this.state.selectedTeams()[playerId];
    if (!sel) return null;
    const toName = (id: string) => {
      const all = this.teams.filterByEligibility(null);
      const t = all.find(x => x.teamId === id);
      return t?.teamName || id;
    };
    if (Array.isArray(sel)) return sel.map(toName).join(', ');
    return toName(sel);
  }
  teamBadge(playerId: string): { label: string; text: string } | null {
    const sel = this.state.selectedTeams()[playerId];
    if (!sel) return null;
    if (Array.isArray(sel)) {
      const names = sel.map(id => {
        const all = this.teams.filterByEligibility(null);
        const t = all.find(x => x.teamId === id);
        return t?.teamName || id;
      });
      return { label: names.length === 1 ? 'Team' : 'Teams', text: names.join(', ') };
    }
    const all = this.teams.filterByEligibility(null);
    const t = all.find(x => x.teamId === sel);
    return { label: 'Team', text: t?.teamName || sel };
  }

  // Field visibility/condition helpers
  isFieldVisible(playerId: string, field: PlayerProfileFieldSchema): boolean {
    // Hide adminOnly/hidden by default in public wizard
    if (field.visibility === 'hidden' || field.visibility === 'adminOnly') return false;
    // Hide any legacy team selection field now that teams are selected in a dedicated step
    const lname = field.name.toLowerCase();
    const llabel = field.label.toLowerCase();
    if (['team', 'teamid', 'teams'].includes(lname) || llabel.includes('select a team')) return false;
    // Hide waiver/consent acceptance fields from Forms (rendered in Waivers step)
    const waiverNames = new Set(this.state.waiverFieldNames());
    if (waiverNames.has(field.name)) return false;
    // Hide eligibility driver fields (handled in the dedicated Eligibility step)
    const tctype = (this.state.teamConstraintType() || '').toUpperCase();
    const hasAll = (s: string, parts: string[]) => parts.every(p => s.includes(p));
    // Generic 'eligibility' named field
    if (lname === 'eligibility' || llabel.includes('eligibility')) return false;
    if (tctype === 'BYGRADYEAR') {
      // e.g., GradYear, GraduationYear, Grad Year, Graduation Year
      if (hasAll(lname, ['grad', 'year']) || hasAll(llabel, ['grad', 'year'])) return false;
    } else if (tctype === 'BYAGEGROUP') {
      if (hasAll(lname, ['age', 'group']) || hasAll(llabel, ['age', 'group'])) return false;
    } else if (tctype === 'BYAGERANGE') {
      if (hasAll(lname, ['age', 'range']) || hasAll(llabel, ['age', 'range'])) return false;
    }
    if (!field.condition) return true;
    const otherVal = this.value(playerId, field.condition.field);
    const op = (field.condition.operator || 'equals').toLowerCase();
    if (op === 'equals') {
      return otherVal === field.condition.value;
    }
    // default fallback
    return otherVal === field.condition.value;
  }
  /**
   * Converts a value to an array for template iteration (used as a pipe in template).
   */
  arrayify(val: string | string[] | null | undefined): string[] {
    if (!val) return [];
    return Array.isArray(val) ? val : [val];
  }

  /**
   * Looks up the team name for a given teamId.
   */
  nameForTeam(teamId: string): string {
    const all = this.teams.filterByEligibility(null);
    const t = all.find(x => x.teamId === teamId);
    return t?.teamName || teamId;
  }
  priceForTeam(teamId: string): number | null {
    const all = this.teams.filterByEligibility(null);
    const t = all.find(x => x.teamId === teamId);
    const fee = (t as any)?.perRegistrantFee as number | undefined;
    return fee ?? null;
  }

  /**
   * Removes a team from the selectedTeams for a player.
   */
  removeTeam(playerId: string, teamId: string): void {
    const map = { ...this.state.selectedTeams() } as Record<string, string | string[]>;
    const sel = map[playerId];
    if (!sel) return;
    let arr = Array.isArray(sel) ? [...sel] : [sel];
    arr = arr.filter(id => id !== teamId);
    map[playerId] = arr;
    this.state.setSelectedTeams(map);
  }

  /** Disable Continue when any required visible field is invalid/pending. MVP: gate on USA Lacrosse field. */
  isNextDisabled(): boolean {
    // Readonly mode (editing existing registrations) should not block navigation
    if (this.readonlyMode()) return false;
    // Use centralized metadata-driven validation (includes USA Lacrosse async state).
    return !this.state.areFormsValid();
  }

  /** Returns true when the USA Lax field state for a given player should block navigation. */
  private shouldBlockForUsLax(playerId: string, field: PlayerProfileFieldSchema): boolean {
    const raw = this.value(playerId, field.name);
    const val = (raw == null) ? '' : String(raw).trim();
    const status = this.usLaxStatus(playerId).status;
    const isPendingOrInvalid = status === 'validating' || status === 'invalid';

    if (field.required) {
      // Empty required value or pending/invalid validation blocks
      if (!val) return true;
      if (isPendingOrInvalid) return true;
      return false;
    }
    // Optional: only block when a value is present and not yet valid
    if (val && isPendingOrInvalid) return true;
    return false;
  }
}
