import { Component, EventEmitter, Output, inject, signal, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RegistrationWizardService } from '../registration-wizard.service';

@Component({
  selector: 'app-rw-waivers',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Waivers & Agreements</h5>
      </div>
      <div class="card-body">
        @if (waivers().length === 0) {
          <div class="alert alert-info">No waivers are required for this registration. You can continue.</div>
        } @else {
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
                      <div *ngIf="w.html && w.html.trim().length; else noContent" [innerHTML]="w.html"></div>
                      <ng-template #noContent>
                        <div class="alert alert-light border small mb-0">No content provided for this waiver.</div>
                      </ng-template>
                    </div>
                    <div class="form-check">
                      <input class="form-check-input" type="checkbox" [checked]="isAccepted(w.id)"
                             [disabled]="isEditingMode()" [id]="'waiver-' + w.id"
                             (change)="onCheckboxChange(w.id, $event)"
                             [class.is-invalid]="submitted() && w.required && !isAccepted(w.id) && !isEditingMode()"
                             [attr.aria-invalid]="submitted() && w.required && !isAccepted(w.id) && !isEditingMode()" />
                      <label class="form-check-label" [for]="'waiver-' + w.id">
                        I have read and agree to the {{ w.title.toLowerCase() }}
                      </label>
                      @if (isAccepted(w.id) && isEditingMode()) {
                        <span class="badge bg-secondary ms-2" title="Previously accepted and locked">Locked</span>
                      }
                      @if (submitted() && w.required && !isAccepted(w.id) && !isEditingMode()) {
                        <div class="invalid-feedback d-block mt-1">Please accept this waiver to continue.</div>
                      }
                    </div>
                  </div>
                </div>
              </div>
            }
          </div>

          <!-- Signature capture removed per updated requirements: individual waiver acceptance only -->
        }

        <div class="rw-bottom-nav d-flex gap-2 mt-3">
          <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">Back</button>
          <button type="button" class="btn btn-primary" [disabled]="disableContinue()" (click)="handleContinue()">Continue</button>
        </div>
      </div>
    </div>
  `
})
export class WaiversComponent implements AfterViewInit {
  @Output() next = new EventEmitter<void>();
  @Output() back = new EventEmitter<void>();

  private readonly state = inject(RegistrationWizardService);

  waivers = () => this.state.waiverDefinitions();
  players = () => this.state.selectedPlayers();
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
  // Ensure first panel opened when view initialized
  ngAfterViewInit(): void { this.initFirst(); }

  // Editing mode detection (checkbox disabled when editing existing registration or when any selected player is already registered)
  isEditingMode(): boolean {
    if (this.state.startMode() === 'edit') return true;
    try {
      const selected = new Set(this.state.selectedPlayers().map(p => p.userId));
      const anyRegisteredSelected = this.state.familyPlayers().some(fp => fp.registered && selected.has(fp.playerId));
      return anyRegisteredSelected;
    } catch { return false; }
  }

  isAccepted = (id: string) => this.state.isWaiverAccepted(id);
  onCheckboxChange(id: string, ev: Event) {
    if (this.isEditingMode()) return; // read-only in edit mode
    const checked = (ev.target as HTMLInputElement | null)?.checked ?? false;
    this.state.setWaiverAccepted(id, checked);
  }

  disableContinue(): boolean {
    const waivers = this.waivers();
    const allAccepted = this.state.allRequiredWaiversAccepted();
    if (waivers.length === 0) return false; // no waivers, can continue
    return !allAccepted; // only gate on required waiver acceptance now
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
    const list = this.waivers();
    for (const w of list) {
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
}
