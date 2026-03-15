import { Injectable, inject, signal } from '@angular/core';
import { JobService } from '@infrastructure/services/job.service';
import type { Loadable } from '@infrastructure/shared/state.models';
import type { VITeamObjectResponse } from '@core/api';

/**
 * State management for team insurance (Vertical Insure integration).
 * Tracks offer data, modal state, and user consent/decline decision.
 */
@Injectable({ providedIn: 'root' })
export class TeamInsuranceStateService {
    private readonly jobService = inject(JobService);

    // Job-level offer flag from metadata
    offerTeamRegSaver(): boolean {
        return this.jobService.currentJob()?.offerTeamRegsaverInsurance ?? false;
    }

    // VI offer payload (widget data)
    private readonly _verticalInsureOffer = signal<Loadable<VITeamObjectResponse>>({
        loading: false,
        data: null,
        error: null
    });

    // Modal visibility
    private readonly _showVerticalInsureModal = signal<boolean>(false);

    // User decision (confirmed with quotes or declined)
    private readonly _viConsent = signal<{
        confirmed: boolean;
        declined: boolean;
        policyNumbers?: Record<string, string>; // teamId -> policyNumber
        quotes?: Record<string, unknown>[];
        decidedUtc?: string;
    } | null>(null);

    // Accessors
    verticalInsureOffer(): Loadable<VITeamObjectResponse> { return this._verticalInsureOffer(); }
    showVerticalInsureModal(): boolean { return this._showVerticalInsureModal(); }
    viConsent() { return this._viConsent(); }
    hasVerticalInsureDecision(): boolean { return !!this._viConsent(); }
    verticalInsureConfirmed(): boolean { return !!this._viConsent()?.confirmed; }
    verticalInsureDeclined(): boolean { return !!this._viConsent()?.declined; }

    // Mutators
    setVerticalInsureOffer(value: Loadable<VITeamObjectResponse>): void {
        this._verticalInsureOffer.set(value);
    }

    openVerticalInsureModal(): void {
        if (!this.offerTeamRegSaver()) return;
        this._showVerticalInsureModal.set(true);
    }

    confirmVerticalInsurePurchase(quotes: Record<string, unknown>[] = []): void {
        this._viConsent.set({
            confirmed: true,
            declined: false,
            quotes,
            decidedUtc: new Date().toISOString()
        });
        this._showVerticalInsureModal.set(false);
    }

    declineVerticalInsurePurchase(): void {
        this._viConsent.set({
            confirmed: false,
            declined: true,
            decidedUtc: new Date().toISOString()
        });
        this._showVerticalInsureModal.set(false);
    }

    closeVerticalInsureModal(): void {
        this._showVerticalInsureModal.set(false);
    }

    updatePolicyNumbers(policyNumbers: Record<string, string>): void {
        const current = this._viConsent();
        if (current?.confirmed) {
            this._viConsent.set({ ...current, policyNumbers });
        }
    }

    reset(): void {
        this._verticalInsureOffer.set({ loading: false, data: null, error: null });
        this._viConsent.set(null);
        this._showVerticalInsureModal.set(false);
    }
}
