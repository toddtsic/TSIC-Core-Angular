import { ChangeDetectionStrategy, Component, computed, DestroyRef, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { PaletteService } from '@infrastructure/services/palette.service';
import { ThemeService } from '@infrastructure/services/theme.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import { MenuStateService } from '../../services/menu-state.service';
import { PalettePickerComponent } from '../palette-picker/palette-picker.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';

/** Admin roles that can customize dashboards */
const ADMIN_ROLES = ['Superuser', 'Director', 'SuperDirector'];

@Component({
    selector: 'app-client-header-bar',
    standalone: true,
    imports: [PalettePickerComponent, ConfirmDialogComponent],
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

    /** Split job name on ':' for compact two-line mobile display */
    jobNameLines = computed(() => {
        const name = this.jobName();
        const idx = name.indexOf(':');
        if (idx === -1) return [name];
        return [name.substring(0, idx).trim(), name.substring(idx + 1).trim()];
    });

    // Single computed `user` derived from AuthService; derive UI values from it.
    user = computed(() => this.auth.currentUser());

    tsicLogoTitle = computed(() =>
        this.user() ? 'Log out & return to TeamSportsInfo.com' : 'TeamSportsInfo.com home'
    );

    // Desktop dropdown state
    userMenuOpen = signal(false);
    paletteExpanded = signal(false);
    menuTop = signal(0);
    menuRight = signal(0);

    // Mobile dropdown menu state
    mobileMenuOpen = signal(false);
    mobileMenuTop = signal(0);
    mobileMenuRight = signal(0);
    mobilePaletteExpanded = signal(false);

    private readonly destroyRef = inject(DestroyRef);

    constructor() {
        // Close all menus when requested (e.g. after role selection navigates away)
        effect(() => {
            if (this.menuState.closeAllMenusRequested()) {
                this.closeUserMenu();
                this.closeMobileMenu();
                this.menuState.closeOffcanvas();
                this.menuState.ackCloseAllMenus();
            }
        });

        // Close all dropdowns on ANY route navigation — standard dropdown behavior
        this.router.events.pipe(
            filter(e => e instanceof NavigationStart),
            takeUntilDestroyed(this.destroyRef),
        ).subscribe(() => {
            this.closeUserMenu();
            this.closeMobileMenu();
            this.menuState.closeOffcanvas();
        });
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
        this.paletteExpanded.set(false);
    }

    toggleMobileMenu(event: Event) {
        event.stopPropagation();
        const wasOpen = this.mobileMenuOpen();
        this.mobileMenuOpen.set(!wasOpen);

        if (!wasOpen) {
            const btn = event.currentTarget as HTMLElement;
            const rect = btn.getBoundingClientRect();
            this.mobileMenuTop.set(rect.bottom + 8);
            this.mobileMenuRight.set(window.innerWidth - rect.right);
        }
    }

    closeMobileMenu() {
        this.mobileMenuOpen.set(false);
        this.mobilePaletteExpanded.set(false);
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

    readonly showTsicConfirm = signal(false);

    goToTsicHome() {
        if (this.auth.isAuthenticated()) {
            this.showTsicConfirm.set(true);
        } else {
            this.router.navigate(['/tsic']);
        }
    }

    confirmTsicHome() {
        this.showTsicConfirm.set(false);
        this.auth.logout({ redirectTo: '/tsic' });
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
