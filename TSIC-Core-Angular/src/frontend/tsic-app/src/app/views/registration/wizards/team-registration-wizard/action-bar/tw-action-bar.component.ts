import {
    Component,
    HostBinding,
    input,
    output,
    computed,
    ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-tw-action-bar',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './tw-action-bar.component.html',
    styleUrls: ['./tw-action-bar.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush, // Angular 21 performance optimization
})
export class TwActionBarComponent {
    // Signal inputs - Angular 21 feature
    canBack = input<boolean>(false);
    canContinue = input<boolean>(false);
    continueLabel = input<string>('Continue');
    showContinue = input<boolean>(true);
    placement = input<'top' | 'bottom'>('top');

    // Contextual details inputs
    detailsBadgeLabel = input<string | null>(null); // e.g., "Payment Due: $2,200"
    detailsBadgeClass = input<string>('badge-danger'); // CSS class for badge styling

    // Signal outputs - Angular 21 feature
    back = output<void>();
    continue = output<void>();

    // Computed signal to determine whether there is any meaningful content to show
    private readonly hasContent = computed(
        () => this.canBack() || this.showContinue(),
    );

    // Computed signal to check if we should show contextual details
    readonly showDetails = computed(() => !!this.detailsBadgeLabel());

    // Hide host entirely when no content so empty toolbar doesn't consume vertical space.
    @HostBinding('style.display') get hostDisplay(): string {
        return this.hasContent() ? 'block' : 'none';
    }
}
