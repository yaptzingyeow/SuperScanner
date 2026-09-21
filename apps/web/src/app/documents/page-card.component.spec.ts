import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { DocumentPage } from './document.models';
import { PageCardComponent } from './page-card.component';

describe('PageCardComponent', () => {
  const page: DocumentPage = {
    id: 'p1',
    position: 2,
    pageNumber: 2,
    sourceUploadId: 'u1',
    sourcePageIndex: 1,
    state: 'NeedsCrop',
    hasPreview: true,
    hasOriginal: true,
    canCrop: true,
    cropStatus: 'NeedsCrop',
    cropRevision: 1,
    appliedCropRevision: 0,
    previewRevision: 1,
    filter: 'Document',
    appliedFilter: 'BlackAndWhite',
  };

  it('shows the page state, thumbnail label, filter, and accessible move controls', async () => {
    await TestBed.configureTestingModule({
      imports: [PageCardComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(PageCardComponent);
    fixture.componentRef.setInput('page', page);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('thumbnailUrl', 'blob:preview');
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Needs Crop');
    expect(text).toContain('BlackAndWhite');
    expect(fixture.nativeElement.querySelector('img').alt).toBe('Thumbnail of page 2');
    expect(fixture.nativeElement.querySelector('[aria-label="Move page 2 earlier"]')).toBeTruthy();
  });
});
