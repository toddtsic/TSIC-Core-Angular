import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { ClubService } from '@infrastructure/services/club.service';
import { ToastService } from '@shared-ui/toast.service';
import { ClubRepRegisterFormComponent } from '@views/registration/team/steps/club-rep-register-form.component';
import type { ClubRepProfileDto } from '@core/api';

/**
 * Standalone page for a ClubRep to edit their own profile post-registration.
 * Loads the profile then renders the shared club-rep form in edit mode.
 */
@Component({
    selector: 'app-club-rep-profile',
    standalone: true,
    imports: [ClubRepRegisterFormComponent],
    styles: [`
      :host { display: block; }
      .profile-page {
        max-width: 460px;
        margin: 0 auto;
        padding: var(--space-6) var(--space-3);
      }
      .back-link {
        display: inline-flex;
        align-items: center;
        gap: var(--space-1);
        background: none;
        border: none;
        padding: 0 0 var(--space-3);
        color: var(--bs-primary);
        font-size: var(--font-size-sm);
        font-weight: var(--font-weight-medium);
        cursor: pointer;
      }
      .back-link:hover { text-decoration: underline; }
      .back-link:focus-visible {
        outline: none;
        box-shadow: var(--shadow-focus);
        border-radius: var(--radius-sm);
      }
      .edit-header h5 {
        display: flex;
        align-items: center;
        gap: var(--space-2);
        font-weight: var(--font-weight-bold);
      }
      .edit-header h5 i { color: var(--bs-primary); }
    `],
    template: `
    <div class="profile-page">
      @if (loading()) {
        <div class="text-center py-5">
          <span class="spinner-border text-primary"></span>
        </div>
      } @else if (profile()) {
        <button type="button" class="back-link" (click)="goBack()">
          <i class="bi bi-arrow-left"></i> Back
        </button>
        <div class="card shadow border-0 card-rounded">
          <div class="card-header card-header-subtle border-0 py-3 edit-header">
            <h5 class="mb-0">
              <i class="bi bi-person-gear"></i>
              Edit Profile
            </h5>
          </div>
          <div class="card-body bg-neutral-0">
            <app-club-rep-register-form
              [mode]="'edit'"
              [existing]="profile()"
              (saved)="onSaved()" />
          </div>
        </div>
      } @else if (error()) {
        <div class="alert alert-danger">{{ error() }}</div>
      }
    </div>
  `,
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClubRepProfileComponent {
    private readonly clubService = inject(ClubService);
    private readonly router = inject(Router);
    private readonly route = inject(ActivatedRoute);
    private readonly toast = inject(ToastService);
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

    onSaved(): void {
        this.goBack();
    }

    goBack(): void {
        const jobPath = this.route.snapshot.parent?.params['jobPath']
            ?? this.route.snapshot.params['jobPath'];
        this.router.navigate([`/${jobPath}`]);
    }
}
