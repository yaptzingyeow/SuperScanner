import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { CreateTextEditRequest, TextEditAccepted, TextEditStatus, TextStyleProposal } from './text-edit.models';

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

  get(documentId: string, pageId: string, editId: string): Promise<TextEditStatus> {
    return firstValueFrom(this.http.get<TextEditStatus>(`${this.url(documentId, pageId)}/${editId}`));
  }
}
