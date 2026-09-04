import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';

export interface DocumentDto {
  id: string;
  title: string;
  status: string;
  pageCount: number;
  updatedAt: string;
}

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
}
