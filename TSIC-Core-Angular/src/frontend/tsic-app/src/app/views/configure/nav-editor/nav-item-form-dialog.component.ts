import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, FormsModule, Validators } from '@angular/forms';
import { Route, Router } from '@angular/router';
import { TsicDialogComponent } from '@shared-ui/components/tsic-dialog/tsic-dialog.component';
import type { NavEditorNavItemDto, NavVisibilityOptionsDto, CreateNavItemRequest, UpdateNavItemRequest } from '@core/api';

/** Deserialized shape of the VisibilityRules JSON. */
interface VisibilityRules {
    sports?: string[];
    jobTypes?: string[];
    customersDeny?: string[];
}

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
                <div class="form-check form-switch mb-2">
                  <input
                    type="checkbox"
                    class="form-check-input"
                    id="customRouteToggle"
                    [(ngModel)]="useCustomRoute"
                    [ngModelOptions]="{standalone: true}"
                  >
                  <label class="form-check-label text-muted small" for="customRouteToggle">
                    Type custom route (e.g. reporting/Get_JobPlayers_TSICDAILY)
                  </label>
                </div>
                @if (!useCustomRoute) {
                  <select
                    id="routerLink"
                    class="form-select"
                    formControlName="routerLink"
                  >
                    <option value="">-- Select a route --</option>
                    @for (route of knownRoutes; track route) {
                      <option [value]="route">{{ route }}</option>
                    }
                  </select>
                } @else {
                  <input
                    type="text"
                    id="routerLinkCustom"
                    class="form-control"
                    formControlName="routerLink"
                    placeholder="e.g. reporting/Get_JobPlayers_TSICDAILY"
                  >
                  <small class="form-text text-muted">
                    Use for parameterized routes like <code>reporting/&lt;action&gt;</code> that don't appear in the dropdown.
                  </small>
                }
                @if (form.get('routerLink')?.value && !useCustomRoute && !knownRoutes.includes(form.get('routerLink')?.value)) {
                  <small class="text-warning mt-1 d-block">
                    <i class="bi bi-exclamation-triangle me-1"></i>
                    Route "{{ form.get('routerLink')?.value }}" is not in the known routes manifest.
                  </small>
                }
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

            <!-- ── Visibility Rules (platform defaults only) ── -->
            @if (isDefaultNav && visibilityOptions) {
              <hr class="my-3">
              <div class="visibility-rules">
                <div class="d-flex align-items-center mb-2 cursor-pointer" (click)="rulesExpanded.set(!rulesExpanded())">
                  <i class="bi me-2" [class.bi-chevron-right]="!rulesExpanded()" [class.bi-chevron-down]="rulesExpanded()"></i>
                  <strong class="small text-uppercase">Visibility Rules</strong>
                  @if (hasAnyRules()) {
                    <span class="badge bg-primary ms-2">{{ activeRuleCount() }}</span>
                  }
                </div>

                @if (rulesExpanded()) {
                  <!-- Sports (allowlist) -->
                  @if (visibilityOptions.sports.length > 0) {
                    <div class="rule-section mb-3">
                      <label class="form-label small fw-medium">
                        Sports
                        <span class="text-muted fw-normal">(show only for selected; empty = all)</span>
                      </label>
                      <div class="checkbox-grid">
                        @for (sport of visibilityOptions.sports; track sport) {
                          <div class="form-check form-check-inline">
                            <input
                              type="checkbox"
                              class="form-check-input"
                              [id]="'sport-' + sport"
                              [checked]="selectedSports().includes(sport)"
                              (change)="toggleSelection('sports', sport)"
                            >
                            <label class="form-check-label small" [for]="'sport-' + sport">{{ sport }}</label>
                          </div>
                        }
                      </div>
                    </div>
                  }

                  <!-- Job Types (allowlist) -->
                  @if (visibilityOptions.jobTypes.length > 0) {
                    <div class="rule-section mb-3">
                      <label class="form-label small fw-medium">
                        Job Types
                        <span class="text-muted fw-normal">(show only for selected; empty = all)</span>
                      </label>
                      <div class="checkbox-grid">
                        @for (jt of visibilityOptions.jobTypes; track jt) {
                          <div class="form-check form-check-inline">
                            <input
                              type="checkbox"
                              class="form-check-input"
                              [id]="'jt-' + jt"
                              [checked]="selectedJobTypes().includes(jt)"
                              (change)="toggleSelection('jobTypes', jt)"
                            >
                            <label class="form-check-label small" [for]="'jt-' + jt">{{ jt }}</label>
                          </div>
                        }
                      </div>
                    </div>
                  }

                  <!-- Customers Deny (denylist) -->
                  @if (visibilityOptions.customers.length > 0) {
                    <div class="rule-section mb-3">
                      <label class="form-label small fw-medium">
                        Customers
                        <span class="text-muted fw-normal">(hide from selected; empty = none hidden)</span>
                      </label>
                      <div class="checkbox-grid">
                        @for (cust of visibilityOptions.customers; track cust) {
                          <div class="form-check form-check-inline">
                            <input
                              type="checkbox"
                              class="form-check-input"
                              [id]="'cust-' + cust"
                              [checked]="selectedCustomersDeny().includes(cust)"
                              (change)="toggleSelection('customersDeny', cust)"
                            >
                            <label class="form-check-label small" [for]="'cust-' + cust">{{ cust }}</label>
                          </div>
                        }
                      </div>
                    </div>
                  }
                }
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
        .cursor-pointer { cursor: pointer; }
        .checkbox-grid {
            display: flex;
            flex-wrap: wrap;
            gap: var(--space-2) var(--space-4);
        }
        .rule-section {
            padding-left: var(--space-3);
        }
    `],
    changeDetection: ChangeDetectionStrategy.OnPush
})
export class NavItemFormDialogComponent implements OnInit {
    private readonly fb = inject(FormBuilder);
    private readonly router = inject(Router);

    @Input() navId!: number;
    @Input() parentNavItemId?: number;
    @Input() existingItem?: NavEditorNavItemDto;
    @Input() isDefaultNav = false;
    @Input() visibilityOptions?: NavVisibilityOptionsDto;

    @Output() saved = new EventEmitter<NavItemFormResult>();
    @Output() cancelled = new EventEmitter<void>();

    form!: FormGroup;
    navType = 'router';
    useCustomRoute = false;
    isEditMode = signal(false);
    isParentItem = false;

    // Visibility rules state
    rulesExpanded = signal(false);
    selectedSports = signal<string[]>([]);
    selectedJobTypes = signal<string[]>([]);
    selectedCustomersDeny = signal<string[]>([]);

    readonly commonIcons = [
        'search', 'gear', 'house', 'person', 'people', 'clipboard', 'calendar',
        'cash-stack', 'cart', 'envelope', 'shield', 'trophy', 'bar-chart',
        'list', 'pencil', 'folder', 'megaphone', 'journal', 'tools', 'sliders',
        'grid', 'tags', 'receipt', 'credit-card', 'map', 'flag',
    ];

    readonly knownRoutes: string[] = this.buildKnownRoutes();

    ngOnInit(): void {
        this.isEditMode.set(!!this.existingItem);
        this.isParentItem = this.parentNavItemId == null && !this.existingItem?.parentNavItemId;
        this.initializeForm();
        this.detectNavigationType();
        if (this.isParentItem) {
            this.navType = 'none';
        }
        this.initializeVisibilityRules();
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
            this.useCustomRoute = !this.knownRoutes.includes(this.existingItem.routerLink);
        } else if (this.existingItem.navigateUrl) {
            this.navType = 'external';
        } else {
            this.navType = 'none';
        }
    }

    private initializeVisibilityRules(): void {
        if (!this.existingItem?.visibilityRules) return;

        try {
            const rules: VisibilityRules = JSON.parse(this.existingItem.visibilityRules);
            this.selectedSports.set(rules.sports ?? []);
            this.selectedJobTypes.set(rules.jobTypes ?? []);
            this.selectedCustomersDeny.set(rules.customersDeny ?? []);
            // Auto-expand if rules exist
            if ((rules.sports?.length ?? 0) > 0 || (rules.jobTypes?.length ?? 0) > 0 || (rules.customersDeny?.length ?? 0) > 0) {
                this.rulesExpanded.set(true);
            }
        } catch {
            // Malformed JSON — ignore
        }
    }

    toggleSelection(dimension: 'sports' | 'jobTypes' | 'customersDeny', value: string): void {
        const signalMap = {
            sports: this.selectedSports,
            jobTypes: this.selectedJobTypes,
            customersDeny: this.selectedCustomersDeny
        };
        const sig = signalMap[dimension];
        const current = sig();
        if (current.includes(value)) {
            sig.set(current.filter(v => v !== value));
        } else {
            sig.set([...current, value]);
        }
    }

    hasAnyRules(): boolean {
        return this.selectedSports().length > 0
            || this.selectedJobTypes().length > 0
            || this.selectedCustomersDeny().length > 0;
    }

    activeRuleCount(): number {
        let count = 0;
        if (this.selectedSports().length > 0) count++;
        if (this.selectedJobTypes().length > 0) count++;
        if (this.selectedCustomersDeny().length > 0) count++;
        return count;
    }

    private serializeVisibilityRules(): string | null {
        if (!this.hasAnyRules()) return null;

        const rules: VisibilityRules = {};
        if (this.selectedSports().length > 0) rules.sports = this.selectedSports();
        if (this.selectedJobTypes().length > 0) rules.jobTypes = this.selectedJobTypes();
        if (this.selectedCustomersDeny().length > 0) rules.customersDeny = this.selectedCustomersDeny();

        return JSON.stringify(rules);
    }

    save(): void {
        if (this.form.invalid) return;

        const v = this.form.value;
        const visibilityRules = this.isDefaultNav ? this.serializeVisibilityRules() : (this.existingItem?.visibilityRules ?? null);

        const cleanedData = {
            text: v.text,
            active: v.active,
            iconName: v.iconName || null,
            routerLink: this.navType === 'router' ? (v.routerLink || null) : null,
            navigateUrl: this.navType === 'external' ? (v.navigateUrl || null) : null,
            target: this.navType === 'external' ? (v.target || null) : null,
            visibilityRules
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

    private buildKnownRoutes(): string[] {
        const paths = new Set<string>();
        const jobPathRoute = this.router.config.find(r => r.path === ':jobPath');
        if (jobPathRoute?.children) {
            this.collectPaths(jobPathRoute.children, '', paths);
        }
        return Array.from(paths).sort();
    }

    private collectPaths(routes: Route[], prefix: string, paths: Set<string>): void {
        for (const route of routes) {
            if (!route.path && route.path !== '') continue;
            if (route.path.includes(':')) continue; // skip parameterized routes
            const fullPath = prefix ? `${prefix}/${route.path}` : route.path;
            if (fullPath) {
                paths.add(fullPath);
            }
            if (route.children) {
                this.collectPaths(route.children, fullPath, paths);
            }
        }
    }
}
