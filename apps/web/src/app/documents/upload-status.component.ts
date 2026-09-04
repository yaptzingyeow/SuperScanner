import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DocumentsApiService, UploadStatusDto } from './documents-api.service';

type StatusView = UploadStatusDto | { state: 'Loading' | 'LoadError'; uploadId: string };

@Component({
  selector: 'app-upload-status',
  imports: [RouterLink],
  templateUrl: './upload-status.component.html',
  styleUrl: './upload-status.component.scss',
})
export class UploadStatusComponent implements OnInit, OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DocumentsApiService);
  protected readonly status = signal<StatusView>({ state: 'Loading', uploadId: '' });
  private timer?: ReturnType<typeof setTimeout>;
  private readonly documentId = this.route.snapshot.paramMap.get('documentId') ?? '';
  private readonly uploadId = this.route.snapshot.paramMap.get('uploadId') ?? '';

  ngOnInit(): void {
    void this.reload();
  }

  ngOnDestroy(): void {
    if (this.timer) clearTimeout(this.timer);
  }

  private async reload(): Promise<void> {
    try {
      const status = await this.api.getUploadStatus(this.documentId, this.uploadId);
      this.status.set(status);
      if (status.state === 'AwaitingUpload' || status.state === 'PendingValidation') {
        this.timer = setTimeout(() => void this.reload(), 2000);
      }
    } catch {
      this.status.set({ state: 'LoadError', uploadId: this.uploadId });
    }
  }
}
