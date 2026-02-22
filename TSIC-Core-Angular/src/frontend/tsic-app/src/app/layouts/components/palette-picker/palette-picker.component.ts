import { Component, inject, input, output } from '@angular/core';
import { PaletteService } from '../../../infrastructure/services/palette.service';

@Component({
  selector: 'app-palette-picker',
  standalone: true,
  templateUrl: './palette-picker.component.html',
  styleUrl: './palette-picker.component.scss'
})
export class PalettePickerComponent {
  readonly paletteService = inject(PaletteService);

  /** 'sm' = 20px swatches (nav bar), 'md' = 28px swatches (header dropdown) */
  readonly size = input<'sm' | 'md'>('md');

  /** Emits after a swatch is clicked — lets the host close its menu */
  readonly selected = output<void>();

  onSwatchClick(index: number): void {
    this.paletteService.togglePalette(index);
    this.selected.emit();
  }
}
