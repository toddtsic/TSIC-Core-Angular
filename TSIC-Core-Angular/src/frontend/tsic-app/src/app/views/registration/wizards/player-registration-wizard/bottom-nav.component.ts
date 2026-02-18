import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
@Component({
    selector: 'app-rw-bottom-nav',
    standalone: true,
    imports: [],
    template: `
    <div class="rw-bottom-nav d-flex gap-2" [class.mt-3]="addTopMargin" [class.border-top]="showBorderTop" [class.pt-3]="showBorderTop">
  @if (!hideBack) { <button type="button" class="btn btn-outline-secondary" (click)="back.emit()">{{ backLabel }}</button> }
      <button type="button" class="btn btn-primary" [disabled]="nextDisabled" (click)="next.emit()">{{ nextLabel }}</button>
    </div>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush
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
