import { ChangeDetectionStrategy, Component, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PaletteService } from '@infrastructure/services/palette.service';

@Component({
    selector: 'app-brand-preview',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './brand-preview.component.html',
    styleUrls: ['./brand-preview.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BrandPreviewComponent {
    readonly paletteService = inject(PaletteService);
    activeTab = signal<string>('colors');

    selectTab(tab: string): void {
        this.activeTab.set(tab);
    }
}
