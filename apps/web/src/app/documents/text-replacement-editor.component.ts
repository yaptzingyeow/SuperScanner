import { Component, OnDestroy, OnInit, computed, input, output, signal, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { TextEditService } from './text-edit.service';
import { CreateTextEditRequest, TextEditBox, TextEditSelection, TextEditStyle,
  TextStyleProposal } from './text-edit.models';
import { TextReplacementOverlayComponent } from './text-replacement-overlay.component';
import { fitSingleLine } from './text-fit';
import { OcrPoint } from './document.models';

@Component({
  selector: 'app-text-replacement-editor',
  standalone: true,
  imports: [FormsModule, TextReplacementOverlayComponent],
  templateUrl: './text-replacement-editor.component.html',
  styleUrl: './text-replacement-editor.component.scss',
})
export class TextReplacementEditorComponent implements OnInit, OnDestroy {
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
  protected readonly status = signal('Loading style proposal…');
  protected readonly warning = signal('');
  protected readonly submitting = signal(false);
  protected readonly done = signal(false);
  protected readonly failed = signal(false);
  private initialStyle: TextEditStyle | null = null;
  private initialBox: TextEditBox | null = null;
  readonly isDirty = computed(() => {
    const proposal = this.proposal();
    if (!proposal || this.done()) return false;
    return this.replacement() !== proposal.originalText ||
      JSON.stringify(this.box()) !== JSON.stringify(this.initialBox) ||
      JSON.stringify(this.style()) !== JSON.stringify(this.initialStyle);
  });
  private readonly imageSize = signal({ width: 1000, height: 1400 });
  private changedSize = false;
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
        minimumLetterSpacing: -.03, minimumFontScale: .6,
      }).overflow;
    } catch { return true; }
  });
  private readonly api = inject(TextEditService);
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  private idempotencyKey = '';

  async ngOnInit(): Promise<void> {
    const selection = this.selection();
    this.replacement.set(selection.phrase);
    try {
      const proposal = await this.api.propose(this.documentId(), selection.pageId,
        selection.ocrResultId, [...selection.wordIds]);
      if (this.destroyed) return;
      if (!proposal.style.candidates.length) {
        this.status.set('No supported font is available for this selection.');
        return;
      }
      this.proposal.set(proposal);
      this.box.set({ ...proposal.box });
      this.replacement.set(proposal.originalText);
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
      this.status.set('Preview ready. Changes have not been applied.');
      if (proposal.style.confidence < .6) this.warning.set('Low confidence font match. Review the alternatives.');
    } catch {
      if (!this.destroyed) this.status.set('Text editing is not available right now. Please try again.');
    }
  }

  protected updateText(value: string): void {
    this.replacement.set(value);
    this.idempotencyKey = '';
  }

  protected chooseFont(value: string): void {
    const candidate = this.proposal()?.style.candidates.find((item) =>
      `${item.catalogueId}:${item.version}` === value);
    if (candidate) {
      this.style.update((current) => current && ({
        ...current, fontId: candidate.catalogueId, fontVersion: candidate.version,
        weight: candidate.version.includes('bold') ? 700 : 400,
      }));
      this.idempotencyKey = '';
    }
  }

  protected updateStyle(field: keyof TextEditStyle, value: string | number): void {
    if (field === 'fontSize') this.changedSize = true;
    this.style.update((current) => {
      if (!current) return current;
      if (field !== 'weight') return { ...current, [field]: value };
      const candidate = this.proposal()?.style.candidates.find((item) =>
        item.catalogueId === current.fontId &&
        item.version.includes('bold') === (value === 700));
      return candidate ? { ...current, weight: value as number, fontVersion: candidate.version } : current;
    });
    this.idempotencyKey = '';
  }

  protected imageLoaded(event: Event): void {
    const image = event.target as HTMLImageElement;
    if (!image.naturalWidth || !image.naturalHeight) return;
    this.imageSize.set({ width: image.naturalWidth, height: image.naturalHeight });
    const suggested = this.proposal();
    if (suggested && !this.changedSize) {
      const fontSize = Math.min(.95, Math.max(.001,
        suggested.style.fontSizePoints / image.naturalHeight));
      this.style.update((current) => current && ({ ...current, fontSize }));
      if (this.initialStyle) this.initialStyle = { ...this.initialStyle, fontSize };
    }
  }

  protected updateBox(box: TextEditBox): void {
    this.box.set(box);
    this.idempotencyKey = '';
  }

  protected cancel(): void {
    if (this.submitting()) return;
    if (this.isDirty() && !window.confirm('Discard your unapplied text changes?')) return;
    this.closed.emit();
  }

  protected async apply(): Promise<void> {
    const proposal = this.proposal();
    const style = this.style();
    const text = this.replacement();
    if (this.submitting() || this.overflow() || !proposal || !style ||
        !text.trim() || /[\r\n]/u.test(text)) return;
    this.submitting.set(true);
    this.failed.set(false);
    this.status.set('Applying change…');
    this.idempotencyKey ||= crypto.randomUUID();
    const request: CreateTextEditRequest = {
      ocrResultId: proposal.ocrResultId,
      expectedRevisionId: proposal.activeRevisionId,
      wordIds: [...proposal.wordIds],
      replacementText: text,
      replacementBox: { ...this.box() },
      style: { ...style },
      idempotencyKey: this.idempotencyKey,
    };
    try {
      const accepted = await this.api.apply(this.documentId(), this.selection().pageId, request);
      if (this.destroyed) return;
      this.status.set('Rendering your change…');
      void this.poll(accepted.editId);
    } catch {
      if (this.destroyed) return;
      this.submitting.set(false);
      this.failed.set(true);
      this.status.set('We could not apply this change. Review the box and try again.');
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
        this.status.set('Rendering failed. The previous page is unchanged.');
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
  }
}
