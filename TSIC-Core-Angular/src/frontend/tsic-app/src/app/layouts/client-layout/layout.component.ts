import { Component, computed, effect, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import type { Job, MenuItemDto } from '../../core/services/job.service';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet, RouterLink, RouterLinkActive, NavigationEnd, ActivatedRoute } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { JobService } from '../../core/services/job.service';
import { JobContextService } from '../../core/services/job-context.service';
import { ThemeService } from '../../core/services/theme.service';
import { MenusComponent } from '../../shared/menus/menus.component';
import { ClientHeaderBarComponent } from '../components/client-header-bar/client-header-bar.component';
import { Subject, takeUntil, filter, skip, startWith, map, distinctUntilChanged } from 'rxjs';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive, MenusComponent, ClientHeaderBarComponent],
  templateUrl: './layout.component.html',
  styleUrls: ['./layout.component.scss']
})
export class LayoutComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private readonly auth = inject(AuthService);
  private readonly jobService = inject(JobService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly jobContext = inject(JobContextService);
  readonly themeService = inject(ThemeService);

  private readonly STATIC_BASE_URL = 'https://statics.teamsportsinfo.com/BannerFiles';

  // Signal to track sidebar open/closed state (default: open on desktop, closed on mobile)
  sidebarOpen = signal<boolean>(true);

  // Track which parent menu items are expanded (for horizontal menu dropdowns)
  expandedItems = signal<Set<string>>(new Set());

  // Observable for auth state changes (must be field initializer for injection context)
  private readonly currentUser$ = toObservable(this.auth.currentUser);

  // Computed signals from JobService for menus
  menus = computed(() => this.jobService.menus());
  menusLoading = computed(() => this.jobService.menusLoading());
  menusError = computed(() => this.jobService.menusError());

  private bestLogoUrl(job: Job | null, userLogo?: string): string {
    // Helper to identify suspicious jobPath-derived filenames like "steps.jpg" that shouldn't be treated as logos.
    const isSuspiciousDerived = (raw: string | undefined, j: Job | null) => {
      if (!raw || !j?.jobPath) return false;
      const lower = raw.trim().toLowerCase();
      const jp = j.jobPath.toLowerCase();
      // Consider direct jobPath + common image extension without any suffix as suspicious.
      return ['.png', '.jpg', '.jpeg', '.gif', '.webp'].some(ext => lower === jp + ext);
    };

    // 1) API provided logo (skip if suspicious jobPath-derived)
    const apiLogoRaw = job?.jobLogoPath;
    if (!isSuspiciousDerived(apiLogoRaw, job)) {
      const apiLogo = this.buildAssetUrl(apiLogoRaw);
      if (apiLogo) return apiLogo;
    }

    // 2) Conventional GUID-based header with variable extensions
    if (job?.jobId) {
      const candidates = [
        // Primary logo header variants
        `${job.jobId}_logoheader.png`,
        `${job.jobId}_logoheader.jpg`,
        `${job.jobId}_logoheader.jpeg`,
        // Parallax / alternate header background variants as secondary fallbacks
        `${job.jobId}_parallaxheader.png`,
        `${job.jobId}_parallaxheader.jpg`,
        `${job.jobId}_parallaxheader.jpeg`
      ];
      for (const c of candidates) {
        const url = this.buildAssetUrl(c);
        if (url) return url;
      }
    }

    // 3) Token-provided logo (skip if suspicious)
    if (!isSuspiciousDerived(userLogo, job)) {
      const tokenLogo = this.buildAssetUrl(userLogo);
      if (tokenLogo) return tokenLogo;
    }

    // 4) None
    return '';
  }

  // Signals
  jobLogoPath = signal('');
  jobBannerPath = signal('');
  jobName = signal('');
  username = signal('');
  // Derived display string for "{username} as {roleName}" (store the STRING, not a function)
  displayUserRole = signal('');
  // Separate role name signal so template can distinguish visually
  roleName = signal('');
  showRoleMenu = signal(false);
  isAuthenticated = signal(false);
  roles = signal(['Parent', 'Director', 'Club Rep']);
  currentRole = signal('Parent'); // NOTE: wire to user/role selection from AuthService when implemented

  constructor() {
    const user = this.auth.getCurrentUser();
    const authenticated = this.auth.isAuthenticated();

    this.isAuthenticated.set(authenticated);
    this.username.set(user?.username || '');
    this.showRoleMenu.set(!!user?.regId);

    // Initialize displayUserRole & roleName once with current user
    {
      const initialRole = (user?.roles?.[0] ?? user?.role) || '';
      let initialDisplay = '';
      if (user?.username) {
        initialDisplay = initialRole ? `${user.username} as ${initialRole}` : user.username;
      }
      this.displayUserRole.set(initialDisplay);
      this.roleName.set(initialRole);
    }

    // Mirror AuthService state reactively into header UI (recompute display string)
    effect(() => {
      const u = this.auth.currentUser();
      this.username.set(u?.username || '');
      const r = (u?.roles?.[0] ?? u?.role) || '';
      let display = '';
      if (u?.username) {
        display = r ? `${u.username} as ${r}` : u.username;
      }
      this.displayUserRole.set(display);
      this.roleName.set(r);
      this.showRoleMenu.set(!!u?.regId);
      this.isAuthenticated.set(!!u);
    });

    // Reactively update header whenever the current job changes
    effect(() => {
      const job = this.jobService.currentJob();
      this.applyJobInfo(job);
    });
  }

  ngOnInit() {
    // Get initial jobPath from route
    const initialJobPath = this.getActiveJobPath();

    // Load job metadata if jobPath is available and job not already loaded
    if (initialJobPath && !this.jobService.currentJob()) {
      this.jobService.loadJobMetadata(initialJobPath);
    }

    // Create an observable that tracks jobPath changes from navigation
    const jobPath$ = this.router.events.pipe(
      filter(event => event instanceof NavigationEnd),
      startWith(null), // Emit immediately on subscription to handle app restart
      map(() => this.getActiveJobPath()),
      distinctUntilChanged(),
      takeUntil(this.destroy$)
    );

    // Load menus whenever jobPath changes (handles navigation between jobs and app restart)
    jobPath$.subscribe(jobPath => {
      if (jobPath && jobPath !== 'tsic') {
        this.jobService.loadMenus(jobPath);
      } else {
        this.jobService.menus.set([]);
      }
    });

    // Watch for authentication state changes (login/logout)
    // Reload menus with cache bypass to get role-specific or anonymous menus
    this.currentUser$
      .pipe(
        skip(1), // Skip initial emission to avoid duplicate load
        takeUntil(this.destroy$)
      )
      .subscribe(() => {
        const jobPath = this.getActiveJobPath();
        if (jobPath && jobPath !== 'tsic') {
          // Bypass cache to force fresh menu fetch when auth state changes
          this.jobService.loadMenus(jobPath, true);
        }
      });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private applyJobInfo(job: Job | null) {
    const user = this.auth.getCurrentUser();
    // Always display the job label derived from jobPath in ALL CAPS for consistency.
    // Fallback order: job.jobPath -> JobContextService -> user.jobPath -> 'TSIC'
    const ctxPath = this.jobContext.jobPath();
    const display = (job?.jobPath || ctxPath || user?.jobPath || 'TSIC').toUpperCase();
    this.jobName.set(display);

    // Compute best logo URL from available inputs
    const bestLogo = this.bestLogoUrl(job, user?.jobLogo || undefined);
    if (bestLogo) this.jobLogoPath.set(bestLogo);
    // Banner comes only from API when available
    if (job) {
      const apiBanner = this.buildAssetUrl(job.jobBannerPath);
      if (apiBanner) this.jobBannerPath.set(apiBanner);
    }
  }

  private buildAssetUrl(path?: string): string {
    if (!path) return '';
    const p = String(path).trim();
    if (!p || p === 'undefined' || p === 'null') return '';
    // Already absolute URL - collapse any accidental double slashes (except after protocol)
    if (/^https?:\/\//i.test(p)) {
      return p.replace(/([^:])\/\/+/, '$1/');
    }
    // Remove leading slashes to avoid double slashes
    const noLead = p.replace(/^\/+/, '');
    // If the value already includes the BannerFiles segment, don't duplicate it
    if (/^BannerFiles\//i.test(noLead)) {
      const rest = noLead.replace(/^BannerFiles\//i, '');
      return `${this.STATIC_BASE_URL}/${rest}`;
    }
    // Prevent accidental use of raw jobPath like 'steps' as an image filename; reject short alpha tokens without extension
    if (!/[.]/.test(noLead) && /^[a-z0-9-]{2,20}$/i.test(noLead)) {
      return '';
    }
    return `${this.STATIC_BASE_URL}/${noLead}`;
  }

  logout() {
    // Desired behavior: sign out and remain on the SAME job (anonymous view).
    // If we can determine the active jobPath, navigate to '/:jobPath'; otherwise, fall back to TSIC landing.
    const jobPath = this.getActiveJobPath();
    const redirectTo = jobPath ? `/${jobPath}` : '/tsic';
    this.auth.logout({ redirectTo });
  }

  login() {
    // Force generic login page even if last_job_path would normally auto-redirect
    this.router.navigate(['/tsic/login'], { queryParams: { force: 1 } });
  }

  switchRole() {
    this.router.navigate(['/tsic/role-selection']);
  }

  onSwitchRole(event: Event) {
    // Prevent any default anchor behavior and force SPA navigation
    if (event) { event.preventDefault(); event.stopPropagation(); }
    // Optional polish: clear current job header to avoid spinner flash
    try { this.jobService.currentJob.set(null); } catch { /* ignore */ }
    this.router.navigate(['/tsic/role-selection']);
  }

  selectRole(role: string) {
    this.currentRole.set(role);
    // NOTE: update role in AuthService and refresh job menu items when role feature is wired
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }

  toggleSidebar() {
    this.sidebarOpen.update(open => !open);
  }

  private getActiveJobPath(): string | null {
    // Prefer JobService current job
    const job = this.jobService.getCurrentJob();
    if (job?.jobPath) return job.jobPath;
    // Next, try AuthService token claim
    const claimPath = this.auth.getJobPath();
    if (claimPath) return claimPath;
    // Fallback: parse current URL for first non-empty segment that isn't 'tsic'
    const url = this.router.url || '';
    const seg = url.split('?')[0].split('#')[0].split('/').find(s => !!s) || '';
    // Ignore known app shell segments like 'tsic' and feature routes like 'register-player'
    const lower = seg.toLowerCase();
    if (lower && lower !== 'tsic' && lower !== 'register-player') return seg;
    // Still unknown
    if (claimPath) return claimPath;
    return null;
  }

  goHome() {
    const jobPath = this.getActiveJobPath();
    if (jobPath) {
      this.router.navigate(['/', jobPath]);
    } else {
      // If we don't know the job, send to TSIC landing/home
      this.router.navigate(['/tsic']);
    }
  }

  // Menu helper methods for horizontal navigation
  /**
   * Toggle expansion state of a parent menu item
   */
  toggleExpanded(menuItemId: string): void {
    const normalizedId = menuItemId.toLowerCase();
    const expanded = this.expandedItems();
    const newExpanded = new Set(expanded);

    if (newExpanded.has(normalizedId)) {
      newExpanded.delete(normalizedId);
    } else {
      newExpanded.add(normalizedId);
    }

    this.expandedItems.set(newExpanded);
  }

  /**
   * Check if a menu item is expanded
   */
  isExpanded(menuItemId: string): boolean {
    const normalizedId = menuItemId.toLowerCase();
    return this.expandedItems().has(normalizedId);
  }

  /**
   * Get the link for a menu item based on precedence:
   * 1. navigateUrl (external link)
   * 2. routerLink (Angular route)
   * 3. controller/action (legacy MVC - map to Angular route)
   */
  getLink(item: MenuItemDto): string | null {
    if (item.navigateUrl) {
      return item.navigateUrl;
    }
    if (item.routerLink) {
      return item.routerLink;
    }
    if (item.controller && item.action) {
      // Legacy MVC route mapping (1:1 mapping as specified)
      return `/${item.controller.toLowerCase()}/${item.action.toLowerCase()}`;
    }
    return null;
  }

  /**
   * Check if the link is external (navigateUrl present)
   */
  isExternalLink(item: MenuItemDto): boolean {
    return !!item.navigateUrl;
  }

  /**
   * Check if item has children
   */
  hasChildren(item: MenuItemDto): boolean {
    return item.children && item.children.length > 0;
  }
}
