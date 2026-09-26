import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { PageMarkDto, PageMarkDraft } from './page-mark.models';

@Injectable({ providedIn: 'root' })
export class PageMarkService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private route(documentId: string, pageId: string): string {
    return `${this.base}/documents/${encodeURIComponent(documentId)}/pages/${encodeURIComponent(pageId)}/marks`;
  }
  list(documentId: string, pageId: string): Promise<PageMarkDto[]> {
    return firstValueFrom(this.http.get<PageMarkDto[]>(this.route(documentId, pageId)));
  }
  create(documentId: string, pageId: string, draft: PageMarkDraft, idempotencyKey: string): Promise<PageMarkDto> {
    return firstValueFrom(this.http.post<PageMarkDto>(this.route(documentId, pageId),
      { kind: draft.kind, box: draft.box, color: draft.color, strokeWidth: draft.strokeWidth },
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) }));
  }
  update(documentId: string, pageId: string, draft: PageMarkDto): Promise<PageMarkDto> {
    return firstValueFrom(this.http.put<PageMarkDto>(`${this.route(documentId, pageId)}/${encodeURIComponent(draft.id)}`,
      { kind: draft.kind, box: draft.box, color: draft.color, strokeWidth: draft.strokeWidth, expectedRevision: draft.revision }));
  }
  async delete(documentId: string, pageId: string, mark: PageMarkDto): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.route(documentId, pageId)}/${encodeURIComponent(mark.id)}`,
      { params: { expectedRevision: mark.revision } }));
  }
}
