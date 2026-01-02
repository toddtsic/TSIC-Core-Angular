import { inject, Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@environments/environment';
import type { RegistrationStatusRequest, RegistrationStatusResponse, BulletinDto, MenuItemDto, MenuDto } from '@core/api';

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
    momLabel?: string;
    dadLabel?: string;
    jobBannerPath?: string;
    jobBannerBackgroundPath?: string;
    jobBannerText1?: string;
    jobBannerText2?: string;
    coreRegformPlayer?: boolean;
    usLaxNumberValidThroughDate?: string;
    expiryUsers?: string;
    playerProfileMetadataJson?: string;
    jsonOptions?: string;
    jobBulletins: JobBulletin[];
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
    public readonly bulletins = signal<BulletinDto[]>([]);
    public readonly bulletinsLoading = signal(false);
    public readonly bulletinsError = signal<string | null>(null);
    public readonly menus = signal<MenuItemDto[]>([]);
    public readonly menusLoading = signal(false);
    public readonly menusError = signal<string | null>(null);

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
            .post<RegistrationStatusResponse[]>(`${this.apiUrl}/player-registration/check-status`, request)
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
        return this.http.post<RegistrationStatusResponse[]>(`${this.apiUrl}/player-registration/check-status`, request);
    }

    /**
     * Load active bulletins for a job.
     * Updates bulletins signal on success, bulletinsError on failure.
     * Available for anonymous users.
     */
    loadBulletins(jobPath: string): void {
        this.bulletinsLoading.set(true);
        this.bulletinsError.set(null);
        this.http
            .get<BulletinDto[]>(`${this.apiUrl}/bulletins/job/${jobPath}`)
            .subscribe({
                next: (bulletins) => {
                    this.bulletins.set(bulletins);
                    this.bulletinsLoading.set(false);
                },
                error: (err) => {
                    this.bulletinsError.set(
                        err?.error?.message || 'Unable to load bulletins. Please try again later.'
                    );
                    this.bulletinsLoading.set(false);
                }
            });
    }

    /**
     * Load role-specific menus for a job.
     * Updates menus signal on success, menusError on failure.
     * Available for anonymous users (returns menu with roleId NULL).
     * JWT token automatically included via HttpClient interceptor.
     * 
     * @param jobPath - The job path to load menus for
     * @param bypassCache - Whether to bypass HTTP cache (use when auth state changes)
     */
    loadMenus(jobPath: string, bypassCache = false): void {
        this.menusLoading.set(true);
        this.menusError.set(null);

        const options = bypassCache
            ? { headers: { 'Cache-Control': 'no-cache', 'Pragma': 'no-cache' } }
            : undefined;

        this.http
            .get<MenuDto>(`${this.apiUrl}/jobs/${jobPath}/menus`, options)
            .subscribe({
                next: (menu) => {
                    this.menus.set(menu.items || []);
                    this.menusLoading.set(false);
                },
                error: (err) => {
                    this.menusError.set(
                        err?.error?.message || 'Unable to load menus. Please try again later.'
                    );
                    this.menusLoading.set(false);
                }
            });
    }
}
