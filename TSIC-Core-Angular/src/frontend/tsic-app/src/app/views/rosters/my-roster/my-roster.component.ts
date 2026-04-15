import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { GridAllModule } from '@syncfusion/ej2-angular-grids';
import { PhonePipe } from '@infrastructure/pipes/phone.pipe';
import { ToastService } from '@shared-ui/toast.service';
import { MyRosterService } from './my-roster.service';
import { MyRosterEmailDialogComponent } from './my-roster-email-dialog.component';
import type { MyRosterPlayerDto } from '@core/api/models/MyRosterPlayerDto';

@Component({
    selector: 'app-my-roster',
    standalone: true,
    imports: [GridAllModule, PhonePipe, MyRosterEmailDialogComponent],
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

    readonly emailMode = signal<'all' | 'selected'>('all');
    readonly emailDialogOpen = signal(false);

    readonly selectedCount = computed(() => this.selectedIds().size);

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
                this.players.set(res.players ?? []);
                this.selectedIds.set(new Set());
            },
            error: (err) => {
                this.isLoading.set(false);
                this.allowed.set(false);
                this.reason.set(err?.error?.message ?? 'Failed to load roster.');
            },
        });
    }

    toggleRow(regId: string, checked: boolean): void {
        const next = new Set(this.selectedIds());
        if (checked) next.add(regId); else next.delete(regId);
        this.selectedIds.set(next);
    }

    isSelected(regId: string): boolean {
        return this.selectedIds().has(regId);
    }

    toggleAll(checked: boolean): void {
        if (checked) {
            this.selectedIds.set(new Set(this.players().map(p => p.registrationId)));
        } else {
            this.selectedIds.set(new Set());
        }
    }

    get allChecked(): boolean {
        const rows = this.players();
        return rows.length > 0 && this.selectedIds().size === rows.length;
    }

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
