import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProfileMigrationService } from '@infrastructure/services/profile-migration.service';
import { ToastService } from '@shared-ui/toast.service';
import type { CopyFormSourceDto, CopyJobFormsResult } from '@core/api';

/**
 * Job-scoped "seed this job's registration forms from another job" card.
 *
 * Copies another job's materialized player and/or coach (adult) form JSON directly onto the CURRENT
 * job (resolved server-side from the JWT regId). This is orthogonal to the type-scoped profile editors
 * below it — it writes the job's live PlayerProfileMetadataJson / AdultProfileMetadataJson, which is what
 * the registration runtime renders. Verify a copy by previewing the actual registration form.
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
    readonly selectedJobId = signal<string | null>(null);
    readonly includePlayer = signal(false);
    readonly includeCoach = signal(false);

    readonly isLoadingSources = signal(false);
    readonly isCopying = signal(false);
    readonly errorMessage = signal<string | null>(null);
    readonly lastResult = signal<CopyJobFormsResult | null>(null);

    readonly selectedSource = computed(() =>
        this.sources().find(s => s.jobId === this.selectedJobId()) ?? null);

    readonly canCopy = computed(() =>
        !!this.selectedJobId()
        && (this.includePlayer() || this.includeCoach())
        && !this.isCopying());

    ngOnInit(): void {
        this.loadSources();
    }

    private loadSources(): void {
        this.isLoadingSources.set(true);
        this.errorMessage.set(null);
        this.service.getCopyFormSources(
            sources => {
                this.sources.set(sources);
                this.isLoadingSources.set(false);
            },
            err => {
                this.errorMessage.set(err?.error?.error || err?.error?.message || 'Failed to load source jobs.');
                this.isLoadingSources.set(false);
            }
        );
    }

    onSourceChange(jobId: string | null): void {
        this.selectedJobId.set(jobId);
        this.lastResult.set(null);
        this.errorMessage.set(null);
        // Default the checkboxes to whatever the chosen source actually offers.
        const src = this.selectedSource();
        this.includePlayer.set(!!src?.hasPlayerForm);
        this.includeCoach.set(!!src?.hasCoachForm);
    }

    copy(): void {
        const src = this.selectedSource();
        if (!src || (!this.includePlayer() && !this.includeCoach())) return;

        this.isCopying.set(true);
        this.errorMessage.set(null);
        this.lastResult.set(null);

        this.service.copyFormsToCurrentJob(
            { sourceJobId: src.jobId, includePlayer: this.includePlayer(), includeCoach: this.includeCoach() },
            result => {
                this.isCopying.set(false);
                if (result.success) {
                    this.lastResult.set(result);
                    const parts: string[] = [];
                    if (result.playerCopied) parts.push('player');
                    if (result.coachCopied) parts.push('coach');
                    const noun = parts.length === 1 ? 'form' : 'forms';
                    this.toast.show(`Copied ${parts.join(' + ')} ${noun} from "${result.sourceJobName}" onto this job.`, 'success');
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
}
