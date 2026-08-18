import { ChangeDetectionStrategy, Component, computed, input, linkedSignal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';

/** Who is reading — a rep owns both places a name lives; an admin owns only the event. */
export type TeamRenameAudience = 'admin' | 'rep';

/**
 * Which name the dialog is editing — set by where it was opened from, NOT by what the team is.
 * 'event'   — the Registered Teams pencil (and every admin door): this event's name.
 * 'library' — the Club Team Library: the entry the rep registers from.
 */
export type TeamRenameOrigin = 'event' | 'library';

/** What `confirmed` carries: the trimmed name, and whether the rep asked for the other side too. */
export interface TeamRenameConfirmation {
    name: string;
    /** Rep ticked the carry-across box. Always false for an admin — they have no second side. */
    alsoPropagate: boolean;
}

/** Sentinel default for `eventLabel`: generic prose, so no event name is shown as a scope line. */
const UNNAMED_EVENT = 'this event';

/**
 * THE team-name dialog, shared by every surface that can rename a team.
 *
 * It exists to teach one model in a glance: a club-linked team's name lives in TWO places — the
 * **Club Team Library** (the club rep's saved list of teams, what they choose from when registering
 * for any event under their club rep account) and **this event's** own copy. The dialog always
 * renders both as named panels; the one they opened from is accented and editable, the other is
 * quiet context with a checkbox to carry the change across.
 *
 * A director/SuperUser (`audience='admin'`) sees the library panel as read-only context and never
 * gets the checkbox: an admin must never write a list that isn't theirs (Todd's ruling, 2026-08-17).
 *
 * NOTHING sweeps. The Club Team Library seeds FUTURE registrations; it is not a mirror of live
 * events. The only way both sides move is the human ticking the box (Todd's ruling, 2026-08-18).
 *
 * Event-origin modes are derived, not passed:
 *   - orphan (no `libraryName`)                → plain rename, no library panel
 *   - club-linked, `newName` === `libraryName`  → reset ("back to the library name")
 *   - club-linked, otherwise                    → this-event rename
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
                    <p class="rename-lede">{{ lede() }}</p>

                    <!-- ── This event ─────────────────────────────────────────────── -->
                    <section class="name-card" [class.is-active]="origin() === 'event'">
                        <header class="name-card-head">
                            <span class="name-card-eyebrow">
                                <i class="bi bi-calendar-event name-card-icon" aria-hidden="true"></i>
                                This Event
                            </span>
                            @if (namedEvent()) {
                                <span class="name-card-scope">{{ namedEvent() }}</span>
                            }
                        </header>

                        @if (origin() === 'event' && editable()) {
                            <label class="visually-hidden" for="team-rename-input">Team name for this event</label>
                            <input id="team-rename-input" type="text"
                                   class="form-control form-control-sm name-card-input"
                                   [ngModel]="draft()" (ngModelChange)="draft.set($event)"
                                   (keydown.enter)="submit()"
                                   [attr.maxlength]="maxLength()" autocomplete="off" autofocus>
                        } @else if (origin() === 'event') {
                            <p class="name-card-pair">
                                <span class="name-was">{{ currentName() }}</span>
                                <i class="bi bi-arrow-right name-arrow" aria-hidden="true"></i>
                                <span class="name-now">{{ effectiveNewName() || '…' }}</span>
                            </p>
                        } @else if (registeredHere() && propagateReplacesName()) {
                            <!-- Ticked: show what it costs, in place. The struck-through name says
                                 "this is what you are about to lose" better than a sentence can. -->
                            <p class="name-card-pair">
                                <span class="name-was">{{ currentName() }}</span>
                                <i class="bi bi-arrow-right name-arrow" aria-hidden="true"></i>
                                <span class="name-now">{{ effectiveNewName() }}</span>
                            </p>
                        } @else if (registeredHere()) {
                            <p class="name-card-value">{{ currentName() }}</p>
                        } @else {
                            <p class="name-card-value is-empty">This team isn't registered for this event.</p>
                        }

                        @if (registeredHere()) {
                            <p class="name-card-note">
                                What appears on this event's schedules, brackets, standings and rosters.
                            </p>
                        }

                        <!-- Carry a library rename across into this event. -->
                        @if (origin() === 'library' && showPropagate() && !propagateIsNoop()) {
                            <div class="form-check name-card-check">
                                <input class="form-check-input" type="checkbox" id="team-rename-propagate"
                                       [ngModel]="propagate()" (ngModelChange)="propagate.set($event)">
                                <label class="form-check-label" for="team-rename-propagate">
                                    Use the new name for this event too
                                </label>
                            </div>
                        }
                    </section>

                    <!-- ── Club Team Library ──────────────────────────────────────── -->
                    @if (libraryName()) {
                        <section class="name-card" [class.is-active]="origin() === 'library'">
                            <header class="name-card-head">
                                <span class="name-card-eyebrow">
                                    <i class="bi bi-bookmarks-fill name-card-icon" aria-hidden="true"></i>
                                    Club Team Library
                                </span>
                            </header>

                            @if (origin() === 'library' && editable()) {
                                <label class="visually-hidden" for="team-rename-input">
                                    Team name in the Club Team Library
                                </label>
                                <input id="team-rename-input" type="text"
                                       class="form-control form-control-sm name-card-input"
                                       [ngModel]="draft()" (ngModelChange)="draft.set($event)"
                                       (keydown.enter)="submit()"
                                       [attr.maxlength]="maxLength()" autocomplete="off" autofocus>
                            } @else if (origin() === 'event' && propagateReplacesName()) {
                                <!-- Ticked: the struck-through name is the thing being given up.
                                     Showing only the new name would bury it. -->
                                <p class="name-card-pair">
                                    <span class="name-was">{{ libraryName() }}</span>
                                    <i class="bi bi-arrow-right name-arrow" aria-hidden="true"></i>
                                    <span class="name-now">{{ effectiveNewName() }}</span>
                                </p>
                            } @else {
                                <p class="name-card-value">{{ libraryName() }}</p>
                            }

                            <p class="name-card-note">
                                @if (audience() === 'rep') {
                                    Your saved list of teams — what you choose from when registering teams for
                                    any event under this club rep account.
                                } @else {
                                    The club's own saved list of teams, which their rep chooses from when
                                    registering for an event. It belongs to the club, not to this event.
                                }
                            </p>

                            <!-- Carry an event rename back into the library. -->
                            @if (origin() === 'event' && showPropagate() && !propagateIsNoop()) {
                                <div class="form-check name-card-check">
                                    <input class="form-check-input" type="checkbox" id="team-rename-propagate"
                                           [ngModel]="propagate()" (ngModelChange)="propagate.set($event)">
                                    <label class="form-check-label" for="team-rename-propagate">
                                        Rename it in my Club Team Library too
                                    </label>
                                </div>
                            }
                        </section>
                    }

                    <p class="rename-foot">
                        <i class="bi bi-info-circle rename-foot-icon" aria-hidden="true"></i>
                        <span>{{ footNote() }}</span>
                    </p>

                    <!-- The "Rename to New Team" pointer lived here. Removed 2026-08-18 with the
                         button itself (team-detail-panel) — a signpost to something a director can
                         no longer see is worse than no signpost. Restore both together. -->
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

        .rename-lede {
            margin: 0;
            font-size: var(--font-size-sm);
            line-height: 1.5;
            color: var(--brand-text);
        }

        /* The two named places a team's name can live. The one being edited is accented; the other
           is quiet context — so the model reads in a glance instead of having to be inferred from
           whichever button happened to open this dialog. */
        .name-card {
            display: flex;
            flex-direction: column;
            gap: var(--space-2);
            padding: var(--space-3);
            border: 1px solid var(--brand-border);
            border-left: 3px solid var(--brand-border);
            border-radius: var(--radius-md);
            background: var(--brand-bg-secondary);
        }
        .name-card.is-active {
            border-color: color-mix(in srgb, var(--bs-primary) 35%, var(--brand-border));
            border-left-color: var(--bs-primary);
            background: var(--brand-surface);
            box-shadow: var(--shadow-sm);
        }

        .name-card-head {
            display: flex;
            flex-wrap: wrap;
            align-items: baseline;
            gap: var(--space-2);
        }
        .name-card-eyebrow {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            font-size: var(--font-size-xs);
            font-weight: var(--font-weight-semibold);
            letter-spacing: 0.04em;
            text-transform: uppercase;
            color: var(--brand-text-muted);
        }
        .name-card.is-active .name-card-eyebrow { color: var(--bs-primary); }
        .name-card-icon { font-size: var(--font-size-sm); }
        .name-card-scope {
            font-size: var(--font-size-xs);
            color: var(--brand-text-muted);
        }

        .name-card-input { font-weight: var(--font-weight-semibold); }
        .name-card-input:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

        .name-card-value {
            margin: 0;
            font-weight: var(--font-weight-semibold);
            color: var(--brand-text);
            overflow-wrap: anywhere;
        }
        .name-card-value.is-empty {
            font-weight: 400;
            font-style: italic;
            color: var(--brand-text-muted);
        }

        .name-card-pair {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: var(--space-2);
            margin: 0;
            font-weight: var(--font-weight-semibold);
            overflow-wrap: anywhere;
        }
        .name-was { color: var(--brand-text-muted); text-decoration: line-through; }
        .name-arrow { color: var(--brand-text-muted); }
        .name-now { color: var(--brand-text); }

        .name-card-note {
            margin: 0;
            font-size: var(--font-size-xs);
            line-height: 1.5;
            color: var(--brand-text-muted);
        }

        .name-card-check { margin: var(--space-1) 0 0; }
        .name-card-check .form-check-label {
            font-size: var(--font-size-sm);
            color: var(--brand-text);
        }
        .form-check-input:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

        .rename-foot {
            display: flex;
            align-items: flex-start;
            gap: var(--space-2);
            margin: 0;
            font-size: var(--font-size-xs);
            line-height: 1.5;
            color: var(--brand-text-muted);
        }
        .rename-foot-icon { margin-top: 0.15em; }

        @media (prefers-reduced-motion: reduce) {
            .name-card { transition: none !important; }
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamRenameConfirmComponent {
    /** The event copy's current name (what the schedule shows today); '' when not registered here. */
    readonly currentName = input.required<string>();
    /** The proposed name — or, when `editable`, the seed for the in-dialog input. */
    readonly newName = input.required<string>();
    /** The Club Team Library entry's name; null/undefined for an orphan team. */
    readonly libraryName = input<string | null | undefined>(null);
    readonly audience = input<TeamRenameAudience>('admin');
    /** The dialog owns the name input (surfaces with no name field of their own). */
    readonly editable = input(false);
    /** Which side they opened from — decides which panel is accented and editable. */
    readonly origin = input<TeamRenameOrigin>('event');
    /** Event display name. Left at the sentinel by surfaces that don't know it. */
    readonly eventLabel = input(UNNAMED_EVENT);
    /** Is this team registered in the current event? Library origin has no event side without it. */
    readonly registeredHere = input(true);
    /**
     * May this event's name be written at all — the director's Allow Edit toggle. Renaming in the
     * Club Team Library is never gated by it (the list is the rep's own), but the opt-in "use it
     * for this event too" is an event write and is.
     */
    readonly canRenameInEvent = input(true);

    readonly confirmed = output<TeamRenameConfirmation>();
    readonly cancelled = output<void>();

    /** In-dialog draft, reseeded only when the `newName` input changes. */
    readonly draft = linkedSignal({ source: this.newName, computation: (v) => v });

    readonly effectiveNewName = computed(() => (this.editable() ? this.draft() : this.newName()).trim());

    /** The event's own name, or '' when the surface didn't supply one (admin doors). */
    readonly namedEvent = computed(() => {
        const label = this.eventLabel();
        return label === UNNAMED_EVENT ? '' : label;
    });

    /** Teams.TeamName is varchar(100); Clubs.ClubTeams.ClubTeamName is varchar(80). */
    readonly maxLength = computed(() => (this.origin() === 'library' || this.propagate() ? 80 : 100));

    /** The value the edited field started at — what "changed" is measured against. */
    readonly baselineName = computed(() =>
        (this.origin() === 'library' ? (this.libraryName() ?? '') : this.currentName()).trim());

    /** The name on the side they did NOT land on. */
    readonly otherName = computed(() =>
        (this.origin() === 'library' ? this.currentName() : (this.libraryName() ?? '')).trim());

    /**
     * The carry-across box exists only for a rep, and only when the other side actually exists: a
     * library entry to write (event origin) or a registered copy to write (library origin).
     */
    readonly showPropagate = computed(() => {
        if (this.audience() !== 'rep') return false;
        return this.origin() === 'library'
            // Writing this event's name needs the director's toggle; the library rename above it does not.
            ? this.canRenameInEvent() && this.registeredHere() && this.currentName().trim().length > 0
            : !!this.libraryName();
    });

    /** The other side already reads as the new name — offering to write it there says nothing. */
    readonly propagateIsNoop = computed(() => this.otherName() === this.effectiveNewName());

    /**
     * ALWAYS OFF to start. This was briefly defaulted on when the two names agreed, on the theory
     * that agreement meant "no one has deliberately set an event name, so they must mean both".
     * The data says otherwise: ~49% of club-linked rows already differ, and whole clubs differ
     * systematically (every event copy prefixed `REV `, every library entry prefixed by grad year)
     * without anyone ever having edited a thing. Agreement and divergence carry no evidence of
     * intent either way, so the dialog infers nothing — it shows both names and lets the rep choose.
     * Ticking writes their Club Team Library, the more consequential of the two writes; that is not
     * something to pre-select.
     *
     * Still a linkedSignal rather than a plain signal so it resets when a different team is opened.
     * `source` is inputs only, never the draft, so it cannot re-arm under the rep's hand mid-type.
     */
    readonly propagate = linkedSignal({
        source: () => ({ base: this.baselineName(), other: this.otherName(), show: this.showPropagate() }),
        computation: () => false,
    });

    /** Ticking would overwrite a genuinely different name over there — say so before they save. */
    readonly propagateReplacesName = computed(() =>
        this.propagate() && this.otherName().length > 0 && this.otherName() !== this.effectiveNewName());

    /** True when the tick will actually be sent (shown, meaningful, and on). */
    readonly propagateEffective = computed(() =>
        this.showPropagate() && !this.propagateIsNoop() && this.propagate());

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
        return this.propagateEffective();
    });

    readonly title = computed(() => {
        if (this.origin() === 'library') return 'Rename in Club Team Library';
        switch (this.mode()) {
            case 'orphan': return 'Rename Team';
            case 'reset': return 'Reset to Library Name';
            case 'this-event': return 'Rename for This Event';
        }
    });

    /** One sentence at the top saying which name they are about to change, and where it lives. */
    readonly lede = computed(() => {
        if (this.origin() === 'library') {
            return 'You are renaming this team in your Club Team Library — the list you pick from '
                + 'when registering teams for an event.';
        }
        switch (this.mode()) {
            case 'orphan':
                return 'This team is not linked to a Club Team Library entry, so the new name applies '
                    + 'to this event only.';
            case 'reset':
                return 'This puts the event back to the name the team carries in the Club Team Library.';
            default:
                return this.audience() === 'rep'
                    ? 'A team\'s name lives in two places. You are changing the one this event uses.'
                    : 'A team\'s name lives in two places — this event, and the club\'s own library. '
                        + 'You are changing the one this event uses.';
        }
    });

    /** The scope reassurance under both panels — the thing reps most need to be sure of. */
    readonly footNote = computed(() => {
        if (this.mode() === 'orphan') {
            return 'This team has no Club Team Library entry, so nothing outside this event is affected.';
        }
        if (this.origin() === 'library') {
            return this.propagateEffective()
                ? 'Every other event this team is registered for keeps the name it has now.'
                : 'No event changes. Every event this team is registered for keeps the name it has now.';
        }
        return this.audience() === 'rep'
            ? 'Every other event this team is registered for keeps the name it has now.'
            : 'Only this event changes. The club\'s library and every other event keep their own name.';
    });

    readonly confirmLabel = computed(() => {
        if (this.propagateEffective()) return 'Rename in Both';
        if (this.origin() === 'library') return 'Rename in Library';
        return this.mode() === 'reset' ? 'Reset Name' : 'Rename for This Event';
    });

    submit(): void {
        if (!this.canSubmit()) return;
        this.confirmed.emit({ name: this.effectiveNewName(), alsoPropagate: this.propagateEffective() });
    }
}
