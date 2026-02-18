import { ChangeDetectionStrategy, Component, OnInit, OnDestroy, inject, signal, output } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Subscription, switchMap, catchError, of } from 'rxjs';
import { ClubService } from '@infrastructure/services/club.service';
import { TeamRegistrationService } from '../services/team-registration.service';
import type { ClubSearchResult } from '@core/api';
import { formatHttpError } from '../../shared/utils/error-utils';

/**
 * Reusable add-club form component with similar clubs detection and resolution.
 * Used in both club selection modal and club management modal.
 */
@Component({
    selector: 'app-add-club-form',
    standalone: true,
    imports: [ReactiveFormsModule],
    templateUrl: './add-club-form.component.html',
    styleUrls: ['./add-club-form.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class AddClubFormComponent implements OnInit, OnDestroy {
    // Services
    private readonly fb = inject(FormBuilder);
    private readonly clubService = inject(ClubService);
    private readonly teamRegService = inject(TeamRegistrationService);

    // Outputs
    readonly clubAdded = output<void>();
    readonly cancelled = output<void>();

    // Form
    addClubForm!: FormGroup;

    // State
    readonly isSubmitting = signal(false);
    readonly errorMessage = signal<string | null>(null);
    readonly successMessage = signal<string | null>(null);
    readonly similarClubs = signal<ClubSearchResult[]>([]);
    readonly selectedExistingClubId = signal<number | null>(null);

    private addClubSubscription?: Subscription;
    private successTimeoutId?: ReturnType<typeof setTimeout>;

    ngOnInit(): void {
        this.addClubForm = this.fb.group({
            clubName: ['', [Validators.required, Validators.maxLength(200)]]
        });
    }

    ngOnDestroy(): void {
        this.addClubSubscription?.unsubscribe();
        if (this.successTimeoutId) {
            clearTimeout(this.successTimeoutId);
        }
    }

    submitAddClub(): void {
        if (this.addClubForm.invalid) {
            this.addClubForm.markAllAsTouched();
            this.errorMessage.set('Please enter a club name');
            return;
        }

        this.isSubmitting.set(true);
        this.errorMessage.set(null);
        this.successMessage.set(null);

        const clubName = this.addClubForm.value.clubName.trim();
        const useExistingClubId = this.selectedExistingClubId();

        const request = {
            clubName,
            useExistingClubId: useExistingClubId ?? undefined
        };

        this.addClubSubscription?.unsubscribe();

        this.addClubSubscription = this.clubService.addClub(request).pipe(
            switchMap((response) => {
                if (response.success) {
                    this.successMessage.set('Club added successfully!');
                    this.similarClubs.set([]);
                    this.selectedExistingClubId.set(null);
                    // Reload clubs list to get updated data
                    return this.teamRegService.getMyClubs();
                } else {
                    this.errorMessage.set(response.message || 'Failed to add club');
                    if (response.similarClubs && response.similarClubs.length > 0) {
                        this.similarClubs.set(response.similarClubs);
                    }
                    this.isSubmitting.set(false);
                    return of(null);
                }
            }),
            catchError((err) => {
                this.errorMessage.set(formatHttpError(err));
                this.isSubmitting.set(false);
                console.error('Add club error:', err);
                return of(null);
            })
        ).subscribe({
            next: (clubs) => {
                if (clubs) {
                    // Success - emit event after short delay
                    this.successTimeoutId = globalThis.setTimeout(() => {
                        this.addClubForm.reset();
                        this.isSubmitting.set(false);
                        this.clubAdded.emit();
                        this.successTimeoutId = undefined;
                    }, 1500);
                }
            },
            error: (err) => {
                console.error('Unexpected error in submitAddClub:', err);
                this.isSubmitting.set(false);
            }
        });
    }

    selectExistingClub(clubId: number): void {
        this.selectedExistingClubId.set(clubId);
        // Resubmit with selected existing club
        this.submitAddClub();
    }

    createNewClubAnyway(): void {
        // Clear similar clubs warning and force creation with new name
        this.similarClubs.set([]);
        this.selectedExistingClubId.set(null);
        // User has acknowledged similar clubs exist but wants to create new anyway
        // The backend will handle final duplicate prevention
    }

    cancel(): void {
        this.addClubForm.reset();
        this.errorMessage.set(null);
        this.successMessage.set(null);
        this.similarClubs.set([]);
        this.selectedExistingClubId.set(null);
        this.cancelled.emit();
    }

    dismissSimilarClubs(): void {
        this.similarClubs.set([]);
        this.errorMessage.set(null);
    }
}
