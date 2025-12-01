import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-tw-action-bar',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './tw-action-bar.component.html',
    styleUrls: ['./tw-action-bar.component.scss']
})
export class TwActionBarComponent {
    @Input() canBack: boolean = false;
    @Input() canContinue: boolean = true;
    @Input() continueLabel: string = 'Continue';
    @Input() showContinue: boolean = true;
    @Input() placement: 'top' | 'bottom' = 'top';
    @Output() back = new EventEmitter<void>();
    @Output() continue = new EventEmitter<void>();
}
