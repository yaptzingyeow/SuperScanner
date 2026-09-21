import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentDetail, DocumentPage } from './document.models';
import { DocumentDetailComponent } from './document-detail.component';
import { DocumentsApiService } from './documents-api.service';

describe('DocumentDetailComponent organizer', () => {
  const page = (id: string, position: number): DocumentPage => ({
    id,
    position,
    pageNumber: position,
    sourceUploadId: 'u1',
    sourcePageIndex: position - 1,
    state: 'Ready',
    hasPreview: false,
    hasOriginal: true,
    canCrop: true,
    cropStatus: 'Ready',
    cropRevision: 1,
    appliedCropRevision: 1,
    previewRevision: 0,
  });
  const detail = (
    pages = [page('p1', 1), page('p2', 2), page('p3', 3), page('p4', 4)],
  ): DocumentDetail => ({
    id: 'doc-1',
    title: 'Agreement',
    status: 'Ready',
    revision: 8,
    pageOrderRevision: 7,
    pages,
    imports: [],
    latestExport: null,
  });

  function setup(document = detail()) {
    const api = {
      getDocument: vi.fn().mockResolvedValue(document),
      reorderPages: vi
        .fn()
        .mockImplementation(async (_id, request) =>
          detail(request.pageIds.map((id: string, index: number) => page(id, index + 1))),
        ),
      removePage: vi.fn().mockResolvedValue(undefined),
    };
    TestBed.configureTestingModule({
      imports: [DocumentDetailComponent],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: DocumentsApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: { get: () => 'doc-1' } } } },
      ],
    });
    const fixture = TestBed.createComponent(DocumentDetailComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance as any, api };
  }

  it('persists the complete optimistic order after drag and keyboard moves', async () => {
    const { fixture, component, api } = setup();
    await fixture.whenStable();
    await component.drop({ previousIndex: 3, currentIndex: 1 } as CdkDragDrop<DocumentPage[]>);
    expect(api.reorderPages).toHaveBeenCalledWith('doc-1', {
      expectedPageOrderRevision: 7,
      pageIds: ['p1', 'p4', 'p2', 'p3'],
    });
    await component.move('p3', -1);
    expect(api.reorderPages).toHaveBeenLastCalledWith('doc-1', {
      expectedPageOrderRevision: 7,
      pageIds: ['p1', 'p4', 'p3', 'p2'],
    });
  });

  it('confirms removal and renders the empty document state', async () => {
    const { fixture, component, api } = setup(detail([]));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('This document has no pages');
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await component.remove(page('p7', 7));
    expect(api.removePage).toHaveBeenCalledWith('doc-1', 'p7');
    expect(window.confirm).toHaveBeenCalledWith('Remove page 7 from this document?');
  });

  it('reloads and announces when another session changed the order', async () => {
    const { fixture, component, api } = setup();
    await fixture.whenStable();
    api.reorderPages.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));

    await component.drop({ previousIndex: 3, currentIndex: 1 } as CdkDragDrop<DocumentPage[]>);

    expect(api.getDocument).toHaveBeenCalledTimes(2);
    expect(component.announcement()).toBe(
      'Page order changed in another session. The latest order has been restored.',
    );
  });
});
