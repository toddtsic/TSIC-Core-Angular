import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/** Who is reading the briefing — the wording differs (an admin is told about the rep; a rep about their list). */
export type TeamRenameAudience = 'admin' | 'rep';

/**
 * Which name the dialog is editing — set by where it was opened from, NOT by what the team is.
 * 'event'   — the Registered Teams pencil (and every admin door): this event's name.
 * 'library' — the club library: the entry the rep registers from next time.
 */
export type TeamRenameOrigin = 'event' | 'library';

/** What `confirmed` carries: the trimmed name, and whether the rep asked for the other side too. */
export interface TeamRenameConfirmation {
    name: string;
    /** Rep ticked the propagate box. Always false for an admin — they have no second side. */
    alsoPropagate: boolean;
}

/**
 * THE team-name dialog, shared by every surface that can rename a team. One modal, two origins:
 * a club rep reaches it from the Registered Teams pencil OR from their library, and only the
 * emphasis changes — the field they landed on is the one they edit, the other name is shown as
 * context with a checkbox to carry the change across. Reps see both sides because they own both.
 *
 * A director/SuperUser (`audience='admin'`) only ever sees the event side. There is deliberately no
 * library field and no checkbox for them at any origin: an admin must never write a list that isn't
 * theirs (Todd's ruling, 2026-08-17).
 *
 * NOTHING sweeps. The library seeds FUTURE registrations; it is not a mirror of live events. The
 * only way both sides move is the human ticking the box (Todd's ruling, 2026-08-18).
 *
 * Event-origin modes are derived, not passed:
 *   - orphan (no `libraryName`)                → plain "Rename X to Y?"
 *   - club-linked, `newName` === `libraryName`  → reset ("back to the library name")
 *   - club-linked, otherwise                    → this-event briefing
 *
 * `editable` — the surface has no name field of its own (the rep's grid, the library), so the
 * dialog carries the input; `newName` is then just the seed. Admin search panels pass `false`.
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
                            <label class="form-label small mb-1" for="team-rename-input">{{ fieldLabel() }}</label>
                            <input id="team-rename-input" type="text" class="form-control form-control-sm"
                                   [ngModel]="draft()" (ngModelChange)="draft.set($event)"
                                   (keydown.enter)="submit()"
                                   [attr.maxlength]="maxLength()" autocomplete="off" autofocus>
                            <div class="form-text rename-field-hint">{{ fieldHint() }}</div>
                        </div>
                    } @else {
                        <!-- X → Y, always -->
                        <p class="rename-pair">
                            <span class="rename-old">{{ currentName() }}</span>
                            <i class="bi bi-arrow-right rename-arrow" aria-hidden="true"></i>
                            <span class="rename-new">{{ effectiveNewName() }}</span>
                        </p>
                    }

                    <!-- The other side: shown as context, changed only if they ask. -->
                    @if (showPropagate()) {
                        <div class="rename-other">
                            <div class="rename-other-head">
                                <span class="rename-other-label">{{ otherLabel() }}</span>
                                <span class="rename-other-name">{{ otherName() }}</span>
                            </div>
                            <div class="form-check mb-0">
                                <input class="form-check-input" type="checkbox" id="team-rename-propagate"
                                       [ngModel]="propagate()" (ngModelChange)="propagate.set($event)">
                                <label class="form-check-label small" for="team-rename-propagate">
                                    {{ propagateLabel() }}
                                </label>
                            </div>
                            @if (propagateReplacesName()) {
                                <p class="rename-warn small mb-0">
                                    <i class="bi bi-exclamation-triangle-fill me-1" aria-hidden="true"></i>
                                    This replaces <strong>{{ otherName() }}</strong>, which you set on purpose.
                                </p>
                            }
                        </div>
                    }

                    @if (origin() === 'library') {
                        <ul class="rename-facts">
                            <li>
                                <strong>Your team list only.</strong> This is the name you'll see when you
                                register for future events.
                            </li>
                            <li>
                                @if (registeredHere()) {
                                    <strong>{{ eventLabel() }} keeps {{ currentName() }}</strong>
                                    unless you tick the box above.
                                } @else {
                                    <strong>No event changes.</strong> Events you've already registered for
                                    keep the names they have.
                                }
                            </li>
                        </ul>
                    } @else {
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
                                        @if (!propagate()) {
                                            <li>
                                                <strong>Your team list keeps {{ libraryName() }}</strong> — other events
                                                aren't affected.
                                            </li>
                                        }
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
                    }
                </div>

                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="cancelled.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary btn-sm"
                            [disabled]="!canSubmit()" (click)="submit()">{{ confirmLabel() }}</button>
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

        .rename-field-hint { color: var(--brand-text-muted); }

        /* The side they did NOT land on — quiet, but present, so the two-name model is visible
           in one glance instead of having to be inferred from which button they clicked. */
        .rename-other {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
            padding: var(--space-3);
            border: 1px solid var(--brand-border);
            border-radius: var(--radius-md);
            background: var(--bs-tertiary-bg);
        }
        .rename-other-head {
            display: flex;
            flex-wrap: wrap;
            align-items: baseline;
            gap: var(--space-2);
        }
        .rename-other-label {
            font-size: var(--font-size-sm);
            color: var(--brand-text-muted);
        }
        .rename-other-name {
            font-weight: var(--font-weight-semibold);
            color: var(--brand-text);
        }
        .rename-warn { color: var(--bs-warning-text-emphasis); }

        .form-check-input:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

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
    /** The event copy's current name (what the schedule shows today); '' when not registered here. */
    readonly currentName = input.required<string>();
    /** The proposed name — or, when `editable`, the seed for the in-dialog input. */
    readonly newName = input.required<string>();
    /** The club-team library name; null/undefined for an orphan team. */
    readonly libraryName = input<string | null | undefined>(null);
    readonly audience = input<TeamRenameAudience>('admin');
    /** The dialog owns the name input (surfaces with no name field of their own). */
    readonly editable = input(false);
    /** Which side they opened from — decides which name is editable. */
    readonly origin = input<TeamRenameOrigin>('event');
    /** Event display name, for library-origin copy ("…for Summer Classic too"). */
    readonly eventLabel = input('this event');
    /** Is this team registered in the current event? Library origin has no event side without it. */
    readonly registeredHere = input(true);

    readonly confirmed = output<TeamRenameConfirmation>();
    readonly cancelled = output<void>();

    /** In-dialog draft, reseeded only when the `newName` input changes. */
    readonly draft = linkedSignal({ source: this.newName, computation: (v) => v });

    readonly effectiveNewName = computed(() => (this.editable() ? this.draft() : this.newName()).trim());

    /** Teams.TeamName is varchar(100); Clubs.ClubTeams.ClubTeamName is varchar(80). */
    readonly maxLength = computed(() => (this.origin() === 'library' || this.propagate() ? 80 : 100));

    /** The value the edited field started at — what "changed" is measured against. */
    readonly baselineName = computed(() =>
        (this.origin() === 'library' ? (this.libraryName() ?? '') : this.currentName()).trim());

    /** The name on the side they did NOT land on. */
    readonly otherName = computed(() =>
        (this.origin() === 'library' ? this.currentName() : (this.libraryName() ?? '')).trim());

    /**
     * The propagate box exists only for a rep, and only when the other side actually exists: a
     * library entry to write (event origin) or a registered copy to write (library origin).
     */
    readonly showPropagate = computed(() => {
        if (this.audience() !== 'rep') return false;
        return this.origin() === 'library'
            ? this.registeredHere() && this.currentName().trim().length > 0
            : !!this.libraryName();
    });

    /**
     * Default ON when the two names currently agree — the typo case, where they plainly mean both.
     * Default OFF once they have diverged: that divergence was deliberate, so undoing it has to be
     * something the rep asks for rather than something the dialog assumes.
     *
     * `source` is inputs only, never the draft, so the box does not re-tick itself under the rep's
     * hand while they type — it reseeds when a different team is opened, and stays theirs after.
     */
    readonly propagate = linkedSignal({
        source: () => ({ base: this.baselineName(), other: this.otherName(), show: this.showPropagate() }),
        computation: (s: { base: string; other: string; show: boolean }) =>
            s.show && s.other.length > 0 && s.other === s.base,
    });

    /** Ticking would overwrite a name that was deliberately set — say so before they save. */
    readonly propagateReplacesName = computed(() =>
        this.propagate() && this.otherName().length > 0 && this.otherName() !== this.baselineName());

    readonly mode = computed<'orphan' | 'reset' | 'this-event'>(() => {
        const lib = this.libraryName();
        if (!lib) return 'orphan';
        if (this.effectiveNewName() === lib && this.currentName() !== lib) return 'reset';
        return 'this-event';
    });

    /** Non-empty and actually different — a no-op rename is refused here, not on the server. */
    readonly canSubmit = computed(() => {
        const n = this.effectiveNewName();
        if (n.length === 0) return false;
        if (n !== this.baselineName()) return true;
        // Name unchanged on this side, but ticking the box still has work to do on the other.
        return this.propagate() && this.otherName() !== n;
    });

    readonly fieldLabel = computed(() =>
        this.origin() === 'library' ? 'Team name in your team list' : 'Team name for this event');

    readonly fieldHint = computed(() =>
        this.origin() === 'library'
            ? 'Used when you register this team for future events.'
            : `Appears on ${this.eventLabel()} schedules, brackets, standings and rosters.`);

    readonly otherLabel = computed(() =>
        this.origin() === 'library' ? `At ${this.eventLabel()}` : 'In your team list');

    readonly propagateLabel = computed(() =>
        this.origin() === 'library'
            ? `Use this name for ${this.eventLabel()} too`
            : 'Update my team list too');

    readonly title = computed(() => {
        if (this.origin() === 'library') return 'Rename in Your Team List';
        switch (this.mode()) {
            case 'orphan': return 'Rename Team?';
            case 'reset': return 'Reset to Library Name?';
            case 'this-event': return 'Rename for This Event';
        }
    });

    readonly confirmLabel = computed(() => {
        if (this.origin() === 'library') return this.propagate() ? 'Rename in Both' : 'Rename in My List';
        switch (this.mode()) {
            case 'orphan': return 'Rename Team';
            case 'reset': return 'Reset Name';
            case 'this-event': return this.propagate() ? 'Rename in Both' : 'Rename in This Event';
        }
    });

    submit(): void {
        if (!this.canSubmit()) return;
        this.confirmed.emit({ name: this.effectiveNewName(), alsoPropagate: this.showPropagate() && this.propagate() });
    }
}
