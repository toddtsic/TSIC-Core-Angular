import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RichTextEditorAllModule, RichTextEditorComponent } from '@syncfusion/ej2-angular-richtexteditor';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { BulletinAdminService } from '../services/bulletin-admin.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import { JOB_CONFIG_RTE_TOOLS } from '../../../configure/job/shared/rte-config';
import type { BulletinAdminDto, CreateBulletinRequest, UpdateBulletinRequest } from '@core/api';

const BULLETIN_TOKENS = [
    { token: '!JOBNAME', description: 'Event/league name' },
    { token: '!USLAXVALIDTHROUGHDATE', description: 'US Lacrosse valid-through date' },
];

export type ModalMode = 'add' | 'edit';

@Component({
    selector: 'bulletin-form-modal',
    standalone: true,
    imports: [CommonModule, TsicDialogComponent, FormsModule, RichTextEditorAllModule],
    template: `
        <tsic-dialog [open]="true" size="lg" (requestClose)="close.emit()">
            <div class="modal-content bulletin-modal">
                <div class="modal-header">
                    <h5 class="modal-title">
                        <i class="bi" [class]="mode === 'add' ? 'bi-plus-circle' : 'bi-pencil'"></i>
                        {{ mode === 'add' ? 'Add Bulletin' : 'Edit Bulletin' }}
                    </h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <!-- Draft with AI -->
                    <div class="ai-compose-section">
                        <label>Draft with AI</label>
                        <div class="ai-compose-col">
                            <textarea class="form-control"
                                   rows="4"
                                   placeholder="Describe the bulletin you want to create..."
                                   [value]="aiPrompt()"
                                   (input)="aiPrompt.set($any($event.target).value)"
                                   [disabled]="isDrafting()"></textarea>
                            <div class="d-flex align-items-start gap-2">
                                <p class="wizard-tip mb-0 flex-grow-1">
                                    e.g. "registration closes Friday" or "schedule is now posted"
                                </p>
                                <button type="button" class="btn-ai"
                                        (click)="draftWithAi()"
                                        [disabled]="isDrafting() || !aiPrompt().trim()">
                                    @if (isDrafting()) {
                                        <span class="spinner"></span> Drafting...
                                    } @else {
                                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none"
                                          stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                          <path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/>
                                        </svg>
                                        Draft
                                    }
                                </button>
                            </div>
                        </div>
                    </div>

                    <!-- Title -->
                    <div class="mb-3">
                        <label for="bulletinTitle" class="form-label">Title</label>
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
                        <label class="form-label">Content</label>
                        <ejs-richtexteditor #rteEditor
                            [value]="text()"
                            [toolbarSettings]="rteTools"
                            [height]="rteHeight"
                            [enableHtmlSanitizer]="false"
                            (change)="onRteChange($event)">
                        </ejs-richtexteditor>
                    </div>

                    <!-- Token chips -->
                    <div class="token-bar">
                        <span class="token-label">Insert token:</span>
                        @for (t of tokens; track t.token) {
                            <button type="button" class="token-chip"
                                    (click)="insertToken(t.token)"
                                    [title]="t.description">{{ t.token }}</button>
                        }
                    </div>

                    <!-- Date Range + Active -->
                    <div class="meta-row">
                        <div class="meta-field">
                            <label for="startDate" class="form-label">Start Date</label>
                            <input
                                id="startDate"
                                type="date"
                                class="form-control form-control-sm"
                                [value]="startDate()"
                                (input)="startDate.set($any($event.target).value)" />
                        </div>
                        <div class="meta-field">
                            <label for="endDate" class="form-label">End Date</label>
                            <input
                                id="endDate"
                                type="date"
                                class="form-control form-control-sm"
                                [value]="endDate()"
                                (input)="endDate.set($any($event.target).value)"
                                [class.is-invalid]="endDate().length > 0 && startDate().length > 0 && endDate() < startDate()" />
                            @if (endDate().length > 0 && startDate().length > 0 && endDate() < startDate()) {
                                <div class="invalid-feedback">End date must be after start date.</div>
                            }
                        </div>
                        <div class="meta-field meta-toggle">
                            <div class="form-check form-switch">
                                <input id="activeToggle" type="checkbox" class="form-check-input" role="switch"
                                    [checked]="active()"
                                    (change)="active.set($any($event.target).checked)" />
                                <label class="form-check-label" for="activeToggle">
                                    {{ active() ? 'Active' : 'Inactive' }}
                                </label>
                            </div>
                        </div>
                    </div>
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary btn-sm" (click)="onSubmit()" [disabled]="!isValid() || isSaving()">
                        @if (isSaving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        <i class="bi bi-check-lg me-1"></i>{{ mode === 'add' ? 'Add Bulletin' : 'Save Changes' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    styles: [`
        .bulletin-modal .modal-title {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            font-size: var(--font-size-lg);
        }

        .bulletin-modal .modal-body {
            padding: var(--space-4) var(--space-5);
        }

        .bulletin-modal .form-label {
            font-size: var(--font-size-xs);
            font-weight: var(--font-weight-bold);
            text-transform: uppercase;
            letter-spacing: 0.04em;
            color: var(--brand-text);
            margin-bottom: var(--space-1);
        }

        .token-bar {
            display: flex;
            align-items: center;
            gap: var(--space-2);
            margin-bottom: var(--space-4);
        }

        .token-label {
            font-size: var(--font-size-xs);
            color: var(--text-secondary);
            white-space: nowrap;
        }

        .token-chip {
            font-size: var(--font-size-xs);
            font-family: monospace;
            padding: 2px var(--space-2);
            border: 1px solid rgba(var(--bs-primary-rgb), 0.25);
            border-radius: var(--radius-full);
            background: rgba(var(--bs-primary-rgb), 0.08);
            color: var(--bs-primary);
            cursor: pointer;
            transition: all 0.15s ease;

            &:hover {
                background: rgba(var(--bs-primary-rgb), 0.18);
                border-color: var(--bs-primary);
            }
        }

        .meta-row {
            display: flex;
            gap: var(--space-4);
            align-items: flex-end;
        }

        .meta-field {
            flex: 1;
        }

        .meta-toggle {
            flex: 0 0 auto;
            padding-bottom: var(--space-1);
        }

        .bulletin-modal .modal-footer {
            padding: var(--space-3) var(--space-5);
        }

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

    @ViewChild('rteEditor') rteEditor!: RichTextEditorComponent;

    private readonly bulletinService = inject(BulletinAdminService);
    private readonly toastService = inject(ToastService);

    // RTE config
    rteTools = JOB_CONFIG_RTE_TOOLS;
    rteHeight = 300;
    tokens = BULLETIN_TOKENS;

    // Form fields
    title = signal('');
    text = signal('');
    startDate = signal('');
    endDate = signal('');
    active = signal(true);

    // AI drafting
    aiPrompt = signal('');
    isDrafting = signal(false);

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

    insertToken(token: string): void {
        this.rteEditor.focusIn();
        this.rteEditor.executeCommand('insertText', token);
    }

    draftWithAi(): void {
        const prompt = this.aiPrompt().trim();
        if (!prompt) { this.toastService.show('Describe the bulletin you want to create', 'danger', 4000); return; }

        this.isDrafting.set(true);
        this.bulletinService.aiComposeBulletin(prompt).subscribe({
            next: (response) => {
                this.title.set(response.subject);
                this.text.set(response.body);
                if (this.rteEditor) {
                    this.rteEditor.value = response.body;
                }
                this.isDrafting.set(false);
            },
            error: (err) => {
                this.isDrafting.set(false);
                this.toastService.show(`AI draft failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
            }
        });
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
