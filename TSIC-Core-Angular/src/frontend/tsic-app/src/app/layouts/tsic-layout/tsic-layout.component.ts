import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from '@infrastructure/services/theme.service';

/**
 * TsicLayoutComponent
 * 
 * Layout for TSIC corporate (TeamSportsInfo.com) routes such as:
 * - Login (/tsic/login)
 * - Role selection (/tsic/role-selection)
 * - Help documents
 * - TeamSportsInfo.com marketing/info pages
 * 
 * Used for both authenticated and unauthenticated TSIC pages.
 * Distinction from job layout is organizational context (TSIC vs specific job).
 * 
 * Features:
 * - TeamSportsInfo.com branded header
 * - Theme toggle (light/dark mode)
 * - Clean, minimal navigation
 * - Centered content area for forms and information
 */
@Component({
    selector: 'app-tsic-layout',
    standalone: true,
    imports: [CommonModule, RouterOutlet],
    templateUrl: './tsic-layout.component.html',
    styleUrls: ['./tsic-layout.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TsicLayoutComponent {
    readonly themeService = inject(ThemeService);
    readonly currentYear = new Date().getFullYear();

    toggleTheme(): void {
        this.themeService.toggleTheme();
    }
}
