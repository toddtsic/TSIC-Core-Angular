import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, input, linkedSignal, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import type { Observable } from 'rxjs';
import type { ClubAffectedJob } from '@core/api';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/** How far the confirmed rename reaches. Mirrors the backend `TeamRenameScope`. */
export type TeamRenameScope = 'this-event' | 'library';

/**
 * Which scopes this surface may offer. `this-event` = every surface but one; `both` = SuperUser in
 * Search Teams, who may step through to the library-wide rename (affected-events briefing).
 */
export type TeamRenameScopeChoice = 'this-event' | 'both';

/** Who is reading the briefing — the wording differs (an admin is told about the rep; a rep about their library). */
export type TeamRenameAudience = 'admin' | 'rep';

/** What `confirmed` carries: the chosen scope and the (trimmed) name — the typed one when `editable`. */
export interface TeamRenameConfirmation {
    scope: TeamRenameScope;
    name: string;
}

/**
 * THE rename briefing for a team, shared by every surface that can rename one. It exists so whoever
 * renames a club-linked team learns — before the write — that the rename is THIS EVENT ONLY: the
 * club's library name and every other event keep theirs, and the change can be reset. The
 * library-wide rename (SuperUser only) shows the affected-events list instead.
 *
 * Modes are derived, not passed:
 *   - orphan (no `libraryName`)                → plain "Rename X to Y?"
 *   - club-linked, `newName` === `libraryName`  → reset ("back to the library name")
 *   - club-linked, otherwise                    → this-event briefing (+ "library…" for SU)
 *
 * `editable` — the surface has no name field of its own (the club rep's Registered Teams grid), so
 * the dialog carries the input; `newName` is then just the seed. `loadImpact` is invoked lazily,
 * only when the library step is reached — job admins never pay for it.
 */
@Component({
    selector: 'team-rename-confirm',
    standalone: true,
    imports: [FormsModule, TsicDialogComponent],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="cancelled.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ title() }}</h5>
                    <button type="button" class="btn-close" (click)="cancelled.emit()" aria-label="Close"></button>
                </div>

                <div class="modal-body rename-body">
                    @if (editable()) {
                        <div>
                            <label class="form-label small mb-1" for="team-rename-input">Team name for this event</label>
                            <input id="team-rename-input" type="text" class="form-control form-control-sm"
                                   [ngModel]="draft()" (ngModelChange)="draft.set($event)"
                                   (keydown.enter)="submit()"
                                   maxlength="100" autocomplete="off" autofocus>
                        </div>
                    } @else {
                        <!-- X → Y, always -->
                        <p class="rename-pair">
                            <span class="rename-old">{{ currentName() }}</span>
                            <i class="bi bi-arrow-right rename-arrow" aria-hidden="true"></i>
                            <span class="rename-new">{{ effectiveNewName() }}</span>
                        </p>
                    }

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
                                    will show <strong>{{ effectiveNewName() || '…' }}</strong>.
                                </li>
                                @if (audience() === 'rep') {
                                    <li>
                                        <strong>Your club library keeps {{ libraryName() }}</strong> — other events
                                        aren't affected.
                                    </li>
                                } @else {
                                    <li>
                                        <strong>The club's library name stays {{ libraryName() }}.</strong>
                                        Other events keep it; the club rep still sees it in their library
                                        (and <em>{{ effectiveNewName() || '…' }}</em> in this event's registered list).
                                    </li>
                                }
                                <li>You can reset to the library name at any time.</li>
                            </ul>
                            @if (audience() === 'admin') {
                                <p class="text-muted small mb-0">
                                    Different team entirely (merged roster, new grad year)? Use
                                    <strong>Rename to New Team</strong> in Search Teams instead.
                                </p>
                            }
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
                            <button type="button" class="btn btn-primary btn-sm" [disabled]="!canSubmit()" (click)="submit()">Rename Team</button>
                        }
                        @case ('reset') {
                            <button type="button" class="btn btn-primary btn-sm" (click)="submit()">Reset Name</button>
                        }
                        @case ('this-event') {
                            @if (scopeChoice() === 'both') {
                                <button type="button" class="btn btn-outline-warning btn-sm" (click)="goToLibrary()">
                                    Rename in library (all events)…
                                </button>
                            }
                            <button type="button" class="btn btn-primary btn-sm" [disabled]="!canSubmit()" (click)="submit()">Rename in This Event</button>
                        }
                        @case ('library') {
                            <button type="button" class="btn btn-warning btn-sm"
                                    [disabled]="impactLoading() || !!impactError()"
                                    (click)="confirmed.emit({ scope: 'library', name: effectiveNewName() })">Rename in Library</button>
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
export class TeamRenameConfirmComponent {
    private readonly destroyRef = inject(DestroyRef);

    /** The event copy's current name (what the schedule shows today). */
    readonly currentName = input.required<string>();
    /** The proposed name — or, when `editable`, the seed for the in-dialog input. */
    readonly newName = input.required<string>();
    /** The club-team library name; null/undefined for an orphan team. */
    readonly libraryName = input<string | null | undefined>(null);
    readonly scopeChoice = input<TeamRenameScopeChoice>('this-event');
    readonly audience = input<TeamRenameAudience>('admin');
    /** The dialog owns the name input (surfaces with no name field of their own). */
    readonly editable = input(false);
    /** Lazily invoked to fetch the affected-events list for the library step. */
    readonly loadImpact = input<(() => Observable<ClubAffectedJob[]>) | null>(null);

    readonly confirmed = output<TeamRenameConfirmation>();
    readonly cancelled = output<void>();

    /** In-dialog draft, reseeded only when the `newName` input changes. */
    readonly draft = linkedSignal({ source: this.newName, computation: (v) => v });

    private readonly libraryStep = signal(false);
    readonly affectedJobs = signal<ClubAffectedJob[]>([]);
    readonly impactLoading = signal(false);
    readonly impactError = signal<string | null>(null);
    private impactLoaded = false;

    readonly effectiveNewName = computed(() => (this.editable() ? this.draft() : this.newName()).trim());

    readonly mode = computed<'orphan' | 'reset' | 'this-event' | 'library'>(() => {
        if (this.libraryStep()) return 'library';
        const lib = this.libraryName();
        if (!lib) return 'orphan';
        if (this.effectiveNewName() === lib && this.currentName() !== lib) return 'reset';
        return 'this-event';
    });

    /** Non-empty and actually different — a no-op rename is refused here, not on the server. */
    readonly canSubmit = computed(() => {
        const n = this.effectiveNewName();
        return n.length > 0 && n !== this.currentName().trim();
    });

    readonly title = computed(() => {
        switch (this.mode()) {
            case 'orphan': return 'Rename Team?';
            case 'reset': return 'Reset to Library Name?';
            case 'this-event': return 'Rename for This Event';
            case 'library': return 'Rename in Library — All Events';
        }
    });

    submit(): void {
        if (this.mode() === 'library') return;
        if (this.mode() !== 'reset' && !this.canSubmit()) return;
        this.confirmed.emit({ scope: 'this-event', name: this.effectiveNewName() });
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
