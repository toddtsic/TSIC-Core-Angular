import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet } from '@angular/router';
import { ButtonModule } from '@syncfusion/ej2-angular-buttons';
import { AuthService } from '../core/services/auth.service';
import { JobService } from '../core/services/job.service';
import { ThemeService } from '../core/services/theme.service';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet, ButtonModule],
  template: `
    <header class="layout-header">
      <div class="branding">
        <img *ngIf="jobLogoPath" [src]="jobLogoPath" alt="Job Logo" class="job-logo-img" />
        <div class="job-banner" *ngIf="jobBannerPath">
          <img [src]="jobBannerPath" alt="Job Banner" />
        </div>
        <div class="job-name">{{ jobName }}</div>
      </div>
      <nav class="role-nav">
        <ng-container *ngFor="let role of roles">
          <button ejs-button cssClass="e-flat role-btn" [ngClass]="{active: role === currentRole}" (click)="selectRole(role)">{{ role }}</button>
        </ng-container>
      </nav>
      <div class="user-info">
        <span class="username">{{ username }}</span>
        <button ejs-button cssClass="e-outline" *ngIf="showRoleMenu" (click)="switchRole()">Switch Role</button>
        <button ejs-button cssClass="e-flat e-danger" (click)="logout()">Logout</button>
        <button ejs-button cssClass="e-flat" (click)="toggleTheme()">{{ themeService.theme() === 'light' ? 'Dark' : 'Light' }} Mode</button>
      </div>
    </header>
    <main class="layout-content">
      <router-outlet></router-outlet>
    </main>
  `,
  styleUrls: ['./layout.component.scss']
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly jobService = inject(JobService);
  readonly themeService = inject(ThemeService);

  jobLogoPath = '';
  jobBannerPath = '';
  jobName = '';
  username = '';
  showRoleMenu = false;
  roles = ['Parent', 'Director', 'Club Rep'];
  currentRole = 'Parent'; // TODO: wire to user/role selection from AuthService

  constructor() {
    const user = this.auth.getCurrentUser();
    this.username = user?.username || '';
    this.showRoleMenu = !!user?.regId;
    // Simulate job info (replace with real JobService fetch)
    const job = this.jobService.getCurrentJob() || {
      jobPath: user?.jobPath || '',
      jobName: (user?.jobPath || 'TSIC').toUpperCase(),
      jobLogoPath: '/assets/branding/default-logo.svg',
      jobBannerPath: '/assets/branding/default-banner.svg',
      jobBulletins: []
    };
    this.jobLogoPath = job.jobLogoPath;
    this.jobBannerPath = job.jobBannerPath;
    this.jobName = job.jobName;
  }

  logout() {
    this.auth.logout();
  }

  switchRole() {
    // TODO: navigate to role selection page
  }

  selectRole(role: string) {
    this.currentRole = role;
    // TODO: update role in AuthService and refresh job menu items
  }

  toggleTheme() {
    this.themeService.toggleTheme();
  }
}
