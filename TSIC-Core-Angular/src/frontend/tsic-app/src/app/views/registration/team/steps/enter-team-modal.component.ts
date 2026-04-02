import { ChangeDetectionStrategy, Component, Input, output, signal, computed } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { AgeGroupDto } from '@core/api';

/**
 * Age-group picker modal.
 * Shows available age groups with fee, capacity, and best-match highlight.
 * Emits the selected ageGroupId and closes.
 */
@Component({
    selector: 'app-age-group-picker-modal',
    standalone: true,
    imports: [CurrencyPipe, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="sm" (requestClose)="closed.emit()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">
            <i class="bi bi-diagram-3 me-2"></i>Choose Age Group
          </h5>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>
        <div class="modal-body p-0">
          <div class="team-banner">
            Assigning <strong>{{ teamName }}</strong>
          </div>
          <div class="ag-list" role="radiogroup" [attr.aria-label]="'Age groups for ' + teamName">
            @for (ag of pills(); track ag.ageGroupId) {
              <button type="button" class="ag-row" role="radio"
                      [attr.aria-checked]="false"
                      [attr.aria-label]="ag.ageGroupName + ' — ' + (ag.fee | currency) + ' — ' + (ag.isFull ? 'Waitlist' : ag.spotsLeft + ' spots left')"
                      [class.is-recommended]="ag.isRecommended"
                      [class.is-almost-full]="ag.isAlmostFull && !ag.isFull"
                      [class.is-full]="ag.isFull"
                      (click)="selected.emit(ag.ageGroupId)">
                <span class="ag-name">
                  {{ ag.ageGroupName }}
                  @if (ag.isRecommended) {
                    <i class="bi bi-star-fill ag-star"></i>
                  }
                </span>
                <span class="ag-fee">{{ ag.fee | currency }}</span>
                <span class="ag-spots" [class.text-warning]="ag.isAlmostFull" [class.text-danger]="ag.isFull">
                  @if (ag.isFull) {
                    <i class="bi bi-exclamation-circle me-1"></i>Waitlist
                  } @else {
                    {{ ag.spotsLeft }} {{ ag.spotsLeft === 1 ? 'spot' : 'spots' }}
                  }
                </span>
              </button>
            }
          </div>
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
      .team-banner {
        padding: var(--space-2) var(--space-3);
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
        background: rgba(var(--bs-primary-rgb), 0.03);
        border-bottom: 1px solid var(--border-color);
      }

      .ag-list {
        max-height: 340px;
        overflow-y: auto;
      }

      .ag-row {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        width: 100%;
        padding: var(--space-2) var(--space-3);
        border: none;
        border-bottom: 1px solid rgba(var(--bs-dark-rgb), 0.04);
        background: transparent;
        cursor: pointer;
        font-size: var(--font-size-xs);
        text-align: left;
        transition: background-color 0.1s ease;

        &:last-child { border-bottom: none; }
        &:hover { background: rgba(var(--bs-primary-rgb), 0.05); }
        &:focus-visible {
          outline: none;
          background: rgba(var(--bs-primary-rgb), 0.08);
          box-shadow: inset 0 0 0 2px rgba(var(--bs-primary-rgb), 0.2);
        }

        &.is-recommended {
          background: rgba(var(--bs-primary-rgb), 0.04);
        }

        &.is-full { opacity: 0.6; }
      }

      .ag-name {
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
        display: flex;
        align-items: center;
        gap: var(--space-1);
      }

      .ag-star {
        font-size: 9px;
        color: var(--bs-primary);
      }

      .ag-fee {
        margin-left: auto;
        font-weight: var(--font-weight-medium);
        color: var(--brand-text);
        white-space: nowrap;
      }

      .ag-spots {
        font-size: 10px;
        color: var(--brand-text-muted);
        white-space: nowrap;
        min-width: 60px;
        text-align: right;
      }

      .ag-row.is-almost-full .ag-spots {
        font-weight: var(--font-weight-semibold);
      }

      @media (prefers-reduced-motion: reduce) {
        .ag-row { transition: none; }
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AgeGroupPickerModalComponent {
    @Input() teamName = '';
    @Input() gradYear = '';
    @Input() ageGroups: AgeGroupDto[] = [];

    readonly selected = output<string>();
    readonly closed = output<void>();

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

    private bestMatch(): string {
        if (!this.gradYear || !this.ageGroups.length) return '';
        const exact = this.ageGroups.find(ag => ag.ageGroupName === this.gradYear);
        if (exact) return exact.ageGroupId;
        const contains = this.ageGroups.find(ag => ag.ageGroupName.includes(this.gradYear));
        if (contains) return contains.ageGroupId;
        return '';
    }
}
