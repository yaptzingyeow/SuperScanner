import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { API_BASE_URL } from '../core/api/security.interceptor';
import { DocumentListComponent } from './document-list.component';

describe('DocumentListComponent', () => {
  let fixture: ComponentFixture<DocumentListComponent>;
  let httpTesting: HttpTestingController;
  let navigations: (readonly unknown[])[];

  beforeEach(async () => {
    navigations = [];
    await TestBed.configureTestingModule({
      imports: [DocumentListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api' },
      ],
    }).compileComponents();

    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation((commands) => {
      navigations.push(commands);
      return Promise.resolve(true);
    });
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

  it('document card shows days until deletion', () => {
    const inDays = (days: number) => new Date(Date.now() + days * 86_400_000 - 60_000).toISOString();
    httpTesting.expectOne('/api/documents').flush([
      { id: 'a', title: 'Kept', status: 'Ready', pageCount: 1, updatedAt: '2026-10-01T00:00:00Z', expiresAt: null },
      { id: 'b', title: 'Soon', status: 'Ready', pageCount: 1, updatedAt: '2026-10-01T00:00:00Z', expiresAt: inDays(3) },
    ]);
    fixture.detectChanges();
    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('Deletes in 3 days');
    expect(text).not.toContain('within a day');

    fixture.componentInstance['documents'].set([
      { id: 'c', title: 'Last', status: 'Ready', pageCount: 1, updatedAt: '2026-10-01T00:00:00Z', expiresAt: inDays(1) },
    ]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Deletes in 1 day');
    expect((fixture.nativeElement as HTMLElement).querySelector('[data-expiry-banner]')?.textContent).toContain('within a day');
  });

  it('opens Recently deleted on demand and restores a document into the list', async () => {
    httpTesting.expectOne('/api/documents').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    httpTesting.expectNone('/api/documents/bin');

    (el.querySelector('[data-open-bin]') as HTMLButtonElement).click();
    httpTesting.expectOne('/api/documents/bin').flush([
      { id: 'x', title: 'Old lease', deletedAt: '2026-10-01T00:00:00Z', purgeAfter: '2026-10-31T00:00:00Z' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-bin]')?.textContent).toContain('Old lease');

    (el.querySelector('[data-restore]') as HTMLButtonElement).click();
    httpTesting.expectOne({ method: 'POST', url: '/api/documents/x/restore' })
      .flush({ id: 'x', title: 'Old lease', status: 'Ready', pageCount: 2, updatedAt: '2026-10-05T00:00:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-bin]')?.textContent).not.toContain('Old lease');
    expect(el.querySelector('[data-document-title]')?.textContent).toBe('Old lease');
  });

  it('explains when restoring would go over the plan document limit', async () => {
    httpTesting.expectOne('/api/documents').flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('[data-open-bin]') as HTMLButtonElement).click();
    httpTesting.expectOne('/api/documents/bin').flush([
      { id: 'x', title: 'Old lease', deletedAt: '2026-10-01T00:00:00Z', purgeAfter: '2026-10-31T00:00:00Z' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();
    (el.querySelector('[data-restore]') as HTMLButtonElement).click();
    httpTesting.expectOne({ method: 'POST', url: '/api/documents/x/restore' })
      .flush({ code: 'plan_limit_reached', kind: 'documents' }, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('document limit');
  });

  it('deletes after an in-card confirmation and renames inline, without browser pop-ups', async () => {
    httpTesting.expectOne('/api/documents').flush([
      { id: 'a', title: 'IMG_9685', status: 'Ready', pageCount: 1, updatedAt: '2026-10-01T00:00:00Z' },
      { id: 'b', title: 'Lease', status: 'Ready', pageCount: 2, updatedAt: '2026-10-01T00:00:00Z' },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const confirm = vi.spyOn(window, 'confirm');
    const prompt = vi.spyOn(window, 'prompt');

    (el.querySelectorAll('[data-delete-document]')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    httpTesting.expectNone({ method: 'DELETE' });
    (el.querySelector('[data-keep-document]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('[data-confirm-delete]')).toBeNull();
    (el.querySelectorAll('[data-delete-document]')[1] as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelector('[data-confirm-delete]') as HTMLButtonElement).click();
    httpTesting.expectOne({ method: 'DELETE', url: '/api/documents/b' }).flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Lease');

    (el.querySelector('[data-rename-document]') as HTMLButtonElement).click();
    fixture.detectChanges();
    const input = el.querySelector('[data-rename-input]') as HTMLInputElement;
    expect(input.value).toBe('IMG_9685');
    input.value = '  Tenancy agreement ';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    const patch = httpTesting.expectOne({ method: 'PATCH', url: '/api/documents/a' });
    expect(patch.request.body).toEqual({ title: 'Tenancy agreement' });
    patch.flush({ id: 'a', title: 'Tenancy agreement', status: 'Ready', pageCount: 1, updatedAt: '2026-10-05T00:00:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-document-title]')?.textContent).toBe('Tenancy agreement');
    expect(confirm).not.toHaveBeenCalled();
    expect(prompt).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });

  it('cancels a rename with Escape', () => {
    httpTesting.expectOne('/api/documents').flush([
      { id: 'a', title: 'IMG_9685', status: 'Ready', pageCount: 1, updatedAt: '2026-10-01T00:00:00Z' },
    ]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('[data-rename-document]') as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelector('[data-rename-input]') as HTMLInputElement).dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(el.querySelector('[data-rename-input]')).toBeNull();
    expect(el.querySelector('[data-document-title]')?.textContent).toBe('IMG_9685');
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

    expect(navigations).toEqual([['/']]);
    httpTesting.expectOne('/api/documents').flush([]);
  });
});
