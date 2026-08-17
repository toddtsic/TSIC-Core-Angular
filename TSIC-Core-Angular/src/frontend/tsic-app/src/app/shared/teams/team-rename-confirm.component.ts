import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, OnInit, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import type { Observable } from 'rxjs';
import type { ClubAffectedJob } from '@core/api';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/** How far the confirmed rename reaches. Mirrors the backend `TeamRenameScope`. */
export type TeamRenameScope = 'this-event' | 'library';

/**
 * Which scopes this surface may offer. `this-event` = admin surfaces (LADT, Pairings, Schedule Hub,
 * Search Teams for a job admin); `both` = SuperUser in Search Teams; `library` = the club rep's own
 * library modal (the rename IS the library — no this-event option).
 */
export type TeamRenameScopeChoice = 'this-event' | 'both' | 'library';

/**
 * THE rename briefing for a team, shared by every surface that can rename one. It exists so a
 * director learns — before the write — that a rename of a club-linked team is THIS EVENT ONLY:
 * the club's library name and every other event keep theirs, and the change can be reset. The
 * library-wide rename (rep's library, SuperUser) shows the affected-events list instead.
 *
 * Modes are derived, not passed:
 *   - orphan (no `libraryName`)                → plain "Rename X to Y?"
 *   - club-linked, `newName` === `libraryName`  → reset ("back to the library name")
 *   - club-linked, otherwise                    → this-event briefing (+ "library…" for SU)
 *   - `scopeChoice` = 'library'                 → straight to the affected-events step
 *
 * `loadImpact` is invoked lazily, only when the library step is reached — job admins never pay for
 * it (and their endpoint is SuperUser-only anyway).
 */
@Component({
    selector: 'team-rename-confirm',
    standalone: true,
    imports: [TsicDialogComponent],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="cancelled.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ title() }}</h5>
                    <button type="button" class="btn-close" (click)="cancelled.emit()" aria-label="Close"></button>
                </div>

                <div class="modal-body rename-body">
                    <!-- X → Y, always -->
                    <p class="rename-pair">
                        <span class="rename-old">{{ currentName() }}</span>
                        <i class="bi bi-arrow-right rename-arrow" aria-hidden="true"></i>
                        <span class="rename-new">{{ newName() }}</span>
                    </p>

                    @switch (mode()) {
                        @case ('orphan') {
                            <p class="text-muted small mb-0">
                                This team isn't linked to a club library — the change is local to this event.
                            </p>
                        }
                        @case ('reset') {
                            <p class="mb-1">Reset this event's name back to the club's library name.</p>
                            <p class="text-muted small mb-0">
                                Schedules, brackets, standings and rosters in this event will show
                                <strong>{{ libraryName() }}</strong> again.
                            </p>
                        }
                        @case ('this-event') {
                            <ul class="rename-facts">
                                <li>
                                    <strong>This event only.</strong> Schedules, brackets, standings and rosters here
                                    will show <strong>{{ newName() }}</strong>.
                                </li>
                                <li>
                                    <strong>The club's library name stays {{ libraryName() }}.</strong>
                                    Other events keep it; the club rep still sees it in their library
                                    (and <em>{{ newName() }}</em> in this event's registered list).
                                </li>
                                <li>You can reset to the library name at any time.</li>
                            </ul>
                            <p class="text-muted small mb-0">
                                Different team entirely (merged roster, new grad year)? Use
                                <strong>Rename to New Team</strong> in Search Teams instead.
                            </p>
                        }
                        @case ('library') {
                            @if (impactLoading()) {
                                <p class="text-muted small mb-0">
                                    <span class="spinner-border spinner-border-sm me-1"></span>Checking which events are affected…
                                </p>
                            } @else if (impactError()) {
                                <p class="text-danger small mb-0">{{ impactError() }}</p>
                            } @else {
                                <p class="mb-1">
                                    <strong>Renames the club's library team</strong> — the name every event copies.
                                </p>
                                @if (affectedJobs().length > 0) {
                                    <p class="mb-1">
                                        This team plays in <strong>{{ affectedJobs().length }} scheduled
                                        event{{ affectedJobs().length !== 1 ? 's' : '' }}</strong>. Every game name in
                                        those schedules will be rewritten — including hand-typed bracket and
                                        consolation names. An event whose director renamed the team for their
                                        event only keeps that name.
                                    </p>
                                    <ul class="mb-0 rename-jobs">
                                        @for (j of affectedJobs(); track j.jobId) {
                                            <li>{{ j.jobName }} <span class="text-muted">({{ j.teamCount }} team{{ j.teamCount !== 1 ? 's' : '' }})</span></li>
                                        }
                                    </ul>
                                } @else {
                                    <p class="text-muted small mb-0">
                                        No other scheduled events — this updates the club's library and this event.
                                    </p>
                                }
                            }
                        }
                    }
                </div>

                <div class="modal-footer">
                    @if (mode() === 'library' && scopeChoice() === 'both') {
                        <button type="button" class="btn btn-outline-secondary btn-sm me-auto" (click)="backToEvent()">
                            <i class="bi bi-arrow-left me-1"></i>Back
                        </button>
                    }
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="cancelled.emit()">Cancel</button>

                    @switch (mode()) {
                        @case ('orphan') {
                            <button type="button" class="btn btn-primary btn-sm" (click)="confirmed.emit('this-event')">Rename Team</button>
                        }
                        @case ('reset') {
                            <button type="button" class="btn btn-primary btn-sm" (click)="confirmed.emit('this-event')">Reset Name</button>
                        }
                        @case ('this-event') {
                            @if (scopeChoice() === 'both') {
                                <button type="button" class="btn btn-outline-warning btn-sm" (click)="goToLibrary()">
                                    Rename in library (all events)…
                                </button>
                            }
                            <button type="button" class="btn btn-primary btn-sm" (click)="confirmed.emit('this-event')">Rename in This Event</button>
                        }
                        @case ('library') {
                            <button type="button" class="btn btn-warning btn-sm"
                                    [disabled]="impactLoading() || !!impactError()"
                                    (click)="confirmed.emit('library')">Rename in Library</button>
                        }
                    }
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        .rename-body { display: flex; flex-direction: column; gap: var(--space-3); }

        .rename-pair {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: var(--space-2);
            margin: 0;
            font-weight: var(--font-weight-semibold);
        }
        .rename-old { color: var(--brand-text-muted); text-decoration: line-through; }
        .rename-arrow { color: var(--brand-text-muted); }
        .rename-new { color: var(--brand-text); }

        .rename-facts {
            margin: 0;
            padding-left: var(--space-5);
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
        }
        .rename-jobs { padding-left: var(--space-5); }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamRenameConfirmComponent implements OnInit {
    private readonly destroyRef = inject(DestroyRef);

    /** The event copy's current name (what the schedule shows today). */
    readonly currentName = input.required<string>();
    readonly newName = input.required<string>();
    /** The club-team library name; null/undefined for an orphan team. */
    readonly libraryName = input<string | null | undefined>(null);
    readonly scopeChoice = input<TeamRenameScopeChoice>('this-event');
    /** Lazily invoked to fetch the affected-events list for the library step. */
    readonly loadImpact = input<(() => Observable<ClubAffectedJob[]>) | null>(null);

    readonly confirmed = output<TeamRenameScope>();
    readonly cancelled = output<void>();

    private readonly libraryStep = signal(false);
    readonly affectedJobs = signal<ClubAffectedJob[]>([]);
    readonly impactLoading = signal(false);
    readonly impactError = signal<string | null>(null);
    private impactLoaded = false;

    readonly mode = computed<'orphan' | 'reset' | 'this-event' | 'library'>(() => {
        if (this.scopeChoice() === 'library' || this.libraryStep()) return 'library';
        const lib = this.libraryName();
        if (!lib) return 'orphan';
        if (this.newName().trim() === lib && this.currentName() !== lib) return 'reset';
        return 'this-event';
    });

    readonly title = computed(() => {
        switch (this.mode()) {
            case 'orphan': return 'Rename Team?';
            case 'reset': return 'Reset to Library Name?';
            case 'this-event': return 'Rename for This Event';
            case 'library': return 'Rename in Library — All Events';
        }
    });

    ngOnInit(): void {
        // Library-only surfaces (the rep's modal) land on the impact step immediately.
        if (this.scopeChoice() === 'library') this.ensureImpact();
    }

    goToLibrary(): void {
        this.libraryStep.set(true);
        this.ensureImpact();
    }

    backToEvent(): void {
        this.libraryStep.set(false);
    }

    private ensureImpact(): void {
        if (this.impactLoaded) return;
        const loader = this.loadImpact();
        if (!loader) { this.impactLoaded = true; return; }
        this.impactLoading.set(true);
        this.impactError.set(null);
        loader().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: (jobs) => {
                this.affectedJobs.set(jobs);
                this.impactLoading.set(false);
                this.impactLoaded = true;
            },
            error: () => {
                this.impactLoading.set(false);
                this.impactError.set('Could not load the affected events. Try again.');
            },
        });
    }
}
