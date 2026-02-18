import { Injectable, inject, signal, computed, effect } from '@angular/core';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import type { Loadable } from '@infrastructure/shared/state.models';
import type { VIPlayerObjectResponse, RegSaverDetailsDto } from '@core/api';

/**
 * Insurance State v2 — same public API as InsuranceStateService,
 * but reads from decomposed JobContextService + FamilyPlayersService
 * instead of RegistrationWizardService.
 */
@Injectable({ providedIn: 'root' })
export class InsuranceStateV2Service {
    private readonly jobCtx = inject(JobContextService);
    private readonly familyPlayers = inject(FamilyPlayersService);

    readonly offerPlayerRegSaver = computed(() => this.jobCtx.offerPlayerRegSaver());
    readonly regSaverDetails = computed(() => this.familyPlayers.regSaverDetails());

    private readonly _verticalInsureOffer = signal<Loadable<VIPlayerObjectResponse>>({ loading: false, data: null, error: null });
    verticalInsureOffer(): Loadable<VIPlayerObjectResponse> { return this._verticalInsureOffer(); }
    setVerticalInsureOffer(value: Loadable<VIPlayerObjectResponse>): void { this._verticalInsureOffer.set(value); }

    // Sync from JobContextService (where preSubmit writes it)
    private readonly _offerSync = effect(() => {
        try {
            const offer = this.jobCtx.verticalInsureOffer();
            const cur = this._verticalInsureOffer();
            if (cur.data !== offer.data || cur.error !== offer.error || cur.loading !== offer.loading) {
                this._verticalInsureOffer.set(offer);
            }
        } catch { /* ignore */ }
    });

    private readonly _showVerticalInsureModal = signal(false);
    private readonly _viConsent = signal<{
        confirmed: boolean;
        declined: boolean;
        policyNumber?: string | null;
        policyCreateDate?: string | null;
        quotes?: Record<string, unknown>[];
        decidedUtc?: string;
    } | null>(null);

    showVerticalInsureModal(): boolean { return this._showVerticalInsureModal(); }
    viConsent() { return this._viConsent(); }
    hasVerticalInsureDecision(): boolean { return !!this._viConsent(); }
    verticalInsureConfirmed(): boolean { return !!this._viConsent()?.confirmed; }
    verticalInsureDeclined(): boolean { return !!this._viConsent()?.declined; }

    openVerticalInsureModal(): void {
        if (!this.offerPlayerRegSaver()) return;
        this._showVerticalInsureModal.set(true);
    }
    confirmVerticalInsurePurchase(policyNumber: string | null, policyCreateDate: string | null, quotes: Record<string, unknown>[] = []): void {
        this._viConsent.set({ confirmed: true, declined: false, policyNumber, policyCreateDate, quotes, decidedUtc: new Date().toISOString() });
        this._showVerticalInsureModal.set(false);
    }
    declineVerticalInsurePurchase(): void {
        this._viConsent.set({ confirmed: false, declined: true, decidedUtc: new Date().toISOString() });
        this._showVerticalInsureModal.set(false);
    }
    closeVerticalInsureModal(): void { this._showVerticalInsureModal.set(false); }
}
