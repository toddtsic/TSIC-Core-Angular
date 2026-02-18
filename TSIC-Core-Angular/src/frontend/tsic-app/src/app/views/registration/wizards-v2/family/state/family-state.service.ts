import { Injectable, inject, signal, computed, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthService } from '@infrastructure/services/auth.service';
import { FamilyService } from '@infrastructure/services/family.service';
import type { FamilyRegistrationRequest, FamilyUpdateRequest } from '@core/api';
import { formatHttpError } from '../../../wizards/shared/utils/error-utils';

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
    firstName: string;
    lastName: string;
    dob?: string;
    gender: string;
    email?: string;
    phone?: string;
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

    // ── Credentials (create mode only) ──────────────────────────────
    private readonly _username = signal('');
    private readonly _password = signal('');
    readonly username = this._username.asReadonly();
    readonly password = this._password.asReadonly();

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

    // ── Derived ─────────────────────────────────────────────────────
    readonly hasValidCredentials = computed(() =>
        this._username().trim().length >= 3 && this._password().trim().length >= 6,
    );

    readonly hasValidParent1 = computed(() => {
        const p = this._parent1();
        return p.firstName.trim().length > 0
            && p.lastName.trim().length > 0
            && p.email.trim().length > 0
            && p.email === p.emailConfirm;
    });

    readonly hasValidAddress = computed(() => {
        const a = this._address();
        return a.address1.trim().length > 0
            && a.city.trim().length > 0
            && a.state.trim().length > 0
            && a.postalCode.trim().length > 0;
    });

    readonly hasChildren = computed(() => {
        const kids = this._children();
        return kids.length > 0 && kids.every(k => k.firstName.trim().length > 0 && k.lastName.trim().length > 0);
    });

    // ── Controlled mutators ─────────────────────────────────────────
    setMode(v: 'create' | 'edit'): void { this._mode.set(v); }

    setCredentials(username: string, password: string): void {
        this._username.set(username);
        this._password.set(password);
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
                    // Populate state from server response
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
                            firstName: c.firstName ?? '',
                            lastName: c.lastName ?? '',
                            gender: c.gender ?? '',
                            dob: c.dob ?? undefined,
                            email: c.email ?? undefined,
                            phone: c.phone ?? undefined,
                        })),
                    );
                },
                error: (err: unknown) => {
                    this._profileLoading.set(false);
                    this._profileError.set(formatHttpError(err));
                },
            });
    }

    // ── API: Submit (create or update) ──────────────────────────────
    submit(): void {
        this._submitting.set(true);
        this._submitError.set(null);
        this._submitSuccess.set(false);

        const children = this._children().map(c => ({
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
