import { Injectable, inject, signal, DestroyRef } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom, Observable, tap } from 'rxjs';
import { AuthService } from '@infrastructure/services/auth.service';
import { getPropertyCI, pickStringCI } from '@views/registration/wizards/shared/utils/property-utils';
import type {
    FamilyPlayerDto,
    FamilyPlayerRegistrationDto,
    FamilyPlayersResponseDto,
    RegSaverDetailsDto,
    NormalizedFamilyUser,
    AuthTokenResponse,
} from '../types/player-wizard.types';

/**
 * Family Players Service — owns family user identity, player list, loading,
 * selection, and prior registrations.
 *
 * Extracted from RegistrationWizardService lines ~42-78, 247-452.
 * Gold-standard signal pattern throughout.
 */
@Injectable({ providedIn: 'root' })
export class FamilyPlayersService {
    private readonly http = inject(HttpClient);
    private readonly auth = inject(AuthService);
    private readonly destroyRef = inject(DestroyRef);

    // ── Family account presence ───────────────────────────────────────
    private readonly _hasFamilyAccount = signal<'yes' | 'no' | null>(null);
    readonly hasFamilyAccount = this._hasFamilyAccount.asReadonly();

    // ── Players ───────────────────────────────────────────────────────
    private readonly _familyPlayers = signal<FamilyPlayerDto[]>([]);
    private readonly _familyPlayersLoading = signal(false);
    readonly familyPlayers = this._familyPlayers.asReadonly();
    readonly familyPlayersLoading = this._familyPlayersLoading.asReadonly();

    // ── Family user summary ───────────────────────────────────────────
    private readonly _familyUser = signal<NormalizedFamilyUser | null>(null);
    readonly familyUser = this._familyUser.asReadonly();

    // ── RegSaver details ──────────────────────────────────────────────
    private readonly _regSaverDetails = signal<RegSaverDetailsDto | null>(null);
    readonly regSaverDetails = this._regSaverDetails.asReadonly();

    // ── Debug snapshot ────────────────────────────────────────────────
    private readonly _debugFamilyPlayersResp = signal<FamilyPlayersResponseDto | null>(null);
    readonly debugFamilyPlayersResp = this._debugFamilyPlayersResp.asReadonly();

    // ── Controlled mutators ───────────────────────────────────────────
    setHasFamilyAccount(v: 'yes' | 'no' | null): void { this._hasFamilyAccount.set(v); }
    updateFamilyPlayers(players: FamilyPlayerDto[]): void { this._familyPlayers.set(players); }
    clearDebugFamilyPlayersResp(): void { this._debugFamilyPlayersResp.set(null); }

    // ── Derived ───────────────────────────────────────────────────────
    selectedPlayerIds(): string[] {
        return this._familyPlayers().filter(p => p.selected || p.registered).map(p => p.playerId);
    }

    isPlayerLocked(playerId: string): boolean {
        return this._familyPlayers().some(p => p.playerId === playerId && p.registered);
    }

    getPlayerLastName(playerId: string): string | null {
        const fam = this._familyPlayers().find(p => p.playerId === playerId);
        return fam?.lastName || null;
    }

    getPlayerDob(playerId: string): Date | null {
        const fam = this._familyPlayers().find(p => p.playerId === playerId);
        if (fam?.dob) {
            const d = new Date(fam.dob);
            if (!Number.isNaN(d.getTime())) return d;
        }
        return null;
    }

    // ── API: load family players ──────────────────────────────────────
    /**
     * Fire-and-forget load. Returns the raw response via onSuccess callback
     * so the orchestrator can chain downstream processing.
     */
    loadFamilyPlayers(
        jobPath: string,
        apiBase: string,
        onSuccess?: (resp: FamilyPlayersResponseDto, players: FamilyPlayerDto[]) => void,
    ): void {
        if (!this.shouldLoadFamily(jobPath)) return;
        this._familyPlayersLoading.set(true);
        this.http.get<FamilyPlayersResponseDto>(`${apiBase}/family/players`, { params: { jobPath, debug: '1' } })
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe({
                next: resp => {
                    const players = this.handleSuccess(resp);
                    this._familyPlayersLoading.set(false);
                    onSuccess?.(resp, players);
                },
                error: (err: unknown) => {
                    this.handleError(err);
                    this._familyPlayersLoading.set(false);
                },
            });
    }

    /** Promise-based variant for sequential flows (e.g. after discount apply). */
    async loadFamilyPlayersOnce(
        jobPath: string,
        apiBase: string,
        onSuccess?: (resp: FamilyPlayersResponseDto, players: FamilyPlayerDto[]) => void,
    ): Promise<void> {
        if (!this.shouldLoadFamily(jobPath)) return;
        this._familyPlayersLoading.set(true);
        try {
            const resp = await firstValueFrom(
                this.http.get<FamilyPlayersResponseDto>(`${apiBase}/family/players`, { params: { jobPath, debug: '1' } }),
            );
            const players = this.handleSuccess(resp);
            onSuccess?.(resp, players);
        } catch (err: unknown) {
            this.handleError(err);
            throw err;
        } finally {
            this._familyPlayersLoading.set(false);
        }
    }

    /** Upgrade Phase 1 token to job-scoped token. */
    setWizardContext(jobPath: string, apiBase: string): Observable<AuthTokenResponse> {
        return this.http.post<AuthTokenResponse>(
            `${apiBase}/player-registration/set-wizard-context`,
            { jobPath },
        ).pipe(
            tap(response => {
                if (response.accessToken) this.auth.applyNewToken(response.accessToken);
            }),
        );
    }

    // ── Player selection ──────────────────────────────────────────────
    togglePlayerSelection(playerId: string): boolean {
        const list = this._familyPlayers();
        let becameSelected = false;
        this._familyPlayers.set(list.map(p => {
            if (p.playerId !== playerId) return p;
            if (p.registered) return p; // locked
            const nextSelected = !p.selected;
            if (nextSelected) becameSelected = true;
            return { ...p, selected: nextSelected };
        }));
        return becameSelected;
    }

    // ── Team prefill from prior registrations ─────────────────────────
    prefillTeamsFromPriorRegistrations(
        players: FamilyPlayerDto[],
        currentTeams: Record<string, string | string[]>,
        setTeams: (map: Record<string, string | string[]>) => void,
    ): void {
        const teamMap: Record<string, string | string[]> = { ...currentTeams };
        for (const fp of players) {
            if (!fp.registered) continue;
            const teamIds = fp.priorRegistrations
                .map(r => r.assignedTeamId)
                .filter((id): id is string => typeof id === 'string' && !!id);
            if (!teamIds.length) continue;
            const unique: string[] = [];
            for (const t of teamIds) if (!unique.includes(t)) unique.push(t);
            teamMap[fp.playerId] = unique.length === 1 ? unique[0] : unique;
        }
        setTeams(teamMap);
    }

    // ── Private helpers ───────────────────────────────────────────────
    private shouldLoadFamily(jobPath: string | null | undefined): boolean {
        if (!jobPath) return false;
        if (!this.auth.getToken()) return false;
        return true;
    }

    private handleSuccess(resp: FamilyPlayersResponseDto): FamilyPlayerDto[] {
        this._debugFamilyPlayersResp.set(resp);
        this.applyFamilyUser(resp);
        this.applyRegSaverDetails(resp);
        const players = this.buildFamilyPlayersList(resp);
        this._familyPlayers.set(players);
        return players;
    }

    private handleError(err: unknown): void {
        console.warn('[FamilyPlayers] Failed to load family players', err);
        this._familyPlayers.set([]);
        this._familyUser.set(null);
        this._regSaverDetails.set(null);
        this._debugFamilyPlayersResp.set(null);
    }

    private applyFamilyUser(resp: FamilyPlayersResponseDto): void {
        const fu = resp.familyUser || getPropertyCI<Record<string, unknown>>(resp as Record<string, unknown>, 'familyUser');
        if (!fu) { this._familyUser.set(null); return; }
        const o = fu as Record<string, unknown>;
        const norm: NormalizedFamilyUser = {
            familyUserId: o['familyUserId'] as string,
            displayName: o['displayName'] as string,
            userName: o['userName'] as string,
            firstName: pickStringCI(o, 'firstName', 'parentFirstName', 'motherFirstName', 'guardianFirstName', 'billingFirstName'),
            lastName: pickStringCI(o, 'lastName', 'parentLastName', 'motherLastName', 'guardianLastName', 'billingLastName'),
            address: pickStringCI(o, 'address', 'billingAddress', 'street', 'street1', 'address1'),
            address1: pickStringCI(o, 'address1', 'street1'),
            address2: pickStringCI(o, 'address2', 'street2', 'apt', 'aptNumber', 'suite'),
            city: pickStringCI(o, 'city'),
            state: pickStringCI(o, 'state', 'stateCode'),
            zipCode: pickStringCI(o, 'zipCode', 'zip', 'postalCode'),
            zip: pickStringCI(o, 'zip'),
            postalCode: pickStringCI(o, 'postalCode'),
            email: pickStringCI(o, 'email', 'parentEmail', 'motherEmail', 'guardianEmail', 'billingEmail', 'userName'),
            phone: (() => {
                const raw = pickStringCI(o, 'phone', 'parentPhone', 'motherPhone', 'guardianPhone', 'billingPhone', 'phoneNumber', 'cellPhone', 'mobile');
                return raw ? raw.replaceAll(/\D+/g, '') : undefined;
            })(),
        };
        const rawCc = resp.ccInfo || getPropertyCI<Record<string, unknown>>(resp as Record<string, unknown>, 'ccInfo');
        if (rawCc) {
            const cc = rawCc as Record<string, unknown>;
            norm.ccInfo = {
                firstName: pickStringCI(cc, 'firstName'),
                lastName: pickStringCI(cc, 'lastName'),
                streetAddress: pickStringCI(cc, 'streetAddress', 'address'),
                zip: pickStringCI(cc, 'zip', 'zipCode', 'postalCode'),
                email: pickStringCI(cc, 'email'),
                phone: (() => {
                    const raw = pickStringCI(cc, 'phone');
                    return raw ? raw.replaceAll(/\D+/g, '') : undefined;
                })(),
            };
        }
        this._familyUser.set(norm);
    }

    private applyRegSaverDetails(resp: FamilyPlayersResponseDto): void {
        const rs = resp.regSaverDetails || getPropertyCI<RegSaverDetailsDto>(resp as Record<string, unknown>, 'regSaverDetails');
        if (!rs) { this._regSaverDetails.set(null); return; }
        this._regSaverDetails.set({
            policyNumber: rs.policyNumber,
            policyCreateDate: rs.policyCreateDate,
        });
    }

    private buildFamilyPlayersList(resp: FamilyPlayersResponseDto): FamilyPlayerDto[] {
        const r = resp as Record<string, unknown>;
        const rawPlayers: Record<string, unknown>[] =
            (resp.familyPlayers as Record<string, unknown>[]) ||
            getPropertyCI<Record<string, unknown>[]>(r, 'familyPlayers', 'players') ||
            [];
        return rawPlayers.map(p => {
            const prior: Record<string, unknown>[] = getPropertyCI<Record<string, unknown>[]>(p, 'priorRegistrations') || [];
            const priorRegs: FamilyPlayerRegistrationDto[] = prior.map(reg => {
                const fin = getPropertyCI<Record<string, unknown>>(reg, 'financials');
                return {
                    registrationId: getPropertyCI<string>(reg, 'registrationId') ?? '',
                    active: !!getPropertyCI<boolean>(reg, 'active'),
                    financials: {
                        feeBase: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeBase') ?? 0),
                        feeProcessing: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeProcessing') ?? 0),
                        feeDiscount: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeDiscount') ?? 0),
                        feeDonation: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeDonation') ?? 0),
                        feeLateFee: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeLateFee') ?? 0),
                        feeTotal: +(getPropertyCI<number>(fin as Record<string, unknown>, 'feeTotal') ?? 0),
                        owedTotal: +(getPropertyCI<number>(fin as Record<string, unknown>, 'owedTotal') ?? 0),
                        paidTotal: +(getPropertyCI<number>(fin as Record<string, unknown>, 'paidTotal') ?? 0),
                    },
                    assignedTeamId: getPropertyCI<string>(reg, 'assignedTeamId') ?? undefined,
                    assignedTeamName: getPropertyCI<string>(reg, 'assignedTeamName') ?? undefined,
                    adnSubscriptionId: (reg['adnSubscriptionId'] as string) ?? undefined,
                    adnSubscriptionStatus: (reg['adnSubscriptionStatus'] as string) ?? undefined,
                    adnSubscriptionAmountPerOccurence: (reg['adnSubscriptionAmountPerOccurence'] as number) ?? undefined,
                    adnSubscriptionBillingOccurences: (reg['adnSubscriptionBillingOccurences'] as number) ?? undefined,
                    adnSubscriptionIntervalLength: (reg['adnSubscriptionIntervalLength'] as number) ?? undefined,
                    adnSubscriptionStartDate: (reg['adnSubscriptionStartDate'] as string) ?? undefined,
                    formFieldValues: (getPropertyCI<Record<string, unknown>>(reg, 'formFieldValues', 'formValues') ?? {}) as Record<string, unknown>,
                };
            });
            return {
                playerId: getPropertyCI<string>(p, 'playerId') ?? '',
                firstName: getPropertyCI<string>(p, 'firstName') ?? '',
                lastName: getPropertyCI<string>(p, 'lastName') ?? '',
                gender: getPropertyCI<string>(p, 'gender') ?? '',
                dob: getPropertyCI<string>(p, 'dob') ?? undefined,
                registered: !!getPropertyCI<boolean>(p, 'registered'),
                selected: !!getPropertyCI<boolean>(p, 'selected') || !!getPropertyCI<boolean>(p, 'registered'),
                priorRegistrations: priorRegs,
            } as FamilyPlayerDto;
        });
    }

    // ── Constraint type extraction (from /family/players response) ─────
    extractConstraintType(resp: FamilyPlayersResponseDto): string | null {
        try {
            const jrf = resp.jobRegForm || getPropertyCI<FamilyPlayersResponseDto['jobRegForm']>(resp as Record<string, unknown>, 'jobRegForm');
            const rawCt = jrf?.constraintType ?? null;
            if (typeof rawCt === 'string' && rawCt.trim()) {
                return rawCt.trim().toUpperCase();
            }
        } catch { /* ignore */ }
        return null;
    }

    // ── Reset ─────────────────────────────────────────────────────────
    reset(): void {
        this._hasFamilyAccount.set(null);
        this._familyPlayers.set([]);
        this._familyPlayersLoading.set(false);
        this._familyUser.set(null);
        this._regSaverDetails.set(null);
        this._debugFamilyPlayersResp.set(null);
    }
}
