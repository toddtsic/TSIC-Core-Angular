import { Injectable, computed, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import { ToastService } from '@shared-ui/toast.service';
import { BaseInsuranceService } from '@views/registration/shared/services/vi-widget-base.service';
import { sanitizeExpiry, sanitizePhone } from '@views/registration/shared/services/credit-card-utils';
import { InsuranceStateV2Service } from './insurance-state-v2.service';
import { JobContextService } from './job-context.service';
import { FamilyPlayersService } from './family-players.service';
import type { InsurancePurchaseRequestDto, InsurancePurchaseResponseDto, CreditCardInfo } from '@core/api';
import type { VIQuoteObject } from '@views/registration/shared/types/wizard.types';

/** Back-compat alias for the player flow's quote shape — structurally a subset
 *  of the unified VIQuoteObject used by the base service. */
export type VerticalInsureQuote = VIQuoteObject;

/**
 * Player-side insurance service. Inherits the entire VerticalInsure widget
 * integration from `BaseInsuranceService`; this class only owns the player
 * purchase endpoint, the player-specific quote display, and the registration-id
 * extraction needed to map quotes back to TSIC registrations.
 */
@Injectable({ providedIn: 'root' })
export class InsuranceV2Service extends BaseInsuranceService {
    private readonly jobCtx = inject(JobContextService);
    private readonly fp = inject(FamilyPlayersService);
    private readonly insuranceState = inject(InsuranceStateV2Service);
    private readonly http = inject(HttpClient);
    private readonly toast = inject(ToastService);

    readonly offerEnabled = computed(() => this.insuranceState.offerPlayerRegSaver());
    readonly consented = computed(() => this.insuranceState.verticalInsureConfirmed());
    readonly declined = computed(() => this.insuranceState.verticalInsureDeclined());

    /** Player display: name plus the per-player premium in parentheses (each
     *  VI quote covers one player). */
    quotedPlayers(): string[] {
        return this._quotes().map(q => {
            const fn = q?.policy_attributes?.participant?.first_name?.trim?.() || '';
            const ln = q?.policy_attributes?.participant?.last_name?.trim?.() || '';
            const name = `${fn} ${ln}`.trim();
            if (!name) return '';
            const totalCents = Number(q?.total ?? 0);
            const suffix = totalCents > 0 ? ` ($${(totalCents / 100).toFixed(2)})` : '';
            return name + suffix;
        }).filter(Boolean);
    }

    /** Purchase player insurance policies. Fire-and-forget callback style for
     *  parity with the legacy player payment flow — the wizard advances on a
     *  successful TSIC charge regardless of VI's outcome, and this just
     *  delivers a status message via `onComplete`. */
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
                error: (_err: HttpErrorResponse) => {
                    this.purchasing.set(false);
                    this.toast.show('Processing with Vertical Insurance failed', 'danger', 4000);
                    onComplete?.('Insurance processing failed. Registration is still complete.');
                },
            });
    }

    private extractRegistrationId(q: VIQuoteObject): string {
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

    private inferRegistrationIdFromParticipant(q: VIQuoteObject): string {
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
