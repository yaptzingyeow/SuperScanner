import { Component, EventEmitter, Input, OnChanges, OnDestroy, OnInit, Output, SimpleChanges, inject, signal } from '@angular/core';
import { DocumentExport, DocumentExportPreview, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { sameWatermark } from './watermark';
import { WatermarkPanelComponent } from './watermark-panel.component';
import { WatermarkStore } from './watermark.store';
import { PlanService } from '../plans/plan.service';
import { UsageLineComponent } from '../plans/usage-line.component';
import { errorMessage, watermarksUsedUp } from '../plans/limit-message';
import { I18nService } from '../core/i18n/i18n.service';

@Component({
  selector: 'app-export-status',
  standalone: true,
  imports: [WatermarkPanelComponent, UsageLineComponent],
  templateUrl: './export-status.component.html',
  styleUrl: './export-status.component.scss',
})
export class ExportStatusComponent implements OnInit, OnChanges, OnDestroy {
  private readonly api = inject(DocumentsApiService);
  protected readonly i18n = inject(I18nService);
  private readonly watermarks = inject(WatermarkStore);
  protected readonly plans = inject(PlanService);
  @Input({ required: true }) documentId = '';
  @Input({ required: true }) documentTitle = 'document';
  @Input() pages: DocumentPage[] = [];
  @Input() initialExport?: DocumentExport | null;
  @Output() readonly exportChange = new EventEmitter<DocumentExport>();
  protected readonly current = signal<DocumentExport | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly announcement = signal('');
  protected readonly preview = signal<DocumentExportPreview | null>(null);
  protected readonly pageLayout = signal<'Original' | 'A4'>('Original');
  protected readonly includeSearchableText = signal(false);
  protected setPageLayout(event: Event): void {
    this.pageLayout.set((event.target as HTMLSelectElement).value as 'Original' | 'A4');
  }
  protected setIncludeSearchableText(event: Event): void {
    this.includeSearchableText.set((event.target as HTMLInputElement).checked);
  }
  protected needsNewLayout(item: DocumentExport): boolean {
    return item.state === 'Ready' && item.pageLayout !== this.pageLayout();
  }
  /** The ready PDF was made with a different watermark (or none) than the one chosen now. */
  protected needsNewWatermark(item: DocumentExport): boolean {
    return item.state === 'Ready' && !sameWatermark(item.watermark, this.watermarks.active());
  }
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;

  protected get readyCount(): number {
    return this.pages.filter((page) => page.state === 'Ready').length;
  }
  protected get excludedCount(): number {
    return this.pages.length - this.readyCount;
  }

  protected stateLabel(state: string): string {
    const key = 'ws.state.' + state;
    const text = this.i18n.t(key);
    return text === key ? state : text;
  }

  protected searchabilityCopy(item: DocumentExport): string {
    switch (item.searchability) {
      case 'Searchable':
        return item.readyPageCount === 1
          ? this.i18n.t('ws.exp.searchOne')
          : this.i18n.t('ws.exp.searchAll', { count: item.readyPageCount });
      case 'PartiallySearchable':
        return this.i18n.t('ws.exp.searchPartial', { searchable: item.searchablePageCount, ready: item.readyPageCount });
      default:
        return this.i18n.t('ws.exp.imageOnly');
    }
  }

  ngOnInit(): void {
    void this.plans.refresh();
    this.current.set(this.initialExport ?? null);
    if (this.initialExport) this.pageLayout.set(this.initialExport.pageLayout);
    if (this.initialExport && this.pending(this.initialExport))
      this.schedulePoll(this.initialExport.id, 0);
  }

  async generate(): Promise<void> {
    if (!this.readyCount || this.busy()) return;
    const t = (key: string) => this.i18n.t(key);
    const message = `${this.readyCount} ${this.readyCount === 1 ? t('ws.exp.readyOne') : t('ws.exp.readyMany')} ${this.excludedCount} ${this.excludedCount === 1 ? t('ws.exp.exclOne') : t('ws.exp.exclMany')} ${t('ws.exp.confirm')}`;
    if (!window.confirm(message)) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const created = await this.api.createExport(
        this.documentId,
        this.pageLayout(),
        this.includeSearchableText(),
        watermarksUsedUp(this.plans.watermarks()) ? null : this.watermarks.active(),
      );
      this.current.set(created);
      this.exportChange.emit(created);
      this.announcement.set(this.i18n.t('ws.exp.started'));
      if (this.pending(created)) this.schedulePoll(created.id, 3000);
    } catch (error) {
      this.error.set(errorMessage(error, this.i18n.t('ws.exp.startFailed'), (key, params) => this.i18n.t(key, params)));
    } finally {
      this.busy.set(false);
      void this.plans.refresh();
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
      this.announcement.set(this.i18n.t('ws.exp.downloadStarted'));
    } catch {
      this.error.set(this.i18n.t('ws.exp.downloadFailed'));
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
      this.exportChange.emit(updated);
      if (changed) {
        this.announcement.set(updated.state === 'Ready'
          ? this.readyAnnouncement(updated)
          : this.i18n.t('ws.exp.exportIs', { state: this.stateLabel(updated.state) }));
      }
      if (this.pending(updated)) this.schedulePoll(exportId, 3000);
    } catch {
      if (!this.destroyed) this.error.set(this.i18n.t('ws.exp.statusFailed'));
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
    if (item.searchability === 'ImageOnly') return this.i18n.t('ws.exp.readyImageOnly');
    if (item.searchability === 'Searchable')
      return item.readyPageCount === 1
        ? this.i18n.t('ws.exp.readyOnePage')
        : this.i18n.t('ws.exp.readyAll', { count: item.readyPageCount });
    return this.i18n.t('ws.exp.readyPartial', { searchable: item.searchablePageCount, ready: item.readyPageCount });
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
