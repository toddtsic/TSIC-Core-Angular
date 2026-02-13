import { ChangeDetectionStrategy, Component, input, output, computed } from '@angular/core';

/**
 * Player Registration Action Bar - Modern Angular 21 signals-based component.
 * Provides navigation controls (Back, Continue) and family context badge.
 * Supports sticky top placement with light/dark mode via CSS variables.
 * 
 * Usage:
 * <app-rw-action-bar
 *   [familyLastName]="familyLastName()"
 *   [canBack]="currentIndex() > 0"
 *   [canContinue]="canContinueStep()"
 *   [continueLabel]="'Proceed to Payment'"
 *   [showContinue]="showContinueButton()"
 *   (back)="back()"
 *   (continue)="onContinue()"
 * />
 */

@Component({
    selector: 'app-rw-action-bar',
    standalone: true,
    imports: [],
    templateUrl: './rw-action-bar.component.html',
    styleUrls: ['./rw-action-bar.component.scss'],
    host: {
        '[style.display]': 'hasContent() ? "block" : "none"'
    },
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class RwActionBarComponent {
    // Signal inputs (Angular 21 modern pattern)
    readonly familyLastName = input<string | null>(null);
    readonly canBack = input(false);
    readonly canContinue = input(false);
    readonly continueLabel = input('Continue');
    readonly showContinue = input(true);
    readonly placement = input<'top' | 'bottom'>('top');

    // Signal outputs
    readonly back = output<void>();
    readonly continue = output<void>();

    // Computed property: determine whether there is any meaningful content to show
    readonly hasContent = computed(() => {
        return !!(this.familyLastName() || this.canBack() || this.showContinue());
    });
}
