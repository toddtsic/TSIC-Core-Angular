import { Injectable, inject, signal, computed } from '@angular/core';
import { TeamRegistrationService } from './team-registration.service';
import { TeamInsuranceStateService } from './team-insurance-state.service';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import type { TeamInsurancePurchaseRequestDto, TeamInsurancePurchaseResponseDto, CreditCardInfo, PreSubmitTeamInsuranceDto } from '@core/api';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { firstValueFrom } from 'rxjs';
import { ViDarkModeService } from '../../shared/services/vi-dark-mode.service';
import type { VIOfferData, VIWidgetState, VIWindowExtension, VIWidgetInstance, VIQuoteObject } from '../../shared/types/wizard.types';
import { formatHttpError } from '../../shared/utils/error-utils';

/**
 * Team insurance service - manages Vertical Insure widget integration for teams.
 * Parallel to player insurance service but uses team-specific endpoints.
 */
@Injectable({ providedIn: 'root' })
export class TeamInsuranceService {
    private readonly teamReg = inject(TeamRegistrationService);
    private readonly insuranceState = inject(TeamInsuranceStateService);
    private readonly http = inject(HttpClient);
    private readonly toast = inject(ToastService);
    private readonly auth = inject(AuthService);
    private readonly viDarkMode = inject(ViDarkModeService);

    private readonly _quotes = signal<VIQuoteObject[]>([]);
    private readonly _hasUserResponse = signal(false);
    private readonly _error = signal<string | null>(null);
    private readonly _widgetInitialized = signal(false);
    private readonly purchasing = signal(false);

    readonly quotes = this._quotes.asReadonly();
    readonly hasUserResponse = this._hasUserResponse.asReadonly();
    readonly error = this._error.asReadonly();
    readonly widgetInitialized = this._widgetInitialized.asReadonly();

    offerEnabled = computed(() => this.insuranceState.offerTeamRegSaver());
    consented = computed(() => this.insuranceState.verticalInsureConfirmed());
    declined = computed(() => this.insuranceState.verticalInsureDeclined());

    /**
     * Initialize VerticalInsure widget for team insurance.
     * @param hostSelector CSS selector for widget mount point (e.g., '#dVITeamOffer')
     * @param offerData PreSubmitTeamInsuranceDto.TeamObject from backend
     */
    initWidget(hostSelector: string, offerData: VIOfferData): void {
        if (this.widgetInitialized()) return;
        const viWindow = globalThis as unknown as VIWindowExtension;
        if (!viWindow.VerticalInsure) {
            this._error.set('VerticalInsure script missing');
            return;
        }
        try {
            // Inject computed dark-mode colors into VI's theme (for iframe compatibility)
            this.viDarkMode.injectDarkModeColors(offerData);

            const instance: VIWidgetInstance = new viWindow.VerticalInsure!(
                hostSelector,
                offerData,
                (st: VIWidgetState) => {
                    instance.validate().then((valid: boolean) => {
                        this._hasUserResponse.set(valid);
                        const quotes = st?.quotes || [];
                        this._quotes.set(quotes);
                        this._error.set(null);
                        this._widgetInitialized.set(true);
                        // Apply dark-mode styling after widget renders
                        this.viDarkMode.applyViDarkMode(hostSelector);
                        // Map widget state to decision signals
                        if (valid) {
                            if (quotes.length > 0) {
                                this.insuranceState.confirmVerticalInsurePurchase(quotes);
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
                        // Re-apply dark-mode on state changes (user interaction)
                        this.viDarkMode.applyViDarkMode(hostSelector);
                        // Update decision on subsequent changes
                        if (valid) {
                            if (quotes.length > 0) {
                                this.insuranceState.confirmVerticalInsurePurchase(quotes);
                            } else {
                                this.insuranceState.declineVerticalInsurePurchase();
                            }
                        }
                    });
                }
            );
        } catch (e: unknown) {
            console.error('VerticalInsure init error', e);
            this._error.set('VerticalInsure initialization failed');
        }
    }

    /**
     * Calculate total premium across all quotes (in dollars).
     */
    premiumTotal(): number {
        return this.quotes().reduce((sum, q) => sum + Number(q?.total || 0), 0) / 100;
    }

    /**
     * Get team names covered by quotes.
     */
    quotedTeams(): string[] {
        return this.quotes().flatMap(q => {
            const teams = q?.policy_attributes?.teams || [];
            return teams.map((t: Record<string, unknown>) => String(t?.['team_name'] || '')).filter(Boolean);
        });
    }

    /**
     * Fetch team insurance offer from backend.
     * Backend derives jobId and clubRepRegId from JWT token.
     */
    async fetchTeamInsuranceOffer(): Promise<PreSubmitTeamInsuranceDto | null> {
        try {
            this.insuranceState.setVerticalInsureOffer({ loading: true, data: null, error: null });
            const url = `${environment.apiUrl}/insurance/team/pre-submit`;
            const result = await firstValueFrom(this.http.get<PreSubmitTeamInsuranceDto>(url));

            if (result.available && result.teamObject) {
                this.insuranceState.setVerticalInsureOffer({ loading: false, data: result.teamObject, error: null });
                return result;
            } else {
                this.insuranceState.setVerticalInsureOffer({ loading: false, data: null, error: result.error || 'Not available' });
                return null;
            }
        } catch (err: unknown) {
            this.insuranceState.setVerticalInsureOffer({ loading: false, data: null, error: formatHttpError(err) });
            return null;
        }
    }

    /**
     * Purchase team insurance policies.
     * Backend derives jobId and clubRepRegId from JWT token.
     * @param teamIds Team IDs to insure
     * @param quoteIds Quote IDs from VI widget
     * @param card Credit card info
     */
    async purchaseTeamInsurance(
        teamIds: string[],
        quoteIds: string[],
        card: CreditCardInfo
    ): Promise<{ success: boolean; policies?: Record<string, string>; error?: string }> {
        if (this.purchasing()) {
            return { success: false, error: 'Purchase already in progress' };
        }

        try {
            this.purchasing.set(true);
            const request: TeamInsurancePurchaseRequestDto = {
                teamIds,
                quoteIds,
                creditCard: card
            };

            const url = `${environment.apiUrl}/insurance/team/purchase`;
            const response = await firstValueFrom(
                this.http.post<TeamInsurancePurchaseResponseDto>(url, request)
            );

            if (response.success && response.policies) {
                this.toast.show('Team insurance policies purchased successfully', 'success');
                return { success: true, policies: response.policies };
            } else {
                this.toast.show(response.error || 'Insurance purchase failed', 'danger');
                return { success: false, error: response.error || undefined };
            }
        } catch (err: unknown) {
            const message = formatHttpError(err);
            this.toast.show(message, 'danger');
            return { success: false, error: message };
        } finally {
            this.purchasing.set(false);
        }
    }

    reset(): void {
        this._quotes.set([]);
        this._hasUserResponse.set(false);
        this._error.set(null);
        this._widgetInitialized.set(false);
        this.purchasing.set(false);
        this.viDarkMode.disconnect();
    }
}
