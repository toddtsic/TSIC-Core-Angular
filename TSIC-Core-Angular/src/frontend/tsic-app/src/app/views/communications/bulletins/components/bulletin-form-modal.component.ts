import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, ViewChild, inject, signal, computed, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RichTextEditorAllModule, RichTextEditorComponent } from '@syncfusion/ej2-angular-richtexteditor';
import { Subject, debounceTime, switchMap } from 'rxjs';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import { BulletinAdminService } from '../services/bulletin-admin.service';
import { ToastService } from '../../../../shared-ui/toast.service';
import { AuthService } from '../../../../infrastructure/services/auth.service';
import { JOB_CONFIG_RTE_TOOLS } from '../../../configure/job/shared/rte-config';
import type {
    BulletinAdminDto,
    CreateBulletinRequest,
    UpdateBulletinRequest,
    BulletinTokenCatalogEntryDto,
    JobPulseDto
} from '@core/api';

// Plain-text tokens that live in bulletin content — these are processed by
// BulletinService.ReplaceTextTokens (separate from bulletin resolver !TOKEN resolution).
const TEXT_TOKENS = [
    { token: '!JOBNAME', description: 'Event/league name' },
    { token: '!USLAXVALIDTHROUGHDATE', description: 'US Lacrosse valid-through date' },
];

// Subset of pulse flags relevant to bulletin tokens — shown as simulate toggles.
const SIMULATE_FLAGS: Array<{ key: keyof JobPulseDto; label: string }> = [
    { key: 'playerRegistrationOpen', label: 'Player reg open' },
    { key: 'teamRegistrationOpen', label: 'Team reg open' },
    { key: 'adultRegistrationPlanned', label: 'Adult reg planned' },
    { key: 'schedulePublished', label: 'Schedule published' },
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

                    <!-- Text (RTE) with optional side-by-side preview for SuperUser -->
                    <div class="mb-3">
                        <div class="d-flex align-items-center justify-content-between mb-1">
                            <label class="form-label mb-0">Content</label>
                            <div class="d-flex gap-2">
                                @if (canFormat()) {
                                    <button type="button" class="btn-ai btn-sm"
                                            (click)="formatWithAi()"
                                            [disabled]="isFormatting() || !text().trim()"
                                            title="Reformat with AI using design-system styling + token vocabulary">
                                        @if (isFormatting()) {
                                            <span class="spinner"></span> Formatting...
                                        } @else {
                                            <i class="bi bi-magic"></i> AI Format
                                        }
                                    </button>
                                    @if (preFormatHtml() !== null) {
                                        <button type="button" class="btn btn-outline-secondary btn-sm"
                                                (click)="revertFormat()"
                                                title="Restore content from before AI formatting">
                                            <i class="bi bi-arrow-counterclockwise"></i> Revert
                                        </button>
                                    }
                                }
                                @if (isSuperUser()) {
                                    <button type="button"
                                            class="btn btn-sm"
                                            [class.btn-outline-secondary]="!previewOpen()"
                                            [class.btn-secondary]="previewOpen()"
                                            (click)="togglePreview()">
                                        <i class="bi bi-eye"></i> {{ previewOpen() ? 'Hide' : 'Show' }} Preview
                                    </button>
                                }
                            </div>
                        </div>

                        <div class="editor-preview-grid" [class.split]="previewOpen()">
                            <ejs-richtexteditor #rteEditor
                                [value]="text()"
                                [toolbarSettings]="rteTools"
                                [height]="rteHeight"
                                [enableHtmlSanitizer]="false"
                                (change)="onRteChange($event)">
                            </ejs-richtexteditor>

                            @if (previewOpen()) {
                                <div class="preview-pane">
                                    <div class="preview-pane-header">
                                        <span>Resolved Preview</span>
                                        @if (isPreviewing()) { <span class="spinner-border spinner-border-sm"></span> }
                                    </div>
                                    <div class="preview-sim">
                                        <span class="sim-label">Simulate:</span>
                                        @for (flag of simulateFlags; track flag.key) {
                                            <label class="sim-check">
                                                <input type="checkbox"
                                                       [checked]="pulseOverride()[flag.key] === true"
                                                       (change)="togglePulseFlag(flag.key, $any($event.target).checked)" />
                                                <span>{{ flag.label }}</span>
                                            </label>
                                        }
                                        @if (pulseHasOverrides()) {
                                            <button type="button" class="btn btn-link btn-sm p-0" (click)="resetPulseOverrides()">Reset</button>
                                        }
                                    </div>
                                    <div class="preview-body" [innerHTML]="previewHtml()"></div>
                                </div>
                            }
                        </div>
                    </div>

                    <!-- Token chips: bulletin token catalog + text tokens -->
                    @if (isSuperUser() && tokenCatalog().length > 0) {
                        <div class="token-bar">
                            <span class="token-label">Bulletin tokens:</span>
                            @for (entry of tokenCatalog(); track entry.tokenName) {
                                <button type="button" class="token-chip token-chip-rich"
                                        (click)="insertToken('!' + entry.tokenName)"
                                        [title]="entry.description + (entry.gatingConditions.length ? ' — gates: ' + entry.gatingConditions.join(', ') : '')">
                                    {{ '!' + entry.tokenName }}
                                </button>
                            }
                        </div>
                    }
                    <div class="token-bar">
                        <span class="token-label">Text tokens:</span>
                        @for (t of textTokens; track t.token) {
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
            flex-wrap: wrap;
            align-items: center;
            gap: var(--space-2);
            margin-bottom: var(--space-3);
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

        .token-chip-rich {
            border-color: rgba(var(--bs-success-rgb), 0.35);
            background: rgba(var(--bs-success-rgb), 0.10);
            color: var(--bs-success);

            &:hover {
                background: rgba(var(--bs-success-rgb), 0.22);
                border-color: var(--bs-success);
            }
        }

        .editor-preview-grid {
            display: grid;
            grid-template-columns: 1fr;
            gap: var(--space-3);

            &.split {
                grid-template-columns: 1fr 1fr;
            }
        }

        .preview-pane {
            display: flex;
            flex-direction: column;
            border: 1px solid var(--border-color);
            border-radius: var(--radius-sm);
            min-height: 300px;
            background: var(--neutral-0);
        }

        .preview-pane-header {
            display: flex;
            align-items: center;
            justify-content: space-between;
            padding: var(--space-2) var(--space-3);
            background: var(--neutral-100);
            border-bottom: 1px solid var(--border-color);
            font-size: var(--font-size-xs);
            font-weight: var(--font-weight-semibold);
            text-transform: uppercase;
            letter-spacing: 0.04em;
            color: var(--text-secondary);
        }

        .preview-sim {
            display: flex;
            flex-wrap: wrap;
            align-items: center;
            gap: var(--space-2);
            padding: var(--space-2) var(--space-3);
            background: var(--neutral-50);
            border-bottom: 1px solid var(--border-color);
            font-size: var(--font-size-xs);
        }

        .sim-label {
            color: var(--text-secondary);
            white-space: nowrap;
        }

        .sim-check {
            display: inline-flex;
            align-items: center;
            gap: var(--space-1);
            margin: 0;
            cursor: pointer;
        }

        .preview-body {
            flex: 1;
            padding: var(--space-3);
            overflow-y: auto;
            font-size: var(--font-size-sm);
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
    private readonly authService = inject(AuthService);

    rteTools = JOB_CONFIG_RTE_TOOLS;
    rteHeight = 300;
    textTokens = TEXT_TOKENS;
    simulateFlags = SIMULATE_FLAGS;

    // Form fields
    title = signal('');
    text = signal('');
    startDate = signal('');
    endDate = signal('');
    active = signal(true);

    // AI drafting (existing) + AI formatting (new)
    aiPrompt = signal('');
    isDrafting = signal(false);
    isFormatting = signal(false);
    preFormatHtml = signal<string | null>(null);

    // Validation
    isSaving = signal(false);

    // Token catalog (SuperUser only)
    tokenCatalog = signal<BulletinTokenCatalogEntryDto[]>([]);

    // Preview pane (SuperUser only)
    previewOpen = signal(false);
    previewHtml = signal('');
    isPreviewing = signal(false);
    pulseOverride = signal<Partial<JobPulseDto>>({});

    // Role gates
    readonly isSuperUser = computed(() => this.authService.isSuperuser());
    readonly canFormat = computed(() => this.authService.isAdmin());

    readonly pulseHasOverrides = computed(() => Object.keys(this.pulseOverride()).length > 0);

    private readonly previewTrigger$ = new Subject<void>();

    ngOnInit(): void {
        if (this.mode === 'edit' && this.bulletin) {
            this.title.set(this.bulletin.title ?? '');
            this.text.set(this.bulletin.text ?? '');
            this.startDate.set(this.formatDateForInput(this.bulletin.startDate));
            this.endDate.set(this.formatDateForInput(this.bulletin.endDate));
            this.active.set(this.bulletin.active);
        } else {
            this.startDate.set(this.formatDateForInput(new Date().toISOString()));
        }

        // Fetch catalog for SuperUser so the token sidebar can render.
        if (this.isSuperUser()) {
            this.bulletinService.getTokenCatalog().subscribe({
                next: (entries) => this.tokenCatalog.set(entries),
                error: () => { /* silent — catalog is optional UX */ }
            });
        }

        // Debounced preview pipeline: any trigger → POST /preview → update previewHtml.
        this.previewTrigger$.pipe(
            debounceTime(400),
            switchMap(() => {
                this.isPreviewing.set(true);
                const jobPath = this.authService.getJobPath() ?? '';
                return this.bulletinService.previewBulletin({
                    html: this.text(),
                    jobPath,
                    pulseOverride: this.buildPulseOverrideForRequest()
                });
            })
        ).subscribe({
            next: (res) => {
                this.previewHtml.set(res.html);
                this.isPreviewing.set(false);
            },
            error: () => {
                this.isPreviewing.set(false);
            }
        });
    }

    onRteChange(event: any): void {
        this.text.set(event.value ?? '');
        // Any edit invalidates the "revert" affordance — user has moved on from AI output.
        if (this.preFormatHtml() !== null) {
            this.preFormatHtml.set(null);
        }
        if (this.previewOpen()) {
            this.previewTrigger$.next();
        }
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

    formatWithAi(): void {
        const current = this.text();
        if (!current.trim()) { return; }

        this.isFormatting.set(true);
        this.preFormatHtml.set(current);

        this.bulletinService.aiFormatBulletin(current).subscribe({
            next: (response) => {
                this.text.set(response.html);
                if (this.rteEditor) { this.rteEditor.value = response.html; }
                this.isFormatting.set(false);
                if (this.previewOpen()) { this.previewTrigger$.next(); }
            },
            error: (err) => {
                this.isFormatting.set(false);
                this.preFormatHtml.set(null);
                this.toastService.show(`AI format failed: ${err.error?.message || 'Unknown error'}`, 'danger', 4000);
            }
        });
    }

    revertFormat(): void {
        const previous = this.preFormatHtml();
        if (previous === null) { return; }
        this.text.set(previous);
        if (this.rteEditor) { this.rteEditor.value = previous; }
        this.preFormatHtml.set(null);
        if (this.previewOpen()) { this.previewTrigger$.next(); }
    }

    togglePreview(): void {
        this.previewOpen.set(!this.previewOpen());
        if (this.previewOpen()) {
            this.previewTrigger$.next();
        }
    }

    togglePulseFlag(key: keyof JobPulseDto, checked: boolean): void {
        const next = { ...this.pulseOverride() };
        // Checkbox checked = override to true (flag ON). Unchecked = override to false (flag OFF).
        // To "use real value", user clicks Reset.
        next[key] = checked as any;
        this.pulseOverride.set(next);
        this.previewTrigger$.next();
    }

    resetPulseOverrides(): void {
        this.pulseOverride.set({});
        this.previewTrigger$.next();
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

    /**
     * Backend /preview accepts a full JobPulseDto. Build one from our partial overrides,
     * leaving unmapped fields null — backend uses null as "fetch real pulse then apply
     * overrides on top". Since we always send a complete JobPulseDto here, any field not
     * overridden falls back to a neutral "true" (demo-like) default for simplicity; the
     * user can always click Reset to use the real pulse.
     *
     * In practice: if the user has toggled nothing, we send PulseOverride=undefined so
     * the backend uses the real pulse verbatim.
     */
    private buildPulseOverrideForRequest(): JobPulseDto | undefined {
        const partial = this.pulseOverride();
        if (Object.keys(partial).length === 0) {
            return undefined;
        }
        return {
            playerRegistrationOpen: partial.playerRegistrationOpen ?? true,
            playerRegRequiresToken: partial.playerRegRequiresToken ?? false,
            teamRegistrationOpen: partial.teamRegistrationOpen ?? true,
            teamRegRequiresToken: partial.teamRegRequiresToken ?? false,
            clubRepAllowAdd: partial.clubRepAllowAdd ?? true,
            clubRepAllowEdit: partial.clubRepAllowEdit ?? true,
            clubRepAllowDelete: partial.clubRepAllowDelete ?? true,
            storeEnabled: partial.storeEnabled ?? true,
            storeHasActiveItems: partial.storeHasActiveItems ?? true,
            allowStoreWalkup: partial.allowStoreWalkup ?? true,
            schedulePublished: partial.schedulePublished ?? true,
            playerRegistrationPlanned: partial.playerRegistrationPlanned ?? true,
            adultRegistrationPlanned: partial.adultRegistrationPlanned ?? true,
            publicSuspended: partial.publicSuspended ?? false,
            registrationExpiry: partial.registrationExpiry
        };
    }
}
