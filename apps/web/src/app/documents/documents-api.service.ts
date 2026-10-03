import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import {
  CropPoint,
  CropState,
  DocumentDetail,
  DocumentDto,
  DocumentExport,
  DocumentExportPreview,
  PageOcr,
  ReorderPagesRequest,
  ScanFilterId,
} from './document.models';
import { ExportWatermarkSettings } from './watermark';

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

  createExport(
    id: string,
    pageLayout: 'Original' | 'A4' = 'Original',
    includeSearchableText = false,
    watermark: ExportWatermarkSettings | null = null,
  ): Promise<DocumentExport> {
    return firstValueFrom(
      this.http.post<DocumentExport>(`${this.baseUrl}/documents/${id}/exports`, {
        pageLayout,
        includeSearchableText,
        ...(watermark ? { watermark } : {}),
      }),
    );
  }

  getExportPreview(id: string): Promise<DocumentExportPreview> {
    return firstValueFrom(
      this.http.get<DocumentExportPreview>(`${this.baseUrl}/documents/${id}/exports/preview`),
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

  downloadPageOriginal(documentId: string, pageId: string): Promise<Blob> {
    return firstValueFrom(
      this.http.get(`${this.baseUrl}/documents/${documentId}/pages/${pageId}/original`, {
        responseType: 'blob',
      }),
    );
  }

  getPageOcr(documentId: string, pageId: string): Promise<PageOcr> {
    return firstValueFrom(
      this.http.get<PageOcr>(`${this.baseUrl}/documents/${documentId}/pages/${pageId}/ocr`),
    );
  }

  requestPageOcr(documentId: string, pageId: string, retryFailed: boolean): Promise<PageOcr> {
    return firstValueFrom(
      this.http.post<PageOcr>(`${this.baseUrl}/documents/${documentId}/pages/${pageId}/ocr`, {
        retryFailed,
      }),
    );
  }

  getCrop(documentId: string, pageId: string): Promise<CropState> {
    return firstValueFrom(
      this.http.get<CropState>(`${this.baseUrl}/documents/${documentId}/pages/${pageId}/crop`),
    );
  }

  applyCrop(
    documentId: string,
    pageId: string,
    body: { revision: number; points: CropPoint[]; filter: ScanFilterId; rotation: number },
  ): Promise<CropState> {
    return firstValueFrom(
      this.http.post<CropState>(
        `${this.baseUrl}/documents/${documentId}/pages/${pageId}/crop/apply`,
        body,
      ),
    );
  }

  getPagePreview(documentId: string, pageId: string): Promise<Blob> {
    return firstValueFrom(
      this.http.get(`${this.baseUrl}/documents/${documentId}/pages/${pageId}/preview`, {
        responseType: 'blob',
      }),
    );
  }
}
