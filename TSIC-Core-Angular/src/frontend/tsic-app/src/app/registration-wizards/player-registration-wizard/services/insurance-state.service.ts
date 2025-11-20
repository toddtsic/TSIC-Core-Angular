import { Injectable, inject, signal } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import type { Loadable } from '../../../core/models/state.models';
import type { VIPlayerObjectResponse } from '../../../core/api/models/VIPlayerObjectResponse';

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
    verticalInsureOffer(): Loadable<VIPlayerObjectResponse> { return this.reg.verticalInsureOffer(); }

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
