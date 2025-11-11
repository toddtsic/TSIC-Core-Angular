import { Component, EventEmitter, Output, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RegistrationWizardService } from '../registration-wizard.service';
import { BottomNavComponent } from '../bottom-nav.component';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-rw-player-selection',
  standalone: true,
  imports: [CommonModule, FormsModule, BottomNavComponent],
  template: `
    <div class="card shadow border-0 card-rounded">
      <div class="card-header card-header-subtle border-0 py-3">
        <h5 class="mb-0 fw-semibold">Select Players</h5>
      </div>
      <div class="card-body position-relative">
        <!-- Loading overlay -->
        @if (state.familyPlayersLoading()) {
        <div class="position-absolute top-0 start-0 w-100 h-100 d-flex flex-column align-items-center justify-content-center bg-white bg-opacity-75" style="z-index: 10;">
          <div class="spinner-border text-primary mb-3" role="status" aria-hidden="true"></div>
          <div class="fw-semibold">Loading Family Players...</div>
        </div>
        }
        <p class="text-secondary mb-3">Select the players you wish to register.</p>

        <!-- Fallback to classic *ngIf / *ngFor for widest compatibility -->
        @if (!state.familyPlayersLoading() && state.familyPlayers().length === 0) {
          <div class="alert alert-info">No players found for your family. You can add players in your Family Account.</div>
        } @else {
          <ul class="list-group list-group-flush mb-3" [class.opacity-50]="state.familyPlayersLoading()">
            @for (p of state.familyPlayers(); track trackPlayer($index, p)) {
            <li class="list-group-item d-flex align-items-center justify-content-between">
              <div class="d-flex align-items-center gap-3">
      <input type="checkbox"
        class="form-check-input"
        [checked]="isSelected(p.playerId)"
        [disabled]="state.isPlayerLocked(p.playerId)"
        (change)="!state.isPlayerLocked(p.playerId) && state.togglePlayerSelection(p.playerId)"
        [attr.aria-label]="state.isPlayerLocked(p.playerId) ? 'Already registered' : (isSelected(p.playerId) ? 'Deselect player' : 'Select player')" />
                <div>
                  <div class="fw-semibold">{{ p.firstName }} {{ p.lastName }}</div>
                  <div class="text-secondary small">{{ p.gender || 'Gender N/A' }} • {{ p.dob || 'DOB not on file' }}</div>
                </div>
              </div>
              @if (state.isPlayerLocked(p.playerId)) { <span class="badge bg-secondary" title="Previously registered for this job">Locked</span> }
            </li>
            }
          </ul>
        }

  <app-rw-bottom-nav [hideBack]="true" [nextDisabled]="state.selectedPlayerIds().length === 0" (next)="next.emit()"></app-rw-bottom-nav>
      </div>
    </div>
  `
})
export class PlayerSelectionComponent {
  @Output() next = new EventEmitter<void>();
  state = inject(RegistrationWizardService);
  private requestedOnce = false;

  // Create the reactive loader in an injection context (field initializer),
  // and allow controlled signal writes inside the effect.
  private readonly autoLoad = effect(() => {
    const jp = this.state.jobPath();
    if (jp && !this.requestedOnce) {
      this.requestedOnce = true;
      this.state.loadFamilyPlayers(jp);
    }
  }, { allowSignalWrites: true });

  isSelected(id: string): boolean {
    try {
      const fam = this.state.familyPlayers();
      const p = fam.find(x => x.playerId === id);
      return !!p && (p.selected || p.registered);
    } catch { return false; }
  }

  trackPlayer = (_: number, p: { playerId: string }) => p.playerId;
}
