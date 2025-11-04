import { inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface JobBulletin {
    id: string;
    title: string;
    content: string;
    postedAt: string; // ISO date
}

export interface Job {
    jobId: string;
    jobPath: string;
    jobName: string;
    jobLogoPath?: string;
    jobBannerPath?: string;
    coreRegformPlayer?: boolean;
    usLaxNumberValidThroughDate?: string;
    expiryUsers?: string;
    playerProfileMetadataJson?: string;
    jsonOptions?: string;
    jobBulletins: JobBulletin[];
}

export interface RegistrationStatusRequest {
    jobPath: string;
    registrationTypes: string[];
}

export interface RegistrationStatusResponse {
    registrationType: string;
    isAvailable: boolean;
    message?: string;
    registrationUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class JobService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    // Signal for reactive state management
    public readonly currentJob = signal<Job | null>(null);
    public readonly registrationStatuses = signal<RegistrationStatusResponse[]>([]);
    public readonly registrationLoading = signal(false);
    public readonly registrationError = signal<string | null>(null);

    // Simulate fetching job info (replace with real API call as needed)
    setJob(job: Job) {
        this.currentJob.set(job);
    }

    // Example: fetch job details from API (to be implemented)
    // fetchJob(jobPath: string): Observable<Job> {
    //   return this.http.get<Job>(`/api/jobs/${jobPath}`);
    // }

    getCurrentJob(): Job | null {
        return this.currentJob();
    }

    // Command-style load that updates the currentJob signal
    loadJobMetadata(jobPath: string): void {
        this.http.get<Job>(`${this.apiUrl}/jobs/${jobPath}`).subscribe({
            next: (job) => this.currentJob.set(job),
            error: () => {
                // Leave currentJob as-is on failure
            }
        });
    }

    // Legacy Observable return (kept temporarily for callers that still expect it)
    fetchJobMetadata(jobPath: string): Observable<Job> {
        return this.http.get<Job>(`${this.apiUrl}/jobs/${jobPath}`);
    }

    // Command-style load for registration status using signals
    loadRegistrationStatus(jobPath: string, registrationTypes: string[]): void {
        const request: RegistrationStatusRequest = { jobPath, registrationTypes };
        this.registrationLoading.set(true);
        this.registrationError.set(null);
        this.http
            .post<RegistrationStatusResponse[]>(`${this.apiUrl}/registration/check-status`, request)
            .subscribe({
                next: (statuses) => {
                    this.registrationStatuses.set(statuses);
                    this.registrationLoading.set(false);
                },
                error: (err) => {
                    this.registrationError.set(
                        err?.error?.message || 'Unable to load registration information. Please try again later.'
                    );
                    this.registrationLoading.set(false);
                }
            });
    }

    // Legacy Observable return (kept temporarily for callers that still expect it)
    checkRegistrationStatus(jobPath: string, registrationTypes: string[]): Observable<RegistrationStatusResponse[]> {
        const request: RegistrationStatusRequest = { jobPath, registrationTypes };
        return this.http.post<RegistrationStatusResponse[]>(`${this.apiUrl}/registration/check-status`, request);
    }
}
