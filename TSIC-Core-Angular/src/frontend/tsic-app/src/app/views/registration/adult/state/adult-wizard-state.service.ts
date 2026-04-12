import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { skipErrorToast } from '@app/infrastructure/interceptors/http-error-context';
import { AuthService } from '@infrastructure/services/auth.service';
import {
    AdultRegistrationService,
    type AdultRegJobInfoResponse,
    type AdultRegFormResponse,
    type AdultRoleOption,
    type AdultRegField,
    type AdultWaiverDto,
    type PreSubmitAdultResponse,
    type AdultFeeBreakdown,
    type AdultValidationError,
    type CreditCardValues,
} from '@infrastructure/services/adult-registration.service';
import { formatHttpError } from '../../shared/utils/error-utils';

// ── Local form-only types ────────────────────────────────────────
export type FormFieldValue = string | number | boolean | null;

/**
 * Adult wizard v2 state service.
 *
 * Follows signal-based pattern:
 * - Private signals + public readonly + controlled mutators.
 * - API calls encapsulated with error signals.
 * - PreSubmit + conditional payment flow.
 */
@Injectable({ providedIn: 'root' })
export class AdultWizardStateService {
    private readonly api = inject(AdultRegistrationService);
    private readonly auth = inject(AuthService);
    private readonly destroyRef = inject(DestroyRef);

    // ── Mode ──────────────────────────────────────────────────────
    private readonly _mode = signal<'create' | 'login'>('create');
    readonly mode = this._mode.asReadonly();

    // ── Account credentials (create mode) ─────────────────────────
    private readonly _username = signal('');
    private readonly _password = signal('');
    private readonly _firstName = signal('');
    private readonly _lastName = signal('');
    private readonly _email = signal('');
    private readonly _phone = signal('');
    readonly username = this._username.asReadonly();
    readonly password = this._password.asReadonly();
    readonly firstName = this._firstName.asReadonly();
    readonly lastName = this._lastName.asReadonly();
    readonly email = this._email.asReadonly();
    readonly phone = this._phone.asReadonly();

    // ── Job info ──────────────────────────────────────────────────
    private readonly _jobInfo = signal<AdultRegJobInfoResponse | null>(null);
    private readonly _jobLoading = signal(false);
    private readonly _jobError = signal<string | null>(null);
    readonly jobInfo = this._jobInfo.asReadonly();
    readonly jobLoading = this._jobLoading.asReadonly();
    readonly jobError = this._jobError.asReadonly();

    // ── Role selection ────────────────────────────────────────────
    private readonly _selectedRole = signal<AdultRoleOption | null>(null);
    readonly selectedRole = this._selectedRole.asReadonly();

    // ── Form schema (loaded per role) ─────────────────────────────
    private readonly _formSchema = signal<AdultRegFormResponse | null>(null);
    private readonly _schemaLoading = signal(false);
    private readonly _schemaError = signal<string | null>(null);
    readonly formSchema = this._formSchema.asReadonly();
    readonly schemaLoading = this._schemaLoading.asReadonly();
    readonly schemaError = this._schemaError.asReadonly();

    // ── Dynamic form values ───────────────────────────────────────
    private readonly _formValues = signal<Record<string, FormFieldValue>>({});
    readonly formValues = this._formValues.asReadonly();

    // ── Waiver acceptance ─────────────────────────────────────────
    private readonly _waiverAcceptance = signal<Record<string, boolean>>({});
    readonly waiverAcceptance = this._waiverAcceptance.asReadonly();

    // ── PreSubmit state ───────────────────────────────────────────
    private readonly _preSubmitResponse = signal<PreSubmitAdultResponse | null>(null);
    private readonly _preSubmitting = signal(false);
    private readonly _preSubmitError = signal<string | null>(null);
    readonly preSubmitResponse = this._preSubmitResponse.asReadonly();
    readonly preSubmitting = this._preSubmitting.asReadonly();
    readonly preSubmitError = this._preSubmitError.asReadonly();

    // ── Fee derived signals ───────────────────────────────────────
    readonly fees = computed<AdultFeeBreakdown | null>(() => this._preSubmitResponse()?.fees ?? null);
    readonly hasFees = computed(() => (this.fees()?.owedTotal ?? 0) > 0);
    readonly validationErrors = computed<AdultValidationError[]>(() => this._preSubmitResponse()?.validationErrors ?? []);

    // ── Payment state ─────────────────────────────────────────────
    private readonly _paymentMethod = signal<'CC' | 'Check'>('CC');
    private readonly _paymentSubmitting = signal(false);
    private readonly _paymentError = signal<string | null>(null);
    private readonly _paymentSuccess = signal(false);
    readonly paymentMethod = this._paymentMethod.asReadonly();
    readonly paymentSubmitting = this._paymentSubmitting.asReadonly();
    readonly paymentError = this._paymentError.asReadonly();
    readonly paymentSuccess = this._paymentSuccess.asReadonly();

    // ── Submission state ──────────────────────────────────────────
    private readonly _submitting = signal(false);
    private readonly _submitError = signal<string | null>(null);
    private readonly _submitSuccess = signal(false);
    private readonly _registrationId = signal<string | null>(null);
    private readonly _confirmationHtml = signal<string | null>(null);
    private readonly _confirmationLoading = signal(false);
    readonly submitting = this._submitting.asReadonly();
    readonly submitError = this._submitError.asReadonly();
    readonly submitSuccess = this._submitSuccess.asReadonly();
    readonly registrationId = this._registrationId.asReadonly();
    readonly confirmationHtml = this._confirmationHtml.asReadonly();
    readonly confirmationLoading = this._confirmationLoading.asReadonly();

    // ── Derived ───────────────────────────────────────────────────
    readonly availableRoles = computed(() => this._jobInfo()?.availableRoles ?? []);

    readonly hasValidCredentials = computed(() =>
        this._username().trim().length >= 6
        && this._password().trim().length >= 6
        && this._firstName().trim().length > 0
        && this._lastName().trim().length > 0
        && this._email().trim().length > 0
        && this._phone().trim().length > 0,
    );

    readonly hasSelectedRole = computed(() => this._selectedRole() !== null);

    readonly formFields = computed<AdultRegField[]>(() => this._formSchema()?.fields ?? []);
    readonly waivers = computed<AdultWaiverDto[]>(() => this._formSchema()?.waivers ?? []);

    readonly hasValidProfile = computed(() => {
        const schema = this._formSchema();
        if (!schema) return false;
        const vals = this._formValues();
        const required = schema.fields.filter(f =>
            f.validation?.required && f.visibility !== 'hidden' && f.visibility !== 'adminOnly');
        return required.every(f => {
            const v = vals[f.name];
            return v !== null && v !== undefined && String(v).trim().length > 0;
        });
    });

    readonly hasAcceptedAllWaivers = computed(() => {
        const waiverList = this._formSchema()?.waivers ?? [];
        if (waiverList.length === 0) return true;
        const acceptance = this._waiverAcceptance();
        return waiverList.every(w => acceptance[w.key] === true);
    });

    /** True when registration is fully complete (submitted or paid). */
    readonly isComplete = computed(() => {
        if (this.hasFees()) return this._paymentSuccess();
        return this._submitSuccess();
    });

    // ── Controlled mutators ───────────────────────────────────────
    setMode(v: 'create' | 'login'): void { this._mode.set(v); }

    setCredentials(data: {
        username: string; password: string;
        firstName: string; lastName: string;
        email: string; phone: string;
    }): void {
        this._username.set(data.username);
        this._password.set(data.password);
        this._firstName.set(data.firstName);
        this._lastName.set(data.lastName);
        this._email.set(data.email);
        this._phone.set(data.phone);
    }

    selectRole(role: AdultRoleOption, jobPath: string): void {
        this._selectedRole.set(role);
        this._formValues.set({});
        this._waiverAcceptance.set({});
        this._preSubmitResponse.set(null);
        this._preSubmitError.set(null);
        this.loadFormSchema(jobPath, role.roleType);
    }

    setFieldValue(fieldName: string, value: FormFieldValue): void {
        this._formValues.set({ ...this._formValues(), [fieldName]: value });
    }

    setWaiverAccepted(key: string, accepted: boolean): void {
        this._waiverAcceptance.set({ ...this._waiverAcceptance(), [key]: accepted });
    }

    setPaymentMethod(method: 'CC' | 'Check'): void {
        this._paymentMethod.set(method);
    }

    /** Populate username from authenticated user's token (login-mode). */
    populateFromAuth(): void {
        const user = this.auth.currentUser();
        if (user) {
            this._username.set(user.username ?? '');
        }
    }

    // ── API: Load job info ────────────────────────────────────────
    loadJobInfo(jobPath: string): void {
        this._jobLoading.set(true);
        this._jobError.set(null);

        this.api.getJobInfo(jobPath)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (data) => {
                    this._jobLoading.set(false);
                    this._jobInfo.set(data);
                },
                error: (err: unknown) => {
                    this._jobLoading.set(false);
                    this._jobError.set(formatHttpError(err));
                },
            });
    }

    // ── API: Load form schema for role ────────────────────────────
    private loadFormSchema(jobPath: string, roleType: number): void {
        this._schemaLoading.set(true);
        this._schemaError.set(null);
        this._formSchema.set(null);

        this.api.getFormSchema(jobPath, roleType)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (data) => {
                    this._schemaLoading.set(false);
                    this._formSchema.set(data);
                },
                error: (err: unknown) => {
                    this._schemaLoading.set(false);
                    this._schemaError.set(formatHttpError(err));
                },
            });
    }

    // ── API: PreSubmit ────────────────────────────────────────────
    async preSubmit(jobPath: string): Promise<boolean> {
        this._preSubmitting.set(true);
        this._preSubmitError.set(null);
        this._preSubmitResponse.set(null);

        const role = this._selectedRole();
        if (!role) {
            this._preSubmitting.set(false);
            this._preSubmitError.set('Please select a role.');
            return false;
        }

        try {
            const resp = await firstValueFrom(
                this.api.preSubmit(jobPath, {
                    roleType: role.roleType,
                    formValues: this._formValues() as Record<string, unknown>,
                    waiverAcceptance: this._waiverAcceptance(),
                }),
            );

            this._preSubmitting.set(false);
            this._preSubmitResponse.set(resp);

            if (!resp.valid) {
                this._preSubmitError.set('Please fix the validation errors above.');
                return false;
            }

            // For login-mode, preSubmit created the registration
            if (resp.registrationId) {
                this._registrationId.set(resp.registrationId);
            }

            return true;
        } catch (err: unknown) {
            this._preSubmitting.set(false);
            this._preSubmitError.set(formatHttpError(err));
            return false;
        }
    }

    // ── API: Submit registration ──────────────────────────────────
    /** Submit registration for create-mode (no fees or fees included). */
    async submit(jobPath: string): Promise<boolean> {
        this._submitting.set(true);
        this._submitError.set(null);
        this._submitSuccess.set(false);

        const role = this._selectedRole();
        if (!role) {
            this._submitting.set(false);
            this._submitError.set('Please select a role.');
            return false;
        }

        try {
            if (this._mode() === 'login') {
                // Login-mode: registration was already created by preSubmit
                // Just mark as success and load confirmation
                this._submitting.set(false);
                this._submitSuccess.set(true);
                const regId = this._registrationId();
                if (regId) this.loadConfirmation(regId);
                return true;
            }

            // Create-mode: register new user
            const request = {
                username: this._username(),
                password: this._password(),
                firstName: this._firstName(),
                lastName: this._lastName(),
                email: this._email(),
                phone: this._phone(),
                roleType: role.roleType,
                formValues: this._formValues(),
                waiverAcceptance: this._waiverAcceptance(),
            } as Record<string, unknown>;

            const res = await firstValueFrom(
                this.api.registerNewUser(jobPath, request as never),
            );

            this._submitting.set(false);

            if (res?.success) {
                this._submitSuccess.set(true);
                this._registrationId.set(res.registrationId);
                this.loadConfirmation(res.registrationId);
                return true;
            }

            this._submitError.set(res?.message ?? 'Registration failed.');
            return false;
        } catch (err: unknown) {
            this._submitting.set(false);
            this._submitError.set(formatHttpError(err));
            return false;
        }
    }

    // ── API: Submit payment ───────────────────────────────────────
    async submitPayment(creditCard?: CreditCardValues): Promise<boolean> {
        const regId = this._registrationId();
        if (!regId) {
            this._paymentError.set('No registration found for payment.');
            return false;
        }

        this._paymentSubmitting.set(true);
        this._paymentError.set(null);

        try {
            if (this._mode() === 'login') {
                // Login-mode: call payment endpoint directly
                const resp = await firstValueFrom(
                    this.api.submitPayment({
                        registrationId: regId,
                        creditCard: this._paymentMethod() === 'CC' ? creditCard : null,
                        paymentMethod: this._paymentMethod(),
                    }),
                );

                this._paymentSubmitting.set(false);
                if (resp.success) {
                    this._paymentSuccess.set(true);
                    this.loadConfirmation(regId);
                    return true;
                }

                this._paymentError.set(resp.message ?? 'Payment failed.');
                return false;
            }

            // Create-mode: register + pay atomically
            const role = this._selectedRole();
            if (!role) {
                this._paymentSubmitting.set(false);
                this._paymentError.set('Please select a role.');
                return false;
            }

            const request = {
                username: this._username(),
                password: this._password(),
                firstName: this._firstName(),
                lastName: this._lastName(),
                email: this._email(),
                phone: this._phone(),
                roleType: role.roleType,
                formValues: this._formValues(),
                waiverAcceptance: this._waiverAcceptance(),
                creditCard: this._paymentMethod() === 'CC' ? creditCard : null,
                paymentMethod: this._paymentMethod(),
            } as Record<string, unknown>;

            const res = await firstValueFrom(
                this.api.registerNewUser(this._jobPath(), request as never),
            );

            this._paymentSubmitting.set(false);

            if (res?.success) {
                this._paymentSuccess.set(true);
                this._submitSuccess.set(true);
                this._registrationId.set(res.registrationId);
                this.loadConfirmation(res.registrationId);
                return true;
            }

            this._paymentError.set(res?.message ?? 'Registration failed.');
            return false;
        } catch (err: unknown) {
            this._paymentSubmitting.set(false);
            this._paymentError.set(formatHttpError(err));
            return false;
        }
    }

    // ── API: Load confirmation HTML ───────────────────────────────
    private loadConfirmation(registrationId: string): void {
        this._confirmationLoading.set(true);

        this.api.getConfirmation(registrationId, skipErrorToast())
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (data) => {
                    this._confirmationLoading.set(false);
                    this._confirmationHtml.set(data.confirmationHtml);
                },
                error: () => {
                    this._confirmationLoading.set(false);
                    // Non-critical — user still sees success state; no toast.
                },
            });
    }

    // ── Job path (stored for create-mode payment) ─────────────────
    private readonly _jobPath = signal('');
    setJobPath(path: string): void { this._jobPath.set(path); }

    // ── Reset ─────────────────────────────────────────────────────
    reset(): void {
        this._mode.set('create');
        this._username.set('');
        this._password.set('');
        this._firstName.set('');
        this._lastName.set('');
        this._email.set('');
        this._phone.set('');
        this._jobInfo.set(null);
        this._jobLoading.set(false);
        this._jobError.set(null);
        this._selectedRole.set(null);
        this._formSchema.set(null);
        this._schemaLoading.set(false);
        this._schemaError.set(null);
        this._formValues.set({});
        this._waiverAcceptance.set({});
        this._preSubmitResponse.set(null);
        this._preSubmitting.set(false);
        this._preSubmitError.set(null);
        this._paymentMethod.set('CC');
        this._paymentSubmitting.set(false);
        this._paymentError.set(null);
        this._paymentSuccess.set(false);
        this._submitting.set(false);
        this._submitError.set(null);
        this._submitSuccess.set(false);
        this._registrationId.set(null);
        this._confirmationHtml.set(null);
        this._confirmationLoading.set(false);
        this._jobPath.set('');
    }
}
