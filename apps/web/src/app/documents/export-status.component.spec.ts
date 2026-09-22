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
    previewRevision: 0,
  });
  const exported = (state: string, isOutdated = false): DocumentExport => ({
    id: 'export-1',
    state,
    documentRevision: 4,
    readyPageCount: 3,
    excludedPageCount: 2,
    createdAt: '2026-09-21T00:00:00Z',
    expiresAt: '2026-09-22T00:00:00Z',
    isOutdated,
  });

  function setup(pages: DocumentPage[], initialExport?: DocumentExport) {
    const api = {
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
    expect(api.createExport).toHaveBeenCalledWith('doc-1');
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
});
