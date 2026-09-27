import { SignatureBox } from './page-signature.models';

export type PageMarkKind = 'Check' | 'Cross';
export interface PageMarkDto {
  id: string;
  pageId: string;
  kind: PageMarkKind;
  box: SignatureBox;
  color: string;
  strokeWidth: number;
  revision: number;
  isDeleted?: boolean;
}
export type PageMarkDraft = Omit<PageMarkDto, 'revision' | 'pageId'> & { revision?: number };

const clamp = (value: number, min: number, max: number): number => Math.max(min, Math.min(max, value));

export function markBoxAt(x: number, y: number, imageWidth: number, imageHeight: number, scale = 1): SignatureBox {
  if (![x, y, imageWidth, imageHeight, scale].every(Number.isFinite) || imageWidth <= 0 || imageHeight <= 0 || scale < .5 || scale > 3)
    throw new Error('Invalid mark placement');
  const width = .025 * scale;
  const height = width * imageWidth / imageHeight;
  if (height > 1) throw new Error('Mark does not fit on this page');
  return { x: clamp(x - width / 2, 0, 1 - width), y: clamp(y - height / 2, 0, 1 - height), width, height };
}

export function markSizeBox(box: SignatureBox, scale: number): SignatureBox {
  if (!Number.isFinite(scale) || scale <= 0) throw new Error('Invalid mark size');
  const width = box.width * scale, height = box.height * scale;
  if (width > 1 || height > 1) throw new Error('Mark does not fit on this page');
  return { x: clamp(box.x, 0, 1 - width), y: clamp(box.y, 0, 1 - height), width, height };
}

export function markViewBoxPath(kind: PageMarkKind): string {
  return kind === 'Check' ? 'M18 52 L42 76 L82 24' : 'M24 24 L76 76 M76 24 L24 76';
}
