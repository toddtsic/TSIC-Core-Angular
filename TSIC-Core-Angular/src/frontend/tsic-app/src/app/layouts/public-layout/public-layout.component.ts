import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { ThemeService } from '../../core/services/theme.service';
import { MatButtonModule } from '@angular/material/button';

/**
 * PublicLayoutComponent
 * 
 * Layout for non-job-specific routes such as:
 * - Login (/tsic/login)
 * - Role selection (/tsic/role-selection)
 * - Help documents
 * - TeamSportsInfo.com marketing/info pages
 * 
 * Features:
 * - TeamSportsInfo.com branded header
 * - Theme toggle (light/dark mode)
 * - Clean, minimal navigation
 * - Centered content area for forms and information
 */
@Component({
    selector: 'app-public-layout',
    standalone: true,
    imports: [CommonModule, RouterOutlet, MatButtonModule],
    templateUrl: './public-layout.component.html',
    styleUrls: ['./public-layout.component.scss']
})
export class PublicLayoutComponent {
    readonly themeService = inject(ThemeService);
    readonly currentYear = new Date().getFullYear();

    toggleTheme(): void {
        this.themeService.toggleTheme();
    }
}
