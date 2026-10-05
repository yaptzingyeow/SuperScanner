import { Injectable } from '@angular/core';
import { PageMarkDraft } from './page-mark.models';
import { SignatureBox } from './page-signature.models';
import { TextEditBox, TextEditStyle } from './text-edit.models';

/** What Ctrl+C copied in the page editor. Kept app-wide so it can be pasted on another page. */
export type EditorClipboardItem =
  | { kind: 'mark'; mark: Omit<PageMarkDraft, 'id' | 'revision'> }
  | { kind: 'signature'; image: Blob; box: SignatureBox; imageAspectRatio: number }
  | { kind: 'text'; text: string; style: TextEditStyle; box: TextEditBox };

@Injectable({ providedIn: 'root' })
export class EditorClipboardService {
  private item: EditorClipboardItem | null = null;
  get current(): EditorClipboardItem | null { return this.item; }
  copy(item: EditorClipboardItem): void { this.item = item; }
}

/** Pasted copies land slightly down and right of the original, staying on the page. */
export function offsetBox(box: SignatureBox, step = .02): SignatureBox {
  const clamp = (value: number, max: number) => Math.max(0, Math.min(max, value));
  return { ...box, x: clamp(box.x + step, 1 - box.width), y: clamp(box.y + step, 1 - box.height) };
}
