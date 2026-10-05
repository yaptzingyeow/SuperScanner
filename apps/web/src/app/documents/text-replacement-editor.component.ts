import { I18nService } from '../core/i18n/i18n.service';
import { Component, HostListener, OnDestroy, OnInit, computed, effect, input, output, signal, inject, untracked } from '@angular/core';
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
import { coversText } from './text-scripts';

@Component({
  selector: 'app-text-replacement-editor',
  standalone: true,
  imports: [FormsModule, TextReplacementOverlayComponent],
  templateUrl: './text-replacement-editor.component.html',
  styleUrl: './text-replacement-editor.component.scss',
  host: { '[class.inline-host]': 'inline()' },
})
export class TextReplacementEditorComponent implements OnInit, OnDestroy {
  protected readonly i18n = inject(I18nService);
  readonly mode = input<'replace' | 'delete' | 'add'>('replace');
  readonly documentId = input.required<string>();
  readonly selection = input.required<TextEditSelection>();
  readonly imageUrl = input.required<string>();
  readonly otherPolygons = input<OcrPoint[][]>([]);
  /**
   * Add text on the page itself: no dialog or page copy here, only the controls. The host page
   * draws the text box on its own image and feeds changes back through updateBox/updateText.
   */
  readonly inline = input(false);
  /** Inline add: where the user clicked on the page (normalised), the new box starts there. */
  readonly placeAt = input<{ x: number; y: number } | null>(null);
  /** Inline add: start from pasted text and style (Ctrl+V of a copied text box). */
  readonly seed = input<{ text: string; style: TextEditStyle; box: TextEditBox } | null>(null);
  /** Inline add: the page image size in pixels, used for the size field and fitting. */
  readonly pageSize = input<{ width: number; height: number } | null>(null);
  readonly closed = output<void>();
  readonly completed = output<void>();
  protected readonly proposal = signal<TextStyleProposal | null>(null);
  readonly replacement = signal('');
  readonly box = signal<TextEditBox>({ x: .1, y: .1, width: .2, height: .05 });
  readonly style = signal<TextEditStyle | null>(null);
  protected readonly fontFaces = signal<FontFaceEntry[]>([]);
  protected readonly fontSearch = signal('');
  protected readonly fontCategories = ['SansSerif', 'Serif', 'Monospace', 'Handwriting'] as const;
  protected readonly visibleFontFamilies = computed(() => {
    const search = this.fontSearch().trim().toLowerCase();
    const text = this.replacement();
    return this.fontFaces().filter((face) => face.weight === 400 && coversText(face.scripts, text) &&
      (!search || face.familyName.toLowerCase().includes(search)));
  });
  /** No installed font has glyphs for every typed character (e.g. a script not bundled yet). */
  protected readonly noFontForText = computed(() => this.fontFaces().length > 0 &&
    !this.fontFaces().some((face) => coversText(face.scripts, this.replacement())));
  protected readonly scriptNotice = signal('');
  // When typed characters need a script the chosen font lacks, switch to one that has it.
  private readonly followScript = effect(() => {
    const text = this.replacement();
    const faces = this.fontFaces();
    const current = untracked(() => this.style());
    if (!current || !faces.length) return;
    const chosen = faces.find((face) => face.catalogueId === current.fontId && face.version === current.fontVersion);
    if (!chosen || coversText(chosen.scripts, text)) return;
    const next = faces.find((face) => face.weight === 400 && face.category === chosen.category && coversText(face.scripts, text))
      ?? faces.find((face) => face.weight === 400 && coversText(face.scripts, text));
    if (!next) return;
    untracked(() => {
      this.chooseFont(next.catalogueId);
      this.scriptNotice.set(this.i18n.t('editor.switchedFont', { font: next.familyName }));
    });
  });
  protected readonly fontWeights = computed(() => this.fontFaces()
    .filter((face) => face.catalogueId === this.style()?.fontId)
    .map((face) => face.weight).filter((weight, index, all) => all.indexOf(weight) === index));
  /** Every weight from 100 to 900. Heavier than a real face is drawn as synthetic bold by the server. */
  protected readonly weightSteps = [100, 200, 300, 400, 500, 600, 700, 800, 900] as const;
  /** The lightest weight this font can draw: a face can be thickened, never thinned. */
  protected readonly lightestWeight = computed(() => {
    const weights = this.fontWeights();
    return weights.length ? Math.min(...weights) : 400;
  });
  readonly selectedWebFamily = computed(() => this.fontFaces().find((face) =>
    face.catalogueId === this.style()?.fontId && face.version === this.style()?.fontVersion)
    ?.webFamilyName ?? null);
  protected fontsInCategory(category: FontFaceEntry['category']): FontFaceEntry[] {
    return this.visibleFontFamilies().filter((face) => face.category === category);
  }
  protected readonly status = signal(this.i18n.t('editor.loadingProposal'));
  protected readonly warning = signal('');
  protected readonly submitting = signal(false);
  protected readonly done = signal(false);
  protected readonly failed = signal(false);
  protected readonly previewZoom = signal(1);
  /** Guide-only: covers the old words so the new text is easier to see. Never sent to the server. */
  protected readonly hideOriginal = signal(false);
  protected readonly spaceHeld = signal(false);
  protected readonly panning = signal(false);
  private pan?: { pointerId: number; x: number; y: number; left: number; top: number };
  readonly previewUrl = signal('');
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
    const pageSize = this.pageSize();
    if (pageSize?.width && pageSize.height) this.imageSize.set(pageSize);
    void this.fonts.list().then((faces) => {
      if (!this.destroyed) this.fontFaces.set(faces);
    }).catch(() => {
      if (!this.destroyed) this.warning.set(this.i18n.t('editor.fontCatalogueUnavailable'));
    });
    if (this.mode() === 'add') {
      try {
        const history = await this.api.history(this.documentId(), selection.pageId);
        if (this.destroyed) return;
        const seed = this.seed();
        const point = this.placeAt();
        const size = { width: .3, height: .05 };
        const box = seed ? { ...seed.box } : point
          ? { x: Math.max(0, Math.min(1 - size.width, point.x)),
              y: Math.max(0, Math.min(1 - size.height, point.y - size.height / 2)), ...size }
          : { x: .1, y: .1, ...size };
        const style: TextEditStyle = seed ? { ...seed.style } : { fontId: 'noto-sans', fontVersion: 'archive-main-regular',
          fontSize: .012, weight: 400, colorHex: '#202020', letterSpacing: 0,
          baseline: .75, angleDegrees: 0, alignment: 0 };
        if (seed) this.replacement.set(seed.text);
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
        this.status.set(this.i18n.t('editor.addStatus'));
      } catch (error) {
        if (!this.destroyed) this.status.set(error instanceof HttpErrorResponse && error.status === 401
          ? this.i18n.t('editor.signInAdd')
          : this.i18n.t('editor.errStartAdd'));
      }
      return;
    }
    this.replacement.set(selection.phrase);
    try {
      let proposal = await this.api.propose(this.documentId(), selection.pageId,
        selection.ocrResultId, [...selection.wordIds]);
      if (this.destroyed) return;
      if (!proposal.style.candidates.length) {
        this.status.set(this.i18n.t('editor.noSupportedFont'));
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
      this.status.set(this.i18n.t('editor.editStatus'));
      if (proposal.style.confidence < .6) this.warning.set(this.i18n.t('editor.lowConfidence'));
    } catch (error) {
      if (!this.destroyed) this.status.set(error instanceof HttpErrorResponse
        ? error.status === 503 && error.error?.code === 'text_edit_disabled'
          ? this.i18n.t('editor.errDisabled')
          : error.status === 401 ? this.i18n.t('editor.signInEdit')
          : error.status === 409 ? this.i18n.t('editor.pageChanged')
          : error.status === 422 ? this.i18n.t('editor.errSelection422')
          : this.i18n.t('editor.errLoadEditor')
        : this.i18n.t('editor.errLoadEditor'));
    }
  }

  updateText(value: string): void {
    this.replacement.set(value);
    this.idempotencyKey = '';
    this.clearPreview();
  }

  protected chooseFont(value: string): void {
    const face = this.fontFaces().find((item) => item.catalogueId === value && item.weight === 400);
    if (face) {
      const keepWeight = this.style()?.weight ?? 400;
      this.style.update((current) => current && ({
        ...current, fontId: face.catalogueId, fontVersion: face.version, weight: face.weight,
      }));
      void this.fonts.loadFace(face).catch(() => this.warning.set(this.i18n.t('editor.fontPreviewUnavailable')));
      this.idempotencyKey = '';
      this.clearPreview();
      // Keep the chosen weight across a font change (nearest face, thickened if needed).
      if (keepWeight !== face.weight) this.chooseWeight(keepWeight);
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

  protected toggleStrikethrough(on: boolean): void {
    this.style.update((current) => current && ({ ...current, strikethrough: on }));
    this.idempotencyKey = '';
    this.clearPreview();
  }

  /** Any weight 100–900: use the heaviest real face at or below it; the server thickens the rest. */
  protected chooseWeight(raw: number): void {
    const requested = Math.round(Number(raw) / 100) * 100;
    if (!Number.isFinite(requested)) return;
    const current = this.style();
    const faces = this.fontFaces().filter((face) => face.catalogueId === current?.fontId);
    if (!current || !faces.length) { this.updateStyle('weight', requested); return; }
    const weight = Math.min(900, Math.max(this.lightestWeight(), requested));
    const face = faces.filter((item) => item.weight <= weight).sort((a, b) => b.weight - a.weight)[0];
    void this.fonts.loadFace(face).catch(() => this.warning.set(this.i18n.t('editor.fontPreviewUnavailable')));
    this.style.set({ ...current, weight, fontVersion: face.version });
    this.idempotencyKey = '';
    this.clearPreview();
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
        void this.fonts.loadFace(face).catch(() => this.warning.set(this.i18n.t('editor.fontPreviewUnavailable')));
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

  updateBox(box: TextEditBox): void {
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
    // Ctrl/⌘+S saves (preview first if needed); inline add also closes on Esc.
    if (event.key === 'Escape' && this.inline()) { event.preventDefault(); this.cancel(); return; }
    if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 's') {
      event.preventDefault();
      void this.saveNow();
      return;
    }
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
    this.status.set(this.i18n.t('editor.adjustStatus'));
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

  cancel(): void {
    if (this.submitting()) return;
    // Inline add: nothing is saved yet and the box is on the page in plain view, so Cancel/Esc
    // just closes. A browser "discard?" prompt here read as "Cancel does nothing".
    if (!this.inline() && this.isDirty() && !window.confirm(this.i18n.t('editor.confirmDiscardText'))) return;
    this.closed.emit();
  }

  /** Ctrl+S: preview the exact result if needed, then save it. */
  async saveNow(): Promise<void> {
    if (this.submitting() || this.previewing()) return;
    if (!this.canApplyPreview()) await this.preview();
    if (this.canApplyPreview()) await this.apply();
  }


  protected async preview(): Promise<void> {
    if (this.previewing() || this.submitting() || !this.proposal() ||
        !this.style() || (this.mode() !== 'delete' && !this.replacement().trim()) ||
        /[\r\n]/u.test(this.replacement())) return;
    this.clearPreview();
    this.previewing.set(true);
    this.status.set(this.i18n.t('editor.renderingExact'));
    const request = this.buildRequest();
    const signature = JSON.stringify(request);
    try {
      const blob = await this.api.preview(this.documentId(), this.selection().pageId, request);
      if (this.destroyed) return;
      if (signature !== JSON.stringify(this.buildRequest())) {
        this.status.set(this.i18n.t('editor.draftChanged'));
        return;
      }
      this.clearPreview();
      this.previewUrl.set(URL.createObjectURL(blob));
      this.previewedSignature = signature;
      this.status.set(this.i18n.t('editor.reviewExact'));
    } catch (error) {
      if (this.destroyed) return;
      const code = await this.errorCode(error);
      this.status.set(code === 'text_edit_font_unsupported'
        ? this.i18n.t('editor.errFontUnsupportedPreview')
        : code === 'text_edit_unsafe_background'
        ? this.i18n.t('editor.errUnsafeBgSelect')
        : code === 'text_edit_placement_overlap'
          ? this.i18n.t('editor.errOverlapPreview')
          : code === 'text_edit_overflow'
            ? this.i18n.t('editor.errOverflowPreview')
            : code === 'text_selection_stale'
              ? this.i18n.t('editor.pageChanged')
              : this.i18n.t('editor.errPreviewFailed'));
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
    this.status.set(this.i18n.t('editor.checkingFit'));
    const request = this.buildRequest();
    try {
      const accepted = await this.api.apply(this.documentId(), this.selection().pageId, request);
      if (this.destroyed) return;
      this.status.set(this.i18n.t('editor.renderingChange'));
      void this.poll(accepted.editId);
    } catch (error) {
      if (this.destroyed) return;
      this.submitting.set(false);
      this.failed.set(true);
      const code = error instanceof HttpErrorResponse ? error.error?.code : undefined;
      this.status.set(code === 'text_edit_overflow'
        ? this.i18n.t('editor.errOverflowApply')
          : code === 'text_selection_invalid'
          ? this.i18n.t('editor.errSelectionInvalid')
          : error instanceof HttpErrorResponse && error.status === 409
            ? this.i18n.t('editor.pageChanged')
            : this.i18n.t('editor.errApply'));
    }
  }

  private async poll(editId: string): Promise<void> {
    if (this.destroyed) return;
    try {
      const result = await this.api.get(this.documentId(), this.selection().pageId, editId);
      if (this.destroyed) return;
      if (result.state === 'Succeeded') {
        this.status.set(this.i18n.t('editor.changeApplied'));
        this.done.set(true);
        this.submitting.set(false);
        this.completed.emit();
      } else if (result.state === 'Failed') {
        this.status.set(result.failureCode === 'text_edit_overflow'
          ? this.i18n.t('editor.errOverflowFail')
          : result.failureCode === 'text_edit_unsafe_background'
            ? this.i18n.t('editor.errUnsafeBgFail')
            : result.failureCode === 'text_edit_placement_overlap'
              ? this.i18n.t('editor.errOverlapFail')
            : result.failureCode === 'text_edit_font_unsupported'
              ? this.i18n.t('editor.errFontUnsupportedFail')
            : result.failureCode === 'text_edit_font_unavailable'
              ? this.i18n.t('editor.errFontUnavailable')
              : this.i18n.t('editor.errRenderFailed'));
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
