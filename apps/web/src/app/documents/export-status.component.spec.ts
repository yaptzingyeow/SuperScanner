import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { DocumentExport, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { ExportStatusComponent } from './export-status.component';

describe('ExportStatusComponent', () => {
  const page = (id: string, state: string): DocumentPage => ({
    id,
    state,
    position: 1,
    pageNumber: 1,
    sourceUploadId: 'u1',
    sourcePageIndex: 0,
    hasPreview: false,
    hasOriginal: true,
    canCrop: true,
    cropStatus: state,
    cropRevision: 1,
    appliedCropRevision: 1,
    previewRevision: 'crop-0',
  });
  const exported = (
    state: string,
    isOutdated = false,
    searchablePageCount = state === 'Ready' ? 3 : 0,
    searchability: DocumentExport['searchability'] = state === 'Ready' ? 'Searchable' : 'ImageOnly',
  ): DocumentExport => ({
    id: 'export-1',
    pageLayout: 'Original',
    state,
    documentRevision: 4,
    readyPageCount: 3,
    excludedPageCount: 2,
    searchablePageCount,
    searchability,
    createdAt: '2026-09-21T00:00:00Z',
    expiresAt: '2026-09-22T00:00:00Z',
    isOutdated,
  });

  function setup(pages: DocumentPage[], initialExport?: DocumentExport) {
    const api = {
      getExportPreview: vi.fn().mockResolvedValue({
        readyPageCount: pages.filter((item) => item.state === 'Ready').length,
        excludedPageCount: pages.filter((item) => item.state !== 'Ready').length,
        searchablePageCount: 0,
        searchability: 'ImageOnly',
      }),
      createExport: vi.fn().mockResolvedValue(exported('Queued')),
      getExport: vi.fn().mockResolvedValue(exported('Ready')),
      downloadExport: vi.fn().mockResolvedValue(new Blob(['pdf'], { type: 'application/pdf' })),
    };
    TestBed.configureTestingModule({
      imports: [ExportStatusComponent],
      providers: [{ provide: DocumentsApiService, useValue: api }],
    });
    const fixture = TestBed.createComponent(ExportStatusComponent);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('documentTitle', 'My Agreement');
    fixture.componentRef.setInput('pages', pages);
    fixture.componentRef.setInput('initialExport', initialExport ?? null);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance as any, api };
  }

  afterEach(() => vi.restoreAllMocks());

  it('shows server-canonical searchable eligibility before export', async () => {
    const { fixture, component, api } = setup([
      page('p1', 'Ready'),
      page('p2', 'Ready'),
      page('p3', 'Ready'),
      page('p4', 'Failed'),
    ]);
    api.getExportPreview.mockResolvedValue({
      readyPageCount: 3,
      excludedPageCount: 1,
      searchablePageCount: 2,
      searchability: 'PartiallySearchable',
    });

    await component.loadPreview();
    fixture.detectChanges();

    expect(api.getExportPreview).toHaveBeenCalledWith('doc-1');
    expect(fixture.nativeElement.querySelector('[data-testid="export-summary"]').textContent)
      .toContain('3 pages · 2 searchable');
  });

  it('shows ready and excluded counts and disables export with no ready page', () => {
    const { fixture } = setup([page('p1', 'Processing'), page('p2', 'Failed')]);
    expect(
      fixture.nativeElement.querySelector('[data-testid="export-summary"]').textContent,
    ).toContain('0 ready pages will be included. 2 pages will be excluded.');
    expect(fixture.nativeElement.querySelector('.primary').disabled).toBe(true);
  });

  it('confirms counts and creates an export', async () => {
    const { component, api } = setup([
      page('p1', 'Ready'),
      page('p2', 'Ready'),
      page('p3', 'Ready'),
      page('p4', 'Failed'),
      page('p5', 'NeedsCrop'),
    ]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await component.generate();
    expect(window.confirm).toHaveBeenCalledWith(
      '3 ready pages will be included. 2 pages will be excluded. Generate the PDF?',
    );
    expect(api.createExport).toHaveBeenCalledWith('doc-1', 'Original', false);
  });

  it('adds the optional transparent OCR layer only when selected', async () => {
    const { fixture, component, api } = setup([page('p1', 'Ready')]);
    const checkbox = fixture.nativeElement.querySelector('#include-searchable-text') as HTMLInputElement;
    expect(checkbox.checked).toBe(false);
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change'));
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await component.generate();

    expect(api.createExport).toHaveBeenCalledWith('doc-1', 'Original', true);
    expect(fixture.nativeElement.textContent).toContain('does not change how the page looks');
  });

  it('sends A4 when chosen and explains fitting before export', async () => {
    const { fixture, component, api } = setup([page('p1', 'Ready')]);
    const select = fixture.nativeElement.querySelector('#pdf-page-layout') as HTMLSelectElement;
    select.value = 'A4';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('white margins');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await component.generate();
    expect(api.createExport).toHaveBeenCalledWith('doc-1', 'A4', false);
  });

  it('offers a new PDF when the selected page size differs from a ready export', () => {
    const item = { ...exported('Ready'), pageLayout: 'Original' as const };
    const { fixture } = setup([page('p1', 'Ready')], item);
    const select = fixture.nativeElement.querySelector('#pdf-page-layout') as HTMLSelectElement;
    select.value = 'A4';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Generate A4 PDF');
  });

  it('keeps an outdated PDF downloadable and offers an updated export', async () => {
    const { fixture, component, api } = setup([page('p1', 'Ready')], exported('Ready', true));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Generate updated PDF');
    const createObjectURL = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:pdf');
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    await component.download();
    expect(api.downloadExport).toHaveBeenCalledWith('doc-1', 'export-1');
    expect(createObjectURL).toHaveBeenCalled();
  });

  it('shows the exported page and searchable-page counts', () => {
    const item = { ...exported('Ready', false, 3, 'PartiallySearchable'), readyPageCount: 4 };
    const { fixture } = setup([page('p1', 'Ready')], item);

    expect(fixture.nativeElement.querySelector('[data-testid="searchability-summary"]').textContent)
      .toContain('4 pages · 3 searchable');
  });

  it.each([
    ['ImageOnly', 0, 'Image-only PDF. Text cannot be searched or selected.'],
    ['PartiallySearchable', 2, '2 of 3 pages have searchable text.'],
    ['Searchable', 3, 'All 3 pages have searchable text.'],
  ] as const)('shows completed %s copy', (searchability, count, expected) => {
    const { fixture } = setup(
      [page('p1', 'Ready')],
      exported('Ready', false, count, searchability),
    );

    expect(fixture.nativeElement.textContent).toContain(expected);
  });

  it('announces searchability when polling reaches Ready', async () => {
    const { component, api, fixture } = setup(
      [page('p1', 'Ready')],
      exported('Processing', false, 0, 'ImageOnly'),
    );
    api.getExport.mockResolvedValue(exported('Ready', false, 2, 'PartiallySearchable'));

    await component.poll('export-1');
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[aria-live="polite"]').textContent)
      .toContain('PDF is ready. 2 of 3 pages are searchable.');
  });
});
