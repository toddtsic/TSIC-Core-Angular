import { ChangeDetectionStrategy, Component, Input, Output, EventEmitter, computed, signal, effect } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { WizardModalComponent } from '../../../../shared/wizard-modal/wizard-modal.component';
import type { SuggestedTeamNameDto, AgeGroupDto, ClubTeamDto } from '@core/api';
import { filterAndSortAgeGroups } from '../../../services/age-group-utils';
export interface RegistrationData {
    clubTeamId?: number;
    teamName?: string;
    clubTeamGradYear?: string;
    ageGroupId: string;
    levelOfPlay: string;
}

@Component({
    selector: 'app-team-registration-modal',
    standalone: true,
    imports: [CurrencyPipe, FormsModule, WizardModalComponent],
    templateUrl: './team-registration-modal.component.html',
    styleUrls: ['./team-registration-modal.component.scss'],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamRegistrationModalComponent {
    @Input() isOpen = false;
    @Input() clubTeams: ClubTeamDto[] = [];
    @Input() suggestedTeamNames: SuggestedTeamNameDto[] = [];
    @Input() ageGroups: AgeGroupDto[] = [];
    @Input() availableLevelsOfPlay: { value: string; label: string }[] = [];
    @Input() isRegistering = false;

    @Output() closed = new EventEmitter<void>();
    @Output() register = new EventEmitter<RegistrationData>();
    @Output() addAnother = new EventEmitter<RegistrationData>();

    // Mode: 'select' existing ClubTeam or 'create' new one
    readonly mode = signal<'select' | 'create'>('select');

    // Select existing mode
    readonly selectedClubTeamId = signal<string>('');

    // Create new mode
    readonly teamNameInput = signal('');
    readonly gradYear = signal<string>('');
    readonly gradYearOptions = signal<(string | number)[]>(this.buildGradYears());

    // Shared fields
    readonly selectedAgeGroupId = signal('');
    readonly levelOfPlayInput = signal('');

    // UI state
    readonly successMessage = signal('');
    readonly specialCharBlocked = signal(false);
    private readonly usedClubTeamIds = signal<Set<number>>(new Set());
    private readonly usedNames = signal<Set<string>>(new Set());

    // Auto-set mode based on available ClubTeams
    constructor() {
        effect(() => {
            if (this.isOpen) {
                const hasClubTeams = this.clubTeams.length > 0;
                this.mode.set(hasClubTeams ? 'select' : 'create');
            }
        });
    }

    // When a ClubTeam is selected, pre-fill LOP
    readonly selectedClubTeam = computed(() => {
        const id = Number(this.selectedClubTeamId());
        if (!id) return null;
        return this.clubTeams.find(ct => ct.clubTeamId === id) ?? null;
    });

    readonly teamNameWarning = computed(() => {
        if (this.mode() !== 'create') return null;
        const teamName = this.teamNameInput().trim();
        if (!teamName) return null;
        if (teamName.length > 30) {
            return { message: 'Your team name may get cut off in schedules, consider shortening.' };
        }
        return null;
    });

    readonly filteredSuggestions = computed(() => {
        const input = this.teamNameInput().toLowerCase().trim();
        const exclude = this.usedNames();
        const list = this.suggestedTeamNames.filter(s => !exclude.has(s.teamName.trim().toLowerCase()));
        if (input && list.some(s => s.teamName.toLowerCase() === input)) return list;
        if (!input) return list;
        return list.filter(s => s.teamName.toLowerCase().includes(input));
    });

    readonly filteredAgeGroups = computed(() => this.getFilteredAgeGroups());

    readonly availableClubTeams = computed(() => {
        const excluded = this.usedClubTeamIds();
        return this.clubTeams.filter(ct => !excluded.has(ct.clubTeamId));
    });

    readonly hasAvailableClubTeams = computed(() => this.availableClubTeams().length > 0);

    readonly isFormValid = computed(() => {
        if (!this.selectedAgeGroupId() || !this.levelOfPlayInput()) return false;
        if (this.mode() === 'select') return !!this.selectedClubTeamId();
        return !!this.teamNameInput().trim() && !!this.gradYear();
    });

    onClubTeamSelected(): void {
        const ct = this.selectedClubTeam();
        if (ct) {
            // Pre-fill level of play from ClubTeam
            const lopOptions = this.availableLevelsOfPlay;
            const match = lopOptions.find(opt => opt.value.startsWith(ct.clubTeamLevelOfPlay));
            this.levelOfPlayInput.set(match?.value ?? ct.clubTeamLevelOfPlay);
        }
    }

    selectFromList(event: Event): void {
        const select = event.target as HTMLSelectElement;
        if (select.value) {
            this.teamNameInput.set(select.value);
            select.selectedIndex = 0;
        }
    }

    onTeamNameInput(event: Event): void {
        const input = event.target as HTMLInputElement;
        const sanitized = input.value.replaceAll(/[^a-zA-Z0-9\s]/g, '');
        if (input.value !== sanitized) {
            input.value = sanitized;
            this.teamNameInput.set(sanitized);
            this.specialCharBlocked.set(true);
            setTimeout(() => this.specialCharBlocked.set(false), 3000);
        }
    }

    onRegisterAddAnother(): void {
        const data = this.getFormData();
        if (!data) return;
        this.addAnother.emit(data);

        const displayName = data.teamName ?? this.selectedClubTeam()?.clubTeamName ?? 'Team';
        this.showSuccessMessage(`"${displayName}" registered!`);

        if (data.clubTeamId) {
            this.usedClubTeamIds.update(curr => { const next = new Set(curr); next.add(data.clubTeamId!); return next; });
        }
        if (data.teamName) {
            this.usedNames.update(curr => { const next = new Set(curr); next.add(data.teamName!.trim().toLowerCase()); return next; });
        }
        this.clearInputs();
    }

    onRegister(): void {
        const data = this.getFormData();
        if (!data) return;
        this.register.emit(data);
    }

    onClose(): void {
        this.clearForm();
        this.usedClubTeamIds.set(new Set());
        this.usedNames.set(new Set());
        this.closed.emit();
    }

    setMode(mode: 'select' | 'create'): void {
        this.mode.set(mode);
        this.clearInputs();
    }

    private getFormData(): RegistrationData | null {
        const ageGroupId = this.selectedAgeGroupId();
        const levelOfPlay = this.levelOfPlayInput().trim();
        if (!ageGroupId || !levelOfPlay) return null;

        if (this.mode() === 'select') {
            const clubTeamId = Number(this.selectedClubTeamId());
            if (!clubTeamId) return null;
            return { clubTeamId, ageGroupId, levelOfPlay };
        } else {
            const teamName = this.teamNameInput().trim();
            const clubTeamGradYear = this.gradYear();
            if (!teamName || !clubTeamGradYear) return null;
            return { teamName, clubTeamGradYear, ageGroupId, levelOfPlay };
        }
    }

    private getFilteredAgeGroups(): AgeGroupDto[] {
        return filterAndSortAgeGroups(this.ageGroups);
    }

    private clearInputs(): void {
        this.selectedClubTeamId.set('');
        this.teamNameInput.set('');
        this.gradYear.set('');
        this.selectedAgeGroupId.set('');
        this.levelOfPlayInput.set('');
    }

    private clearForm(): void {
        this.clearInputs();
        this.successMessage.set('');
    }

    private showSuccessMessage(message: string): void {
        this.successMessage.set(message);
    }

    private buildGradYears(): (string | number)[] {
        const currentYear = new Date().getFullYear();
        const years: number[] = [];
        for (let i = 0; i < 20; i++) {
            years.push(currentYear + i);
        }
        return years;
    }
}
