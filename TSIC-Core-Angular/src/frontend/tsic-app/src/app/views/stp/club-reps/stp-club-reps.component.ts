import { Component, ChangeDetectionStrategy, inject, signal, computed, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { GridAllModule, GridComponent, ToolbarItems } from '@syncfusion/ej2-angular-grids';
import { environment } from '@environments/environment';
import { JobPulseService } from '@infrastructure/services/job-pulse.service';
import { AuthService } from '@infrastructure/services/auth.service';
import { Roles } from '@infrastructure/constants/roles.constants';
import type { StpClubRepDto } from '@core/api';

/**
 * Stay-to-Play club rep summary. Ports the one legacy STP admin screen that carried
 * real function (Controllers/STP/Admin/STPClubRepsController + Views/STPClubReps).
 *
 * The grid IS the deliverable: a housing vendor reads the team counts to size room
 * blocks, and takes the data away with the Excel toolbar button. Legacy's batch-email
 * half is deliberately not ported — Stay-to-Play is a data transfer to a third party,
 * not a mailing service we run on their behalf.
 */
@Component({
    selector: 'app-stp-club-reps',
    standalone: true,
    imports: [CommonModule, GridAllModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './stp-club-reps.component.html',
    styleUrls: ['./stp-club-reps.component.scss'],
})
export class StpClubRepsComponent {
    private readonly http = inject(HttpClient);
    private readonly pulseService = inject(JobPulseService);
    private readonly auth = inject(AuthService);
    private readonly endpoint = `${environment.apiUrl}/stp/club-reps`;

    // BEnableSTP off = the director has not consented to share this event's data. An admin
    // can still land here (their nav leaf is gated on the flag, but a bookmark or a
    // mid-session flip both get past that), and the API answers 403 either way — so
    // without this the screen would tell a director they lack permission to their own data.
    // Read off the pulse rather than the 403 body: the same 403 covers a genuine role
    // failure, and guessing which one from a message string is not a distinction to bet on.
    readonly stpDisabled = computed(() => this.pulseService.pulse()?.enableStayToPlay === false);

    // Vendors get told nothing about why. The flag is the director's data-sharing decision,
    // and an STPAdmin is the third party it concerns — handing them "the director switched
    // you off" gives them someone to lean on to reverse it.
    readonly isVendor = computed(() => {
        const user = this.auth.currentUser();
        return user?.role === Roles.StpAdmin || !!user?.roles?.includes(Roles.StpAdmin);
    });

    readonly isLoading = signal(false);
    readonly errorMessage = signal('');
    readonly rows = signal<StpClubRepDto[]>([]);

    readonly jobName = computed(() => this.rows()[0]?.jobName ?? '');
    readonly totalActiveTeams = computed(() =>
        this.rows().reduce((sum, r) => sum + r.activeTeamCount, 0));

    readonly toolbar: ToolbarItems[] = ['ExcelExport'];
    readonly grid = viewChild.required<GridComponent>('grid');

    constructor() {
        this.load();
    }

    load(): void {
        this.isLoading.set(true);
        this.errorMessage.set('');

        this.http.get<StpClubRepDto[]>(this.endpoint).subscribe({
            next: rows => {
                this.rows.set(rows);
                this.isLoading.set(false);
            },
            error: err => {
                this.isLoading.set(false);
                if (err.status === 401) {
                    this.errorMessage.set('You must be logged in to view this screen.');
                } else if (err.status === 403) {
                    this.errorMessage.set('You do not have permission to view this event\'s club reps.');
                } else {
                    this.errorMessage.set(err.error?.message || 'Failed to load club reps.');
                }
            },
        });
    }

    onToolbarClick(args: { item?: { id?: string } }): void {
        if (args.item?.id?.includes('excelexport')) {
            this.grid().excelExport();
        }
    }
}
