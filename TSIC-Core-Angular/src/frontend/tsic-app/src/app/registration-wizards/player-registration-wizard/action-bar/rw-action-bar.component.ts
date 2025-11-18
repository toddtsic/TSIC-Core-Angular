import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
    selector: 'app-rw-action-bar',
    standalone: true,
    imports: [],
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
}
