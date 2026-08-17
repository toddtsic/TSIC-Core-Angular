import { inject, Injectable, signal, computed } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, finalize, shareReplay, tap } from 'rxjs';
import { environment } from '@environments/environment';
import { skipErrorToast } from '@infrastructure/interceptors/http-error-context';
import { ToastService } from '@shared-ui/toast.service';
import type { RegistrationStatusRequest, RegistrationStatusResponse, BulletinDto, NavDto, NavItemDto, JobMetadataResponse } from '@core/api';

@Injectable({ providedIn: 'root' })
export class JobService {
    private readonly http = inject(HttpClient);
    private readonly router = inject(Router);
    private readonly toast = inject(ToastService);
    private readonly apiUrl = environment.apiUrl;

    // Signal for reactive state management
    public readonly currentJob = signal<JobMetadataResponse | null>(null);

    /**
     * jobPath of the most recent metadata fetch that came back 404 — i.e. a job that does
     * not exist. LastLocationService watches this to drop a poisoned last-job-path.
     */
    public readonly jobNotFound = signal<string | null>(null);
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

    /**
     * In-flight dedupe for `GET /jobs/{jobPath}`. The layout and the landing page each
     * request the same job's metadata within milliseconds of a cold load — two identical
     * HTTP round trips, one of whose answers the seq guard always discarded. While a
     * request for a jobPath is on the wire, every caller shares it; the slot clears when
     * it settles, so a later remount still issues a FRESH request (that refetch is
     * load-bearing — its new object reference is what re-fires the pulse on return
     * visits). A request for a DIFFERENT jobPath bypasses the slot and takes over as
     * newest via the seq guard, exactly as before.
     */
    private inflightJobPath: string | null = null;
    private inflightJob$: Observable<JobMetadataResponse> | null = null;

    /**
     * Single chokepoint for job-metadata HTTP: dedupes concurrent same-path callers, and —
     * because it is the ONE place the app learns a job does not exist — owns the 404 response.
     *
     * That 404 used to be swallowed (`error: () => {}`), which is the whole reason a mistyped
     * URL rendered a blank "Welcome" instead of a not-found page: the router had already bound
     * the segment to `:jobPath`, the layout mounted, and the server's "no such job" went in the
     * bin. Handling it HERE covers every caller — layout, landing, widget dashboard, entry,
     * family, team wizard — with no pre-flight check and no extra request. An earlier attempt
     * gated the route match on a separate existence probe instead; it cost a duplicate
     * round trip and broke the landing's in-flight dedupe (see 1c5c686f, reverted).
     *
     * skipErrorToast + an explicit toast below: the interceptor's generic 4xx handler would
     * otherwise fire a warning on top of the not-found page. Non-404 failures still toast,
     * they just do it from here so the 404 can be silent.
     */
    private requestJobMetadata(jobPath: string): Observable<JobMetadataResponse> {
        if (this.inflightJob$ && this.inflightJobPath?.toLowerCase() === jobPath.toLowerCase()) {
            return this.inflightJob$;
        }
        const seq = this.beginJobRequest(jobPath);
        this.jobMetadataLoading.set(true);
        const req$: Observable<JobMetadataResponse> = this.http
            .get<JobMetadataResponse>(`${this.apiUrl}/jobs/${jobPath}`, { context: skipErrorToast() })
            .pipe(
                tap({
                    next: metadata => {
                        if (!this.isCurrentJobRequest(seq)) return;
                        this.currentJob.set(metadata);
                        this.jobMetadataLoading.set(false);
                    },
                    error: (err: HttpErrorResponse) => {
                        if (!this.isCurrentJobRequest(seq)) return;
                        this.jobMetadataLoading.set(false);

                        if (err.status === 404) {
                            // skipLocationChange keeps the URL the user typed in the bar while
                            // showing the 404 — the address is the thing they need to see is wrong.
                            this.jobNotFound.set(jobPath);
                            this.router.navigateByUrl('/not-found', { skipLocationChange: true });
                            return;
                        }

                        // Fail LOUD but stay put: a 5xx or a dropped connection means we cannot
                        // tell whether the job exists, and bouncing a user off a real event
                        // because the API hiccuped would be far worse than a blank panel.
                        this.toast.show('Unable to load event information. Please try again.', 'danger', 7000);
                    }
                }),
                finalize(() => {
                    if (this.inflightJob$ === req$) {
                        this.inflightJob$ = null;
                        this.inflightJobPath = null;
                    }
                }),
                // refCount:false — the request stays hot for late same-path joiners even if
                // the first subscriber unsubscribes; bufferSize 1 replays the response to a
                // caller that subscribes just after it lands (slot not yet finalized).
                shareReplay({ bufferSize: 1, refCount: false })
            );
        this.inflightJob$ = req$;
        this.inflightJobPath = jobPath;
        return req$;
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

    // Command-style load that updates the currentJob signal. Signal writes happen in the
    // shared request's tap; the subscription only exists to fire it (errors surface via
    // the jobMetadataLoading signal, same as before).
    loadJobMetadata(jobPath: string): void {
        this.requestJobMetadata(jobPath).subscribe({ error: () => { /* handled in tap */ } });
    }

    // Observable-style fetch that returns Observable and updates currentJob signal.
    // Only the WRITE is guarded — the response still reaches the caller either way, so
    // the Observable contract is unchanged.
    fetchJobMetadata(jobPath: string): Observable<JobMetadataResponse> {
        return this.requestJobMetadata(jobPath);
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
