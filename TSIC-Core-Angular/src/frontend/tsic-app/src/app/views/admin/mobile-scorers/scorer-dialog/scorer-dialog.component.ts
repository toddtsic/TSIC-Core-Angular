import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { MobileScorerDto } from '@core/api';

export type ScorerDialogMode = 'add' | 'edit';

@Component({
    selector: 'scorer-dialog',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent],
    templateUrl: './scorer-dialog.component.html',
    styleUrl: './scorer-dialog.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class ScorerDialogComponent implements OnInit {
    @Input() mode: ScorerDialogMode = 'add';
    @Input() scorer: MobileScorerDto | null = null;
    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<{
        mode: ScorerDialogMode;
        data: {
            username?: string;
            firstName?: string;
            lastName?: string;
            email?: string;
            cellphone?: string;
            bActive?: boolean;
        };
    }>();

    // Form fields
    username = signal('');
    firstName = signal('');
    lastName = signal('');
    email = signal('');
    cellphone = signal('');
    bActive = signal(true);

    // UI state
    isSaving = signal(false);

    ngOnInit(): void {
        if (this.mode === 'edit' && this.scorer) {
            this.username.set(this.scorer.username ?? '');
            this.firstName.set(this.scorer.firstName ?? '');
            this.lastName.set(this.scorer.lastName ?? '');
            this.email.set(this.scorer.email ?? '');
            this.cellphone.set(this.scorer.cellphone ?? '');
            this.bActive.set(this.scorer.bActive);
        }
    }

    onInput(field: 'username' | 'firstName' | 'lastName' | 'email' | 'cellphone', event: Event): void {
        const value = (event.target as HTMLInputElement).value;
        this[field].set(value);
    }

    onActiveChange(event: Event): void {
        this.bActive.set((event.target as HTMLInputElement).checked);
    }

    isValid(): boolean {
        if (this.mode === 'add') {
            return this.username().trim().length >= 6
                && this.firstName().trim().length > 0
                && this.lastName().trim().length > 0;
        }
        // Edit mode — always valid (email/cellphone/active are optional or boolean)
        return true;
    }

    onSubmit(): void {
        if (!this.isValid() || this.isSaving()) return;
        this.isSaving.set(true);

        if (this.mode === 'add') {
            this.saved.emit({
                mode: 'add',
                data: {
                    username: this.username().trim(),
                    firstName: this.firstName().trim(),
                    lastName: this.lastName().trim(),
                    email: this.email().trim() || undefined,
                    cellphone: this.cellphone().trim() || undefined
                }
            });
        } else {
            this.saved.emit({
                mode: 'edit',
                data: {
                    email: this.email().trim() || undefined,
                    cellphone: this.cellphone().trim() || undefined,
                    bActive: this.bActive()
                }
            });
        }
    }

    /** Called by parent when save completes (success or error) */
    resetSaving(): void {
        this.isSaving.set(false);
    }
}
