import { Injectable, inject, signal, computed } from '@angular/core';
import { TeamRegistrationService } from './team-registration.service';
import { TeamInsuranceStateService } from './team-insurance-state.service';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import type { TeamInsurancePurchaseRequestDto, TeamInsurancePurchaseResponseDto, CreditCardInfo, PreSubmitTeamInsuranceDto } from '@core/api';
import { ToastService } from '@shared-ui/toast.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { firstValueFrom } from 'rxjs';

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

    quotes = signal<any[]>([]);
    hasUserResponse = signal(false);
    error = signal<string | null>(null);
    widgetInitialized = signal(false);
    private readonly purchasing = signal(false);
    private viMutationObserver: MutationObserver | null = null;

    offerEnabled = computed(() => this.insuranceState.offerTeamRegSaver());
    consented = computed(() => this.insuranceState.verticalInsureConfirmed());
    declined = computed(() => this.insuranceState.verticalInsureDeclined());

    /**
     * Initialize VerticalInsure widget for team insurance.
     * @param hostSelector CSS selector for widget mount point (e.g., '#dVITeamOffer')
     * @param offerData PreSubmitTeamInsuranceDto.TeamObject from backend
     */
    initWidget(hostSelector: string, offerData: any): void {
        if (this.widgetInitialized()) return;
        if (!(globalThis as any).VerticalInsure) {
            this.error.set('VerticalInsure script missing');
            return;
        }
        try {
            // Inject computed dark-mode colors into VI's theme (for iframe compatibility)
            this.injectDarkModeColors(offerData);

            const instance = new (globalThis as any).VerticalInsure(
                hostSelector,
                offerData,
                (st: any) => {
                    instance.validate().then((valid: boolean) => {
                        this.hasUserResponse.set(valid);
                        const quotes = st?.quotes || [];
                        this.quotes.set(quotes);
                        this.error.set(null);
                        this.widgetInitialized.set(true);
                        // Apply dark-mode styling after widget renders
                        this.applyViDarkMode(hostSelector);
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
                (st: any) => {
                    instance.validate().then((valid: boolean) => {
                        this.hasUserResponse.set(valid);
                        const quotes = st?.quotes || [];
                        this.quotes.set(quotes);
                        // Re-apply dark-mode on state changes (user interaction)
                        this.applyViDarkMode(hostSelector);
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
        } catch (e) {
            console.error('VerticalInsure init error', e);
            this.error.set('VerticalInsure initialization failed');
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
            return teams.map((t: any) => t?.team_name || '').filter(Boolean);
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
        } catch (err) {
            const error = err instanceof HttpErrorResponse ? err.message : 'Failed to fetch insurance offer';
            this.insuranceState.setVerticalInsureOffer({ loading: false, data: null, error });
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
        } catch (err) {
            const error = err instanceof HttpErrorResponse ? err.message : 'Insurance purchase failed';
            this.toast.show(error, 'danger');
            return { success: false, error };
        } finally {
            this.purchasing.set(false);
        }
    }

    private injectDarkModeColors(offerData: any): void {
        if (!offerData?.theme) return;

        const style = globalThis.window.getComputedStyle(document.documentElement);
        const bgColor = style.getPropertyValue('--bs-body-bg').trim() || '#1c1917';
        const borderColor = style.getPropertyValue('--bs-border-color').trim() || '#57534e';
        const cardBg = style.getPropertyValue('--bs-card-bg').trim() || '#44403c';

        // Replace CSS variables with computed hex values
        offerData.theme.colors = offerData.theme.colors || {};
        offerData.theme.colors.background = bgColor;
        offerData.theme.colors.border = borderColor;
        // Add card background for VI components
        offerData.theme.colors.cardBackground = cardBg;
    }

    /**
     * Apply and maintain dark-mode styling to VI widget.
     * Targets the host container and its children with CSS variable overrides.
     * Sets up a MutationObserver to reapply styling when VI injects/updates nodes.
     */
    private applyViDarkMode(hostSelector: string): void {
        const host = document.querySelector(hostSelector) as HTMLElement;
        if (!host) return;

        // Apply inline styles to host and force dark-mode palette
        host.style.setProperty('background-color', 'var(--bs-body-bg)', 'important');
        host.style.setProperty('color', 'var(--bs-body-color)', 'important');

        // If VI rendered inside an iframe, force its surface to dark by applying a filter.
        const viFrame = host.querySelector('iframe');
        if (viFrame) {
            const bg = globalThis.window.getComputedStyle(document.documentElement).getPropertyValue('--bs-body-bg').trim();
            if (this.isDarkColor(bg)) {
                viFrame.style.setProperty('background-color', bg, 'important');
                viFrame.style.setProperty('border-color', 'var(--bs-border-color)', 'important');
                viFrame.style.setProperty('filter', 'invert(1) hue-rotate(180deg) contrast(0.95)', 'important');
            }
        }

        // Walk the entire subtree and recolor key elements
        this.recolorViSubtree(host);

        // Attach MutationObserver if not already attached
        if (!this.viMutationObserver) {
            this.viMutationObserver = new MutationObserver(() => {
                this.recolorViSubtree(host);
            });
            this.viMutationObserver.observe(host, {
                childList: true,
                subtree: true,
                attributes: true,
                attributeFilter: ['style', 'class']
            });
        }
    }

    /**
     * Recursively walk VI widget subtree and apply dark-mode colors to text/backgrounds.
     */
    private recolorViSubtree(root: HTMLElement): void {
        const walker = document.createTreeWalker(
            root,
            NodeFilter.SHOW_ELEMENT,
            null
        );

        let node: HTMLElement | null;
        while ((node = walker.nextNode() as HTMLElement)) {
            const bgColor = globalThis.window.getComputedStyle(node).backgroundColor;
            // If background is white or very light, override to card bg
            if (bgColor === 'rgb(255, 255, 255)' || bgColor === '#fff' || bgColor === '#ffffff') {
                node.style.setProperty('background-color', 'var(--bs-card-bg)', 'important');
            }
            // If text is dark on a now-dark background, flip to light
            const textColor = globalThis.window.getComputedStyle(node).color;
            if (textColor === 'rgb(0, 0, 0)' || textColor === '#000' || textColor === '#000000') {
                node.style.setProperty('color', 'var(--bs-body-color)', 'important');
            }
        }
    }

    reset(): void {
        this.quotes.set([]);
        this.hasUserResponse.set(false);
        this.error.set(null);
        this.widgetInitialized.set(false);
        this.purchasing.set(false);
        this.viMutationObserver?.disconnect();
        this.viMutationObserver = null;
    }

    private isDarkColor(color: string): boolean {
        const hexMatch = /^#([0-9a-fA-F]{6})$/.exec(color);
        if (hexMatch) {
            const num = Number.parseInt(hexMatch[1], 16);
            const r = (num >> 16) & 0xff;
            const g = (num >> 8) & 0xff;
            const b = num & 0xff;
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) < 140;
        }
        const rgbMatch = /rgb\((\d+),\s*(\d+),\s*(\d+)\)/i.exec(color);
        if (rgbMatch) {
            const r = Number(rgbMatch[1]);
            const g = Number(rgbMatch[2]);
            const b = Number(rgbMatch[3]);
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) < 140;
        }
        return false;
    }
}
