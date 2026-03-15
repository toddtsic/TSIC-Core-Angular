import { Component, inject, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { ArbDefensiveService } from './health/services/arb-defensive.service';
import type { ArbSubscriptionInfoDto, ArbUpdateCcRequest, ArbUpdateCcResultDto } from '@core/api';

@Component({
    selector: 'app-arb-update-cc',
    standalone: true,
    imports: [FormsModule, DecimalPipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './arb-update-cc.component.html',
    styleUrl: './arb-update-cc.component.scss'
})
export class ArbUpdateCcComponent implements OnInit {
    private readonly route = inject(ActivatedRoute);
    private readonly arbService = inject(ArbDefensiveService);

    // Subscription info (loaded)
    readonly info = signal<ArbSubscriptionInfoDto | null>(null);
    readonly isLoadingInfo = signal(true);
    readonly loadError = signal<string | null>(null);

    // Form fields
    readonly cardNumber = signal('');
    readonly cardCode = signal('');
    readonly expirationMonth = signal('');
    readonly expirationYear = signal('');
    readonly firstName = signal('');
    readonly lastName = signal('');
    readonly address = signal('');
    readonly zip = signal('');
    readonly email = signal('');

    // Submit state
    readonly isSubmitting = signal(false);
    readonly result = signal<ArbUpdateCcResultDto | null>(null);
    readonly submitError = signal<string | null>(null);

    readonly registrationId = signal('');

    readonly months = [
        '01', '02', '03', '04', '05', '06',
        '07', '08', '09', '10', '11', '12'
    ];

    readonly years: string[] = [];

    constructor() {
        const currentYear = new Date().getFullYear();
        for (let i = 0; i < 10; i++) {
            this.years.push((currentYear + i).toString());
        }
    }

    ngOnInit(): void {
        const regId = this.route.snapshot.paramMap.get('registrationId') ?? '';
        this.registrationId.set(regId);

        if (!regId) {
            this.loadError.set('Missing registration ID.');
            this.isLoadingInfo.set(false);
            return;
        }

        this.arbService.getSubscriptionInfo(regId).subscribe({
            next: data => {
                this.info.set(data);
                this.isLoadingInfo.set(false);
            },
            error: () => {
                this.loadError.set('Could not load subscription information. The link may be invalid or expired.');
                this.isLoadingInfo.set(false);
            }
        });
    }

    onFieldChange(field: string, value: string): void {
        switch (field) {
            case 'cardNumber': this.cardNumber.set(value); break;
            case 'cardCode': this.cardCode.set(value); break;
            case 'expirationMonth': this.expirationMonth.set(value); break;
            case 'expirationYear': this.expirationYear.set(value); break;
            case 'firstName': this.firstName.set(value); break;
            case 'lastName': this.lastName.set(value); break;
            case 'address': this.address.set(value); break;
            case 'zip': this.zip.set(value); break;
            case 'email': this.email.set(value); break;
        }
    }

    canSubmit(): boolean {
        return !!(
            this.cardNumber() &&
            this.cardCode() &&
            this.expirationMonth() &&
            this.expirationYear() &&
            this.firstName() &&
            this.lastName() &&
            this.address() &&
            this.zip() &&
            this.email() &&
            !this.isSubmitting()
        );
    }

    submit(): void {
        if (!this.canSubmit()) return;
        const subInfo = this.info();
        if (!subInfo) return;

        this.isSubmitting.set(true);
        this.submitError.set(null);
        this.result.set(null);

        const request: ArbUpdateCcRequest = {
            registrationId: this.registrationId(),
            subscriptionId: subInfo.subscriptionId,
            cardNumber: this.cardNumber(),
            cardCode: this.cardCode(),
            expirationMonth: this.expirationMonth(),
            expirationYear: this.expirationYear(),
            firstName: this.firstName(),
            lastName: this.lastName(),
            address: this.address(),
            zip: this.zip(),
            email: this.email(),
            balanceDue: subInfo.balanceDue
        };

        this.arbService.updateCreditCard(request).subscribe({
            next: res => {
                this.result.set(res);
                this.isSubmitting.set(false);
            },
            error: err => {
                this.submitError.set(err?.error?.message || 'Failed to update credit card. Please try again.');
                this.isSubmitting.set(false);
            }
        });
    }
}
