import { Component, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '@shared-ui/toast.service';
import { CampGroupsService } from './camp-groups.service';
import type { TeamRosterCountDto, CampPlayerDto } from '@core/api';

@Component({
    selector: 'app-camp-groups',
    standalone: true,
    imports: [CommonModule, FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './camp-groups.component.html',
    styleUrl: './camp-groups.component.scss'
})
export class CampGroupsComponent {
    private readonly service = inject(CampGroupsService);
    private readonly toast = inject(ToastService);

    readonly teams = signal<TeamRosterCountDto[]>([]);
    readonly selectedTeamId = signal<string | null>(null);
    readonly campers = signal<CampPlayerDto[]>([]);
    readonly selectedRegIds = signal<Set<string>>(new Set());

    readonly dayGroups = signal<string[]>([]);
    readonly nightGroups = signal<string[]>([]);

    readonly isLoadingTeams = signal(false);
    readonly isLoadingCampers = signal(false);
    readonly isBulkSaving = signal(false);
    readonly unassignedOnly = signal(false);

    readonly bulkDayGroup = signal<string>('');
    readonly bulkNightGroup = signal<string>('');

    readonly dayColumnVisible = computed(() => this.dayGroups().length > 0);
    readonly nightColumnVisible = computed(() => this.nightGroups().length > 0);

    readonly selectedTeam = computed(() => {
        const id = this.selectedTeamId();
        return id ? this.teams().find(t => t.teamId === id) ?? null : null;
    });

    readonly visibleCampers = computed(() => {
        const all = this.campers();
        if (!this.unassignedOnly()) return all;
        const dayOn = this.dayColumnVisible();
        const nightOn = this.nightColumnVisible();
        return all.filter(c => {
            const dayMissing = dayOn && !c.dayGroup;
            const nightMissing = nightOn && !c.nightGroup;
            return dayMissing || nightMissing;
        });
    });

    readonly selectedCount = computed(() => this.selectedRegIds().size);
    readonly hasAnyGroups = computed(() => this.dayColumnVisible() || this.nightColumnVisible());

    constructor() {
        this.loadTeams();
        this.loadOptions();
    }

    private loadTeams(): void {
        this.isLoadingTeams.set(true);
        this.service.getTeams().subscribe({
            next: teams => {
                this.teams.set(teams);
                this.isLoadingTeams.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load teams', 'danger');
                this.isLoadingTeams.set(false);
            }
        });
    }

    private loadOptions(): void {
        this.service.getOptions().subscribe({
            next: opts => {
                this.dayGroups.set(opts.dayGroups);
                this.nightGroups.set(opts.nightGroups);
            },
            error: err => this.toast.show(err?.error?.message || 'Failed to load group options', 'danger')
        });
    }

    selectTeam(team: TeamRosterCountDto): void {
        if (this.selectedTeamId() === team.teamId) return;
        this.selectedTeamId.set(team.teamId);
        this.selectedRegIds.set(new Set());
        this.loadCampers(team.teamId);
    }

    private loadCampers(teamId: string): void {
        this.isLoadingCampers.set(true);
        this.campers.set([]);
        this.service.getCampers(teamId).subscribe({
            next: campers => {
                this.campers.set(campers);
                this.isLoadingCampers.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load campers', 'danger');
                this.isLoadingCampers.set(false);
            }
        });
    }

    // ── Inline edit ─────────────────────────────────

    onDayGroupChange(camper: CampPlayerDto, value: string): void {
        const previous = camper.dayGroup ?? '';
        const normalized = value || null;
        if ((camper.dayGroup ?? '') === (normalized ?? '')) return;

        this.patchCamperLocal(camper.registrationId, { dayGroup: normalized });

        this.service.updateGroups(camper.registrationId, {
            dayGroup: normalized,
            updateDayGroup: true,
            updateNightGroup: false,
        }).subscribe({
            error: err => {
                this.patchCamperLocal(camper.registrationId, { dayGroup: previous || null });
                this.toast.show(err?.error?.message || 'Failed to update day group', 'danger');
            }
        });
    }

    onNightGroupChange(camper: CampPlayerDto, value: string): void {
        const previous = camper.nightGroup ?? '';
        const normalized = value || null;
        if ((camper.nightGroup ?? '') === (normalized ?? '')) return;

        this.patchCamperLocal(camper.registrationId, { nightGroup: normalized });

        this.service.updateGroups(camper.registrationId, {
            nightGroup: normalized,
            updateDayGroup: false,
            updateNightGroup: true,
        }).subscribe({
            error: err => {
                this.patchCamperLocal(camper.registrationId, { nightGroup: previous || null });
                this.toast.show(err?.error?.message || 'Failed to update night group', 'danger');
            }
        });
    }

    private patchCamperLocal(regId: string, patch: Partial<CampPlayerDto>): void {
        this.campers.update(list =>
            list.map(c => c.registrationId === regId ? { ...c, ...patch } : c)
        );
    }

    // ── Selection ───────────────────────────────────

    toggleSelect(regId: string, checked: boolean): void {
        this.selectedRegIds.update(set => {
            const next = new Set(set);
            if (checked) next.add(regId); else next.delete(regId);
            return next;
        });
    }

    isSelected(regId: string): boolean {
        return this.selectedRegIds().has(regId);
    }

    toggleSelectAllVisible(checked: boolean): void {
        if (!checked) {
            this.selectedRegIds.set(new Set());
            return;
        }
        const ids = this.visibleCampers().map(c => c.registrationId);
        this.selectedRegIds.set(new Set(ids));
    }

    readonly allVisibleSelected = computed(() => {
        const visible = this.visibleCampers();
        if (visible.length === 0) return false;
        const selected = this.selectedRegIds();
        return visible.every(c => selected.has(c.registrationId));
    });

    // ── Bulk apply ──────────────────────────────────

    applyBulkDay(): void {
        const ids = Array.from(this.selectedRegIds());
        if (ids.length === 0) return;
        const value = this.bulkDayGroup() || null;

        this.isBulkSaving.set(true);
        this.service.bulkUpdateGroups({
            registrationIds: ids,
            dayGroup: value,
            updateDayGroup: true,
            updateNightGroup: false,
        }).subscribe({
            next: res => {
                this.toast.show(`Updated day group on ${res.updatedCount}`, 'success');
                this.campers.update(list =>
                    list.map(c => ids.includes(c.registrationId) ? { ...c, dayGroup: value } : c)
                );
                this.bulkDayGroup.set('');
                this.isBulkSaving.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Bulk update failed', 'danger');
                this.isBulkSaving.set(false);
            }
        });
    }

    applyBulkNight(): void {
        const ids = Array.from(this.selectedRegIds());
        if (ids.length === 0) return;
        const value = this.bulkNightGroup() || null;

        this.isBulkSaving.set(true);
        this.service.bulkUpdateGroups({
            registrationIds: ids,
            nightGroup: value,
            updateDayGroup: false,
            updateNightGroup: true,
        }).subscribe({
            next: res => {
                this.toast.show(`Updated night group on ${res.updatedCount}`, 'success');
                this.campers.update(list =>
                    list.map(c => ids.includes(c.registrationId) ? { ...c, nightGroup: value } : c)
                );
                this.bulkNightGroup.set('');
                this.isBulkSaving.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Bulk update failed', 'danger');
                this.isBulkSaving.set(false);
            }
        });
    }
}
