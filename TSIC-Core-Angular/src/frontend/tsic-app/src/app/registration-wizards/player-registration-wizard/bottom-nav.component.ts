import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';

@Component({
    selector: 'app-rw-bottom-nav',
    standalone: true,
    imports: [CommonModule, MatButtonModule],
    template: `
    <div class="rw-bottom-nav d-flex gap-2" [class.mt-3]="addTopMargin" [class.border-top]="showBorderTop" [class.pt-3]="showBorderTop">
    @if (!hideBack) { <button type="button" mat-stroked-button (click)="back.emit()">{{ backLabel }}</button> }
      <button type="button" mat-raised-button color="primary" [disabled]="nextDisabled" (click)="next.emit()">{{ nextLabel }}</button>
    </div>
  `
})
export class BottomNavComponent {
    @Input() nextLabel = 'Continue';
    @Input() backLabel = 'Back';
    @Input() nextDisabled = false;
    @Input() hideBack = false;
    @Input() addTopMargin = true;
    @Input() showBorderTop = false;
    @Output() next = new EventEmitter<void>();
    @Output() back = new EventEmitter<void>();
}
