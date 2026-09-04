import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { UploadFlowError, UploadService } from './upload.service';

const SAFE_ERRORS: Record<string, string> = {
  unsupported_type: 'Choose a PDF, JPEG, PNG, or HEIC file.',
  empty_file: 'Choose a file that is not empty.',
  file_too_large: 'Choose a file smaller than 25 MB.',
  intent_expired: 'The secure upload link expired. Please try again.',
  upload_failed: 'The file could not be uploaded. Please try again.',
  request_failed: 'We could not start the scan. Please try again.',
};

@Component({
  selector: 'app-new-document',
  imports: [RouterLink],
  templateUrl: './new-document.component.html',
  styleUrl: './new-document.component.scss',
})
export class NewDocumentComponent {
  private readonly uploadService = inject(UploadService);
  private readonly router = inject(Router);
  protected readonly title = signal('');
  protected readonly selectedFile = signal<File | null>(null);
  protected readonly errorMessage = signal('');
  protected readonly active = signal(false);
  protected readonly progress = this.uploadService.progress;

  setTitle(value: string): void {
    this.title.set(value);
  }

  selectFile(file: File): void {
    this.selectedFile.set(file);
    this.errorMessage.set('');
  }

  onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.item(0);
    if (file) this.selectFile(file);
  }

  async submit(): Promise<void> {
    const file = this.selectedFile();
    const title = this.title().trim();
    if (!title) {
      this.errorMessage.set('Enter a document title.');
      return;
    }
    if (!file) {
      this.errorMessage.set('Choose a document to scan.');
      return;
    }
    if (this.active()) return;

    this.active.set(true);
    this.errorMessage.set('');
    try {
      const result = await this.uploadService.upload(title, file);
      await this.router.navigate(['/documents', result.documentId, 'uploads', result.uploadId]);
    } catch (error) {
      const code =
        error instanceof UploadFlowError
          ? error.code
          : ((error as { code?: string } | null)?.code ?? 'request_failed');
      this.errorMessage.set(SAFE_ERRORS[code] ?? SAFE_ERRORS['request_failed']);
    } finally {
      this.active.set(false);
    }
  }

  protected formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }
}
