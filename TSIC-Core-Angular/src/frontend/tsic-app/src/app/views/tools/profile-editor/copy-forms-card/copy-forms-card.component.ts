import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { CopyFormSourceDto, CopyJobFormsResult, EditableJobDto } from '@core/api';

/**
 * Copy a job's registration form(s) FROM one job INTO another. The target defaults to the caller's
 * current job (sent as a null targetJobId) but can be pointed at any job — useful for standing up a
 * brand-new job from an existing one. Beyond the two form blobs, the profile-type pointer and per-job
 * option sets can optionally be carried too. Every write is announced in a read-back sentence and
 * confirmed against source==target / target-already-has-a-form before it runs.
 */
@Component({
    selector: 'app-copy-forms-card',
    standalone: true,
    imports: [CommonModule],
    templateUrl: './copy-forms-card.component.html',
    styleUrl: './copy-forms-card.component.scss',
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class CopyFormsCardComponent implements OnInit {
    private readonly service = inject(ProfileMigrationService);
    private readonly toast = inject(ToastService);

    readonly sources = signal<CopyFormSourceDto[]>([]);
    readonly targets = signal<EditableJobDto[]>([]);

    readonly selectedSourceId = signal<string | null>(null);
    /** null ⇒ the current job (server resolves from JWT). */
    readonly selectedTargetId = signal<string | null>(null);

    readonly includePlayer = signal(false);
    readonly includeCoach = signal(false);
    readonly includePointer = signal(false);
    readonly includeOptions = signal(false);

    readonly isLoadingSources = signal(false);
    readonly isCopying = signal(false);
    readonly errorMessage = signal<string | null>(null);
    readonly lastResult = signal<CopyJobFormsResult | null>(null);

    readonly selectedSource = computed(() =>
        this.sources().find(s => s.jobId === this.selectedSourceId()) ?? null);

    readonly selectedTarget = computed(() =>
        this.targets().find(t => t.jobId === this.selectedTargetId()) ?? null);

    readonly sourceName = computed(() => this.selectedSource()?.jobName ?? '');
    readonly targetName = computed(() =>
        this.selectedTargetId() ? (this.selectedTarget()?.jobName ?? 'the chosen job') : 'this job (current)');

    /** Same job on both ends — never allowed. */
    readonly sourceEqualsTarget = computed(() =>
        !!this.selectedSourceId() && this.selectedSourceId() === this.selectedTargetId());

    /** Target already carries a player form → copying replaces it, doesn't merge. */
    readonly targetHasForm = computed(() => !!this.selectedTarget()?.hasPlayerForm);

    readonly anyIncludes = computed(() =>
        this.includePlayer() || this.includeCoach() || this.includeOptions());

    readonly readBack = computed(() => {
        const parts: string[] = [];
        if (this.includePlayer()) parts.push(this.includePointer() ? 'player form + profile type' : 'player form');
        if (this.includeCoach()) parts.push('coach form');
        if (this.includeOptions()) parts.push('dropdown options');
        const what = parts.length ? parts.join(', ') : 'nothing selected';
        return `Copy the ${what} FROM “${this.sourceName() || '…'}” INTO ${this.targetName()}.`;
    });

    readonly canCopy = computed(() =>
        !!this.selectedSourceId()
        && this.anyIncludes()
        && !this.sourceEqualsTarget()
        && !this.isCopying());

    ngOnInit(): void {
        this.loadSources();
        this.service.listEditableJobs(
            jobs => this.targets.set(jobs),
            () => { /* silent; target picker falls back to current-job only */ }
        );
    }

    private loadSources(): void {
        this.isLoadingSources.set(true);
        this.errorMessage.set(null);
        this.service.getCopyFormSources(
            sources => { this.sources.set(sources); this.isLoadingSources.set(false); },
            err => {
                this.errorMessage.set(err?.error?.error || err?.error?.message || 'Failed to load source jobs.');
                this.isLoadingSources.set(false);
            }
        );
    }

    onSourceChange(jobId: string | null): void {
        this.selectedSourceId.set(jobId);
        this.lastResult.set(null);
        this.errorMessage.set(null);
        // Default the checkboxes to whatever the chosen source actually offers.
        const src = this.selectedSource();
        this.includePlayer.set(!!src?.hasPlayerForm);
        this.includeCoach.set(!!src?.hasCoachForm);
        this.includePointer.set(false);
        this.includeOptions.set(false);
    }

    onTargetChange(jobId: string | null): void {
        this.selectedTargetId.set(jobId);
        this.lastResult.set(null);
        this.errorMessage.set(null);
    }

    // Pointer only makes sense alongside the player form; keep the two in lockstep.
    onIncludePlayerChange(checked: boolean): void {
        this.includePlayer.set(checked);
        if (!checked) this.includePointer.set(false);
    }

    copy(): void {
        const src = this.selectedSource();
        if (!src || !this.anyIncludes() || this.sourceEqualsTarget()) return;

        this.isCopying.set(true);
        this.errorMessage.set(null);
        this.lastResult.set(null);

        this.service.copyForms(
            {
                sourceJobId: src.jobId,
                targetJobId: this.selectedTargetId(),
                includePlayer: this.includePlayer(),
                includeCoach: this.includeCoach(),
                includePointer: this.includePointer(),
                includeOptions: this.includeOptions()
            },
            result => {
                this.isCopying.set(false);
                if (result.success) {
                    this.lastResult.set(result);
                    const into = result.targetJobName || this.targetName();
                    this.toast.show(`Copied from “${result.sourceJobName}” into “${into}”.`, 'success');
                } else {
                    this.errorMessage.set(result.errorMessage || 'Copy failed.');
                }
            },
            err => {
                this.isCopying.set(false);
                this.errorMessage.set(err?.error?.error || err?.error?.message || 'Copy failed.');
            }
        );
    }

    /** Option label: "Job Name (2025) — Player, Coach". */
    sourceLabel(s: CopyFormSourceDto): string {
        const forms: string[] = [];
        if (s.hasPlayerForm) forms.push('Player');
        if (s.hasCoachForm) forms.push('Coach');
        const year = s.year ? ` (${s.year})` : '';
        const suffix = forms.length ? ` — ${forms.join(', ')}` : '';
        return `${s.jobName}${year}${suffix}`;
    }

    targetLabel(t: EditableJobDto): string {
        const year = t.year ? ` (${t.year})` : '';
        const form = t.hasPlayerForm ? ' — has form' : ' — no form yet';
        return `${t.jobName}${year}${form}`;
    }
}
