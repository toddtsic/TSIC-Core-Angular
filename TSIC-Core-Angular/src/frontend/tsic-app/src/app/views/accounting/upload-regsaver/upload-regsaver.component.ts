import { Component, ChangeDetectionStrategy, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '@environments/environment';
import { RegSaverUploadResultDto } from '@core/api';

@Component({
    selector: 'app-upload-regsaver',
    standalone: true,
    imports: [CommonModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    templateUrl: './upload-regsaver.component.html',
    styleUrls: ['./upload-regsaver.component.scss'],
})
export class UploadRegSaverComponent {
    private readonly http = inject(HttpClient);
    private readonly endpoint = `${environment.apiUrl}/regsaver-upload/upload`;

    readonly isUploading = signal(false);
    readonly selectedFile = signal<File | null>(null);
    readonly uploadResult = signal<RegSaverUploadResultDto | null>(null);
    readonly errorMessage = signal('');
    readonly isDragOver = signal(false);

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

        this.http.post<RegSaverUploadResultDto>(this.endpoint, formData).subscribe({
            next: result => {
                this.isUploading.set(false);
                this.uploadResult.set(result);
                this.selectedFile.set(null);
            },
            error: err => {
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
