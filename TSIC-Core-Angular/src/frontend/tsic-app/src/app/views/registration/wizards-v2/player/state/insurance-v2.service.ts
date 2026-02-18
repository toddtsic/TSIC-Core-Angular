import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { ToastService } from '@shared-ui/toast.service';
import { ViDarkModeService } from '@views/registration/wizards/shared/services/vi-dark-mode.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/wizards/shared/services/credit-card-utils';
import { InsuranceStateV2Service } from './insurance-state-v2.service';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import type { InsurancePurchaseRequestDto, InsurancePurchaseResponseDto, CreditCardInfo } from '@core/api';
import type { VIOfferData, VIWidgetState, VIWindowExtension } from '@views/registration/wizards/shared/types/wizard.types';

export interface VerticalInsureQuote {
    quote_id?: string;
    quoteId?: string;
    total?: number;
    id?: string;
    metadata?: Record<string, unknown>;
    policy_attributes?: {
        participant?: { first_name?: string; last_name?: string };
        teams?: { team_name?: string }[];
    };
}

/**
 * Insurance v2 — same business logic as InsuranceService,
 * but reads from decomposed services instead of RegistrationWizardService.
 */
@Injectable({ providedIn: 'root' })
export class InsuranceV2Service {
    private readonly jobCtx = inject(JobContextService);
    private readonly fp = inject(FamilyPlayersService);
    private readonly insuranceState = inject(InsuranceStateV2Service);
    private readonly http = inject(HttpClient);
    private readonly toast = inject(ToastService);
    private readonly viDarkMode = inject(ViDarkModeService);

    private readonly _quotes = signal<VerticalInsureQuote[]>([]);
    private readonly _hasUserResponse = signal(false);
    private readonly _error = signal<string | null>(null);
    private readonly _widgetInitialized = signal(false);
    private readonly purchasing = signal(false);

    readonly quotes = this._quotes.asReadonly();
    readonly hasUserResponse = this._hasUserResponse.asReadonly();
    readonly error = this._error.asReadonly();
    readonly widgetInitialized = this._widgetInitialized.asReadonly();

    offerEnabled = computed(() => this.insuranceState.offerPlayerRegSaver());
    consented = computed(() => this.insuranceState.verticalInsureConfirmed());
    declined = computed(() => this.insuranceState.verticalInsureDeclined());

    initWidget(hostSelector: string, offerData: VIOfferData): void {
        if (this._widgetInitialized()) return;
        const viWindow = globalThis as unknown as VIWindowExtension;
        if (!viWindow.VerticalInsure) {
            this._error.set('VerticalInsure script missing');
            return;
        }
        try {
            this.viDarkMode.injectDarkModeColors(offerData);
            const instance = new viWindow.VerticalInsure(
                hostSelector,
                offerData,
                (st: VIWidgetState) => {
                    instance.validate().then((valid: boolean) => {
                        this._hasUserResponse.set(valid);
                        const quotes = st?.quotes || [];
                        this._quotes.set(quotes);
                        this._error.set(null);
                        this._widgetInitialized.set(true);
                        this.viDarkMode.applyViDarkMode(hostSelector);
                        if (valid) {
                            if (quotes.length > 0) {
                                this.insuranceState.confirmVerticalInsurePurchase(null, null, quotes);
                            } else {
                                this.insuranceState.declineVerticalInsurePurchase();
                            }
                        }
                    });
                },
                (st: VIWidgetState) => {
                    instance.validate().then((valid: boolean) => {
                        this._hasUserResponse.set(valid);
                        const quotes = st?.quotes || [];
                        this._quotes.set(quotes);
                        this.viDarkMode.applyViDarkMode(hostSelector);
                        if (valid) {
                            if (quotes.length > 0) {
                                this.insuranceState.confirmVerticalInsurePurchase(null, null, quotes);
                            } else {
                                this.insuranceState.declineVerticalInsurePurchase();
                            }
                        }
                    });
                },
            );
        } catch (e: unknown) {
            console.error('VerticalInsure init error', e);
            this._error.set('VerticalInsure initialization failed');
        }
    }

    premiumTotal(): number {
        return this._quotes().reduce((c, q) => c + Number(q?.total || 0), 0) / 100;
    }

    quotedPlayers(): string[] {
        return this._quotes().map(q => {
            const fn = q?.policy_attributes?.participant?.first_name?.trim?.() || '';
            const ln = q?.policy_attributes?.participant?.last_name?.trim?.() || '';
            const name = `${fn} ${ln}`.trim();
            if (!name) return '';
            const totalCents = Number(q?.total || 0);
            const suffix = totalCents > 0 ? ` ($${(totalCents / 100).toFixed(2)})` : '';
            return name + suffix;
        }).filter(Boolean);
    }

    purchaseInsurance(card?: {
        number?: string; expiry?: string; code?: string;
        firstName?: string; lastName?: string; zip?: string;
        email?: string; phone?: string; address?: string;
    }, onComplete?: (message: string) => void): void {
        if (this.purchasing()) return;
        const quoteIds = this._quotes().map(q => String(q?.quote_id ?? q?.quoteId ?? '')).filter(Boolean);
        const registrationIds = this._quotes().map(q => this.extractRegistrationId(q)).filter(Boolean);
        if (quoteIds.length === 0) {
            if (onComplete) this.toast.show('No insurance quotes to purchase', 'danger', 3000);
            return;
        }
        if (registrationIds.length !== quoteIds.length) {
            this.toast.show('Insurance quote / registration mismatch. Please retry.', 'danger', 4000);
            return;
        }
        if (!card) {
            this.toast.show('Credit card information required for insurance purchase', 'danger', 4000);
            return;
        }
        const creditCardPayload: CreditCardInfo = {
            number: card.number?.trim() || undefined,
            expiry: sanitizeExpiry(card.expiry),
            code: card.code?.trim() || undefined,
            firstName: card.firstName?.trim() || undefined,
            lastName: card.lastName?.trim() || undefined,
            zip: card.zip?.trim() || undefined,
            email: card.email?.trim() || undefined,
            phone: sanitizePhone(card.phone) || undefined,
            address: card.address?.trim() || undefined,
        };
        const req: InsurancePurchaseRequestDto = {
            jobPath: this.jobCtx.jobPath(),
            registrationIds,
            quoteIds,
            creditCard: creditCardPayload,
        };
        this.purchasing.set(true);
        this.http.post<InsurancePurchaseResponseDto>(`${environment.apiUrl}/insurance/purchase`, req)
            .subscribe({
                next: resp => {
                    this.purchasing.set(false);
                    if (resp?.success) {
                        this.toast.show('Processing with Vertical Insurance was SUCCESSFUL', 'success', 3000);
                        onComplete?.('Insurance processed via Vertical Insure.');
                    } else {
                        this.toast.show('Vertical Insurance processing failed', 'danger', 4000);
                        onComplete?.('Insurance processing failed. Registration is still complete.');
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this.purchasing.set(false);
                    this.toast.show('Processing with Vertical Insurance failed', 'danger', 4000);
                    onComplete?.('Insurance processing failed. Registration is still complete.');
                },
            });
    }

    private extractRegistrationId(q: VerticalInsureQuote): string {
        const idFromMeta = this.extractRegistrationIdFromMeta(q?.metadata);
        if (idFromMeta) return idFromMeta;
        return this.inferRegistrationIdFromParticipant(q);
    }

    private extractRegistrationIdFromMeta(meta: Record<string, unknown> | undefined): string {
        if (!meta) return '';
        const direct = meta['TsicRegistrationId'] ?? meta['tsicRegistrationId'] ?? meta['tsic_registration_id'];
        if (direct) return String(direct);
        for (const k of Object.keys(meta)) {
            if (/registration.?id/i.test(k)) {
                const v = meta[k];
                if (v) return String(v);
            }
        }
        return '';
    }

    private inferRegistrationIdFromParticipant(q: VerticalInsureQuote): string {
        const fn = q?.policy_attributes?.participant?.first_name?.trim?.() || '';
        const ln = q?.policy_attributes?.participant?.last_name?.trim?.() || '';
        const participant = (fn + ' ' + ln).trim().toLowerCase();
        if (!participant) return '';
        try {
            for (const p of this.fp.familyPlayers()) {
                const playerName = (p.firstName + ' ' + p.lastName).trim().toLowerCase();
                if (playerName === participant) {
                    const lastReg = p.priorRegistrations?.at(-1);
                    if (lastReg?.registrationId) return String(lastReg.registrationId);
                }
            }
        } catch { /* ignore */ }
        return '';
    }
}
