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
    template: `
    @if (loading()) {
      <div class="text-center py-5">
        <span class="spinner-border text-primary"></span>
      </div>
    } @else if (profile()) {
      <app-club-rep-register-form
        [mode]="'edit'"
        [existing]="profile()"
        (saved)="onSaved()"
        (closed)="goBack()" />
    } @else if (error()) {
      <div class="alert alert-danger m-4">{{ error() }}</div>
    }
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
