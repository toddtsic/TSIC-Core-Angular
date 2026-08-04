import { Component, ChangeDetectionStrategy, inject } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

/**
 * "Back to Scheduling Checklist" chip for the standalone scheduling tools
 * (view-schedule, master-schedule, rescheduler, ...). Renders only when the
 * checklist deep-linked here (?from=scheduling), so every other entry path is
 * visually unchanged. Shell children don't need this — the scheduling shell
 * renders its own back bar.
 */
@Component({
    selector: 'app-checklist-back-link',
    standalone: true,
    imports: [RouterLink],
    template: `
        @if (visible) {
            <a class="checklist-back" routerLink="../scheduling">
                <i class="bi bi-arrow-left"></i>
                Scheduling Checklist
            </a>
        }
    `,
    styles: `
        .checklist-back {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            margin: var(--space-2) 0;
            font-size: 0.85rem;
            font-weight: 500;
            color: var(--brand-text-muted);
            text-decoration: none;
        }

        .checklist-back:hover {
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

    readonly visible = this.route.snapshot.queryParamMap.get('from') === 'scheduling';
}
