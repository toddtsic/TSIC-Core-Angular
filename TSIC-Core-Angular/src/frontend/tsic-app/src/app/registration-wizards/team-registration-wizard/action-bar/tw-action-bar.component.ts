import { Component, EventEmitter, Input, Output, HostBinding } from '@angular/core';

@Component({
    selector: 'app-tw-action-bar',
    standalone: true,
    imports: [],
    templateUrl: './tw-action-bar.component.html',
    styleUrls: ['./tw-action-bar.component.scss']
})
export class TwActionBarComponent {
    @Input() canBack = false;
    @Input() canContinue = false;
    @Input() continueLabel = 'Continue';
    @Input() showContinue = true;
    @Input() placement: 'top' | 'bottom' = 'top';

    @Output() back = new EventEmitter<void>();
    @Output() continue = new EventEmitter<void>();

    // Determine whether there is any meaningful content to show.
    private get hasContent(): boolean {
        return this.canBack || this.showContinue;
    }

    // Hide host entirely when no content so empty toolbar doesn't consume vertical space.
    @HostBinding('style.display') get hostDisplay(): string {
        return this.hasContent ? 'block' : 'none';
    }
}
