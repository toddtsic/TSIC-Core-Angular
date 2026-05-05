import { Injectable, computed, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '@environments/environment';
import { ToastService } from '@shared-ui/toast.service';
import { BaseInsuranceService } from '@views/registration/shared/services/vi-widget-base.service';
import { formatHttpError } from '../../shared/utils/error-utils';
import { TeamInsuranceStateService } from './team-insurance-state.service';
import type {
    CreditCardInfo,
    PreSubmitTeamInsuranceDto,
    TeamInsurancePurchaseRequestDto,
    TeamInsurancePurchaseResponseDto,
} from '@core/api';

/**
 * Team-side insurance service. Inherits the entire VerticalInsure widget
 * integration from `BaseInsuranceService`; this class only owns the team
 * purchase endpoint, the team-specific quote display, and convenience reads
 * off `TeamInsuranceStateService`.
 */
@Injectable({ providedIn: 'root' })
export class TeamInsuranceService extends BaseInsuranceService {
    private readonly insuranceState = inject(TeamInsuranceStateService);
    private readonly http = inject(HttpClient);
    private readonly toast = inject(ToastService);

    readonly offerEnabled = computed(() => this.insuranceState.offerTeamRegSaver());
    readonly consented = computed(() => this.insuranceState.verticalInsureConfirmed());
    readonly declined = computed(() => this.insuranceState.verticalInsureDeclined());

    /** Team display: just team names. Premium is shown as a single total in
     *  the charge-confirm modal — no per-team breakdown (a single VI quote
     *  can cover multiple teams). */
    quotedTeams(): string[] {
        return this._quotes().flatMap(q => {
            const teams = q?.policy_attributes?.teams ?? [];
            return teams
                .map((t: Record<string, unknown>) => String(t?.['team_name'] ?? ''))
                .filter(Boolean);
        });
    }

    /** Fetch the team insurance offer from the backend. Backend derives jobId
     *  and clubRepRegId from the JWT. */
    async fetchTeamInsuranceOffer(): Promise<PreSubmitTeamInsuranceDto | null> {
        try {
            this.insuranceState.setVerticalInsureOffer({ loading: true, data: null, error: null });
            const url = `${environment.apiUrl}/insurance/team/pre-submit`;
            const result = await firstValueFrom(this.http.get<PreSubmitTeamInsuranceDto>(url));

            if (result.available && result.teamObject) {
                this.insuranceState.setVerticalInsureOffer({ loading: false, data: result.teamObject, error: null });
                return result;
            }
            this.insuranceState.setVerticalInsureOffer({ loading: false, data: null, error: result.error || 'Not available' });
            return null;
        } catch (err: unknown) {
            this.insuranceState.setVerticalInsureOffer({ loading: false, data: null, error: formatHttpError(err) });
            return null;
        }
    }

    /** Purchase team insurance policies. Backend derives jobId + clubRepRegId
     *  from the JWT; teamIds + quoteIds are derived from the widget's quotes
     *  (the rep may have accepted coverage on a subset of paid teams). */
    async purchaseTeamInsurance(
        card: CreditCardInfo,
    ): Promise<{ success: boolean; policies?: Record<string, string>; error?: string }> {
        if (this.purchasing()) {
            return { success: false, error: 'Purchase already in progress' };
        }

        // Derive teamIds + quoteIds from quotes so they're aligned by construction.
        // VI's quote metadata carries `tsic_teamid` which is the canonical link
        // back to TSIC's Teams.TeamId for each insured team.
        const quotes = this._quotes();
        const pairs = quotes
            .map(q => {
                const meta = q?.metadata as Record<string, unknown> | undefined;
                const teamId = meta ? String(meta['tsic_teamid'] ?? '') : '';
                const quoteId = String(q?.quote_id ?? q?.quoteId ?? '');
                return { teamId, quoteId };
            })
            .filter(p => p.teamId && p.quoteId);

        if (pairs.length === 0) {
            return { success: false, error: 'No insurable teams in current quotes.' };
        }

        try {
            this.purchasing.set(true);
            const request: TeamInsurancePurchaseRequestDto = {
                teamIds: pairs.map(p => p.teamId),
                quoteIds: pairs.map(p => p.quoteId),
                creditCard: card,
            };

            const url = `${environment.apiUrl}/insurance/team/purchase`;
            const response = await firstValueFrom(
                this.http.post<TeamInsurancePurchaseResponseDto>(url, request),
            );

            if (response.success && response.policies) {
                this.toast.show('Team insurance policies purchased successfully', 'success');
                return { success: true, policies: response.policies };
            }
            this.toast.show(response.error || 'Insurance purchase failed', 'danger');
            return { success: false, error: response.error || undefined };
        } catch (err: unknown) {
            const message = formatHttpError(err);
            this.toast.show(message, 'danger');
            return { success: false, error: message };
        } finally {
            this.purchasing.set(false);
        }
    }
}
