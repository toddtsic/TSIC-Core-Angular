import { Injectable, inject, signal, computed } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import { InsuranceStateService } from './insurance-state.service';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import type { InsurancePurchaseRequestDto } from '../../../core/api/models/InsurancePurchaseRequestDto';
import type { InsurancePurchaseResponseDto } from '../../../core/api/models/InsurancePurchaseResponseDto';
import { ToastService } from '../../../shared/toast.service';

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
            const instance = new (globalThis as any).VerticalInsure(
                hostSelector,
                offerData,
                (st: any) => {
                    instance.validate().then((valid: boolean) => {
                        this.hasUserResponse.set(valid);
                        this.quotes.set(st?.quotes || []);
                        this.error.set(null);
                        this.widgetInitialized.set(true);
                    });
                },
                () => {
                    instance.validate().then((valid: boolean) => {
                        this.hasUserResponse.set(valid);
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

    purchaseInsurance(): void {
        if (this.purchasing()) return;
        const quoteIds: string[] = this.quotes().map(q => String(q?.id ?? q?.quote_id ?? '')).filter(s => !!s);
        const registrationIds: string[] = this.quotes().map(q => String(q?.metadata?.TsicRegistrationId ?? q?.metadata?.tsicRegistrationId ?? '')).filter(s => !!s);
        if (quoteIds.length === 0) return; // nothing to purchase
        const req: InsurancePurchaseRequestDto = {
            jobId: this.state.jobId(),
            familyUserId: this.state.familyUser()?.familyUserId!,
            registrationIds,
            quoteIds
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

    purchaseInsuranceAndFinish(onFinish: (message: string) => void): void {
        if (this.purchasing()) return;
        const quoteIds: string[] = this.quotes().map(q => String(q?.id ?? q?.quote_id ?? '')).filter(s => !!s);
        const registrationIds: string[] = this.quotes().map(q => String(q?.metadata?.TsicRegistrationId ?? q?.metadata?.tsicRegistrationId ?? '')).filter(s => !!s);
        if (quoteIds.length === 0) {
            this.toast.show('No insurance quotes to purchase', 'danger', 3000);
            return;
        }
        const req: InsurancePurchaseRequestDto = {
            jobId: this.state.jobId(),
            familyUserId: this.state.familyUser()?.familyUserId!,
            registrationIds,
            quoteIds
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
}
