import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';

export type PaymentOption = 'PIF' | 'Deposit';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    private readonly http = inject(HttpClient);
    // Job context
    jobPath = signal<string>('');
    jobId = signal<string>('');

    // Start mode selection: 'new' (start fresh), 'edit' (edit prior), 'parent' (update/deassign)
    startMode = signal<'new' | 'edit' | 'parent' | null>(null);

    // Family account presence (from Family Check step)
    hasFamilyAccount = signal<'yes' | 'no' | null>(null);

    // Players and selections
    selectedPlayers = signal<Array<{ userId: string; name: string }>>([]);
    activeFamilyUser = signal<{ familyUserId: string; displayName: string } | null>(null);
    familyUsers = signal<Array<{ familyUserId: string; displayName: string }>>([]);
    // Whether an existing player registration for the current job + active family user already exists.
    // null = unknown/not yet checked; true/false = definitive.
    existingRegistrationAvailable = signal<boolean | null>(null);
    teamConstraintType = signal<string | null>(null); // e.g., BYGRADYEAR
    teamConstraintValue = signal<string | null>(null); // e.g., 2027
    selectedTeams = signal<Record<string, string>>({}); // playerId -> teamId

    // Forms data per player (dynamic fields later)
    formData = signal<Record<string, any>>({}); // playerId -> { fieldName: value }

    // Payment
    paymentOption = signal<PaymentOption>('PIF');

    reset(): void {
        this.startMode.set(null);
        this.hasFamilyAccount.set(null);
        this.selectedPlayers.set([]);
        this.teamConstraintType.set(null);
        this.teamConstraintValue.set(null);
        this.selectedTeams.set({});
        this.formData.set({});
        this.paymentOption.set('PIF');
        this.activeFamilyUser.set(null);
        this.familyUsers.set([]);
        this.existingRegistrationAvailable.set(null);
    }

    loadFamilyUsers(jobPath: string): void {
        if (!jobPath) return;
        // Future: cache by jobPath; for now simple fetch
        this.http.get<Array<{ familyUserId: string; displayName: string }>>(`/api/family/users`, { params: { jobPath } })
            .subscribe({
                next: users => {
                    this.familyUsers.set(users || []);
                    // Auto-select if exactly one user
                    if (users?.length === 1) {
                        this.activeFamilyUser.set(users[0]);
                    }
                },
                error: err => {
                    console.error('[RegWizard] Failed to load family users', err);
                    this.familyUsers.set([]);
                }
            });
    }
}
