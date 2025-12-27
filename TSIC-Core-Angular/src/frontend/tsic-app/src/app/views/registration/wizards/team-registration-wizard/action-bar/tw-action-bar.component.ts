import { Component, HostBinding, input, output, computed, ChangeDetectionStrategy } from '@angular/core';


@Component({
    selector: 'app-tw-action-bar',
    standalone: true,
    imports: [],
    templateUrl: './tw-action-bar.component.html',
    styleUrls: ['./tw-action-bar.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush // Angular 21 performance optimization
})
export class TwActionBarComponent {
    // Signal inputs - Angular 21 feature
    canBack = input<boolean>(false);
    canContinue = input<boolean>(false);
    continueLabel = input<string>('Continue');
    showContinue = input<boolean>(true);
    placement = input<'top' | 'bottom'>('top');

    // Signal outputs - Angular 21 feature
    back = output<void>();
    continue = output<void>();

    // Computed signal to determine whether there is any meaningful content to show
    private hasContent = computed(() => this.canBack() || this.showContinue());

    // Hide host entirely when no content so empty toolbar doesn't consume vertical space.
    @HostBinding('style.display') get hostDisplay(): string {
        return this.hasContent() ? 'block' : 'none';
    }
}
