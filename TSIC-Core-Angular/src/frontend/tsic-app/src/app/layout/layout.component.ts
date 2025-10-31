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
    <!-- Job Header -->
    <header class="bg-white border-bottom shadow-sm sticky-top">
      <div class="container-fluid">
        <div class="row align-items-center py-3">
          <!-- Job Branding -->
          <div class="col-md-3">
            <div class="d-flex align-items-center gap-2">
              @if (jobLogoPath()) {
                <img [src]="jobLogoPath()" alt="Job Logo" class="job-logo" style="height: 40px; width: auto;" />
              }
              <div>
                <h1 class="h5 mb-0 fw-semibold">{{ jobName() }}</h1>
              </div>
            </div>
          </div>

          <!-- Role Navigation -->
          <div class="col-md-6">
            <nav class="d-flex gap-2 justify-content-center">
              @for (role of roles(); track role) {
                <button 
                  type="button" 
                  class="btn btn-sm"
                  [class.btn-primary]="role === currentRole()"
                  [class.btn-outline-secondary]="role !== currentRole()"
                  (click)="selectRole(role)">
                  {{ role }}
                </button>
              }
            </nav>
          </div>

          <!-- User Actions -->
          <div class="col-md-3">
            <div class="d-flex align-items-center justify-content-end gap-2">
              <span class="text-secondary small d-none d-md-inline">{{ username() }}</span>
              @if (showRoleMenu()) {
                <button type="button" class="btn btn-sm btn-outline-primary" (click)="switchRole()">
                  <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z"/>
                    <path d="M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319z"/>
                  </svg>
                </button>
              }
              <button type="button" class="btn btn-sm btn-outline-secondary" (click)="toggleTheme()">
                @if (themeService.theme() === 'light') {
                  <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M6 .278a.768.768 0 0 1 .08.858 7.208 7.208 0 0 0-.878 3.46c0 4.021 3.278 7.277 7.318 7.277.527 0 1.04-.055 1.533-.16a.787.787 0 0 1 .81.316.733.733 0 0 1-.031.893A8.349 8.349 0 0 1 8.344 16C3.734 16 0 12.286 0 7.71 0 4.266 2.114 1.312 5.124.06A.752.752 0 0 1 6 .278z"/>
                  </svg>
                } @else {
                  <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M8 11a3 3 0 1 1 0-6 3 3 0 0 1 0 6zm0 1a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM8 0a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-1 0v-2A.5.5 0 0 1 8 0zm0 13a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-1 0v-2A.5.5 0 0 1 8 13zm8-5a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1 0-1h2a.5.5 0 0 1 .5.5zM3 8a.5.5 0 0 1-.5.5h-2a.5.5 0 0 1 0-1h2A.5.5 0 0 1 3 8zm10.657-5.657a.5.5 0 0 1 0 .707l-1.414 1.415a.5.5 0 1 1-.707-.708l1.414-1.414a.5.5 0 0 1 .707 0zm-9.193 9.193a.5.5 0 0 1 0 .707L3.05 13.657a.5.5 0 0 1-.707-.707l1.414-1.414a.5.5 0 0 1 .707 0zm9.193 2.121a.5.5 0 0 1-.707 0l-1.414-1.414a.5.5 0 0 1 .707-.707l1.414 1.414a.5.5 0 0 1 0 .707zM4.464 4.465a.5.5 0 0 1-.707 0L2.343 3.05a.5.5 0 1 1 .707-.707l1.414 1.414a.5.5 0 0 1 0 .708z"/>
                  </svg>
                }
              </button>
              <button type="button" class="btn btn-sm btn-outline-danger" (click)="logout()">
                <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" fill="currentColor" viewBox="0 0 16 16">
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
    .job-logo {
      object-fit: contain;
    }
  `]
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly jobService = inject(JobService);
  private readonly router = inject(Router);
  readonly themeService = inject(ThemeService);

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
    
    // Simulate job info (replace with real JobService fetch)
    const job = this.jobService.getCurrentJob() || {
      jobPath: user?.jobPath || '',
      jobName: (user?.jobPath || 'TSIC').toUpperCase(),
      jobLogoPath: '/assets/branding/default-logo.svg',
      jobBannerPath: '/assets/branding/default-banner.svg',
      jobBulletins: []
    };
    this.jobLogoPath.set(job.jobLogoPath);
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
