import { ChangeDetectionStrategy, Component, Input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { SuggestedEventDto } from '@core/api';

/**
 * Modal that surfaces "Looking for a new event?" suggestions on the role-select
 * screen. Opened by the pivot link above the role list. Closed via X / esc /
 * backdrop. "Go to event" navigates away — route change destroys the modal.
 */
@Component({
    selector: 'app-suggested-events-modal',
    standalone: true,
    imports: [RouterLink, TsicDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="closed.emit()">
      <div class="modal-content">

        <div class="modal-header">
          <h3 class="modal-title">
            <i class="bi bi-calendar2-event" aria-hidden="true"></i>
            Looking for a new event?
          </h3>
          <button type="button" class="btn-close" (click)="closed.emit()" aria-label="Close"></button>
        </div>

        <div class="modal-body">
          <p class="suggested-modal-lede">Based on events you've registered for before.</p>

          <ul class="suggested-events-list">
            @for (event of events; track event.jobId) {
            <li class="suggested-event">
              @if (event.jobLogo) {
              <img class="suggested-event-logo" [src]="event.jobLogo" alt="" aria-hidden="true" />
              } @else {
              <div class="suggested-event-logo-placeholder" aria-hidden="true">
                <i class="bi bi-image"></i>
              </div>
              }

              <div class="suggested-event-body">
                <div class="suggested-event-name">{{ event.jobName }}</div>
                <div class="suggested-event-customer">{{ event.customerName }}</div>
                <div class="suggested-event-badges">
                  @if (event.playerRegistrationOpen) {
                  <span class="badge bg-primary-subtle text-primary-emphasis">Player registration open</span>
                  }
                  @if (event.storeOpen) {
                  <span class="badge bg-info-subtle text-info-emphasis">Store live</span>
                  }
                  @if (event.schedulePublished) {
                  <span class="badge bg-success-subtle text-success-emphasis">Schedules published</span>
                  }
                </div>
              </div>

              <a class="btn btn-primary btn-sm suggested-event-cta"
                [routerLink]="['/', event.jobPath]"
                [attr.aria-label]="'Go to ' + event.jobName">
                Go to event
                <i class="bi bi-arrow-right ms-1" aria-hidden="true"></i>
              </a>
            </li>
            }
          </ul>
        </div>

      </div>
    </tsic-dialog>
  `,
    styles: [`
        .suggested-modal-lede {
            margin: 0 0 var(--space-4);
            color: var(--brand-text-muted);
            font-size: var(--font-size-sm);
            text-align: center;
        }

        .suggested-events-list {
            list-style: none;
            margin: 0;
            padding: 0;
            display: flex;
            flex-direction: column;
            gap: var(--space-3);
        }

        .suggested-event {
            display: flex;
            align-items: center;
            gap: var(--space-3);
            padding: var(--space-3);
            border: 1px solid var(--border-color);
            border-radius: var(--radius-md);
            background: var(--brand-surface);
            transition: border-color 150ms ease, box-shadow 150ms ease;

            &:hover,
            &:focus-within {
                border-color: var(--bs-primary);
                box-shadow: var(--shadow-sm);
            }

            @media (prefers-reduced-motion: reduce) {
                transition: none;
            }
        }

        .suggested-event-logo,
        .suggested-event-logo-placeholder {
            flex-shrink: 0;
            width: 3rem;
            height: 3rem;
            object-fit: contain;
            border-radius: var(--radius-sm);
        }

        .suggested-event-logo-placeholder {
            display: flex;
            align-items: center;
            justify-content: center;
            background: var(--bs-secondary-bg);
            color: var(--brand-text-muted);
            font-size: 1.25rem;
        }

        .suggested-event-body {
            flex: 1;
            min-width: 0;
        }

        .suggested-event-name {
            font-weight: var(--font-weight-semibold);
            color: var(--brand-text);
            margin-bottom: var(--space-1);
        }

        .suggested-event-customer {
            font-size: var(--font-size-sm);
            color: var(--brand-text-muted);
            margin-bottom: var(--space-2);
        }

        .suggested-event-badges {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-1);

            .badge {
                font-size: var(--font-size-xs);
                font-weight: var(--font-weight-medium);
            }
        }

        .suggested-event-cta {
            flex-shrink: 0;

            &:focus-visible {
                outline: none;
                box-shadow: var(--shadow-focus);
            }
        }

        @media (max-width: 575.98px) {
            .suggested-event {
                flex-wrap: wrap;
            }

            .suggested-event-cta {
                width: 100%;
                justify-content: center;
            }
        }
    `]
})
export class SuggestedEventsModalComponent {
    @Input({ required: true }) events!: SuggestedEventDto[];
    closed = output<void>();
}
