import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
    selector: 'app-brand-preview',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './brand-preview.component.html',
    styleUrls: ['./brand-preview.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BrandPreviewComponent {
    activeTab = signal<string>('colors');

    selectTab(tab: string): void {
        this.activeTab.set(tab);
    }
}
