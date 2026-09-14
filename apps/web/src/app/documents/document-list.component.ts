import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, OnDestroy, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';

export interface DocumentSummary {
  id: string;
  title: string;
  status: string;
  pageCount: number;
  updatedAt: string;
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

  protected startNewScan(): void {
    void this.router.navigate(['/']);
  }
}
