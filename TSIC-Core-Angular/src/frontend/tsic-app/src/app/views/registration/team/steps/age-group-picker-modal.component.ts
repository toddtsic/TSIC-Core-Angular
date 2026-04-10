import { ChangeDetectionStrategy, Component, Input, OnInit, output, signal, computed } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { AgeGroupDto } from '@core/api';

export interface AgeGroupSelection {
    ageGroupId: string;
    levelOfPlay: string;
}

/**
 * Age-group picker modal — full visual treatment with staggered pill animations.
 * Shows available age groups with fee, capacity, best-match highlight, and current selection.
 */
@Component({
    selector: 'app-age-group-picker-modal',
    standalone: true,
    imports: [CurrencyPipe, FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content picker-modal">

        <!-- Hero banner -->
        <div class="picker-hero">
          <div class="picker-hero-inner">
            <i class="bi bi-people-fill picker-hero-icon"></i>
            <h5 class="picker-team-name">{{ teamName }}</h5>
          </div>
          <p class="picker-tip">
            @if (currentAgeGroupId) {
              Tap to change age group
            } @else {
              Pick an age group below
            }
            @if (hasRecommended()) {
              <span class="picker-legend-inline"><i class="bi bi-star-fill"></i> = best match</span>
            }
          </p>
        </div>

        <!-- LOP selector (only when event defines LOP options) -->
        @if (lopOptions.length > 0) {
          <div class="lop-selector">
            <label class="lop-label">Level of Play</label>
            <div class="lop-pills">
              @for (lop of lopOptions; track lop) {
                <button type="button" class="lop-pill"
                        [class.active]="selectedLop() === lop"
                        (click)="selectedLop.set(lop)">
                  {{ lop }}
                </button>
              }
            </div>
          </div>
        }

        <!-- Age group pills -->
        <div class="picker-body">
          <div class="picker-pill-grid" role="radiogroup" [attr.aria-label]="'Age groups for ' + teamName">
            @for (ag of pills(); track ag.ageGroupId; let i = $index) {
              <div class="pill-flip-wrapper" [style.animation-delay]="(200 + (i * 60)) + 'ms'">
                <button type="button" class="picker-pill" role="radio"
                        [class.is-recommended]="ag.isRecommended"
                        [class.is-selected]="ag.ageGroupId === currentAgeGroupId"
                        [class.is-full]="ag.isFull"
                        [class.is-almost-full]="ag.isAlmostFull && !ag.isFull"
                        [class.is-flashing]="flashingId() === ag.ageGroupId"
                        [disabled]="lopRequired()"
                        [attr.aria-checked]="ag.ageGroupId === currentAgeGroupId"
                        [attr.aria-label]="ag.ageGroupName + ' — ' + (ag.fee | currency) + ' — ' + (ag.isFull ? 'Waitlist' : ag.spotsLeft + ' spots left')"
                        (click)="onPillClick(ag.ageGroupId)">
                  <!-- Card front (content) -->
                  <span class="pill-face pill-front">
                    <span class="pill-name">
                      {{ ag.ageGroupName }}
                      @if (ag.isRecommended) { <i class="bi bi-star-fill pill-star"></i> }
                      @if (ag.ageGroupId === currentAgeGroupId) { <i class="bi bi-check-circle-fill pill-current"></i> }
                    </span>
                    <span class="pill-fee">{{ ag.fee | currency }}</span>
                    <span class="pill-spots" [class.text-warning]="ag.isAlmostFull && !ag.isFull" [class.text-danger]="ag.isFull">
                      @if (ag.isFull) { <i class="bi bi-exclamation-circle me-1"></i>Waitlist }
                      @else { {{ ag.spotsLeft }} {{ ag.spotsLeft === 1 ? 'spot' : 'spots' }} }
                    </span>
                  </span>
                  <!-- Card back (blank) -->
                  <span class="pill-face pill-back">
                    <i class="bi bi-people-fill"></i>
                  </span>
                </button>
              </div>
            }
          </div>
        </div>

      </div>
    </tsic-dialog>
  `,
    styles: [`
      /* ── Hero Banner ── */
      .picker-hero {
        padding: var(--space-4) var(--space-4) var(--space-3);
        background: linear-gradient(135deg, rgba(var(--bs-primary-rgb), 0.08) 0%, rgba(var(--bs-primary-rgb), 0.02) 100%);
        border-bottom: 2px solid rgba(var(--bs-primary-rgb), 0.12);
        text-align: center;
        animation: heroSlideIn 0.2s ease-out 80ms backwards;
      }

      .picker-hero-inner {
        display: flex;
        align-items: center;
        justify-content: center;
        gap: var(--space-2);
        flex-wrap: wrap;
      }

      .picker-hero-icon {
        font-size: var(--font-size-xl);
        color: var(--bs-primary);
      }

      .picker-team-name {
        margin: 0;
        font-size: var(--font-size-xl);
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .picker-lop-badge {
        display: inline-flex;
        align-items: center;
        padding: 2px var(--space-2);
        border-radius: var(--radius-full);
        background: rgba(var(--bs-primary-rgb), 0.1);
        color: var(--bs-primary);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        letter-spacing: 0.02em;
      }

      .picker-tip {
        margin: var(--space-2) 0 0;
        font-size: var(--font-size-sm);
        color: var(--brand-text-muted);
        animation: tipFadeIn 0.15s ease-out 150ms backwards;
      }

      .picker-legend-inline {
        display: inline-flex;
        align-items: center;
        gap: 3px;
        margin-left: var(--space-2);
        font-size: var(--font-size-xs);
        color: var(--neutral-400);

        i { font-size: 8px; color: var(--bs-primary); }
      }

      /* ── LOP Selector ── */
      .lop-selector {
        padding: var(--space-3) var(--space-4) 0;
        background: var(--bs-body-bg);
      }

      .lop-label {
        display: block;
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text-muted);
        margin-bottom: var(--space-2);
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }

      .lop-pills {
        display: flex;
        flex-wrap: wrap;
        gap: var(--space-1);
      }

      .lop-pill {
        padding: var(--space-1) var(--space-3);
        border: 1.5px solid var(--border-color);
        border-radius: var(--radius-full);
        background: var(--brand-surface);
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        cursor: pointer;
        transition: all 0.12s ease;
        min-width: 36px;
        text-align: center;

        &:hover { border-color: var(--bs-primary); }

        &.active {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.1);
          color: var(--bs-primary);
          font-weight: var(--font-weight-semibold);
        }
      }

      /* ── Pill Grid ── */
      .picker-body {
        padding: var(--space-4);
        background: var(--bs-body-bg);
        border-radius: 0 0 var(--radius-md) var(--radius-md);
      }

      .picker-pill-grid {
        display: flex;
        flex-wrap: wrap;
        justify-content: center;
        gap: var(--space-3);
        perspective: 800px;
      }

      /* Flip wrapper — handles the 3D card-flip entrance */
      .pill-flip-wrapper {
        animation: pillFlipIn 0.5s ease-out backwards;
        transform-style: preserve-3d;
      }

      .picker-pill {
        position: relative;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        border-radius: var(--radius-lg);
        border: 2px solid var(--border-color);
        background: var(--brand-surface);
        cursor: pointer;
        min-width: 110px;
        min-height: 60px;
        box-shadow: var(--shadow-xs);
        transition: border-color 0.15s ease, background 0.15s ease, box-shadow 0.15s ease, transform 0.15s ease;
        transform-style: preserve-3d;

        &:hover:not(:disabled) {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.06);
          box-shadow: var(--shadow-sm);
          transform: translateY(-2px);
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }

        &:active:not(:disabled) {
          transform: scale(0.97);
        }

        &.is-recommended {
          /* Star icon is the indicator — no border/bg change to avoid looking pre-selected */
        }

        &.is-selected {
          border-color: var(--bs-success);
          background: rgba(var(--bs-success-rgb), 0.08);
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

        &.is-almost-full .pill-spots {
          font-weight: var(--font-weight-semibold);
        }

        &.is-flashing {
          animation: pillFlash 0.25s ease-out;
        }

        &:disabled { cursor: default; }
      }

      /* Card faces */
      .pill-face {
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 3px;
        padding: var(--space-2) var(--space-3);
        backface-visibility: hidden;
      }

      .pill-front {
        /* Normal flow — visible after flip */
      }

      .pill-back {
        position: absolute;
        inset: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        border-radius: var(--radius-lg);
        background: linear-gradient(135deg, var(--bs-primary), rgba(var(--bs-primary-rgb), 0.7));
        color: rgba(255, 255, 255, 0.4);
        font-size: var(--font-size-2xl);
        transform: rotateY(180deg);
        backface-visibility: hidden;
      }

      .pill-name {
        font-weight: var(--font-weight-bold);
        font-size: var(--font-size-sm);
        color: var(--brand-text);
        display: flex;
        align-items: center;
        gap: var(--space-1);
      }

      .pill-star {
        font-size: 9px;
        color: var(--bs-primary);
      }

      .pill-current {
        font-size: 11px;
        color: var(--bs-success);
      }

      .pill-fee {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-primary);
      }

      .pill-spots {
        font-size: 10px;
        color: var(--brand-text-muted);
        white-space: nowrap;
      }

      /* ── Keyframes ── */
      @keyframes heroSlideIn {
        from { opacity: 0; transform: translateY(-12px); }
        to   { opacity: 1; transform: translateY(0); }
      }

      @keyframes tipFadeIn {
        from { opacity: 0; }
        to   { opacity: 1; }
      }

      @keyframes pillFlipIn {
        0%   { opacity: 0; transform: rotateY(180deg) scale(0.8); }
        40%  { opacity: 1; }
        100% { transform: rotateY(0deg) scale(1); }
      }

      @keyframes pillFlash {
        0%   { transform: scale(1); box-shadow: var(--shadow-xs); }
        50%  { transform: scale(1.06); box-shadow: 0 0 0 4px rgba(var(--bs-primary-rgb), 0.25), var(--shadow-md); }
        100% { transform: scale(1); box-shadow: var(--shadow-xs); }
      }

      /* ── Mobile ── */
      @media (max-width: 575.98px) {
        .pill-flip-wrapper {
          flex: 1 1 calc(50% - var(--space-3));
        }
        .picker-pill {
          min-width: unset;
          width: 100%;
          min-height: 56px;
        }
      }

      /* ── Reduced Motion ── */
      @media (prefers-reduced-motion: reduce) {
        .picker-hero, .picker-tip, .pill-flip-wrapper { animation: none; }
        .picker-pill { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgeGroupPickerModalComponent implements OnInit {
    @Input() teamName = '';
    @Input() gradYear = '';
    @Input() levelOfPlay = '';
    @Input() currentAgeGroupId = '';
    @Input() ageGroups: AgeGroupDto[] = [];
    @Input() lopOptions: string[] = [];

    readonly selected = output<AgeGroupSelection>();
    readonly closed = output<void>();
    readonly flashingId = signal<string | null>(null);
    readonly selectedLop = signal('');

    /** True when LOP options exist but none selected — blocks age group pills */
    lopRequired(): boolean {
        return this.lopOptions.length > 0 && !this.selectedLop();
    }

    readonly pills = computed(() => {
        const recommended = this.bestMatch();
        return this.ageGroups.map(ag => {
            const spotsLeft = Math.max(0, ag.maxTeams - ag.registeredCount);
            return {
                ageGroupId: ag.ageGroupId,
                ageGroupName: ag.ageGroupName,
                fee: (ag.deposit || 0) + (ag.balanceDue || 0),
                spotsLeft,
                isFull: spotsLeft === 0,
                isAlmostFull: spotsLeft > 0 && spotsLeft <= 2,
                isRecommended: ag.ageGroupId === recommended,
            };
        });
    });

    readonly hasRecommended = computed(() => this.pills().some(p => p.isRecommended));

    ngOnInit(): void {
        // Default LOP to current team's existing LOP only — do NOT auto-select first option.
        // If no LOP set, user must explicitly choose before selecting an age group.
        this.selectedLop.set(this.levelOfPlay || '');
    }

    onPillClick(ageGroupId: string): void {
        // Block if LOP options exist but none selected
        if (this.lopOptions.length > 0 && !this.selectedLop()) {
            return;
        }

        // Same age group and same LOP — just close
        if (ageGroupId === this.currentAgeGroupId && this.selectedLop() === this.levelOfPlay) {
            this.closed.emit();
            return;
        }

        // Flash the pill, then emit after animation
        this.flashingId.set(ageGroupId);
        setTimeout(() => {
            this.selected.emit({ ageGroupId, levelOfPlay: this.selectedLop() });
        }, 250);
    }

    private bestMatch(): string {
        if (!this.gradYear || !this.ageGroups.length) return '';
        const exact = this.ageGroups.find(ag => ag.ageGroupName === this.gradYear);
        if (exact) return exact.ageGroupId;
        const contains = this.ageGroups.find(ag => ag.ageGroupName.includes(this.gradYear));
        if (contains) return contains.ageGroupId;
        return '';
    }
}
