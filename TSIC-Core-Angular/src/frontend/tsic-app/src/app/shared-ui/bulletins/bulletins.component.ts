import { Component, input, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import type { BulletinDto } from '@infrastructure/api';

/**
 * Bulletins Display Component
 * 
 * Displays active bulletins for a job
 */
@Component({
    selector: 'app-bulletins',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './bulletins.component.html',
    styleUrl: './bulletins.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulletinsComponent {
    bulletins = input<BulletinDto[]>([]);
    loading = input<boolean>(false);
    error = input<string | null>(null);
}
