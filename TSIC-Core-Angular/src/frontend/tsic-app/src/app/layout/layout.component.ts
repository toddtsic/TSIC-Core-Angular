import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { JobService } from '../core/services/job.service';
import { ThemeService } from '../core/services/theme.service';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet],
  template: `
    <!-- Header -->
    <header class="tsic-header">
      <div class="container-fluid">
        <div class="row align-items-center py-2">
          <!-- TSIC Branding + Job Info -->
          <div class="col-lg-4">
            <div class="d-flex align-items-center gap-2">
              <!-- TSIC Logo with light background -->
              <div class="tsic-brand-container">
                <img src="images/tsic-logo.png" alt="TSIC" class="tsic-logo" />
              </div>
              
              <!-- Divider -->
              <div class="brand-divider"></div>
              
              <!-- Job Info Container -->
              <div class="job-brand-container d-flex align-items-center gap-2 flex-grow-1">
                @if (jobLogoPath()) {
                  <img [src]="jobLogoPath()" alt="Job Logo" class="job-logo" />
                }
                <div class="job-info">
                  <div class="tsic-label text-uppercase fw-bold text-success small">TSIC</div>
                  @if (jobName()) {
                    <div class="job-name small text-muted">{{ jobName() }}</div>
                  }
                </div>
              </div>
            </div>
          </div>

          <!-- Role Navigation -->
          <div class="col-lg-4 d-none d-lg-block">
            <nav class="d-flex gap-2 justify-content-center flex-wrap">
              @for (role of roles(); track role) {
                <button 
                  type="button" 
                  class="btn btn-sm"
                  [class.btn-success]="role === currentRole()"
                  [class.btn-outline-secondary]="role !== currentRole()"
                  (click)="selectRole(role)">
                  {{ role }}
                </button>
              }
            </nav>
          </div>

          <!-- User Actions -->
          <div class="col-lg-4">
            <div class="d-flex align-items-center justify-content-end gap-2">
              <span class="text-secondary small d-none d-md-inline fw-medium">{{ username() }}</span>
              @if (showRoleMenu()) {
                <button 
                  type="button" 
                  class="btn btn-sm btn-outline-success" 
                  (click)="switchRole()"
                  title="Switch Role">
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M1 14s-1 0-1-1 1-4 6-4 6 3 6 4-1 1-1 1H1zm5-6a3 3 0 1 0 0-6 3 3 0 0 0 0 6z"/>
                    <path fill-rule="evenodd" d="M13.5 5a.5.5 0 0 1 .5.5V7h1.5a.5.5 0 0 1 0 1H14v1.5a.5.5 0 0 1-1 0V8h-1.5a.5.5 0 0 1 0-1H13V5.5a.5.5 0 0 1 .5-.5z"/>
                  </svg>
                </button>
              }
              <button 
                type="button" 
                class="btn btn-sm btn-outline-secondary" 
                (click)="toggleTheme()"
                title="Toggle Theme">
                @if (themeService.theme() === 'light') {
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M6 .278a.768.768 0 0 1 .08.858 7.208 7.208 0 0 0-.878 3.46c0 4.021 3.278 7.277 7.318 7.277.527 0 1.04-.055 1.533-.16a.787.787 0 0 1 .81.316.733.733 0 0 1-.031.893A8.349 8.349 0 0 1 8.344 16C3.734 16 0 12.286 0 7.71 0 4.266 2.114 1.312 5.124.06A.752.752 0 0 1 6 .278z"/>
                  </svg>
                } @else {
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M8 11a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm0 1a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM8 0a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-1 0v-2A.5.5 0 0 1 8 0zm0 13a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-1 0v-2A.5.5 0 0 1 8 13zm8-5a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1 0-1h2a.5.5 0 0 1 .5.5zM3 8a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1 0-1h2A.5.5 0 0 1 3 8zm10.657-5.657a.5.5 0 0 1 0 .707l-1.414 1.415a.5.5 0 1 1-.707-.708l1.414-1.414a.5.5 0 0 1 .707 0zm-9.193 9.193a.5.5 0 0 1 0 .707L3.05 13.657a.5.5 0 0 1-.707-.707l1.414-1.414a.5.5 0 0 1 .707 0zm9.193 2.121a.5.5 0 0 1-.707 0l-1.414-1.414a.5.5 0 0 1 .707-.707l1.414 1.414a.5.5 0 0 1 0 .707zM4.464 4.465a.5.5 0 0 1-.707 0L2.343 3.05a.5.5 0 1 1 .707-.707l1.414 1.414a.5.5 0 0 1 0 .708z"/>
                  </svg>
                }
              </button>
              <button 
                type="button" 
                class="btn btn-sm btn-outline-danger" 
                (click)="logout()"
                title="Logout">
                <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                  <path fill-rule="evenodd" d="M10 12.5a.5.5 0 0 1-.5.5h-8a.5.5 0 0 1-.5-.5v-9a.5.5 0 0 1 .5-.5h8a.5.5 0 0 1 .5.5v2a.5.5 0 0 0 1 0v-2A1.5 1.5 0 0 0 9.5 2h-8A1.5 1.5 0 0 0 0 3.5v9A1.5 1.5 0 0 0 1.5 14h8a1.5 1.5 0 0 0 1.5-1.5v-2a.5.5 0 0 0-1 0v2z"/>
                  <path fill-rule="evenodd" d="M15.854 8.354a.5.5 0 0 0 0-.708l-3-3a.5.5 0 0 0-.708.708L14.293 7.5H5.5a.5.5 0 0 0 0 1h8.793l-2.147 2.146a.5.5 0 0 0 .708.708l3-3z"/>
                </svg>
              </button>
            </div>
          </div>
        </div>
      </div>
    </header>

    <!-- Main Content -->
    <main class="container-fluid py-4">
      <router-outlet></router-outlet>
    </main>
  `,
  styles: [`
    .tsic-header {
      background: white;
      border-bottom: 2px solid #6DBE45;
      box-shadow: 0 2px 4px rgba(0, 0, 0, 0.08);
      position: sticky;
      top: 0;
      z-index: 1000;
    }

    .tsic-brand-container {
      background: white;
      border-radius: 8px;
      padding: 8px 12px;
      box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
      width: 210px;
      height: 48px;
    }

    .tsic-logo {
      height: 32px;
      width: auto;
      object-fit: contain;
    }

    .brand-divider {
      width: 2px;
      height: 40px;
      background: linear-gradient(to bottom, transparent, rgba(0, 0, 0, 0.1) 20%, rgba(0, 0, 0, 0.1) 80%, transparent);
      margin: 0 4px;
      flex-shrink: 0;
    }

    .job-logo-container {
      background: white;
      border-radius: 6px;
      padding: 4px 8px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
      display: flex;
      align-items: center;
      height: 40px;
    }

    .job-logo {
      height: 32px;
      width: auto;
      object-fit: contain;
      max-width: 120px;
    }

    .job-brand-container {
      background: #f5f5f5;
      border-radius: 6px;
      padding: 6px 12px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.06);
      border: 1px solid rgba(0, 0, 0, 0.04);
    }

    .job-info {
      line-height: 1.2;
    }

    .tsic-brand {
      font-size: 0.75rem;
      letter-spacing: 0.05em;
      color: #6DBE45;
    }

    .tsic-label {
      font-size: 0.75rem;
      letter-spacing: 0.05em;
      color: #6DBE45;
    }

    .job-name {
      font-size: 0.875rem;
      font-weight: 500;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      max-width: 200px;
    }

    .vr {
      opacity: 0.2;
      width: 1px;
      height: 40px;
    }

    /* Dark mode support */
    :host-context([data-bs-theme="dark"]) .tsic-header {
      background: #212529;
      border-bottom-color: #5aa839;
    }

    /* TSIC brand container gets stronger shadow in dark mode */
    :host-context([data-bs-theme="dark"]) .tsic-brand-container {
      background: #ffffff;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
    }

    /* Brand divider in dark mode */
    :host-context([data-bs-theme="dark"]) .brand-divider {
      background: linear-gradient(to bottom, transparent, rgba(255, 255, 255, 0.15) 20%, rgba(255, 255, 255, 0.15) 80%, transparent);
    }

    /* Job logo container in dark mode */
    :host-context([data-bs-theme="dark"]) .job-brand-container {
      background: #2a2e33;
      border-color: rgba(255, 255, 255, 0.08);
      box-shadow: 0 1px 4px rgba(0, 0, 0, 0.3);
    }

    :host-context([data-bs-theme="dark"]) .tsic-label {
      color: #6DBE45;
    }

    :host-context([data-bs-theme="dark"]) .tsic-brand {
      color: #6DBE45;
    }

    /* Button hover states */
    .btn-success {
      background-color: #6DBE45;
      border-color: #6DBE45;
    }

    .btn-success:hover {
      background-color: #5aa839;
      border-color: #5aa839;
    }

    .btn-outline-success {
      color: #6DBE45;
      border-color: #6DBE45;
    }

    .btn-outline-success:hover {
      background-color: #6DBE45;
      border-color: #6DBE45;
      color: white;
    }
  `]
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly jobService = inject(JobService);
  private readonly router = inject(Router);
  readonly themeService = inject(ThemeService);

  private readonly STATIC_BASE_URL = 'https://statics.teamsportsinfo.com/BannerFiles';

  // Signals
  jobLogoPath = signal('');
  jobBannerPath = signal('');
  jobName = signal('');
  username = signal('');
  showRoleMenu = signal(false);
  roles = signal(['Parent', 'Director', 'Club Rep']);
  currentRole = signal('Parent'); // TODO: wire to user/role selection from AuthService

  constructor() {
    const user = this.auth.getCurrentUser();

    this.username.set(user?.username || '');
    this.showRoleMenu.set(!!user?.regId);

    // Get job logo from user token if available
    if (user?.jobLogo) {
      const logoUrl = user.jobLogo.startsWith('http')
        ? user.jobLogo
        : `${this.STATIC_BASE_URL}/${user.jobLogo}`;
      this.jobLogoPath.set(logoUrl);
    }

    // Simulate job info (replace with real JobService fetch)
    const job = this.jobService.getCurrentJob() || {
      jobPath: user?.jobPath || '',
      jobName: (user?.jobPath || 'TSIC').toUpperCase(),
      jobLogoPath: this.jobLogoPath(),
      jobBannerPath: '',
      jobBulletins: []
    };

    // Only set jobLogoPath if not already set from user
    if (!this.jobLogoPath()) {
      // Leave empty if no logo available - template will hide with @if
      this.jobLogoPath.set('');
    }
    this.jobBannerPath.set(job.jobBannerPath);
    this.jobName.set(job.jobName);
  }

  logout() {
    this.auth.logout();
  }

  switchRole() {
    this.router.navigate(['/tsic/role-selection']);
  }

  selectRole(role: string) {
    this.currentRole.set(role);
    // TODO: update role in AuthService and refresh job menu items
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }
}
