import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentListComponent } from './document-list.component';

describe('DocumentListComponent', () => {
  let fixture: ComponentFixture<DocumentListComponent>;
  let httpTesting: HttpTestingController;
  let navigations: unknown[][];

  beforeEach(async () => {
    navigations = [];
    await TestBed.configureTestingModule({
      imports: [DocumentListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '/api' },
        {
          provide: Router,
          useValue: {
            navigate: (commands: unknown[]) => {
              navigations.push(commands);
              return Promise.resolve(true);
            },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DocumentListComponent);
    httpTesting = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => httpTesting.verify());

  it('announces that documents are loading', () => {
    const status = fixture.nativeElement.querySelector('[aria-live]') as HTMLElement;

    expect(status.textContent).toContain('Loading documents');
    httpTesting.expectOne('/api/documents').flush([]);
  });

  it('renders an empty state after an empty response', () => {
    httpTesting.expectOne('/api/documents').flush([]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No scans yet');
  });

  it('renders document titles as text instead of trusted HTML', () => {
    httpTesting.expectOne('/api/documents').flush([
      {
        id: '8f1da70a-c734-49f3-8dd6-4ad74ed080b1',
        title: '<img src=x onerror=alert(1)>Application form',
        status: 'Ready',
        pageCount: 2,
        updatedAt: '2026-09-04T02:00:00Z',
      },
    ]);
    fixture.detectChanges();

    const title = fixture.nativeElement.querySelector('[data-document-title]') as HTMLElement;
    expect(title.textContent).toBe('<img src=x onerror=alert(1)>Application form');
    expect(title.querySelector('img')).toBeNull();
  });

  it('shows a safe error without exposing the provider response', () => {
    httpTesting
      .expectOne('/api/documents')
      .flush(
        { detail: 'firebase-token=secret-provider-message' },
        { status: 503, statusText: 'Provider unavailable' },
      );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain("We couldn't load your documents");
    expect(fixture.nativeElement.textContent).not.toContain('secret-provider-message');
  });

  it('provides New Scan as a native button', () => {
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    expect(button.textContent).toContain('New Scan');
    expect(button.type).toBe('button');
    httpTesting.expectOne('/api/documents').flush([]);
  });

  it('opens the secure scan flow from the primary action', () => {
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    button.click();

    expect(navigations).toEqual([['/scan']]);
    httpTesting.expectOne('/api/documents').flush([]);
  });
});
