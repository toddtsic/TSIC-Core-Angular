import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ClubRepStateService } from './club-rep-state.service';
import { TeamRegistrationService } from '@views/registration/wizards/team-registration-wizard/services/team-registration.service';
import { TeamPaymentService } from '@views/registration/wizards/team-registration-wizard/services/team-payment.service';
import { TeamPaymentStateService } from '@views/registration/wizards/team-registration-wizard/services/team-payment-state.service';
import { TeamInsuranceStateService } from '@views/registration/wizards/team-registration-wizard/services/team-insurance-state.service';
import { TeamInsuranceService } from '@views/registration/wizards/team-registration-wizard/services/team-insurance.service';
import { JobService } from '@infrastructure/services/job.service';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';

/**
 * Team Wizard State Service — THIN ORCHESTRATOR.
 *
 * Coordinates across ClubRepStateService and existing team services.
 * Does not own business logic — just orchestrates cross-cutting operations.
 */
@Injectable({ providedIn: 'root' })
export class TeamWizardStateService {
    private readonly destroyRef = inject(DestroyRef);

    readonly clubRep = inject(ClubRepStateService);
    readonly teamReg = inject(TeamRegistrationService);
    readonly teamPayment = inject(TeamPaymentService);
    readonly teamPaymentState = inject(TeamPaymentStateService);
    readonly teamInsuranceState = inject(TeamInsuranceStateService);
    readonly teamInsurance = inject(TeamInsuranceService);
    private readonly jobService = inject(JobService);
    private readonly fieldData = inject(FormFieldDataService);

    // ── Job context ────────────────────────────────────────────────────
    private readonly _jobPath = signal('');
    readonly jobPath = this._jobPath.asReadonly();

    private readonly _hasActiveDiscountCodes = signal(false);
    readonly hasActiveDiscountCodes = this._hasActiveDiscountCodes.asReadonly();

    setJobPath(v: string): void { this._jobPath.set(v); }
    setHasActiveDiscountCodes(v: boolean): void { this._hasActiveDiscountCodes.set(v); }

    // ── Initialize ─────────────────────────────────────────────────────
    initialize(jobPath: string): void {
        this._jobPath.set(jobPath);
        this.clubRep.initFromPreferences();
        this.loadMetadata(jobPath);
    }

    private loadMetadata(jobPath: string): void {
        if (!jobPath) {
            this.clubRep.setMetadataError('Invalid job path. Please refresh the page.');
            return;
        }
        this.clubRep.setMetadataError(null);
        this.jobService.fetchJobMetadata(jobPath)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: job => {
                    this.fieldData.setJobOptions(job.jsonOptions);
                },
                error: (err: unknown) => {
                    console.error('[TeamWizard] Failed to load job metadata:', err);
                    this.clubRep.setMetadataError('Failed to load registration information. Please try again.');
                },
            });
    }

    // ── Rep switch reset ───────────────────────────────────────────────
    resetForRepSwitch(): void {
        this.teamPayment.reset();
        this.teamPaymentState.reset();
        this.teamInsuranceState.reset();
        this.teamInsurance.reset();
    }

    // ── Full reset ─────────────────────────────────────────────────────
    reset(): void {
        this._jobPath.set('');
        this._hasActiveDiscountCodes.set(false);
        this.clubRep.reset();
        this.teamPayment.reset();
        this.teamPaymentState.reset();
        this.teamInsuranceState.reset();
        this.teamInsurance.reset();
    }
}
