import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '@infrastructure/services/auth.service';
import { JobService } from '@infrastructure/services/job.service';
import { BulletinsComponent } from '../shared/bulletins/bulletins.component';
import type { RegistrationStatusResponse } from '@infrastructure/api';

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
    imports: [CommonModule, BulletinsComponent],
    templateUrl: './job-landing.component.html',
    styleUrl: './job-landing.component.scss'
})
export class JobLandingComponent {
    private authService = inject(AuthService);
    private jobService = inject(JobService);
    private route = inject(ActivatedRoute);
    private router = inject(Router);

    // Signals
    jobPath = signal<string>('');
    isAuthenticated = signal<boolean>(false);
    currentJob = computed(() => this.jobService.currentJob());
    bulletins = computed(() => this.jobService.bulletins());
    bulletinsLoading = computed(() => this.jobService.bulletinsLoading());
    bulletinsError = computed(() => this.jobService.bulletinsError());
    registrationStatuses = computed(() => this.jobService.registrationStatuses());

    ngOnInit() {
        // Get jobPath from route
        let jobPathParam = this.route.snapshot.paramMap.get('jobPath');
        if (!jobPathParam && this.route.parent) {
            jobPathParam = this.route.parent.snapshot.paramMap.get('jobPath');
        }
        this.jobPath.set(jobPathParam || '');

        // Check authentication status
        this.isAuthenticated.set(this.authService.isAuthenticated());

        // Always load job metadata and bulletins (available for anonymous users)
        this.jobService.loadJobMetadata(this.jobPath());
        this.jobService.loadBulletins(this.jobPath());

        // Only load registration status if authenticated
        if (this.isAuthenticated()) {
            this.jobService.loadRegistrationStatus(this.jobPath(), ['Player', 'Team']);
        }
    }
}
