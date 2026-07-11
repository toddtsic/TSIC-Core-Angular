import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import { ReportingService } from '@infrastructure/services/reporting.service';
import { ToastService } from '@shared-ui/toast.service';
import { MyRosterService } from './my-roster.service';
import { MyRosterEmailDialogComponent } from './my-roster-email-dialog.component';
import type { MyRosterPlayerDto } from '@core/api/models/MyRosterPlayerDto';

@Component({
    selector: 'app-my-roster',
    standalone: true,
    imports: [PhonePipe, DatePipe, MyRosterEmailDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './my-roster.component.html',
    styleUrl: './my-roster.component.scss',
})
export class MyRosterComponent implements OnInit {
    private readonly rosterService = inject(MyRosterService);
    private readonly reporting = inject(ReportingService);
    private readonly toast = inject(ToastService);

    readonly isLoading = signal(true);
    readonly allowed = signal(false);
    readonly reason = signal<string | null>(null);
    readonly teamName = signal<string | null>(null);
    readonly agegroupName = signal<string | null>(null);
    readonly players = signal<MyRosterPlayerDto[]>([]);
    readonly selectedIds = signal<Set<string>>(new Set());

    // Directory browsing — filter replaces the old Syncfusion grid affordances.
    readonly filter = signal('');

    readonly emailMode = signal<'all' | 'selected'>('all');
    readonly emailDialogOpen = signal(false);
    readonly isDownloading = signal(false);

    /** Placeholder cards while loading. */
    readonly skeletons = [0, 1, 2, 3, 4, 5];

    readonly selectedCount = computed(() => this.selectedIds().size);

    /** Count of players only (staff/coaches excluded) for the hero badge. */
    readonly playerCount = computed(() => this.players().filter(p => !this.isStaff(p)).length);

    /** Roster filtered by the search box and ordered by name. */
    readonly visiblePlayers = computed(() => {
        const q = this.filter().trim().toLowerCase();
        let rows = this.players();
        if (q) {
            rows = rows.filter(p =>
                (p.playerName ?? '').toLowerCase().includes(q)
                || (p.firstName ?? '').toLowerCase().includes(q)
                || (p.lastName ?? '').toLowerCase().includes(q)
                || (p.position ?? '').toLowerCase().includes(q));
        }
        // Staff/coaches sort to the top, then players — each group alphabetical by name.
        return [...rows].sort((a, b) =>
            (Number(this.isStaff(b)) - Number(this.isStaff(a)))
            || (a.playerName ?? '').localeCompare(b.playerName ?? ''));
    });

    // The modal list mirrors the SELECTION — every checked teammate by name, regardless of whether
    // they carry a direct email. Address resolution happens server-side (a Player resolves to Mom +
    // Dad + own email), so filtering on p.email here would silently drop reachable players and make
    // "Email Selected" inconsistent with "Email All".
    readonly emailRecipients = computed(() => {
        const mode = this.emailMode();
        const all = this.players();
        const source = mode === 'all' ? all : all.filter(p => this.selectedIds().has(p.registrationId));
        return source.map(p => ({ id: p.registrationId, name: p.playerName }));
    });

    readonly emailRegistrationIds = computed(() => this.emailMode() === 'all'
        ? this.players().map(p => p.registrationId)
        : this.players().filter(p => this.selectedIds().has(p.registrationId)).map(p => p.registrationId));

    ngOnInit(): void {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.rosterService.get().subscribe({
            next: (res) => {
                this.isLoading.set(false);
                this.allowed.set(res.allowed);
                this.reason.set(res.reason ?? null);
                this.teamName.set(res.teamName ?? null);
                this.agegroupName.set(res.agegroupName ?? null);
                this.players.set((res.players ?? []) as MyRosterPlayerDto[]);
                this.selectedIds.set(new Set());
            },
            error: (err) => {
                this.isLoading.set(false);
                this.allowed.set(false);
                this.reason.set(err?.error?.message ?? 'Failed to load roster.');
            },
        });
    }

    // ── Display helpers (pure) ────────────────────────────────────────────────

    displayName(p: MyRosterPlayerDto): string {
        const natural = `${p.firstName ?? ''} ${p.lastName ?? ''}`.trim();
        return natural || p.playerName || 'Unknown';
    }

    initials(p: MyRosterPlayerDto): string {
        const f = (p.firstName ?? '').trim();
        const l = (p.lastName ?? '').trim();
        const combined = `${f ? f[0] : ''}${l ? l[0] : ''}`.toUpperCase();
        return combined || (p.playerName ?? '?').trim().charAt(0).toUpperCase() || '?';
    }

    /** Staff/coaches get the warm accent; players get the primary accent. */
    isStaff(p: MyRosterPlayerDto): boolean {
        return (p.roleName ?? '').toLowerCase() !== 'player';
    }

    gradLabel(p: MyRosterPlayerDto): string | null {
        return p.gradYear ? `Class of '${String(p.gradYear).slice(-2)}` : null;
    }

    momName(p: MyRosterPlayerDto): string {
        return `${p.momFirstName ?? ''} ${p.momLastName ?? ''}`.trim();
    }

    dadName(p: MyRosterPlayerDto): string {
        return `${p.dadFirstName ?? ''} ${p.dadLastName ?? ''}`.trim();
    }

    hasMom(p: MyRosterPlayerDto): boolean {
        return !!(p.momFirstName || p.momLastName || p.momEmail || p.momCellphone);
    }

    hasDad(p: MyRosterPlayerDto): boolean {
        return !!(p.dadFirstName || p.dadLastName || p.dadEmail || p.dadCellphone);
    }

    hasParents(p: MyRosterPlayerDto): boolean {
        return this.hasMom(p) || this.hasDad(p);
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    toggleRow(regId: string, checked: boolean): void {
        const next = new Set(this.selectedIds());
        if (checked) next.add(regId); else next.delete(regId);
        this.selectedIds.set(next);
    }

    isSelected(regId: string): boolean {
        return this.selectedIds().has(regId);
    }

    // ── Email ─────────────────────────────────────────────────────────────────

    openEmailAll(): void {
        if (this.players().length === 0) { return; }
        this.emailMode.set('all');
        this.emailDialogOpen.set(true);
    }

    openEmailSelected(): void {
        if (this.selectedIds().size === 0) {
            this.toast.show('Select at least one recipient.', 'warning', 3000);
            return;
        }
        this.emailMode.set('selected');
        this.emailDialogOpen.set(true);
    }

    closeEmail(): void {
        this.emailDialogOpen.set(false);
    }

    onEmailSent(): void {
        this.emailDialogOpen.set(false);
        this.selectedIds.set(new Set());
    }

    // ── PDF ─────────────────────────────────────────────────────────────────────

    /** Downloads the team roster as a PDF listing (server enforces the same visibility gate). */
    downloadPdf(): void {
        if (this.isDownloading()) { return; }
        this.isDownloading.set(true);
        this.rosterService.downloadPdf().subscribe({
            next: (res) => {
                this.reporting.triggerDownload(res, 'Team-Roster');
                this.isDownloading.set(false);
            },
            error: () => {
                this.toast.show('Could not generate the roster PDF. Please try again.', 'danger', 4000);
                this.isDownloading.set(false);
            },
        });
    }
}
