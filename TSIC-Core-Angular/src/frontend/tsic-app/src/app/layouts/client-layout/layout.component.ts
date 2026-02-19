import { Component, computed, inject, signal, OnInit, OnDestroy, ChangeDetectionStrategy } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import type { JobMetadataResponse } from '@core/api';

import { Router, RouterOutlet, NavigationEnd, ActivatedRoute } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { ThemeService } from '@infrastructure/services/theme.service';
import { ClientHeaderBarComponent } from '../components/client-header-bar/client-header-bar.component';
import { ClientMenuComponent } from '../components/client-menu/client-menu.component';
import { ClientFooterBarComponent } from '../components/client-footer-bar/client-footer-bar.component';
import { ScrollToTopComponent } from '../../shared-ui/scroll-to-top/scroll-to-top.component';
import { BreadcrumbComponent } from '../../shared-ui/breadcrumb/breadcrumb.component';
import { Subject, takeUntil, filter, skip, startWith, map, distinctUntilChanged } from 'rxjs';
import { isJobLanding } from '@infrastructure/utils/route-segment.utils';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [RouterOutlet, ClientHeaderBarComponent, ClientMenuComponent, ClientFooterBarComponent, ScrollToTopComponent, BreadcrumbComponent],
  templateUrl: './layout.component.html',
  styleUrls: ['./layout.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
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

  // Observable for auth state changes (must be field initializer for injection context)
  private readonly currentUser$ = toObservable(this.auth.currentUser);

  private bestLogoUrl(job: JobMetadataResponse | null, userLogo?: string): string {
    // Helper to identify suspicious jobPath-derived filenames like "steps.jpg" that shouldn't be treated as logos.
    const isSuspiciousDerived = (raw: string | null | undefined, j: JobMetadataResponse | null) => {
      if (!raw || !j?.jobPath) return false;
      const lower = raw.trim().toLowerCase();
      const jp = j.jobPath.toLowerCase();
      // Consider direct jobPath + common image extension without any suffix as suspicious.
      return ['.png', '.jpg', '.jpeg', '.gif', '.webp'].some(ext => lower === jp + ext);
    };

    // 1) API provided logo (skip if suspicious jobPath-derived)
    const apiLogoRaw = job?.jobLogoPath;
    if (apiLogoRaw && !isSuspiciousDerived(apiLogoRaw, job)) {
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

  // Computed signals derived from services
  username = computed(() => this.auth.currentUser()?.username || '');
  roleName = computed(() => (this.auth.currentUser()?.roles?.[0] ?? this.auth.currentUser()?.role) || '');
  displayUserRole = computed(() => {
    const u = this.auth.currentUser();
    if (!u?.username) return '';
    const r = (u.roles?.[0] ?? u.role) || '';
    return r ? `${u.username} as ${r}` : u.username;
  });
  showRoleMenu = computed(() => !!this.auth.currentUser()?.regId);
  isAuthenticated = computed(() => !!this.auth.currentUser());

  // Job-related computed signals
  jobName = computed(() => {
    const job = this.jobService.currentJob();
    const user = this.auth.getCurrentUser();
    const ctxPath = this.jobContext.jobPath();
    return (job?.jobPath || ctxPath || user?.jobPath || 'TSIC').toUpperCase();
  });

  jobLogoPath = computed(() => {
    const job = this.jobService.currentJob();
    const user = this.auth.getCurrentUser();
    return this.bestLogoUrl(job, user?.jobLogo || undefined);
  });

  jobBannerPath = computed(() => {
    const job = this.jobService.currentJob();
    if (!job?.jobBannerPath) return '';
    const apiBanner = this.buildAssetUrl(job.jobBannerPath);
    return apiBanner || '';
  });

  roles = signal(['Parent', 'Director', 'Club Rep']);
  currentRole = signal('Parent'); // NOTE: wire to user/role selection from AuthService when implemented

  constructor() {
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

    // Load nav when jobPath changes AND user is authenticated
    jobPath$.subscribe(jobPath => {
      const user = this.auth.currentUser();
      console.log('[NAV DEBUG] jobPath$:', jobPath, 'user:', !!user, user?.role);
      if (jobPath && jobPath !== 'tsic' && user) {
        console.log('[NAV DEBUG] calling loadNav()');
        this.jobService.loadNav();
      } else {
        this.jobService.clearNav();
      }
    });

    // Watch for authentication state changes (login/logout/role-switch)
    // Reload nav AND job metadata to keep everything in sync
    this.currentUser$
      .pipe(
        skip(1), // Skip initial emission to avoid duplicate load
        takeUntil(this.destroy$)
      )
      .subscribe(user => {
        const jobPath = this.getActiveJobPath();
        if (jobPath && jobPath !== 'tsic') {
          // Reload job metadata so currentJob stays in sync after role/job switch
          this.jobService.loadJobMetadata(jobPath);
          // Load nav if authenticated, clear if logged out
          if (user) {
            this.jobService.loadNav();
          } else {
            this.jobService.clearNav();
          }
        }
      });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
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


  /**
   * Check if the current route is the job-landing route
   * Uses route segment utilities (jSeg/controller/action convention)
   */
  private isJobLandingRoute(): boolean {
    return isJobLanding(this.router.url || '');
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
}
