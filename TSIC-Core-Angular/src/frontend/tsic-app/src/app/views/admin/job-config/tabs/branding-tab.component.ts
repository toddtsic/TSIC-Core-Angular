import { Component, ChangeDetectionStrategy, inject, signal, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { JobConfigService } from '../job-config.service';
import { ImageUploadComponent } from '../shared/image-upload.component';
import { buildAssetUrl } from '@infrastructure/utils/asset-url.utils';
import type { UpdateJobConfigBrandingRequest } from '@core/api';

@Component({
  selector: 'app-branding-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, ImageUploadComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (svc.branding(); as b) {
      <div class="tab-panel">
        <!-- Banner Toggle -->
        <div class="form-section">
          <h6 class="section-title">
            <i class="bi bi-toggle-on me-2"></i>Banner Settings
          </h6>
          <div class="form-check">
            <input
              type="checkbox"
              class="form-check-input"
              id="bBannerIsCustom"
              [ngModel]="bBannerIsCustom()"
              (ngModelChange)="bBannerIsCustom.set($event); onFieldChange()" />
            <label class="form-check-label" for="bBannerIsCustom">
              Use custom banner images
            </label>
          </div>
          <small class="text-muted d-block mt-1">
            When disabled, a default TSIC banner is shown instead of the images below.
          </small>
        </div>

        <!-- Banner Background -->
        <div class="form-section">
          <h6 class="section-title">
            <i class="bi bi-image me-2"></i>Banner Background
          </h6>
          <p class="text-muted small mb-2">Full-width background image. Resized to max 1920px wide.</p>
          <app-image-upload
            label="Banner Background"
            [imageUrl]="bannerBgUrl()"
            conventionName="paralaxbackgroundimage"
            (uploaded)="onImageUploaded()"
            (deleted)="onImageDeleted()" />
        </div>

        <!-- Banner Overlay -->
        <div class="form-section">
          <h6 class="section-title">
            <i class="bi bi-layers me-2"></i>Banner Overlay
          </h6>
          <p class="text-muted small mb-2">Overlay image displayed on top of the banner background. Resized to max 800px wide.</p>
          <app-image-upload
            label="Banner Overlay"
            [imageUrl]="bannerOverlayUrl()"
            conventionName="paralaxslide1image"
            (uploaded)="onImageUploaded()"
            (deleted)="onImageDeleted()" />

          <div class="row g-3 mt-3">
            <div class="col-md-6">
              <label class="form-label">Overlay Headline</label>
              <input
                type="text"
                class="form-control"
                [ngModel]="overlayText1()"
                (ngModelChange)="overlayText1.set($event); onFieldChange()" />
            </div>
            <div class="col-md-6">
              <label class="form-label">Overlay Subheadline</label>
              <input
                type="text"
                class="form-control"
                [ngModel]="overlayText2()"
                (ngModelChange)="overlayText2.set($event); onFieldChange()" />
            </div>
          </div>
        </div>

        <!-- Logo Header -->
        <div class="form-section">
          <h6 class="section-title">
            <i class="bi bi-badge-tm me-2"></i>Logo Header
          </h6>
          <p class="text-muted small mb-2">Job logo for the header area. Resized to max 400px wide.</p>
          <app-image-upload
            label="Logo Header"
            [imageUrl]="logoHeaderUrl()"
            conventionName="logoheader"
            (uploaded)="onImageUploaded()"
            (deleted)="onImageDeleted()" />
        </div>

        <!-- Save button (text fields + toggle only) -->
        <div class="action-bar">
          <button
            class="btn btn-primary"
            [disabled]="svc.isSaving() || !svc.dirtyTabs().has('branding')"
            (click)="save()">
            @if (svc.isSaving()) {
              <span class="spinner-border spinner-border-sm me-1"></span>
            }
            Save Branding
          </button>
        </div>
      </div>
    }
  `,
})
export class BrandingTabComponent {
  protected readonly svc = inject(JobConfigService);

  // ── Local form model (text/toggle only — images are immediate) ──
  bBannerIsCustom = signal(false);
  overlayText1 = signal<string | null>(null);
  overlayText2 = signal<string | null>(null);

  // ── Image preview URLs ──
  bannerBgUrl = signal<string | null>(null);
  bannerOverlayUrl = signal<string | null>(null);
  logoHeaderUrl = signal<string | null>(null);

  private cleanSnapshot = '';

  constructor() {
    effect(() => {
      const b = this.svc.branding();
      if (!b) return;
      this.bBannerIsCustom.set(b.bBannerIsCustom);
      this.overlayText1.set(b.bannerOverlayText1 ?? null);
      this.overlayText2.set(b.bannerOverlayText2 ?? null);
      this.bannerBgUrl.set(buildAssetUrl(b.bannerBackgroundImage));
      this.bannerOverlayUrl.set(buildAssetUrl(b.bannerOverlayImage));
      this.logoHeaderUrl.set(buildAssetUrl(b.logoHeader));
      this.cleanSnapshot = JSON.stringify(this.buildPayload());
    });
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot) {
      this.svc.markClean('branding');
    } else {
      this.svc.markDirty('branding');
    }
  }

  save(): void {
    this.svc.saveBranding(this.buildPayload());
  }

  onImageUploaded(): void {
    this.svc.loadConfig();
  }

  onImageDeleted(): void {
    this.svc.loadConfig();
  }

  private buildPayload(): UpdateJobConfigBrandingRequest {
    return {
      bBannerIsCustom: this.bBannerIsCustom(),
      bannerOverlayText1: this.overlayText1(),
      bannerOverlayText2: this.overlayText2(),
    };
  }
}
