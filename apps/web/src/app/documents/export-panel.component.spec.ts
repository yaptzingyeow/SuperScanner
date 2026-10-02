import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { DocumentExport, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { ExportPanelComponent } from './export-panel.component';

describe('ExportPanelComponent', () => {
  const page = (id: string): DocumentPage => ({
    id, position: 1, pageNumber: 1, sourceUploadId: 'u1', sourcePageIndex: 0, state: 'Ready',
    hasPreview: true, hasOriginal: true, canCrop: true, cropStatus: 'Ready', cropRevision: 1,
    appliedCropRevision: 1, previewRevision: 'r1',
  });
  const exported = (state: string, isOutdated = false): DocumentExport => ({
    id: 'e1', pageLayout: 'Original', state, documentRevision: 4, readyPageCount: 1, excludedPageCount: 0,
    searchablePageCount: 0, searchability: 'ImageOnly', createdAt: '2026-10-01T00:00:00Z',
    expiresAt: '2026-10-02T00:00:00Z', isOutdated,
  });

  function setup(kind: 'pdf' | 'print' | 'original', latest: DocumentExport | null = null) {
    const api = {
      getExportPreview: vi.fn().mockResolvedValue({
        readyPageCount: 1, excludedPageCount: 0, searchablePageCount: 0, searchability: 'ImageOnly' }),
      createExport: vi.fn().mockResolvedValue(exported('Queued')),
      getExport: vi.fn().mockResolvedValue(exported('Ready')),
      downloadExport: vi.fn().mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' })),
      downloadPageOriginal: vi.fn().mockResolvedValue(new Blob(['png'], { type: 'image/png' })),
    };
    TestBed.configureTestingModule({
      imports: [ExportPanelComponent],
      providers: [{ provide: DocumentsApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(ExportPanelComponent);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('documentTitle', 'Agreement');
    fixture.componentRef.setInput('pages', [page('p1')]);
    fixture.componentRef.setInput('selectedPageId', 'p1');
    fixture.componentRef.setInput('latestExport', latest);
    fixture.componentRef.setInput('kind', kind);
    fixture.detectChanges();
    const print = vi.fn().mockResolvedValue(undefined);
    (fixture.componentInstance as any).printFn = print;
    return { fixture, api, print, el: fixture.nativeElement as HTMLElement };
  }
  const button = (el: HTMLElement, text: string) =>
    [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement;

  afterEach(() => vi.restoreAllMocks());

  it('PDF export sends the chosen layout and searchable flag', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const { fixture, api, el } = setup('pdf');
    await fixture.whenStable();
    fixture.detectChanges();
    const select = el.querySelector('select') as HTMLSelectElement;
    select.value = 'A4';
    select.dispatchEvent(new Event('change'));
    const box = el.querySelector('input[type=checkbox]') as HTMLInputElement;
    box.click();
    fixture.detectChanges();
    button(el, 'Generate PDF').click();
    await fixture.whenStable();
    expect(api.createExport).toHaveBeenCalledWith('doc-1', 'A4', true);
  });

  it('Print builds or reuses a Ready export and calls printPdf with its blob', async () => {
    const reuse = setup('print', exported('Ready'));
    const print = reuse.print;
    button(reuse.el, 'Print').click();
    await reuse.fixture.whenStable();
    expect(reuse.api.createExport).not.toHaveBeenCalled();
    expect(print).toHaveBeenCalledTimes(1);
    expect((print.mock.calls[0][0] as Blob).type).toBe('application/pdf');
    TestBed.resetTestingModule();

    const build = setup('print', exported('Ready', true));
    build.api.getExport.mockResolvedValue(exported('Ready', true));
    build.api.createExport.mockResolvedValue(exported('Ready'));
    button(build.el, 'Print').click();
    await build.fixture.whenStable();
    expect(build.api.createExport).toHaveBeenCalledWith('doc-1', 'Original', false);
    expect(build.api.downloadExport).toHaveBeenCalledWith('doc-1', 'e1');
    expect(build.print).toHaveBeenCalledTimes(1);
  });

  it('print re-checks the latest export and builds a new one when it became outdated', async () => {
    const { fixture, api, print, el } = setup('print', exported('Ready'));
    api.getExport.mockResolvedValue(exported('Ready', true));
    api.createExport.mockResolvedValue({ ...exported('Ready'), id: 'e2' });
    button(el, 'Print').click();
    await fixture.whenStable();
    expect(api.getExport).toHaveBeenCalledWith('doc-1', 'e1');
    expect(api.createExport).toHaveBeenCalledWith('doc-1', 'Original', false);
    expect(api.downloadExport).toHaveBeenCalledWith('doc-1', 'e2');
    expect(print).toHaveBeenCalledTimes(1);
  });

  it('print does not reuse an A4 export; it builds an Original-size one', async () => {
    const a4: DocumentExport = { ...exported('Ready'), pageLayout: 'A4' };
    const { fixture, api, print, el } = setup('print', a4);
    api.createExport.mockResolvedValue({ ...exported('Ready'), id: 'e2' });
    button(el, 'Print').click();
    await fixture.whenStable();
    expect(api.createExport).toHaveBeenCalledWith('doc-1', 'Original', false);
    expect(api.downloadExport).toHaveBeenCalledWith('doc-1', 'e2');
    expect(api.downloadExport).not.toHaveBeenCalledWith('doc-1', 'e1');
    expect(print).toHaveBeenCalledTimes(1);
  });

  it('disables the original download when the page has no original', () => {
    const { fixture, el } = setup('original');
    fixture.componentRef.setInput('pages', [{ ...page('p1'), hasOriginal: false }]);
    fixture.detectChanges();
    expect(button(el, 'Download original').disabled).toBe(true);
    expect(el.textContent).toContain('no longer available');
  });

  it("Original file downloads the selected page's original", async () => {
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:o');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    const { fixture, api, el } = setup('original');
    button(el, 'Download original').click();
    await fixture.whenStable();
    expect(api.downloadPageOriginal).toHaveBeenCalledWith('doc-1', 'p1');
    expect(create).toHaveBeenCalled();
    expect(click).toHaveBeenCalled();
  });
});
