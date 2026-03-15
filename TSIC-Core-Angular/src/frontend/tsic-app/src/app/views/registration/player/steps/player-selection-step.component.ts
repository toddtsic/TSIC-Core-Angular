import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { PlayerWizardStateService } from '../state/player-wizard-state.service';

/**
 * Player Selection step — shows family players as checkboxes.
 * Registered players are locked (checked, disabled).
 * Unregistered players can be toggled on/off.
 */
@Component({
    selector: 'app-prw-player-selection-step',
    standalone: true,
    template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Players</h5>
      </div>
      <div class="card-body">
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
          <p class="text-muted small mb-3">
            Select which players to register. Players already registered for this event are locked.
          </p>
          <div class="list-group">
            @for (player of state.familyPlayers.familyPlayers(); track player.playerId) {
              <label
                class="list-group-item d-flex align-items-center gap-3"
                [class.list-group-item-success]="player.registered">
                <input
                  type="checkbox"
                  class="form-check-input mt-0"
                  [checked]="player.selected || player.registered"
                  [disabled]="player.registered"
                  (change)="toggle(player.playerId)"
                  [attr.aria-label]="'Select ' + player.firstName + ' ' + player.lastName" />
                <div class="flex-grow-1">
                  <span class="fw-semibold">{{ player.firstName }} {{ player.lastName }}</span>
                  @if (player.dob) {
                    <span class="text-muted small ms-2">DOB: {{ player.dob }}</span>
                  }
                </div>
                @if (player.registered) {
                  <span class="badge bg-success">Registered</span>
                }
              </label>
            }
          </div>
        }
      </div>
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlayerSelectionStepComponent {
    readonly state = inject(PlayerWizardStateService);

    toggle(playerId: string): void {
        this.state.togglePlayerSelection(playerId);
    }
}
