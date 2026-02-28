import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../../environments/environment';
import { UniformUploadResultDto } from '@core/api';

@Component({
  selector: 'app-uniform-upload',
  standalone: true,
  imports: [CommonModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './uniform-upload.component.html',
  styleUrls: ['./uniform-upload.component.scss'],
})
export class UniformUploadComponent {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = `${environment.apiUrl}/uniform-upload`;

  readonly isDownloading = signal(false);
  readonly isUploading = signal(false);
  readonly selectedFile = signal<File | null>(null);
  readonly uploadResult = signal<UniformUploadResultDto | null>(null);
  readonly errorMessage = signal('');
  readonly isDragOver = signal(false);

  downloadTemplate(): void {
    this.isDownloading.set(true);
    this.errorMessage.set('');

    this.http.get(`${this.apiUrl}/template`, { responseType: 'blob' }).subscribe({
      next: (blob) => {
        this.isDownloading.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'uniform-numbers-template.xlsx';
        a.click();
        URL.revokeObjectURL(url);
      },
      error: () => {
        this.isDownloading.set(false);
        this.errorMessage.set('Failed to download template. Please try again.');
      },
    });
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(true);
  }

  onDragLeave(): void {
    this.isDragOver.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.isDragOver.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.selectFile(file);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.selectFile(file);
    input.value = '';
  }

  upload(): void {
    const file = this.selectedFile();
    if (!file) return;

    this.isUploading.set(true);
    this.errorMessage.set('');
    this.uploadResult.set(null);

    const formData = new FormData();
    formData.append('file', file);

    this.http.post<UniformUploadResultDto>(`${this.apiUrl}/upload`, formData).subscribe({
      next: (result) => {
        this.isUploading.set(false);
        this.uploadResult.set(result);
        this.selectedFile.set(null);
      },
      error: (err) => {
        this.isUploading.set(false);
        const msg = err.error?.message || 'Upload failed. Please try again.';
        this.errorMessage.set(msg);
      },
    });
  }

  clearFile(): void {
    this.selectedFile.set(null);
    this.errorMessage.set('');
  }

  private selectFile(file: File): void {
    this.errorMessage.set('');
    this.uploadResult.set(null);

    const ext = file.name.split('.').pop()?.toLowerCase();
    if (ext !== 'xlsx') {
      this.errorMessage.set('Only .xlsx files are accepted.');
      return;
    }

    if (file.size > 10 * 1024 * 1024) {
      this.errorMessage.set('File exceeds 10MB limit.');
      return;
    }

    this.selectedFile.set(file);
  }
}
