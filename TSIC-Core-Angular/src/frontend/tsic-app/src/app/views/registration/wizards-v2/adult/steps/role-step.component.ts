import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { AdultWizardStateService } from '../state/adult-wizard-state.service';
import type { AdultRoleOption } from '@infrastructure/services/adult-registration.service';

@Component({
    selector: 'app-adult-role-step',
    standalone: true,
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="step-content">
            <h3 class="step-title">Select Your Role</h3>
            <p class="text-muted mb-4">Choose how you'd like to participate in this event.</p>

            @if (state.jobLoading()) {
                <div class="text-center py-4">
                    <div class="spinner-border text-primary" role="status">
                        <span class="visually-hidden">Loading...</span>
                    </div>
                </div>
            }

            @if (state.jobError()) {
                <div class="alert alert-danger" role="alert">{{ state.jobError() }}</div>
            }

            <div class="role-cards">
                @for (role of state.availableRoles(); track role.roleType) {
                    <button class="role-card"
                        [class.selected]="state.selectedRole()?.roleType === role.roleType"
                        (click)="selectRole(role)">
                        <div class="role-icon">
                            @switch (role.roleType) {
                                @case (0) { <i class="bi bi-person-badge"></i> }
                                @case (1) { <i class="bi bi-whistle"></i> }
                                @case (2) { <i class="bi bi-mortarboard"></i> }
                                @default { <i class="bi bi-person"></i> }
                            }
                        </div>
                        <div class="role-info">
                            <h5 class="role-name mb-1">{{ role.displayName }}</h5>
                            <p class="role-desc text-muted mb-0">{{ role.description }}</p>
                        </div>
                        @if (state.selectedRole()?.roleType === role.roleType) {
                            <i class="bi bi-check-circle-fill text-success check-icon"></i>
                        }
                    </button>
                }
            </div>

            @if (state.schemaLoading()) {
                <div class="text-center py-3 mt-3">
                    <div class="spinner-border spinner-border-sm text-primary" role="status">
                        <span class="visually-hidden">Loading form...</span>
                    </div>
                    <span class="ms-2 text-muted">Loading form fields...</span>
                </div>
            }
        </div>
    `,
    styles: [`
        .role-cards {
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
        }
        .role-card {
            display: flex;
            align-items: center;
            gap: var(--space-4);
            padding: var(--space-4);
            border: 2px solid var(--border-color);
            border-radius: var(--radius-md);
            background: var(--brand-surface);
            cursor: pointer;
            transition: border-color 0.2s, box-shadow 0.2s;
            text-align: left;
            width: 100%;
        }
        .role-card:hover {
            border-color: var(--bs-primary);
        }
        .role-card.selected {
            border-color: var(--bs-primary);
            box-shadow: var(--shadow-focus);
        }
        .role-icon {
            font-size: var(--font-size-2xl);
            color: var(--bs-primary);
            flex-shrink: 0;
            width: 48px;
            text-align: center;
        }
        .role-info {
            flex: 1;
        }
        .role-name {
            font-size: var(--font-size-lg);
            font-weight: var(--font-weight-semibold);
        }
        .role-desc {
            font-size: var(--font-size-sm);
        }
        .check-icon {
            font-size: var(--font-size-xl);
            flex-shrink: 0;
        }
    `],
})
export class RoleStepComponent {
    readonly state = inject(AdultWizardStateService);
    readonly jobPath = input.required<string>();

    selectRole(role: AdultRoleOption): void {
        this.state.selectRole(role, this.jobPath());
    }
}
