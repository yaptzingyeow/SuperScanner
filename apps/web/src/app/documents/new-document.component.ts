import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
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
  templateUrl: './new-document.component.html',
  styleUrl: './new-document.component.scss',
})
export class NewDocumentComponent {
  private readonly uploadService = inject(UploadService);
  private readonly router = inject(Router);
  protected readonly title = signal('');
  protected readonly selectedFiles = signal<File[]>([]);
  protected readonly errorMessage = signal('');
  protected readonly fileErrors = signal<Array<{ fileName: string; message: string }>>([]);
  protected readonly active = signal(false);
  protected readonly pending = signal<{ documentId: string; uploadIds: string[] } | null>(null);
  protected readonly progress = this.uploadService.progress;
  protected readonly items = () => this.uploadService.items();

  setTitle(value: string): void {
    this.title.set(value);
  }

  selectFile(file: File): void {
    this.selectFiles([file]);
  }

  selectFiles(files: readonly File[]): void {
    this.selectedFiles.set([...files]);
    this.errorMessage.set('');
    this.fileErrors.set([]);
    this.pending.set(null);
  }

  onFileSelected(event: Event): void {
    const files = (event.target as HTMLInputElement).files;
    if (files && files.length > 0 && !this.active()) {
      this.selectFiles(Array.from(files));
      void this.submit();
    }
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    const files = event.dataTransfer?.files;
    if (files && files.length > 0 && !this.active()) {
      this.selectFiles(Array.from(files));
      void this.submit();
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  async continueToImport(): Promise<void> {
    const target = this.pending();
    if (!target) return;
    await this.router.navigate(['/documents', target.documentId, 'import'], {
      queryParams: { uploads: target.uploadIds.join(',') },
    });
  }

  private collectFailures(): Set<string> {
    const rejected = this.items().filter(
      (item) =>
        (item.stage === 'error' || item.stage === 'rejected' || item.stage === 'failed') &&
        item.errorCode,
    );
    this.fileErrors.set(
      rejected.map((item) => ({
        fileName: item.fileName,
        message: SAFE_ERRORS[item.errorCode!] ?? SAFE_ERRORS['request_failed'],
      })),
    );
    return new Set(rejected.flatMap((item) => (item.uploadId ? [item.uploadId] : [])));
  }

  async submit(): Promise<void> {
    const files = this.selectedFiles();
    if (files.length === 0) {
      this.errorMessage.set('Choose a document to scan.');
      return;
    }
    if (this.active() || this.pending()) return;
    const title = this.title().trim() || files[0].name.replace(/\.[^.]*$/, '');

    this.active.set(true);
    this.errorMessage.set('');
    this.fileErrors.set([]);
    try {
      const result = await this.uploadService.startDocument(title, files);
      const rejectedIds = this.collectFailures();
      const accepted = result.uploadIds.filter((id) => !rejectedIds.has(id));
      if (accepted.length === 0) {
        this.errorMessage.set('None of the files could be uploaded.');
        return;
      }
      const target = { documentId: result.documentId, uploadIds: accepted };
      if (this.fileErrors().length > 0) {
        this.pending.set(target);
        return;
      }
      await this.router.navigate(['/documents', target.documentId, 'import'], {
        queryParams: { uploads: accepted.join(',') },
      });
    } catch (error) {
      this.collectFailures();
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
