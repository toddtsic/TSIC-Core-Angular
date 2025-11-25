import { Component, EventEmitter, Input, Output, HostBinding } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';

@Component({
    selector: 'app-rw-action-bar',
    standalone: true,
    imports: [MatButtonModule],
    templateUrl: './rw-action-bar.component.html',
    styleUrls: ['./rw-action-bar.component.scss']
})
export class RwActionBarComponent {
    @Input() familyLastName: string | null = null;
    @Input() canBack = false;
    @Input() canContinue = false;
    @Input() continueLabel = 'Continue';
    @Input() showContinue = true;
    @Input() placement: 'top' | 'bottom' = 'top';

    @Output() back = new EventEmitter<void>();
    @Output() continue = new EventEmitter<void>();

    // Determine whether there is any meaningful content to show.
    private get hasContent(): boolean {
        return !!(this.familyLastName || this.canBack || this.showContinue);
    }

    // Hide host entirely when no content so empty toolbar doesn't consume vertical space.
    @HostBinding('style.display') get hostDisplay(): string {
        return this.hasContent ? 'block' : 'none';
    }
}
