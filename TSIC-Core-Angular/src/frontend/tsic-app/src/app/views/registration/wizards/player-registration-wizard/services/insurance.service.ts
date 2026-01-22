import { Injectable, inject, signal, computed } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import { InsuranceStateService } from './insurance-state.service';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '@environments/environment';
import type { InsurancePurchaseRequestDto, InsurancePurchaseResponseDto, CreditCardInfo } from '@core/api';
import { ToastService } from '@shared-ui/toast.service';

@Injectable({ providedIn: 'root' })
export class InsuranceService {
    private readonly state = inject(RegistrationWizardService);
    private readonly insuranceState = inject(InsuranceStateService);
    private readonly http = inject(HttpClient);
    private readonly toast = inject(ToastService);
    quotes = signal<any[]>([]);
    hasUserResponse = signal(false);
    error = signal<string | null>(null);
    widgetInitialized = signal(false);
    private readonly purchasing = signal(false);
    private viMutationObserver: MutationObserver | null = null;

    offerEnabled = computed(() => this.insuranceState.offerPlayerRegSaver());
    consented = computed(() => this.insuranceState.verticalInsureConfirmed());
    declined = computed(() => this.insuranceState.verticalInsureDeclined());

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
                        // Map widget state to decision signals: quotes -> confirmed, none -> declined
                        if (valid) {
                            if (quotes.length > 0) {
                                this.insuranceState.confirmVerticalInsurePurchase(null, null, quotes);
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
                        // Update decision on subsequent changes as well
                        if (valid) {
                            if (quotes.length > 0) {
                                this.insuranceState.confirmVerticalInsurePurchase(null, null, quotes);
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

    premiumTotal(): number {
        return this.quotes().reduce((c, q) => c + Number(q?.total || 0), 0) / 100;
    }

    quotedPlayers(): string[] {
        return this.quotes().map(q => {
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
        number?: string; expiry?: string; code?: string; firstName?: string; lastName?: string; zip?: string; email?: string; phone?: string; address?: string;
    }): void {
        if (this.purchasing()) return;
        // Prefer explicit VerticalInsure quote identifier; avoid using generic id which may be a registration GUID.
        const quoteIds = this.quotes().map(q => String(q?.quote_id ?? q?.quoteId ?? '')).filter(Boolean);
        const registrationIds = this.quotes().map(q => this.extractRegistrationId(q)).filter(Boolean);
        if (quoteIds.length === 0 || registrationIds.length === 0) {
            console.warn('[InsuranceService] Missing quoteIds or registrationIds before purchase', { quoteCount: quoteIds.length, regCount: registrationIds.length, rawQuotes: this.quotes() });
        }
        if (quoteIds.length === 0) return; // nothing to purchase
        if (registrationIds.length !== quoteIds.length) {
            this.toast.show('Insurance quote / registration mismatch. Please retry.', 'danger', 4000);
            console.warn('[InsuranceService] Mismatch lengths', { quoteIds, registrationIds });
            return;
        }
        if (!card) {
            this.toast.show('Credit card information required for insurance purchase', 'danger', 4000);
            console.warn('[InsuranceService] No credit card provided');
            return;
        }
        const creditCardPayload: CreditCardInfo = {
            number: card.number?.trim() || undefined,
            expiry: this.sanitizeExpiry(card.expiry),
            code: card.code?.trim() || undefined,
            firstName: card.firstName?.trim() || undefined,
            lastName: card.lastName?.trim() || undefined,
            zip: card.zip?.trim() || undefined,
            email: card.email?.trim() || undefined,
            phone: (card.phone || '').replaceAll(/\D+/g, '').slice(0, 15) || undefined,
            address: card.address?.trim() || undefined
        };
        const req: InsurancePurchaseRequestDto = {
            jobId: this.state.jobId(),
            familyUserId: this.state.familyUser()?.familyUserId!,
            registrationIds,
            quoteIds,
            creditCard: creditCardPayload
        };
        this.purchasing.set(true);
        this.http.post<InsurancePurchaseResponseDto>(`${environment.apiUrl}/insurance/purchase`, req)
            .subscribe({
                next: resp => {
                    this.purchasing.set(false);
                    if (resp?.success) {
                        this.toast.show('Processing with Vertical Insurance was SUCCESSFUL', 'success', 3000);
                    } else {
                        this.toast.show('Vertical Insurance processing failed', 'danger', 4000);
                        console.warn('Insurance purchase failed', resp);
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this.purchasing.set(false);
                    this.toast.show('Processing with Vertical Insurance failed', 'danger', 4000);
                    console.warn('Insurance purchase error', err?.error?.message || err.message || err);
                }
            });
    }

    purchaseInsuranceAndFinish(onFinish: (message: string) => void, card?: {
        number?: string; expiry?: string; code?: string; firstName?: string; lastName?: string; zip?: string; email?: string; phone?: string; address?: string;
    }): void {
        if (this.purchasing()) return;
        const quoteIds = this.quotes().map(q => String(q?.quote_id ?? q?.quoteId ?? '')).filter(Boolean);
        const registrationIds = this.quotes().map(q => this.extractRegistrationId(q)).filter(Boolean);
        if (quoteIds.length === 0 || registrationIds.length === 0) {
            console.warn('[InsuranceService] Missing quoteIds or registrationIds before purchaseAndFinish', { quoteCount: quoteIds.length, regCount: registrationIds.length, rawQuotes: this.quotes() });
        }
        if (quoteIds.length === 0) {
            this.toast.show('No insurance quotes to purchase', 'danger', 3000);
            return;
        }
        if (registrationIds.length !== quoteIds.length) {
            this.toast.show('Insurance quote / registration mismatch. Please retry.', 'danger', 4000);
            console.warn('[InsuranceService] Mismatch lengths', { quoteIds, registrationIds });
            return;
        }
        if (!card) {
            this.toast.show('Credit card information required for insurance purchase', 'danger', 4000);
            console.warn('[InsuranceService] No credit card provided');
            return;
        }
        const creditCardPayload: CreditCardInfo = {
            number: card.number?.trim() || undefined,
            expiry: this.sanitizeExpiry(card.expiry),
            code: card.code?.trim() || undefined,
            firstName: card.firstName?.trim() || undefined,
            lastName: card.lastName?.trim() || undefined,
            zip: card.zip?.trim() || undefined,
            email: card.email?.trim() || undefined,
            phone: (card.phone || '').replaceAll(/\D+/g, '').slice(0, 15) || undefined,
            address: card.address?.trim() || undefined
        };
        const req: InsurancePurchaseRequestDto = {
            jobId: this.state.jobId(),
            familyUserId: this.state.familyUser()?.familyUserId!,
            registrationIds,
            quoteIds,
            creditCard: creditCardPayload
        };
        this.purchasing.set(true);
        this.http.post<InsurancePurchaseResponseDto>(`${environment.apiUrl}/insurance/purchase`, req)
            .subscribe({
                next: resp => {
                    this.purchasing.set(false);
                    if (resp?.success) {
                        this.toast.show('Processing with Vertical Insurance was SUCCESSFUL', 'success', 3000);
                        onFinish('Insurance processed via Vertical Insure.');
                    } else {
                        this.toast.show('Vertical Insurance processing failed', 'danger', 4000);
                        console.warn('Insurance purchase failed', resp);
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this.purchasing.set(false);
                    this.toast.show('Processing with Vertical Insurance failed', 'danger', 4000);
                    console.warn('Insurance purchase error', err?.error?.message || err.message || err);
                }
            });
    }
    private extractRegistrationId(q: any): string {
        const idFromMeta = this.extractRegistrationIdFromMeta(q?.metadata);
        if (idFromMeta) return idFromMeta;
        return this.inferRegistrationIdFromParticipant(q);
    }

    private sanitizeExpiry(raw?: string): string | undefined {
        const digits = String(raw || '').replaceAll(/\D+/g, '').slice(0, 4);
        if (digits.length === 3) return ('0' + digits);
        return digits.length === 4 ? digits : undefined;
    }

    private extractRegistrationIdFromMeta(meta: any): string {
        if (!meta) return '';
        const direct = meta.TsicRegistrationId ?? meta.tsicRegistrationId ?? meta.tsic_registration_id;
        if (direct) return String(direct);
        for (const k of Object.keys(meta)) {
            if (/registration.?id/i.test(k)) {
                const v = meta[k];
                if (v) return String(v);
            }
        }
        return '';
    }

    private inferRegistrationIdFromParticipant(q: any): string {
        const fn = q?.policy_attributes?.participant?.first_name?.trim?.() || '';
        const ln = q?.policy_attributes?.participant?.last_name?.trim?.() || '';
        const participant = (fn + ' ' + ln).trim().toLowerCase();
        if (!participant) return '';
        try {
            for (const p of this.state.familyPlayers()) {
                const playerName = (p.firstName + ' ' + p.lastName).trim().toLowerCase();
                if (playerName === participant) {
                    const lastReg = p.priorRegistrations?.at(-1);
                    if (lastReg?.registrationId) return String(lastReg.registrationId);
                }
            }
        } catch { /* ignore */ }
        return '';
    }

    /**
     * Apply and maintain dark-mode styling to VI widget.
     * Targets the host container and its children with CSS variable overrides.
     * Sets up a MutationObserver to reapply styling when VI injects/updates nodes.
     */
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
     * Apply and maintain dark-mode styling to VI widget host container.
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
