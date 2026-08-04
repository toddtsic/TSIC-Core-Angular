import { Component, ChangeDetectionStrategy, booleanAttribute, computed, inject, input } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

/**
 * Compact "back to Scheduling Checklist" arrow. Drop it in as the first child of a
 * page's `<h2 class="page-title">` — that element is already a flex row, so the arrow
 * rides the title line and costs no vertical space.
 *
 * Visibility: pages under the scheduling shell pass `always` (the checklist is their
 * only door). Everything else shows it only when the checklist deep-linked here
 * (?from=scheduling), leaving every other entry path untouched.
 *
 * `routePath` is the embed guard: the hub renders Pairings/Fields/Timeslots/QA/Master
 * as tool panels inside itself, and those copies must not sprout a back arrow. Passing
 * the host's own route path keeps the link to the genuinely-routed instance.
 */
@Component({
    selector: 'app-checklist-back-link',
    standalone: true,
    imports: [RouterLink],
    template: `
        @if (visible()) {
            <a class="checklist-back" [routerLink]="target()"
               title="Back to Scheduling Checklist"
               aria-label="Back to Scheduling Checklist">
                <i class="bi bi-arrow-left" aria-hidden="true"></i>
            </a>
        }
    `,
    styles: `
        :host {
            display: contents;
        }

        .checklist-back {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            flex-shrink: 0;
            width: 1.75rem;
            height: 1.75rem;
            border: 1px solid var(--bs-border-color);
            border-radius: var(--radius-md);
            background: var(--brand-surface);
            color: var(--brand-text-muted);
            font-size: var(--font-size-sm);
            line-height: 1;
            text-decoration: none;
        }

        .checklist-back:hover {
            border-color: var(--bs-primary);
            color: var(--bs-primary);
        }

        .checklist-back:focus-visible {
            outline: none;
            box-shadow: var(--shadow-focus);
        }
    `,
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ChecklistBackLinkComponent {
    private readonly route = inject(ActivatedRoute);

    /** Relative route back to the checklist. Shell children use '..'; deeper hosts override. */
    readonly target = input('../scheduling');

    /** Pages reachable only from the checklist always show the way back. */
    readonly always = input(false, { transform: booleanAttribute });

    /** Host's own route path — set on components the hub also embeds. */
    readonly routePath = input<string | null>(null);

    private readonly cameFromChecklist =
        this.route.snapshot.queryParamMap.get('from') === 'scheduling';

    /** False when this instance is an embedded copy rather than the routed page. */
    private readonly isRoutedHere = computed(() => {
        const expected = this.routePath();
        return expected === null || this.route.routeConfig?.path === expected;
    });

    readonly visible = computed(() =>
        this.isRoutedHere() && (this.always() || this.cameFromChecklist));
}
