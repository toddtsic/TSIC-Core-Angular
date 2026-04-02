import { ChangeDetectionStrategy, Component, inject, signal, output, AfterViewInit, OnDestroy, DestroyRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';

/**
 * Waivers step — accordion showing each waiver's HTML content with an
 * acceptance checkbox. Includes optional signature field.
 */
@Component({
    selector: 'app-prw-waivers-step',
    standalone: true,
    imports: [FormsModule],
    template: `
    <!-- Centered hero -->
    <div class="welcome-hero">
      <h4 class="welcome-title"><i class="bi bi-shield-check welcome-icon" style="color: var(--bs-warning)"></i> Review & Accept Waivers</h4>
      <p class="welcome-desc">
        <i class="bi bi-book me-1"></i>Read each waiver
        <span class="desc-dot"></span>
        <i class="bi bi-check-square me-1"></i>Check to accept
        <span class="desc-dot"></span>
        <i class="bi bi-people me-1"></i>Applies to all selected players
      </p>
    </div>

    <div class="card shadow border-0 card-rounded">
      <div class="card-body">
        @if (waiverDefs().length === 0) {
          <div class="alert alert-info">No waivers required for this event.</div>
        } @else {
          <div class="wizard-callout wizard-callout-info">
            <i class="bi bi-info-circle"></i>
            <span>These waivers apply to <strong>all selected players</strong>. Read each one and check to accept.</span>
          </div>

          <!-- Player badges -->
          <div class="d-flex flex-wrap gap-1 mb-3">
            @for (pid of selectedPlayerIds(); track pid) {
              <span class="badge bg-primary-subtle text-primary-emphasis border border-primary-subtle">
                {{ getPlayerName(pid) }}
              </span>
            }
          </div>

          <!-- Accordion -->
          <div class="accordion" id="waiverAccordion">
            @for (w of waiverDefs(); track w.id; let i = $index) {
              <div class="accordion-item">
                <h2 class="accordion-header">
                  <button class="accordion-button" type="button"
                          [class.collapsed]="openIndex() !== i"
                          (click)="toggleAccordion(i)"
                          [attr.aria-expanded]="openIndex() === i"
                          [attr.aria-controls]="'waiver-' + i">
                    @if (isAccepted(w.id)) {
                      <span class="badge bg-success me-2">Accepted</span>
                    } @else {
                      <span class="badge bg-warning text-dark me-2">Not Accepted</span>
                    }
                    <span class="me-auto">{{ w.title }}</span>
                  </button>
                </h2>
                @if (openIndex() === i) {
                  <div [id]="'waiver-' + i" class="accordion-collapse collapse show">
                    <div class="accordion-body">
                      @if (w.html) {
                        <div class="waiver-html-content mb-3 border rounded p-3 bg-body-tertiary"
                             style="max-height: 300px; overflow-y: auto"
                             [innerHTML]="w.html"></div>
                      }
                      <div class="form-check">
                        <input class="form-check-input" type="checkbox"
                               [id]="'waiver-check-' + i"
                               [checked]="isAccepted(w.id)"
                               [disabled]="isLocked(w.id)"
                               (change)="onAcceptChange(w.id, $event)">
                        <label class="form-check-label fw-semibold" [for]="'waiver-check-' + i">
                          I agree to the {{ w.title }}
                          @if (w.required) { <span class="text-danger">*</span> }
                        </label>
                      </div>
                      @if (isLocked(w.id)) {
                        <span class="badge bg-secondary mt-1">Previously accepted</span>
                      }
                    </div>
                  </div>
                }
              </div>
            }
          </div>

        }
      </div>
    </div>
  `,
    styles: [],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WaiversStepComponent implements AfterViewInit, OnDestroy {
    readonly advance = output<void>();
    readonly state = inject(PlayerWizardStateService);
    readonly openIndex = signal(0);
    private _autoAdvanceTimer: ReturnType<typeof setTimeout> | null = null;

    ngAfterViewInit(): void {
        // Auto-open first unchecked waiver
        const defs = this.waiverDefs();
        const idx = defs.findIndex(w => !this.isAccepted(w.id));
        if (idx >= 0) this.openIndex.set(idx);
    }

    waiverDefs() {
        return this.state.jobCtx.waiverDefinitions();
    }

    selectedPlayerIds(): string[] {
        return this.state.familyPlayers.selectedPlayerIds();
    }

    getPlayerName(playerId: string): string {
        const p = this.state.familyPlayers.familyPlayers().find(fp => fp.playerId === playerId);
        return p ? `${p.firstName} ${p.lastName}`.trim() : playerId;
    }

    isAccepted(id: string): boolean {
        return this.state.jobCtx.isWaiverAccepted(id);
    }

    isLocked(id: string): boolean {
        // Locked if all selected players are already registered
        const selected = this.state.familyPlayers.selectedPlayerIds();
        const players = this.state.familyPlayers.familyPlayers();
        return selected.every(pid => {
            const p = players.find(fp => fp.playerId === pid);
            return !!p?.registered;
        });
    }

    toggleAccordion(index: number): void {
        this.openIndex.set(this.openIndex() === index ? -1 : index);
    }

    onAcceptChange(waiverId: string, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        this.state.jobCtx.setWaiverAccepted(waiverId, checked);

        if (this._autoAdvanceTimer) clearTimeout(this._autoAdvanceTimer);

        if (checked) {
            const defs = this.waiverDefs();
            const nextIdx = defs.findIndex(w => !this.isAccepted(w.id));
            if (nextIdx >= 0) {
                // Open next unchecked waiver
                this.openIndex.set(nextIdx);
            } else {
                // All waivers accepted — auto-advance after 500ms
                this._autoAdvanceTimer = setTimeout(() => this.advance.emit(), 500);
            }
        }
    }

    ngOnDestroy(): void {
        if (this._autoAdvanceTimer) clearTimeout(this._autoAdvanceTimer);
    }
}
