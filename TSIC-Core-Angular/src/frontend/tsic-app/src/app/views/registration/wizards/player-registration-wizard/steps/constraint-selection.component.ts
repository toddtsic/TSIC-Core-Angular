import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, signal, effect, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';
import { JobService } from '@infrastructure/services/job.service';
import type { JobMetadataResponse } from '@core/api';
// Reactive forms were previously layered here but not used in the template; simplified to template-driven.

@Component({
  selector: 'app-rw-eligibility-selection',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">{{ headingText() }}</h5>
      </div>
      <div class="card-body">
        <p class="text-muted mb-3">{{ helperText() }}</p>

        @if (loading()) {
          <div class="text-muted small">Loading eligibility options...</div>
        } @else if (eligibleOptions().length === 0) {
          <div class="alert alert-warning small mb-3">No eligibility options were found for this job. You can continue to teams.</div>
        } @else {
          <div class="mb-3">
            <p class="small text-muted mb-2">Select {{ selectLabel().toLowerCase() }} for each player you are registering.</p>
            <div class="vstack gap-3">
              @for (p of selectedPlayers(); track p.userId; let idx = $index) {
                <div class="card card-rounded shadow-sm mb-3" style="border-width: 1px; border-style: solid;">
                  <div class="card-header border-bottom-0" [ngClass]="colorClassForIndex(idx)">
                    <div class="d-flex align-items-center gap-2">
                      <span class="badge rounded-pill px-3 py-2" [ngClass]="textColorClassForIndex(idx)">{{ p.name }}</span>
                      @if (isPlayerLocked(p.userId)) {
                        <span class="badge bg-secondary" title="Already registered; eligibility locked">Locked</span>
                      }
                    </div>
                  </div>
                  <div class="card-body" [ngClass]="colorClassForIndex(idx)">
                    <select class="form-select"
                            [disabled]="eligibilityDisabled() || isPlayerLocked(p.userId)"
                            [ngModel]="eligibilityFor(p.userId)"
                            (ngModelChange)="state.setEligibilityForPlayer(p.userId, $event)">
                      <option value="" disabled>Select {{ selectLabel().toLowerCase() }}</option>
                      @if (eligibilityFor(p.userId) && !hasEligibleOption(eligibilityFor(p.userId))) {
                        <option [value]="eligibilityFor(p.userId)" selected>{{ eligibilityFor(p.userId) }}</option>
                      }
                      @for (opt of eligibleOptions(); track opt.value) {
                        <option [value]="opt.value">{{ opt.label }}</option>
                      }
                    </select>
                  </div>
                </div>
              }
            </div>
            @if (submitted() && missingEligibility().length) {
              <div class="invalid-feedback d-block mt-2">Please select {{ selectLabel().toLowerCase() }} for: {{ missingEligibilityNames() }}.</div>
            }
          </div>
        }
      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ConstraintSelectionComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  public readonly state = inject(RegistrationWizardService);

  constructor() { }

  // Services
  private readonly jobService = inject(JobService);

  // Submission state
  submitted = signal(false);

  // Reactive option state
  private readonly _rawJob = signal<JobMetadataResponse | null>(null);
  private readonly _teamConstraintType = signal<string | null>(null);
  private readonly _eligibleOptions = signal<Array<{ value: string; label: string }>>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  // Derived computed properties
  eligibleOptions = this._eligibleOptions.asReadonly();
  headingText = computed(() => {
    const type = this._teamConstraintType();
    switch (type) {
      case 'BYGRADYEAR': return 'Select Eligibility';
      case 'BYAGEGROUP': return 'Select Age Group';
      case 'BYAGERANGE': return 'Select Age Range';
      case 'BYCLUBNAME': return 'Select Club';
      default: return 'Select Eligibility';
    }
  });
  selectLabel = computed(() => {
    const type = this._teamConstraintType();
    switch (type) {
      case 'BYGRADYEAR': return 'Graduation Year';
      case 'BYAGEGROUP': return 'Age Group';
      case 'BYAGERANGE': return 'Age Range';
      case 'BYCLUBNAME': return 'Club Name';
      default: return 'Eligibility Value';
    }
  });
  helperText = computed(() => {
    const type = this._teamConstraintType();
    switch (type) {
      case 'BYGRADYEAR': return 'Choose your graduation year to filter available teams.';
      case 'BYAGEGROUP': return 'Choose your age group to filter available teams.';
      case 'BYAGERANGE': return 'Choose the applicable age range to filter available teams.';
      case 'BYCLUBNAME': return 'Choose the club you wish to register under to filter available teams.';
      default: return 'Choose the value (e.g., Graduation Year) that determines which teams you\'re eligible to join.';
    }
  });

  eligibilityDisabled = computed(() => this.state.familyPlayers().some(p => (p.selected || p.registered) && p.registered));

  private isPlayerRegistered(playerId: string): boolean {
    try {
      return !!this.state.familyPlayers().find(p => p.playerId === playerId)?.registered;
    } catch { return false; }
  }

  // Effect: watch current job and derive options
  private readonly _jobEffect = effect(() => {
    const job = this.jobService.getCurrentJob();
    this._rawJob.set(job);
    if (!job) { this.loading.set(false); return; }
    try {
      const constraintType = this.detectConstraintType(job);
      this._teamConstraintType.set(constraintType);
      this.state.teamConstraintType.set(constraintType);
      const opts = this.extractEligibleOptions(job, constraintType);
      this._eligibleOptions.set(opts);
      // No local FormGroup used; each player's selection is tracked via state map.
      this.loading.set(false);
    } catch (e) {
      this.error.set('Unable to load eligibility options');
      console.error('[Eligibility] Parse failure', e);
      this.loading.set(false);
    }
  });

  // Heuristic detection (temporary until backend-configured type exposed)
  private detectConstraintType(job: JobMetadataResponse): string | null {
    const existing = this.state.teamConstraintType();
    if (existing) return existing;
    const raw = job.jsonOptions;
    if (raw) {
      try {
        const parsed = JSON.parse(raw);
        const explicit = String(parsed?.constraintType ?? parsed?.teamConstraint ?? parsed?.eligibilityConstraint ?? '').toUpperCase();
        switch (explicit) {
          case 'BYGRADYEAR':
          case 'BYAGEGROUP':
          case 'BYAGERANGE':
          case 'BYCLUBNAME':
            return explicit;
        }
      } catch { /* ignore */ }
    }
    // No recognizable constraint tokens -> no eligibility step required
    return null;
  }

  private getJobOptionsRaw(job: JobMetadataResponse): string | null {
    const raw = job.jsonOptions
      ?? (job as any).jobOptions
      ?? (job as any).JsonOptions
      ?? (job as any).jsonoptions
      ?? (job as any).playerProfileMetadataJson
      ?? null;
    return typeof raw === 'string' && raw.trim() ? raw : null;
  }

  private extractEligibleOptions(job: JobMetadataResponse, type: string | null): Array<{ value: string; label: string }> {
    const raw = this.getJobOptionsRaw(job);
    if (!raw) return [];
    let parsed: any;
    try { parsed = JSON.parse(raw); } catch { return []; }
    if (!parsed || typeof parsed !== 'object') return [];
    const keys = Object.keys(parsed);
    try { if ((globalThis as any).location?.host?.startsWith?.('localhost')) { console.debug('[Eligibility] jsonOptions keys', keys); } } catch { }
    switch (type) {
      case 'BYGRADYEAR': return this.buildGradYearOptions(parsed, keys);
      case 'BYAGEGROUP': return this.buildAgeGroupOptions(parsed, keys);
      case 'BYAGERANGE': return this.buildAgeRangeOptions(parsed, keys);
      default: return [];
    }
  }

  private buildGradYearOptions(parsed: any, keys: string[]): Array<{ value: string; label: string }> {
    const isYear = (v: any) => /^(20|19)\d{2}$/.test(String(v));
    const normalizeObj = (o: any) => {
      if (o && typeof o === 'object') {
        const val = o.Value ?? o.value ?? o.year ?? o.id;
        const label = o.Text ?? o.text ?? o.label ?? o.name ?? o.display ?? val;
        return { value: String(val), label: String(label) };
      }
      return { value: String(o), label: String(o) };
    };
    const key = keys.find(k => k.toLowerCase().includes('grad') && k.toLowerCase().includes('year'));
    let arr: any[] = [];
    if (key) {
      const candidate = parsed[key];
      if (Array.isArray(candidate)) arr = candidate; else arr = [];
    }
    let mapped = arr.map(normalizeObj).filter(o => isYear(o.value));
    if (mapped.length === 0) {
      for (const k of keys) {
        const candidate = Array.isArray(parsed[k]) ? parsed[k] : [];
        if (candidate.length > 0 && candidate.every(x => isYear((x && typeof x === 'object') ? (x.Value ?? x.value ?? x.year ?? x.id ?? x) : x))) {
          mapped = candidate.map(normalizeObj).filter(o => isYear(o.value));
          break;
        }
      }
    }
    return mapped.sort((a, b) => a.value.localeCompare(b.value));
  }

  private buildAgeGroupOptions(parsed: any, keys: string[]): Array<{ value: string; label: string }> {
    const key = keys.find(k => k.toLowerCase().includes('age') && k.toLowerCase().includes('group'));
    if (!key) return [];
    const arr: any[] = Array.isArray(parsed[key]) ? parsed[key] : [];
    return arr.map(o => {
      if (o && typeof o === 'object') {
        const val = o.Value ?? o.value ?? o.id ?? o.code ?? o.name;
        const label = o.Text ?? o.text ?? o.label ?? o.name ?? val;
        return { value: String(val), label: String(label) };
      }
      return { value: String(o), label: String(o) };
    }).filter(o => o.value);
  }

  private buildAgeRangeOptions(parsed: any, keys: string[]): Array<{ value: string; label: string }> {
    const key = keys.find(k => k.toLowerCase().includes('age') && k.toLowerCase().includes('range'));
    if (!key) return [];
    const arr: any[] = Array.isArray(parsed[key]) ? parsed[key] : [];
    return arr.map(r => {
      if (r && typeof r === 'object') {
        const min = r.MinAge ?? r.minAge ?? r.min ?? r.start;
        const max = r.MaxAge ?? r.maxAge ?? r.max ?? r.end;
        if ((min || min === 0) && (max || max === 0)) {
          return { value: `${min}-${max}`, label: `${min}-${max}` };
        }
      }
      return null;
    }).filter(x => !!x) as Array<{ value: string; label: string }>;
  }

  disableContinue(): boolean {
    if (this.loading()) return true;
    const opts = this.eligibleOptions();
    if (opts.length === 0) return false; // no options means skip eligibility
    // Require eligibility value for each selected player
    return this.missingEligibility().length > 0;
  }

  handleContinue(): void {
    if (this.disableContinue()) {
      this.submitted.set(true);
      return;
    }
    // For backward compatibility set global value if all players share same eligibility selection
    const eligMap = this.state.eligibilityByPlayer();
    const values = Object.values(eligMap).filter(v => !!v);
    const unique = Array.from(new Set(values));
    if (unique.length === 1) {
      this.state.teamConstraintValue.set(unique[0]);
    }
    this.next.emit();
  }

  eligibilityFor(playerId: string): string {
    return this.state.getEligibilityForPlayer(playerId) || '';
  }
  isPlayerLocked(playerId: string): boolean {
    // Locked only when player already registered (edit mode concept removed)
    return this.eligibilityDisabled() || this.isPlayerRegistered(playerId);
  }
  onSelectChange(playerId: string, ev: Event) {
    if (this.eligibilityDisabled()) return; // locked in edit mode
    const target = ev.target as HTMLSelectElement | null;
    const val = target?.value ?? '';
    this.state.setEligibilityForPlayer(playerId, val);
  }
  missingEligibility() {
    const sel = this.selectedPlayers();
    const map = this.state.eligibilityByPlayer();
    const opts = this.eligibleOptions();
    if (opts.length === 0) return []; // skip check when no options
    return sel.filter(p => !map[p.userId]);
  }
  missingEligibilityNames() {
    return this.missingEligibility().map(p => p.name).join(', ');
  }

  selectedPlayers() {
    return this.state.familyPlayers()
      .filter(p => p.selected || p.registered)
      .map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
  }

  // Helper: check if a given value exists in the eligible options list
  hasEligibleOption(val: string | undefined): boolean {
    if (!val) return false;
    const opts = this.eligibleOptions();
    return Array.isArray(opts) && opts.some(o => o?.value === val);
  }

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
