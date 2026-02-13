import { ChangeDetectionStrategy, Component, EventEmitter, Output, inject, signal, AfterViewInit, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormGroup, FormControl, Validators } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';
import { WaiverStateService } from '../services/waiver-state.service';

@Component({
  selector: 'app-rw-waivers',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Waivers & Agreements</h5>
      </div>
      <div class="card-body">
        @if (waivers().length === 0) {
          <div class="alert alert-info">No waivers are required for this registration. You can continue.</div>
        } @else {
          <form [formGroup]="waiverForm" novalidate>
          <!-- Prominent banner explaining scope -->
          <div class="alert alert-warning mb-4" role="alert">
            <div class="fw-semibold mb-1">Waivers must be agreed to in order for players to participate.</div>
            <div class="small mb-2">You (the parent/guardian or adult participant) are agreeing to all waivers for the following players:</div>
            <div class="d-flex flex-wrap gap-2 mb-2">
              @for (p of players(); track p.userId) {
                <span class="badge bg-primary-subtle text-primary-emphasis px-2 py-1">{{ p.name }}</span>
              }
            </div>
            <div class="small text-muted">Review each accordion section below. Acceptance status is shown and locked if already on file.</div>
          </div>

          <!-- Bootstrap-like accordion (exclusive open) -->
          <div class="accordion" id="waiverAccordion">
            @for (w of waivers(); track w.id; let i = $index) {
              <div class="accordion-item border rounded mb-2 shadow-sm">
                <h2 class="accordion-header" [id]="'waiver-h-' + w.id">
                  <button class="accordion-button d-flex justify-content-between gap-2" type="button"
                          [class.collapsed]="!isOpen(w.id)" (click)="toggleOpenExclusive(w.id)"
                          [attr.aria-expanded]="isOpen(w.id)" [attr.aria-controls]="'waiver-c-' + w.id">
                    <span class="fw-semibold">{{ w.title }}</span>
                    <span class="ms-auto d-inline-flex align-items-center">
                      <span class="badge" [ngClass]="isAccepted(w.id) ? 'bg-success-subtle text-success-emphasis' : 'bg-danger-subtle text-danger-emphasis'">
                        {{ isAccepted(w.id) ? 'Accepted' : 'Not Accepted' }}
                      </span>
                    </span>
                  </button>
                </h2>
                <div [id]="'waiver-c-' + w.id" class="accordion-collapse collapse" [class.show]="isOpen(w.id)" role="region" [attr.aria-labelledby]="'waiver-h-' + w.id">
                  <div class="accordion-body small">
                    <div class="mb-3">
                      @if (w.html && w.html.trim().length) {
                        <div [innerHTML]="w.html"></div>
                      } @else {
                        <div class="alert alert-light border small mb-0">No content provided for this waiver.</div>
                      }
                    </div>
                    <div class="form-check">
       <input class="form-check-input" type="checkbox"
         [formControlName]="bindingKey(w.id)"
         [id]="'waiver-' + w.id"
         [class.is-invalid]="submitted() && w.required && controlInvalid(w.id)"
         [attr.aria-invalid]="submitted() && w.required && controlInvalid(w.id)" />
                      <label class="form-check-label" [for]="'waiver-' + w.id">
                        I have read and agree to the {{ w.title.toLowerCase() }}
                      </label>
                      @if (isAccepted(w.id) && isLocked(w.id)) {
                        <span class="badge bg-secondary ms-2" title="Previously accepted and locked">Locked</span>
                      }
                      @if (submitted() && w.required && controlInvalid(w.id)) {
                        <div class="invalid-feedback d-block mt-1">Please accept this waiver to continue.</div>
                      }
                    </div>
                  </div>
                </div>
              </div>
            }
          </div>

          <!-- Signature capture removed per updated requirements: individual waiver acceptance only -->
          </form>
        }

      </div>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class WaiversComponent implements OnInit, AfterViewInit {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();

  private readonly state = inject(RegistrationWizardService);
  private readonly waiverState = inject(WaiverStateService);

  waivers = () => this.waiverState.waiverDefinitions();
  players = () => this.state.familyPlayers()
    .filter(p => p.selected || p.registered)
    .map(p => ({ userId: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
  submitted = signal(false);

  // Track open panels (exclusive) and auto-open first waiver
  openSet = signal<Set<string>>(new Set());
  private firstInit = false;
  isOpen(id: string): boolean { return this.openSet().has(id); }
  private initFirst(): void {
    if (this.firstInit) return;
    const first = this.waivers()[0]?.id;
    if (first) this.openSet.set(new Set([first]));
    this.firstInit = true;
  }
  toggleOpenExclusive(id: string): void {
    this.initFirst();
    const s = new Set<string>();
    if (!this.openSet().has(id)) s.add(id); // open clicked, close others
    this.openSet.set(s);
  }
  // First panel open handled in ngAfterViewInit below

  // Editing mode detection (checkbox disabled when editing existing registration or when any selected player is already registered)
  isEditingMode(): boolean {
    // Editing mode only when ALL selected players are registered (pure edit scenario)
    try {
      const selected = this.state.familyPlayers().filter(p => p.selected || p.registered);
      return selected.length > 0 && selected.every(p => p.registered);
    } catch { return false; }
  }

  waiverForm!: FormGroup;
  private readonly lockedIds = new Set<string>();

  ngOnInit(): void {
    this.buildForm();
    // Subscribe to value changes to push into service (skip disabled locked controls)
    this.waiverForm.valueChanges.subscribe(v => {
      // Persist to service
      for (const [key, val] of Object.entries(v)) {
        if (!this.lockedIds.has(key)) {
          // Keys are field names; set acceptance normalized in service
          this.waiverState.setWaiverAccepted(key, !!val);
        }
      }
      // Detect newly accepted waiver and open next unchecked
      for (const [key, val] of Object.entries(v)) {
        const prev = this._prevValues[key];
        if (!prev && !!val) {
          // key is field name; get corresponding def id for UI navigation
          const id = this.reverseBind(key) || key;
          this.openNextUncheckedAfter(id);
        }
        this._prevValues[key] = !!val;
      }
      // Update global gate after any change
      this.pushGate();
    });
    // Initial gate
    this.pushGate();
  }

  private buildForm(): void {
    const defs = this.waivers();
    const svcMap = this.waiverState.waiversAccepted();
    const editing = this.isEditingMode();
    const group: Record<string, FormControl> = {};
    // Track previous values so we can detect newly accepted waivers for auto-open UX
    this._prevValues = {};
    // Determine if legacy edit scenario with none marked accepted yet
    const legacyEdit = editing && defs.some(d => d.required) && Object.values(svcMap).every(v => !v);
    for (const d of defs) {
      const key = this.bindingKey(d.id);
      const initialAccepted = legacyEdit && d.required ? true : !!svcMap[key];
      const control = new FormControl(initialAccepted, d.required ? Validators.requiredTrue : []);
      if (editing && initialAccepted) {
        control.disable({ emitEvent: false });
        this.lockedIds.add(key);
        // Ensure service map reflects locked acceptance so Continue can enable
        // Use definition id for stable keying; setter records both def id and field name
        if (d.required) this.waiverState.setWaiverAccepted(d.id, true);
      }
      group[key] = control;
      this._prevValues[key] = initialAccepted; // seed previous snapshot
    }
    this.waiverForm = new FormGroup(group);
  }

  isAccepted(id: string): boolean {
    const ctrl = this.waiverForm?.get(this.bindingKey(id));
    return !!ctrl?.value;
  }
  isLocked(id: string): boolean { return this.lockedIds.has(this.bindingKey(id)); }
  controlInvalid(id: string): boolean {
    const ctrl = this.waiverForm?.get(this.bindingKey(id));
    return !!ctrl && ctrl.invalid && (ctrl.touched || this.submitted());
  }

  ngAfterViewInit(): void {
    this.initFirst();
    // Auto-seed acceptance if edit-only scenario and none accepted yet
    if (this.isEditingMode() && this.waivers().length > 0 && !this.waivers().some(w => this.isAccepted(w.id))) {
      for (const w of this.waivers()) {
        if (w.required) this.waiverState.setWaiverAccepted(this.bindingKey(w.id), true);
      }
    }
    // Re-evaluate gate after any programmatic seeding
    this.pushGate();
  }
  // (moved ngAfterViewInit implementation above to add auto-seed logic)

  // Reactive continue disabled computation using local map
  disableContinue(): boolean {
    // No waivers => always allow
    if (this.waivers().length === 0) return false;
    // If all required accepted according to state, allow even if form is disabled (edit/locked scenario)
    const allAccepted = this.waivers().every(w => !w.required || this.isAccepted(w.id));
    if (allAccepted) return false;
    // Fallback: rely on form validity (reactive path)
    if (this.waiverForm?.disabled) {
      // Disabled group but not all accepted? Treat as blocked until acceptance map fills
      return !allAccepted;
    }
    return !this.waiverForm.valid;
  }

  handleContinue(): void {
    this.submitted.set(true);
    if (this.disableContinue()) {
      this.openFirstMissingAndFocus();
      return;
    }
    this.next.emit();
  }

  private firstMissingRequiredWaiverId(): string | null {
    for (const w of this.waivers()) {
      if (w.required && !this.isAccepted(w.id)) return w.id;
    }
    return null;
  }

  private openFirstMissingAndFocus(): void {
    const id = this.firstMissingRequiredWaiverId();
    if (!id) return;
    // Open this waiver exclusively
    this.openSet.set(new Set([id]));
    // Focus checkbox after DOM updates
    try {
      queueMicrotask(() => {
        const el = document.getElementById(`waiver-${id}`) as HTMLInputElement | null;
        el?.focus();
        // If not visible yet, try next frame
        if (!el) requestAnimationFrame(() => (document.getElementById(`waiver-${id}`) as HTMLInputElement | null)?.focus());
      });
    } catch {
      // no-op
    }
  }

  // --- Auto-open next unchecked waiver after accepting current one ---
  private _prevValues: Record<string, boolean> = {};
  private openNextUncheckedAfter(id: string): void {
    try {
      const defs = this.waivers();
      const idx = defs.findIndex(w => w.id === id);
      if (idx === -1) return;
      for (let i = idx + 1; i < defs.length; i++) {
        const w = defs[i];
        // Skip already accepted or locked waivers
        if (this.isLocked(w.id)) continue;
        const ctrl = this.waiverForm.get(this.bindingKey(w.id));
        if (!ctrl || !!ctrl.value) continue; // already accepted
        // Found the first subsequent unchecked, open it
        this.openSet.set(new Set([w.id]));
        // Scroll into view (defer to allow collapse/expand classes to apply)
        setTimeout(() => {
          const headerBtn = document.getElementById('waiver-h-' + w.id)?.querySelector('button');
          (headerBtn as HTMLElement | null)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }, 30);
        break;
      }
    } catch { /* no-op */ }
  }

  // --- Binding helpers between waiver definition ID and schema field name ---
  bindingKey(defId: string): string {
    const map = this.waiverState.waiverIdToField();
    return map[defId] || defId; // fallback to id if mapping not found
  }
  reverseBind(fieldName: string): string | null {
    const map = this.waiverState.waiverIdToField();
    for (const [id, fname] of Object.entries(map)) {
      if (fname === fieldName) return id;
    }
    return null;
  }

  // --- Gate helper: publish whether Continue should be enabled based on current form state ---
  private pushGate(): void {
    try {
      const allow = !this.disableContinue();
      this.waiverState.waiversGateOk.set(allow);
    } catch {
      // no-op
    }
  }
}
