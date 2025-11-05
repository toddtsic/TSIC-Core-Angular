import { Injectable, signal } from '@angular/core';

export type PaymentOption = 'PIF' | 'Deposit';

@Injectable({ providedIn: 'root' })
export class RegistrationWizardService {
    // Job context
    jobPath = signal<string>('');
    jobId = signal<string>('');

    // Start mode selection: 'new' (start fresh), 'edit' (edit prior), 'parent' (update/deassign)
    startMode = signal<'new' | 'edit' | 'parent' | null>(null);

    // Players and selections
    selectedPlayers = signal<Array<{ userId: string; name: string }>>([]);
    teamConstraintType = signal<string | null>(null); // e.g., BYGRADYEAR
    teamConstraintValue = signal<string | null>(null); // e.g., 2027
    selectedTeams = signal<Record<string, string>>({}); // playerId -> teamId

    // Forms data per player (dynamic fields later)
    formData = signal<Record<string, any>>({}); // playerId -> { fieldName: value }

    // Payment
    paymentOption = signal<PaymentOption>('PIF');

    reset(): void {
        this.startMode.set(null);
        this.selectedPlayers.set([]);
        this.teamConstraintType.set(null);
        this.teamConstraintValue.set(null);
        this.selectedTeams.set({});
        this.formData.set({});
        this.paymentOption.set('PIF');
    }
}
