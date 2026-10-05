import { errorMessage } from '../plans/limit-message';
import { I18nService } from '../core/i18n/i18n.service';
import { Component, EventEmitter, Input, OnDestroy, Output, inject, signal } from '@angular/core';
import { DocumentExport, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { ExportStatusComponent } from './export-status.component';
import { sameWatermark } from './watermark';
import { WatermarkPanelComponent } from './watermark-panel.component';
import { WatermarkStore } from './watermark.store';
import { printPdf } from './print-pdf';

export type ExportKind = 'pdf' | 'print' | 'original';

export const EXPORT_KINDS: readonly { id: ExportKind; label: string }[] = [
  { id: 'pdf', label: 'PDF' },
  { id: 'print', label: 'Print' },
  { id: 'original', label: 'Original file' },
];

const POLL_MS = 1500;
const POLL_LIMIT = 80;
const EXTENSIONS: Record<string, string> = {
  'application/pdf': '.pdf', 'image/png': '.png', 'image/jpeg': '.jpg', 'image/heic': '.heic',
};

@Component({
  selector: 'app-export-panel',
  standalone: true,
  imports: [ExportStatusComponent, WatermarkPanelComponent],
  templateUrl: './export-panel.component.html',
  styleUrl: './export-panel.component.scss',
})
export class ExportPanelComponent implements OnDestroy {
  private readonly api = inject(DocumentsApiService);
  protected readonly i18n = inject(I18nService);
  private readonly watermarks = inject(WatermarkStore);
  @Input({ required: true }) documentId = '';
  @Input() documentTitle = 'document';
  @Input() pages: DocumentPage[] = [];
  @Input() selectedPageId: string | null = null;
  @Input() kind: ExportKind = 'pdf';
  @Input() set latestExport(value: DocumentExport | null | undefined) {
    const current = this.latest();
    if (!value) {
      if (!current) this.latest.set(null);
      return;
    }
    if (!current || value.id === current.id || Date.parse(value.createdAt) > Date.parse(current.createdAt)) {
      this.latest.set(value);
    }
  }
  @Output() readonly exportChange = new EventEmitter<DocumentExport>();

  protected readonly latest = signal<DocumentExport | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly announcement = signal('');
  /** Overridable for tests. */
  protected printFn: (blob: Blob) => Promise<void> = (blob) => printPdf(blob);
  private destroyed = false;

  protected get readyCount(): number {
    return this.pages.filter((p) => p.state === 'Ready').length;
  }

  protected onExportChange(item: DocumentExport): void {
    this.latest.set(item);
    this.exportChange.emit(item);
  }

  async print(): Promise<void> {
    if (this.busy() || !this.readyCount) return;
    this.busy.set(true);
    this.error.set('');
    try {
      // Printing is at the original page size with the chosen watermark: anything else is rebuilt.
      const watermark = this.watermarks.active();
      const latest = this.latest();
      let item = latest?.pageLayout === 'Original' && sameWatermark(latest.watermark, watermark) ? latest : null;
      if (item) {
        try {
          item = await this.api.getExport(this.documentId, item.id);
          this.onExportChange(item);
        } catch {
          item = null;
        }
      }
      if (!item || item.isOutdated || item.state === 'Failed' || item.pageLayout !== 'Original') {
        item = await this.api.createExport(this.documentId, 'Original', false, watermark);
        this.onExportChange(item);
      }
      for (let i = 0; item.state !== 'Ready' && item.state !== 'Failed' && i < POLL_LIMIT; i++) {
        await new Promise((resolve) => setTimeout(resolve, POLL_MS));
        if (this.destroyed) return;
        item = await this.api.getExport(this.documentId, item.id);
        this.onExportChange(item);
      }
      if (item.state !== 'Ready') throw new Error('not ready');
      const blob = await this.api.downloadExport(this.documentId, item.id);
      await this.printFn(blob);
      this.announcement.set(this.i18n.t('ws.printOpened'));
    } catch (error) {
      this.error.set(errorMessage(error, this.i18n.t('ws.printFailed'), (key, params) => this.i18n.t(key, params)));
    } finally {
      this.busy.set(false);
    }
  }

  async downloadOriginal(): Promise<void> {
    if (!this.selectedPageId || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const blob = await this.api.downloadPageOriginal(this.documentId, this.selectedPageId);
      const url = URL.createObjectURL(blob);
      const link = window.document.createElement('a');
      link.href = url;
      link.download = 'original' + (EXTENSIONS[blob.type] ?? '.bin');
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
      this.announcement.set(this.i18n.t('ws.originalStarted'));
    } catch {
      this.error.set(this.i18n.t('ws.originalFailed'));
    } finally {
      this.busy.set(false);
    }
  }

  protected selectedHasOriginal(): boolean {
    return this.pages.find((p) => p.id === this.selectedPageId)?.hasOriginal !== false;
  }

  protected selectedNumber(): number | null {
    return this.pages.find((p) => p.id === this.selectedPageId)?.position ?? null;
  }

  ngOnDestroy(): void {
    this.destroyed = true;
  }
}
