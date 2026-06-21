import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { LOP_CHOICES } from '@shared/teams/lop-choices';

/**
 * Level-of-Play picker — the canonical fixed 1–5 pill row, sourced from
 * `LOP_CHOICES` (never a job's freeform `List_Lops`). Shared by the team
 * registration surfaces that let a rep set a team's level of play: the library
 * fly-in's inline register row and the add-and-register first-team form.
 *
 * The stored value is always the bare digit ('1'..'5'). `fill` stretches pills
 * to fill their row (the form's split layout); the default hugs content (the
 * fly-in's inline row). This is an inline control, not a dialog.
 */
@Component({
    selector: 'app-level-of-play-picker',
    standalone: true,
    template: `
    <div class="lop-pills" role="radiogroup" aria-label="Level of play">
      @for (c of choices; track c.value) {
        <button type="button" class="lop-pill" role="radio"
                [class.lop-pill--fill]="fill()"
                [class.active]="selected() === c.value"
                [class.is-invalid]="invalid() && selected() !== c.value"
                [disabled]="disabled()"
                [attr.aria-checked]="selected() === c.value"
                [attr.title]="c.label"
                (click)="selected.set(c.value)">
          {{ labels() === 'full' ? c.label : c.short }}
        </button>
      }
    </div>
  `,
    styles: [`
      .lop-pills {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .lop-pill {
        flex: 0 1 auto;
        min-width: 44px;
        padding: 4px var(--space-2);
        border: 1.5px solid var(--border-color);
        border-radius: var(--radius-full);
        background: var(--brand-surface);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        cursor: pointer;
        transition: all 0.12s ease;
        text-align: center;
      }

      .lop-pill:hover:not(:disabled) { border-color: var(--bs-primary); }

      .lop-pill.active {
        border-color: var(--bs-primary);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        font-weight: var(--font-weight-semibold);
      }

      .lop-pill.is-invalid:not(.active) {
        border-color: rgba(var(--bs-danger-rgb), 0.5);
      }

      .lop-pill:disabled { opacity: 0.5; cursor: default; }
      .lop-pill:focus-visible { outline: none; box-shadow: var(--shadow-focus); }

      /* Fill variant — stretch pills evenly across the row (form split layouts,
         and the team-form modal's full-label row). */
      .lop-pill--fill {
        flex: 1 1 0;
        min-width: 36px;
        padding: var(--space-1) var(--space-2);
      }

      @media (prefers-reduced-motion: reduce) {
        .lop-pill { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LevelOfPlayPickerComponent {
    /** Selected LOP value — bare digit '1'..'5' (two-way). */
    readonly selected = model<string>('');
    /** Disable all pills (e.g. a prerequisite step isn't done yet). */
    readonly disabled = input(false);
    /** Stretch pills to fill the row (form split layout) vs hug content (inline row). */
    readonly fill = input(false);
    /** Paint an unselected-required error state (submitted with no pick). */
    readonly invalid = input(false);
    /** Pill text: bare digit ('short', compact pickers) or friendly label ('full', roomier forms). */
    readonly labels = input<'short' | 'full'>('short');

    readonly choices = LOP_CHOICES;
}
