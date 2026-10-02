import { CdkDragDrop } from '@angular/cdk/drag-drop';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentDetail, DocumentPage } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { PageRailComponent } from './page-rail.component';

describe('PageRailComponent', () => {
  const page = (id: string, position: number, hasPreview = false): DocumentPage => ({
    id, position, pageNumber: position, sourceUploadId: 'u1', sourcePageIndex: position - 1,
    state: 'Ready', hasPreview, hasOriginal: true, canCrop: true, cropStatus: 'Ready',
    cropRevision: 1, appliedCropRevision: 1, previewRevision: 'r1',
  });
  const pages = () => [page('p1', 1), page('p2', 2), page('p3', 3)];
  const detail = (list: DocumentPage[]): DocumentDetail => ({
    id: 'doc-1', title: 'Agreement', status: 'Ready', revision: 8, pageOrderRevision: 8,
    pages: list, imports: [], latestExport: null,
  });

  function setup(list = pages()) {
    const api = {
      reorderPages: vi.fn().mockImplementation(async (_id, request) =>
        detail(request.pageIds.map((id: string, i: number) => page(id, i + 1)))),
      removePage: vi.fn().mockResolvedValue(undefined),
      getDocument: vi.fn().mockResolvedValue(detail([page('p3', 1), page('p1', 2), page('p2', 3)])),
    };
    TestBed.configureTestingModule({
      imports: [PageRailComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' },
        { provide: DocumentsApiService, useValue: api },
      ],
    });
    const fixture = TestBed.createComponent(PageRailComponent);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('pages', list);
    fixture.componentRef.setInput('selectedPageId', 'p1');
    fixture.componentRef.setInput('pageOrderRevision', 7);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance as any, api };
  }

  it('dropping page 3 before page 1 sends the new order with the expected revision', async () => {
    const { component, api, fixture } = setup();
    const changed = vi.fn();
    fixture.componentInstance.pagesChanged.subscribe(changed);
    await component.drop({ previousIndex: 2, currentIndex: 0 } as CdkDragDrop<DocumentPage[]>);
    expect(api.reorderPages).toHaveBeenCalledWith('doc-1', {
      expectedPageOrderRevision: 7, pageIds: ['p3', 'p1', 'p2'],
    });
    expect(changed).toHaveBeenCalledTimes(1);
  });

  it('a 409 reorder restores the server order and shows the conflict message', async () => {
    const { component, api, fixture } = setup();
    const changed = vi.fn();
    fixture.componentInstance.pagesChanged.subscribe(changed);
    api.reorderPages.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await component.drop({ previousIndex: 2, currentIndex: 0 } as CdkDragDrop<DocumentPage[]>);
    fixture.detectChanges();
    expect(api.getDocument).toHaveBeenCalledWith('doc-1');
    expect(changed.mock.calls[0][0].pages.map((p: DocumentPage) => p.id)).toEqual(['p3', 'p1', 'p2']);
    expect(fixture.nativeElement.textContent).toContain(
      'Page order changed in another session. The latest order has been restored.');
  });

  it('remove asks for confirmation before calling removePage', async () => {
    const { component, api } = setup();
    const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false);
    await component.remove(pages()[1]);
    expect(api.removePage).not.toHaveBeenCalled();
    confirm.mockReturnValueOnce(true);
    await component.remove(pages()[1]);
    expect(api.removePage).toHaveBeenCalledWith('doc-1', 'p2');
  });

  it('disables Remove while a reorder is being saved', async () => {
    const { component, api, fixture } = setup();
    let finish!: (value: DocumentDetail) => void;
    api.reorderPages.mockReturnValueOnce(new Promise<DocumentDetail>((resolve) => { finish = resolve; }));
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    const removeButtons = () => [...(fixture.nativeElement as HTMLElement)
      .querySelectorAll<HTMLButtonElement>('button[aria-label^="Remove page"]')];
    expect(removeButtons().every((b) => !b.disabled)).toBe(true);

    const saving = component.drop({ previousIndex: 2, currentIndex: 0 } as CdkDragDrop<DocumentPage[]>);
    fixture.detectChanges();
    expect(removeButtons().every((b) => b.disabled)).toBe(true);
    await component.remove(pages()[1]);
    expect(api.removePage).not.toHaveBeenCalled();

    finish(detail([page('p3', 1), page('p1', 2), page('p2', 3)]));
    await saving;
    fixture.detectChanges();
    expect(removeButtons().every((b) => !b.disabled)).toBe(true);
    confirm.mockRestore();
  });

  it('adding pages emits the new upload ids', () => {
    const { component, fixture } = setup();
    const added = vi.fn();
    fixture.componentInstance.pagesAdded.subscribe(added);
    component.onAdded(['up-1', 'up-2']);
    expect(added).toHaveBeenCalledWith(['up-1', 'up-2']);
  });

  it('selects pages, marks the selection and offers move buttons', () => {
    const { fixture } = setup();
    const selected = vi.fn();
    fixture.componentInstance.selectPage.subscribe(selected);
    const el: HTMLElement = fixture.nativeElement;
    const thumbs = el.querySelectorAll<HTMLButtonElement>('button.thumb');
    expect(thumbs[0].getAttribute('aria-pressed')).toBe('true');
    thumbs[1].click();
    expect(selected).toHaveBeenCalledWith('p2');
    expect(el.querySelectorAll('button[aria-label^="Move page"]').length).toBeGreaterThan(0);
    expect(el.textContent).toContain('Add pages');
  });

  it('loads thumbnails and revokes them on destroy', async () => {
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:t');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
    const { fixture } = setup([page('p1', 1, true)]);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/documents/doc-1/pages/p1/thumbnail').flush(new Blob(['x']));
    await fixture.whenStable();
    expect(create).toHaveBeenCalled();
    fixture.destroy();
    expect(revoke).toHaveBeenCalledWith('blob:t');
  });
});
