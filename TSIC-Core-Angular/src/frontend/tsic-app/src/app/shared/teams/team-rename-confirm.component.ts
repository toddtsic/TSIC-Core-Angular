import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/** Who is reading the briefing — the wording differs (an admin is told about the rep; a rep about their library). */
export type TeamRenameAudience = 'admin' | 'rep';

/** What `confirmed` carries: the (trimmed) name — the typed one when `editable`. */
export interface TeamRenameConfirmation {
    name: string;
}

/**
 * THE rename briefing for a team, shared by every surface that can rename one. It exists so whoever
 * renames a club-linked team learns — before the write — that the rename is THIS EVENT ONLY: the
 * club's library name and every other event keep theirs, and the change can be reset. There is
 * deliberately no library-wide option here for any role (Todd's ruling, 2026-08-17).
 *
 * Modes are derived, not passed:
 *   - orphan (no `libraryName`)                → plain "Rename X to Y?"
 *   - club-linked, `newName` === `libraryName`  → reset ("back to the library name")
 *   - club-linked, otherwise                    → this-event briefing
 *
 * `editable` — the surface has no name field of its own (the club rep's Registered Teams grid), so
 * the dialog carries the input; `newName` is then just the seed.
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
                    }
                </div>

                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="cancelled.emit()">Cancel</button>
                    @switch (mode()) {
                        @case ('orphan') {
                            <button type="button" class="btn btn-primary btn-sm" [disabled]="!canSubmit()" (click)="submit()">Rename Team</button>
                        }
                        @case ('reset') {
                            <button type="button" class="btn btn-primary btn-sm" (click)="submit()">Reset Name</button>
                        }
                        @case ('this-event') {
                            <button type="button" class="btn btn-primary btn-sm" [disabled]="!canSubmit()" (click)="submit()">Rename in This Event</button>
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
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamRenameConfirmComponent {
    /** The event copy's current name (what the schedule shows today). */
    readonly currentName = input.required<string>();
    /** The proposed name — or, when `editable`, the seed for the in-dialog input. */
    readonly newName = input.required<string>();
    /** The club-team library name; null/undefined for an orphan team. */
    readonly libraryName = input<string | null | undefined>(null);
    readonly audience = input<TeamRenameAudience>('admin');
    /** The dialog owns the name input (surfaces with no name field of their own). */
    readonly editable = input(false);

    readonly confirmed = output<TeamRenameConfirmation>();
    readonly cancelled = output<void>();

    /** In-dialog draft, reseeded only when the `newName` input changes. */
    readonly draft = linkedSignal({ source: this.newName, computation: (v) => v });

    readonly effectiveNewName = computed(() => (this.editable() ? this.draft() : this.newName()).trim());

    readonly mode = computed<'orphan' | 'reset' | 'this-event'>(() => {
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
        }
    });

    submit(): void {
        if (this.mode() !== 'reset' && !this.canSubmit()) return;
        this.confirmed.emit({ name: this.effectiveNewName() });
    }
}
