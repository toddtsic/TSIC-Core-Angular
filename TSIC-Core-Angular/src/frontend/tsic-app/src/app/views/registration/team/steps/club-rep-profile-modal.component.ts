import { ChangeDetectionStrategy, Component, DestroyRef, inject, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { ClubService } from '@infrastructure/services/club.service';
import { ClubRepRegisterFormComponent } from './club-rep-register-form.component';
import type { ClubRepProfileDto } from '@core/api';

/**
 * Team wizard's inline "Edit Profile" surface for a club rep.
 *
 * The wizard auto-advances past the Login tab on sign-in (and the ToS bounce-back
 * fix sends a returning rep straight into Teams), so the Login tab's account-summary
 * is no longer the discoverable home for profile editing. This modal restores that
 * affordance where the rep now lands — the Teams step — keeping her in the wizard.
 *
 * Mirrors the standalone `/account/club-rep` page (load via getSelfProfile, render
 * the shared form in edit mode), presented as a dialog shell consistent with the
 * other steps/ modals. The shared form owns the save (updateSelfProfile) and emits
 * `saved`; this modal only loads and frames it.
 */
@Component({
    selector: 'app-club-rep-profile-modal',
    standalone: true,
    imports: [TsicDialogComponent, ClubRepRegisterFormComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="closed.emit()">
      <div class="modal-content edit-profile-modal">

        <!-- Header -->
        <div class="edit-profile-header">
          <h5 class="edit-profile-title">
            <i class="bi bi-person-gear me-2"></i>Edit Profile
          </h5>
          <button type="button" class="edit-profile-close" (click)="closed.emit()" aria-label="Close">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>

        <!-- Body -->
        <div class="edit-profile-body">
          @if (loading()) {
            <div class="text-center py-5">
              <span class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Loading your profile...</span>
              </span>
            </div>
          } @else if (profile()) {
            <app-club-rep-register-form
              [mode]="'edit'"
              [existing]="profile()"
              (saved)="saved.emit()" />
          } @else if (error()) {
            <div class="alert alert-danger m-0">{{ error() }}</div>
          }
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
      :host { display: block; }

      .edit-profile-header {
        position: relative;
        display: flex;
        align-items: center;
        padding: var(--space-4) var(--space-5);
        border-bottom: 1px solid var(--border-color);
      }

      .edit-profile-title {
        display: flex;
        align-items: center;
        margin: 0;
        font-weight: var(--font-weight-bold);
        color: var(--brand-text);
      }

      .edit-profile-title i { color: var(--bs-primary); }

      .edit-profile-close {
        margin-left: auto;
        background: none;
        border: none;
        padding: var(--space-1);
        color: var(--brand-text-muted);
        cursor: pointer;
        line-height: 1;
      }

      .edit-profile-close:hover { color: var(--brand-text); }
      .edit-profile-close:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
        border-radius: var(--radius-sm);
      }

      .edit-profile-body {
        padding: var(--space-5);
        background: var(--neutral-0);
      }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubRepProfileModalComponent {
    readonly saved = output<void>();
    readonly closed = output<void>();

    private readonly clubService = inject(ClubService);
    private readonly destroyRef = inject(DestroyRef);

    readonly loading = signal(true);
    readonly profile = signal<ClubRepProfileDto | null>(null);
    readonly error = signal<string | null>(null);

    constructor() {
        this.clubService.getSelfProfile()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: p => {
                    this.profile.set(p);
                    this.loading.set(false);
                },
                error: () => {
                    this.error.set('Unable to load your profile. Please try again.');
                    this.loading.set(false);
                },
            });
    }
}
