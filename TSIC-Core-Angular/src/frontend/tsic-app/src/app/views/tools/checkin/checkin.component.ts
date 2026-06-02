import { Component, ChangeDetectionStrategy, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ToastService } from '@shared-ui/toast.service';
import { JobService } from '@infrastructure/services/job.service';
import { CheckinService } from './checkin.service';
import { RegistrationDetailPanelComponent } from '../../search/registrations/components/registration-detail-panel.component';
import { RegistrationSearchService } from '../../search/registrations/services/registration-search.service';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import type {
    TeamRosterCountDto,
    TeamCheckinRowDto,
    PlayerCheckinRowDto,
    RegistrationDetailDto,
} from '@core/api';

type CheckinMode = 'teams' | 'players';

// Mirrors TSIC.Domain.Constants.JobConstants — team-based job types default to
// team check-in; everything else (Club tryouts, Camp) defaults to player check-in.
const JOB_TYPE_TOURNAMENT = 2;
const JOB_TYPE_LEAGUE = 3;

@Component({
    selector: 'app-checkin',
    standalone: true,
    imports: [CommonModule, RegistrationDetailPanelComponent, PhonePipe],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './checkin.component.html',
    styleUrl: './checkin.component.scss',
})
export class CheckinComponent {
    private readonly service = inject(CheckinService);
    private readonly toast = inject(ToastService);
    private readonly jobService = inject(JobService);
    private readonly searchService = inject(RegistrationSearchService);

    readonly mode = signal<CheckinMode>('teams');

    // Team mode
    readonly teamRoster = signal<TeamCheckinRowDto[]>([]);
    readonly isLoadingTeamRoster = signal(false);

    // Player mode
    readonly teams = signal<TeamRosterCountDto[]>([]);
    readonly selectedTeamId = signal<string | null>(null);
    readonly players = signal<PlayerCheckinRowDto[]>([]);
    readonly isLoadingTeams = signal(false);
    readonly isLoadingPlayers = signal(false);

    // Ids of rows with an in-flight check-in/undo (disables their button).
    readonly busyIds = signal<Set<string>>(new Set());

    // Detail/payment fly-in — reuses the Search registration detail panel as-is.
    readonly selectedDetail = signal<RegistrationDetailDto | null>(null);
    readonly isPanelOpen = signal(false);

    readonly selectedTeam = computed(() => {
        const id = this.selectedTeamId();
        return id ? this.teams().find(t => t.teamId === id) ?? null : null;
    });

    readonly teamCheckedInCount = computed(
        () => this.teamRoster().filter(t => !!t.checkedInTs).length
    );
    readonly playerCheckedInCount = computed(
        () => this.players().filter(p => !!p.checkedInTs).length
    );

    // Roommate is a camp-only field; only surface the column when at least one
    // loaded player actually has a value, so non-camp/early rosters stay clean.
    readonly showRoommate = computed(
        () => this.players().some(p => !!p.roommatePref)
    );

    // Client-side roster filter — camp sessions can have hundreds of players.
    readonly playerSearch = signal('');
    readonly filteredPlayers = computed(() => {
        const q = this.playerSearch().trim().toLowerCase();
        const list = this.players();
        if (!q) return list;
        return list.filter(p =>
            `${p.lastName} ${p.firstName}`.toLowerCase().includes(q)
            || (p.clubName?.toLowerCase().includes(q) ?? false)
            || (p.schoolName?.toLowerCase().includes(q) ?? false)
        );
    });

    constructor() {
        const jobType = this.jobService.currentJob()?.jobTypeId;
        const teamBased = jobType === JOB_TYPE_TOURNAMENT || jobType === JOB_TYPE_LEAGUE;
        this.mode.set(teamBased ? 'teams' : 'players');
        this.loadForMode();
    }

    setMode(mode: CheckinMode): void {
        if (this.mode() === mode) return;
        this.mode.set(mode);
        this.loadForMode();
    }

    private loadForMode(): void {
        if (this.mode() === 'teams') {
            if (this.teamRoster().length === 0) this.loadTeamRoster();
        } else if (this.teams().length === 0) {
            this.loadTeams();
        }
    }

    // ── Team mode ───────────────────────────────────

    private loadTeamRoster(): void {
        this.isLoadingTeamRoster.set(true);
        this.service.getTeamRoster().subscribe({
            next: rows => {
                this.teamRoster.set(rows);
                this.isLoadingTeamRoster.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load team roster', 'danger');
                this.isLoadingTeamRoster.set(false);
            },
        });
    }

    toggleTeam(row: TeamCheckinRowDto): void {
        if (this.isBusy(row.teamId)) return;
        this.setBusy(row.teamId, true);

        if (row.checkedInTs) {
            this.service.undoTeam(row.teamId).subscribe({
                next: () => {
                    this.patchTeam(row.teamId, { checkedInTs: null, checkedInByRegId: null });
                    this.setBusy(row.teamId, false);
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to undo check-in', 'danger');
                    this.setBusy(row.teamId, false);
                },
            });
        } else {
            this.service.checkInTeam(row.teamId).subscribe({
                next: state => {
                    this.patchTeam(row.teamId, {
                        checkedInTs: state.checkedInTs,
                        checkedInByRegId: state.checkedInByRegId,
                    });
                    this.setBusy(row.teamId, false);
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to check in team', 'danger');
                    this.setBusy(row.teamId, false);
                },
            });
        }
    }

    private patchTeam(teamId: string, patch: Partial<TeamCheckinRowDto>): void {
        this.teamRoster.update(list =>
            list.map(t => (t.teamId === teamId ? { ...t, ...patch } : t))
        );
    }

    // ── Player mode ─────────────────────────────────

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
            },
        });
    }

    selectTeam(team: TeamRosterCountDto): void {
        if (this.selectedTeamId() === team.teamId) return;
        this.selectedTeamId.set(team.teamId);
        this.loadPlayers(team.teamId);
    }

    private loadPlayers(teamId: string): void {
        this.isLoadingPlayers.set(true);
        this.players.set([]);
        this.playerSearch.set(''); // reset filter when switching sessions
        this.service.getPlayers(teamId).subscribe({
            next: players => {
                this.players.set(players);
                this.isLoadingPlayers.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load players', 'danger');
                this.isLoadingPlayers.set(false);
            },
        });
    }

    togglePlayer(row: PlayerCheckinRowDto): void {
        if (this.isBusy(row.registrationId)) return;
        this.setBusy(row.registrationId, true);

        if (row.checkedInTs) {
            this.service.undoPlayer(row.registrationId).subscribe({
                next: () => {
                    this.patchPlayer(row.registrationId, { checkedInTs: null, checkedInByRegId: null });
                    this.setBusy(row.registrationId, false);
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to undo check-in', 'danger');
                    this.setBusy(row.registrationId, false);
                },
            });
        } else {
            this.service.checkInPlayer(row.registrationId).subscribe({
                next: state => {
                    this.patchPlayer(row.registrationId, {
                        checkedInTs: state.checkedInTs,
                        checkedInByRegId: state.checkedInByRegId,
                    });
                    this.setBusy(row.registrationId, false);
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to check in player', 'danger');
                    this.setBusy(row.registrationId, false);
                },
            });
        }
    }

    private patchPlayer(regId: string, patch: Partial<PlayerCheckinRowDto>): void {
        this.players.update(list =>
            list.map(p => (p.registrationId === regId ? { ...p, ...patch } : p))
        );
    }

    viewMedForm(row: PlayerCheckinRowDto): void {
        this.service.viewMedForm(row.playerUserId).subscribe({
            next: blob => {
                const url = URL.createObjectURL(blob);
                window.open(url, '_blank');
                // Revoke after the new tab has had a chance to load the blob.
                setTimeout(() => URL.revokeObjectURL(url), 60_000);
            },
            error: () =>
                this.toast.show(`No med form on file for ${row.firstName} ${row.lastName}`, 'warning'),
        });
    }

    /**
     * Build a printable check-in sheet for the selected session and print it via a
     * hidden iframe (client-side "Save as PDF"). Static by design: balance is plain
     * text, the med-form box is pre-checked from hasMedForm, and Arrived is a blank
     * box to mark by hand. Prints the full session roster regardless of the search filter.
     */
    printSheet(): void {
        const team = this.selectedTeam();
        if (!team) return;
        const rows = this.players();
        const showRoom = this.showRoommate();

        const escMap: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' };
        const esc = (s: string | null | undefined): string =>
            (s ?? '').replace(/[&<>"]/g, c => escMap[c] ?? c);

        const phone = (s: string | null | undefined): string => {
            if (!s) return '';
            let d = s.replace(/\D/g, '');
            if (d.length === 11 && d.startsWith('1')) d = d.slice(1);
            return d.length === 10 ? `${d.slice(0, 3)}-${d.slice(3, 6)}-${d.slice(6)}` : esc(s);
        };

        const CHECKED = '&#9745;';
        const EMPTY = '&#9744;';

        const contactCell = (p: PlayerCheckinRowDto): string => {
            const lines: string[] = [];
            if (p.momName || p.momCellphone) lines.push(`${esc(p.momName)} ${phone(p.momCellphone)}`.trim());
            if (p.dadName || p.dadCellphone) lines.push(`${esc(p.dadName)} ${phone(p.dadCellphone)}`.trim());
            return lines.join('<br>') || '—';
        };

        const body = rows.map((p, i) => {
            const bal = p.owedTotal > 0 ? `$${p.owedTotal.toFixed(2)} due` : 'Paid';
            const med = p.hasMedForm
                ? `${CHECKED} Uploaded&nbsp;&nbsp;${EMPTY} Pending`
                : `${EMPTY} Uploaded&nbsp;&nbsp;${CHECKED} Pending`;
            const dn = `${esc(p.dayGroup) || 'N.A.'} / ${esc(p.nightGroup) || 'N.A.'}`;
            const room = showRoom ? `<td>${esc(p.roommatePref) || '—'}</td>` : '';
            return '<tr>'
                + `<td class="num">${i + 1}</td>`
                + `<td>${esc(p.lastName)}, ${esc(p.firstName)}</td>`
                + `<td>${dn}</td>`
                + room
                + `<td class="contact">${contactCell(p)}</td>`
                + `<td class="bal">${bal}</td>`
                + `<td class="med">${med}</td>`
                + `<td class="arrived">${EMPTY}</td>`
                + '</tr>';
        }).join('');

        const css =
            '@page{size:landscape;margin:.4in}'
            + '*{box-sizing:border-box}'
            + 'body{font-family:Arial,Helvetica,sans-serif;font-size:9pt;color:#000;margin:0}'
            + '.hd{display:flex;justify-content:space-between;align-items:baseline;border-bottom:2px solid #000;padding-bottom:4px;margin-bottom:8px}'
            + '.hd h1{font-size:14pt;margin:0}.hd .sub{font-size:10pt}'
            + 'table{width:100%;border-collapse:collapse}'
            + 'th,td{border:1px solid #999;padding:3px 5px;text-align:left;vertical-align:top}'
            + 'thead th{background:#eee;font-size:8pt;text-transform:uppercase;letter-spacing:.03em}'
            + 'tbody tr{page-break-inside:avoid}'
            + 'td.num{text-align:center;width:26px;color:#555}'
            + 'td.bal{white-space:nowrap}td.med{white-space:nowrap;font-size:8.5pt}'
            + 'td.contact{font-size:8pt}'
            + 'td.arrived{text-align:center;font-size:14pt;width:70px}';

        const html =
            '<!doctype html><html><head><meta charset="utf-8">'
            + `<title>Check-In — ${esc(team.teamName)}</title>`
            + `<style>${css}</style></head><body>`
            + `<div class="hd"><h1>Check-In Sheet</h1><div class="sub">${esc(team.teamName)} · ${rows.length} players</div></div>`
            + '<table><thead><tr>'
            + '<th class="num">#</th><th>Player</th><th>Day/Night</th>'
            + (showRoom ? '<th>Roommate</th>' : '')
            + '<th>Contacts</th><th>Balance</th><th>Med Form</th><th>Arrived</th>'
            + '</tr></thead><tbody>' + body + '</tbody></table>'
            + '</body></html>';

        const iframe = document.createElement('iframe');
        iframe.setAttribute('aria-hidden', 'true');
        iframe.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0';
        document.body.appendChild(iframe);
        const win = iframe.contentWindow;
        if (!win) { iframe.remove(); return; }
        win.document.open();
        win.document.write(html);
        win.document.close();
        // Let the iframe lay out before invoking the print dialog, then clean up.
        setTimeout(() => {
            win.focus();
            win.print();
            setTimeout(() => iframe.remove(), 1000);
        }, 300);
    }

    // ── Detail / payment fly-in ─────────────────────
    // Opens the same Search detail panel (Accounting tab has record-check /
    // charge-cc / refund already wired). Player rows open their own registration;
    // team rows open the clubrep registration (team/club payment scope).

    openPlayerDetail(row: PlayerCheckinRowDto): void {
        this.openDetail(row.registrationId);
    }

    openTeamDetail(row: TeamCheckinRowDto): void {
        if (!row.clubRepRegistrationId) {
            this.toast.show('No club rep on this team to bill.', 'warning');
            return;
        }
        this.openDetail(row.clubRepRegistrationId);
    }

    private openDetail(registrationId: string): void {
        this.searchService.getRegistrationDetail(registrationId).subscribe({
            next: detail => {
                this.selectedDetail.set(detail);
                this.isPanelOpen.set(true);
            },
            error: () => this.toast.show('Failed to load registration detail', 'danger'),
        });
    }

    closePanel(): void {
        this.isPanelOpen.set(false);
        this.selectedDetail.set(null);
    }

    /** A payment or edit landed in the panel — refresh the affected balance. */
    onDetailSaved(): void {
        if (this.mode() === 'teams') {
            this.loadTeamRoster();
        } else {
            const teamId = this.selectedTeamId();
            if (teamId) this.loadPlayers(teamId);
        }
    }

    // ── Busy-row helpers ────────────────────────────

    isBusy(id: string): boolean {
        return this.busyIds().has(id);
    }

    private setBusy(id: string, busy: boolean): void {
        this.busyIds.update(set => {
            const next = new Set(set);
            if (busy) next.add(id); else next.delete(id);
            return next;
        });
    }
}
