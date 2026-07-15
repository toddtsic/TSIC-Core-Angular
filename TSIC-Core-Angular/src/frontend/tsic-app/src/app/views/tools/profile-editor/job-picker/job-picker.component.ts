import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import type { EditableJobDto } from '@core/api';

/**
 * Searchable job picker over the editor's editable-jobs list. One-way bound: the parent owns the
 * selected id and reacts to (jobSelected). Reused for the "A specific job" edit scope and for the
 * copy-forms source/target pickers. A job flagged customized is badged so the user sees drift.
 */
@Component({
    selector: 'app-job-picker',
    standalone: true,
    imports: [CommonModule, FormsModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
        <div class="job-picker">
            <input type="text" class="form-control form-control-sm mb-2" [ngModel]="search()"
                (ngModelChange)="search.set($event)" [placeholder]="searchPlaceholder()"
                [attr.aria-label]="searchPlaceholder()" />
            <select class="form-select" [ngModel]="selectedJobId()" (ngModelChange)="onPick($event)"
                [attr.aria-label]="placeholder()">
                <option [ngValue]="null">{{ placeholder() }}</option>
                @for (j of filtered(); track j.jobId) {
                <option [ngValue]="j.jobId">{{ label(j) }}</option>
                }
            </select>
            @if (search() && filtered().length === 0) {
            <small class="text-muted d-block mt-1">No jobs match “{{ search() }}”.</small>
            }
        </div>
    `
})
export class JobPickerComponent {
    readonly jobs = input<EditableJobDto[]>([]);
    readonly selectedJobId = input<string | null>(null);
    readonly placeholder = input<string>('Choose a job…');
    readonly searchPlaceholder = input<string>('Search jobs…');
    /** Exclude one job from the options (e.g. can't copy a job onto itself). */
    readonly excludeJobId = input<string | null>(null);

    readonly jobSelected = output<EditableJobDto | null>();

    readonly search = signal('');

    readonly filtered = computed(() => {
        const q = this.search().trim().toLowerCase();
        const ex = this.excludeJobId();
        return this.jobs()
            .filter(j => j.jobId !== ex)
            .filter(j => !q || `${j.jobName} ${j.year ?? ''} ${j.profileType ?? ''}`.toLowerCase().includes(q));
    });

    onPick(jobId: string | null): void {
        const job = jobId ? this.jobs().find(j => j.jobId === jobId) ?? null : null;
        this.jobSelected.emit(job);
    }

    label(j: EditableJobDto): string {
        const parts = [j.jobName];
        if (j.year) parts.push(`(${j.year})`);
        if (j.profileType) parts.push(`· ${j.profileType}`);
        if (j.isCustomized) parts.push('· customized');
        return parts.join(' ');
    }
}
