import { ChangeDetectionStrategy, Component, computed, inject, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';

/**
 * Player Selection step — shows family players as checkboxes.
 * Registered players are locked (checked, disabled).
 * Unregistered players can be toggled on/off.
 */
@Component({
    selector: 'app-prw-player-selection-step',
    standalone: true,
    imports: [DatePipe],
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-2">
        <h5 class="mb-0 fw-semibold" style="font-size: var(--font-size-base)">Select Players</h5>
      </div>
      <div class="card-body pt-3">
        @if (state.familyPlayers.familyPlayersLoading()) {
          <div class="text-center py-4">
            <div class="spinner-border text-primary" role="status">
              <span class="visually-hidden">Loading players...</span>
            </div>
          </div>
        } @else if (state.familyPlayers.familyPlayers().length === 0) {
          <div class="alert alert-warning">
            No players found. Please ensure your family account has children added.
          </div>
        } @else {
          <p class="wizard-tip">
            @if (hasRegistered()) {
              Select additional players to register. Players already in this event are shown for reference.
            } @else {
              Choose which players to register for this event.
            }
          </p>
          <div class="player-list">
            @for (player of state.familyPlayers.familyPlayers(); track player.playerId) {
              <label class="player-row"
                [class.is-selected]="player.selected && !player.registered"
                [class.is-registered]="player.registered">
                <input
                  type="checkbox"
                  class="player-check"
                  [checked]="player.selected || player.registered"
                  [disabled]="player.registered"
                  (change)="toggle(player.playerId)"
                  [attr.aria-label]="'Select ' + player.firstName + ' ' + player.lastName" />
                <i class="bi player-icon"
                  [class.bi-person-fill]="!player.registered"
                  [class.bi-person-check-fill]="player.registered"></i>
                <div class="player-info">
                  <span class="player-name">{{ player.firstName }} {{ player.lastName }}</span>
                  @if (player.dob) {
                    <span class="player-dob">{{ player.dob | date:'MM/dd/yyyy' }}</span>
                  }
                </div>
                @if (player.registered) {
                  <span class="reg-badge">
                    <i class="bi bi-check-circle-fill me-1"></i>Registered
                  </span>
                }
              </label>
            }
          </div>
        }
      </div>
    </div>
  `,
    styles: [`
      .player-list {
        display: flex;
        flex-direction: column;
        gap: var(--space-2);
      }

      .player-row {
        display: flex;
        align-items: center;
        gap: var(--space-3);
        padding: var(--space-3) var(--space-4);
        border-radius: var(--radius-md);
        border: 1px solid var(--border-color);
        background: var(--brand-surface);
        cursor: pointer;
        transition: border-color 0.15s ease, background-color 0.15s ease, box-shadow 0.15s ease;

        &:hover:not(.is-registered) {
          border-color: rgba(var(--bs-primary-rgb), 0.4);
          background: rgba(var(--bs-primary-rgb), 0.03);
        }

        &.is-selected {
          border-color: var(--bs-primary);
          background: rgba(var(--bs-primary-rgb), 0.06);
          box-shadow: 0 0 0 1px rgba(var(--bs-primary-rgb), 0.15);
        }

        &.is-registered {
          border-color: rgba(var(--bs-success-rgb), 0.25);
          background: rgba(var(--bs-success-rgb), 0.05);
          cursor: default;
          opacity: 0.75;
        }
      }

      /* Custom checkbox */
      .player-check {
        appearance: none;
        width: 20px;
        height: 20px;
        min-width: 20px;
        border: 2px solid var(--neutral-300);
        border-radius: var(--radius-sm);
        background: var(--brand-surface);
        cursor: pointer;
        transition: border-color 0.15s ease, background-color 0.15s ease;
        position: relative;

        &:checked {
          background: var(--bs-primary);
          border-color: var(--bs-primary);

          &::after {
            content: '';
            position: absolute;
            top: 2px;
            left: 5px;
            width: 6px;
            height: 10px;
            border: solid var(--neutral-0);
            border-width: 0 2px 2px 0;
            transform: rotate(45deg);
          }
        }

        &:disabled {
          cursor: default;

          &:checked {
            background: var(--bs-success);
            border-color: var(--bs-success);
          }
        }

        &:focus-visible {
          outline: none;
          box-shadow: var(--shadow-focus);
        }
      }

      .player-icon {
        font-size: var(--font-size-xl);
        color: var(--neutral-400);

        .is-selected & { color: var(--bs-primary); }
        .is-registered & { color: var(--bs-success); }
      }

      .player-info {
        flex: 1;
        min-width: 0;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }

      .player-name {
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-semibold);
        color: var(--brand-text);
      }

      .player-dob {
        font-size: var(--font-size-xs);
        color: var(--brand-text-muted);
      }

      .reg-badge {
        font-size: var(--font-size-xs);
        font-weight: var(--font-weight-semibold);
        color: var(--bs-success);
        white-space: nowrap;
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerSelectionStepComponent {
    readonly state = inject(PlayerWizardStateService);
    readonly hasRegistered = computed(() =>
        this.state.familyPlayers.familyPlayers().some(p => p.registered));

    toggle(playerId: string): void {
        this.state.togglePlayerSelection(playerId);
    }
}
