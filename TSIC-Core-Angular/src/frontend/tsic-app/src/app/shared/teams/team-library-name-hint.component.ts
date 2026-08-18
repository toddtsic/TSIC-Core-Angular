import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * One-line status under an ADMIN team-name field: shows the club's library name when this event's
 * name differs from it, and offers to adopt it. Renders nothing for an orphan team or when the names
 * match, so it can sit unconditionally under every admin name field (Search Teams, LADT, Pairings,
 * Schedule Hub). Deliberately neutral — legacy data has event names that never matched the library
 * (about half of all club-linked teams), so it must not claim "renamed for this event". Adopting the
 * library name is a this-event rename — the parent handles it through its normal save path.
 */
@Component({
    selector: 'team-library-name-hint',
    standalone: true,
    template: `
        @if (differs()) {
            <div class="lib-hint" role="note">
                <i class="bi bi-info-circle" aria-hidden="true"></i>
                <span>
                    Library name: <strong>{{ libraryName() }}</strong>
                </span>
                @if (canReset()) {
                    <button type="button" class="lib-hint-reset" (click)="reset.emit()">Use library name</button>
                }
            </div>
        }
    `,
    styles: [`
        .lib-hint {
            display: flex;
            align-items: center;
            flex-wrap: wrap;
            gap: var(--space-2);
            margin-top: var(--space-1);
            font-size: var(--font-size-xs);
            color: var(--brand-text-muted);

            i { color: var(--bs-info); }
            strong { color: var(--brand-text); }
        }
        .lib-hint-reset {
            border: none;
            background: transparent;
            padding: 0;
            font-size: inherit;
            font-weight: var(--font-weight-semibold);
            color: var(--bs-primary);
            text-decoration: underline;
            cursor: pointer;
        }
        .lib-hint-reset:focus-visible { outline: none; box-shadow: var(--shadow-focus); border-radius: var(--radius-sm); }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamLibraryNameHintComponent {
    /** The event copy's name as currently saved (not the draft). */
    readonly teamName = input.required<string | null | undefined>();
    /** The library name; null/undefined for an orphan team. */
    readonly libraryName = input<string | null | undefined>(null);
    readonly canReset = input(true);

    readonly reset = output<void>();

    readonly differs = computed(() => {
        const lib = this.libraryName();
        return !!lib && (this.teamName() ?? '') !== lib;
    });
}
