import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output, OnInit, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { AdminTeamLinkDto, CreateTeamLinkRequest, UpdateTeamLinkRequest, TeamLinkTeamOptionDto } from '@core/api';

export type TeamLinkModalMode = 'add' | 'edit';

export interface TeamLinkFormResult {
    mode: TeamLinkModalMode;
    docId?: string;
    addRequest?: CreateTeamLinkRequest;
    updateRequest?: UpdateTeamLinkRequest;
}

const ALL_TEAMS_SENTINEL = '__all-teams__';

@Component({
    selector: 'team-link-form-modal',
    standalone: true,
    imports: [TsicDialogComponent, FormsModule],
    template: `
        <tsic-dialog [open]="true" size="sm" (requestClose)="close.emit()">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title">{{ mode === 'add' ? 'Add Team Link' : 'Edit Team Link' }}</h5>
                    <button type="button" class="btn-close" (click)="close.emit()" aria-label="Close"></button>
                </div>
                <div class="modal-body">
                    <div class="row g-2">
                        <div class="col-12">
                            <label for="teamSelect" class="field-label">Team <span class="req-star">*</span></label>
                            <select id="teamSelect" class="field-input field-select"
                                [ngModel]="selectedTeamValue()"
                                (ngModelChange)="onTeamChange($event)">
                                <option value="" disabled>Select a team...</option>
                                <option [value]="allTeamsSentinel">— All Teams —</option>
                                @for (t of availableTeams; track t.teamId) {
                                    <option [value]="t.teamId">{{ t.display }}</option>
                                }
                            </select>
                        </div>
                        <div class="col-12">
                            <label for="linkLabel" class="field-label">Label <span class="req-star">*</span></label>
                            <input id="linkLabel" type="text" class="field-input"
                                [ngModel]="label()"
                                (ngModelChange)="label.set($event)"
                                maxlength="255" />
                        </div>
                        <div class="col-12">
                            <label for="linkUrl" class="field-label">URL <span class="req-star">*</span></label>
                            <input id="linkUrl" type="url" class="field-input"
                                placeholder="https://..."
                                [ngModel]="docUrl()"
                                (ngModelChange)="docUrl.set($event)" />
                        </div>
                    </div>
                    @if (errorMessage()) {
                        <div class="alert alert-danger py-2 mt-2 mb-0">{{ errorMessage() }}</div>
                    }
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-outline-secondary btn-sm" (click)="close.emit()">Cancel</button>
                    <button type="button" class="btn btn-primary btn-sm"
                        [disabled]="!isValid() || saving()"
                        (click)="onSave()">
                        @if (saving()) {
                            <span class="spinner-border spinner-border-sm me-1"></span>
                        }
                        {{ mode === 'add' ? 'Add' : 'Save' }}
                    </button>
                </div>
            </div>
        </tsic-dialog>
    `,
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class TeamLinkFormModalComponent implements OnInit {
    @Input() mode: TeamLinkModalMode = 'add';
    @Input() availableTeams: TeamLinkTeamOptionDto[] = [];
    @Input() editLink: AdminTeamLinkDto | null = null;

    @Output() close = new EventEmitter<void>();
    @Output() saved = new EventEmitter<TeamLinkFormResult>();

    readonly allTeamsSentinel = ALL_TEAMS_SENTINEL;

    readonly selectedTeamValue = signal<string>('');
    readonly label = signal('');
    readonly docUrl = signal('');
    readonly errorMessage = signal<string | null>(null);
    readonly saving = signal(false);

    readonly isValid = computed(() =>
        this.selectedTeamValue() !== '' &&
        this.label().trim().length > 0 &&
        this.docUrl().trim().length > 0
    );

    ngOnInit() {
        if (this.mode === 'edit' && this.editLink) {
            this.selectedTeamValue.set(this.editLink.teamId ?? ALL_TEAMS_SENTINEL);
            this.label.set(this.editLink.label);
            this.docUrl.set(this.editLink.docUrl);
        }
    }

    onTeamChange(value: string) {
        this.selectedTeamValue.set(value);
    }

    onSave() {
        if (!this.isValid()) return;
        this.saving.set(true);
        this.errorMessage.set(null);

        const teamValue = this.selectedTeamValue();
        const teamId = teamValue === ALL_TEAMS_SENTINEL ? null : teamValue;
        const label = this.label().trim();
        const docUrl = this.docUrl().trim();

        const result: TeamLinkFormResult = { mode: this.mode };

        if (this.mode === 'add') {
            result.addRequest = { teamId, label, docUrl };
        } else {
            result.docId = this.editLink?.docId;
            result.updateRequest = { teamId, label, docUrl };
        }

        this.saved.emit(result);
    }
}
