import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { BulletinsComponent } from '@shared-ui/bulletins/bulletins.component';
import { ClientBannerComponent } from '@layouts/components/client-banner/client-banner.component';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';

/**
 * Job Landing Page Component
 * 
 * Default route for /:jobPath
 * Displays job information, bulletins, and registration options
 * Handles both anonymous and authenticated users
 */
@Component({
    selector: 'app-job-landing',
    standalone: true,
    imports: [BulletinsComponent, ClientBannerComponent],
    templateUrl: './job-landing.component.html',
    styleUrl: './job-landing.component.scss'
})
export class JobLandingComponent implements OnInit {
    private readonly authService = inject(AuthService);
    private readonly jobService = inject(JobService);
    private readonly route = inject(ActivatedRoute);
    private readonly router = inject(Router);

    // Signals
    jobPath = signal<string>('');
    isAuthenticated = signal<boolean>(false);
    currentJob = computed(() => this.jobService.currentJob());
    bulletins = computed(() => this.jobService.bulletins());
    bulletinsLoading = computed(() => this.jobService.bulletinsLoading());
    bulletinsError = computed(() => this.jobService.bulletinsError());
    registrationStatuses = computed(() => this.jobService.registrationStatuses());

    // Ready when job loaded and bulletins finished loading (success or error)
    dataReady = computed(() => !!this.currentJob() && !this.bulletinsLoading());

    ngOnInit() {
        // Get jobPath from route
        let jobPathParam = this.route.snapshot.paramMap.get('jobPath');
        if (!jobPathParam && this.route.parent) {
            jobPathParam = this.route.parent.snapshot.paramMap.get('jobPath');
        }
        this.jobPath.set(jobPathParam || '');

        // Check authentication status
        this.isAuthenticated.set(this.authService.isAuthenticated());

        // Fetch job metadata first - if job doesn't exist, redirect to not-found
        this.jobService.fetchJobMetadata(this.jobPath()).subscribe({
            next: (job: any) => {
                // Job exists - set it and load additional data
                this.jobService.setJob(job);
                this.jobService.loadBulletins(this.jobPath());

                // Only load registration status if authenticated
                if (this.isAuthenticated()) {
                    this.jobService.loadRegistrationStatus(this.jobPath(), ['Player', 'Team']);
                }
            },
            error: (err: any) => {
                // Only redirect to 404 if it's a genuine 404 (job not found)
                // Network errors (status 0) are handled by the interceptor with a toast
                if (err.status === 404) {
                    this.router.navigate(['/not-found']);
                }
                // For other errors (500, 0/network, etc.), stay on page - user will see error toast
            }
        });
    }
}
