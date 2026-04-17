import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@infrastructure/services/auth.service';
import { FamilyService } from '@infrastructure/services/family.service';
import type { FamilyRegistrationRequest, FamilyUpdateRequest, FamilyProfileResponse } from '@core/api';
import { formatHttpError } from '../../shared/utils/error-utils';

// ── Local interfaces (form-only, not backend DTOs) ─────────────────
export interface FamilyContact {
    firstName: string;
    lastName: string;
    phone: string;
    email: string;
    emailConfirm: string;
}

export interface FamilyAddress {
    address1: string;
    city: string;
    state: string;
    postalCode: string;
}

export interface ChildProfileDraft {
    userId?: string;
    firstName: string;
    lastName: string;
    dob?: string;
    gender: string;
    email?: string;
    phone?: string;
    hasRegistrations?: boolean;
}

const EMPTY_CONTACT: FamilyContact = { firstName: '', lastName: '', phone: '', email: '', emailConfirm: '' };
const EMPTY_ADDRESS: FamilyAddress = { address1: '', city: '', state: '', postalCode: '' };

/**
 * Family wizard v2 state service.
 *
 * Follows InsuranceStateService gold-standard pattern:
 * - Private signals + public readonly + controlled mutators.
 * - API calls encapsulated with error signals (no swallowed errors).
 * - Fully independent — no dependency on RegistrationWizardService.
 */
@Injectable({ providedIn: 'root' })
export class FamilyStateService {
    private readonly familyService = inject(FamilyService);
    private readonly authService = inject(AuthService);
    private readonly destroyRef = inject(DestroyRef);

    // ── Mode ────────────────────────────────────────────────────────
    private readonly _mode = signal<'create' | 'edit'>('create');
    readonly mode = this._mode.asReadonly();

    // ── Credentials ─────────────────────────────────────────────────
    private readonly _username = signal('');
    private readonly _password = signal('');
    private readonly _confirmPassword = signal('');
    private readonly _accountExists = signal(false);
    readonly username = this._username.asReadonly();
    readonly password = this._password.asReadonly();
    readonly accountExists = this._accountExists.asReadonly();

    // ── Parent contacts ─────────────────────────────────────────────
    private readonly _parent1 = signal<FamilyContact>({ ...EMPTY_CONTACT });
    private readonly _parent2 = signal<FamilyContact>({ ...EMPTY_CONTACT });
    readonly parent1 = this._parent1.asReadonly();
    readonly parent2 = this._parent2.asReadonly();

    // ── Address ─────────────────────────────────────────────────────
    private readonly _address = signal<FamilyAddress>({ ...EMPTY_ADDRESS });
    readonly address = this._address.asReadonly();

    // ── Children ────────────────────────────────────────────────────
    private readonly _children = signal<ChildProfileDraft[]>([]);
    readonly children = this._children.asReadonly();

    // ── Submission state ────────────────────────────────────────────
    private readonly _submitting = signal(false);
    private readonly _submitError = signal<string | null>(null);
    private readonly _submitSuccess = signal(false);
    readonly submitting = this._submitting.asReadonly();
    readonly submitError = this._submitError.asReadonly();
    readonly submitSuccess = this._submitSuccess.asReadonly();

    // ── Profile loading (edit mode) ─────────────────────────────────
    private readonly _profileLoading = signal(false);
    private readonly _profileError = signal<string | null>(null);
    readonly profileLoading = this._profileLoading.asReadonly();
    readonly profileError = this._profileError.asReadonly();

    // ── Profile persist (edit mode) ─────────────────────────────────
    private readonly _profileSaving = signal(false);
    private readonly _profileSaved = signal(false);
    readonly profileSaving = this._profileSaving.asReadonly();
    readonly profileSaved = this._profileSaved.asReadonly();

    // ── Derived ─────────────────────────────────────────────────────
    readonly hasValidCredentials = computed(() => {
        const u = this._username().trim().length >= 3;
        const p = this._password().trim().length >= 6;
        if (!u || !p) return false;
        // Existing accounts don't need confirm password
        if (this._accountExists()) return true;
        return this._confirmPassword().length >= 6
            && this._confirmPassword() === this._password();
    });

    readonly hasValidParent1 = computed(() => {
        const p = this._parent1();
        return p.firstName.trim().length > 0
            && p.lastName.trim().length > 0
            && p.phone.trim().length > 0
            && p.email.trim().length > 0
            && p.email === p.emailConfirm;
    });

    readonly hasValidParent2 = computed(() => {
        const p = this._parent2();
        return p.firstName.trim().length > 0
            && p.lastName.trim().length > 0
            && p.phone.trim().length > 0
            && p.email.trim().length > 0
            && p.email === p.emailConfirm;
    });

    readonly hasValidAddress = computed(() => {
        const a = this._address();
        return a.address1.trim().length > 0
            && a.city.trim().length > 0
            && a.state.trim().length > 0
            && a.postalCode.trim().length >= 5;
    });

    readonly hasChildren = computed(() => {
        const kids = this._children();
        return kids.length > 0 && kids.every(k => k.firstName.trim().length > 0 && k.lastName.trim().length > 0);
    });

    // ── Controlled mutators ─────────────────────────────────────────
    setMode(v: 'create' | 'edit'): void { this._mode.set(v); }

    setCredentials(username: string, password: string, confirmPassword: string = ''): void {
        this._username.set(username);
        this._password.set(password);
        this._confirmPassword.set(confirmPassword);
    }

    setAccountExists(exists: boolean): void { this._accountExists.set(exists); }

    /** Populate wizard state from a validated profile response (existing account). */
    populateFromProfile(profile: FamilyProfileResponse): void {
        this._mode.set('edit');
        this._accountExists.set(true);
        this._username.set(profile.username ?? '');
        this._parent1.set({
            firstName: profile.primary?.firstName ?? '',
            lastName: profile.primary?.lastName ?? '',
            phone: profile.primary?.cellphone ?? '',
            email: profile.primary?.email ?? '',
            emailConfirm: profile.primary?.email ?? '',
        });
        this._parent2.set({
            firstName: profile.secondary?.firstName ?? '',
            lastName: profile.secondary?.lastName ?? '',
            phone: profile.secondary?.cellphone ?? '',
            email: profile.secondary?.email ?? '',
            emailConfirm: profile.secondary?.email ?? '',
        });
        this._address.set({
            address1: profile.address?.streetAddress ?? '',
            city: profile.address?.city ?? '',
            state: profile.address?.state ?? '',
            postalCode: profile.address?.postalCode ?? '',
        });
        this._children.set(
            (profile.children ?? []).map(c => ({
                userId: c.userId ?? undefined,
                firstName: c.firstName ?? '',
                lastName: c.lastName ?? '',
                gender: c.gender ?? '',
                dob: c.dob ?? undefined,
                email: c.email ?? undefined,
                phone: c.phone ?? undefined,
                hasRegistrations: c.hasRegistrations ?? false,
            })),
        );
    }

    setParent1(contact: FamilyContact): void { this._parent1.set({ ...contact }); }
    setParent2(contact: FamilyContact): void { this._parent2.set({ ...contact }); }
    setAddress(addr: FamilyAddress): void { this._address.set({ ...addr }); }

    setChildren(kids: ChildProfileDraft[]): void { this._children.set([...kids]); }

    addChild(child: ChildProfileDraft): void {
        this._children.set([...this._children(), { ...child }]);
    }

    removeChildAt(index: number): void {
        const list = [...this._children()];
        if (index >= 0 && index < list.length) {
            list.splice(index, 1);
            this._children.set(list);
        }
    }

    updateChildAt(index: number, child: ChildProfileDraft): void {
        const list = [...this._children()];
        if (index >= 0 && index < list.length) {
            list[index] = { ...child };
            this._children.set(list);
        }
    }

    // ── API: Load existing profile (edit mode) ──────────────────────
    loadProfile(): void {
        this._profileLoading.set(true);
        this._profileError.set(null);

        this.familyService.getMyFamily()
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: (profile) => {
                    this._profileLoading.set(false);
                    if (!profile) {
                        this._profileError.set('No family profile found.');
                        return;
                    }
                    // Existing account — switch to edit mode
                    this._mode.set('edit');
                    this._accountExists.set(true);
                    this._username.set(profile.username ?? '');
                    this._parent1.set({
                        firstName: profile.primary?.firstName ?? '',
                        lastName: profile.primary?.lastName ?? '',
                        phone: profile.primary?.cellphone ?? '',
                        email: profile.primary?.email ?? '',
                        emailConfirm: profile.primary?.email ?? '',
                    });
                    this._parent2.set({
                        firstName: profile.secondary?.firstName ?? '',
                        lastName: profile.secondary?.lastName ?? '',
                        phone: profile.secondary?.cellphone ?? '',
                        email: profile.secondary?.email ?? '',
                        emailConfirm: profile.secondary?.email ?? '',
                    });
                    this._address.set({
                        address1: profile.address?.streetAddress ?? '',
                        city: profile.address?.city ?? '',
                        state: profile.address?.state ?? '',
                        postalCode: profile.address?.postalCode ?? '',
                    });
                    this._children.set(
                        (profile.children ?? []).map(c => ({
                            userId: c.userId ?? undefined,
                            firstName: c.firstName ?? '',
                            lastName: c.lastName ?? '',
                            gender: c.gender ?? '',
                            dob: c.dob ?? undefined,
                            email: c.email ?? undefined,
                            phone: c.phone ?? undefined,
                            hasRegistrations: c.hasRegistrations ?? false,
                        })),
                    );
                },
                error: (err: unknown) => {
                    this._profileLoading.set(false);
                    this._profileError.set(formatHttpError(err));
                },
            });
    }

    // ── API: Persist profile (edit mode, per-step save) ──────────────
    persistProfile(): void {
        if (this._mode() !== 'edit') return;
        this._profileSaving.set(true);
        this._profileSaved.set(false);
        const username = this._username() || this.authService.getCurrentUser()?.username || '';
        this.familyService.updateFamily({
            username,
            primary: { firstName: this._parent1().firstName, lastName: this._parent1().lastName, cellphone: this._parent1().phone, email: this._parent1().email },
            secondary: { firstName: this._parent2().firstName, lastName: this._parent2().lastName, cellphone: this._parent2().phone, email: this._parent2().email },
            address: { streetAddress: this._address().address1, city: this._address().city, state: this._address().state, postalCode: this._address().postalCode },
            children: this._children().map(c => ({ userId: c.userId, firstName: c.firstName, lastName: c.lastName, gender: c.gender, dob: c.dob, email: c.email, phone: c.phone })),
        }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
            next: () => {
                this._profileSaving.set(false);
                this._profileSaved.set(true);
            },
            error: (err: unknown) => {
                this._profileSaving.set(false);
                console.error('[FamilyState] persistProfile failed', err);
            },
        });
    }

    clearSavedFlag(): void { this._profileSaved.set(false); }

    // ── API: Submit (create or update) ──────────────────────────────
    submit(): void {
        this._submitting.set(true);
        this._submitError.set(null);
        this._submitSuccess.set(false);

        const children = this._children().map(c => ({
            userId: c.userId,
            firstName: c.firstName,
            lastName: c.lastName,
            gender: c.gender,
            dob: c.dob,
            email: c.email,
            phone: c.phone,
        }));

        const address = {
            streetAddress: this._address().address1,
            city: this._address().city,
            state: this._address().state,
            postalCode: this._address().postalCode,
        };

        const primary = {
            firstName: this._parent1().firstName,
            lastName: this._parent1().lastName,
            cellphone: this._parent1().phone,
            email: this._parent1().email,
        };

        const secondary = {
            firstName: this._parent2().firstName,
            lastName: this._parent2().lastName,
            cellphone: this._parent2().phone,
            email: this._parent2().email,
        };

        if (this._mode() === 'edit') {
            const username = this._username() || this.authService.getCurrentUser()?.username || '';
            const req: FamilyUpdateRequest = { username, primary, secondary, address, children };
            this.familyService.updateFamily(req)
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({
                    next: (res) => {
                        this._submitting.set(false);
                        if (res?.success) {
                            this._submitSuccess.set(true);
                        } else {
                            this._submitError.set(res?.message ?? 'Unable to update Family Account');
                        }
                    },
                    error: (err: unknown) => {
                        this._submitting.set(false);
                        this._submitError.set(formatHttpError(err));
                    },
                });
        } else {
            const req: FamilyRegistrationRequest = {
                username: this._username(),
                password: this._password(),
                primary, secondary, address, children,
            };
            this.familyService.registerFamily(req)
                .pipe(takeUntilDestroyed(this.destroyRef))
                .subscribe({
                    next: (res) => {
                        this._submitting.set(false);
                        if (res?.success) {
                            this._submitSuccess.set(true);
                        } else {
                            this._submitError.set(res?.message ?? 'Unable to create Family Account');
                        }
                    },
                    error: (err: unknown) => {
                        this._submitting.set(false);
                        this._submitError.set(formatHttpError(err));
                    },
                });
        }
    }

    // ── Reset ───────────────────────────────────────────────────────
    reset(): void {
        this._mode.set('create');
        this._username.set('');
        this._password.set('');
        this._confirmPassword.set('');
        this._accountExists.set(false);
        this._parent1.set({ ...EMPTY_CONTACT });
        this._parent2.set({ ...EMPTY_CONTACT });
        this._address.set({ ...EMPTY_ADDRESS });
        this._children.set([]);
        this._submitting.set(false);
        this._submitError.set(null);
        this._submitSuccess.set(false);
        this._profileLoading.set(false);
        this._profileError.set(null);
    }
}
