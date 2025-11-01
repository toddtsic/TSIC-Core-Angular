import { Component, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { JobService, RegistrationStatusResponse } from '../core/services/job.service';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'app-job-home',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './job-home.component.html',
  styleUrl: './job-home.component.scss'
})
export class JobHomeComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly jobService = inject(JobService);
  protected readonly authService = inject(AuthService);

  jobPath = signal('');
  registrationStatuses = signal<RegistrationStatusResponse[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  isAuthenticated = signal(false);

  ngOnInit() {
    // Get jobPath from route - check parent if on /home child route
    let jobPathParam = this.route.snapshot.paramMap.get('jobPath');
    if (!jobPathParam && this.route.parent) {
      jobPathParam = this.route.parent.snapshot.paramMap.get('jobPath');
    }
    this.jobPath.set(jobPathParam || '');

    // Check if user is authenticated
    this.isAuthenticated.set(this.authService.isAuthenticated());

    // Fetch job metadata (for both authenticated and anonymous users)
    this.loadJobMetadata();

    // Load registration status (for both authenticated and anonymous users)
    this.loadRegistrationStatus();
  }

  private loadJobMetadata() {
    this.jobService.fetchJobMetadata(this.jobPath()).subscribe({
      next: (job) => {
        this.jobService.setJob(job);
      },
      error: (err) => {
        console.error('Error loading job metadata:', err);
        // Don't show error to user - registration status is more critical
      }
    });
  }

  private loadRegistrationStatus() {
    this.loading.set(true);
    this.error.set(null);

    // For Phase 1, only check Player registration
    const registrationTypes = ['Player'];

    this.jobService.checkRegistrationStatus(this.jobPath(), registrationTypes).subscribe({
      next: (statuses) => {
        this.registrationStatuses.set(statuses);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading registration status:', err);
        this.error.set('Unable to load registration information. Please try again later.');
        this.loading.set(false);
      }
    });
  }
}
