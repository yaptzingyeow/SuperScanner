import { I18nService } from '../core/i18n/i18n.service';
import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import {
  Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject, signal,
} from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { AddPagesDialogComponent } from './add-pages-dialog.component';
import { DocumentDetail, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';

@Component({
  selector: 'app-page-rail',
  standalone: true,
  imports: [DragDropModule, AddPagesDialogComponent],
  templateUrl: './page-rail.component.html',
  styleUrl: './page-rail.component.scss',
})
export class PageRailComponent implements OnChanges, OnDestroy {
  private readonly http = inject(HttpClient);
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DocumentsApiService);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  @Input({ required: true }) documentId = '';
  @Input() pages: DocumentPage[] = [];
  @Input() selectedPageId: string | null = null;
  @Input() pageOrderRevision = 0;
  @Output() readonly selectPage = new EventEmitter<string>();
  @Output() readonly pagesChanged = new EventEmitter<DocumentDetail>();
  @Output() readonly pagesAdded = new EventEmitter<string[]>();

  protected readonly ordered = signal<DocumentPage[]>([]);
  protected readonly thumbnails = signal<Record<string, string>>({});
  protected readonly busy = signal(false);
  protected readonly showAdd = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');
  private readonly loaded: Record<string, string> = {};
  private destroyed = false;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['pages']) {
      this.ordered.set([...this.pages].sort((a, b) => a.position - b.position));
      void this.loadThumbnails();
    }
  }

  protected async drop(event: CdkDragDrop<DocumentPage[]>): Promise<void> {
    if (event.previousIndex === event.currentIndex) return;
    const list = [...this.ordered()];
    moveItemInArray(list, event.previousIndex, event.currentIndex);
    await this.persist(list);
  }

  protected async move(pageId: string, offset: -1 | 1): Promise<void> {
    const list = [...this.ordered()];
    const from = list.findIndex((p) => p.id === pageId);
    const to = from + offset;
    if (from < 0 || to < 0 || to >= list.length) return;
    moveItemInArray(list, from, to);
    await this.persist(list);
  }

  protected async remove(page: DocumentPage): Promise<void> {
    if (this.busy()) return;
    if (!window.confirm(this.i18n.t('pages.rail.confirm', { n: page.position }))) return;
    this.error.set('');
    try {
      await this.api.removePage(this.documentId, page.id);
      this.pagesChanged.emit(await this.api.getDocument(this.documentId));
      this.message.set(this.i18n.t('pages.rail.removed', { n: page.position }));
    } catch {
      this.error.set(this.i18n.t('pages.rail.err.remove'));
    }
  }

  protected onAdded(uploadIds: string[]): void {
    this.showAdd.set(false);
    this.message.set(this.i18n.t('pages.rail.added'));
    this.pagesAdded.emit(uploadIds);
  }

  private async persist(list: DocumentPage[]): Promise<void> {
    if (this.busy()) return;
    const previous = this.ordered();
    this.ordered.set(list.map((p, i) => ({ ...p, position: i + 1, pageNumber: i + 1 })));
    this.busy.set(true);
    this.error.set('');
    try {
      const updated = await this.api.reorderPages(this.documentId, {
        expectedPageOrderRevision: this.pageOrderRevision,
        pageIds: list.map((p) => p.id),
      });
      this.pagesChanged.emit(updated);
      this.message.set(this.i18n.t('pages.rail.saved'));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        try {
          this.pagesChanged.emit(await this.api.getDocument(this.documentId));
        } catch {
          this.ordered.set(previous);
        }
        this.message.set(this.i18n.t('pages.rail.changed'));
      } else {
        this.ordered.set(previous);
        this.error.set(this.i18n.t('pages.rail.err.save'));
      }
    } finally {
      this.busy.set(false);
    }
  }

  private async loadThumbnails(): Promise<void> {
    const active = new Set(this.pages.map((p) => p.id));
    for (const [id, url] of Object.entries(this.thumbnails())) {
      if (!active.has(id)) {
        URL.revokeObjectURL(url);
        delete this.loaded[id];
        this.thumbnails.update((c) => {
          const next = { ...c };
          delete next[id];
          return next;
        });
      }
    }
    for (const page of this.pages.filter(
      (p) => p.hasPreview && (!this.thumbnails()[p.id] || this.loaded[p.id] !== p.previewRevision),
    )) {
      try {
        const blob = await firstValueFrom(
          this.http.get(`${this.base}/documents/${this.documentId}/pages/${page.id}/thumbnail`, {
            responseType: 'blob',
          }),
        );
        if (this.destroyed) return;
        const old = this.thumbnails()[page.id];
        if (old) URL.revokeObjectURL(old);
        this.loaded[page.id] = page.previewRevision;
        this.thumbnails.update((c) => ({ ...c, [page.id]: URL.createObjectURL(blob) }));
      } catch {
        // keep the placeholder when a thumbnail is unavailable
      }
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    Object.values(this.thumbnails()).forEach((url) => URL.revokeObjectURL(url));
  }
}
