import { Component, effect, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
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
  private readonly router = inject(Router);

  jobPath = signal('');
  registrationStatuses = signal<RegistrationStatusResponse[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);
  isAuthenticated = signal(false);

  // Reflect service signals into local signals used by the template
  // Define as a field initializer so it runs within the component's injection context
  private readonly _mirrorServiceState = effect(() => {
    const statuses = this.jobService.registrationStatuses();
    const isLoading = this.jobService.registrationLoading();
    const err = this.jobService.registrationError();
    this.registrationStatuses.set(statuses);
    this.loading.set(isLoading);
    this.error.set(err);
  });

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
    this.jobService.loadJobMetadata(this.jobPath());

    // Load registration status (for both authenticated and anonymous users)
    this.loading.set(true);
    this.error.set(null);
    this.jobService.loadRegistrationStatus(this.jobPath(), ['Player']);
  }

  // Start a fresh Family Registration flow: ensure no existing auth context carries over
  startFamilyRegistration(): void {
    try { this.authService.logoutLocal(); } catch { /* no-op */ }
    const jp = this.jobPath();
    const returnUrl = `/${jp}/register-player?step=players`;
    // Provide both a concrete returnUrl (with jobPath) and a next hint for older flows
    this.router.navigate(['/tsic/family-account'], { queryParams: { next: 'register-player', returnUrl } });
  }

  // Start a fresh Player Registration flow: explicit logout for symmetry and safety
  startPlayerRegistration(): void {
    try { this.authService.logoutLocal(); } catch { /* no-op */ }
    const jp = this.jobPath();
    this.router.navigate(['/', jp, 'register-player']);
  }

  // Start a fresh Team Registration flow
  startTeamRegistration(): void {
    try { this.authService.logoutLocal(); } catch { /* no-op */ }
    const jp = this.jobPath();
    this.router.navigate(['/', jp, 'register-team']);
  }
}
