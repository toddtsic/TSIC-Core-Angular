import { Injectable, inject, signal, computed, linkedSignal } from '@angular/core';
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

    /**
     * Mirrors JobContextService (where preSubmit writes the offer), while staying locally
     * settable. Reseeds only when jobCtx's offer actually changes, so a local .set() survives
     * until the source moves. The effect this replaced read the very signal it wrote, so it
     * re-ran on its own write and reverted every local set back to the jobCtx value.
     */
    private readonly _verticalInsureOffer = linkedSignal<Loadable<VIPlayerObjectResponse>, Loadable<VIPlayerObjectResponse>>({
        source: () => this.jobCtx.verticalInsureOffer(),
        computation: offer => offer,
    });
    verticalInsureOffer(): Loadable<VIPlayerObjectResponse> { return this._verticalInsureOffer(); }
    setVerticalInsureOffer(value: Loadable<VIPlayerObjectResponse>): void { this._verticalInsureOffer.set(value); }

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

    reset(): void {
        this._verticalInsureOffer.set({ loading: false, data: null, error: null });
        this._showVerticalInsureModal.set(false);
        this._viConsent.set(null);
    }
}
