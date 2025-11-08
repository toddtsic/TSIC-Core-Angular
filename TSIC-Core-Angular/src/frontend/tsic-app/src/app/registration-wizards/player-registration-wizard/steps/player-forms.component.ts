import { Component, EventEmitter, Output, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService, PlayerProfileFieldSchema } from '../registration-wizard.service';
import { TeamService } from '../team.service';

@Component({
  selector: 'app-rw-player-forms',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Player Forms</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">Provide required information for each player.</p>

        @if (schemas().length === 0) {
          <div class="alert alert-info mb-3">
            This registration doesn’t define any player form fields.
            <br class="d-none d-md-block" />
            <small class="text-muted">If this seems wrong, contact an administrator and reference Job {{ jobId() || jobPath() || 'N/A' }}.</small>
          </div>
        }

        @if (schemas().length > 0) {
          <div class="vstack gap-4">
            @for (player of selectedPlayers(); track player.userId; let i = $index) {
              <div class="border rounded p-3 shadow-sm" [ngClass]="cardBgClass(i)">
                <h6 class="fw-semibold mb-3 d-flex flex-wrap align-items-center gap-2">
                  <span>{{ player.name }}</span>
                  @if (teamBadge(player.userId)) {
                    <span class="badge bg-warning text-dark px-2 py-1">
                      {{ teamBadge(player.userId)?.label }}: {{ teamBadge(player.userId)?.text }}
                    </span>
                  }
                </h6>
                <div class="row g-3">
                  @for (field of schemas(); track field.name) {
                    @if (isFieldVisible(player.userId, field)) {
                      <div class="col-12 col-md-6">
                        <label class="form-label fw-semibold mb-1">
                          {{ field.label }}
                          @if (field.required) { <span class="text-danger">*</span> }
                        </label>

                        @switch (field.type) {
                          @case ('text') {
                            <input type="text" class="form-control"
                                   [required]="field.required"
                                   [ngModel]="value(player.userId, field.name)"
                                   (ngModelChange)="setValue(player.userId, field.name, $event)"
                                   [attr.aria-describedby]="helpId(player.userId, field.name)" />
                          }
                          @case ('number') {
                            <input type="number" class="form-control"
                                   [required]="field.required"
                                   [ngModel]="value(player.userId, field.name)"
                                   (ngModelChange)="setValue(player.userId, field.name, $event)"
                                   [attr.aria-describedby]="helpId(player.userId, field.name)" />
                          }
                          @case ('date') {
                            <input type="date" class="form-control"
                                   [required]="field.required"
                                   [ngModel]="value(player.userId, field.name)"
                                   (ngModelChange)="setValue(player.userId, field.name, $event)"
                                   [attr.aria-describedby]="helpId(player.userId, field.name)" />
                          }
                          @case ('select') {
                            <select class="form-select"
                                    [required]="field.required"
                                    [ngModel]="value(player.userId, field.name)"
                                    (ngModelChange)="setValue(player.userId, field.name, $event)"
                                    [attr.aria-describedby]="helpId(player.userId, field.name)">
                              @if (!field.required) {
                                <option value=""></option>
                              }
                              @for (opt of field.options; track opt) {
                                <option [value]="opt">{{ opt }}</option>
                              }
                            </select>
                          }
                          @case ('multiselect') {
                            <div class="border rounded p-2 bg-light-subtle">
                              @for (opt of field.options; track opt) {
                                <div class="form-check">
                                  <input class="form-check-input" type="checkbox"
                                         [checked]="isMultiChecked(player.userId, field.name, opt)"
                                         (change)="toggleMulti(player.userId, field.name, opt, $event)" />
                                  <label class="form-check-label">{{ opt }}</label>
                                </div>
                              }
                            </div>
                          }
                          @case ('checkbox') {
                            <div class="form-check mt-1">
                              <input class="form-check-input" type="checkbox"
                                     [checked]="value(player.userId, field.name) === true"
                                     (change)="onCheckboxChange(player.userId, field.name, $event)" />
                              <label class="form-check-label">Yes</label>
                            </div>
                          }
                          @default {
                            <input type="text" class="form-control"
                                   [required]="field.required"
                                   [ngModel]="value(player.userId, field.name)"
                                   (ngModelChange)="setValue(player.userId, field.name, $event)" />
                          }
                        }

                        @if (field.helpText) {
                          <div class="form-text" [id]="helpId(player.userId, field.name)">{{ field.helpText }}</div>
                        }
                      </div>
                    }
                  }
                </div>
              </div>
            }
          </div>
        }
        @if (hasWaivers()) {
          <div class="mt-4">
            <h6 class="fw-semibold">Waivers and Agreements</h6>
            <div class="vstack gap-2">
              @for (w of waiverEntries(); track w[0]) {
                <details class="border rounded p-2 bg-body-tertiary">
                  <summary class="fw-semibold cursor-pointer">{{ waiverTitle(w[0]) }}</summary>
                  <div class="mt-2 small" style="white-space:pre-wrap">{{ w[1] }}</div>
                </details>
              }
            </div>
          </div>
        }
        <div class="rw-bottom-nav d-flex gap-2 mt-3">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" (click)="next.emit()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class PlayerFormsComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  private readonly state = inject(RegistrationWizardService);
  private readonly teams = inject(TeamService);

  schemas = () => this.state.profileFieldSchemas();
  selectedPlayers = () => this.state.selectedPlayers();
  jobId = () => this.state.jobId();
  jobPath = () => this.state.jobPath();
  hasWaivers = () => Object.keys(this.state.jobWaivers()).length > 0;
  waiverEntries = () => Object.entries(this.state.jobWaivers());
  waiverTitle(key: string): string {
    const trimmed = key.replace(/^PlayerReg/, '');
    // Split camel-case into words (replaceAll for lint preference)
    return trimmed.replaceAll(/([a-z])([A-Z])/g, '$1 $2').trim();
  }
  cardBgClass(i: number): string {
    const palette = ['bg-primary-subtle', 'bg-success-subtle', 'bg-info-subtle', 'bg-warning-subtle', 'bg-secondary-subtle', 'bg-danger-subtle'];
    return palette[i % palette.length];
  }

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
  trackPlayer = (_: number, p: { userId: string }) => p.userId;
  trackField = (_: number, f: PlayerProfileFieldSchema) => f.name;
  trackOpt = (_: number, o: string) => o;
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
}
