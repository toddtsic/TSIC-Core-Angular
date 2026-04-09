import { Component, ChangeDetectionStrategy, inject, computed, linkedSignal, OnInit } from '@angular/core';
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
            <label class="field-label" for="bBannerIsCustom">
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
              <label class="field-label">Overlay Headline</label>
              <textarea
                rows="2"
                class="field-input"
                placeholder="Banner headline text"
                [ngModel]="overlayText1()"
                (ngModelChange)="overlayText1.set($event); onFieldChange()"></textarea>
            </div>
            <div class="col-md-6">
              <label class="field-label">Overlay Subheadline</label>
              <textarea
                rows="2"
                class="field-input"
                placeholder="Banner subheadline text"
                [ngModel]="overlayText2()"
                (ngModelChange)="overlayText2.set($event); onFieldChange()"></textarea>
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

      </div>
    }
  `,
})
export class BrandingTabComponent implements OnInit {
  protected readonly svc = inject(JobConfigService);

  // ── Local form model (text/toggle only — images are immediate) ──
  bBannerIsCustom = linkedSignal(() => this.svc.branding()?.bBannerIsCustom ?? false);
  overlayText1 = linkedSignal(() => this.svc.branding()?.bannerOverlayText1 ?? null);
  overlayText2 = linkedSignal(() => this.svc.branding()?.bannerOverlayText2 ?? null);

  // ── Image preview URLs (read-only, derived from server data) ──
  bannerBgUrl = computed(() => buildAssetUrl(this.svc.branding()?.bannerBackgroundImage));
  bannerOverlayUrl = computed(() => buildAssetUrl(this.svc.branding()?.bannerOverlayImage));
  logoHeaderUrl = computed(() => buildAssetUrl(this.svc.branding()?.logoHeader));

  private readonly cleanSnapshot = computed(() => {
    const b = this.svc.branding();
    if (!b) return '';
    return JSON.stringify({
      bBannerIsCustom: b.bBannerIsCustom,
      bannerOverlayText1: b.bannerOverlayText1 ?? null,
      bannerOverlayText2: b.bannerOverlayText2 ?? null,
    } satisfies UpdateJobConfigBrandingRequest);
  });

  ngOnInit(): void {
    this.svc.saveHandler.set(() => this.save());
  }

  onFieldChange(): void {
    if (JSON.stringify(this.buildPayload()) === this.cleanSnapshot()) {
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
