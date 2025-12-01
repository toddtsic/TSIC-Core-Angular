import { Injectable, inject, computed, signal } from '@angular/core';
import { RegistrationWizardService } from '../registration-wizard.service';
import { PlayerStateService } from './player-state.service';
import { TeamService } from '../team.service';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import type { ApplyDiscountItemDto, ApplyDiscountRequestDto, ApplyDiscountResponseDto } from '../../../core/api/models';
import { environment } from '../../../../environments/environment';

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
            .map(p => ({ id: p.playerId, name: `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim() }));
        const selTeams = this.playerState.selectedTeams();
        for (const p of players) {
            const teamId = selTeams[p.id];
            if (!teamId || typeof teamId !== 'string') continue; // multi-team not supported in summary yet
            const team = this.teams.getTeamById(teamId);
            if (!team) continue;
            items.push({
                playerId: p.id,
                playerName: p.name,
                teamName: team.teamName,
                amount: this.getAmount(team)
            });
        }
        return items;
    });

    totalAmount = computed(() => this.lineItems().reduce((s, i) => s + i.amount, 0));

    depositTotal = computed(() => {
        const selTeams = this.playerState.selectedTeams();
        let sum = 0;
        for (const li of this.lineItems()) {
            const teamId = selTeams[li.playerId];
            const team = this.teams.getTeamById(teamId as string);
            sum += Number(team?.perRegistrantDeposit ?? 0) || 0;
        }
        return sum;
    });

    isArbScenario = computed(() => !!this.state.adnArb());
    isDepositScenario = computed(() => {
        if (this.isArbScenario()) return false;
        if (this.lineItems().length === 0) return false;
        return this.lineItems().every(li => {
            const team = this.teams.getTeamById(this.playerState.selectedTeams()[li.playerId] as string);
            return (Number(team?.perRegistrantDeposit) > 0 && Number(team?.perRegistrantFee) > 0);
        });
    });

    currentTotal = computed(() => {
        const opt = this.state.paymentOption();
        const base = opt === 'Deposit' ? this.depositTotal() : this.totalAmount();
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
        this.discountApplying.set(true);
        this.discountMessage.set(null);
        const req: ApplyDiscountRequestDto = {
            jobId: this.state.jobId(),
            familyUserId: this.state.familyUser()?.familyUserId!,
            code,
            items
        };
        this.http.post<ApplyDiscountResponseDto>(`${environment.apiUrl}/registration/apply-discount`, req)
            .subscribe({
                next: resp => {
                    this.discountApplying.set(false);
                    const total = resp?.totalDiscount ?? 0;
                    if (resp?.success && total > 0) {
                        const applied = Math.round((total + Number.EPSILON) * 100) / 100;
                        this.appliedDiscount.set(applied);
                        this.discountMessage.set(`Discount applied: ${applied.toLocaleString(undefined, { style: 'currency', currency: 'USD' })}`);
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
