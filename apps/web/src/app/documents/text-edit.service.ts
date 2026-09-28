import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { CreateTextEditRequest, PageEditHistory, TextEditAccepted, TextEditStatus, TextStyleProposal } from './text-edit.models';

@Injectable({ providedIn: 'root' })
export class TextEditService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');

  private url(documentId: string, pageId: string): string {
    return `${this.base}/documents/${documentId}/pages/${pageId}/text-edits`;
  }

  propose(documentId: string, pageId: string, ocrResultId: string,
    wordIds: string[]): Promise<TextStyleProposal> {
    return firstValueFrom(this.http.post<TextStyleProposal>(
      `${this.url(documentId, pageId)}/style-proposal`, { ocrResultId, wordIds }));
  }

  apply(documentId: string, pageId: string,
    request: CreateTextEditRequest): Promise<TextEditAccepted> {
    return firstValueFrom(this.http.post<TextEditAccepted>(this.url(documentId, pageId), request));
  }

  preview(documentId: string, pageId: string,
    request: CreateTextEditRequest): Promise<Blob> {
    return firstValueFrom(this.http.post(`${this.url(documentId, pageId)}/preview`,
      request, { responseType: 'blob' }));
  }

  get(documentId: string, pageId: string, editId: string): Promise<TextEditStatus> {
    return firstValueFrom(this.http.get<TextEditStatus>(`${this.url(documentId, pageId)}/${editId}`));
  }

  history(documentId: string, pageId: string): Promise<PageEditHistory> {
    return firstValueFrom(this.http.get<PageEditHistory>(`${this.url(documentId, pageId)}/history`));
  }

  switchRevision(documentId: string, pageId: string, direction: 'undo' | 'redo',
    expectedRevisionId: string | null): Promise<PageEditHistory> {
    return firstValueFrom(this.http.post<PageEditHistory>(
      `${this.url(documentId, pageId)}/${direction}`, { expectedRevisionId }));
  }
}
