import { HttpClient } from '@angular/common/http';
import { Component, HostListener, OnDestroy, OnInit, ViewChild, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentPage, OcrPoint, PageOcr } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { flattenSelectableWords } from './ocr-selection';
import { OcrTextOverlayComponent } from './ocr-text-overlay.component';
import { TextEditSelection } from './text-edit.models';
import { TextReplacementEditorComponent } from './text-replacement-editor.component';
import { SignatureCreatorComponent } from './signature-creator.component';
import { PageSignatureOverlayComponent } from './page-signature-overlay.component';
import { PageSignatureService } from './page-signature.service';
import { SignatureBox, SignatureDraft, SignatureView } from './page-signature.models';
import { CdkTrapFocus } from '@angular/cdk/a11y';

@Component({
  selector: 'app-page-text-editor',
  standalone: true,
  imports: [RouterLink, OcrTextOverlayComponent, TextReplacementEditorComponent, SignatureCreatorComponent, PageSignatureOverlayComponent, CdkTrapFocus],
  templateUrl: './page-text-editor.component.html',
  styleUrl: './page-text-editor.component.scss',
})
export class PageTextEditorComponent implements OnInit, OnDestroy {
  @ViewChild(TextReplacementEditorComponent) private editor?: TextReplacementEditorComponent;
  private readonly route = inject(ActivatedRoute);
  private readonly api = inject(DocumentsApiService);
  private readonly http = inject(HttpClient);
  private readonly signatureApi = inject(PageSignatureService);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  protected readonly documentId = this.route.snapshot.paramMap.get('documentId') ?? '';
  protected readonly pageId = this.route.snapshot.paramMap.get('pageId') ?? '';
  protected readonly page = signal<DocumentPage | null>(null);
  protected readonly title = signal('');
  protected readonly imageUrl = signal('');
  protected readonly ocr = signal<PageOcr | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly selection = signal<TextEditSelection | null>(null);
  protected readonly editMode = signal<'replace' | 'delete' | 'add'>('replace');
  protected readonly otherPolygons = signal<OcrPoint[][]>([]);
  protected readonly zoom = signal(1);
  protected readonly spaceHeld = signal(false);
  protected readonly panning = signal(false);
  protected readonly signatures = signal<SignatureView[]>([]);
  protected readonly signatureDraft = signal<SignatureDraft | null>(null);
  protected readonly selectedSignatureId = signal<string | null>(null);
  protected readonly signatureCreatorOpen = signal(false);
  protected readonly signatureBusy = signal(false);
  protected readonly signatureError = signal('');
  protected readonly signatureNotice = signal('');
  private signatureBlob?: Blob;
  private signatureRequestId = '';
  private signatureTarget?: SignatureView;
  private signatureGeneration = 0;
  private readonly signatureUrls = new Set<string>();
  private pan?: { pointerId: number; x: number; y: number; left: number; top: number };
  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;

  ngOnInit(): void {
    void this.load();
  }

  canLeave(): boolean {
    return !(this.editor?.isDirty() || this.signatureDraft() || this.signatureCreatorOpen()) || window.confirm('Discard your unsaved page changes?');
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.editor?.isDirty() || this.signatureDraft() || this.signatureCreatorOpen()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    try {
      const document = await this.api.getDocument(this.documentId);
      if (this.destroyed) return;
      const page = document.pages.find((candidate) => candidate.id === this.pageId);
      if (!page || !page.hasPreview) throw new Error('Page preview unavailable');
      this.page.set(page);
      this.title.set(document.title);
      const blob = await firstValueFrom(this.http.get(
        `${this.base}/documents/${this.documentId}/pages/${this.pageId}/preview`,
        { responseType: 'blob' },
      ));
      if (this.destroyed) return;
      if (this.imageUrl()) URL.revokeObjectURL(this.imageUrl());
      this.imageUrl.set(URL.createObjectURL(blob));
      await Promise.all([this.refreshOcr(), this.refreshSignatures()]);
    } catch {
      if (!this.destroyed) this.error.set('Could not open this page for text editing. Try again.');
    } finally {
      if (!this.destroyed) this.loading.set(false);
    }
  }

  protected async recognize(): Promise<void> {
    const current = this.ocr();
    if (this.busy() || !current || !['NotRequested', 'Failed'].includes(current.state)) return;
    this.busy.set(true);
    this.error.set('');
    try {
      const next = await this.api.requestPageOcr(this.documentId, this.pageId,
        current.state === 'Failed');
      if (this.destroyed) return;
      this.ocr.set(next);
      this.schedule(next);
    } catch {
      if (!this.destroyed) this.error.set('Text recognition could not start. Try again.');
    } finally {
      if (!this.destroyed) this.busy.set(false);
    }
  }

  protected setZoom(value: number): void {
    this.zoom.set(Math.min(3, Math.max(1, value)));
  }

  @HostListener('document:keydown', ['$event'])
  protected panKeyDown(event: KeyboardEvent): void {
    if (event.code !== 'Space' && event.key !== ' ') return;
    if (event.target instanceof HTMLElement &&
        event.target.closest('input, textarea, select, button, [contenteditable="true"]')) return;
    if (this.zoom() <= 1) return;
    event.preventDefault();
    this.spaceHeld.set(true);
  }

  @HostListener('document:keyup', ['$event'])
  protected panKeyUp(event: KeyboardEvent): void {
    if (event.code === 'Space' || event.key === ' ') this.spaceHeld.set(false);
  }

  protected beginPan(event: PointerEvent): void {
    if (this.zoom() <= 1 || event.button !== 0) return;
    if (event.target instanceof Element &&
        event.target.closest('button, a, input, select, textarea, [role="button"]')) return;
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

  protected beginTextEdit(selection: TextEditSelection, mode: 'replace' | 'delete' = 'replace'): void {
    if (!this.leaveSignatureTool()) return;
    this.editMode.set(mode);
    this.selection.set(selection);
    const selected = new Set(selection.wordIds);
    this.otherPolygons.set(flattenSelectableWords(this.ocr()?.elements ?? [])
      .filter((word) => !selected.has(word.id))
      .map((word) => word.polygon.map((point) => ({ ...point }))));
  }

  protected addText(): void {
    if (!this.leaveSignatureTool()) return;
    this.editMode.set('add');
    this.otherPolygons.set([]);
    this.selection.set({ pageId: this.pageId,
      ocrResultId: '00000000-0000-0000-0000-000000000000',
      wordIds: [], phrase: '', textType: 'Printed', polygon: [] });
  }

  protected closeTextEdit(): void {
    this.selection.set(null);
    this.otherPolygons.set([]);
  }

  protected async textEditCompleted(): Promise<void> {
    this.closeTextEdit();
    await this.load();
  }

  protected openSignatureCreator(): void {
    if (this.signatureBusy()) return;
    if (this.editor?.isDirty() && !window.confirm('Discard your unapplied text changes?')) return;
    if (!this.leaveSignatureTool()) return;
    this.closeTextEdit(); this.signatureError.set(''); this.signatureNotice.set(''); this.signatureCreatorOpen.set(true);
  }

  protected async signatureCreated(blob: Blob): Promise<void> {
    const url = URL.createObjectURL(blob); this.signatureUrls.add(url);
    const image = new Image();
    try {
      await new Promise<void>((resolve, reject) => { image.onload = () => resolve(); image.onerror = () => reject(new Error()); image.src = url; });
      if (this.destroyed) { URL.revokeObjectURL(url); return; }
      const aspect = image.naturalWidth / image.naturalHeight;
      if (!Number.isFinite(aspect) || aspect <= 0) throw new Error();
      const surface = document.querySelector<HTMLElement>('.full-page-image');
      const viewport = surface?.closest<HTMLElement>('.page-scroll');
      const pageImage = surface?.querySelector<HTMLImageElement>('img');
      const pageAspect = pageImage?.naturalWidth && pageImage.naturalHeight ? pageImage.naturalWidth / pageImage.naturalHeight : 1;
      let width = .3, height = width * pageAspect / aspect;
      if (height > .35) { height = .35; width = height * aspect / pageAspect; }
      const rect = surface?.getBoundingClientRect(), view = viewport?.getBoundingClientRect();
      const centerX = rect?.width && view ? ((view.left + view.right) / 2 - rect.left) / rect.width : .5;
      const centerY = rect?.height && view ? ((view.top + view.bottom) / 2 - rect.top) / rect.height : .5;
      this.signatureRequestId = crypto.randomUUID(); this.signatureBlob = blob; this.signatureTarget = undefined;
      this.signatureDraft.set({ id: 'draft', box: { x: Math.max(0, Math.min(1 - width, centerX - width / 2)),
        y: Math.max(0, Math.min(1 - height, centerY - height / 2)), width, height }, imageAspectRatio: aspect, localImageUrl: url });
      this.selectedSignatureId.set('draft'); this.signatureCreatorOpen.set(false); this.signatureNotice.set('Drag to place your signature, then Save.');
    } catch { this.signatureError.set('Could not open this signature. Choose another image.'); URL.revokeObjectURL(url); this.signatureUrls.delete(url); }
  }

  protected selectSignature(id: string): void {
    if (this.signatureBusy() || this.signatureDraft()?.id === id) return;
    if (this.signatureDraft() && !window.confirm('Discard your unsaved signature placement?')) return;
    this.cancelSignature(); this.selectedSignatureId.set(id); this.signatureError.set('');
  }
  protected selectedSignature(): SignatureView | undefined { return this.signatures().find(signature => signature.id === this.selectedSignatureId()); }
  protected moveSignature(): void {
    const signature = this.selectedSignature(); if (!signature || this.signatureBusy()) return;
    this.signatureTarget = signature; this.signatureBlob = undefined;
    this.signatureDraft.set({ ...signature, box: { ...signature.box } });
    this.signatureNotice.set('Drag to move; use the corner handles to resize. Save when ready.');
  }
  protected changeSignatureBox(change: { id: string; box: SignatureBox }): void {
    const draft = this.signatureDraft(); if (draft?.id === change.id && !this.signatureBusy()) this.signatureDraft.set({ ...draft, box: change.box });
  }
  protected signatureNumber(field: 'x' | 'y' | 'width', event: Event): void {
    const draft = this.signatureDraft(); if (!draft || this.signatureBusy()) return;
    const value = Number((event.target as HTMLInputElement).value) / 100; if (!Number.isFinite(value)) return;
    const box = { ...draft.box };
    if (field === 'width') {
      const ratio = box.width / box.height;
      box.width = Math.max(.005, Math.min(value, 1 - box.x, (1 - box.y) * ratio)); box.height = box.width / ratio;
    } else box[field] = Math.max(0, Math.min(value, 1 - (field === 'x' ? box.width : box.height)));
    this.changeSignatureBox({ id: draft.id, box });
  }
  protected cancelSignature(): void {
    if (this.signatureBusy()) return;
    this.signatureDraft.set(null); this.signatureBlob = undefined; this.signatureTarget = undefined;
    this.signatureNotice.set(''); this.releaseSignatureUrls();
  }
  protected async saveSignature(): Promise<void> {
    const draft = this.signatureDraft(); if (!draft || this.signatureBusy()) return;
    this.signatureBusy.set(true); this.signatureError.set('');
    try {
      const dto = this.signatureTarget
        ? await this.signatureApi.update(this.documentId, this.pageId, this.signatureTarget.id, draft.box, this.signatureTarget.revision)
        : await this.signatureApi.create(this.documentId, this.pageId, this.signatureBlob!, draft.box, this.signatureRequestId);
      if (this.destroyed) return;
      this.signatures.update(signatures => [...signatures.filter(signature => signature.id !== dto.id), { ...dto, localImageUrl: draft.localImageUrl }]);
      this.signatureDraft.set(null); this.signatureBlob = undefined; this.signatureTarget = undefined;
      this.selectedSignatureId.set(dto.id); this.signatureNotice.set('Signature saved. Export a new PDF to include it.'); this.releaseSignatureUrls();
    } catch (error) {
      if (!this.destroyed) this.signatureError.set((error as { status?: number }).status === 409
        ? 'This signature changed elsewhere. Your draft is still here. Reload saved signatures, then review and Save again.'
        : 'Signature could not be saved. Your draft is still here. Try Save again.');
    } finally { if (!this.destroyed) this.signatureBusy.set(false); }
  }
  protected async deleteSignature(): Promise<void> {
    const signature = this.selectedSignature();
    if (!signature || this.signatureBusy() || !window.confirm('Delete this signature from this page?')) return;
    this.signatureBusy.set(true); this.signatureError.set('');
    try {
      await this.signatureApi.delete(this.documentId, this.pageId, signature.id, signature.revision);
      if (this.destroyed) return;
      this.signatures.update(signatures => signatures.filter(candidate => candidate.id !== signature.id));
      this.selectedSignatureId.set(null); this.signatureNotice.set('Signature deleted. Your original scan is unchanged.'); this.releaseSignatureUrls();
    } catch { if (!this.destroyed) this.signatureError.set('Could not delete the signature. Reload saved signatures and try again.'); }
    finally { if (!this.destroyed) this.signatureBusy.set(false); }
  }
  protected async refreshSignatures(): Promise<void> {
    const generation = ++this.signatureGeneration;
    const created: string[] = [];
    try {
      const signatures = await this.signatureApi.list(this.documentId, this.pageId);
      const views = await Promise.all(signatures.map(async signature => {
        const blob = await this.signatureApi.image(this.documentId, this.pageId, signature.id);
        const url = URL.createObjectURL(blob); created.push(url); this.signatureUrls.add(url);
        return { ...signature, localImageUrl: url };
      }));
      if (this.destroyed || generation !== this.signatureGeneration) { for (const url of created) { URL.revokeObjectURL(url); this.signatureUrls.delete(url); } return; }
      this.signatures.set(views);
      if (this.signatureTarget) {
        const current = views.find(signature => signature.id === this.signatureTarget!.id);
        if (current) this.signatureTarget = current;
        else { this.signatureError.set('This saved signature was deleted elsewhere. Cancel this placement to continue.'); return; }
      }
      this.signatureError.set(''); this.releaseSignatureUrls();
    } catch {
      for (const url of created) { URL.revokeObjectURL(url); this.signatureUrls.delete(url); }
      if (!this.destroyed && generation === this.signatureGeneration) this.signatureError.set('Could not load saved signatures. Try Reload saved signatures.');
    }
  }
  private leaveSignatureTool(): boolean {
    if (this.signatureBusy()) return false;
    if (this.signatureDraft() && !window.confirm('Discard your unsaved signature placement?')) return false;
    this.cancelSignature(); this.signatureCreatorOpen.set(false); this.selectedSignatureId.set(null); return true;
  }
  private releaseSignatureUrls(): void {
    const used = new Set(this.signatures().map(signature => signature.localImageUrl));
    if (this.signatureDraft()) used.add(this.signatureDraft()!.localImageUrl);
    for (const url of this.signatureUrls) if (!used.has(url)) { URL.revokeObjectURL(url); this.signatureUrls.delete(url); }
  }

  private async refreshOcr(): Promise<void> {
    clearTimeout(this.timer);
    try {
      const next = await this.api.getPageOcr(this.documentId, this.pageId);
      if (this.destroyed) return;
      this.ocr.set(next);
      this.schedule(next);
    } catch {
      if (!this.destroyed) this.error.set('Could not check text recognition. Try again.');
    }
  }

  private schedule(ocr: PageOcr): void {
    clearTimeout(this.timer);
    if (ocr.state === 'Queued' || ocr.state === 'Processing')
      this.timer = setTimeout(() => void this.refreshOcr(), 3000);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
    if (this.imageUrl()) URL.revokeObjectURL(this.imageUrl());
    this.signatureGeneration++;
    for (const url of this.signatureUrls) URL.revokeObjectURL(url);
    this.signatureUrls.clear();
  }
}
