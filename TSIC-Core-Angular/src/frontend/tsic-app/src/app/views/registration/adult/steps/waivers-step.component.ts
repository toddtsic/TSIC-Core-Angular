import { AfterViewInit, ChangeDetectionStrategy, Component, OnDestroy, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

/**
 * Waivers step — matches the player wizard's pattern:
 * welcome-hero + info callout + Bootstrap accordion with status badges.
 * Auto-opens the first unchecked waiver on mount, advances to the next
 * unchecked when one is accepted, and auto-emits <c>advance</c> ~500ms
 * after the final waiver is accepted.
 */
@Component({
    selector: 'app-adult-waivers-step',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <!-- Centered hero -->
        <div class="welcome-hero">
            <h4 class="welcome-title">
                <i class="bi bi-shield-check welcome-icon" style="color: var(--bs-warning)"></i>
                Review &amp; Accept Waivers
            </h4>
            <p class="welcome-desc">
                <i class="bi bi-book me-1"></i>Read each waiver
                <span class="desc-dot"></span>
                <i class="bi bi-check-square me-1"></i>Check to accept
            </p>
        </div>

        <div class="card shadow border-0 card-rounded">
            <div class="card-body">
                @if (state.waivers().length === 0) {
                    <div class="alert alert-info">No waivers required for this event.</div>
                } @else {
                    <div class="wizard-callout wizard-callout-info">
                        <i class="bi bi-info-circle"></i>
                        <span>Read each waiver carefully and check to accept.</span>
                    </div>

                    <div class="accordion" id="adultWaiverAccordion">
                        @for (w of state.waivers(); track w.key; let i = $index) {
                            <div class="accordion-item">
                                <h2 class="accordion-header">
                                    <button class="accordion-button" type="button"
                                        [class.collapsed]="openIndex() !== i"
                                        (click)="toggleAccordion(i)"
                                        [attr.aria-expanded]="openIndex() === i"
                                        [attr.aria-controls]="'adult-waiver-' + i">
                                        @if (isAccepted(w.key)) {
                                            <span class="badge bg-success me-2">Accepted</span>
                                        } @else {
                                            <span class="badge bg-warning text-dark me-2">Not Accepted</span>
                                        }
                                        <span class="me-auto">{{ w.title }}</span>
                                    </button>
                                </h2>
                                @if (openIndex() === i) {
                                    <div [id]="'adult-waiver-' + i" class="accordion-collapse collapse show">
                                        <div class="accordion-body">
                                            <div class="waiver-html-content mb-3 border rounded p-3 bg-body-tertiary"
                                                style="max-height: 300px; overflow-y: auto"
                                                [innerHTML]="w.htmlContent"></div>
                                            <div class="form-check">
                                                <input class="form-check-input" type="checkbox"
                                                    [id]="'adult-waiver-check-' + i"
                                                    [checked]="isAccepted(w.key)"
                                                    (change)="onAcceptChange(w.key, $event)" />
                                                <label class="form-check-label fw-semibold" [for]="'adult-waiver-check-' + i">
                                                    I agree to the {{ w.title }}
                                                    <span class="text-danger">*</span>
                                                </label>
                                            </div>
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
})
export class WaiversStepComponent implements AfterViewInit, OnDestroy {
    readonly advance = output<void>();
    readonly state = inject(AdultWizardStateService);
    readonly openIndex = signal(0);

    private _autoAdvanceTimer: ReturnType<typeof setTimeout> | null = null;

    ngAfterViewInit(): void {
        // Auto-open first unaccepted waiver.
        const waivers = this.state.waivers();
        const idx = waivers.findIndex(w => !this.isAccepted(w.key));
        if (idx >= 0) this.openIndex.set(idx);
    }

    isAccepted(key: string): boolean {
        return !!this.state.waiverAcceptance()[key];
    }

    toggleAccordion(index: number): void {
        this.openIndex.set(this.openIndex() === index ? -1 : index);
    }

    onAcceptChange(key: string, event: Event): void {
        const checked = (event.target as HTMLInputElement).checked;
        this.state.setWaiverAccepted(key, checked);

        if (this._autoAdvanceTimer) clearTimeout(this._autoAdvanceTimer);

        if (checked) {
            const waivers = this.state.waivers();
            const nextIdx = waivers.findIndex(w => !this.isAccepted(w.key));
            if (nextIdx >= 0) {
                this.openIndex.set(nextIdx);
            } else {
                // All accepted — auto-advance after a short beat.
                this._autoAdvanceTimer = setTimeout(() => this.advance.emit(), 500);
            }
        }
    }

    ngOnDestroy(): void {
        if (this._autoAdvanceTimer) clearTimeout(this._autoAdvanceTimer);
    }
}
