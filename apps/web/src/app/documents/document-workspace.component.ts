import { PlanService } from '../plans/plan.service';
import { limitMessage, planLimitOf } from '../plans/limit-message';
import { HttpClient } from '@angular/common/http';
import {
  Component, ElementRef, effect, untracked, OnDestroy, OnInit, afterNextRender, computed, inject, Injector, signal, viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom, take } from 'rxjs';
import { AuthService } from '../core/auth/auth.service';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentDetail, DocumentExport, PageOcr } from './document.models';
import { searchPages } from './document-search';
import { DocumentsApiService } from './documents-api.service';
import { EditPanelComponent, EditToolbarComponent, openEditTool } from './edit-toolbar.component';
import { EXPORT_KINDS, ExportKind, ExportPanelComponent } from './export-panel.component';
import { EditTool, EditToolId, opensInWorkspace } from './edit-tools';
import { PageTextEditorComponent } from './page-text-editor.component';
import { IconComponent, IconName } from './ui-icon.component';
import { PageRailComponent } from './page-rail.component';
import { PageViewerComponent, ViewerLayout, clampZoom } from './page-viewer.component';

export type WorkspaceTab = 'view' | 'edit' | 'export';
export type PointerMode = 'select' | 'pan';

const TABS: readonly { id: WorkspaceTab; label: string; icon: IconName }[] = [
  { id: 'view', label: 'View', icon: 'view' },
  { id: 'edit', label: 'Edit', icon: 'edit' },
  { id: 'export', label: 'Export', icon: 'export' },
];
const EXPORT_ICONS: Record<ExportKind, IconName> = { pdf: 'pdf', print: 'print', original: 'original' };
const ZOOM_STEP = 10;
const POLL_MS = 3000;
const BUSY_DOCUMENT = ['Uploading', 'Processing'];
const BUSY_PAGE = ['Importing', 'Processing'];
const OCR_POLL_MS = 2000;
const OCR_PENDING = ['Queued', 'Processing'];

function asTab(value: string | null): WorkspaceTab {
  return value === 'edit' || value === 'export' ? value : 'view';
}

@Component({
  selector: 'app-document-workspace',
  standalone: true,
  imports: [RouterLink, PageRailComponent, PageViewerComponent, EditToolbarComponent, EditPanelComponent, ExportPanelComponent, PageTextEditorComponent, IconComponent],
  templateUrl: './document-workspace.component.html',
  styleUrl: './document-workspace.component.scss',
})
export class DocumentWorkspaceComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly api = inject(DocumentsApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly base = inject(API_BASE_URL).replace(/\/+$/, '');
  private readonly injector = inject(Injector);
  private readonly auth = inject(AuthService);
  private readonly viewer = viewChild(PageViewerComponent);
  private readonly searchBox = viewChild<ElementRef<HTMLInputElement>>('searchBox');
  private readonly pageEditor = viewChild(PageTextEditorComponent);

  protected readonly id = this.route.snapshot.paramMap.get('documentId') ?? '';
  protected readonly tabs = TABS;
  protected readonly exportKinds = EXPORT_KINDS;
  protected readonly exportKind = signal<ExportKind>('pdf');
  protected readonly exportIcons = EXPORT_ICONS;
  protected readonly layouts: readonly { id: ViewerLayout; label: string; icon: IconName; isNew?: boolean }[] = [
    { id: 'continuous', label: 'Continuous', icon: 'continuous' },
    { id: 'single', label: 'Single page', icon: 'single' },
    { id: 'double', label: 'Two pages', icon: 'double', isNew: true },
  ];

  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });
  readonly tab = computed<WorkspaceTab>(() => asTab(this.query().get('tab')));
  private readonly pageParam = computed(() => this.query().get('page'));

  protected readonly document = signal<DocumentDetail | null>(null);
  protected readonly error = signal('');
  protected readonly retrying = signal(false);
  protected readonly layout = signal<ViewerLayout>('continuous');
  protected readonly pointer = signal<PointerMode>('select');
  protected readonly zoom = signal(100);
  protected readonly searchOpen = signal(false);
  protected readonly editTool = signal<EditToolId>('crop');
  /** The page editor open in the Edit tab: the tool it was started with, on which page. */
  protected readonly inlineEditor = signal<{ tool: EditTool; pageId: string; launch: number } | null>(null);
  private launches = 0;
  /** A guest chose a tool that needs an account: ask them to log in first. */
  protected readonly loginPrompt = signal<EditTool | null>(null);
  /** Recreates the embedded editor for each page and each fresh start. */
  protected readonly inlineKey = computed(() => {
    const editor = this.inlineEditor();
    return editor ? `${this.selectedPageId()}:${editor.launch}` : '';
  });
  protected readonly inlineTool = computed(() => {
    const editor = this.inlineEditor();
    return editor && editor.pageId === this.selectedPageId() ? editor.tool.query ?? null : null;
  });
  protected readonly searchQuery = signal('');
  protected readonly searchIndex = signal(0);
  private readonly ocrByPage = signal<ReadonlyMap<string, PageOcr | null>>(new Map());
  private readonly ocrKeys = new Map<string, string>();
  protected readonly searchResult = computed(() => {
    const pages = this.document()?.pages ?? [];
    const cache = this.ocrByPage();
    return searchPages(this.searchQuery(), new Map(pages.map((p) => [p.id, cache.get(p.id) ?? null])));
  });
  protected readonly searchActive = computed(() => this.searchQuery().trim().length >= 2);
  protected readonly effectiveIndex = computed(() =>
    Math.max(0, Math.min(this.searchIndex(), this.searchResult().hits.length - 1)));
  protected readonly currentHit = computed(() => this.searchResult().hits[this.effectiveIndex()] ?? null);
  protected readonly searchCountLabel = computed(() => {
    if (!this.searchActive()) return '';
    const total = this.searchResult().hits.length;
    return total ? (this.effectiveIndex() + 1) + ' of ' + total : 'No results';
  });
  private selectedForQuery = '';
  protected readonly highlights = computed<Record<string, string[]>>(() => {
    const hit = this.searchOpen() && this.tab() === 'edit' ? this.currentHit() : null;
    return hit ? { [hit.pageId]: hit.wordIds } : {};
  });
  /** Unrecognized pages search can recognize now (not already queued or running). */
  protected readonly recognizable = computed(() => {
    const cache = this.ocrByPage();
    return this.searchResult().unrecognizedPageIds.filter((id) => {
      const ocr = cache.get(id);
      return !!ocr && (ocr.state === 'NotRequested' || (ocr.state === 'Failed' && ocr.canRetry));
    });
  });
  protected readonly recognizing = computed(() => {
    const cache = this.ocrByPage();
    return [...cache.values()].some((ocr) => !!ocr && OCR_PENDING.includes(ocr.state));
  });
  protected readonly recognizeError = signal('');
  private readonly plans = inject(PlanService);
  private ocrTimer?: ReturnType<typeof setTimeout>;
  protected readonly unrecognizedLabel = computed(() => {
    const pages = this.document()?.pages ?? [];
    const ids = new Set(this.searchResult().unrecognizedPageIds);
    const names = pages.filter((p) => ids.has(p.id)).map((p) => 'Page ' + p.position);
    return names.length ? names.join(', ') + ' not recognized yet' : '';
  });

  readonly selectedPageId = computed(() => {
    const pages = this.document()?.pages ?? [];
    const requested = this.pageParam();
    return pages.find((p) => p.id === requested)?.id ?? pages[0]?.id ?? null;
  });
  protected readonly hasFailedPages = computed(() => {
    const doc = this.document();
    return !!doc && (doc.status === 'Failed' || doc.pages.some((p) => p.state === 'Failed'));
  });

  private timer?: ReturnType<typeof setTimeout>;
  private destroyed = false;
  /** Bumped by every load and local document change; an older load's response is then ignored. */
  private generation = 0;

  constructor() {
    // Select the first hit's page once per query, as soon as hits exist (also when OCR arrives late).
    effect(() => {
      const first = this.searchResult().hits[0];
      const query = this.searchQuery();
      if (!this.searchOpen() || !first || this.selectedForQuery === query) return;
      this.selectedForQuery = query;
      untracked(() => this.selectPage(first.pageId));
    });
  }

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    const generation = ++this.generation;
    clearTimeout(this.timer);
    this.timer = undefined;
    this.error.set('');
    try {
      const doc = await this.api.getDocument(this.id);
      if (this.destroyed || generation !== this.generation) return;
      this.setDocument(doc);
      this.schedulePoll(doc);
    } catch {
      if (!this.destroyed && generation === this.generation)
        this.error.set('We could not load this document. Please try again.');
    }
  }

  private schedulePoll(doc: DocumentDetail): void {
    clearTimeout(this.timer);
    this.timer = undefined;
    if (this.isProcessing(doc)) this.timer = setTimeout(() => void this.load(), POLL_MS);
  }

  /** False when the user keeps unsaved changes in the embedded page editor. */
  canLeave(): boolean {
    return this.pageEditor()?.canLeave() ?? true;
  }

  protected selectTab(tab: WorkspaceTab): void {
    if (tab === this.tab()) return;
    if (!this.canLeave()) return;
    this.inlineEditor.set(null);
    this.loginPrompt.set(null);
    this.searchOpen.set(false);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      queryParamsHandling: 'merge',
    });
  }

  protected selectPage(pageId: string): void {
    if (pageId === this.selectedPageId()) return;
    if (!this.canLeave()) return;
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { page: pageId },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  protected async startEditTool(tool: EditTool): Promise<void> {
    const pageId = this.selectedPageId();
    if (!pageId) return;
    this.editTool.set(tool.id);
    if (!opensInWorkspace(tool)) {
      void openEditTool(this.router, this.id, pageId, tool);
      return;
    }
    const user = await firstValueFrom(this.auth.user$.pipe(take(1)));
    if (this.destroyed) return;
    if (!user || user.isAnonymous) {
      this.inlineEditor.set(null);
      this.loginPrompt.set(tool);
      return;
    }
    this.loginPrompt.set(null);
    const editor = this.pageEditor();
    if (editor && this.inlineEditor()) {
      // already open on this page: start the tool in place
      editor.startTool(tool.query ?? null);
      return;
    }
    this.inlineEditor.set({ tool, pageId, launch: ++this.launches });
  }

  protected closeInlineEditor(): void {
    this.inlineEditor.set(null);
  }

  /** Logging in keeps this guest's documents (the account is linked) and returns here. */
  protected logIn(): void {
    void this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
  }

  protected selectedPageNumber(): number | null {
    return this.document()?.pages.find((p) => p.id === this.selectedPageId())?.position ?? null;
  }

  protected pagesChanged(doc: DocumentDetail): void {
    // A load still in flight was started before this change; its response must not undo it.
    this.generation++;
    this.setDocument(doc);
    this.schedulePoll(doc);
  }

  /** Keeps the running export on the document so a re-created export panel starts from it. */
  protected exportChanged(item: DocumentExport): void {
    this.document.update((doc) => doc ? { ...doc, latestExport: item } : doc);
  }

  protected pagesAdded(uploadIds: string[]): void {
    void this.router.navigate(['/documents', this.id, 'import'], {
      queryParams: { uploads: uploadIds.join(',') },
    });
  }

  protected zoomBy(delta: number): void {
    this.zoom.set(clampZoom(this.zoom() + delta));
  }

  protected readonly zoomStep = ZOOM_STEP;

  protected fitWidth(): void {
    this.viewer()?.fitWidth();
  }

  protected toggleSearch(): void {
    if (!this.searchOpen() && this.inlineEditor()) {
      // Results are highlighted on the pages, so the page editor makes way for them.
      if (!this.canLeave()) return;
      this.inlineEditor.set(null);
      this.loginPrompt.set(null);
    }
    this.searchOpen.update((open) => !open);
    if (this.searchOpen()) {
      void this.ensureOcr();
      afterNextRender(() => this.searchBox()?.nativeElement.focus(), { injector: this.injector });
    }
  }

  protected onSearchInput(value: string): void {
    this.searchQuery.set(value);
    this.selectedForQuery = '';
    this.searchIndex.set(0);
    void this.ensureOcr();
  }

  protected stepSearch(delta: number): void {
    const hits = this.searchResult().hits;
    if (hits.length === 0) return;
    const next = (this.effectiveIndex() + delta + hits.length) % hits.length;
    this.searchIndex.set(next);
    this.selectPage(hits[next].pageId);
  }

  /** Recognizes, on request, the pages search could not read yet (OCR is never automatic). */
  protected async recognizeForSearch(): Promise<void> {
    const ids = this.recognizable();
    if (ids.length === 0) return;
    this.recognizeError.set('');
    const cache = this.ocrByPage();
    let failed = false;
    let limit = '';
    await Promise.all(ids.map(async (id) => {
      try {
        const next = await this.api.requestPageOcr(this.id, id, cache.get(id)?.state === 'Failed');
        if (!this.destroyed) this.ocrByPage.update((current) => new Map(current).set(id, next));
      } catch (error) {
        failed = true;
        const problem = planLimitOf(error);
        if (problem) limit = limitMessage(problem);
      }
    }));
    void this.plans.refresh();
    if (this.destroyed) return;
    if (failed) this.recognizeError.set(limit || 'Some pages could not start recognition. Try again.');
    this.pollOcr();
  }

  private pollOcr(): void {
    clearTimeout(this.ocrTimer);
    const pending = [...this.ocrByPage()].filter(([, ocr]) => !!ocr && OCR_PENDING.includes(ocr.state)).map(([id]) => id);
    if (pending.length === 0 || this.destroyed) return;
    this.ocrTimer = setTimeout(async () => {
      await Promise.all(pending.map(async (id) => {
        try {
          const next = await this.api.getPageOcr(this.id, id);
          if (!this.destroyed) this.ocrByPage.update((current) => new Map(current).set(id, next));
        } catch { /* keep the last state and try again */ }
      }));
      this.pollOcr();
    }, OCR_POLL_MS);
  }

  /** Fetches OCR once per page per preview revision; the text is never logged. */
  private async ensureOcr(): Promise<void> {
    const pages = this.document()?.pages ?? [];
    await Promise.all(pages.map(async (page) => {
      if (this.ocrKeys.get(page.id) === page.previewRevision) return;
      this.ocrKeys.set(page.id, page.previewRevision);
      let ocr: PageOcr | null = null;
      try {
        ocr = await this.api.getPageOcr(this.id, page.id);
      } catch {
        ocr = null;
      }
      if (this.destroyed) return;
      this.ocrByPage.update((current) => new Map(current).set(page.id, ocr));
    }));
  }

  async retry(): Promise<void> {
    this.retrying.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.http.post(`${this.base}/documents/${this.id}/retry-preview`, {}));
      await this.load();
    } catch {
      this.error.set('Could not restart processing. Please try again.');
    } finally {
      this.retrying.set(false);
    }
  }

  private isProcessing(doc: DocumentDetail): boolean {
    return BUSY_DOCUMENT.includes(doc.status) || doc.pages.some((p) => BUSY_PAGE.includes(p.state));
  }

  private setDocument(doc: DocumentDetail): void {
    this.document.set({
      ...doc,
      pages: [...doc.pages]
        .sort((a, b) => a.position - b.position)
        .map((page, index) => ({ ...page, position: index + 1, pageNumber: index + 1 })),
    });
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    clearTimeout(this.timer);
    clearTimeout(this.ocrTimer);
  }
}
