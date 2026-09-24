import { Component, Input, OnChanges, OnDestroy, OnInit, SimpleChanges, inject, signal } from '@angular/core';
import { DocumentExport, DocumentExportPreview, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';

@Component({
  selector: 'app-export-status',
  standalone: true,
  templateUrl: './export-status.component.html',
  styleUrl: './export-status.component.scss',
})
export class ExportStatusComponent implements OnInit, OnChanges, OnDestroy {
  private readonly api = inject(DocumentsApiService);
  @Input({ required: true }) documentId = '';
  @Input({ required: true }) documentTitle = 'document';
  @Input() pages: DocumentPage[] = [];
  @Input() initialExport?: DocumentExport | null;
  protected readonly current = signal<DocumentExport | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly announcement = signal('');
  protected readonly preview = signal<DocumentExportPreview | null>(null);
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;

  protected get readyCount(): number {
    return this.pages.filter((page) => page.state === 'Ready').length;
  }
  protected get excludedCount(): number {
    return this.pages.length - this.readyCount;
  }

  protected searchabilityCopy(item: DocumentExport): string {
    switch (item.searchability) {
      case 'Searchable':
        return item.readyPageCount === 1
          ? 'The page has searchable text.'
          : `All ${item.readyPageCount} pages have searchable text.`;
      case 'PartiallySearchable':
        return `${item.searchablePageCount} of ${item.readyPageCount} pages have searchable text.`;
      default:
        return 'Image-only PDF. Text cannot be searched or selected.';
    }
  }

  ngOnInit(): void {
    this.current.set(this.initialExport ?? null);
    if (this.initialExport && this.pending(this.initialExport))
      this.schedulePoll(this.initialExport.id, 0);
  }

  async generate(): Promise<void> {
    if (!this.readyCount || this.busy()) return;
    const message = `${this.readyCount} ready ${this.readyCount === 1 ? 'page' : 'pages'} will be included. ${this.excludedCount} ${this.excludedCount === 1 ? 'page' : 'pages'} will be excluded. Generate the PDF?`;
    if (!window.confirm(message)) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const created = await this.api.createExport(this.documentId);
      this.current.set(created);
      this.announcement.set('PDF generation started.');
      if (this.pending(created)) this.schedulePoll(created.id, 3000);
    } catch {
      this.error.set('We could not start the PDF export. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  async download(): Promise<void> {
    const item = this.current();
    if (!item || item.state !== 'Ready') return;
    this.busy.set(true);
    this.error.set('');
    try {
      const blob = await this.api.downloadExport(this.documentId, item.id);
      const url = URL.createObjectURL(blob);
      const anchor = window.document.createElement('a');
      anchor.href = url;
      anchor.download = `${this.safeName(this.documentTitle)}.pdf`;
      anchor.click();
      setTimeout(() => URL.revokeObjectURL(url), 0);
      this.announcement.set('PDF download started.');
    } catch {
      this.error.set('We could not download the PDF. Please try again.');
    } finally {
      this.busy.set(false);
    }
  }

  private schedulePoll(exportId: string, delay: number): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => void this.poll(exportId), delay);
  }

  private async poll(exportId: string): Promise<void> {
    try {
      const updated = await this.api.getExport(this.documentId, exportId);
      if (this.destroyed) return;
      const previous = this.current();
      const changed = updated.state !== previous?.state ||
        updated.searchability !== previous?.searchability ||
        updated.searchablePageCount !== previous?.searchablePageCount;
      this.current.set(updated);
      if (changed) {
        this.announcement.set(updated.state === 'Ready'
          ? this.readyAnnouncement(updated)
          : `PDF export is ${updated.state}.`);
      }
      if (this.pending(updated)) this.schedulePoll(exportId, 3000);
    } catch {
      if (!this.destroyed) this.error.set('We could not refresh the PDF status. Please try again.');
    }
  }

  private pending(item: DocumentExport): boolean {
    return item.state === 'Queued' || item.state === 'Processing';
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['documentId'] || changes['pages']) void this.loadPreview();
  }

  async loadPreview(): Promise<void> {
    if (!this.documentId) return;
    try {
      const preview = await this.api.getExportPreview(this.documentId);
      if (!this.destroyed) this.preview.set(preview);
    } catch {
      if (!this.destroyed) this.preview.set(null);
    }
  }
  private readyAnnouncement(item: DocumentExport): string {
    if (item.searchability === 'ImageOnly') return 'PDF is ready. It is image-only.';
    if (item.searchability === 'Searchable')
      return item.readyPageCount === 1
        ? 'PDF is ready. The page is searchable.'
        : `PDF is ready. All ${item.readyPageCount} pages are searchable.`;
    return `PDF is ready. ${item.searchablePageCount} of ${item.readyPageCount} pages are searchable.`;
  }
  private safeName(value: string): string {
    return (
      value
        .trim()
        .replace(/[\\/:*?"<>|]+/g, '-')
        .replace(/\s+/g, ' ') || 'document'
    );
  }
  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
  }
}
