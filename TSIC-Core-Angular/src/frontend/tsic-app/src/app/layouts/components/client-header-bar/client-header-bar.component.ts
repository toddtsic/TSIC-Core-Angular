import { afterNextRender, ChangeDetectionStrategy, Component, computed, DestroyRef, ElementRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { combineLatest, debounceTime, filter } from 'rxjs';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { ThemeService } from '@infrastructure/services/theme.service';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import { Roles } from '@infrastructure/constants/roles.constants';
import { MenuStateService } from '../../services/menu-state.service';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { HelpLauncherComponent } from '../help-launcher/help-launcher.component';

/** Single dropdown task-list entry derived from role + pulse. */
interface TaskItem {
    readonly icon: string;
    readonly label: string;
    readonly route: string;  // relative under :jobPath (may include ?query)
    readonly primary?: boolean;  // the one urgent next-action — emphasized + drives the avatar badge
}

@Component({
    selector: 'app-client-header-bar',
    standalone: true,
    imports: [ConfirmDialogComponent, HelpLauncherComponent],
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
    private readonly menuState = inject(MenuStateService);

    readonly pulse = this.pulseService.pulse;

    readonly isAdmin = this.auth.isAdmin;

    /**
     * Gates the Dashboard + Customize Dashboard menu entries — the dropdown door into
     * /dashboard. Being an admin is necessary but not sufficient: the pulse reports
     * whether this job+role has any dashboard widget at all, so a job whose dashboard
     * would render empty offers no way in rather than a blank page.
     *
     * `=== true` on purpose. The flag is null while the pulse is still in flight, null
     * for a non-admin, and null when the JWT is not scoped to this job — none of those
     * is "yes", and treating a loading pulse as yes is what would flash a dead link.
     */
    readonly showDashboardEntries = computed(() =>
        this.isAdmin() && this.pulse()?.myHasDashboardWidgets === true);

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

    /**
     * AR-033 copy control — gated on the ACTIVE role being Superuser, and NOTHING else.
     *
     * Deliberately NOT `auth.isSuperuser`: that one also reads true when Superuser merely
     * appears in the `roles` array, so it would light the button for someone acting as a
     * Director the moment the server starts issuing multiple role claims (the decode at
     * auth.service.ts already handles an array claim). Todd's ruling: role = SuperUser only.
     */
    readonly isSuperuser = computed(() => this.auth.currentUser()?.role === Roles.Superuser);

    /** Transient outcome of the job-name copy: null | 'copied' | 'failed'. A copy button
     *  that silently does nothing is worse than no button, so failure is SHOWN. */
    readonly jobNameCopyState = signal<'copied' | 'failed' | null>(null);

    readonly jobNameCopyTitle = computed(() => {
        switch (this.jobNameCopyState()) {
            case 'copied': return 'Copied!';
            case 'failed': return 'Copy blocked — the clipboard needs a secure (https) page';
            default: return 'Copy event name';
        }
    });

    private copyResetTimer?: ReturnType<typeof setTimeout>;

    // Single computed `user` derived from AuthService; derive UI values from it.
    user = computed(() => this.auth.currentUser());

    tsicLogoTitle = computed(() =>
        this.user() ? 'Log out & return to TeamSportsInfo.com' : 'TeamSportsInfo.com home'
    );

    /** First name from the pulse for the friendly header label. Empty when the pulse
     *  has no name for the current scope (e.g. family account pre-role-selection) — the
     *  template falls back to the original icon + username layout in that case. */
    readonly firstName = computed(() => this.pulse()?.myFirstName?.trim() ?? '');

    /** Sport key for the header ball icon. Each ball has a distinctive color/pattern that
     *  reads at icon size. Lacrosse is the house fallback for unknown sports. */
    readonly sportKey = computed<'basketball' | 'baseball' | 'soccer' | 'lacrosse'>(() => {
        const name = this.jobService.currentJob()?.sportName?.toLowerCase() ?? '';
        if (name.includes('basketball')) return 'basketball';
        if (name.includes('baseball')) return 'baseball';
        if (name.includes('soccer')) return 'soccer';
        return 'lacrosse';
    });

    /** "First Last" from pulse when available; used in tooltip + dropdown header. */
    readonly fullName = computed(() => {
        const p = this.pulse();
        const first = p?.myFirstName?.trim();
        const last = p?.myLastName?.trim();
        if (first && last) return `${first} ${last}`;
        return first || last || '';
    });

    /** Tooltip for the avatar trigger: "John Smith — Player" (or just role, or username). */
    readonly avatarTitle = computed(() => {
        const name = this.fullName();
        const role = this.user()?.role;
        if (name && role) return `${name} — ${role}`;
        if (name) return name;
        if (role) return role;
        return this.user()?.username ?? '';
    });

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
            // Team Registration is the ClubRep's workspace — always linked; the wizard
            // itself gates add/edit/delete on clubRepAllow* caps.
            items.push({ icon: 'bi-pencil-square', label: 'Team Registration', route: 'registration/team?step=teams' });
            // Non-ARB owed only — an ARB team auto-drafts and keeps a positive OwedTotal,
            // so the full sum would float a permanent `primary` nudge for a rep who owes
            // nothing by hand. See JobPulseDto.MyClubRepNonArbOwed.
            if ((pulse.myClubRepNonArbOwed ?? 0) > 0) {
                items.push({ icon: 'bi-cash-stack', label: 'Pay Balance Due', route: 'registration/team?step=payment' });
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
            // Never nudge a LIVE ARB registrant: their balance is auto-drafted on a schedule,
            // so myRegistrationOwedTotal stays > 0 by design and this row (which floats to
            // the top as `primary` and lights the avatar badge) would invite a double payment.
            if ((pulse.myRegistrationOwedTotal ?? 0) > 0 && !pulse.myHasLiveArbSubscription) {
                items.push({ icon: 'bi-cash-stack', label: 'Pay Balance Due', route: 'registration/player?step=payment' });
            }
            // myTeamHidesRoster = my team sits in a WAITLIST/Dropped/Registration holding agegroup.
            // Don't offer a roster the server will only deny — a waitlisted family must not be handed
            // the list of everyone else on the waitlist. Mirrors MyRosterService's gate.
            if (pulse.allowRosterViewPlayer && pulse.myAssignedTeamId && !pulse.myTeamHidesRoster) {
                items.push({ icon: 'bi-people', label: 'View Roster', route: 'rosters/view-rosters' });
            }
            if (pulse.offerPlayerRegsaverInsurance && pulse.myHasPurchasedPlayerRegsaver === false) {
                items.push({ icon: 'bi-shield-check', label: 'Buy Regsaver', route: 'PlayerVIUpdate' });
            }
            // ARB-only. Gate on LIVENESS, not id-presence: adnSubscriptionId is never cleared when a
            // plan ends, so an id-gate offered this to every family whose plan had finished or been
            // cancelled — and on a dead plan the ADN subscription update can only fail, while the
            // screen still charges any balance due. Suspended plans (a declined draft, which is the
            // case this screen exists for) read LIVE and keep the row, along with the catch-up charge
            // that comes with it. Dead-plan families who owe are routed to Pay Balance Due instead.
            if (pulse.myHasLiveArbSubscription && user.regId) {
                items.push({ icon: 'bi-credit-card', label: 'Update CC Info', route: `arb/update-cc/${user.regId}` });
            }
            // Player-only self-service: check whether our email reaches them, and unblock it.
            if (role === Roles.Player) {
                items.push({ icon: 'bi-envelope-check', label: 'Check My Email', route: 'tools/email-deliverability' });
            }
        } else if (role === Roles.Staff) {
            // Staff = a self-rostered coach; the adult wizard REQUIRES ?role=<key> or it
            // shows an "incomplete link" error, so the coach roleKey must ride the URL.
            items.push({ icon: 'bi-person-gear', label: 'My Registration', route: 'registration/adult?role=coach&step=profile' });
            // Same live-ARB suppression as the player branch. No adult is placed on an ARB plan
            // today, but the gate is free and holds if that ever changes.
            if ((pulse.myRegistrationOwedTotal ?? 0) > 0 && !pulse.myHasLiveArbSubscription) {
                items.push({ icon: 'bi-cash-stack', label: 'Pay Balance Due', route: 'registration/adult?role=coach&step=payment' });
            }
            // Same holding-agegroup suppression as the player branch above.
            if (pulse.allowRosterViewAdult && pulse.myAssignedTeamId && !pulse.myTeamHidesRoster) {
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
            // No Stay-to-Play item here. There was one, gated on pulse.enableStayToPlay and
            // routed to 'store' — but STP is a data transfer to a housing vendor, not a
            // storefront: the flag opens a vendor login, and there has never been a
            // player-facing Stay-to-Play screen for the link to reach. It sent players to
            // the store. Removed 2026-08-23.
        }

        // Rank the single most-important "next action". A balance due is the
        // urgent one; we tag it + float it to the top so the menu emphasizes it
        // and the avatar shows a nudge badge. When nothing's urgent, the list is
        // unchanged — no manufactured emphasis, no nagging badge.
        const urgentIdx = items.findIndex(i => i.label === 'Pay Balance Due');
        if (urgentIdx > 0) {
            const [urgent] = items.splice(urgentIdx, 1);
            items.unshift({ ...urgent, primary: true });
        } else if (urgentIdx === 0) {
            items[0] = { ...items[0], primary: true };
        }

        return items;
    });

    /** True when there's an urgent next-action (a balance due) worth a nudge badge. */
    readonly hasUrgentAction = computed(() => this.taskItems().some(i => i.primary));

    // Desktop dropdown state
    userMenuOpen = signal(false);
    menuTop = signal(0);
    menuRight = signal(0);

    // Mobile dropdown menu state
    mobileMenuOpen = signal(false);
    mobileMenuTop = signal(0);
    mobileMenuRight = signal(0);

    private readonly destroyRef = inject(DestroyRef);
    private readonly host = inject(ElementRef<HTMLElement>);

    constructor() {
        // Publish the header's MEASURED height into the shared flyin anchor token.
        // The token's static 48px is only a fallback: job names wrap the mobile header
        // taller than 48px, and flyins anchored to the guess sliced through the subtitle.
        // Pure DOM measurement (afterNextRender + ResizeObserver) — no signals, and the
        // header's own min-height deliberately does NOT read this token (see .scss).
        afterNextRender(() => {
            const el = this.host.nativeElement;
            const publish = () => {
                const h = el.offsetHeight;
                if (h > 0) {
                    document.documentElement.style.setProperty('--app-header-height-mobile', `${Math.round(h)}px`);
                }
            };
            const ro = new ResizeObserver(publish);
            ro.observe(el);
            publish();
            this.destroyRef.onDestroy(() => {
                ro.disconnect();
                document.documentElement.style.removeProperty('--app-header-height-mobile');
            });
        });

        this.destroyRef.onDestroy(() => clearTimeout(this.copyResetTimer));

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

    /**
     * AR-033: copy the full `Customer:Job` string (SuperUser only; Ann pastes it into email).
     *
     * Reads jobName() — NOT the rendered text. Mobile splits the name across two lines via
     * jobNameLines(), so a text-based copy comes back broken in half on a phone.
     */
    async copyJobName(event: Event): Promise<void> {
        // The job-name pill sits right beside this button and navigates home on click.
        event.stopPropagation();
        const value = this.jobName();
        if (!value) return;
        try {
            // navigator.clipboard is UNDEFINED outside a secure context (plain http), which
            // throws rather than rejecting — hence try/catch, not a .catch() on the promise.
            await navigator.clipboard.writeText(value);
            this.setCopyState('copied');
        } catch {
            this.setCopyState('failed');
        }
    }

    private setCopyState(state: 'copied' | 'failed'): void {
        clearTimeout(this.copyResetTimer);
        this.jobNameCopyState.set(state);
        this.copyResetTimer = setTimeout(() => this.jobNameCopyState.set(null), 2000);
    }

    /** Admin-only: go to the widget dashboard (charts/metrics). Admins land on the
     *  public landing by default now, so this is their explicit way to the dashboard. */
    viewDashboard() {
        this.closeUserMenu();
        this.closeMobileMenu();
        const jobPath = this.jobService.currentJob()?.jobPath || 'tsic';
        this.router.navigate([`/${jobPath}/dashboard`]);
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
        // Close the menus explicitly. Logout redirects to the current job's landing
        // (`/${jobPath}`), which is frequently the page we're already on — and the
        // router is configured with onSameUrlNavigation:'ignore', so that navigation
        // is dropped and the NavigationStart-based menu-close never fires. Leaving the
        // full-page .user-menu-backdrop mounted would then swallow the user's first
        // click anywhere on the page (needing a dead first click to dismiss it).
        this.closeUserMenu();
        this.closeMobileMenu();
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
