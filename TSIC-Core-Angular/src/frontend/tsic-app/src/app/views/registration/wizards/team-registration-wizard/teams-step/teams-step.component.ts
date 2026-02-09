import {
    Component,
    OnInit,
    computed,
    inject,
    signal,
    output,
    CUSTOM_ELEMENTS_SCHEMA,
    ViewChild,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import {
    GridAllModule,
    GridComponent,
    SortService,
    FilterService,
    ToolbarService,
    ExcelExportService,
    PageService,
    ResizeService,
    QueryCellInfoEventArgs,
} from '@syncfusion/ej2-angular-grids';
// Removed toolbar click handler usage; no need for ClickEventArgs
import { TeamRegistrationService } from '../services/team-registration.service';
import { TeamPaymentService } from '../services/team-payment.service';
import type {
    SuggestedTeamNameDto,
    RegisteredTeamDto,
    AgeGroupDto,
    ClubTeamDto,
} from '@core/api';
import { JobContextService } from '@infrastructure/services/job-context.service';
import { FormFieldDataService } from '@infrastructure/services/form-field-data.service';
import { ToastService } from '@shared-ui/toast.service';
import {
    TeamRegistrationModalComponent,
    type RegistrationData,
} from './modals/team-registration-modal/team-registration-modal.component';

interface FinancialSummary {
    feesTotal: number;
    paidTotal: number;
    balanceDue: number;
    depositDueTotal: number;
    additionalDueTotal: number;
}

/**
 * Teams Step Component
 *
 * Manages team registration for a club rep registering teams for an event.
 * Features:
 * - Modal-based team registration
 * - Team suggestions from historical registrations
 * - Real-time financial summary
 * - Support for multiple registrations per session ("Add Another")
 */
@Component({
    selector: 'app-teams-step',
    standalone: true,
    imports: [
        CommonModule,
        FormsModule,
        GridAllModule,
        TeamRegistrationModalComponent,
    ],
    schemas: [CUSTOM_ELEMENTS_SCHEMA],
    providers: [
        SortService,
        FilterService,
        ToolbarService,
        ExcelExportService,
        PageService,
        ResizeService,
    ],
    templateUrl: './teams-step.component.html',
    styleUrls: ['./teams-step.component.scss'],
})
export class TeamsStepComponent implements OnInit {
    // Injected services
    private readonly teamService = inject(TeamRegistrationService);
    private readonly paymentService = inject(TeamPaymentService);
    private readonly jobContext = inject(JobContextService);
    private readonly fieldData = inject(FormFieldDataService);
    private readonly route = inject(ActivatedRoute);
    private readonly toast = inject(ToastService);

    // Outputs
    proceed = output<void>();

    // Grid reference
    @ViewChild('grid') public grid!: GridComponent;

    // Sort settings for 2-state sorting (no unsorted state)
    public sortOptions = { allowUnsort: false };

    // Data signals (private, exposed via computed properties)
    private readonly clubNameSignal = signal<string | null>(null);
    private readonly suggestedTeamNamesSignal = signal<SuggestedTeamNameDto[]>(
        [],
    );
    private readonly registeredTeamsSignal = signal<RegisteredTeamDto[]>([]);
    private readonly ageGroupsSignal = signal<AgeGroupDto[]>([]);
    private readonly clubTeamsSignal = signal<ClubTeamDto[]>([]);
    private readonly recentlyAddedTeamNames = signal<string[]>([]);

    // Metadata for payment configuration
    private readonly paymentMethodsAllowedCode = signal<number>(1);
    private readonly bAddProcessingFees = signal<boolean>(false);
    private readonly bApplyProcessingFeesToTeamDeposit = signal<boolean>(false);

    // UI state
    readonly isLoading = signal(false);
    readonly errorMessage = signal<string | null>(null);
    readonly showRegistrationModal = signal(false);
    readonly isRegistering = signal(false);
    private readonly clubId = signal<number | null>(null);
    readonly attemptedProceed = signal(false);

    // Refund policy state
    readonly refundPolicyHtml = signal<string | null>(null);
    readonly refundPolicyAccepted = signal(false);
    readonly refundPolicyLocked = signal(false);

    // Public data accessors - expose signals via computed properties
    readonly clubName = computed(() => this.clubNameSignal());
    readonly suggestedTeamNames = computed(() => this.suggestedTeamNamesSignal());
    readonly registeredTeams = computed(() => this.registeredTeamsSignal());
    readonly ageGroups = computed(() => this.ageGroupsSignal());
    readonly clubTeams = computed(() => this.clubTeamsSignal());

    // Public computed properties
    readonly availableLevelsOfPlay = computed(() =>
        this.fieldData.getOptionsForDataSource('List_Lops'),
    );

    readonly levelsOfPlayOptions = computed(() =>
        this.availableLevelsOfPlay().map((opt) => ({
            value: opt.value,
            label: opt.label,
        })),
    );

    readonly eventName = computed(() => {
        const jp = this.jobContext.jobPath();
        if (!jp) return 'This Event';
        return jp.toUpperCase().replaceAll('-', ' ');
    });

    readonly displayedAgeGroups = computed(() => this.getDisplayedAgeGroups());

    readonly filteredAgeGroupsForModal = computed(() =>
        this.getFilteredAgeGroupsForModal(),
    );

    readonly suggestedTeamNamesForModal = computed(() => {
        const excludeSet = new Set([
            ...this.registeredTeams().map((t) => t.teamName.trim().toLowerCase()),
            ...this.recentlyAddedTeamNames().map((n) => n.trim().toLowerCase()),
        ]);
        return this.suggestedTeamNames().filter(
            (s) => !excludeSet.has(s.teamName.trim().toLowerCase()),
        );
    });

    readonly financialSummary = computed(() => this.calculateFinancialSummary());

    ngOnInit(): void {
        // Load metadata immediately. If clubName input is set, use it; if refresh, backend derives from token.
        this.loadTeamsMetadata();
    }

    /**
     * Public reset method for when rep/club switches.
     * Clears all team data and UI state, prepares for fresh metadata load.
     */
    reset(): void {
        // Clear all team data signals
        this.clubNameSignal.set(null);
        this.suggestedTeamNamesSignal.set([]);
        this.registeredTeamsSignal.set([]);
        this.ageGroupsSignal.set([]);
        this.clubTeamsSignal.set([]);
        this.recentlyAddedTeamNames.set([]);

        // Clear payment metadata
        this.paymentMethodsAllowedCode.set(1);
        this.bAddProcessingFees.set(false);
        this.bApplyProcessingFeesToTeamDeposit.set(false);

        // Reset UI state
        this.isLoading.set(false);
        this.errorMessage.set(null);
        this.showRegistrationModal.set(false);
        this.isRegistering.set(false);
        this.clubId.set(null);

        // Reset refund policy state
        this.refundPolicyHtml.set(null);
        this.refundPolicyAccepted.set(false);
        this.refundPolicyLocked.set(false);

        // Reload metadata for new rep/club context
        this.loadTeamsMetadata();
    }

    private loadTeamsMetadata(showLoading: boolean = true): void {
        const jobPath = this.jobContext.resolveFromRoute(this.route);

        if (!jobPath) {
            this.errorMessage.set(
                'Event not found. Please navigate from a valid event link.',
            );
            return;
        }

        if (showLoading) {
            this.isLoading.set(true);
        }
        this.errorMessage.set(null);

        this.teamService.getTeamsMetadata().subscribe({
            next: (response) => {
                // Derive clubName from response (authoritative source)
                this.clubNameSignal.set(response.clubName);
                this.clubId.set(response.clubId);
                this.suggestedTeamNamesSignal.set(response.suggestedTeamNames);
                this.registeredTeamsSignal.set(response.registeredTeams);
                this.ageGroupsSignal.set(response.ageGroups);
                this.clubTeamsSignal.set(response.clubTeams);
                this.refundPolicyHtml.set(response.playerRegRefundPolicy || null);

                // Store payment metadata
                this.paymentMethodsAllowedCode.set(response.paymentMethodsAllowedCode);
                this.bAddProcessingFees.set(response.bAddProcessingFees);
                this.bApplyProcessingFeesToTeamDeposit.set(
                    response.bApplyProcessingFeesToTeamDeposit ?? false,
                );

                // Check if refund policy already accepted (from first registered team's club rep registration)
                if (response.registeredTeams.length > 0) {
                    const firstTeam = response.registeredTeams[0];
                    if (firstTeam.bWaiverSigned3) {
                        this.refundPolicyAccepted.set(true);
                        this.refundPolicyLocked.set(true);
                    }
                }

                this.isLoading.set(false);
            },
            error: (err) => {
                console.error('Failed to load teams metadata:', err);
                this.errorMessage.set(
                    err.error?.message || 'Failed to load team data. Please try again.',
                );
                this.isLoading.set(false);
            },
        });
    }

    openRegistrationModal(): void {
        this.showRegistrationModal.set(true);
    }

    closeRegistrationModal(): void {
        this.showRegistrationModal.set(false);
        this.recentlyAddedTeamNames.set([]);
    }

    onTeamAddedAnother(data: RegistrationData): void {
        this.registerTeam(data, () => {
            if (data.teamName) {
                this.recentlyAddedTeamNames.update((arr) => [...arr, data.teamName!]);
            }
            this.loadTeamsMetadata(false);
        });
    }

    onTeamRegistered(data: RegistrationData): void {
        this.registerTeam(data, () => {
            this.showRegistrationModal.set(false);
            this.recentlyAddedTeamNames.set([]);
            this.loadTeamsMetadata(false);
            this.toast.show('Team registered successfully', 'success');
        });
    }

    unregisterTeam(team: RegisteredTeamDto): void {
        if (this.toNumber(team.paidTotal) > 0) {
            this.errorMessage.set(
                'Cannot unregister a team that has payments. Please contact support.',
            );
            return;
        }

        this.errorMessage.set(null);
        this.teamService.unregisterTeamFromEvent(team.teamId).subscribe({
            next: () => {
                this.toast.show('Team unregistered successfully', 'success');
                const removedName = team.teamName.trim().toLowerCase();
                this.recentlyAddedTeamNames.update((list) =>
                    list.filter((n) => n.trim().toLowerCase() !== removedName),
                );
                this.registeredTeamsSignal.update((list) =>
                    list.filter((t) => t.teamId !== team.teamId),
                );
            },
            error: (err) => {
                console.error('Failed to unregister team:', err);
                this.errorMessage.set(
                    err.error?.message || 'Failed to unregister team. Please try again.',
                );
            },
        });
    }

    isAgeGroupFull(ageGroupName: string): boolean {
        const ag = this.ageGroups().find((a) => a.ageGroupName === ageGroupName);
        return ag ? ag.registeredCount >= ag.maxTeams : false;
    }

    // Private helpers

    private registerTeam(data: RegistrationData, onSuccess: () => void): void {
        const jobPath = this.jobContext.jobPath();

        if (!jobPath) {
            this.errorMessage.set('Invalid event context');
            return;
        }

        if (this.isRegistering()) {
            return;
        }

        this.errorMessage.set(null);
        this.isRegistering.set(true);

        this.teamService
            .registerTeamForEvent({
                clubTeamId: data.clubTeamId,
                teamName: data.teamName,
                clubTeamGradYear: data.clubTeamGradYear,
                ageGroupId: data.ageGroupId,
                levelOfPlay: data.levelOfPlay,
            })
            .subscribe({
                next: () => {
                    this.isRegistering.set(false);
                    onSuccess();
                },
                error: (err) => {
                    console.error('Failed to register team:', err);
                    this.isRegistering.set(false);
                    this.errorMessage.set(
                        err.error?.message || 'Failed to register team. Please try again.',
                    );
                },
            });
    }

    private getDisplayedAgeGroups(): AgeGroupDto[] {
        return this.ageGroupsSignal()
            .filter((ag) => !ag.ageGroupName.toLowerCase().startsWith('dropped'))
            .sort((a, b) => {
                const aFull = a.registeredCount >= a.maxTeams;
                const bFull = b.registeredCount >= b.maxTeams;
                if (aFull && !bFull) return 1;
                if (!aFull && bFull) return -1;
                return a.ageGroupName.localeCompare(b.ageGroupName);
            });
    }

    private getFilteredAgeGroupsForModal(): AgeGroupDto[] {
        return this.ageGroupsSignal()
            .filter((ag) => {
                const name = ag.ageGroupName.toLowerCase();
                if (name.startsWith('dropped')) return false;
                if (name.startsWith('waitlist')) {
                    return (
                        this.toNumber(ag.maxTeams) - this.toNumber(ag.registeredCount) > 0
                    );
                }
                return true;
            })
            .sort((a, b) => this.sortAgeGroups(a, b));
    }

    private sortAgeGroups(a: AgeGroupDto, b: AgeGroupDto): number {
        const aName = a.ageGroupName.toLowerCase();
        const bName = b.ageGroupName.toLowerCase();
        const aFull =
            this.toNumber(a.registeredCount) >= this.toNumber(a.maxTeams) &&
            !aName.startsWith('waitlist');
        const bFull =
            this.toNumber(b.registeredCount) >= this.toNumber(b.maxTeams) &&
            !bName.startsWith('waitlist');
        const aWaitlist = aName.startsWith('waitlist');
        const bWaitlist = bName.startsWith('waitlist');

        if (aFull && !bFull) return 1;
        if (!aFull && bFull) return -1;
        if (aWaitlist && !bWaitlist) return 1;
        if (!aWaitlist && bWaitlist) return -1;
        return a.ageGroupName.localeCompare(b.ageGroupName);
    }

    private calculateFinancialSummary(): FinancialSummary {
        return {
            feesTotal: this.registeredTeamsSignal().reduce(
                (sum, t) => sum + this.toNumber(t.feeTotal),
                0,
            ),
            paidTotal: this.registeredTeamsSignal().reduce(
                (sum, t) => sum + this.toNumber(t.paidTotal),
                0,
            ),
            balanceDue: this.registeredTeamsSignal().reduce(
                (sum, t) => sum + this.toNumber(t.owedTotal),
                0,
            ),
            depositDueTotal: this.registeredTeamsSignal().reduce(
                (sum, t) => sum + this.toNumber(t.depositDue),
                0,
            ),
            additionalDueTotal: this.registeredTeamsSignal().reduce(
                (sum, t) => sum + this.toNumber(t.additionalDue),
                0,
            ),
        };
    }

    private toNumber(value: number | string | undefined | null): number {
        if (value === undefined || value === null) return 0;
        return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
    }

    /** Handler for cell rendering - adds row numbers efficiently */
    onQueryCellInfo(args: QueryCellInfoEventArgs): void {
        if (args.column?.field === 'rowNum' && args.data) {
            const index = (this.grid.currentViewData as any[]).findIndex(
                (item) => item.teamId === (args.data as any).teamId,
            );
            (args.cell as HTMLElement).innerText = (index + 1).toString();
        }
    }

    onDataBound(): void {
        // Auto-fit all columns to content on data load
        this.grid?.autoFitColumns();
    }

    /**
     * Trigger Excel export programmatically from the external icon button.
     */
    exportTeamsToExcel(): void {
        const excelExportProperties = {
            dataSource: this.registeredTeams(),
            fileName: 'RegisteredTeams.xlsx',
        };
        this.grid.excelExport(excelExportProperties);
    }

    proceedToPayment(): void {
        this.attemptedProceed.set(true);

        // Validate refund policy acceptance
        if (this.refundPolicyHtml() && !this.refundPolicyAccepted()) {
            this.toast.show('Please accept the refund policy to continue', 'warning');
            return;
        }

        // Populate payment service with teams and metadata
        this.paymentService.teams.set(this.registeredTeamsSignal());
        this.paymentService.paymentMethodsAllowedCode.set(
            this.paymentMethodsAllowedCode(),
        );
        this.paymentService.bAddProcessingFees.set(this.bAddProcessingFees());
        this.paymentService.bApplyProcessingFeesToTeamDeposit.set(
            this.bApplyProcessingFeesToTeamDeposit(),
        );

        // Set initial payment method based on allowed options
        if (this.paymentMethodsAllowedCode() === 3) {
            this.paymentService.selectedPaymentMethod.set('Check'); // Check only
        } else {
            this.paymentService.selectedPaymentMethod.set('CC'); // Default to CC
        }

        this.proceed.emit();
    }

    onRefundPolicyAcceptanceChange(accepted: boolean): void {
        this.refundPolicyAccepted.set(accepted);

        if (accepted && !this.refundPolicyLocked()) {
            // Call API to record acceptance
            this.teamService.acceptRefundPolicy().subscribe({
                next: () => {
                    this.refundPolicyLocked.set(true);
                    this.toast.show(
                        'Your acceptance of the Refund Policy has been recorded',
                        'success',
                    );
                },
                error: (err) => {
                    console.error('Failed to record refund policy acceptance:', err);
                    this.toast.show(
                        'Failed to record acceptance. Please try again.',
                        'danger',
                    );
                    // Rollback checkbox state
                    this.refundPolicyAccepted.set(false);
                },
            });
        }
    }
}
