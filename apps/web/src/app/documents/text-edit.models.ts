import { OcrPoint } from './document.models';

export interface TextEditBox { x: number; y: number; width: number; height: number }
export interface FontCandidate { catalogueId: string; version: string; score: number }
export interface TextStyleEstimate {
  candidates: FontCandidate[];
  confidence: number;
  colorHex: string;
  fontSizePoints: number;
  fontWeight: number;
  letterSpacing: number;
  baselineAngleDegrees: number;
  alignment: string;
}
export interface TextStyleProposal {
  activeRevisionId: string | null;
  ocrResultId: string;
  wordIds: string[];
  originalText: string;
  box: TextEditBox;
  style: TextStyleEstimate;
}
export interface TextEditStyle {
  fontId: string;
  fontVersion: string;
  fontSize: number;
  weight: number;
  colorHex: string;
  letterSpacing: number;
  baseline: number;
  angleDegrees: number;
  alignment: number;
}
export interface CreateTextEditRequest {
  ocrResultId: string;
  expectedRevisionId: string | null;
  wordIds: string[];
  replacementText: string;
  replacementBox: TextEditBox;
  style: TextEditStyle;
  idempotencyKey: string;
}
export interface TextEditAccepted { editId: string; state: string; replayed: boolean }
export interface TextEditStatus {
  id: string;
  sourceRevisionId: string;
  state: 'Queued' | 'Processing' | 'Succeeded' | 'Failed';
  failureCode?: string | null;
  resultRevisionId?: string | null;
}
export interface PageEditHistory {
  canUndo: boolean;
  canRedo: boolean;
  activeRevisionId: string | null;
  entries: TextEditStatus[];
}
export interface TextEditSelection {
  pageId: string;
  ocrResultId: string;
  wordIds: string[];
  phrase: string;
  textType: 'Printed';
  polygon: OcrPoint[];
}
