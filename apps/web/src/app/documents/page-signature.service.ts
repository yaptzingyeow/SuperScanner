import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { PageSignatureDto, SignatureBox } from './page-signature.models';

@Injectable({ providedIn: 'root' })
export class PageSignatureService {
  private readonly http = inject(HttpClient);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private route(documentId: string, pageId: string): string {
    return `${this.base}/documents/${encodeURIComponent(documentId)}/pages/${encodeURIComponent(pageId)}/signatures`;
  }
  private canonical(dto: PageSignatureDto, documentId: string, pageId: string): PageSignatureDto {
    return { id: dto.id, pageId, box: dto.box, imageAspectRatio: dto.imageAspectRatio, revision: dto.revision,
      imageUrl: `${this.route(documentId, pageId)}/${encodeURIComponent(dto.id)}/image` };
  }
  /** The server removes the paper from a signature photo and returns a transparent PNG (nothing is saved). */
  async prepare(image: File, strength: number, keepOriginal: boolean): Promise<Blob> {
    const form = new FormData();
    form.append('image', image, image.name);
    form.append('strength', String(strength));
    form.append('keepOriginal', String(keepOriginal));
    return firstValueFrom(this.http.post(`${this.base}/signatures/prepare`, form, { responseType: 'blob' }));
  }

  async list(documentId: string, pageId: string): Promise<PageSignatureDto[]> {
    const dtos = await firstValueFrom(this.http.get<PageSignatureDto[]>(this.route(documentId, pageId)));
    return dtos.map(dto => this.canonical(dto, documentId, pageId));
  }
  async create(documentId: string, pageId: string, png: Blob, box: SignatureBox, idempotencyKey: string): Promise<PageSignatureDto> {
    const form = new FormData(); form.append('image', png, 'signature.png'); form.append('box', JSON.stringify(box));
    const dto = await firstValueFrom(this.http.post<PageSignatureDto>(this.route(documentId, pageId), form,
      { headers: new HttpHeaders({ 'Idempotency-Key': idempotencyKey }) }));
    return this.canonical(dto, documentId, pageId);
  }
  async update(documentId: string, pageId: string, signatureId: string, box: SignatureBox, expectedRevision: number): Promise<PageSignatureDto> {
    const dto = await firstValueFrom(this.http.put<PageSignatureDto>(`${this.route(documentId, pageId)}/${encodeURIComponent(signatureId)}`, { box, expectedRevision }));
    return this.canonical(dto, documentId, pageId);
  }
  async delete(documentId: string, pageId: string, signatureId: string, expectedRevision: number): Promise<void> {
    await firstValueFrom(this.http.delete(`${this.route(documentId, pageId)}/${encodeURIComponent(signatureId)}`, { params: { expectedRevision } }));
  }
  image(documentId: string, pageId: string, signatureId: string): Promise<Blob> {
    return firstValueFrom(this.http.get(`${this.route(documentId, pageId)}/${encodeURIComponent(signatureId)}/image`, { responseType: 'blob' }));
  }
}
