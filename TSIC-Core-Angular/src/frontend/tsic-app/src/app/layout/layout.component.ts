import { Component, effect, inject, signal } from '@angular/core';
import type { Job } from '../core/services/job.service';
import { CommonModule } from '@angular/common';
import { Router, RouterOutlet, RouterLink } from '@angular/router';
import { AuthService } from '../core/services/auth.service';
import { JobService } from '../core/services/job.service';
import { JobContextService } from '../core/services/job-context.service';
import { ThemeService } from '../core/services/theme.service';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';

@Component({
  selector: 'app-layout',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, MatButtonModule, MatChipsModule],
  template: `
    <!-- Header -->
    <header class="tsic-header">
      <div class="container-fluid">
        <div class="row align-items-center py-2 py-md-2 py-1">
          <!-- Left: TSIC + Job Logos (auto-width) -->
          <div class="col-auto">
            <!-- Button group on mobile -->
            <div class="btn-group d-md-none">
              <button type="button" mat-stroked-button class="p-2">
                <img src="images/tsic-notext-logo.png" alt="TSIC" style="height: 24px; display: block;" />
              </button>
              @if (jobLogoPath()) {
                <button type="button" mat-stroked-button class="p-2">
                  <img [src]="jobLogoPath()" alt="Job Logo" style="height: 24px; display: block;" />
                </button>
              }
            </div>
            
            <!-- Full layout on desktop -->
            <div class="d-none d-md-flex align-items-center gap-2">
              <!-- TSIC Logo with light background -->
              <div class="tsic-brand-container">
                <img src="images/tsic-logo.png" alt="TSIC" class="tsic-logo" />
              </div>
              
              <!-- Divider -->
              <div class="brand-divider"></div>
              
              <!-- Job Info Container (logo + name pill) -->
              <div class="job-brand-container d-flex align-items-center gap-2 flex-md-grow-1">
                @if (jobName()) {
                  <button type="button" mat-stroked-button class="job-button d-inline-flex align-items-center gap-2 px-2 py-1" (click)="goHome()" [title]="jobName()">
                    @if (jobLogoPath()) {
                      <img [src]="jobLogoPath()" alt="Job Logo" class="job-logo-inline" />
                    }
                    <span class="job-name-text text-muted fw-medium">{{ jobName() }}</span>
                  </button>
                }
              </div>
            </div>
          </div>

          <!-- Middle: Flexible spacer / Job Name on mobile -->
          <div class="col">
            <div class="text-center d-md-none">
              @if (jobName()) {
                <span class="fw-semibold text-success" style="font-size: 0.75rem;">{{ jobName() }}</span>
              }
            </div>
          </div>

          <!-- Right: User Actions (auto-width) -->
          <div class="col-auto">
            <div class="d-flex align-items-center justify-content-end gap-2">
              <span class="user-role-pill d-none d-md-inline" *ngIf="username() as u">
                <mat-chip-set class="me-1"><mat-chip>{{ u }}</mat-chip></mat-chip-set>
                <ng-container *ngIf="roleName() as r; else noRole">
                  <span class="text-muted small">as</span>
                  <mat-chip-set class="ms-1"><mat-chip>{{ r }}</mat-chip></mat-chip-set>
                </ng-container>
                <ng-template #noRole></ng-template>
              </span>
              
              <!-- Button group on mobile, separate buttons on desktop -->
              <div class="btn-group d-md-none">
                <!-- Home -->
                <button 
                  type="button" 
                  mat-stroked-button color="primary"
                  (click)="goHome()"
                  title="Home">
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M8.354 1.146a.5.5 0 0 0-.708 0l-6 6a.5.5 0 1 0 .708.708L2 7.207V14.5A1.5 1.5 0 0 0 3.5 16h2A1.5 1.5 0 0 0 7 14.5V11h2v3.5A1.5 1.5 0 0 0 10.5 16h2a1.5 1.5 0 0 0 1.5-1.5V7.207l.646.647a.5.5 0 0 0 .708-.708l-6-6z"/>
                  </svg>
                </button>
                @if (showRoleMenu()) {
                  <a 
                    role="button"
                    mat-stroked-button color="accent"
                    [routerLink]="['/tsic/role-selection']"
                    (click)="onSwitchRole($event)"
                    title="Switch Role">
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                      <path d="M1 14s-1 0-1-1 1-4 6-4 6 3 6 4-1 1-1 1H1zm5-6a3 3 0 1 0 0-6 3 3 0 0 0 0 6z"/>
                      <path fill-rule="evenodd" d="M13.5 5a.5.5 0 0 1 .5.5V7h1.5a.5.5 0 0 1 0 1H14v1.5a.5.5 0 0 1-1 0V8h-1.5a.5.5 0 0 1 0-1H13V5.5a.5.5 0 0 1 .5-.5z"/>
                    </svg>
                  </a>
                }
                <button 
                  type="button" 
                  mat-stroked-button
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
                @if (isAuthenticated()) {
                  <button 
                    type="button" 
                    mat-stroked-button color="warn"
                    (click)="logout()"
                    title="Logout">
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                      <path fill-rule="evenodd" d="M10 12.5a.5.5 0 0 1-.5.5h-8a.5.5 0 0 1-.5-.5v-9a.5.5 0 0 1 .5-.5h8a.5.5 0 0 1 .5.5v2a.5.5 0 0 0 1 0v-2A1.5 1.5 0 0 0 9.5 2h-8A1.5 1.5 0 0 0 0 3.5v9A1.5 1.5 0 0 0 1.5 14h8a1.5 1.5 0 0 0 1.5-1.5v-2a.5.5 0 0 0-1 0v2z"/>
                      <path fill-rule="evenodd" d="M15.854 8.354a.5.5 0 0 0 0-.708l-3-3a.5.5 0 0 0-.708.708L14.293 7.5H5.5a.5.5 0 0 0 0 1h8.793l-2.147 2.146a.5.5 0 0 0 .708.708l3-3z"/>
                    </svg>
                  </button>
                } @else {
                  <button 
                    type="button" 
                    mat-stroked-button color="accent"
                    (click)="login()"
                    title="Login">
                    <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                      <path fill-rule="evenodd" d="M6 3.5a.5.5 0 0 1 .5-.5h8a.5.5 0 0 1 .5.5v9a.5.5 0 0 1-.5.5h-8a.5.5 0 0 1-.5-.5v-2a.5.5 0 0 0-1 0v2A1.5 1.5 0 0 0 6.5 14h8a1.5 1.5 0 0 0 1.5-1.5v-9A1.5 1.5 0 0 0 14.5 2h-8A1.5 1.5 0 0 0 5 3.5v2a.5.5 0 0 0 1 0v-2z"/>
                      <path fill-rule="evenodd" d="M11.854 8.354a.5.5 0 0 0 0-.708l-3-3a.5.5 0 1 0-.708.708L10.293 7.5H1.5a.5.5 0 0 0 0 1h8.793l-2.147 2.146a.5.5 0 0 0 .708.708l3-3z"/>
                    </svg>
                  </button>
                }
              </div>
              
              <!-- Separate buttons on desktop -->
              <button 
                type="button" 
                mat-stroked-button color="primary" class="d-none d-md-inline-flex" 
                (click)="goHome()"
                title="Home">
                <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                  <path d="M8.354 1.146a.5.5 0 0 0-.708 0l-6 6a.5.5 0 1 0 .708.708L2 7.207V14.5A1.5 1.5 0 0 0 3.5 16h2A1.5 1.5 0 0 0 7 14.5V11h2v3.5A1.5 1.5 0 0 0 10.5 16h2a1.5 1.5 0 0 0 1.5-1.5V7.207l.646.647a.5.5 0 0 0 .708-.708l-6-6z"/>
                </svg>
              </button>
              @if (showRoleMenu()) {
                <a 
                  role="button"
                  mat-stroked-button color="accent" class="d-none d-md-inline-flex" 
                  [routerLink]="['/tsic/role-selection']"
                  (click)="onSwitchRole($event)"
                  title="Switch Role">
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path d="M1 14s-1 0-1-1 1-4 6-4 6 3 6 4-1 1-1 1H1zm5-6a3 3 0 1 0 0-6 3 3 0 0 0 0 6z"/>
                    <path fill-rule="evenodd" d="M13.5 5a.5.5 0 0 1 .5.5V7h1.5a.5.5 0 0 1 0 1H14v1.5a.5.5 0 0 1-1 0V8h-1.5a.5.5 0 0 1 0-1H13V5.5a.5.5 0 0 1 .5-.5z"/>
                  </svg>
                </a>
              }
              <button 
                type="button" 
                mat-stroked-button class="d-none d-md-inline-flex" 
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
              @if (isAuthenticated()) {
                <button 
                  type="button" 
                  mat-stroked-button color="warn" class="d-none d-md-inline-flex" 
                  (click)="logout()"
                  title="Logout">
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path fill-rule="evenodd" d="M10 12.5a.5.5 0 0 1-.5.5h-8a.5.5 0 0 1-.5-.5v-9a.5.5 0 0 1 .5-.5h8a.5.5 0 0 1 .5.5v2a.5.5 0 0 0 1 0v-2A1.5 1.5 0 0 0 9.5 2h-8A1.5 1.5 0 0 0 0 3.5v9A1.5 1.5 0 0 0 1.5 14h8a1.5 1.5 0 0 0 1.5-1.5v-2a.5.5 0 0 0-1 0v2z"/>
                    <path fill-rule="evenodd" d="M15.854 8.354a.5.5 0 0 0 0-.708l-3-3a.5.5 0 0 0-.708.708L14.293 7.5H5.5a.5.5 0 0 0 0 1h8.793l-2.147 2.146a.5.5 0 0 0 .708.708l3-3z"/>
                  </svg>
                </button>
              } @else {
                <button 
                  type="button" 
                  mat-stroked-button color="accent" class="d-none d-md-inline-flex" 
                  (click)="login()"
                  title="Login">
                  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" fill="currentColor" viewBox="0 0 16 16">
                    <path fill-rule="evenodd" d="M6 3.5a.5.5 0 0 1 .5-.5h8a.5.5 0 0 1 .5.5v9a.5.5 0 0 1-.5.5h-8a.5.5 0 0 1-.5-.5v-2a.5.5 0 0 0-1 0v2A1.5 1.5 0 0 0 6.5 14h8a1.5 1.5 0 0 0 1.5-1.5v-9A1.5 1.5 0 0 0 14.5 2h-8A1.5 1.5 0 0 0 5 3.5v2a.5.5 0 0 0 1 0v-2z"/>
                    <path fill-rule="evenodd" d="M11.854 8.354a.5.5 0 0 0 0-.708l-3-3a.5.5 0 1 0-.708.708L10.293 7.5H1.5a.5.5 0 0 0 0 1h8.793l-2.147 2.146a.5.5 0 0 0 .708.708l3-3z"/>
                  </svg>
                </button>
              }
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

    /* Compact logo on mobile */
    @media (max-width: 767.98px) {
      .tsic-brand-container {
        width: 36px;
        height: 36px;
        padding: 4px;
      }
      
      .job-brand-container {
        width: 36px;
        height: 36px;
        padding: 4px;
        flex-grow: 0;
      }
      
      .tsic-logo,
      .job-logo {
        height: 28px;
      }
      
      /* Ensure button groups have proper shared borders */
      .btn-group > .btn {
        border-radius: 0;
      }
      
      .btn-group > .btn:first-child {
        border-top-left-radius: 0.25rem;
        border-bottom-left-radius: 0.25rem;
      }
      
      .btn-group > .btn:last-child {
        border-top-right-radius: 0.25rem;
        border-bottom-right-radius: 0.25rem;
      }
      
      .btn-group > .btn:not(:last-child) {
        border-right-width: 0;
      }
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

    .job-logo-inline {
      height: 20px;
      width: auto;
      object-fit: contain;
      display: block;
    }

    .job-button {
      background: #ffffff;
      border-color: rgba(0,0,0,0.15);
      border-radius: 6px;
      box-shadow: 0 1px 3px rgba(0,0,0,0.06);
      transition: background-color .15s, box-shadow .15s;
      line-height: 1;
    }
    .job-button:hover {
      background: #f8f9fa;
      box-shadow: 0 2px 6px rgba(0,0,0,0.12);
    }
    .job-name-text {
      font-size: 0.75rem;
      white-space: nowrap;
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
      /* Let the job label grow with its text; don't truncate */
      white-space: nowrap;
      overflow: visible;
      text-overflow: clip;
      max-width: none;
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
    :host-context([data-bs-theme="dark"]) .job-button {
      background: #2f3439;
      border-color: rgba(255,255,255,0.15);
      color: #ddd;
    }
    :host-context([data-bs-theme="dark"]) .job-button:hover {
      background: #3a4046;
      box-shadow: 0 2px 6px rgba(0,0,0,0.5);
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
    /* User/Role badges */
    .user-role-pill { display: inline-flex; align-items: center; }
    .badge.bg-user {
      background: #e9f7ef; /* light green tint */
      color: #2e7d32;
      font-weight: 600;
    }
    .badge.bg-role {
      background: #e7f1ff; /* light blue tint */
      color: #0d47a1;
      font-weight: 600;
    }

    :host-context([data-bs-theme="dark"]) .badge.bg-user {
      background: rgba(109,190,69,.2);
      color: #9fe28a;
    }
    :host-context([data-bs-theme="dark"]) .badge.bg-role {
      background: rgba(13,71,161,.25);
      color: #9ec3ff;
    }
  `]
})
export class LayoutComponent {
  private readonly auth = inject(AuthService);
  private readonly jobService = inject(JobService);
  private readonly router = inject(Router);
  private readonly jobContext = inject(JobContextService);
  readonly themeService = inject(ThemeService);

  private readonly STATIC_BASE_URL = 'https://statics.teamsportsinfo.com/BannerFiles';

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

    // One-shot attempt to load job metadata early; later changes to jobPath after init
    // (e.g. async routing) should be handled elsewhere (kept minimal to reduce effect count).
    const jp = this.jobContext.jobPath();
    if (jp && !this.jobService.currentJob()) {
      this.jobService.loadJobMetadata(jp);
    }
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
}
