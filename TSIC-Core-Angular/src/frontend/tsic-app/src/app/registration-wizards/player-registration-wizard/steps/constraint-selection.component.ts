import { Component, EventEmitter, Output, inject, signal, effect, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { JobService, Job } from '../../../core/services/job.service';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';

@Component({
  selector: 'app-rw-eligibility-selection',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">{{ headingText() }}</h5>
      </div>
      <div class="card-body">
        <p class="text-secondary mb-3">{{ helperText() }}</p>

        @if (loading()) {
          <div class="text-muted small">Loading eligibility options...</div>
        } @else if (eligibleOptions().length === 0) {
          <div class="alert alert-warning small mb-3">No eligibility options were found for this job. You can continue to teams.</div>
        } @else {
          <form [formGroup]="form" (ngSubmit)="handleContinue()" class="mb-3">
            <div class="mb-3">
              <label for="eligibilityValue" class="form-label fw-semibold">{{ selectLabel() }}</label>
              <select id="eligibilityValue" formControlName="eligibilityValue" class="form-select">
                <option value="" disabled>Select {{ selectLabel().toLowerCase() }}</option>
                @for (opt of eligibleOptions(); track opt.value) {
                  <option [value]="opt.value">{{ opt.label }}</option>
                }
              </select>
              @if (submitted() && form.get('eligibilityValue')?.invalid) {
                <div class="invalid-feedback d-block">A selection is required.</div>
              }
            </div>
          </form>
        }

        <div class="rw-bottom-nav d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" (click)="handleContinue()" [disabled]="disableContinue()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class ConstraintSelectionComponent {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();
  constructor(public state: RegistrationWizardService) { }

  // Services
  private readonly jobService = inject(JobService);
  private readonly fb = inject(FormBuilder);

  // Form & submission state
  form: FormGroup = this.fb.group({
    eligibilityValue: ['']
  });
  submitted = signal(false);

  // Reactive option state
  private readonly _rawJob = signal<Job | null>(null);
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
      default: return 'Select Eligibility';
    }
  });
  selectLabel = computed(() => {
    const type = this._teamConstraintType();
    switch (type) {
      case 'BYGRADYEAR': return 'Graduation Year';
      case 'BYAGEGROUP': return 'Age Group';
      case 'BYAGERANGE': return 'Age Range';
      default: return 'Eligibility Value';
    }
  });
  helperText = computed(() => {
    const type = this._teamConstraintType();
    switch (type) {
      case 'BYGRADYEAR': return 'Choose your graduation year to filter available teams.';
      case 'BYAGEGROUP': return 'Choose your age group to filter available teams.';
      case 'BYAGERANGE': return 'Choose the applicable age range to filter available teams.';
      default: return 'Choose the value (e.g., Graduation Year) that determines which teams you\'re eligible to join.';
    }
  });

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
      if (opts.length > 0) {
        this.form.get('eligibilityValue')?.addValidators([Validators.required]);
        this.form.get('eligibilityValue')?.updateValueAndValidity({ emitEvent: false });
      }
      this.loading.set(false);
    } catch (e) {
      this.error.set('Unable to load eligibility options');
      console.error('[Eligibility] Parse failure', e);
      this.loading.set(false);
    }
  }, { allowSignalWrites: true });

  // Heuristic detection (temporary until backend-configured type exposed)
  private detectConstraintType(job: Job): string {
    const existing = this.state.teamConstraintType();
    if (existing) return existing;
    const raw = job.jsonOptions;
    if (raw) {
      const lower = raw.toLowerCase();
      if (lower.includes('grad')) return 'BYGRADYEAR';
      if (lower.includes('agegroup')) return 'BYAGEGROUP';
      if (lower.includes('agerange')) return 'BYAGERANGE';
    }
    return 'BYGRADYEAR';
  }

  private getJobOptionsRaw(job: Job): string | null {
    const raw = job.jsonOptions
      ?? (job as any).jobOptions
      ?? (job as any).JsonOptions
      ?? (job as any).jsonoptions
      ?? (job as any).playerProfileMetadataJson
      ?? null;
    return typeof raw === 'string' && raw.trim() ? raw : null;
  }

  private extractEligibleOptions(job: Job, type: string): Array<{ value: string; label: string }> {
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
    if (opts.length === 0) return false;
    return this.form.invalid;
  }

  handleContinue(): void {
    if (this.disableContinue()) {
      this.submitted.set(true);
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.get('eligibilityValue')?.value;
    if (value) {
      this.state.teamConstraintValue.set(String(value));
    }
    this.next.emit();
  }
}
