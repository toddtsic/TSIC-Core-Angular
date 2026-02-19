import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, FormsModule, Validators } from '@angular/forms';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { NavEditorNavItemDto, CreateNavItemRequest, UpdateNavItemRequest } from '@core/api';

export interface NavItemFormResult {
    type: 'create' | 'update';
    navItemId?: number;
    data: CreateNavItemRequest | UpdateNavItemRequest;
}

@Component({
    selector: 'app-nav-item-form-dialog',
    standalone: true,
    imports: [ReactiveFormsModule, FormsModule, TsicDialogComponent],
    template: `
    <tsic-dialog [open]="true" size="md" (requestClose)="cancel()">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title">{{ isEditMode() ? 'Edit' : 'Create' }} {{ isParentItem ? 'Parent' : 'Nav' }} Item</h5>
          <button type="button" class="btn-close" (click)="cancel()"></button>
        </div>

        <div class="modal-body">
          <form [formGroup]="form">
            <!-- Text (required) -->
            <div class="mb-3">
              <label for="text" class="form-label">Text <span class="text-danger">*</span></label>
              <input
                type="text"
                id="text"
                class="form-control"
                formControlName="text"
                placeholder="Menu item display text"
              >
              @if (form.get('text')?.invalid && form.get('text')?.touched) {
                <div class="text-danger small mt-1">Text is required</div>
              }
            </div>

            <!-- Active -->
            <div class="mb-3">
              <div class="form-check">
                <input
                  type="checkbox"
                  id="active"
                  class="form-check-input"
                  formControlName="active"
                >
                <label for="active" class="form-check-label">Active</label>
              </div>
            </div>

            <!-- Icon Name -->
            <div class="mb-3">
              <label for="iconName" class="form-label">Icon Name (Bootstrap Icons)</label>
              <input
                type="text"
                id="iconName"
                class="form-control"
                formControlName="iconName"
                placeholder="e.g., house, person, gear"
              >
              <small class="form-text text-muted">
                Browse icons at <a href="https://icons.getbootstrap.com/" target="_blank" rel="noopener">Bootstrap Icons</a>
              </small>
              <div class="icon-picker">
                @for (icon of commonIcons; track icon) {
                  <button
                    type="button"
                    class="icon-pick-btn"
                    [class.active]="form.get('iconName')?.value === icon"
                    (click)="pickIcon(icon)"
                    [title]="icon"
                  >
                    <i class="bi bi-{{ icon }}"></i>
                  </button>
                }
              </div>
            </div>

            @if (!isParentItem) {
            <!-- Navigation Type Selection -->
            <div class="mb-3">
              <label class="form-label">Navigation Type</label>
              <div class="btn-group w-100" role="group">
                <input
                  type="radio"
                  class="btn-check"
                  id="navTypeRouter"
                  value="router"
                  [(ngModel)]="navType"
                  [ngModelOptions]="{standalone: true}"
                >
                <label class="btn btn-outline-primary" for="navTypeRouter">Angular Route</label>

                <input
                  type="radio"
                  class="btn-check"
                  id="navTypeExternal"
                  value="external"
                  [(ngModel)]="navType"
                  [ngModelOptions]="{standalone: true}"
                >
                <label class="btn btn-outline-primary" for="navTypeExternal">External URL</label>

                <input
                  type="radio"
                  class="btn-check"
                  id="navTypeNone"
                  value="none"
                  [(ngModel)]="navType"
                  [ngModelOptions]="{standalone: true}"
                >
                <label class="btn btn-outline-primary" for="navTypeNone">None (Header)</label>
              </div>
            </div>

            <!-- Router Link (Angular) -->
            @if (navType === 'router') {
              <div class="mb-3">
                <label for="routerLink" class="form-label">Router Link</label>
                <input
                  type="text"
                  id="routerLink"
                  class="form-control"
                  formControlName="routerLink"
                  placeholder="e.g., admin/job-config"
                >
              </div>
            }

            <!-- Navigate URL (External) -->
            @if (navType === 'external') {
              <div class="mb-3">
                <label for="navigateUrl" class="form-label">External URL</label>
                <input
                  type="text"
                  id="navigateUrl"
                  class="form-control"
                  formControlName="navigateUrl"
                  placeholder="https://example.com"
                >
              </div>

              <div class="mb-3">
                <label for="target" class="form-label">Link Target</label>
                <select id="target" class="form-select" formControlName="target">
                  <option value="_self">Same Window (_self)</option>
                  <option value="_blank">New Tab (_blank)</option>
                </select>
              </div>
            }
            }
          </form>
        </div>

        <div class="modal-footer">
          <button type="button" class="btn btn-secondary" (click)="cancel()">Cancel</button>
          <button
            type="button"
            class="btn btn-primary"
            (click)="save()"
            [disabled]="form.invalid"
          >
            {{ isEditMode() ? 'Update' : 'Create' }}
          </button>
        </div>
      </div>
    </tsic-dialog>
  `,
    styles: [`
        .icon-picker {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-1);
            margin-top: var(--space-2);
        }
        .icon-pick-btn {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 36px;
            height: 36px;
            border: 1px solid var(--border-color);
            border-radius: var(--radius-sm);
            background: var(--bs-body-bg);
            color: var(--bs-body-color);
            cursor: pointer;
            font-size: var(--font-size-lg);
            transition: all 0.15s ease;
        }
        .icon-pick-btn:hover {
            border-color: var(--bs-primary);
            color: var(--bs-primary);
        }
        .icon-pick-btn.active {
            border-color: var(--bs-primary);
            background: rgba(var(--bs-primary-rgb), 0.1);
            color: var(--bs-primary);
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class NavItemFormDialogComponent implements OnInit {
    private readonly fb = inject(FormBuilder);

    @Input() navId!: number;
    @Input() parentNavItemId?: number;
    @Input() existingItem?: NavEditorNavItemDto;

    @Output() saved = new EventEmitter<NavItemFormResult>();
    @Output() cancelled = new EventEmitter<void>();

    form!: FormGroup;
    navType = 'router';
    isEditMode = signal(false);
    isParentItem = false;

    readonly commonIcons = [
        'search', 'gear', 'house', 'person', 'people', 'clipboard', 'calendar',
        'cash-stack', 'cart', 'envelope', 'shield', 'trophy', 'bar-chart',
        'list', 'pencil', 'folder', 'megaphone', 'journal', 'tools', 'sliders',
        'grid', 'tags', 'receipt', 'credit-card', 'map', 'flag',
    ];

    ngOnInit(): void {
        this.isEditMode.set(!!this.existingItem);
        this.isParentItem = this.parentNavItemId == null && !this.existingItem?.parentNavItemId;
        this.initializeForm();
        this.detectNavigationType();
        if (this.isParentItem) {
            this.navType = 'none';
        }
    }

    private initializeForm(): void {
        this.form = this.fb.group({
            text: [this.existingItem?.text || '', Validators.required],
            active: [this.existingItem?.active ?? true],
            iconName: [this.existingItem?.iconName || ''],
            routerLink: [this.existingItem?.routerLink || ''],
            navigateUrl: [this.existingItem?.navigateUrl || ''],
            target: [this.existingItem?.target || '_self']
        });
    }

    private detectNavigationType(): void {
        if (!this.existingItem) return;

        if (this.existingItem.routerLink) {
            this.navType = 'router';
        } else if (this.existingItem.navigateUrl) {
            this.navType = 'external';
        } else {
            this.navType = 'none';
        }
    }

    save(): void {
        if (this.form.invalid) return;

        const v = this.form.value;

        const cleanedData = {
            text: v.text,
            active: v.active,
            iconName: v.iconName || null,
            routerLink: this.navType === 'router' ? (v.routerLink || null) : null,
            navigateUrl: this.navType === 'external' ? (v.navigateUrl || null) : null,
            target: this.navType === 'external' ? (v.target || null) : null
        };

        if (this.isEditMode()) {
            this.saved.emit({
                type: 'update',
                navItemId: this.existingItem!.navItemId,
                data: cleanedData as UpdateNavItemRequest
            });
        } else {
            this.saved.emit({
                type: 'create',
                data: {
                    navId: this.navId,
                    parentNavItemId: this.parentNavItemId,
                    ...cleanedData
                } as CreateNavItemRequest
            });
        }
    }

    pickIcon(icon: string): void {
        this.form.get('iconName')?.setValue(icon);
    }

    cancel(): void {
        this.cancelled.emit();
    }
}
