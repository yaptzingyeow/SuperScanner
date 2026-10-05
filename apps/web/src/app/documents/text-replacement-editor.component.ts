import { Component, HostListener, OnDestroy, OnInit, computed, input, output, signal, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { TextEditService } from './text-edit.service';
import { CreateTextEditRequest, TextEditBox, TextEditSelection, TextEditStyle,
  TextStyleProposal } from './text-edit.models';
import { TextReplacementOverlayComponent } from './text-replacement-overlay.component';
import { fitSingleLine } from './text-fit';
import { OcrPoint } from './document.models';
import { FontCatalogueService } from './font-catalogue.service';
import { FontFaceEntry } from './font-catalogue.models';

@Component({
  selector: 'app-text-replacement-editor',
  standalone: true,
  imports: [FormsModule, TextReplacementOverlayComponent],
  templateUrl: './text-replacement-editor.component.html',
  styleUrl: './text-replacement-editor.component.scss',
})
export class TextReplacementEditorComponent implements OnInit, OnDestroy {
  readonly mode = input<'replace' | 'delete' | 'add'>('replace');
  readonly documentId = input.required<string>();
  readonly selection = input.required<TextEditSelection>();
  readonly imageUrl = input.required<string>();
  readonly otherPolygons = input<OcrPoint[][]>([]);
  readonly closed = output<void>();
  readonly completed = output<void>();
  protected readonly proposal = signal<TextStyleProposal | null>(null);
  protected readonly replacement = signal('');
  protected readonly box = signal<TextEditBox>({ x: .1, y: .1, width: .2, height: .05 });
  protected readonly style = signal<TextEditStyle | null>(null);
  protected readonly fontFaces = signal<FontFaceEntry[]>([]);
  protected readonly fontSearch = signal('');
  protected readonly fontCategories = ['SansSerif', 'Serif', 'Monospace', 'Handwriting'] as const;
  protected readonly visibleFontFamilies = computed(() => {
    const search = this.fontSearch().trim().toLowerCase();
    return this.fontFaces().filter((face) => face.weight === 400 &&
      (!search || face.familyName.toLowerCase().includes(search)));
  });
  protected readonly fontWeights = computed(() => this.fontFaces()
    .filter((face) => face.catalogueId === this.style()?.fontId)
    .map((face) => face.weight).filter((weight, index, all) => all.indexOf(weight) === index));
  protected readonly selectedWebFamily = computed(() => this.fontFaces().find((face) =>
    face.catalogueId === this.style()?.fontId && face.version === this.style()?.fontVersion)
    ?.webFamilyName ?? null);
  protected fontsInCategory(category: FontFaceEntry['category']): FontFaceEntry[] {
    return this.visibleFontFamilies().filter((face) => face.category === category);
  }
  protected readonly status = signal('Loading style proposal…');
  protected readonly warning = signal('');
  protected readonly submitting = signal(false);
  protected readonly done = signal(false);
  protected readonly failed = signal(false);
  protected readonly previewZoom = signal(1);
  protected readonly spaceHeld = signal(false);
  protected readonly panning = signal(false);
  private pan?: { pointerId: number; x: number; y: number; left: number; top: number };
  protected readonly previewUrl = signal('');
  protected readonly previewing = signal(false);
  private initialStyle: TextEditStyle | null = null;
  private initialBox: TextEditBox | null = null;
  readonly isDirty = computed(() => {
    const proposal = this.proposal();
    if (!proposal || this.done()) return false;
    return this.replacement() !== (this.mode() === 'replace' ? proposal.originalText : '') ||
      JSON.stringify(this.box()) !== JSON.stringify(this.initialBox) ||
      JSON.stringify(this.style()) !== JSON.stringify(this.initialStyle);
  });
  protected readonly imageSize = signal({ width: 1000, height: 1400 });
  /** The size field in image pixels, to one decimal place. */
  protected fontSizePixels(fontSize: number): number {
    return Math.round(fontSize * this.imageSize().height * 10) / 10;
  }
  private changedSize = false;
  private boxAdjusted = false;
  private boxPadded = false;
  protected readonly overflow = computed(() => {
    const text = this.replacement();
    const style = this.style();
    if (!style || !text.trim()) return false;
    try {
      return fitSingleLine((value, fontPixels) => ({
        width: [...value].reduce((total, character) =>
          total + fontPixels * (character === ' ' ? .28 : /[ilI.,]/u.test(character) ? .3 : .62), 0),
        height: fontPixels * 1.15,
      }), {
        text, box: this.box(), imageWidth: this.imageSize().width,
        imageHeight: this.imageSize().height,
        fontSizeNormalized: style.fontSize, letterSpacing: style.letterSpacing,
        minimumLetterSpacing: style.letterSpacing > .1 ? style.letterSpacing : -.02,
        minimumFontScale: .7,
      }).overflow;
    } catch { return true; }
  });
  private readonly api = inject(TextEditService);
  private readonly fonts = inject(FontCatalogueService);
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private idempotencyKey = '';
  private previewedSignature = '';

  async ngOnInit(): Promise<void> {
    const selection = this.selection();
    void this.fonts.list().then((faces) => {
      if (!this.destroyed) this.fontFaces.set(faces);
    }).catch(() => {
      if (!this.destroyed) this.warning.set('Font catalogue unavailable. Recommended fonts remain available.');
    });
    if (this.mode() === 'add') {
      try {
        const history = await this.api.history(this.documentId(), selection.pageId);
        if (this.destroyed) return;
        const box = { x: .1, y: .1, width: .3, height: .05 };
        const style: TextEditStyle = { fontId: 'noto-sans', fontVersion: 'archive-main-regular',
          fontSize: .012, weight: 400, colorHex: '#202020', letterSpacing: 0,
          baseline: .75, angleDegrees: 0, alignment: 0 };
        this.proposal.set({ activeRevisionId: history.activeRevisionId,
          ocrResultId: selection.ocrResultId, wordIds: [], originalText: '', box,
          style: { candidates: [
            { catalogueId: 'noto-sans', version: 'archive-main-regular', score: 1 },
            { catalogueId: 'noto-sans', version: 'archive-main-bold', score: 1 },
            { catalogueId: 'noto-serif', version: 'archive-main-regular', score: 1 },
            { catalogueId: 'noto-serif', version: 'archive-main-bold', score: 1 },
          ], confidence: 1, colorHex: '#202020', fontSizePoints: 16,
          fontWeight: 400, letterSpacing: 0, baselineAngleDegrees: 0, alignment: 'left' } });
        this.box.set(box);
        this.style.set(style);
        this.initialBox = { ...box };
        this.initialStyle = { ...style };
        this.status.set('Enter text, drag the transparent box, then preview the exact result.');
      } catch (error) {
        if (!this.destroyed) this.status.set(error instanceof HttpErrorResponse && error.status === 401
          ? 'Sign in to add text on this page.'
          : 'Could not start adding text. Please try again.');
      }
      return;
    }
    this.replacement.set(selection.phrase);
    try {
      let proposal = await this.api.propose(this.documentId(), selection.pageId,
        selection.ocrResultId, [...selection.wordIds]);
      if (this.destroyed) return;
      if (!proposal.style.candidates.length) {
        this.status.set('No supported font is available for this selection.');
        return;
      }
      // OCR text can carry doubled or trailing spaces; edit a tidy phrase.
      proposal = { ...proposal, originalText: proposal.originalText.replace(/\s+/g, ' ').trim() };
      this.proposal.set(proposal);
      this.box.set({ ...proposal.box });
      this.replacement.set(this.mode() === 'delete' ? '' : proposal.originalText);
      const first = proposal.style.candidates[0];
      this.style.set({
        fontId: first.catalogueId,
        fontVersion: first.version,
        fontSize: Math.min(.95, Math.max(.001,
          proposal.style.fontSizePoints / this.imageSize().height)),
        weight: proposal.style.fontWeight,
        colorHex: proposal.style.colorHex,
        letterSpacing: proposal.style.letterSpacing,
        baseline: .75,
        angleDegrees: proposal.style.baselineAngleDegrees,
        alignment: { left: 0, center: 1, right: 2 }[proposal.style.alignment.toLowerCase()] ?? 0,
      });
      this.initialStyle = { ...this.style()! };
      this.initialBox = { ...proposal.box };
      this.status.set('Edit the text, then select Preview exact result to check it.');
      if (proposal.style.confidence < .6) this.warning.set('Low confidence font match. Review the alternatives.');
    } catch (error) {
      if (!this.destroyed) this.status.set(error instanceof HttpErrorResponse
        ? error.status === 503 && error.error?.code === 'text_edit_disabled'
          ? 'Text editing is disabled on this server. An administrator must enable it before edits can be applied.'
          : error.status === 401 ? 'Sign in to edit text on this page.'
          : error.status === 409 ? 'This page changed. Close the editor and select the words again.'
          : error.status === 422 ? 'This text selection cannot be edited. Select printed words and try again.'
          : 'Could not load the text editor. Please try again.'
        : 'Could not load the text editor. Please try again.');
    }
  }

  protected updateText(value: string): void {
    this.replacement.set(value);
    this.idempotencyKey = '';
    this.clearPreview();
  }

  protected chooseFont(value: string): void {
    const face = this.fontFaces().find((item) => item.catalogueId === value && item.weight === 400);
    if (face) {
      this.style.update((current) => current && ({
        ...current, fontId: face.catalogueId, fontVersion: face.version, weight: face.weight,
      }));
      void this.fonts.loadFace(face).catch(() => this.warning.set('Font preview unavailable; the server font remains selectable.'));
      this.idempotencyKey = '';
      this.clearPreview();
      return;
    }
    const candidates = this.proposal()?.style.candidates.filter((item) => item.catalogueId === value);
    const candidate = candidates?.find((item) => item.version.includes('bold') === (this.style()?.weight === 700))
      ?? candidates?.[0];
    if (candidate) {
      this.style.update((current) => current && ({
        ...current, fontId: candidate.catalogueId, fontVersion: candidate.version,
        weight: candidate.version.includes('bold') ? 700 : 400,
      }));
      this.idempotencyKey = '';
      this.clearPreview();
    }
  }

  protected fontFamilyName(id: string): string {
    return id.split('-').map((part) => part === 'noto' ? 'Noto' : part.charAt(0).toUpperCase() + part.slice(1)).join(' ');
  }

  protected updateFontSizePixels(value: number): void {
    if (!Number.isFinite(value) || value <= 0) return;
    this.updateStyle('fontSize', Math.min(1, value / this.imageSize().height));
  }

  protected updateStyle(field: keyof TextEditStyle, value: string | number): void {
    if (field === 'fontSize') this.changedSize = true;
    if (field === 'letterSpacing') {
      const spacing = Number(value);
      value = Number.isFinite(spacing) ? Math.min(3, Math.max(-.1, spacing)) : 0;
    }
    this.style.update((current) => {
      if (!current) return current;
      if (field !== 'weight') return { ...current, [field]: value };
      const face = this.fontFaces().find((item) => item.catalogueId === current.fontId && item.weight === value);
      if (face) {
        void this.fonts.loadFace(face).catch(() => this.warning.set('Font preview unavailable; the server font remains selectable.'));
        return { ...current, weight: value as number, fontVersion: face.version };
      }
      const candidate = this.proposal()?.style.candidates.find((item) =>
        item.catalogueId === current.fontId &&
        item.version.includes('bold') === (value === 700));
      return candidate ? { ...current, weight: value as number, fontVersion: candidate.version } : current;
    });
    this.idempotencyKey = '';
    this.clearPreview();
  }

  protected imageLoaded(event: Event): void {
    const image = event.target as HTMLImageElement;
    if (!image.naturalWidth || !image.naturalHeight) return;
    this.imageSize.set({ width: image.naturalWidth, height: image.naturalHeight });
    const suggested = this.proposal();
    if (suggested && !this.boxAdjusted && !this.boxPadded) {
      const padded = this.padInitialBox(suggested.box, image.naturalWidth, image.naturalHeight);
      this.box.set(padded);
      this.initialBox = { ...padded };
      this.boxPadded = true;
    }
    if (suggested && !this.changedSize) {
      const fontSize = Math.min(.95, Math.max(.001,
        suggested.style.fontSizePoints / image.naturalHeight));
      this.style.update((current) => current && ({ ...current, fontSize }));
      if (this.initialStyle) this.initialStyle = { ...this.initialStyle, fontSize };
    }
  }

  protected updateBox(box: TextEditBox): void {
    this.boxAdjusted = true;
    this.box.set(box);
    this.idempotencyKey = '';
    this.clearPreview();
  }

  protected changePreviewZoom(delta: number): void {
    this.previewZoom.update((current) => Math.min(3, Math.max(1, current + delta)));
  }

  @HostListener('document:keydown', ['$event'])
  protected panKeyDown(event: KeyboardEvent): void {
    if (event.code !== 'Space' && event.key !== ' ') return;
    if (event.target instanceof HTMLElement &&
        event.target.closest('input, textarea, select, button, [contenteditable="true"]')) return;
    if (this.previewZoom() <= 1) return;
    event.preventDefault();
    this.spaceHeld.set(true);
  }

  @HostListener('document:keyup', ['$event'])
  protected panKeyUp(event: KeyboardEvent): void {
    if (event.code === 'Space' || event.key === ' ') this.spaceHeld.set(false);
  }

  protected beginPan(event: PointerEvent): void {
    if (this.previewZoom() <= 1 || event.button !== 0) return;
    const viewport = event.currentTarget as HTMLElement;
    event.preventDefault();
    this.pan = { pointerId: event.pointerId, x: event.clientX, y: event.clientY,
      left: viewport.scrollLeft, top: viewport.scrollTop };
    this.panning.set(true);
    viewport.setPointerCapture?.(event.pointerId);
  }

  protected movePan(event: PointerEvent): void {
    if (!this.pan || this.pan.pointerId !== event.pointerId) return;
    const viewport = event.currentTarget as HTMLElement;
    viewport.scrollLeft = this.pan.left + this.pan.x - event.clientX;
    viewport.scrollTop = this.pan.top + this.pan.y - event.clientY;
  }

  protected endPan(event: PointerEvent): void {
    if (this.pan?.pointerId !== event.pointerId) return;
    this.pan = undefined;
    this.panning.set(false);
  }

  protected adjustPlacement(): void {
    this.clearPreview();
    this.status.set('Adjust the text or placement box, then preview the result again.');
  }

  private padInitialBox(box: TextEditBox, width: number, height: number): TextEditBox {
    let padded = { ...box };
    const overlapsOtherWord = (candidate: TextEditBox) => this.otherPolygons().some((polygon) => {
      const xs = polygon.map((point) => point.x);
      const ys = polygon.map((point) => point.y);
      return xs.length > 0 && ys.length > 0 &&
        Math.min(...xs) < candidate.x + candidate.width && Math.max(...xs) > candidate.x &&
        Math.min(...ys) < candidate.y + candidate.height && Math.max(...ys) > candidate.y;
    });
    const tryExpand = (candidate: TextEditBox) => {
      if (!overlapsOtherWord(candidate)) padded = candidate;
    };
    const left = Math.max(0, padded.x - 2 / width);
    tryExpand({ ...padded, x: left, width: padded.width + padded.x - left });
    const right = Math.min(1, padded.x + padded.width + 2 / width);
    tryExpand({ ...padded, width: right - padded.x });
    const top = Math.max(0, padded.y - 2 / height);
    tryExpand({ ...padded, y: top, height: padded.height + padded.y - top });
    const bottom = Math.min(1, padded.y + padded.height + 2 / height);
    tryExpand({ ...padded, height: bottom - padded.y });
    return padded;
  }

  protected cancel(): void {
    if (this.submitting()) return;
    if (this.isDirty() && !window.confirm('Discard your unapplied text changes?')) return;
    this.closed.emit();
  }

  protected async preview(): Promise<void> {
    if (this.previewing() || this.submitting() || !this.proposal() ||
        !this.style() || (this.mode() !== 'delete' && !this.replacement().trim()) ||
        /[\r\n]/u.test(this.replacement())) return;
    this.clearPreview();
    this.previewing.set(true);
    this.status.set('Rendering an exact preview. Your page is unchanged…');
    const request = this.buildRequest();
    const signature = JSON.stringify(request);
    try {
      const blob = await this.api.preview(this.documentId(), this.selection().pageId, request);
      if (this.destroyed) return;
      if (signature !== JSON.stringify(this.buildRequest())) {
        this.status.set('The draft changed while rendering. Preview it again.');
        return;
      }
      this.clearPreview();
      this.previewUrl.set(URL.createObjectURL(blob));
      this.previewedSignature = signature;
      this.status.set('Review the exact result, then select Apply change to save it.');
    } catch (error) {
      if (this.destroyed) return;
      const code = await this.errorCode(error);
      this.status.set(code === 'text_edit_unsafe_background'
        ? 'We could not clear the original ink without risking other page content. Select different words. Your page is unchanged.'
        : code === 'text_edit_placement_overlap'
          ? 'The new letters would cover nearby text. Move or resize the transparent placement box, or use a smaller font.'
          : code === 'text_edit_overflow'
            ? 'The replacement does not fit. Use a smaller font or widen the placement box.'
            : code === 'text_selection_stale'
              ? 'This page changed. Close the editor and select the words again.'
              : 'The preview could not be rendered. Your page is unchanged.');
    } finally {
      if (!this.destroyed) this.previewing.set(false);
    }
  }

  private clearPreview(): void {
    if (this.previewUrl()) URL.revokeObjectURL(this.previewUrl());
    this.previewUrl.set('');
    this.previewedSignature = '';
  }

  private async errorCode(error: unknown): Promise<string | undefined> {
    if (!(error instanceof HttpErrorResponse)) return undefined;
    if (error.error instanceof Blob) {
      try { return JSON.parse(await error.error.text()).code; }
      catch { return undefined; }
    }
    return error.error?.code;
  }

  private buildRequest(): CreateTextEditRequest {
    const proposal = this.proposal()!;
    this.idempotencyKey ||= crypto.randomUUID();
    return {
      ocrResultId: proposal.ocrResultId,
      expectedRevisionId: proposal.activeRevisionId,
      wordIds: [...proposal.wordIds],
      replacementText: this.replacement(),
      replacementBox: { ...this.box() },
      style: { ...this.style()! },
      idempotencyKey: this.idempotencyKey,
    };
  }

  protected canApplyPreview(): boolean {
    return !!this.previewUrl() && this.previewedSignature === JSON.stringify(this.buildRequest());
  }

  protected async apply(): Promise<void> {
    const proposal = this.proposal();
    const style = this.style();
    const text = this.replacement();
    if (this.submitting() || !proposal || !style ||
        (this.mode() !== 'delete' && !text.trim()) || /[\r\n]/u.test(text) || !this.canApplyPreview()) return;
    this.submitting.set(true);
    this.failed.set(false);
    this.status.set('Checking text fit…');
    const request = this.buildRequest();
    try {
      const accepted = await this.api.apply(this.documentId(), this.selection().pageId, request);
      if (this.destroyed) return;
      this.status.set('Rendering your change…');
      void this.poll(accepted.editId);
    } catch (error) {
      if (this.destroyed) return;
      this.submitting.set(false);
      this.failed.set(true);
      const code = error instanceof HttpErrorResponse ? error.error?.code : undefined;
      this.status.set(code === 'text_edit_overflow'
        ? 'The text does not fit the replacement box, even after automatic fitting. Use a smaller font or widen the box. Your page is unchanged.'
          : code === 'text_selection_invalid'
          ? 'The selected OCR words cannot be edited safely. Select the words again. Your page is unchanged.'
          : error instanceof HttpErrorResponse && error.status === 409
            ? 'This page changed. Close the editor and select the words again.'
            : 'We could not apply this change. Review the box and try again.');
    }
  }

  private async poll(editId: string): Promise<void> {
    if (this.destroyed) return;
    try {
      const result = await this.api.get(this.documentId(), this.selection().pageId, editId);
      if (this.destroyed) return;
      if (result.state === 'Succeeded') {
        this.status.set('Change applied. Refreshing page…');
        this.done.set(true);
        this.submitting.set(false);
        this.completed.emit();
      } else if (result.state === 'Failed') {
        this.status.set(result.failureCode === 'text_edit_overflow'
          ? 'The text does not fit the replacement box. Use a smaller font or widen the box. The previous page is unchanged.'
          : result.failureCode === 'text_edit_unsafe_background'
            ? 'We could not clear the original ink without risking other page content. Try a different word selection. The previous page is unchanged.'
            : result.failureCode === 'text_edit_placement_overlap'
              ? 'The new letters would cover nearby text. Move or resize the transparent placement box, or use a smaller font. The previous page is unchanged.'
            : result.failureCode === 'text_edit_font_unavailable'
              ? 'This font is unavailable for rendering. Choose another font and try again. The previous page is unchanged.'
              : 'Rendering failed. The previous page is unchanged.');
        this.submitting.set(false);
        this.failed.set(true);
        this.proposal.update((current) => current && ({
          ...current,
          activeRevisionId: result.sourceRevisionId,
        }));
        this.idempotencyKey = '';
      } else this.schedulePoll(editId);
    } catch {
      if (!this.destroyed) this.schedulePoll(editId);
    }
  }

  private schedulePoll(editId: string): void {
    this.timer = setTimeout(() => void this.poll(editId), 1500);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
    this.clearPreview();
  }
}
