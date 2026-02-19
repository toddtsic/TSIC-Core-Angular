import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';

import { Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { PaletteService } from '@infrastructure/services/palette.service';
import { ThemeService } from '@infrastructure/services/theme.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import { MenuStateService } from '../../services/menu-state.service';

/** Admin roles that can customize dashboards */
const ADMIN_ROLES = ['Superuser', 'Director', 'SuperDirector'];

@Component({
    selector: 'app-client-header-bar',
    standalone: true,
    templateUrl: './client-header-bar.component.html',
    styleUrls: ['./client-header-bar.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ClientHeaderBarComponent {
    private readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly router = inject(Router);
    readonly themeService = inject(ThemeService);
    readonly paletteService = inject(PaletteService);
    private readonly menuState = inject(MenuStateService);

    // Admin check for dashboard customization
    readonly isAdmin = computed(() => {
        const user = this.auth.currentUser();
        const roles = user?.roles || (user?.role ? [user.role] : []);
        return roles.some(r => ADMIN_ROLES.includes(r));
    });

    // Job-related signals
    jobLogoPath = computed(() => {
        const job = this.jobService.currentJob();
        if (job?.jobLogoPath) {
            return buildAssetUrl(job.jobLogoPath);
        }
        return '';
    });

    jobName = computed(() => this.jobService.currentJob()?.jobName || '');

    // Single computed `user` derived from AuthService; derive UI values from it.
    user = computed(() => this.auth.currentUser());

    // Desktop dropdown state
    userMenuOpen = signal(false);
    paletteExpanded = signal(false);
    menuTop = signal(0);
    menuRight = signal(0);

    // Mobile palette panel state
    mobilePaletteOpen = signal(false);
    mobilePaletteTop = signal(0);
    mobilePaletteRight = signal(0);

    // Mobile menu toggle
    toggleOffcanvas() {
        this.menuState.toggleOffcanvas();
    }

    toggleUserMenu(event: Event) {
        event.stopPropagation();
        const wasOpen = this.userMenuOpen();
        this.userMenuOpen.set(!wasOpen);

        if (!wasOpen) {
            const btn = event.currentTarget as HTMLElement;
            const rect = btn.getBoundingClientRect();
            this.menuTop.set(rect.bottom + 8);
            this.menuRight.set(window.innerWidth - rect.right);
        }
    }

    closeUserMenu() {
        this.userMenuOpen.set(false);
        this.paletteExpanded.set(false);
    }

    toggleMobilePalette(event: Event) {
        event.stopPropagation();
        const wasOpen = this.mobilePaletteOpen();
        this.mobilePaletteOpen.set(!wasOpen);

        if (!wasOpen) {
            const btn = event.currentTarget as HTMLElement;
            const rect = btn.getBoundingClientRect();
            this.mobilePaletteTop.set(rect.bottom + 8);
            this.mobilePaletteRight.set(window.innerWidth - rect.right);
        }
    }

    closeMobilePalette() {
        this.mobilePaletteOpen.set(false);
    }

    switchRole() {
        this.closeUserMenu();
        const jobPath = this.jobService.currentJob()?.jobPath || 'tsic';
        this.router.navigate([`/${jobPath}/role-selection`]);
    }

    goHome() {
        const jobPath = this.jobService.currentJob()?.jobPath || 'tsic';
        this.router.navigate([`/${jobPath}`]);
    }

    login() {
        const jobPath = this.jobService.currentJob()?.jobPath || 'tsic';
        this.router.navigate([`/${jobPath}/login`], { queryParams: { force: 1 } });
    }

    logout() {
        const jobPath = this.jobService.currentJob()?.jobPath || 'tsic';
        const redirectTo = `/${jobPath}`;
        this.auth.logout({ redirectTo });
    }

    toggleTheme() {
        this.themeService.toggleTheme();
    }

    openDashboardCustomize() {
        this.closeUserMenu();
        this.menuState.requestCustomizeDashboard();
    }
}
