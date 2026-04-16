import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { combineLatest, debounceTime, filter } from 'rxjs';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { PaletteService } from '@infrastructure/services/palette.service';
import { ThemeService } from '@infrastructure/services/theme.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import { Roles } from '@infrastructure/constants/roles.constants';
import { MenuStateService } from '../../services/menu-state.service';
import { PalettePickerComponent } from '../palette-picker/palette-picker.component';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';

/** Admin roles that can customize dashboards */
const ADMIN_ROLES = ['Superuser', 'Director', 'SuperDirector'];

/** Single dropdown task-list entry derived from role + pulse. */
interface TaskItem {
    readonly icon: string;
    readonly label: string;
    readonly route: string;  // relative under :jobPath (may include ?query)
}

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
    private readonly pulseService = inject(JobPulseService);
    private readonly router = inject(Router);
    readonly themeService = inject(ThemeService);
    readonly paletteService = inject(PaletteService);
    private readonly menuState = inject(MenuStateService);

    readonly pulse = this.pulseService.pulse;

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

    /**
     * Role-conditional task list shown in the user dropdown for non-admin roles.
     * Admin roles get their tasks from the primary nav chrome, not this dropdown.
     */
    readonly taskItems = computed<TaskItem[]>(() => {
        const user = this.user();
        const pulse = this.pulse();
        if (!user?.role || !pulse) return [];

        const role = user.role;
        const items: TaskItem[] = [];

        if (role === Roles.ClubRep) {
            items.push({ icon: 'bi-person-gear', label: 'Edit Profile', route: 'account/club-rep' });

            const canEditTeamReg = pulse.teamRegistrationOpen
                && (pulse.clubRepAllowAdd || pulse.clubRepAllowEdit || pulse.clubRepAllowDelete);
            if (canEditTeamReg) {
                items.push({ icon: 'bi-pencil-square', label: 'Team Registration', route: 'registration/team?step=teams' });
            }
            if ((pulse.myClubRepTotalOwed ?? 0) > 0) {
                items.push({ icon: 'bi-cash-stack', label: 'Pay Team Balance', route: 'registration/team?step=payment' });
            }
            if ((pulse.myClubRepTeamCount ?? 0) > 0) {
                items.push({ icon: 'bi-people', label: 'Club Rosters', route: 'rosters/club' });
            }
            if (pulse.offerTeamRegsaverInsurance && pulse.myClubRepHasTeamWithoutRegsaver) {
                items.push({ icon: 'bi-shield-check', label: 'Buy Team Regsaver', route: 'ClubRepVIUpdate' });
            }
        } else if (role === Roles.Family || role === Roles.Player) {
            if (pulse.playerRegistrationOpen) {
                items.push({ icon: 'bi-person-badge', label: 'My Registration', route: 'registration/player?step=players' });
            }
            if ((pulse.myRegistrationOwedTotal ?? 0) > 0) {
                items.push({ icon: 'bi-cash-stack', label: 'Pay Balance', route: 'registration/player?step=payment' });
            }
            if (pulse.allowRosterViewPlayer && pulse.myAssignedTeamId) {
                items.push({ icon: 'bi-people', label: 'View Roster', route: 'rosters/view-rosters' });
            }
            if (pulse.offerPlayerRegsaverInsurance && pulse.myHasPurchasedPlayerRegsaver === false) {
                items.push({ icon: 'bi-shield-check', label: 'Buy Regsaver', route: 'PlayerVIUpdate' });
            }
        } else if (role === Roles.Staff) {
            items.push({ icon: 'bi-person-gear', label: 'My Registration', route: 'registration/adult?step=profile' });
            if ((pulse.myRegistrationOwedTotal ?? 0) > 0) {
                items.push({ icon: 'bi-cash-stack', label: 'Pay Balance', route: 'registration/adult?step=payment' });
            }
            if (pulse.allowRosterViewAdult && pulse.myAssignedTeamId) {
                items.push({ icon: 'bi-people', label: 'View Roster', route: 'rosters/view-rosters' });
            }
        }

        // Universal (non-admin) actions
        const isNonAdmin = role === Roles.ClubRep || role === Roles.Family
            || role === Roles.Player || role === Roles.Staff || role === Roles.UnassignedAdult;
        if (isNonAdmin) {
            if (pulse.storeEnabled && pulse.storeHasActiveItems) {
                items.push({ icon: 'bi-cart', label: 'Store', route: 'store' });
            }
            if (pulse.enableStayToPlay) {
                items.push({ icon: 'bi-building-check', label: 'Stay-to-Play', route: 'store' });
            }
        }

        return items;
    });

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
        toObservable(this.menuState.closeAllMenusRequested).pipe(
            filter(requested => requested),
            takeUntilDestroyed(this.destroyRef),
        ).subscribe(() => {
            this.closeUserMenu();
            this.closeMobileMenu();
            this.menuState.closeOffcanvas();
            this.menuState.ackCloseAllMenus();
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

        // Refresh pulse whenever the current job or authenticated user changes.
        // Debounced so simultaneous job+user changes (e.g. login flow) coalesce.
        combineLatest([
            toObservable(this.jobService.currentJob),
            toObservable(this.auth.currentUser),
        ]).pipe(
            debounceTime(50),
            takeUntilDestroyed(this.destroyRef),
        ).subscribe(([job]) => {
            const jobPath = job?.jobPath;
            if (jobPath) {
                this.pulseService.load(jobPath);
            } else {
                this.pulseService.clear();
            }
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

    /** Navigate to a task-item route (relative to the current job). Closes both menus. */
    navigateTask(route: string): void {
        this.closeUserMenu();
        this.closeMobileMenu();
        const jobPath = this.jobService.currentJob()?.jobPath;
        if (!jobPath) return;
        this.router.navigateByUrl(`/${jobPath}/${route}`);
    }

    readonly showTsicConfirm = signal(false);

    goToTsicHome() {
        if (this.auth.isAuthenticated()) {
            this.showTsicConfirm.set(true);
        } else {
            this.router.navigate(['/tsic'], { queryParams: { force: 1 } });
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
