import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { I18nService } from '../core/i18n/i18n.service';

export interface DocumentSummary {
  id: string;
  title: string;
  status: string;
  pageCount: number;
  updatedAt: string;
  /** When Free-plan retention will delete it; null when the plan keeps documents. */
  expiresAt?: string | null;
}

/** A deleted document still restorable from the bin until purgeAfter. */
export interface BinEntry {
  id: string;
  title: string;
  deletedAt: string;
  purgeAfter: string;
}

type LoadState = 'loading' | 'loaded' | 'error';

@Component({
  selector: 'app-document-list',
  imports: [DatePipe, RouterLink],
  templateUrl: './document-list.component.html',
  styleUrl: './document-list.component.scss',
})
export class DocumentListComponent implements OnInit, OnDestroy {
  private refreshTimer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  ngOnDestroy(): void { this.destroyed = true; clearTimeout(this.refreshTimer); }
  private readonly http = inject(HttpClient);
  protected readonly i18n = inject(I18nService);
  private readonly router = inject(Router);
  private readonly apiBaseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

  protected readonly documents = signal<readonly DocumentSummary[]>([]);
  protected readonly loadState = signal<LoadState>('loading');
  /** True when any document will be deleted within a day. */
  protected readonly expiringSoon = computed(() => this.documents().some((d) => {
    const days = this.daysLeft(d);
    return days !== null && days <= 1;
  }));

  protected daysLeft(document: DocumentSummary): number | null {
    if (!document.expiresAt) return null;
    return Math.max(0, Math.ceil((Date.parse(document.expiresAt) - Date.now()) / 86_400_000));
  }

  ngOnInit(): void {
    this.http.get<readonly DocumentSummary[]>(`${this.apiBaseUrl}/documents`).subscribe({
      next: (documents) => {
        if (this.destroyed) return;
        this.documents.set(documents);
        this.loadState.set('loaded');
        if (documents.some(doc => doc.status === 'Processing' || doc.status === 'Uploading'))
          this.refreshTimer = setTimeout(() => this.ngOnInit(), 4000);
      },
      error: () => this.loadState.set('error'),
    });
  }

  protected readonly actionError = signal('');

  /** Card waiting for "Yes, delete" (in-card, because browsers can block confirm()). */
  protected readonly confirmingDelete = signal<string | null>(null);
  /** Card whose title is being edited inline, and the draft title. */
  protected readonly renaming = signal<string | null>(null);
  protected readonly renameDraft = signal('');

  protected askDelete(document: DocumentSummary): void {
    this.renaming.set(null);
    this.confirmingDelete.set(document.id);
  }

  protected async deleteDocument(document: DocumentSummary): Promise<void> {
    this.confirmingDelete.set(null);
    this.actionError.set('');
    try {
      await firstValueFrom(this.http.delete(`${this.apiBaseUrl}/documents/${document.id}`));
      this.documents.update((all) => all.filter((item) => item.id !== document.id));
      if (this.binOpen()) void this.loadBin();
    } catch {
      this.actionError.set(this.i18n.t('list.deleteFailed'));
    }
  }

  /** Recently deleted: loaded only when opened. */
  protected readonly binOpen = signal(false);
  protected readonly bin = signal<readonly BinEntry[] | null>(null);

  protected toggleBin(): void {
    this.binOpen.update((open) => !open);
    if (this.binOpen()) void this.loadBin();
  }

  private async loadBin(): Promise<void> {
    try {
      this.bin.set(await firstValueFrom(this.http.get<readonly BinEntry[]>(`${this.apiBaseUrl}/documents/bin`)));
    } catch {
      this.bin.set([]);
      this.actionError.set(this.i18n.t('list.bin.loadFailed'));
    }
  }

  protected async restore(entry: BinEntry): Promise<void> {
    this.actionError.set('');
    try {
      const restored = await firstValueFrom(
        this.http.post<DocumentSummary>(`${this.apiBaseUrl}/documents/${entry.id}/restore`, null));
      this.bin.update((all) => (all ?? []).filter((item) => item.id !== entry.id));
      this.documents.update((all) => [restored, ...all.filter((item) => item.id !== restored.id)]);
      this.loadState.set('loaded');
    } catch (error) {
      const limit = (error as { status?: number }).status === 429;
      this.actionError.set(this.i18n.t(limit ? 'list.bin.limit' : 'list.bin.restoreFailed'));
    }
  }

  protected startRename(document: DocumentSummary): void {
    this.confirmingDelete.set(null);
    this.renameDraft.set(document.title);
    this.renaming.set(document.id);
  }

  protected renameKey(event: KeyboardEvent, document: DocumentSummary): void {
    if (event.key === 'Enter') { event.preventDefault(); void this.saveRename(document); }
    if (event.key === 'Escape') { event.preventDefault(); this.renaming.set(null); }
  }

  protected async saveRename(document: DocumentSummary): Promise<void> {
    const title = this.renameDraft().trim();
    if (!title || title === document.title) { this.renaming.set(null); return; }
    if (title.length > 200) { this.actionError.set(this.i18n.t('list.titleTooLong')); return; }
    this.actionError.set('');
    try {
      const renamed = await firstValueFrom(
        this.http.patch<DocumentSummary>(`${this.apiBaseUrl}/documents/${document.id}`, { title }));
      this.documents.update((all) => all.map((item) => item.id === document.id ? { ...item, title: renamed.title } : item));
      this.renaming.set(null);
    } catch {
      this.actionError.set(this.i18n.t('list.renameFailed'));
    }
  }

  protected startNewScan(): void {
    void this.router.navigate(['/']);
  }
}
