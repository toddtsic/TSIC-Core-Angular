import { Component, Input, Output, EventEmitter, computed, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { SuggestedTeamNameDto, AgeGroupDto } from '@core/api';

@Component({
    selector: 'app-team-registration-modal',
    standalone: true,
    imports: [CommonModule, FormsModule],
    templateUrl: './team-registration-modal.component.html',
    styleUrls: ['./team-registration-modal.component.scss']
})
export class TeamRegistrationModalComponent {
    @Input() isOpen = false;
    @Input() suggestedTeamNames: SuggestedTeamNameDto[] = [];
    @Input() ageGroups: AgeGroupDto[] = [];
    @Input() availableLevelsOfPlay: { value: string; label: string }[] = [];
    @Input() isRegistering = false;

    @Output() close = new EventEmitter<void>();
    @Output() register = new EventEmitter<{ teamName: string; ageGroupId: string; levelOfPlay: string }>();

    // Form state
    teamNameInput = signal<string>('');
    selectedAgeGroupId = signal<string>('');
    levelOfPlayInput = signal<string>('');

    // Mobile popover state
    showInfoPopover = signal<boolean>(false);

    // Filtered suggestions for autocomplete
    filteredSuggestions = computed(() => {
        const input = this.teamNameInput().toLowerCase().trim();
        if (!input) return this.suggestedTeamNames;
        return this.suggestedTeamNames.filter(s => s.teamName.toLowerCase().includes(input));
    });

    // Filtered age groups for modal (special waitlist handling)
    filteredAgeGroups = computed(() => {
        const toNumber = (value: number | string | undefined | null): number => {
            if (value === undefined || value === null) return 0;
            return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
        };

        return this.ageGroups
            .filter(ag => {
                const name = ag.ageGroupName.toLowerCase();
                // Exclude "Dropped" age groups
                if (name.startsWith('dropped')) return false;
                // Only include Waitlist if it has spots available
                if (name.startsWith('waitlist')) {
                    return (toNumber(ag.maxTeams) - toNumber(ag.registeredCount)) > 0;
                }
                return true;
            })
            .sort((a, b) => {
                const aName = a.ageGroupName.toLowerCase();
                const bName = b.ageGroupName.toLowerCase();
                const aFull = toNumber(a.registeredCount) >= toNumber(a.maxTeams) && !aName.startsWith('waitlist');
                const bFull = toNumber(b.registeredCount) >= toNumber(b.maxTeams) && !bName.startsWith('waitlist');
                const aWaitlist = aName.startsWith('waitlist');
                const bWaitlist = bName.startsWith('waitlist');

                // Available first, then full (red), then waitlist
                if (aFull && !bFull) return 1;
                if (!aFull && bFull) return -1;
                if (aWaitlist && !bWaitlist) return 1;
                if (!aWaitlist && bWaitlist) return -1;

                return a.ageGroupName.localeCompare(b.ageGroupName);
            });
    });

    constructor() {
        // Clear form when modal closes
        effect(() => {
            if (!this.isOpen) {
                this.clearForm();
            }
        });
    }

    /**
     * Select a suggested team name
     */
    selectSuggestion(suggestion: SuggestedTeamNameDto): void {
        this.teamNameInput.set(suggestion.teamName);
    }

    /**
     * Select team name from dropdown list
     */
    selectFromList(event: Event): void {
        const select = event.target as HTMLSelectElement;
        if (select.value) {
            this.teamNameInput.set(select.value);
            select.selectedIndex = 0; // Reset to placeholder
        }
    }

    /**
     * Submit registration form
     */
    onRegister(): void {
        const teamName = this.teamNameInput().trim();
        const ageGroupId = this.selectedAgeGroupId();
        const levelOfPlay = this.levelOfPlayInput().trim();

        if (teamName && ageGroupId && levelOfPlay) {
            this.register.emit({ teamName, ageGroupId, levelOfPlay });
        }
    }

    /**
     * Close modal
     */
    onClose(): void {
        this.close.emit();
    }

    /**
     * Clear form inputs
     */
    private clearForm(): void {
        this.teamNameInput.set('');
        this.selectedAgeGroupId.set('');
        this.levelOfPlayInput.set('');
    }
}
