import { Component, computed, inject, signal } from '@angular/core';

import { Router, RouterLink } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { ThemeService } from '@infrastructure/services/theme.service';
import { MenuStateService } from '../../services/menu-state.service';

@Component({
    selector: 'app-client-header-bar',
    standalone: true,
    imports: [RouterLink],
    templateUrl: './client-header-bar.component.html',
    styleUrls: ['./client-header-bar.component.scss']
})
export class ClientHeaderBarComponent {
    private readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly router = inject(Router);
    readonly themeService = inject(ThemeService);
    private readonly menuState = inject(MenuStateService);

    // (menu/sidebar bindings removed) -- header is decoupled from menus

    // Job-related signals
    jobLogoPath = computed(() => {
        const job = this.jobService.currentJob();
        if (job?.jobLogoPath) {
            return this.buildAssetUrl(job.jobLogoPath);
        }
        return '';
    });

    jobName = computed(() => this.jobService.currentJob()?.jobName || '');

    // Single computed `user` derived from AuthService; derive UI values from it.
    user = computed(() => this.auth.currentUser());

    // Desktop dropdown state
    userMenuOpen = signal(false);
    menuTop = signal(0);
    menuRight = signal(0);

    private buildAssetUrl(path?: string): string {
        if (!path) return '';
        const STATIC_BASE_URL = 'https://statics.teamsportsinfo.com/BannerFiles';
        const p = String(path).trim();
        if (!p || p === 'undefined' || p === 'null') return '';

        if (/^https?:\/\//i.test(p)) {
            return p.replace(/([^:])\/\/+/, '$1/');
        }

        const noLead = p.replace(/^\/+/, '');
        if (/^BannerFiles\//i.test(noLead)) {
            const rest = noLead.replace(/^BannerFiles\//i, '');
            return `${STATIC_BASE_URL}/${rest}`;
        }

        if (!/[.]/.test(noLead) && /^[a-z0-9-]{2,20}$/i.test(noLead)) {
            return '';
        }

        return `${STATIC_BASE_URL}/${noLead}`;
    }

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
    }

    switchRole() {
        this.closeUserMenu();
        this.router.navigate(['role-selection']);
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

    onSwitchRole(event: Event) {
        // Prevent default link behavior if needed
    }

    toggleTheme() {
        this.themeService.toggleTheme();
    }
}
