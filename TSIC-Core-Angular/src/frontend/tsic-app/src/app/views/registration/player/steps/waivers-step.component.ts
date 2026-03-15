import { ChangeDetectionStrategy, Component, inject, signal, AfterViewInit, DestroyRef } from '@angular/core';
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
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Waivers</h5>
      </div>
      <div class="card-body">
        @if (waiverDefs().length === 0) {
          <div class="alert alert-info">No waivers required for this event.</div>
        } @else {
          <div class="alert alert-secondary border-0 small mb-3">
            These waivers apply to all selected players. Please read each waiver
            and check the box to accept.
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
                    <span class="me-auto">{{ w.title }}</span>
                    @if (isAccepted(w.id)) {
                      <span class="badge bg-success ms-2">Accepted</span>
                    } @else {
                      <span class="badge bg-warning text-dark ms-2">Not Accepted</span>
                    }
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

          <!-- Signature field -->
          @if (state.jobCtx.requireSignature()) {
            <div class="mt-4 p-3 rounded-3 border">
              <h6 class="fw-semibold mb-2">Signature</h6>
              <div class="row g-3">
                <div class="col-12 col-md-6">
                  <label for="sigName" class="form-label">Full Name <span class="text-danger">*</span></label>
                  <input type="text" class="form-control" id="sigName"
                         [ngModel]="state.jobCtx.signatureName()"
                         (ngModelChange)="state.jobCtx.setSignatureName($event)"
                         placeholder="Your full name">
                </div>
                <div class="col-12 col-md-6">
                  <label for="sigRole" class="form-label">Role</label>
                  <select class="form-select" id="sigRole"
                          [ngModel]="state.jobCtx.signatureRole()"
                          (ngModelChange)="state.jobCtx.setSignatureRole($event)">
                    <option value="">— Select —</option>
                    <option value="Parent/Guardian">Parent/Guardian</option>
                    <option value="Adult Player">Adult Player</option>
                  </select>
                </div>
              </div>
            </div>
          }
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WaiversStepComponent implements AfterViewInit {
    readonly state = inject(PlayerWizardStateService);
    readonly openIndex = signal(0);

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

        // Auto-advance to next unchecked waiver
        if (checked) {
            const defs = this.waiverDefs();
            const nextIdx = defs.findIndex(w => !this.isAccepted(w.id));
            if (nextIdx >= 0) this.openIndex.set(nextIdx);
        }
    }
}
