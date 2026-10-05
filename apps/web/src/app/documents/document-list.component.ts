import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';

export interface DocumentSummary {
  id: string;
  title: string;
  status: string;
  pageCount: number;
  updatedAt: string;
  /** When Free-plan retention will delete it; null when the plan keeps documents. */
  expiresAt?: string | null;
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

  protected async deleteDocument(document: DocumentSummary): Promise<void> {
    if (!window.confirm(`Delete “${document.title}”? This cannot be undone from the app.`)) return;
    this.actionError.set('');
    try {
      await firstValueFrom(this.http.delete(`${this.apiBaseUrl}/documents/${document.id}`));
      this.documents.update((all) => all.filter((item) => item.id !== document.id));
    } catch {
      this.actionError.set('That document could not be deleted. Please try again.');
    }
  }

  protected async renameDocument(document: DocumentSummary): Promise<void> {
    const title = window.prompt('Rename document', document.title)?.trim();
    if (!title || title === document.title) return;
    if (title.length > 200) { this.actionError.set('A title can have at most 200 characters.'); return; }
    this.actionError.set('');
    try {
      const renamed = await firstValueFrom(
        this.http.patch<DocumentSummary>(`${this.apiBaseUrl}/documents/${document.id}`, { title }));
      this.documents.update((all) => all.map((item) => item.id === document.id ? { ...item, title: renamed.title } : item));
    } catch {
      this.actionError.set('That document could not be renamed. Please try again.');
    }
  }

  protected startNewScan(): void {
    void this.router.navigate(['/']);
  }
}
