import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RichTextEditorAllModule } from '@syncfusion/ej2-angular-richtexteditor';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { BulletinAdminService } from '../services/bulletin-admin.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import { JOB_CONFIG_RTE_TOOLS } from '../../../configure/job/shared/rte-config';
import type { BulletinAdminDto, CreateBulletinRequest, UpdateBulletinRequest } from '@core/api';

export type ModalMode = 'add' | 'edit';

@Component({
    selector: 'bulletin-form-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent, FormsModule, RichTextEditorAllModule],
    template: `
        <tsic-dialog [open]="true" size="lg" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ mode === 'add' ? 'Add Bulletin' : 'Edit Bulletin' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <!-- Title -->
                    <div class="mb-3">
                        <label for="bulletinTitle" class="form-label fw-semibold">Title</label>
                        <input
                            id="bulletinTitle"
                            type="text"
                            class="form-control"
                            placeholder="Bulletin title"
                            [value]="title()"
                            (input)="title.set($any($event.target).value)"
                            maxlength="200" />
                    </div>

                    <!-- Text (RTE) -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Content</label>
                        <ejs-richtexteditor
                            [value]="text()"
                            [toolbarSettings]="rteTools"
                            [height]="rteHeight"
                            [enableHtmlSanitizer]="false"
                            (change)="onRteChange($event)">
                        </ejs-richtexteditor>
                    </div>

                    <!-- Token Hint -->
                    <div class="alert alert-info d-flex align-items-start py-2 mb-3" role="note">
                        <i class="bi bi-info-circle me-2 mt-1"></i>
                        <small>
                            <strong>Available tokens:</strong>
                            <code>!JOBNAME</code> (replaced with job name),
                            <code>!USLAXVALIDTHROUGHDATE</code> (replaced with US Lacrosse valid-through date)
                        </small>
                    </div>

                    <!-- Date Range -->
                    <div class="row">
                        <div class="col-md-6 mb-3">
                            <label for="startDate" class="form-label fw-semibold">Start Date</label>
                            <input
                                id="startDate"
                                type="date"
                                class="form-control"
                                [value]="startDate()"
                                (input)="startDate.set($any($event.target).value)" />
                        </div>
                        <div class="col-md-6 mb-3">
                            <label for="endDate" class="form-label fw-semibold">End Date</label>
                            <input
                                id="endDate"
                                type="date"
                                class="form-control"
                                [value]="endDate()"
                                (input)="endDate.set($any($event.target).value)"
                                [class.is-invalid]="endDate().length > 0 && startDate().length > 0 && endDate() < startDate()" />
                            @if (endDate().length > 0 && startDate().length > 0 && endDate() < startDate()) {
                                <div class="invalid-feedback">End date must be on or after start date.</div>
                            }
                        </div>
                    </div>

                    <!-- Active Toggle -->
                    <div class="mb-3">
                        <div class="form-check form-switch">
                            <input id="activeToggle" type="checkbox" class="form-check-input" role="switch"
                                [checked]="active()"
                                (change)="active.set($any($event.target).checked)" />
                            <label class="form-check-label fw-semibold" for="activeToggle">
                                {{ active() ? 'Active' : 'Inactive' }}
                            </label>
                        </div>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
                        @if (isSaving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        {{ mode === 'add' ? 'Add Bulletin' : 'Save Changes' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        :host ::ng-deep .e-richtexteditor {
            border-radius: var(--radius-sm);
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class BulletinFormModalComponent implements OnInit {
    @Input() mode: ModalMode = 'add';
    @Input() bulletin: BulletinAdminDto | null = null;
    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<void>();

    private readonly bulletinService = inject(BulletinAdminService);
    private readonly toastService = inject(ToastService);

    // RTE config
    rteTools = JOB_CONFIG_RTE_TOOLS;
    rteHeight = 300;

    // Form fields
    title = signal('');
    text = signal('');
    startDate = signal('');
    endDate = signal('');
    active = signal(true);

    // Validation
    isSaving = signal(false);

    ngOnInit(): void {
        if (this.mode === 'edit' && this.bulletin) {
            this.title.set(this.bulletin.title ?? '');
            this.text.set(this.bulletin.text ?? '');
            this.startDate.set(this.formatDateForInput(this.bulletin.startDate));
            this.endDate.set(this.formatDateForInput(this.bulletin.endDate));
            this.active.set(this.bulletin.active);
        } else {
            // Default start date to today
            this.startDate.set(this.formatDateForInput(new Date().toISOString()));
        }
    }

    onRteChange(event: any): void {
        this.text.set(event.value ?? '');
    }

    isValid(): boolean {
        const hasTitle = this.title().trim().length > 0;
        const hasText = this.text().trim().length > 0;
        const datesValid = !(this.endDate().length > 0 && this.startDate().length > 0 && this.endDate() < this.startDate());
        return hasTitle && hasText && datesValid;
    }

    onSubmit(): void {
        if (!this.isValid() || this.isSaving()) return;

        this.isSaving.set(true);

        if (this.mode === 'add') {
            const request: CreateBulletinRequest = {
                title: this.title().trim(),
                text: this.text(),
                active: this.active(),
                startDate: this.startDate() ? new Date(this.startDate()).toISOString() : undefined,
                endDate: this.endDate() ? new Date(this.endDate()).toISOString() : undefined
            };

            this.bulletinService.createBulletin(request).subscribe({
                next: () => {
                    this.toastService.show(`Bulletin "${this.title()}" created`, 'success');
                    this.saved.emit();
                },
                error: (error) => {
                    this.toastService.show(error.error?.message || 'Failed to create bulletin', 'danger');
                    this.isSaving.set(false);
                }
            });
        } else if (this.bulletin) {
            const request: UpdateBulletinRequest = {
                title: this.title().trim(),
                text: this.text(),
                active: this.active(),
                startDate: this.startDate() ? new Date(this.startDate()).toISOString() : undefined,
                endDate: this.endDate() ? new Date(this.endDate()).toISOString() : undefined
            };

            this.bulletinService.updateBulletin(this.bulletin.bulletinId, request).subscribe({
                next: () => {
                    this.toastService.show(`Bulletin "${this.title()}" updated`, 'success');
                    this.saved.emit();
                },
                error: (error) => {
                    this.toastService.show(error.error?.message || 'Failed to update bulletin', 'danger');
                    this.isSaving.set(false);
                }
            });
        }
    }

    private formatDateForInput(isoDate: string | null | undefined): string {
        if (!isoDate) return '';
        const date = new Date(isoDate);
        return date.toISOString().split('T')[0];
    }
}
