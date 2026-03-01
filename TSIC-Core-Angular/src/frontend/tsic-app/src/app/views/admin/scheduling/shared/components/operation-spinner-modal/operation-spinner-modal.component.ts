import { Component, ChangeDetectionStrategy, input } from '@angular/core';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

@Component({
    selector: 'app-operation-spinner-modal',
    standalone: true,
    imports: [TsicDialogComponent],
    templateUrl: './operation-spinner-modal.component.html',
    styleUrl: './operation-spinner-modal.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class OperationSpinnerModalComponent {
    readonly open = input(false);
    readonly title = input('Processing...');
    readonly subtitle = input('');
    readonly icon = input('bi-lightning-charge-fill');
}
