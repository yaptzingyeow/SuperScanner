import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, OnInit, output, signal } from '@angular/core';
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
  imports: [DatePipe],
  templateUrl: './document-list.component.html',
  styleUrl: './document-list.component.scss',
})
export class DocumentListComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly apiBaseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

  protected readonly documents = signal<readonly DocumentSummary[]>([]);
  protected readonly loadState = signal<LoadState>('loading');
  readonly newScan = output<void>();

  ngOnInit(): void {
    this.http.get<readonly DocumentSummary[]>(`${this.apiBaseUrl}/documents`).subscribe({
      next: (documents) => {
        this.documents.set(documents);
        this.loadState.set('loaded');
      },
      error: () => this.loadState.set('error'),
    });
  }
}
