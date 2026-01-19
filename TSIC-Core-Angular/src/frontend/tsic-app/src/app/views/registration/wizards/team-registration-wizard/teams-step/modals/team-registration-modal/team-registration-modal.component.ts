import { Component, Input, Output, EventEmitter, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { SuggestedTeamNameDto, AgeGroupDto } from '@core/api';
import { NgbPopover } from '@ng-bootstrap/ng-bootstrap';

interface RegistrationData {
    teamName: string;
    ageGroupId: string;
    levelOfPlay: string;
}

/**
 * Team Registration Modal Component
 *
 * Dumb presentation component for registering a team.
 * Receives data via inputs, emits events for parent handling.
 * Filtering/sorting handled locally via computed properties.
 */
@Component({
    selector: 'app-team-registration-modal',
    standalone: true,
    imports: [CommonModule, FormsModule, NgbPopover],
    templateUrl: './team-registration-modal.component.html',
    styleUrls: ['./team-registration-modal.component.scss']
})
export class TeamRegistrationModalComponent {
    @Input() isOpen = false;
    @Input() suggestedTeamNames: SuggestedTeamNameDto[] = [];
    @Input() ageGroups: AgeGroupDto[] = [];
    @Input() availableLevelsOfPlay: { value: string; label: string }[] = [];
    @Input() isRegistering = false;

    @Output() closed = new EventEmitter<void>();
    @Output() register = new EventEmitter<RegistrationData>();
    @Output() addAnother = new EventEmitter<RegistrationData>();
    // clubName input removed: warning logic handles null gracefully

    // Form state (exposed for template binding)
    readonly teamNameInput = signal('');
    readonly selectedAgeGroupId = signal('');
    readonly levelOfPlayInput = signal('');
    readonly successMessage = signal('');
    readonly specialCharBlocked = signal(false);
    private readonly usedNames = signal<Set<string>>(new Set());

    // Derived state for template
    readonly teamNameWarning = computed(() => {
        const teamName = this.teamNameInput().trim();

        if (!teamName) {
            return null;
        }

        // Check length first (max 30 characters recommended)
        if (teamName.length > 30) {
            return {
                message: `Your team name may get cut off in schedules, consider shortening.`,
                suggestedName: teamName.substring(0, 30)
            };
        }

        // Note: Club name detection disabled - requires club context
        return null;
    });

    readonly filteredSuggestions = computed(() => {
        const input = this.teamNameInput().toLowerCase().trim();
        const exclude = this.usedNames();
        const list = this.suggestedTeamNames.filter(s => !exclude.has(this.normalizeName(s.teamName)));

        // Don't filter if input exactly matches a suggestion (user selected from list)
        if (input && list.some(s => s.teamName.toLowerCase() === input)) {
            return list;
        }

        if (!input) return list;
        return list.filter(s => s.teamName.toLowerCase().includes(input));
    });

    readonly filteredAgeGroups = computed(() => this.getFilteredAgeGroups());

    selectFromList(event: Event): void {
        const select = event.target as HTMLSelectElement;
        if (select.value) {
            this.teamNameInput.set(select.value);
            select.selectedIndex = 0;
        }
    }

    onTeamNameInput(event: Event): void {
        const input = event.target as HTMLInputElement;
        const sanitized = input.value.replace(/[^a-zA-Z0-9\s]/g, '');
        if (input.value !== sanitized) {
            input.value = sanitized;
            this.teamNameInput.set(sanitized);
            // Show feedback that special chars were blocked
            this.specialCharBlocked.set(true);
            setTimeout(() => this.specialCharBlocked.set(false), 3000);
        }
    }

    onRegisterAddAnother(): void {
        const data = this.getFormData();
        if (!data) return;
        this.addAnother.emit(data);
        this.showSuccessMessage(`"${data.teamName}" registered!`);
        // Exclude the just-used name immediately for the next iteration
        const norm = this.normalizeName(data.teamName);
        this.usedNames.update(curr => {
            const next = new Set(curr);
            next.add(norm);
            return next;
        });
        this.clearInputs();
    }

    onRegister(): void {
        const data = this.getFormData();
        if (!data) return;
        this.register.emit(data);
    }

    onClose(): void {
        this.clearForm();
        // Reset session-specific exclusions
        this.usedNames.set(new Set());
        this.closed.emit();
    }

    private getFormData(): RegistrationData | null {
        const teamName = this.teamNameInput().trim();
        const ageGroupId = this.selectedAgeGroupId();
        const levelOfPlay = this.levelOfPlayInput().trim();

        if (!teamName || !ageGroupId || !levelOfPlay) {
            return null;
        }

        return { teamName, ageGroupId, levelOfPlay };
    }

    private getFilteredAgeGroups(): AgeGroupDto[] {
        return this.ageGroups
            .filter(ag => {
                const name = ag.ageGroupName.toLowerCase();
                if (name.startsWith('dropped')) return false;
                if (name.startsWith('waitlist')) {
                    return (this.toNumber(ag.maxTeams) - this.toNumber(ag.registeredCount)) > 0;
                }
                return true;
            })
            .sort((a, b) => this.sortAgeGroups(a, b));
    }

    private sortAgeGroups(a: AgeGroupDto, b: AgeGroupDto): number {
        const aName = a.ageGroupName.toLowerCase();
        const bName = b.ageGroupName.toLowerCase();
        const aFull = this.toNumber(a.registeredCount) >= this.toNumber(a.maxTeams) && !aName.startsWith('waitlist');
        const bFull = this.toNumber(b.registeredCount) >= this.toNumber(b.maxTeams) && !bName.startsWith('waitlist');
        const aWaitlist = aName.startsWith('waitlist');
        const bWaitlist = bName.startsWith('waitlist');

        if (aFull && !bFull) return 1;
        if (!aFull && bFull) return -1;
        if (aWaitlist && !bWaitlist) return 1;
        if (!aWaitlist && bWaitlist) return -1;
        return a.ageGroupName.localeCompare(b.ageGroupName);
    }

    private toNumber(value: number | string | undefined | null): number {
        if (value === undefined || value === null) return 0;
        return typeof value === 'string' ? Number.parseFloat(value) || 0 : value;
    }

    private normalizeName(value: string): string {
        return value.trim().toLowerCase();
    }

    private clearInputs(): void {
        this.teamNameInput.set('');
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
}
