import { Component, input, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import type { BulletinDto } from '@core/api';
import { TranslateLegacyUrlsPipe } from '@infrastructure/pipes/translate-legacy-urls.pipe';
import { InternalLinkDirective } from '@infrastructure/directives/internal-link.directive';

/**
 * Bulletins Display Component
 * 
 * Displays active bulletins for a job with legacy URL translation and secure in-app routing
 */
@Component({
    selector: 'app-bulletins',
    standalone: true,
    imports: [CommonModule, TranslateLegacyUrlsPipe, InternalLinkDirective],
    templateUrl: './bulletins.component.html',
    styleUrl: './bulletins.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulletinsComponent {
    bulletins = input<BulletinDto[]>([]);
    loading = input<boolean>(false);
    error = input<string | null>(null);
    jobPath = input<string>('');
}
