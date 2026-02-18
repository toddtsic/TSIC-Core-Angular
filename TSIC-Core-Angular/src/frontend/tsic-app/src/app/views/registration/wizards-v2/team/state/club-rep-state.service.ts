import { Injectable, inject, signal, computed } from '@angular/core';
import { AuthService } from '@infrastructure/services/auth.service';
import { UserPreferencesService } from '@infrastructure/services/user-preferences.service';
import type { ClubRepClubDto, ClubSearchResult } from '@core/api';

/**
 * Club Rep State Service — owns club selection and rep session state.
 *
 * Extracted from TeamRegistrationWizardComponent to follow gold-standard
 * signal encapsulation pattern.
 */
@Injectable({ providedIn: 'root' })
export class ClubRepStateService {
    private readonly auth = inject(AuthService);
    private readonly userPrefs = inject(UserPreferencesService);

    // ── Private backing signals ────────────────────────────────────────
    private readonly _availableClubs = signal<ClubRepClubDto[]>([]);
    private readonly _selectedClub = signal<string | null>(null);
    private readonly _clubInfoCollapsed = signal(false);
    private readonly _clubRepInfoAlreadyRead = signal(false);
    private readonly _metadataError = signal<string | null>(null);
    private readonly _similarClubs = signal<ClubSearchResult[]>([]);

    // ── Public readonly ────────────────────────────────────────────────
    readonly availableClubs = this._availableClubs.asReadonly();
    readonly selectedClub = this._selectedClub.asReadonly();
    readonly clubInfoCollapsed = this._clubInfoCollapsed.asReadonly();
    readonly clubRepInfoAlreadyRead = this._clubRepInfoAlreadyRead.asReadonly();
    readonly metadataError = this._metadataError.asReadonly();
    readonly similarClubs = this._similarClubs.asReadonly();

    // ── Derived ────────────────────────────────────────────────────────
    readonly registrationId = computed(() => this.auth.currentUser()?.regId || '');

    // ── Controlled mutators ────────────────────────────────────────────
    setAvailableClubs(clubs: ClubRepClubDto[]): void { this._availableClubs.set(clubs); }
    setSelectedClub(club: string | null): void { this._selectedClub.set(club); }
    setMetadataError(err: string | null): void { this._metadataError.set(err); }
    setSimilarClubs(clubs: ClubSearchResult[]): void { this._similarClubs.set(clubs); }

    toggleClubInfoCollapsed(): void {
        this._clubInfoCollapsed.update(v => !v);
    }

    acknowledgeClubRepInfo(): void {
        this.userPrefs.markClubRepModalInfoAsRead();
        this._clubRepInfoAlreadyRead.set(true);
        this._clubInfoCollapsed.set(true);
    }

    /** Initialize accordion state from user preferences. */
    initFromPreferences(): void {
        const hasRead = this.userPrefs.isClubRepModalInfoRead();
        this._clubInfoCollapsed.set(hasRead);
        this._clubRepInfoAlreadyRead.set(hasRead);
    }

    // ── Reset ──────────────────────────────────────────────────────────
    reset(): void {
        this._availableClubs.set([]);
        this._selectedClub.set(null);
        this._metadataError.set(null);
        this._similarClubs.set([]);
    }
}
