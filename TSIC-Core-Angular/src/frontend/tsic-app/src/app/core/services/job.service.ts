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

    fetchJobMetadata(jobPath: string): Observable<Job> {
        return this.http.get<Job>(`${this.apiUrl}/jobs/${jobPath}`);
    }

    checkRegistrationStatus(jobPath: string, registrationTypes: string[]): Observable<RegistrationStatusResponse[]> {
        const request: RegistrationStatusRequest = {
            jobPath,
            registrationTypes
        };

        return this.http.post<RegistrationStatusResponse[]>(
            `${this.apiUrl}/registration/check-status`,
            request
        );
    }
}
