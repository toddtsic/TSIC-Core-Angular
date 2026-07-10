import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import { ToastService } from '@shared-ui/toast.service';
import { MyRosterService } from './my-roster.service';
import { MyRosterEmailDialogComponent } from './my-roster-email-dialog.component';
import type { MyRosterPlayerDto } from '@core/api/models/MyRosterPlayerDto';

type SortKey = 'name' | 'role';

@Component({
    selector: 'app-my-roster',
    standalone: true,
    imports: [PhonePipe, MyRosterEmailDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './my-roster.component.html',
    styleUrl: './my-roster.component.scss',
})
export class MyRosterComponent implements OnInit {
    private readonly rosterService = inject(MyRosterService);
    private readonly toast = inject(ToastService);

    readonly isLoading = signal(true);
    readonly allowed = signal(false);
    readonly reason = signal<string | null>(null);
    readonly teamName = signal<string | null>(null);
    readonly players = signal<MyRosterPlayerDto[]>([]);
    readonly selectedIds = signal<Set<string>>(new Set());

    // Job-configured parent terminology (falls back to Mom/Dad server-side).
    readonly momLabel = signal('Mom');
    readonly dadLabel = signal('Dad');

    // Directory browsing — filter + sort replace the old Syncfusion grid affordances.
    readonly filter = signal('');
    readonly sortBy = signal<SortKey>('name');

    readonly emailMode = signal<'all' | 'selected'>('all');
    readonly emailDialogOpen = signal(false);

    /** Placeholder cards while loading. */
    readonly skeletons = [0, 1, 2, 3, 4, 5];

    readonly selectedCount = computed(() => this.selectedIds().size);

    /** Roster filtered by the search box and ordered by the active sort. */
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
        const by = this.sortBy();
        return [...rows].sort((a, b) => by === 'role'
            ? (a.roleName ?? '').localeCompare(b.roleName ?? '') || (a.playerName ?? '').localeCompare(b.playerName ?? '')
            : (a.playerName ?? '').localeCompare(b.playerName ?? ''));
    });

    readonly emailRecipients = computed(() => {
        const mode = this.emailMode();
        const all = this.players();
        const source = mode === 'all' ? all : all.filter(p => this.selectedIds().has(p.registrationId));
        return source
            .filter(p => !!p.email)
            .map(p => ({ name: p.playerName, email: p.email! }));
    });

    readonly emailRegistrationIds = computed(() => this.emailRecipients().length === 0
        ? []
        : (this.emailMode() === 'all'
            ? this.players().filter(p => !!p.email).map(p => p.registrationId)
            : this.players().filter(p => !!p.email && this.selectedIds().has(p.registrationId)).map(p => p.registrationId)));

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
                this.momLabel.set(res.momLabel?.trim() || 'Mom');
                this.dadLabel.set(res.dadLabel?.trim() || 'Dad');
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

    /** "Select all" operates on the shown (filtered) set, preserving any off-filter picks. */
    toggleAll(checked: boolean): void {
        const visible = this.visiblePlayers().map(p => p.registrationId);
        const next = new Set(this.selectedIds());
        if (checked) visible.forEach(id => next.add(id));
        else visible.forEach(id => next.delete(id));
        this.selectedIds.set(next);
    }

    get allChecked(): boolean {
        const visible = this.visiblePlayers();
        return visible.length > 0 && visible.every(p => this.selectedIds().has(p.registrationId));
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
}
