export interface DocumentDto {
  id: string;
  title: string;
  status: string;
  pageCount: number;
  updatedAt: string;
}

export interface DocumentPage {
  id: string;
  position: number;
  pageNumber: number;
  sourceUploadId: string;
  sourcePageIndex: number;
  state: string;
  failureCode?: string | null;
  hasPreview: boolean;
  hasOriginal: boolean;
  canCrop: boolean;
  cropStatus: string;
  cropRevision: number;
  appliedCropRevision: number;
  previewRevision: number;
  filter?: string | null;
  appliedFilter?: string | null;
}

export interface DocumentImport {
  uploadId: string;
  fileName: string;
  mediaType: string;
  state: string;
  discoveredPageCount: number;
  createdPageCount: number;
  failedPageCount: number;
  errorCode?: string | null;
}

export interface DocumentExport {
  id: string;
  state: string;
  documentRevision: number;
  readyPageCount: number;
  excludedPageCount: number;
  failureCode?: string | null;
  createdAt: string;
  completedAt?: string | null;
  expiresAt: string;
  isOutdated: boolean;
  statusUrl?: string | null;
  downloadUrl?: string | null;
}

export interface DocumentDetail {
  id: string;
  title: string;
  status: string;
  message?: string | null;
  revision: number;
  pageOrderRevision: number;
  pages: DocumentPage[];
  imports: DocumentImport[];
  latestExport?: DocumentExport | null;
}

export interface ReorderPagesRequest {
  expectedPageOrderRevision: number;
  pageIds: string[];
}

export type OcrState = 'NotRequested' | 'Queued' | 'Processing' | 'Ready' | 'Failed';

export interface OcrPoint {
  x: number;
  y: number;
}

export interface OcrElement {
  id: string;
  kind: 'Block' | 'Line' | 'Word';
  text: string;
  confidence: number;
  textType: 'Printed' | 'Handwritten' | 'Unknown';
  readingOrder: number;
  polygon: OcrPoint[];
  children: OcrElement[];
}

export interface PageOcr {
  resultId?: string | null;
  state: OcrState;
  sourceFingerprint?: string | null;
  fullText?: string | null;
  aggregateConfidence?: number | null;
  elementCount: number;
  failureCode?: string | null;
  canRetry: boolean;
  queuedAt?: string | null;
  startedAt?: string | null;
  completedAt?: string | null;
  elements: OcrElement[];
}

export type UploadStage =
  | 'idle'
  | 'preparing'
  | 'uploading'
  | 'validating'
  | 'expanding'
  | 'accepted'
  | 'rejected'
  | 'failed'
  | 'error';

export interface UploadItemProgress {
  fileName: string;
  stage: UploadStage;
  percent: number;
  uploadId?: string;
  state?: string;
  discoveredPageCount?: number;
  createdPageCount?: number;
  failedPageCount?: number;
  errorCode?: string;
}
