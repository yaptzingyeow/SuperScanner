import { HttpClient, HttpEventType, HttpHeaders } from '@angular/common/http';
import { Inject, Injectable, InjectionToken, signal } from '@angular/core';
import { lastValueFrom, tap } from 'rxjs';
import {
  CreateUploadIntentRequest,
  DocumentsApiService,
  UploadState,
} from './documents-api.service';

const MAX_FILE_SIZE_BYTES = 25 * 1024 * 1024;
const SUPPORTED_MEDIA_TYPES = new Set(['application/pdf', 'image/jpeg', 'image/png', 'image/heic']);

export type UploadStage =
  'idle' | 'preparing' | 'uploading' | 'validating' | 'accepted' | 'rejected' | 'error';

export interface UploadProgress {
  stage: UploadStage;
  percent: number;
  errorCode?: string;
}

export interface UploadResult {
  documentId: string;
  uploadId: string;
  state: UploadState;
}

export class UploadFlowError extends Error {
  constructor(readonly code: string) {
    super(code);
    this.name = 'UploadFlowError';
  }
}

export interface SignedUploadClient {
  put(
    url: string,
    file: File,
    mediaType: string,
    onProgress: (percent: number) => void,
  ): Promise<void>;
}

export const SIGNED_UPLOAD_CLIENT = new InjectionToken<SignedUploadClient>('SIGNED_UPLOAD_CLIENT');

@Injectable({ providedIn: 'root' })
export class HttpSignedUploadClient implements SignedUploadClient {
  constructor(private readonly http: HttpClient) {}

  async put(
    url: string,
    file: File,
    mediaType: string,
    onProgress: (percent: number) => void,
  ): Promise<void> {
    await lastValueFrom(
      this.http
        .request('PUT', url, {
          body: file,
          headers: new HttpHeaders({ 'Content-Type': mediaType }),
          observe: 'events',
          reportProgress: true,
        })
        .pipe(
          tap((event) => {
            if (event.type === HttpEventType.UploadProgress) {
              const total = event.total ?? file.size;
              onProgress(total > 0 ? Math.round((event.loaded / total) * 100) : 0);
            }
          }),
        ),
    );
  }
}

@Injectable({ providedIn: 'root' })
export class UploadService {
  private active = false;
  readonly progress = signal<UploadProgress>({ stage: 'idle', percent: 0 });

  constructor(
    private readonly api: DocumentsApiService,
    @Inject(SIGNED_UPLOAD_CLIENT) private readonly uploader: SignedUploadClient,
  ) {}

  async upload(title: string, file: File): Promise<UploadResult> {
    if (this.active) {
      throw new UploadFlowError('upload_active');
    }

    this.validate(file);
    this.active = true;
    this.progress.set({ stage: 'preparing', percent: 0 });

    try {
      const sha256Hex = await this.sha256(file);
      const document = await this.api.createDocument(title.trim());
      const request: CreateUploadIntentRequest = {
        fileName: file.name,
        mediaType: file.type,
        sizeBytes: file.size,
        sha256Hex,
      };
      const intent = await this.api.createUploadIntent(document.id, request);
      if (Date.parse(intent.expiresAt) <= Date.now()) {
        throw new UploadFlowError('intent_expired');
      }

      this.progress.set({ stage: 'uploading', percent: 0 });
      try {
        await this.uploader.put(intent.putUrl, file, file.type, (percent) =>
          this.progress.set({ stage: 'uploading', percent }),
        );
      } catch {
        throw new UploadFlowError('upload_failed');
      }

      const completed = await this.api.completeUpload(document.id, intent.uploadId);
      const stage = completed.state === 'Accepted' ? 'accepted' : 'validating';
      this.progress.set({ stage, percent: 100 });
      return { documentId: document.id, uploadId: intent.uploadId, state: completed.state };
    } catch (error) {
      const safeError =
        error instanceof UploadFlowError ? error : new UploadFlowError('request_failed');
      this.progress.set({ stage: 'error', percent: 0, errorCode: safeError.code });
      throw safeError;
    } finally {
      this.active = false;
    }
  }

  private validate(file: File): void {
    if (!SUPPORTED_MEDIA_TYPES.has(file.type)) {
      throw new UploadFlowError('unsupported_type');
    }
    if (file.size <= 0) {
      throw new UploadFlowError('empty_file');
    }
    if (file.size > MAX_FILE_SIZE_BYTES) {
      throw new UploadFlowError('file_too_large');
    }
  }

  private async sha256(file: File): Promise<string> {
    const digest = await crypto.subtle.digest('SHA-256', await file.arrayBuffer());
    return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, '0')).join(
      '',
    );
  }
}
