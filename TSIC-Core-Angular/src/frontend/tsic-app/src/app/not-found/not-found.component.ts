import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { JobService } from '../core/services/job.service';

@Component({
    selector: 'app-not-found',
    standalone: true,
    imports: [RouterLink],
    template: `
    <div class="container-fluid d-flex align-items-center justify-content-center min-vh-100">
      <div class="text-center">
        <h1 class="display-1 fw-bold">404</h1>
        <p class="fs-3 mb-4">Page Not Found</p>
        <p class="text-muted mb-4">
          The page you're looking for doesn't exist or the job path is invalid.
        </p>
        <a [routerLink]="homeLink()" class="btn btn-primary">
          <i class="bi bi-house-door me-2"></i>Go to Home
        </a>
      </div>
    </div>
  `,
    styles: [`
    :host {
      display: block;
    }
    
    .min-vh-100 {
      min-height: 100vh;
    }
    
    .display-1 {
      color: var(--bs-danger);
    }
  `]
})
export class NotFoundComponent {
    private readonly jobService = inject(JobService);
    private readonly router = inject(Router);

    // Return to current job landing page if available, otherwise TSIC home
    homeLink = computed(() => {
        // First try to get from current job signal
        const job = this.jobService.currentJob();
        if (job?.jobPath) {
            return `/${job.jobPath}`;
        }

        // If not set, try to parse from current URL (first segment)
        const url = this.router.url;
        const segments = url.split('/').filter(s => s);

        // Check if first segment is not 'tsic' (which is TSIC-specific, not a job)
        if (segments.length > 0 && segments[0] !== 'tsic') {
            return `/${segments[0]}`;
        }

        // Fall back to TSIC home
        return '/tsic';
    });
}
