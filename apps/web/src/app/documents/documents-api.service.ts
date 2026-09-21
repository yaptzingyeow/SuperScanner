import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import {
  DocumentDetail,
  DocumentDto,
  DocumentExport,
  ReorderPagesRequest,
} from './document.models';

export type { DocumentDto } from './document.models';

export interface CreateUploadIntentRequest {
  fileName: string;
  mediaType: string;
  sizeBytes: number;
  sha256Hex: string;
}

export interface UploadIntentDto {
  uploadId: string;
  pageId: string;
  putUrl: string;
  expiresAt: string;
}

export type UploadState = 'AwaitingUpload' | 'PendingValidation' | 'Accepted' | 'Rejected';

export interface UploadStatusDto {
  uploadId: string;
  state: UploadState;
  discoveredPageCount: number;
  createdPageCount: number;
  failedPageCount: number;
  errorCode?: string | null;
}

@Injectable({ providedIn: 'root' })
export class DocumentsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

  createDocument(title: string): Promise<DocumentDto> {
    return firstValueFrom(this.http.post<DocumentDto>(`${this.baseUrl}/documents`, { title }));
  }

  createUploadIntent(
    documentId: string,
    request: CreateUploadIntentRequest,
  ): Promise<UploadIntentDto> {
    return firstValueFrom(
      this.http.post<UploadIntentDto>(`${this.baseUrl}/documents/${documentId}/uploads`, request),
    );
  }

  completeUpload(documentId: string, uploadId: string): Promise<UploadStatusDto> {
    return firstValueFrom(
      this.http.post<UploadStatusDto>(
        `${this.baseUrl}/documents/${documentId}/uploads/${uploadId}/complete`,
        null,
      ),
    );
  }

  getUploadStatus(documentId: string, uploadId: string): Promise<UploadStatusDto> {
    return firstValueFrom(
      this.http.get<UploadStatusDto>(`${this.baseUrl}/documents/${documentId}/uploads/${uploadId}`),
    );
  }

  getDocument(id: string): Promise<DocumentDetail> {
    return firstValueFrom(this.http.get<DocumentDetail>(`${this.baseUrl}/documents/${id}`));
  }

  async reorderPages(id: string, request: ReorderPagesRequest): Promise<DocumentDetail> {
    await firstValueFrom(this.http.put(`${this.baseUrl}/documents/${id}/page-order`, request));
    return this.getDocument(id);
  }

  async removePage(id: string, pageId: string): Promise<void> {
    await firstValueFrom(this.http.delete<void>(`${this.baseUrl}/documents/${id}/pages/${pageId}`));
  }

  createExport(id: string): Promise<DocumentExport> {
    return firstValueFrom(
      this.http.post<DocumentExport>(`${this.baseUrl}/documents/${id}/exports`, null),
    );
  }

  getExport(id: string, exportId: string): Promise<DocumentExport> {
    return firstValueFrom(
      this.http.get<DocumentExport>(`${this.baseUrl}/documents/${id}/exports/${exportId}`),
    );
  }

  downloadExport(id: string, exportId: string): Promise<Blob> {
    return firstValueFrom(
      this.http.get(`${this.baseUrl}/documents/${id}/exports/${exportId}/download`, {
        responseType: 'blob',
      }),
    );
  }
}
