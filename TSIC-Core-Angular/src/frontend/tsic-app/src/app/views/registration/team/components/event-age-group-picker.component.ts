import { ChangeDetectionStrategy, Component, computed, input, model } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import type { AgeGroupDto } from '@core/api';
import { buildAgeGroupSlots } from './event-age-group.util';

/**
 * Event age-group picker for team registration — the grad-year-aware grid of
 * age groups a rep registers a team into. Two presentations:
 *
 *   - `variant="pill"` — fee-bearing card grid used by the add-and-register
 *     first-team form: star on the recommended match, fee, spots.
 *   - `variant="chip"` — compact inline chip row used by the library fly-in's
 *     expand-in-place register row: asterisk on a literal grad-year match,
 *     "Waitlist" / "N left" meta.
 *
 * Both variants share ONE source of slot/recommendation logic
 * ([event-age-group.util.ts](./event-age-group.util.ts)) so the WAITLIST-twin
 * default + recommended highlight can never diverge between the two call sites
 * again. Selection is a `model` — the parent owns pre-fill (it imports
 * `resolveRecommendedAgeGroupId` to seed the recommended slot) and the
 * submit/create flow; this is purely an inline picker, not a dialog.
 */
@Component({
    selector: 'app-event-age-group-picker',
    standalone: true,
    imports: [CurrencyPipe],
    template: `
    @if (variant() === 'pill') {
      <div class="eagp-pill-grid" role="radiogroup">
        @for (slot of slots(); track slot.ageGroupId; let i = $index) {
          <button type="button" class="age-pill" role="radio"
                  [class.is-recommended]="slot.isRecommended"
                  [class.is-selected]="selected() === slot.ageGroupId"
                  [class.is-full]="slot.isFull"
                  [class.is-almost-full]="slot.isAlmostFull && !slot.isFull"
                  [disabled]="disabled()"
                  [attr.aria-checked]="selected() === slot.ageGroupId"
                  [style.animation-delay]="disabled() ? '0ms' : (60 + (i * 40)) + 'ms'"
                  (click)="selected.set(slot.ageGroupId)">
            <span class="age-pill-name">
              {{ slot.ageGroupName }}
              @if (slot.isRecommended) { <i class="bi bi-star-fill age-pill-star"></i> }
              @if (selected() === slot.ageGroupId) { <i class="bi bi-check-circle-fill age-pill-check"></i> }
            </span>
            <span class="age-pill-fee">
              @if (slot.isFull) { Free }
              @else { {{ slot.fee | currency }} }
            </span>
            <span class="age-pill-spots"
                  [class.text-warning]="slot.isAlmostFull && !slot.isFull"
                  [class.text-danger]="slot.isFull">
              @if (slot.isFull) { <i class="bi bi-exclamation-circle me-1"></i>Waitlist }
              @else { {{ slot.spotsLeft }} {{ slot.spotsLeft === 1 ? 'spot' : 'spots' }} }
            </span>
          </button>
        }
      </div>
    } @else {
      <div class="eagp-chip-row" role="radiogroup" aria-label="Age group">
        @for (slot of slots(); track slot.ageGroupId) {
          <button type="button" class="ag-chip" role="radio"
                  [class.is-recommended]="slot.isRecommended"
                  [class.is-selected]="selected() === slot.ageGroupId"
                  [class.is-full]="slot.isFull"
                  [class.is-almost-full]="slot.isAlmostFull"
                  [disabled]="disabled()"
                  [attr.aria-checked]="selected() === slot.ageGroupId"
                  [title]="slot.isFull ? 'Age group is full — registering will waitlist this team' : null"
                  (click)="selected.set(slot.ageGroupId)">
            <span class="ag-chip-name">{{ slot.ageGroupName }}@if (slot.matchesGradYear) {<span class="ag-chip-gradyear-match" title="Matches this team's grad year" aria-label="Matches this team's grad year">*</span>}</span>
            <span class="ag-chip-meta">
              @if (slot.isFull) { Waitlist }
              @else if (slot.isAlmostFull) { {{ slot.spotsLeft }} left }
            </span>
          </button>
        }
      </div>
    }
  `,
    styles: [`
      /* ── Pill variant (add-and-register first-team form) ───────────── */
      .eagp-pill-grid {
        display: flex;
        flex-wrap: wrap;
        justify-content: center;
        gap: var(--space-2);
      }

      .age-pill {
        position: relative;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 2px;
        min-width: 108px;
        min-height: 60px;
        padding: var(--space-2) var(--space-3);
        border: 2px solid var(--border-color);
        border-radius: var(--radius-md);
        background: var(--brand-surface);
        cursor: pointer;
        box-shadow: var(--shadow-xs);
        transition: border-color 0.15s ease, background 0.15s ease,
                    box-shadow 0.15s ease, transform 0.15s ease;
        animation: eagpAgePillFadeIn 0.3s ease-out backwards;

        &:hover:not(:disabled) {
          border-color: var(--bs-success);
          background: rgba(var(--bs-success-rgb), 0.06);
          transform: translateY(-2px);
          box-shadow: var(--shadow-sm);
        }

        &:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
        &:active:not(:disabled) { transform: scale(0.97); }

        &.is-selected {
          border-color: var(--bs-success);
          background: rgba(var(--bs-success-rgb), 0.1);
          box-shadow: 0 0 0 2px rgba(var(--bs-success-rgb), 0.2), var(--shadow-xs);
        }

        &.is-full {
          border-color: rgba(var(--bs-warning-rgb), 0.4);
          background: rgba(var(--bs-warning-rgb), 0.04);
        }

        &:hover:not(:disabled).is-full {
          border-color: var(--bs-warning);
          background: rgba(var(--bs-warning-rgb), 0.1);
        }

        &.is-almost-full .age-pill-spots {
          font-weight: var(--font-weight-semibold);
        }

        &:disabled {
          opacity: 0.42;
          cursor: not-allowed;
          animation: none;
        }
      }

      .age-pill-name {
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        display: inline-flex;
        align-items: center;
        gap: 4px;
      }

      .age-pill-star { font-size: 9px; color: var(--bs-danger); }
      .age-pill-check { font-size: 11px; color: var(--bs-success); }

      .age-pill-fee {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
      }

      .age-pill-spots {
        font-size: 10px;
        color: var(--brand-text-muted);
        white-space: nowrap;
      }

      @keyframes eagpAgePillFadeIn {
        from { opacity: 0; transform: translateY(6px); }
        to   { opacity: 1; transform: translateY(0); }
      }

      /* ── Chip variant (library fly-in expand row) ──────────────────── */
      .eagp-chip-row {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .ag-chip {
        display: inline-flex;
        flex-direction: column;
        align-items: center;
        gap: 2px;
        min-width: 64px;
        padding: 6px var(--space-2);
        border: 1.5px solid color-mix(in srgb, var(--bs-primary) 35%, transparent);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        cursor: pointer;
        transition: background-color 0.12s ease, border-color 0.12s ease, transform 0.12s ease;
      }

      .ag-chip:hover:not(:disabled) {
        background: var(--bs-primary);
        color: var(--neutral-0);
        border-color: var(--bs-primary);
        transform: translateY(-1px);
      }

      .ag-chip:focus-visible { outline: none; box-shadow: var(--shadow-focus); }
      .ag-chip:disabled { opacity: 0.4; cursor: default; transform: none; }

      .ag-chip.is-recommended {
        border-color: var(--bs-success);
        box-shadow: 0 0 0 2px color-mix(in srgb, var(--bs-success) 20%, transparent);
      }

      .ag-chip.is-almost-full { border-color: var(--bs-warning); color: var(--bs-warning); }

      /* Full = waitlist path. Keep clickable; visually distinct from open AGs
         via dashed border + amber accent + uppercase meta label. */
      .ag-chip.is-full {
        border-style: dashed;
        border-color: var(--bs-warning);
        color: var(--bs-warning);
        background: color-mix(in srgb, var(--bs-warning) 6%, transparent);
      }
      .ag-chip.is-full:hover:not(:disabled) {
        background: var(--bs-warning);
        color: var(--neutral-0);
        border-color: var(--bs-warning);
        border-style: solid;
      }
      .ag-chip.is-full .ag-chip-meta {
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }

      /* Selected = the chip the rep has chosen (commits on Submit, not on click).
         Filled primary so it wins over the recommended/almost-full/full accents.
         Placed last so its rules take precedence over the states above. */
      .ag-chip.is-selected,
      .ag-chip.is-selected:hover:not(:disabled) {
        background: var(--bs-primary);
        color: var(--neutral-0);
        border-color: var(--bs-primary);
        border-style: solid;
      }

      .ag-chip-name { font-size: var(--font-size-xs); }
      /* Asterisk flag — age group name literally matches the team's library grad
         year. Red + bold so it reads as the "this is the matching one" cue. */
      .ag-chip-gradyear-match {
        color: var(--bs-danger);
        font-weight: var(--font-weight-bold);
        margin-left: 1px;
      }
      .ag-chip-meta { font-size: 10px; font-weight: var(--font-weight-medium); opacity: 0.85; }

      @media (prefers-reduced-motion: reduce) {
        .age-pill { animation: none !important; transition: none; }
        .age-pill:hover:not(:disabled) { transform: none; }
        .ag-chip { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EventAgeGroupPickerComponent {
    /** Event age groups to render. */
    readonly ageGroups = input<readonly AgeGroupDto[]>([]);
    /** Team grad year — drives the recommended (asterisked/starred) slot. */
    readonly gradYear = input<string | null | undefined>('');
    /** Selected age-group id (two-way). */
    readonly selected = model<string>('');
    /** Disable all slots (e.g. prerequisites unmet, or an action in flight). */
    readonly disabled = input(false);
    /** Presentation: fee-bearing cards ('pill') or compact chips ('chip'). */
    readonly variant = input<'pill' | 'chip'>('chip');

    readonly slots = computed(() => buildAgeGroupSlots(this.ageGroups(), this.gradYear()));
}
