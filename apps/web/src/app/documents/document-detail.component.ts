import { CdkDragDrop, DragDropModule, moveItemInArray } from '@angular/cdk/drag-drop';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { AddPagesDialogComponent } from './add-pages-dialog.component';
import { DocumentDetail, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { ExportStatusComponent } from './export-status.component';
import { PageCardComponent } from './page-card.component';
import { OcrStatusComponent } from './ocr-status.component';

@Component({
  selector: 'app-document-detail',
  standalone: true,
  imports: [
    RouterLink,
    DragDropModule,
    PageCardComponent,
    AddPagesDialogComponent,
    ExportStatusComponent,
    OcrStatusComponent,
  ],
  templateUrl: './document-detail.component.html',
  styleUrl: './document-detail.component.scss',
})
export class DocumentDetailComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly documentsApi = inject(DocumentsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  protected readonly id = this.route.snapshot.paramMap.get('documentId') ?? '';
  protected readonly document = signal<DocumentDetail | null>(null);
  protected readonly images = signal<Record<string, string>>({});
  protected readonly error = signal('');
  protected readonly announcement = signal('');
  protected readonly retrying = signal(false);
  protected readonly reordering = signal(false);
  protected readonly showAddPages = signal(false);
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private readonly loadedRevisions: Record<string, number> = {};

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    clearTimeout(this.timer);
    this.error.set('');
    try {
      const doc = await this.documentsApi.getDocument(this.id);
      if (this.destroyed) return;
      this.document.set(this.withPositions(doc));
      await this.loadPreviews(doc);
      if (!this.destroyed && !['Ready', 'Failed', 'NeedsCrop'].includes(doc.status)) {
        this.timer = setTimeout(() => void this.load(), 3000);
      }
    } catch {
      if (!this.destroyed) this.error.set('We could not load this document. Please try again.');
    }
  }

  async drop(event: CdkDragDrop<DocumentPage[]>): Promise<void> {
    if (event.previousIndex === event.currentIndex) return;
    const pages = [...(this.document()?.pages ?? [])];
    moveItemInArray(pages, event.previousIndex, event.currentIndex);
    await this.persistOrder(pages);
  }

  async move(pageId: string, offset: -1 | 1): Promise<void> {
    const pages = [...(this.document()?.pages ?? [])];
    const from = pages.findIndex((page) => page.id === pageId);
    const to = from + offset;
    if (from < 0 || to < 0 || to >= pages.length) return;
    moveItemInArray(pages, from, to);
    await this.persistOrder(pages);
  }

  async remove(page: DocumentPage): Promise<void> {
    if (!window.confirm(`Remove page ${page.position} from this document?`)) return;
    try {
      await this.documentsApi.removePage(this.id, page.id);
      await this.load();
      this.announcement.set(`Page ${page.position} was removed.`);
    } catch {
      this.error.set('We could not remove this page. Please try again.');
    }
  }

  protected async pagesAdded(): Promise<void> {
    this.showAddPages.set(false);
    await this.load();
    this.announcement.set('New pages were added to the document.');
  }

  protected closeAddPages(): void {
    this.showAddPages.set(false);
    void this.load();
  }

  private async persistOrder(pages: DocumentPage[]): Promise<void> {
    const doc = this.document();
    if (!doc || this.reordering()) return;
    const optimistic = pages.map((page, index) => ({
      ...page,
      position: index + 1,
      pageNumber: index + 1,
    }));
    this.document.set({ ...doc, pages: optimistic });
    this.reordering.set(true);
    try {
      const updated = await this.documentsApi.reorderPages(this.id, {
        expectedPageOrderRevision: doc.pageOrderRevision,
        pageIds: optimistic.map((page) => page.id),
      });
      this.document.set(this.withPositions(updated));
      this.announcement.set('Page order saved.');
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 409) {
        await this.load();
        this.announcement.set(
          'Page order changed in another session. The latest order has been restored.',
        );
      } else {
        this.document.set(doc);
        this.error.set('We could not save the page order. Please try again.');
      }
    } finally {
      this.reordering.set(false);
    }
  }

  private withPositions(doc: DocumentDetail): DocumentDetail {
    return {
      ...doc,
      pages: [...doc.pages]
        .sort((a, b) => a.position - b.position)
        .map((page, index) => ({ ...page, position: index + 1, pageNumber: index + 1 })),
    };
  }

  private async loadPreviews(doc: DocumentDetail): Promise<void> {
    const activeIds = new Set(doc.pages.map((page) => page.id));
    for (const [pageId, url] of Object.entries(this.images())) {
      if (!activeIds.has(pageId)) {
        URL.revokeObjectURL(url);
        delete this.loadedRevisions[pageId];
        this.images.update((current) => {
          const next = { ...current };
          delete next[pageId];
          return next;
        });
      }
    }
    for (const page of doc.pages.filter(
      (candidate) =>
        candidate.hasPreview &&
        (!this.images()[candidate.id] ||
          this.loadedRevisions[candidate.id] !== candidate.previewRevision),
    )) {
      const blob = await firstValueFrom(
        this.http.get(`${this.base}/documents/${this.id}/pages/${page.id}/preview`, {
          responseType: 'blob',
        }),
      );
      if (this.destroyed) return;
      if (this.images()[page.id]) URL.revokeObjectURL(this.images()[page.id]);
      this.loadedRevisions[page.id] = page.previewRevision;
      this.images.update((current) => ({ ...current, [page.id]: URL.createObjectURL(blob) }));
    }
  }

  async retry(): Promise<void> {
    this.retrying.set(true);
    try {
      await firstValueFrom(this.http.post(`${this.base}/documents/${this.id}/retry-preview`, {}));
      await this.load();
    } catch {
      this.error.set('Could not restart processing. Please try again.');
    } finally {
      this.retrying.set(false);
    }
  }

  async download(pageId: string): Promise<void> {
    try {
      const blob = await firstValueFrom(
        this.http.get(`${this.base}/documents/${this.id}/pages/${pageId}/original`, {
          responseType: 'blob',
        }),
      );
      const url = URL.createObjectURL(blob);
      const extension =
        (
          {
            'application/pdf': '.pdf',
            'image/png': '.png',
            'image/jpeg': '.jpg',
            'image/heic': '.heic',
          } as Record<string, string>
        )[blob.type] ?? '.bin';
      const link = window.document.createElement('a');
      link.href = url;
      link.download = 'original' + extension;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch {
      this.error.set('Could not download the original. Please try again.');
    }
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
    Object.values(this.images()).forEach((url) => URL.revokeObjectURL(url));
  }
}
