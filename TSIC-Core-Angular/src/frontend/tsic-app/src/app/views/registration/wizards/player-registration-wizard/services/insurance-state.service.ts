import { Injectable, inject, signal, effect } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import type { Loadable } from '@infrastructure/shared/state.models';
import type { VIPlayerObjectResponse } from '@core/api';

/**
 * Facade service for insurance-related state (offer, modal, consent).
 * Incremental extraction: proxies existing signals on RegistrationWizardService.
 * Future step: migrate signals fully into this service and slim the wizard service.
 */
@Injectable({ providedIn: 'root' })
export class InsuranceStateService {
    private readonly reg = inject(RegistrationWizardService);

    // Job-level offer flag still sourced from wizard (avoids cycle)
    offerPlayerRegSaver(): boolean { return this.reg.offerPlayerRegSaver(); }
    regSaverDetails(): any { return this.reg.regSaverDetails(); }
    // Migrated offer payload signal (initially synchronized from wizard for backward compatibility)
    private readonly _verticalInsureOffer = signal<Loadable<VIPlayerObjectResponse>>({ loading: false, data: null, error: null });
    verticalInsureOffer(): Loadable<VIPlayerObjectResponse> { return this._verticalInsureOffer(); }
    setVerticalInsureOffer(value: Loadable<VIPlayerObjectResponse>): void { this._verticalInsureOffer.set(value); }
    // Temporary synchronization effect while wizard still owns original signal; will be removed once fully migrated.
    private readonly _offerSync = effect(() => {
        try {
            const offer = this.reg.verticalInsureOffer();
            // Only overwrite local when data or error changes (avoid needless writes)
            const cur = this._verticalInsureOffer();
            if (cur.data !== offer.data || cur.error !== offer.error || cur.loading !== offer.loading) {
                this._verticalInsureOffer.set(offer);
            }
        } catch { /* ignore */ }
    });

    // Localized signals migrated from wizard ---------------------------------
    private readonly _showVerticalInsureModal = signal<boolean>(false);
    private readonly _viConsent = signal<{ confirmed: boolean; declined: boolean; policyNumber?: string | null; policyCreateDate?: string | null; quotes?: any[]; decidedUtc?: string } | null>(null);

    showVerticalInsureModal(): boolean { return this._showVerticalInsureModal(); }
    viConsent() { return this._viConsent(); }
    hasVerticalInsureDecision(): boolean { return !!this._viConsent(); }
    verticalInsureConfirmed(): boolean { return !!this._viConsent()?.confirmed; }
    verticalInsureDeclined(): boolean { return !!this._viConsent()?.declined; }

    // Mutators ---------------------------------------------------------------
    openVerticalInsureModal(): void {
        if (!this.offerPlayerRegSaver()) return;
        this._showVerticalInsureModal.set(true);
    }
    confirmVerticalInsurePurchase(policyNumber: string | null, policyCreateDate: string | null, quotes: any[] = []): void {
        this._viConsent.set({ confirmed: true, declined: false, policyNumber, policyCreateDate, quotes, decidedUtc: new Date().toISOString() });
        this._showVerticalInsureModal.set(false);
    }
    declineVerticalInsurePurchase(): void {
        this._viConsent.set({ confirmed: false, declined: true, decidedUtc: new Date().toISOString() });
        this._showVerticalInsureModal.set(false);
    }
    closeVerticalInsureModal(): void { this._showVerticalInsureModal.set(false); }
}
