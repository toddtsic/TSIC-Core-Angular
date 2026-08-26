import { Component, input, output, computed, ChangeDetectionStrategy } from '@angular/core';

/**
 * Unified Wizard Action Bar Component
 * 
 * Used by both Player and Team registration wizards.
 * Provides navigation controls (Back, Continue) with distinctive modern styling.
 * Supports responsive design (.82rem on mobile, responsive button sizing).
 * 
 * The parent `.wizard-action-bar-container` provides the sticky positioning context.
 * This component is the inner element and handles all visual styling and interactions.
 * 
 * Usage:
 * <div class="wizard-action-bar-container">
 *   <app-wizard-action-bar
 *     [canBack]="currentIndex() > 0"
 *     [canContinue]="canContinueStep()"
 *     [continueLabel]="'Proceed to Review'"
 *     (back)="back()"
 *     (continue)="onContinue()"
 *   />
 * </div>
 */

@Component({
    selector: 'app-wizard-action-bar',
    standalone: true,
    imports: [],
    templateUrl: './wizard-action-bar.component.html',
    styleUrls: ['./wizard-action-bar.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WizardActionBarComponent {
    // Signal inputs
    readonly canBack = input(false);
    readonly canContinue = input(false);
    /** When true, Continue is disabled and shows a spinner — an async step transition is in flight. */
    readonly busy = input(false);
    readonly continueLabel = input('Continue');
    /**
     * What the forward button DOES, which decides its icon and how a screen reader announces it.
     * 'proceed' (default) = another step follows: chevron, "Proceed: <label>".
     * 'done' = terminal step, the registration is already complete and the button only navigates
     * home: house icon, "<label>" alone. AR-035 — a forward chevron on a confirmation screen
     * implies there is something left to do, which is the whole complaint. Every non-terminal
     * step keeps today's behaviour by default.
     */
    readonly continueIntent = input<'proceed' | 'done'>('proceed');
    readonly showContinue = input(true);
    readonly detailsBadgeLabel = input<string | null>(null);
    readonly detailsBadgeClass = input<string>('badge-danger');

    // Signal outputs
    readonly back = output<void>();
    readonly continue = output<void>();

    // Computed: determine if there's content to display
    readonly hasContent = computed(() => this.canBack() || this.showContinue());

    // Computed: determine if badge should be shown
    readonly showBadge = computed(() => !!this.detailsBadgeLabel());

    // Computed: strip "Proceed to " prefix from continue label for mobile view
    /** Forward-button icon: a chevron while steps remain, a house on the terminal step. */
    readonly continueIcon = computed(() =>
        this.continueIntent() === 'done' ? 'bi-house' : 'bi-chevron-right');

    /** Screen-reader label. "Proceed:" would repeat the false implication on a terminal step. */
    readonly continueAriaLabel = computed(() =>
        this.continueIntent() === 'done' ? this.continueLabel() : 'Proceed: ' + this.continueLabel());

    readonly continueLabelShort = computed(() => {
        const label = this.continueLabel();
        return label.replace(/^Proceed to\s+/i, '');
    });
}
