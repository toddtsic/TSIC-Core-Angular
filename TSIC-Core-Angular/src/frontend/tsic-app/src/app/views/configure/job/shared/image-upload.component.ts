import {
  Component, ChangeDetectionStrategy, inject, input, output, signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { JobConfigService } from '../job-config.service';

@Component({
  selector: 'app-image-upload',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="image-upload-zone"
         [class.has-image]="imageUrl()"
         [class.drag-over]="isDragOver()"
         (dragover)="onDragOver($event)"
         (dragleave)="isDragOver.set(false)"
         (drop)="onDrop($event)">

      @if (isUploading()) {
        <div class="upload-overlay">
          <div class="spinner-border text-primary" role="status">
            <span class="visually-hidden">Uploading...</span>
          </div>
        </div>
      }

      @if (imageUrl()) {
        <div class="preview-wrapper">
          <img [src]="imageUrl()" [alt]="label()" class="preview-img" />
          <button
            type="button"
            class="delete-btn"
            title="Remove image"
            [disabled]="isUploading()"
            (click)="onDelete()">
            <i class="bi bi-x-lg"></i>
          </button>
        </div>
      } @else {
        <div class="drop-prompt" (click)="fileInput.click()">
          <i class="bi bi-cloud-arrow-up"></i>
          <span>Drop image here or click to browse</span>
          <small class="text-muted">{{ acceptTypes() }} &bull; Max {{ maxSizeMb() }}MB</small>
        </div>
      }

      <input
        #fileInput
        type="file"
        class="d-none"
        [accept]="acceptTypes()"
        (change)="onFileSelected($event)" />

      @if (imageUrl()) {
        <button type="button" class="btn btn-sm btn-outline-secondary mt-2"
                (click)="fileInput.click()" [disabled]="isUploading()">
          <i class="bi bi-arrow-repeat me-1"></i>Replace
        </button>
      }
    </div>

    @if (errorMsg()) {
      <div class="text-danger mt-1 small">{{ errorMsg() }}</div>
    }
  `,
  styles: [`
    .image-upload-zone {
      position: relative;
      border: 2px dashed var(--border-color);
      border-radius: var(--radius-md);
      padding: var(--space-3);
      text-align: center;
      transition: border-color 0.15s ease, background-color 0.15s ease;
    }

    .image-upload-zone.drag-over {
      border-color: var(--bs-primary);
      background: rgba(var(--bs-primary-rgb), 0.05);
    }

    .image-upload-zone.has-image {
      border-style: solid;
    }

    .drop-prompt {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--space-2);
      padding: var(--space-6) var(--space-4);
      cursor: pointer;
      color: var(--brand-text-muted);

      .bi-cloud-arrow-up {
        font-size: 2rem;
        color: var(--bs-primary);
      }
    }

    .preview-wrapper {
      position: relative;
      display: inline-block;
    }

    .preview-img {
      max-width: 100%;
      max-height: 200px;
      border-radius: var(--radius-sm);
      object-fit: contain;
    }

    .delete-btn {
      position: absolute;
      top: var(--space-1);
      right: var(--space-1);
      width: 28px;
      height: 28px;
      border-radius: var(--radius-full);
      border: none;
      background: var(--bs-danger);
      color: var(--bs-light);
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      font-size: var(--font-size-xs);
      box-shadow: var(--shadow-sm);
    }

    .delete-btn:hover {
      opacity: 0.85;
    }

    .upload-overlay {
      position: absolute;
      inset: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      background: rgba(var(--bs-body-bg-rgb, 255, 255, 255), 0.7);
      border-radius: var(--radius-md);
      z-index: 2;
    }
  `],
})
export class ImageUploadComponent {
  private readonly svc = inject(JobConfigService);

  readonly label = input.required<string>();
  readonly imageUrl = input<string | null>(null);
  readonly conventionName = input.required<string>();
  readonly acceptTypes = input('.jpg,.jpeg,.png,.webp');
  readonly maxSizeMb = input(5);

  readonly uploaded = output<string>();
  readonly deleted = output<void>();

  readonly isUploading = signal(false);
  readonly isDragOver = signal(false);
  readonly errorMsg = signal('');

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.upload(file);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.upload(file);
    input.value = ''; // allow re-selecting the same file
  }

  onDelete(): void {
    this.isUploading.set(true);
    this.errorMsg.set('');
    this.svc.deleteBrandingImage(this.conventionName()).subscribe({
      next: () => {
        this.isUploading.set(false);
        this.deleted.emit();
      },
      error: () => {
        this.isUploading.set(false);
        this.errorMsg.set('Failed to delete image.');
      },
    });
  }

  private upload(file: File): void {
    this.errorMsg.set('');

    // Client-side validation
    const maxBytes = this.maxSizeMb() * 1024 * 1024;
    if (file.size > maxBytes) {
      this.errorMsg.set(`File exceeds ${this.maxSizeMb()}MB limit.`);
      return;
    }

    const allowed = this.acceptTypes().split(',').map(t => t.trim().toLowerCase());
    const ext = '.' + file.name.split('.').pop()?.toLowerCase();
    if (!allowed.some(a => a === ext || a === file.type)) {
      this.errorMsg.set(`Invalid file type. Allowed: ${this.acceptTypes()}`);
      return;
    }

    this.isUploading.set(true);
    this.svc.uploadBrandingImage(this.conventionName(), file).subscribe({
      next: (result) => {
        this.isUploading.set(false);
        this.uploaded.emit(result.url);
      },
      error: () => {
        this.isUploading.set(false);
        this.errorMsg.set('Upload failed. Please try again.');
      },
    });
  }
}
