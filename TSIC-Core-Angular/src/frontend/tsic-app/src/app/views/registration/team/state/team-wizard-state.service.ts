import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ClubRepStateService } from './club-rep-state.service';
import { TeamRegistrationService } from '@views/registration/team/services/team-registration.service';
import { TeamPaymentService } from '@views/registration/team/services/team-payment.service';
import { TeamPaymentStateService } from '@views/registration/team/services/team-payment-state.service';
import { TeamInsuranceStateService } from '@views/registration/team/services/team-insurance-state.service';
import { TeamInsuranceService } from '@views/registration/team/services/team-insurance.service';
import { JobService } from '@infrastructure/services/job.service';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';
import { environment } from '@environments/environment';
import type { UserContactInfoDto, JobPulseDto } from '@core/api';

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
    private readonly http = inject(HttpClient);

    // ── Job context ────────────────────────────────────────────────────
    private readonly _jobPath = signal('');
    readonly jobPath = this._jobPath.asReadonly();

    private readonly _hasActiveDiscountCodes = signal(false);
    readonly hasActiveDiscountCodes = this._hasActiveDiscountCodes.asReadonly();

    private readonly _fullPaymentRequired = signal(true);
    readonly fullPaymentRequired = this._fullPaymentRequired.asReadonly();

    private readonly _clubRepContact = signal<UserContactInfoDto | null>(null);
    readonly clubRepContact = this._clubRepContact.asReadonly();

    private readonly _refundPolicyHtml = signal<string | null>(null);
    readonly refundPolicyHtml = this._refundPolicyHtml.asReadonly();

    private readonly _waiverAccepted = signal(false);
    readonly waiverAccepted = this._waiverAccepted.asReadonly();

    /**
     * Job pulse — exposes team-registration capability flags:
     *   teamRegistrationOpen, clubRepAllowAdd, clubRepAllowEdit, clubRepAllowDelete.
     * Null until the pulse fetch completes; treat null as "not yet loaded" (err on
     * the conservative side and hide mutating controls).
     */
    private readonly _pulse = signal<JobPulseDto | null>(null);
    readonly pulse = this._pulse.asReadonly();

    /** Can the ClubRep register a library team for this event? */
    readonly canRegisterTeam = computed(() => {
        const p = this._pulse();
        return !!p && p.teamRegistrationOpen && p.clubRepAllowAdd;
    });

    /** Can the ClubRep unregister (drop) a team from this event? */
    readonly canRemoveTeam = computed(() => {
        const p = this._pulse();
        return !!p && p.teamRegistrationOpen && p.clubRepAllowDelete;
    });

    /** True when the job has a refund policy configured */
    hasRefundPolicy(): boolean { return !!this._refundPolicyHtml()?.trim(); }

    setJobPath(v: string): void { this._jobPath.set(v); }
    setHasActiveDiscountCodes(v: boolean): void { this._hasActiveDiscountCodes.set(v); }
    setFullPaymentRequired(v: boolean): void { this._fullPaymentRequired.set(v); }
    setClubRepContact(v: UserContactInfoDto | null): void { this._clubRepContact.set(v); }
    setRefundPolicyHtml(v: string | null): void { this._refundPolicyHtml.set(v); }
    setWaiverAccepted(v: boolean): void { this._waiverAccepted.set(v); }

    // ── Initialize ─────────────────────────────────────────────────────
    initialize(jobPath: string): void {
        this._jobPath.set(jobPath);
        this.clubRep.initFromPreferences();
        this.loadMetadata(jobPath);
        this.loadPulse(jobPath);
    }

    private loadPulse(jobPath: string): void {
        this.http.get<JobPulseDto>(`${environment.apiUrl}/jobs/${jobPath}/pulse`)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: p => this._pulse.set(p),
                error: () => this._pulse.set(null),
            });
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
                    this._refundPolicyHtml.set(job.playerRegRefundPolicy ?? null);
                },
                error: () => {
                    // Interceptor safety net shows toast; also set inline error for the component.
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
        this._fullPaymentRequired.set(true);
        this._clubRepContact.set(null);
        this._refundPolicyHtml.set(null);
        this._waiverAccepted.set(false);
        this._pulse.set(null);
        this.clubRep.reset();
        this.teamPayment.reset();
        this.teamPaymentState.reset();
        this.teamInsuranceState.reset();
        this.teamInsurance.reset();
    }
}
