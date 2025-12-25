import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { JobService } from '../../../core/services/job.service';
import { ThemeService } from '../../../core/services/theme.service';

@Component({
    selector: 'app-client-header-bar',
    standalone: true,
    imports: [CommonModule, RouterLink],
    templateUrl: './client-header-bar.component.html',
    styleUrls: ['./client-header-bar.component.scss']
})
export class ClientHeaderBarComponent {
    private readonly auth = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly router = inject(Router);
    readonly themeService = inject(ThemeService);

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

    // Derived values are read from `user()` directly in the template

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

    // No sidebar toggle here; menus are handled by a separate component

    goHome() {
        const jobPath = this.jobService.currentJob()?.jobPath;
        if (jobPath) {
            this.router.navigate([`/${jobPath}`]);
        } else {
            this.router.navigate(['/tsic']);
        }
    }

    login() {
        this.router.navigate(['/tsic/login'], { queryParams: { force: 1 } });
    }

    logout() {
        const jobPath = this.jobService.currentJob()?.jobPath;
        const redirectTo = jobPath ? `/${jobPath}` : '/tsic';
        this.auth.logout({ redirectTo });
    }

    onSwitchRole(event: Event) {
        // Prevent default link behavior if needed
    }

    toggleTheme() {
        this.themeService.toggleTheme();
    }
}
