import { Component, inject, signal, ChangeDetectionStrategy, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConfirmDialogComponent } from '@shared-ui/components/confirm-dialog/confirm-dialog.component';
import { ToastService } from '@shared-ui/toast.service';
import { MobileScorersService } from './mobile-scorers.service';
import { ScorerDialogComponent, type ScorerDialogMode } from './scorer-dialog/scorer-dialog.component';
import type { MobileScorerDto, CreateMobileScorerRequest, UpdateMobileScorerRequest } from '@core/api';

@Component({
    selector: 'app-mobile-scorers',
    standalone: true,
    imports: [CommonModule, ConfirmDialogComponent, ScorerDialogComponent],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './mobile-scorers.component.html',
    styleUrl: './mobile-scorers.component.scss'
})
export class MobileScorersComponent {
    private readonly service = inject(MobileScorersService);
    private readonly toast = inject(ToastService);

    @ViewChild(ScorerDialogComponent) dialogRef?: ScorerDialogComponent;

    // ── Data signals ──────────────────────────────────
    readonly scorers = signal<MobileScorerDto[]>([]);

    // ── UI state ──────────────────────────────────────
    readonly isLoading = signal(false);

    // ── Dialog state ──────────────────────────────────
    readonly dialogOpen = signal(false);
    readonly dialogMode = signal<ScorerDialogMode>('add');
    readonly editingScorer = signal<MobileScorerDto | null>(null);

    // ── Delete confirm ────────────────────────────────
    readonly showDeleteConfirm = signal(false);
    readonly deletingScorer = signal<MobileScorerDto | null>(null);

    constructor() {
        this.loadScorers();
    }

    // ── Load ──────────────────────────────────────────

    loadScorers(): void {
        this.isLoading.set(true);
        this.service.getScorers().subscribe({
            next: scorers => {
                this.scorers.set(scorers);
                this.isLoading.set(false);
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to load scorers', 'danger');
                this.isLoading.set(false);
            }
        });
    }

    // ── Add ───────────────────────────────────────────

    openAdd(): void {
        this.dialogMode.set('add');
        this.editingScorer.set(null);
        this.dialogOpen.set(true);
    }

    // ── Edit ──────────────────────────────────────────

    openEdit(scorer: MobileScorerDto): void {
        this.dialogMode.set('edit');
        this.editingScorer.set(scorer);
        this.dialogOpen.set(true);
    }

    // ── Dialog save handler ───────────────────────────

    onDialogSaved(event: { mode: ScorerDialogMode; data: Record<string, unknown> }): void {
        if (event.mode === 'add') {
            const request: CreateMobileScorerRequest = {
                username: event.data['username'] as string,
                firstName: event.data['firstName'] as string,
                lastName: event.data['lastName'] as string,
                email: event.data['email'] as string | undefined,
                cellphone: event.data['cellphone'] as string | undefined
            };

            this.service.createScorer(request).subscribe({
                next: () => {
                    this.toast.show('Scorer created', 'success');
                    this.dialogOpen.set(false);
                    this.loadScorers();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to create scorer', 'danger');
                    this.dialogRef?.resetSaving();
                }
            });
        } else {
            const scorer = this.editingScorer();
            if (!scorer) return;

            const request: UpdateMobileScorerRequest = {
                email: event.data['email'] as string | undefined,
                cellphone: event.data['cellphone'] as string | undefined,
                bActive: event.data['bActive'] as boolean
            };

            this.service.updateScorer(scorer.registrationId, request).subscribe({
                next: () => {
                    this.toast.show('Scorer updated', 'success');
                    this.dialogOpen.set(false);
                    this.loadScorers();
                },
                error: err => {
                    this.toast.show(err?.error?.message || 'Failed to update scorer', 'danger');
                    this.dialogRef?.resetSaving();
                }
            });
        }
    }

    onDialogClose(): void {
        this.dialogOpen.set(false);
    }

    // ── Delete ────────────────────────────────────────

    confirmDelete(scorer: MobileScorerDto): void {
        this.deletingScorer.set(scorer);
        this.showDeleteConfirm.set(true);
    }

    onDeleteConfirmed(): void {
        const scorer = this.deletingScorer();
        if (!scorer) return;

        this.service.deleteScorer(scorer.registrationId).subscribe({
            next: () => {
                this.toast.show('Scorer removed', 'success');
                this.showDeleteConfirm.set(false);
                this.deletingScorer.set(null);
                this.loadScorers();
            },
            error: err => {
                this.toast.show(err?.error?.message || 'Failed to delete scorer', 'danger');
                this.showDeleteConfirm.set(false);
                this.deletingScorer.set(null);
            }
        });
    }
}
