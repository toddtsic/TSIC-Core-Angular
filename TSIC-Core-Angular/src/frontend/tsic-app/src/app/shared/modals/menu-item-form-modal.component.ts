import { ChangeDetectionStrategy, Component, inject, signal, OnInit, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, FormsModule, Validators } from '@angular/forms';
import { NgbActiveModal } from '@ng-bootstrap/ng-bootstrap';
import type { MenuItemAdminDto, CreateMenuItemRequest, UpdateMenuItemRequest } from '@core/api';

@Component({
    selector: 'app-menu-item-form-modal',
    standalone: true,
    imports: [CommonModule, ReactiveFormsModule, FormsModule],
    template: `
    <div class="modal-header">
      <h5 class="modal-title">{{ isEditMode() ? 'Edit' : 'Create' }} Menu Item</h5>
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
            Browse icons at <a href="https://icons.getbootstrap.com/" target="_blank">Bootstrap Icons</a>
          </small>
        </div>

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
              id="navTypeLegacy"
              value="legacy"
              [(ngModel)]="navType"
              [ngModelOptions]="{standalone: true}"
            >
            <label class="btn btn-outline-primary" for="navTypeLegacy">Legacy MVC</label>
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
              placeholder="/dashboard"
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

        <!-- Legacy MVC (Controller/Action) -->
        @if (navType === 'legacy') {
          <div class="mb-3">
            <label for="controller" class="form-label">Controller</label>
            <input
              type="text"
              id="controller"
              class="form-control"
              formControlName="controller"
              placeholder="Home"
            >
          </div>

          <div class="mb-3">
            <label for="action" class="form-label">Action</label>
            <input
              type="text"
              id="action"
              class="form-control"
              formControlName="action"
              placeholder="Index"
            >
          </div>
        }
      </form>
    </div>

    <div class="modal-footer">
      <button type="button" class="btn btn-secondary" (click)="cancel()">Cancel</button>
      <button
        type="button"
        class="btn btn-primary"
        (click)="save()"
        [disabled]="form.invalid || isSaving()"
      >
        @if (isSaving()) {
          <span class="spinner-border spinner-border-sm me-2"></span>
        }
        {{ isEditMode() ? 'Update' : 'Create' }}
      </button>
    </div>
  `,
    styles: [`
    .modal-body {
      max-height: 70vh;
      overflow-y: auto;
    }
  `],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class MenuItemFormModalComponent implements OnInit {
    private readonly fb = inject(FormBuilder);
    public readonly activeModal = inject(NgbActiveModal);

    // Inputs from parent
    @Input() menuId!: string;
    @Input() parentMenuItemId?: string;
    @Input() existingItem?: MenuItemAdminDto;

    // Component state
    form!: FormGroup;
    navType = 'router'; // 'router' | 'external' | 'legacy'
    isSaving = signal(false);
    isEditMode = signal(false);

    ngOnInit(): void {
        this.isEditMode.set(!!this.existingItem);
        this.initializeForm();
        this.detectNavigationType();
    }

    private initializeForm(): void {
        this.form = this.fb.group({
            text: [this.existingItem?.text || '', Validators.required],
            active: [this.existingItem?.active ?? true],
            iconName: [this.existingItem?.iconName || ''],
            routerLink: [this.existingItem?.routerLink || ''],
            navigateUrl: [this.existingItem?.navigateUrl || ''],
            controller: [this.existingItem?.controller || ''],
            action: [this.existingItem?.action || ''],
            target: [this.existingItem?.target || '_self']
        });
    }

    private detectNavigationType(): void {
        if (!this.existingItem) return;

        if (this.existingItem.routerLink) {
            this.navType = 'router';
        } else if (this.existingItem.navigateUrl) {
            this.navType = 'external';
        } else if (this.existingItem.controller || this.existingItem.action) {
            this.navType = 'legacy';
        }
    }

    save(): void {
        if (this.form.invalid) return;

        const formValue = this.form.value;

        // Clear irrelevant fields based on navigation type
        const cleanedData: Partial<CreateMenuItemRequest | UpdateMenuItemRequest> = {
            text: formValue.text,
            active: formValue.active,
            iconName: formValue.iconName || undefined,
            routerLink: this.navType === 'router' ? (formValue.routerLink || undefined) : undefined,
            navigateUrl: this.navType === 'external' ? (formValue.navigateUrl || undefined) : undefined,
            controller: this.navType === 'legacy' ? (formValue.controller || undefined) : undefined,
            action: this.navType === 'legacy' ? (formValue.action || undefined) : undefined,
            target: this.navType === 'external' ? (formValue.target || undefined) : undefined
        };

        if (this.isEditMode()) {
            // Update existing item
            this.activeModal.close({
                type: 'update',
                data: cleanedData as UpdateMenuItemRequest,
                menuItemId: this.existingItem!.menuItemId
            });
        } else {
            // Create new item
            const createRequest: CreateMenuItemRequest = {
                menuId: this.menuId,
                parentMenuItemId: this.parentMenuItemId,
                ...cleanedData
            } as CreateMenuItemRequest;

            this.activeModal.close({
                type: 'create',
                data: createRequest
            });
        }
    }

    cancel(): void {
        this.activeModal.dismiss();
    }
}
