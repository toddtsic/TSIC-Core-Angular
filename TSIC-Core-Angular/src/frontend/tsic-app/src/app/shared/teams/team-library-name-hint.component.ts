import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

/**
 * One-line status under a team-name field: says when this event's name differs from the club's
 * library name, and offers the reset. Renders nothing for an orphan team or when the names match,
 * so it can sit unconditionally under every name field that can rename a team (Search Teams, LADT,
 * Pairings, Schedule Hub). The reset itself is a this-event rename back to the library name — the
 * parent handles it through its normal save path.
 */
@Component({
    selector: 'team-library-name-hint',
    standalone: true,
    template: `
        @if (differs()) {
            <div class="lib-hint" role="note">
                <i class="bi bi-info-circle" aria-hidden="true"></i>
                <span>
                    Renamed for this event — library name is <strong>{{ libraryName() }}</strong>.
                </span>
                @if (canReset()) {
                    <button type="button" class="lib-hint-reset" (click)="reset.emit()">Reset</button>
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
