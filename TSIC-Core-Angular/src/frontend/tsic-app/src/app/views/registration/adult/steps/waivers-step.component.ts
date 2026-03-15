import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';

@Component({
    selector: 'app-adult-waivers-step',
    standalone: true,
    imports: [FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            <h3 class="step-title">Waivers & Agreements</h3>
            <p class="text-muted mb-4">Please read and accept the following policies to continue.</p>

            @for (waiver of state.waivers(); track waiver.key) {
                <div class="waiver-card mb-3">
                    <div class="waiver-header" (click)="toggleExpanded(waiver.key)">
                        <h5 class="mb-0">{{ waiver.title }}</h5>
                        <i class="bi" [class.bi-chevron-down]="!isExpanded(waiver.key)" [class.bi-chevron-up]="isExpanded(waiver.key)"></i>
                    </div>
                    @if (isExpanded(waiver.key)) {
                        <div class="waiver-body" [innerHTML]="waiver.htmlContent"></div>
                    }
                    <div class="waiver-footer">
                        <div class="form-check">
                            <input type="checkbox" class="form-check-input"
                                [id]="'waiver-' + waiver.key"
                                [ngModel]="!!state.waiverAcceptance()[waiver.key]"
                                (ngModelChange)="onAcceptChange(waiver.key, $event)" />
                            <label class="form-check-label fw-medium" [for]="'waiver-' + waiver.key">
                                I have read and agree to the {{ waiver.title }}
                            </label>
                        </div>
                    </div>
                </div>
            }
        </div>
    `,
    styles: [`
        .waiver-card {
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            overflow: hidden;
        }
        .waiver-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding: var(--space-3) var(--space-4);
            background: rgba(var(--bs-primary-rgb), 0.05);
            cursor: pointer;
        }
        .waiver-header:hover {
            background: rgba(var(--bs-primary-rgb), 0.1);
        }
        .waiver-body {
            padding: var(--space-4);
            max-height: 300px;
            overflow-y: auto;
            font-size: var(--font-size-sm);
            line-height: var(--line-height-relaxed);
            border-top: 1px solid var(--border-color);
            border-bottom: 1px solid var(--border-color);
        }
        .waiver-footer {
            padding: var(--space-3) var(--space-4);
        }
    `],
})
export class WaiversStepComponent {
    readonly state = inject(AdultWizardStateService);
    private expandedKeys = new Set<string>();

    constructor() {
        // Auto-expand first waiver
        const waivers = this.state.waivers();
        if (waivers.length > 0) {
            this.expandedKeys.add(waivers[0].key);
        }
    }

    isExpanded(key: string): boolean {
        return this.expandedKeys.has(key);
    }

    toggleExpanded(key: string): void {
        if (this.expandedKeys.has(key)) {
            this.expandedKeys.delete(key);
        } else {
            this.expandedKeys.add(key);
        }
    }

    onAcceptChange(key: string, accepted: boolean): void {
        this.state.setWaiverAccepted(key, accepted);
    }
}
