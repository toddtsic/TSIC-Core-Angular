import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { firstValueFrom } from 'rxjs';
import { skipErrorToast } from '@app/infrastructure/interceptors/http-error-context';
import { AuthService } from '@infrastructure/services/auth.service';
import { AccountService } from '@infrastructure/services/account.service';
import { AdultRegistrationService } from '@infrastructure/services/adult-registration.service';
import type {
    JobRegFieldDto,
    AdultWaiverDto,
    AdultTeamOptionDto,
    AdultRoleConfigDto,
    PreSubmitAdultRegResponseDto,
    AdultFeeBreakdownDto,
    AdultValidationErrorDto,
    CreditCardInfo,
} from '@core/api';
import { formatHttpError } from '../../shared/utils/error-utils';

export type FormFieldValue = string | number | boolean | null;

export type UsLaxVerifyStatus =
    'idle' | 'sending' | 'sent' | 'verifying' | 'verified' | 'unverified' | 'error';

/**
 * Adult wizard state service — unified, role-config-driven.
 *
 * The authoritative role configuration comes from the backend
 * (<c>GET /adult-registration/{jobPath}/role-config/{roleKey}</c>). The wizard
 * never chooses a role itself; it loads config for the URL's <c>?role=</c>
 * param and renders accordingly. If the backend rejects the combination
 * (unknown role, security invariant violation, unsupported job type), the
 * wizard shows an error card and does not render the stepper.
 */
@Injectable({ providedIn: 'root' })
export class AdultWizardStateService {
    private readonly api = inject(AdultRegistrationService);
    private readonly auth = inject(AuthService);
    private readonly account = inject(AccountService);
    private readonly destroyRef = inject(DestroyRef);

    // ── Mode ──────────────────────────────────────────────────────
    private readonly _mode = signal<'create' | 'login'>('create');
    readonly mode = this._mode.asReadonly();

    // ── Credentials (create mode only) ────────────────────────────
    private readonly _username = signal('');
    private readonly _password = signal('');
    private readonly _confirmPassword = signal('');
    readonly username = this._username.asReadonly();
    readonly password = this._password.asReadonly();
    readonly confirmPassword = this._confirmPassword.asReadonly();

    // ── Identity ──────────────────────────────────────────────────
    private readonly _firstName = signal('');
    private readonly _lastName = signal('');
    private readonly _gender = signal('');
    readonly firstName = this._firstName.asReadonly();
    readonly lastName = this._lastName.asReadonly();
    readonly gender = this._gender.asReadonly();

    // ── Contact ───────────────────────────────────────────────────
    private readonly _email = signal('');
    private readonly _confirmEmail = signal('');
    private readonly _phone = signal('');
    readonly email = this._email.asReadonly();
    readonly confirmEmail = this._confirmEmail.asReadonly();
    readonly phone = this._phone.asReadonly();

    // ── Address ───────────────────────────────────────────────────
    private readonly _streetAddress = signal('');
    private readonly _city = signal('');
    private readonly _state = signal('');
    private readonly _postalCode = signal('');
    readonly streetAddress = this._streetAddress.asReadonly();
    readonly city = this._city.asReadonly();
    readonly state = this._state.asReadonly();
    readonly postalCode = this._postalCode.asReadonly();

    // ── ToS (create mode only) ────────────────────────────────────
    private readonly _acceptedTos = signal(false);
    readonly acceptedTos = this._acceptedTos.asReadonly();

    // ── Role configuration (loaded from backend) ──────────────────
    private readonly _roleConfig = signal<AdultRoleConfigDto | null>(null);
    private readonly _roleConfigLoading = signal(false);
    private readonly _roleConfigError = signal<string | null>(null);
    readonly roleConfig = this._roleConfig.asReadonly();
    readonly roleConfigLoading = this._roleConfigLoading.asReadonly();
    readonly roleConfigError = this._roleConfigError.asReadonly();

    // ── Self profile (returning-user account summary / edit) ──────
    private readonly _selfProfileLoaded = signal(false);
    private readonly _selfProfileLoading = signal(false);
    private readonly _selfProfileSaving = signal(false);
    private readonly _selfProfileError = signal<string | null>(null);
    readonly selfProfileLoaded = this._selfProfileLoaded.asReadonly();
    readonly selfProfileLoading = this._selfProfileLoading.asReadonly();
    readonly selfProfileSaving = this._selfProfileSaving.asReadonly();
    readonly selfProfileError = this._selfProfileError.asReadonly();

    // ── Existing registration (returning user prefill) ────────────
    private readonly _hasExistingRegistration = signal(false);
    private readonly _existingRegistrationIds = signal<string[]>([]);
    readonly hasExistingRegistration = this._hasExistingRegistration.asReadonly();
    readonly existingRegistrationIds = this._existingRegistrationIds.asReadonly();

    // ── Dynamic form values (role-specific profile fields) ────────
    private readonly _formValues = signal<Record<string, FormFieldValue>>({});
    readonly formValues = this._formValues.asReadonly();

    // ── Coach USLax identity verification (email OTP) ─────────────
    // idle → sending → sent (code-entry open) → verifying → verified
    //                              └→ unverified (no on-file email / coach skipped) | error
    private readonly _usLaxStatus = signal<UsLaxVerifyStatus>('idle');
    private readonly _usLaxVerificationId = signal<string | null>(null);
    private readonly _usLaxMaskedEmail = signal<string | null>(null);
    private readonly _usLaxMessage = signal<string | null>(null);
    readonly usLaxStatus = this._usLaxStatus.asReadonly();
    readonly usLaxMaskedEmail = this._usLaxMaskedEmail.asReadonly();
    readonly usLaxMessage = this._usLaxMessage.asReadonly();

    // ── Teams (Coach in tournament only) ──────────────────────────
    private readonly _availableTeams = signal<AdultTeamOptionDto[]>([]);
    private readonly _teamsLoading = signal(false);
    private readonly _teamsError = signal<string | null>(null);
    private readonly _teamIdsCoaching = signal<string[]>([]);
    // Stable seed fed to the Syncfusion multi-select's [value] input. It is set ONLY on
    // prefill (returning user) and reset — NEVER on each user click. Re-feeding the live
    // selection back into [value] on every (change) makes Syncfusion repaint the just-clicked
    // checkbox as unchecked (visual desync → "click twice to check"); keeping the seed
    // reference stable lets the control own its own checkbox state during interaction.
    private readonly _teamPickerSeed = signal<string[]>([]);
    readonly availableTeams = this._availableTeams.asReadonly();
    readonly teamsLoading = this._teamsLoading.asReadonly();
    readonly teamsError = this._teamsError.asReadonly();
    readonly teamIdsCoaching = this._teamIdsCoaching.asReadonly();
    readonly teamPickerSeed = this._teamPickerSeed.asReadonly();

    // ── Waiver acceptance ─────────────────────────────────────────
    private readonly _waiverAcceptance = signal<Record<string, boolean>>({});
    readonly waiverAcceptance = this._waiverAcceptance.asReadonly();

    // ── PreSubmit state ───────────────────────────────────────────
    private readonly _preSubmitResponse = signal<PreSubmitAdultRegResponseDto | null>(null);
    private readonly _preSubmitting = signal(false);
    private readonly _preSubmitError = signal<string | null>(null);
    readonly preSubmitResponse = this._preSubmitResponse.asReadonly();
    readonly preSubmitting = this._preSubmitting.asReadonly();
    readonly preSubmitError = this._preSubmitError.asReadonly();

    readonly fees = computed<AdultFeeBreakdownDto | null>(() => this._preSubmitResponse()?.fees ?? null);
    // AMEX offered only when this job's merchant account accepts it (fail-closed false).
    readonly jobUsesAmex = computed<boolean>(() => this._preSubmitResponse()?.jobUsesAmex ?? false);
    readonly hasFees = computed(() => (this.fees()?.owedTotal ?? 0) > 0);
    readonly validationErrors = computed<AdultValidationErrorDto[]>(() => this._preSubmitResponse()?.validationErrors ?? []);

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

    // ── Context ───────────────────────────────────────────────────
    private readonly _jobPath = signal('');
    private readonly _roleKey = signal('');
    readonly jobPath = this._jobPath.asReadonly();
    readonly roleKey = this._roleKey.asReadonly();

    // ── Derived from role config ──────────────────────────────────
    readonly profileFields = computed<JobRegFieldDto[]>(() => this._roleConfig()?.profileFields ?? []);
    readonly waivers = computed<AdultWaiverDto[]>(() => this._roleConfig()?.waivers ?? []);
    readonly needsTeamSelection = computed(() => this._roleConfig()?.needsTeamSelection ?? false);
    /** Club/League coach: team picker shown as a non-binding REQUEST (no assignment, no PII). */
    readonly allowTeamRequests = computed(() => this._roleConfig()?.allowTeamRequests ?? false);
    /** Whether the team multi-select renders at all — either Staff assignment or UA request. */
    readonly showTeamPicker = computed(() => this.needsTeamSelection() || this.allowTeamRequests());
    readonly roleDisplayName = computed(() => this._roleConfig()?.displayName ?? '');

    // ── Validation computed signals ───────────────────────────────
    readonly isPhoneValid = computed(() => /^\d{10,}$/.test(this._phone().trim()));
    readonly isEmailValid = computed(() => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(this._email().trim()));
    readonly emailsMatch = computed(() =>
        this._email().trim() === this._confirmEmail().trim()
        && this._email().trim().length > 0);
    readonly passwordsMatch = computed(() =>
        this._password() === this._confirmPassword()
        && this._password().length >= 6);

    /**
     * Create-form (account) validation excluding ToS. ToS lives on its own
     * inline wizard step; this computed gates the Account step's Continue button.
     */
    readonly hasCompleteCreateForm = computed(() => {
        const identityOk = this._firstName().trim().length > 0
            && this._lastName().trim().length > 0
            && this._gender().trim().length > 0
            && this.isPhoneValid()
            && this.isEmailValid();
        const addressOk = this._streetAddress().trim().length > 0
            && this._city().trim().length > 0
            && this._state().trim().length > 0
            && this._postalCode().trim().length > 0;
        return identityOk
            && addressOk
            && this._username().trim().length >= 6
            && this.passwordsMatch()
            && this.emailsMatch();
    });

    /** Account step can advance: create-mode needs full form; login-mode is always ready. */
    readonly accountStepReady = computed(() =>
        this._mode() === 'login' ? true : this.hasCompleteCreateForm());

    /** Display name for the returning-user account summary. */
    readonly fullName = computed(() => `${this._firstName()} ${this._lastName()}`.trim());

    /**
     * Self-profile edit form validity. Identity fields (name) are locked, so only
     * contact + address are validated — mirrors the club-rep edit contract.
     */
    readonly selfProfileEditValid = computed(() =>
        this.isEmailValid()
        && this.isPhoneValid()
        && this._streetAddress().trim().length > 0
        && this._city().trim().length > 0
        && this._state().trim().length > 0
        && this._postalCode().trim().length > 0);

    readonly hasValidTeams = computed(() =>
        !this.needsTeamSelection() || this._teamIdsCoaching().length > 0);

    readonly hasValidProfile = computed(() => {
        const fields = this.profileFields();
        const vals = this._formValues();
        const required = fields.filter(f =>
            f.validation?.required && f.visibility !== 'hidden' && f.visibility !== 'adminOnly');
        const fieldsOk = required.every(f => {
            const v = vals[f.name];
            return v !== null && v !== undefined && String(v).trim().length > 0;
        });
        return fieldsOk && this.hasValidTeams();
    });

    readonly hasAcceptedAllWaivers = computed(() => {
        const waiverList = this.waivers();
        if (waiverList.length === 0) return true;
        const acceptance = this._waiverAcceptance();
        return waiverList.every(w => acceptance[w.key] === true);
    });

    readonly isComplete = computed(() => {
        if (this.hasFees()) return this._paymentSuccess();
        return this._submitSuccess();
    });

    // ── Controlled mutators ───────────────────────────────────────
    setMode(v: 'create' | 'login'): void { this._mode.set(v); }
    setJobPath(path: string): void { this._jobPath.set(path); }
    setRoleKey(key: string): void { this._roleKey.set(key); }

    setUsername(v: string): void { this._username.set(v); }
    setPassword(v: string): void { this._password.set(v); }
    setConfirmPassword(v: string): void { this._confirmPassword.set(v); }
    setFirstName(v: string): void { this._firstName.set(v); }
    setLastName(v: string): void { this._lastName.set(v); }
    setGender(v: string): void { this._gender.set(v); }
    setEmail(v: string): void { this._email.set(v); }
    setConfirmEmail(v: string): void { this._confirmEmail.set(v); }
    setPhone(v: string): void { this._phone.set((v ?? '').replace(/\D/g, '')); }
    setStreetAddress(v: string): void { this._streetAddress.set(v); }
    setCity(v: string): void { this._city.set(v); }
    setState(v: string): void { this._state.set(v); }
    setPostalCode(v: string): void { this._postalCode.set(v); }
    setAcceptedTos(v: boolean): void { this._acceptedTos.set(v); }

    setFieldValue(fieldName: string, value: FormFieldValue): void {
        this._formValues.set({ ...this._formValues(), [fieldName]: value });
    }

    setWaiverAccepted(key: string, accepted: boolean): void {
        this._waiverAcceptance.set({ ...this._waiverAcceptance(), [key]: accepted });
    }

    setTeamIdsCoaching(ids: string[]): void {
        this._teamIdsCoaching.set([...ids]);
    }

    setPaymentMethod(method: 'CC' | 'Check'): void {
        this._paymentMethod.set(method);
    }

    /** Populate username from authenticated user's token (login-mode sign-in). */
    populateFromAuth(): void {
        const user = this.auth.currentUser();
        if (user?.username) {
            this._username.set(user.username);
        }
    }

    // ── API: Self profile (returning-user account summary / edit) ──
    /**
     * Load the signed-in user's stored profile into the identity/contact/address
     * signals so the account-summary view can display their name/email and the
     * edit sub-view is pre-filled. Mirrors the club-rep getSelfProfile flow.
     */
    async loadSelfProfile(): Promise<void> {
        this._selfProfileLoading.set(true);
        this._selfProfileError.set(null);
        try {
            const p = await firstValueFrom(this.account.getMyProfile());
            this._firstName.set(p.firstName ?? '');
            this._lastName.set(p.lastName ?? '');
            this._email.set(p.email ?? '');
            this._confirmEmail.set(p.email ?? '');
            this._phone.set((p.cellphone ?? '').replace(/\D/g, ''));
            this._streetAddress.set(p.streetAddress ?? '');
            this._city.set(p.city ?? '');
            this._state.set(p.state ?? '');
            this._postalCode.set(p.postalCode ?? '');
            this._selfProfileLoaded.set(true);
        } catch (err: unknown) {
            // Non-fatal: summary falls back to the token username, edit stays usable.
            this._selfProfileError.set(formatHttpError(err));
        } finally {
            this._selfProfileLoading.set(false);
        }
    }

    /**
     * Persist edits to the signed-in user's profile. Identity fields (name) are
     * carried unchanged for contract parity but locked in the UI.
     */
    async saveSelfProfile(): Promise<boolean> {
        this._selfProfileSaving.set(true);
        this._selfProfileError.set(null);
        try {
            await firstValueFrom(this.account.updateMyProfile({
                firstName: this._firstName(),
                lastName: this._lastName(),
                email: this._email().trim(),
                cellphone: this._phone(),
                streetAddress: this._streetAddress().trim(),
                city: this._city().trim(),
                state: this._state(),
                postalCode: this._postalCode().trim(),
            }));
            return true;
        } catch (err: unknown) {
            this._selfProfileError.set(formatHttpError(err));
            return false;
        } finally {
            this._selfProfileSaving.set(false);
        }
    }

    // ── API: Load role config ─────────────────────────────────────
    async loadRoleConfig(jobPath: string, roleKey: string): Promise<boolean> {
        this._roleConfigLoading.set(true);
        this._roleConfigError.set(null);
        this._roleConfig.set(null);

        try {
            const config = await firstValueFrom(
                this.api.getRoleConfig(jobPath, roleKey),
            );
            this._roleConfigLoading.set(false);
            this._roleConfig.set(config);

            // Prefetch teams whenever the picker will render — Staff assignment
            // (needsTeamSelection) or Club/League coach request (allowTeamRequests).
            if (config.needsTeamSelection || config.allowTeamRequests) {
                this.loadAvailableTeams(jobPath);
            }
            return true;
        } catch (err: unknown) {
            this._roleConfigLoading.set(false);
            this._roleConfigError.set(formatHttpError(err));
            return false;
        }
    }

    // ── API: Load existing registration (returning user prefill) ──
    /**
     * Fetches the signed-in user's existing active registrations for (jobPath, roleKey)
     * and populates state signals (teamIdsCoaching, formValues, waiverAcceptance) so
     * the Profile step shows what they previously selected.
     *
     * Silent on error / not-found — just leaves the form empty.
     */
    async loadExistingRegistration(jobPath: string, roleKey: string): Promise<void> {
        try {
            const existing = await firstValueFrom(
                this.api.getMyExistingRegistration(jobPath, roleKey),
            );
            if (!existing.hasExisting) {
                this._hasExistingRegistration.set(false);
                this._existingRegistrationIds.set([]);
                return;
            }

            this._hasExistingRegistration.set(true);
            this._existingRegistrationIds.set(existing.registrationIds ?? []);
            // Seed BOTH the tracked selection and the picker's [value] input. The seed is the
            // one intentional [value] re-feed (prefill) — user clicks thereafter touch only
            // _teamIdsCoaching, so the control's checkbox state isn't disturbed mid-interaction.
            this._teamIdsCoaching.set([...(existing.teamIds ?? [])]);
            this._teamPickerSeed.set([...(existing.teamIds ?? [])]);

            if (existing.formValues) {
                const fv: Record<string, FormFieldValue> = {};
                for (const [k, v] of Object.entries(existing.formValues)) {
                    fv[k] = v as FormFieldValue;
                }
                this._formValues.set(fv);
            }
            if (existing.waiverAcceptance) {
                this._waiverAcceptance.set({ ...existing.waiverAcceptance });
            }
        } catch {
            // Non-fatal — user registers as fresh.
            this._hasExistingRegistration.set(false);
            this._existingRegistrationIds.set([]);
        }
    }

    // ── API: Load available teams (Coach in tournament only) ──────
    private loadAvailableTeams(jobPath: string): void {
        this._teamsLoading.set(true);
        this._teamsError.set(null);
        this._availableTeams.set([]);

        this.api.getAvailableTeams(jobPath)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (teams) => {
                    this._teamsLoading.set(false);
                    this._availableTeams.set(teams);
                },
                error: (err: unknown) => {
                    this._teamsLoading.set(false);
                    this._teamsError.set(formatHttpError(err));
                },
            });
    }

    // ── API: PreSubmit ────────────────────────────────────────────
    async preSubmit(): Promise<boolean> {
        this._preSubmitting.set(true);
        this._preSubmitError.set(null);
        this._preSubmitResponse.set(null);

        const roleKey = this._roleKey();
        if (!roleKey) {
            this._preSubmitting.set(false);
            this._preSubmitError.set('No role specified.');
            return false;
        }

        try {
            const resp = await firstValueFrom(
                this.api.preSubmit(this._jobPath(), {
                    roleKey,
                    formValues: this._formValues() as Record<string, unknown>,
                    waiverAcceptance: this._waiverAcceptance(),
                    teamIdsCoaching: this._teamIdsCoaching(),
                    usLaxVerificationId: this.confirmedUsLaxVerificationId(),
                } as never),
            );

            this._preSubmitting.set(false);
            this._preSubmitResponse.set(resp);

            if (!resp.valid) {
                this._preSubmitError.set('Please fix the validation errors above.');
                return false;
            }

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

    // ── API: Submit registration (create-mode or login-mode confirm) ──
    async submit(): Promise<boolean> {
        this._submitting.set(true);
        this._submitError.set(null);
        this._submitSuccess.set(false);

        const roleKey = this._roleKey();
        if (!roleKey) {
            this._submitting.set(false);
            this._submitError.set('No role specified.');
            return false;
        }

        try {
            if (this._mode() === 'login') {
                // Registration created in preSubmit; just mark success + load confirmation.
                this._submitting.set(false);
                this._submitSuccess.set(true);
                const regId = this._registrationId();
                if (regId) this.loadConfirmation(regId);
                return true;
            }

            const res = await firstValueFrom(
                this.api.registerNewUser(this._jobPath(), this.buildCreateRequest()),
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

    // ── API: Submit payment (with fees) ───────────────────────────
    async submitPayment(creditCard?: CreditCardInfo): Promise<boolean> {
        this._paymentSubmitting.set(true);
        this._paymentError.set(null);

        try {
            if (this._mode() === 'login') {
                const regId = this._registrationId();
                if (!regId) {
                    this._paymentSubmitting.set(false);
                    this._paymentError.set('No registration found for payment.');
                    return false;
                }
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

            // Create-mode: atomic register + pay
            const payload = this.buildCreateRequest();
            (payload as Record<string, unknown>)['creditCard'] =
                this._paymentMethod() === 'CC' ? creditCard : null;
            (payload as Record<string, unknown>)['paymentMethod'] = this._paymentMethod();

            const res = await firstValueFrom(
                this.api.registerNewUser(this._jobPath(), payload),
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

    /** Build the full create-mode request payload (roleKey-driven). */
    private buildCreateRequest(): never {
        return {
            username: this._username(),
            password: this._password(),
            firstName: this._firstName(),
            lastName: this._lastName(),
            gender: this._gender(),
            email: this._email(),
            phone: this._phone(),
            streetAddress: this._streetAddress(),
            city: this._city(),
            state: this._state(),
            postalCode: this._postalCode(),
            roleKey: this._roleKey(),
            acceptedTos: this._acceptedTos(),
            formValues: this._formValues(),
            waiverAcceptance: this._waiverAcceptance(),
            teamIdsCoaching: this._teamIdsCoaching(),
            usLaxVerificationId: this.confirmedUsLaxVerificationId(),
        } as unknown as never;
    }

    /** The verification id to submit — only when identity was actually confirmed; otherwise
     *  undefined so the backend records the coach as "unverified" (the fallback path). */
    private confirmedUsLaxVerificationId(): string | undefined {
        return this._usLaxStatus() === 'verified' ? (this._usLaxVerificationId() ?? undefined) : undefined;
    }

    // ── Coach USLax identity verification (email OTP) ──────────────

    /** Validate the number + email a code to the on-file USA Lacrosse address. */
    async beginUsLaxVerify(sportAssnId: string): Promise<void> {
        const num = sportAssnId?.trim();
        if (!num) return;
        this._usLaxStatus.set('sending');
        this._usLaxMessage.set(null);
        try {
            const resp = await firstValueFrom(this.api.beginUsLaxVerify(this._jobPath(), num));
            switch (resp.status) {
                case 'Sent':
                    this._usLaxVerificationId.set(resp.verificationId ?? null);
                    this._usLaxMaskedEmail.set(resp.maskedEmail ?? null);
                    this._usLaxStatus.set('sent');
                    break;
                case 'EmailUnavailable':
                    this._usLaxStatus.set('unverified');
                    this._usLaxMessage.set(resp.message ?? null);
                    break;
                default: // MembershipInvalid | ServiceUnavailable
                    this._usLaxStatus.set('error');
                    this._usLaxMessage.set(resp.message ?? 'Could not verify this number.');
            }
        } catch (err: unknown) {
            this._usLaxStatus.set('error');
            this._usLaxMessage.set(formatHttpError(err));
        }
    }

    /** Confirm the code the registrant entered. */
    async confirmUsLaxCode(code: string): Promise<void> {
        const vid = this._usLaxVerificationId();
        const trimmed = code?.trim();
        if (!vid || !trimmed) return;
        this._usLaxStatus.set('verifying');
        this._usLaxMessage.set(null);
        try {
            const resp = await firstValueFrom(this.api.confirmUsLaxVerify(this._jobPath(), vid, trimmed));
            if (resp.success) {
                this._usLaxStatus.set('verified');
                this._usLaxMessage.set(null);
            } else {
                this._usLaxStatus.set('sent'); // keep the code-entry open
                this._usLaxMessage.set(resp.message ?? 'Incorrect code.');
            }
        } catch (err: unknown) {
            this._usLaxStatus.set('error');
            this._usLaxMessage.set(formatHttpError(err));
        }
    }

    /** Coach opts to continue without verifying (fallback) — recorded as unverified. */
    markUsLaxUnverified(): void {
        this._usLaxStatus.set('unverified');
        this._usLaxVerificationId.set(null);
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
                },
            });
    }

    // ── Reset ─────────────────────────────────────────────────────
    reset(): void {
        this._mode.set('create');
        this._username.set('');
        this._password.set('');
        this._confirmPassword.set('');
        this._firstName.set('');
        this._lastName.set('');
        this._gender.set('');
        this._email.set('');
        this._confirmEmail.set('');
        this._phone.set('');
        this._streetAddress.set('');
        this._city.set('');
        this._state.set('');
        this._postalCode.set('');
        this._acceptedTos.set(false);
        this._roleConfig.set(null);
        this._roleConfigLoading.set(false);
        this._roleConfigError.set(null);
        this._selfProfileLoaded.set(false);
        this._selfProfileLoading.set(false);
        this._selfProfileSaving.set(false);
        this._selfProfileError.set(null);
        this._hasExistingRegistration.set(false);
        this._existingRegistrationIds.set([]);
        this._formValues.set({});
        this._waiverAcceptance.set({});
        this._availableTeams.set([]);
        this._teamsLoading.set(false);
        this._teamsError.set(null);
        this._teamIdsCoaching.set([]);
        this._teamPickerSeed.set([]);
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
        this._roleKey.set('');
        this._usLaxStatus.set('idle');
        this._usLaxVerificationId.set(null);
        this._usLaxMaskedEmail.set(null);
        this._usLaxMessage.set(null);
    }
}
