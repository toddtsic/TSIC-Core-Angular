import { inject, Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '@environments/environment';
import type { RegistrationStatusRequest, RegistrationStatusResponse, BulletinDto, NavDto, NavItemDto, JobMetadataResponse } from '@core/api';

@Injectable({ providedIn: 'root' })
export class JobService {
    private readonly http = inject(HttpClient);
    private readonly apiUrl = environment.apiUrl;

    // Signal for reactive state management
    public readonly currentJob = signal<JobMetadataResponse | null>(null);
    public readonly jobMetadataLoading = signal(false);
    public readonly registrationStatuses = signal<RegistrationStatusResponse[]>([]);
    public readonly registrationLoading = signal(false);
    public readonly registrationError = signal<string | null>(null);
    public readonly bulletins = signal<BulletinDto[]>([]);
    public readonly bulletinsLoading = signal(false);
    public readonly bulletinsError = signal<string | null>(null);
    public readonly navItems = signal<NavItemDto[]>([]);
    public readonly navLoading = signal(false);
    public readonly navError = signal<string | null>(null);

    // Computed: Team registration status from job metadata
    public readonly isTeamRegistrationOpen = computed(() =>
        this.currentJob()?.bRegistrationAllowTeam ?? false
    );

    /**
     * Stale-write guard for `currentJob`. A cross-job role switch puts metadata requests
     * for TWO jobs in flight at once (the layout refetches the outgoing job the moment the
     * new token lands, then the landing fetches the incoming one), and plain
     * last-response-wins let the slower OLD response revert `currentJob` — which in turn
     * re-fired the pulse for the wrong job and left the smart-bulletin band showing the
     * previous event's lifecycle phase until a browser refresh.
     *
     * The guard is on the WRITE, not on cancellation: both requests are legitimately
     * issued, so whichever answers after a newer one was asked for is simply discarded.
     */
    private jobRequestSeq = 0;
    private latestJobPath: string | null = null;

    /** Same guard, independent stream — bulletins are fetched per jobPath alongside metadata. */
    private bulletinRequestSeq = 0;

    /** Stamp a new job-metadata request and become the one whose answer counts. */
    private beginJobRequest(jobPath: string): number {
        this.latestJobPath = jobPath;
        return ++this.jobRequestSeq;
    }

    /** True while `seq` is still the newest job-metadata request issued. */
    private isCurrentJobRequest(seq: number): boolean {
        return seq === this.jobRequestSeq;
    }

    // Set current job metadata. Guarded by jobPath (not seq) because callers hand us an
    // already-resolved response — a caller holding the result of a SUPERSEDED fetch must
    // not re-apply it after a newer job has been requested. Case-insensitive to match the
    // backend's OrdinalIgnoreCase jobPath handling, so a route-cased path never self-rejects.
    setJob(job: JobMetadataResponse) {
        if (this.latestJobPath && job.jobPath?.toLowerCase() !== this.latestJobPath.toLowerCase()) return;
        this.currentJob.set(job);
    }

    getCurrentJob(): JobMetadataResponse | null {
        return this.currentJob();
    }

    // Command-style load that updates the currentJob signal
    loadJobMetadata(jobPath: string): void {
        const seq = this.beginJobRequest(jobPath);
        this.jobMetadataLoading.set(true);
        this.http.get<JobMetadataResponse>(`${this.apiUrl}/jobs/${jobPath}`).subscribe({
            next: (metadata) => {
                if (!this.isCurrentJobRequest(seq)) return;
                this.currentJob.set(metadata);
                this.jobMetadataLoading.set(false);
            },
            error: () => {
                if (!this.isCurrentJobRequest(seq)) return;
                this.jobMetadataLoading.set(false);
            }
        });
    }

    // Observable-style fetch that returns Observable and updates currentJob signal.
    // Only the WRITE is guarded — the response still reaches the caller either way, so
    // the Observable contract is unchanged.
    fetchJobMetadata(jobPath: string): Observable<JobMetadataResponse> {
        const seq = this.beginJobRequest(jobPath);
        this.jobMetadataLoading.set(true);
        return this.http.get<JobMetadataResponse>(`${this.apiUrl}/jobs/${jobPath}`).pipe(
            tap({
                next: metadata => {
                    if (!this.isCurrentJobRequest(seq)) return;
                    this.currentJob.set(metadata);
                    this.jobMetadataLoading.set(false);
                },
                error: () => {
                    if (!this.isCurrentJobRequest(seq)) return;
                    this.jobMetadataLoading.set(false);
                }
            })
        );
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
        // Same stale-write guard as currentJob, and for the same reason: the landing kicks
        // this off from the job-metadata callback, so a cross-job switch can leave two jobs'
        // bulletin requests in flight and paint the outgoing job's hand-authored bulletins
        // beside the incoming job's smart band.
        const seq = ++this.bulletinRequestSeq;
        this.bulletinsLoading.set(true);
        this.bulletinsError.set(null);
        this.http
            .get<BulletinDto[]>(`${this.apiUrl}/bulletins/job/${jobPath}`)
            .subscribe({
                next: (bulletins) => {
                    if (seq !== this.bulletinRequestSeq) return;
                    this.bulletins.set(bulletins);
                    this.bulletinsLoading.set(false);
                },
                error: (err) => {
                    if (seq !== this.bulletinRequestSeq) return;
                    this.bulletinsError.set(
                        err?.error?.message || 'Unable to load bulletins. Please try again later.'
                    );
                    this.bulletinsLoading.set(false);
                }
            });
    }

    /**
     * Load nav items for the current user's role and job.
     * Reads role/job from the JWT — no jobPath parameter needed.
     * Requires authentication; call only when user is logged in.
     */
    loadNav(): void {
        this.navLoading.set(true);
        this.navError.set(null);

        this.http
            .get<NavDto>(`${this.apiUrl}/nav/merged`)
            .subscribe({
                next: (nav) => {
                    this.navItems.set(nav.items || []);
                    this.navLoading.set(false);
                },
                error: (err) => {
                    this.navError.set(
                        err?.error?.message || 'Unable to load navigation. Please try again later.'
                    );
                    this.navLoading.set(false);
                }
            });
    }

    /** Clear nav items (on logout or unauthenticated navigation). */
    clearNav(): void {
        this.navItems.set([]);
        this.navError.set(null);
    }
}
