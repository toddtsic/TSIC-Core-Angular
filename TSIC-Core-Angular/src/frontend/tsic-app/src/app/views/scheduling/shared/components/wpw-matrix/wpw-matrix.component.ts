import { Component, ChangeDetectionStrategy, input, computed } from '@angular/core';

@Component({
    selector: 'app-wpw-matrix',
    standalone: true,
    templateUrl: './wpw-matrix.component.html',
    styleUrl: './wpw-matrix.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class WpwMatrixComponent {
    readonly matrix = input<number[][] | null>(null);
    readonly teamCount = input(0);
    readonly isLoading = input(false);

    readonly teamRange = computed(() =>
        Array.from({ length: this.teamCount() }, (_, i) => i + 1)
    );
}
