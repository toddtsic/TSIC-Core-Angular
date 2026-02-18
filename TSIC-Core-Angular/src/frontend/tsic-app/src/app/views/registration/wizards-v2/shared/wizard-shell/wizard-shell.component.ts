import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { StepIndicatorComponent, type StepDefinition } from '@shared-ui/components/step-indicator/step-indicator.component';
import { WizardActionBarComponent } from '../../../wizards/shared/wizard-action-bar/wizard-action-bar.component';
import { WizardThemeDirective } from '@shared-ui/directives/wizard-theme.directive';
import type { WizardStepDef, WizardShellConfig } from '../types/wizard-shell.types';

/**
 * Composition-based wizard shell.
 *
 * Provides: fixed header (title + step indicator + action bar) + scrollable content region.
 * Each wizard composes this shell and projects its step content via <ng-content />.
 *
 * The shell does NOT own step navigation logic — the parent wizard handles that
 * and communicates via signal inputs + back/continue outputs.
 *
 * Usage:
 * ```html
 * <app-wizard-shell
 *   [steps]="steps()" [currentIndex]="currentIndex()" [config]="shellConfig()"
 *   [canContinue]="canContinue()" [continueLabel]="continueLabel()"
 *   [showContinue]="showContinue()"
 *   (back)="back()" (continue)="onContinue()">
 *
 *   @switch (currentStepId()) {
 *     @case ('step-1') { <app-step-one /> }
 *     @case ('step-2') { <app-step-two /> }
 *   }
 * </app-wizard-shell>
 * ```
 */
@Component({
    selector: 'app-wizard-shell',
    standalone: true,
    imports: [StepIndicatorComponent, WizardActionBarComponent, WizardThemeDirective],
    templateUrl: './wizard-shell.component.html',
    styleUrls: ['./wizard-shell.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WizardShellComponent {
    // ── Inputs ──────────────────────────────────────────────────────────
    /** Full step list (shell filters to enabled steps). */
    readonly steps = input.required<WizardStepDef[]>();
    /** 0-based index into the *active* steps array. */
    readonly currentIndex = input.required<number>();
    /** Wizard identity: title, theme, optional badge. */
    readonly config = input.required<WizardShellConfig>();

    /** Whether the Continue button should be enabled. */
    readonly canContinue = input(false);
    /** Label for the Continue button. */
    readonly continueLabel = input('Continue');
    /** Whether to show the Continue button at all. */
    readonly showContinue = input(true);
    /** Optional badge text on the action bar (e.g. "$120.00 due"). */
    readonly detailsBadgeLabel = input<string | null>(null);
    /** CSS class for the action bar badge. */
    readonly detailsBadgeClass = input('badge-danger');

    // ── Outputs ─────────────────────────────────────────────────────────
    readonly back = output<void>();
    readonly continue = output<void>();

    // ── Computed ─────────────────────────────────────────────────────────
    /** Only enabled steps — the shell hides disabled ones entirely. */
    readonly activeSteps = computed(() => this.steps().filter(s => s.enabled));

    /** Mapped to StepDefinition[] for the StepIndicatorComponent. */
    readonly stepDefinitions = computed<StepDefinition[]>(() =>
        this.activeSteps().map((s, i) => ({
            id: s.id,
            label: s.label,
            stepNumber: i + 1,
        })),
    );

    /** Show the action bar when we're past the first step (first step typically has its own CTAs). */
    readonly showActionBar = computed(() => this.currentIndex() > 0);
}
