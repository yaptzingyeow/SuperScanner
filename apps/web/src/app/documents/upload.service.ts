import { HttpClient, HttpEventType, HttpHeaders } from '@angular/common/http';
import { Inject, Injectable, InjectionToken, Optional, signal } from '@angular/core';
import { lastValueFrom, tap } from 'rxjs';
import {
  CreateUploadIntentRequest,
  DocumentsApiService,
  UploadState,
} from './documents-api.service';
import { UploadItemProgress, UploadStage } from './document.models';

const MAX_FILE_SIZE_BYTES = 25 * 1024 * 1024;
const SUPPORTED_MEDIA_TYPES = new Set(['application/pdf', 'image/jpeg', 'image/png', 'image/heic']);

export type { UploadItemProgress, UploadStage } from './document.models';

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
export const UPLOAD_POLL_DELAY_MS = new InjectionToken<number>('UPLOAD_POLL_DELAY_MS');

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
  readonly items = signal<UploadItemProgress[]>([]);

  constructor(
    private readonly api: DocumentsApiService,
    @Inject(SIGNED_UPLOAD_CLIENT) private readonly uploader: SignedUploadClient,
    @Optional() @Inject(UPLOAD_POLL_DELAY_MS) private readonly pollDelayMs: number = 1000,
  ) {}

  async upload(title: string, file: File): Promise<UploadResult> {
    if (this.active) {
      throw new UploadFlowError('upload_active');
    }

    this.active = true;
    this.progress.set({ stage: 'preparing', percent: 0 });

    try {
      this.validate(file);
      const document = await this.api.createDocument(title.trim());
      const [item] = await this.addFilesInternal(document.id, [file]);
      if (item.stage === 'error' || !item.uploadId) {
        throw new UploadFlowError(item.errorCode ?? 'request_failed');
      }
      const state: UploadState =
        item.stage === 'rejected' || item.stage === 'failed' ? 'Rejected' : 'Accepted';
      this.progress.set({ stage: item.stage, percent: item.percent, errorCode: item.errorCode });
      return { documentId: document.id, uploadId: item.uploadId, state };
    } catch (error) {
      const safeError =
        error instanceof UploadFlowError ? error : new UploadFlowError('request_failed');
      this.progress.set({ stage: 'error', percent: 0, errorCode: safeError.code });
      throw safeError;
    } finally {
      this.active = false;
    }
  }

  async addFiles(documentId: string, files: readonly File[]): Promise<UploadItemProgress[]> {
    if (this.active) throw new UploadFlowError('upload_active');
    this.active = true;
    try {
      return await this.addFilesInternal(documentId, files);
    } finally {
      this.active = false;
    }
  }

  private async addFilesInternal(
    documentId: string,
    files: readonly File[],
  ): Promise<UploadItemProgress[]> {
    const initial = files.map((file) => ({
      fileName: file.name,
      stage: 'idle' as const,
      percent: 0,
    }));
    this.items.set(initial);

    for (let index = 0; index < files.length; index++) {
      const file = files[index];
      try {
        this.validate(file);
        this.updateItem(index, { stage: 'preparing', percent: 0 });
        const item = await this.uploadOne(documentId, file, index);
        this.updateItem(index, item);
      } catch (error) {
        const safe =
          error instanceof UploadFlowError ? error : new UploadFlowError('request_failed');
        this.updateItem(index, { stage: 'error', percent: 0, errorCode: safe.code });
      }
    }
    return this.items();
  }

  private async uploadOne(
    documentId: string,
    file: File,
    itemIndex: number,
  ): Promise<Partial<UploadItemProgress>> {
    const request: CreateUploadIntentRequest = {
      fileName: file.name,
      mediaType: file.type,
      sizeBytes: file.size,
      sha256Hex: await this.sha256(file),
    };
    const intent = await this.api.createUploadIntent(documentId, request);
    if (Date.parse(intent.expiresAt) <= Date.now()) throw new UploadFlowError('intent_expired');

    this.updateItem(itemIndex, { stage: 'uploading', percent: 0, uploadId: intent.uploadId });
    try {
      await this.uploader.put(intent.putUrl, file, file.type, (percent) =>
        this.updateItem(itemIndex, { stage: 'uploading', percent }),
      );
    } catch {
      throw new UploadFlowError('upload_failed');
    }

    await this.api.completeUpload(documentId, intent.uploadId);
    this.updateItem(itemIndex, { stage: 'validating', percent: 100 });
    return this.pollExpansion(documentId, intent.uploadId);
  }

  private async pollExpansion(
    documentId: string,
    uploadId: string,
  ): Promise<Partial<UploadItemProgress>> {
    for (;;) {
      const status = await this.api.getUploadStatus(documentId, uploadId);
      const accounted = status.createdPageCount + status.failedPageCount;
      const progress = {
        uploadId,
        state: status.state,
        discoveredPageCount: status.discoveredPageCount,
        createdPageCount: status.createdPageCount,
        failedPageCount: status.failedPageCount,
        errorCode: status.errorCode ?? undefined,
      };
      if (status.state === 'Rejected') return { ...progress, stage: 'rejected', percent: 100 };
      if (status.errorCode) return { ...progress, stage: 'failed', percent: 100 };
      if (
        status.state === 'Accepted' &&
        status.discoveredPageCount > 0 &&
        accounted === status.discoveredPageCount
      ) {
        return { ...progress, stage: 'accepted', percent: 100 };
      }
      this.updateMatchingItem(uploadId, { ...progress, stage: 'expanding', percent: 100 });
      if (this.pollDelayMs > 0)
        await new Promise((resolve) => setTimeout(resolve, this.pollDelayMs));
    }
  }

  private updateItem(index: number, patch: Partial<UploadItemProgress>): void {
    this.items.update((items) =>
      items.map((item, current) => (current === index ? { ...item, ...patch } : item)),
    );
    if (this.items().length === 1) {
      const item = this.items()[0];
      this.progress.set({ stage: item.stage, percent: item.percent, errorCode: item.errorCode });
    }
  }

  private updateMatchingItem(uploadId: string, patch: Partial<UploadItemProgress>): void {
    this.items.update((items) =>
      items.map((item) => (item.uploadId === uploadId ? { ...item, ...patch } : item)),
    );
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
