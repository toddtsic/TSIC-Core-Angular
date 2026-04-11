import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { skipErrorToast } from '@app/infrastructure/interceptors/http-error-context';
import {
    AdultRegistrationService,
    type AdultRegJobInfoResponse,
    type AdultRegFormResponse,
    type AdultRoleOption,
    type AdultRegField,
    type AdultWaiverDto,
} from '@infrastructure/services/adult-registration.service';
import { formatHttpError } from '../../shared/utils/error-utils';

// ── Local form-only types ────────────────────────────────────────
export type FormFieldValue = string | number | boolean | null;

/**
 * Adult wizard v2 state service.
 *
 * Follows FamilyStateService gold-standard pattern:
 * - Private signals + public readonly + controlled mutators.
 * - API calls encapsulated with error signals.
 * - Fully independent.
 */
@Injectable({ providedIn: 'root' })
export class AdultWizardStateService {
    private readonly api = inject(AdultRegistrationService);
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

    // ── Submission state ──────────────────────────────────────────
    private readonly _submitting = signal(false);
    private readonly _submitError = signal<string | null>(null);
    private readonly _submitSuccess = signal(false);
    private readonly _registrationId = signal<string | null>(null);
    private readonly _confirmationHtml = signal<string | null>(null);
    readonly submitting = this._submitting.asReadonly();
    readonly submitError = this._submitError.asReadonly();
    readonly submitSuccess = this._submitSuccess.asReadonly();
    readonly registrationId = this._registrationId.asReadonly();
    readonly confirmationHtml = this._confirmationHtml.asReadonly();

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
        this.loadFormSchema(jobPath, role.roleType);
    }

    setFieldValue(fieldName: string, value: FormFieldValue): void {
        this._formValues.set({ ...this._formValues(), [fieldName]: value });
    }

    setWaiverAccepted(key: string, accepted: boolean): void {
        this._waiverAcceptance.set({ ...this._waiverAcceptance(), [key]: accepted });
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

    // ── API: Submit registration ──────────────────────────────────
    submit(jobPath: string, onSuccess?: () => void): void {
        this._submitting.set(true);
        this._submitError.set(null);
        this._submitSuccess.set(false);

        const role = this._selectedRole();
        if (!role) {
            this._submitting.set(false);
            this._submitError.set('Please select a role.');
            return;
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
        };

        this.api.registerNewUser(jobPath, request)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (res) => {
                    this._submitting.set(false);
                    if (res?.success) {
                        this._submitSuccess.set(true);
                        this._registrationId.set(res.registrationId);
                        this.loadConfirmation(res.registrationId);
                        onSuccess?.();
                    } else {
                        this._submitError.set(res?.message ?? 'Registration failed.');
                    }
                },
                error: (err: unknown) => {
                    this._submitting.set(false);
                    this._submitError.set(formatHttpError(err));
                },
            });
    }

    // ── API: Load confirmation HTML ───────────────────────────────
    private loadConfirmation(registrationId: string): void {
        this.api.getConfirmation(registrationId, skipErrorToast())
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (data) => {
                    this._confirmationHtml.set(data.confirmationHtml);
                },
                error: () => {
                    // Non-critical — user still sees success state; no toast.
                },
            });
    }

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
        this._submitting.set(false);
        this._submitError.set(null);
        this._submitSuccess.set(false);
        this._registrationId.set(null);
        this._confirmationHtml.set(null);
    }
}
