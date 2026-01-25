import { Injectable, inject, computed, signal } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import { PlayerStateService } from './player-state.service';
import { TeamService } from '../team.service';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import type { ApplyDiscountItemDto, ApplyDiscountRequestDto, ApplyDiscountResponseDto } from '@core/api';
import { environment } from '@environments/environment';

// Helper to safely convert number | string to number
function toNumber(value: number | string | undefined | null): number {
    if (value === undefined || value === null) return 0;
    return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
}

export interface LineItem {
    playerId: string;
    playerName: string;
    teamName: string;
    amount: number;
}

@Injectable({ providedIn: 'root' })
export class PaymentService {
    private readonly state = inject(RegistrationWizardService);
    private readonly playerState = inject(PlayerStateService);
    private readonly teams = inject(TeamService);
    private readonly http = inject(HttpClient);

    appliedDiscount = signal(0);
    discountMessage = signal<string | null>(null);
    discountApplying = signal(false);

    lineItems = computed<LineItem[]>(() => {
        const items: LineItem[] = [];
        const players = this.state.familyPlayers()
            .filter(p => p.selected || p.registered)
            .map(p => ({ id: p.playerId, name: `${p.firstName} ${p.lastName}`.trim() }));
        const selTeams = this.playerState.selectedTeams();
        for (const p of players) {
            const teamId = selTeams[p.id];
            if (!teamId || typeof teamId !== 'string') continue; // multi-team not supported in summary yet
            const team = this.teams.getTeamById(teamId);
            const registration = this.getExistingRegistration(p.id);
            const financials = registration?.financials;
            if (!team && !registration) continue;
            const amount = financials ? this.getAmountFromFinancials(financials) : this.getAmount(team);
            items.push({
                playerId: p.id,
                playerName: p.name,
                teamName: team?.teamName || registration?.assignedTeamName || '',
                amount
            });
        }
        return items;
    });

    private readonly existingBalanceTotal = computed(() => this.lineItems()
        .filter(li => !!this.getExistingRegistration(li.playerId)?.financials)
        .reduce((sum, li) => sum + li.amount, 0));

    private readonly newSelectionTotal = computed(() => this.lineItems()
        .filter(li => !this.getExistingRegistration(li.playerId)?.financials)
        .reduce((sum, li) => sum + li.amount, 0));

    totalAmount = computed(() => this.existingBalanceTotal() + this.newSelectionTotal());

    depositTotal = computed(() => {
        const selTeams = this.playerState.selectedTeams();
        let sum = 0;
        for (const li of this.lineItems()) {
            if (this.getExistingRegistration(li.playerId)?.financials) continue; // existing registrations already have balances
            const teamId = selTeams[li.playerId];
            const team = this.teams.getTeamById(teamId as string);
            sum += Number(team?.perRegistrantDeposit ?? 0) || 0;
        }
        return sum;
    });

    isArbScenario = computed(() => !!this.state.adnArb());
    isDepositScenario = computed(() => {
        if (this.isArbScenario()) return false;
        const payable = this.lineItems().filter(li => !this.getExistingRegistration(li.playerId)?.financials);
        if (payable.length === 0) return false;
        return payable.every(li => {
            const team = this.teams.getTeamById(this.playerState.selectedTeams()[li.playerId] as string);
            return (Number(team?.perRegistrantDeposit) > 0 && Number(team?.perRegistrantFee) > 0);
        });
    });

    currentTotal = computed(() => {
        const opt = this.state.paymentOption();
        const existing = this.existingBalanceTotal();
        const base = opt === 'Deposit'
            ? existing + this.depositTotal()
            : this.totalAmount();
        return Math.max(0, base - this.appliedDiscount());
    });

    arbOccurrences = computed(() => this.state.adnArbBillingOccurences() || 10);
    arbIntervalLength = computed(() => this.state.adnArbIntervalLength() || 1);
    arbStartDate = computed(() => {
        const raw = this.state.adnArbStartDate();
        return raw ? new Date(raw) : new Date(Date.now() + 24 * 60 * 60 * 1000);
    });
    arbPerOccurrence = computed(() => {
        const occ = this.arbOccurrences();
        const tot = this.totalAmount();
        return occ > 0 ? Math.round((tot / occ) * 100) / 100 : tot;
    });

    monthLabel(): string { return this.arbIntervalLength() === 1 ? 'month' : 'months'; }

    resetDiscount(): void {
        this.appliedDiscount.set(0);
        this.discountMessage.set(null);
    }

    applyDiscount(code: string): void {
        if (!code || this.discountApplying()) return;
        const option = this.state.paymentOption();
        const items: ApplyDiscountItemDto[] = this.lineItems().map(li => ({
            playerId: li.playerId,
            amount: option === 'Deposit' ? this.getDepositForPlayer(li.playerId) : li.amount
        }));
        if (items.length === 0) {
            this.appliedDiscount.set(0);
            this.discountMessage.set('No payable items eligible for discount');
            return;
        }
        this.discountApplying.set(true);
        this.discountMessage.set(null);
        const req: ApplyDiscountRequestDto = {
            jobPath: this.state.jobPath(),
            code,
            items
        };
        this.http.post<ApplyDiscountResponseDto>(`${environment.apiUrl}/player-registration/apply-discount`, req)
            .subscribe({
                next: (resp: ApplyDiscountResponseDto) => {
                    this.discountApplying.set(false);
                    const total = resp?.totalDiscount ?? 0;
                    if (resp?.success && toNumber(total) > 0) {
                        this.appliedDiscount.set(0); // rely on refreshed financials/owed totals
                        this.discountMessage.set(resp?.message || 'Discount applied');

                        // Merge returned UpdatedFinancials into family players for immediate UI reflection
                        // This optimizes the response without requiring a full async reload
                        if (resp?.updatedFinancials) {
                            this.mergeUpdatedFinancials(resp.updatedFinancials);
                        }

                        // Refresh registrations to pick up persisted financials after discount
                        // This ensures definitive server state, especially for concurrent operations
                        const jobPath = this.state.jobPath();
                        this.state.loadFamilyPlayersOnce(jobPath).catch(err => console.warn('[Payment] refresh after discount failed', err));
                    } else {
                        this.appliedDiscount.set(0);
                        this.discountMessage.set(resp?.message || 'Invalid or ineligible discount code');
                    }
                },
                error: (err: HttpErrorResponse) => {
                    this.discountApplying.set(false);
                    this.appliedDiscount.set(0);
                    this.discountMessage.set(err?.error?.message || err?.message || 'Failed to apply code');
                }
            });
    }

    private getExistingRegistration(playerId: string) {
        const p = this.state.familyPlayers().find(fp => fp.playerId === playerId);
        if (!p?.priorRegistrations?.length) return null;
        const active = p.priorRegistrations.find(r => r.active);
        return active ?? p.priorRegistrations.at(-1) ?? null;
    }

    /**
     * Merge updated financials from discount response into family players' prior registrations.
     * This allows the UI to reflect the discount immediately without waiting for full reload.
     * The async loadFamilyPlayersOnce() ensures definitive server state after this completes.
     * @param updatedFinancials Dictionary of playerId → RegistrationFinancialsDto from ApplyDiscountResponseDto
     */
    private mergeUpdatedFinancials(updatedFinancials: Record<string, any>): void {
        const players = this.state.familyPlayers();
        const updated = players.map(p => {
            const newFinancials = updatedFinancials[p.playerId];
            if (!newFinancials || !p.priorRegistrations?.length) {
                return p;
            }
            // Update the active (or latest) registration's financials
            const active = p.priorRegistrations.findIndex(r => r.active);
            const targetIdx = active >= 0 ? active : p.priorRegistrations.length - 1;
            const updated = { ...p };
            updated.priorRegistrations = [...p.priorRegistrations];
            updated.priorRegistrations[targetIdx] = {
                ...updated.priorRegistrations[targetIdx],
                financials: newFinancials
            };
            return updated;
        });
        this.state.familyPlayers.set(updated);
    }

    private getAmountFromFinancials(financials: any): number {
        if (financials?.owedTotal !== undefined && financials?.owedTotal !== null) {
            const owed = toNumber(financials.owedTotal);
            if (owed >= 0) return owed; // owedTotal is authoritative and already accounts for discounts
        }
        const base = toNumber(financials?.feeBase)
            + toNumber(financials?.feeProcessing)
            + toNumber(financials?.feeLateFee)
            + toNumber(financials?.feeDonation);
        const discount = toNumber(financials?.feeDiscount);
        const paid = toNumber(financials?.paidTotal);
        const due = base - discount - paid;
        return Math.max(0, due);
    }

    private getAmount(team: any): number {
        const v = Number(team?.perRegistrantFee ?? 0);
        return Number.isNaN(v) || v <= 0 ? 100 : v;
    }

    getDepositForPlayer(playerId: string): number {
        const teamId = this.playerState.selectedTeams()[playerId];
        const team = this.teams.getTeamById(teamId as string);
        return Number(team?.perRegistrantDeposit ?? 0) || 0;
    }
}
