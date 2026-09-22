import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { PageOcr } from './document.models';
import { DocumentsApiService } from './documents-api.service';
import { OcrStatusComponent } from './ocr-status.component';

describe('OcrStatusComponent', () => {
  const notRequested: PageOcr = {
    state: 'NotRequested',
    elementCount: 0,
    canRetry: false,
    elements: [],
  };
  const queued: PageOcr = { ...notRequested, resultId: 'r1', state: 'Queued' };
  const ready: PageOcr = {
    ...notRequested,
    resultId: 'r1',
    state: 'Ready',
    elementCount: 12,
    aggregateConfidence: 0.94,
    completedAt: '2026-09-22T09:00:00Z',
  };
  let fixture: ComponentFixture<OcrStatusComponent>;
  let api: {
    getPageOcr: ReturnType<typeof vi.fn>;
    requestPageOcr: ReturnType<typeof vi.fn>;
  };

  beforeEach(() => {
    vi.useFakeTimers();
    api = {
      getPageOcr: vi.fn().mockResolvedValue(notRequested),
      requestPageOcr: vi.fn().mockResolvedValue(queued),
    };
    TestBed.configureTestingModule({
      imports: [OcrStatusComponent],
      providers: [{ provide: DocumentsApiService, useValue: api }],
    });
    fixture = TestBed.createComponent(OcrStatusComponent);
    fixture.componentRef.setInput('documentId', 'd1');
    fixture.componentRef.setInput('pageId', 'p1');
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
  });

  it('polls pending OCR and stops when Ready', async () => {
    api.getPageOcr.mockResolvedValueOnce(queued).mockResolvedValueOnce(ready);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    await vi.advanceTimersByTimeAsync(3000);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('12 words recognized');
    expect(fixture.nativeElement.textContent).toContain('94% confidence');
    expect(api.getPageOcr).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(3000);
    expect(api.getPageOcr).toHaveBeenCalledTimes(2);
  });

  it('starts recognition and exposes no fabricated progress percentage', async () => {
    api.getPageOcr.mockResolvedValueOnce(notRequested);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    const button = [...fixture.nativeElement.querySelectorAll('button')].find(
      (candidate: HTMLButtonElement) => candidate.textContent?.includes('Recognize text'),
    ) as HTMLButtonElement;

    button.click();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(api.requestPageOcr).toHaveBeenCalledWith('d1', 'p1', false);
    expect(fixture.nativeElement.textContent).toContain('Waiting for text recognition');
    expect(fixture.nativeElement.textContent).not.toMatch(/\d+% complete/);
  });

  it('shows only safe failure copy and allows retry when the API permits it', async () => {
    api.getPageOcr.mockResolvedValueOnce({
      ...notRequested,
      resultId: 'r1',
      state: 'Failed',
      failureCode: 'ocr_timeout',
      canRetry: true,
    });
    api.requestPageOcr.mockResolvedValueOnce(queued);
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Text recognition did not finish.');
    expect(fixture.nativeElement.textContent).not.toContain('ocr_timeout');

    const retry = [...fixture.nativeElement.querySelectorAll('button')].find(
      (candidate: HTMLButtonElement) => candidate.textContent?.includes('Retry text recognition'),
    ) as HTMLButtonElement;
    retry.click();
    await vi.advanceTimersByTimeAsync(0);
    expect(api.requestPageOcr).toHaveBeenCalledWith('d1', 'p1', true);
  });

  it('does not mutate state after destroy while a request resolves', async () => {
    let resolve!: (value: PageOcr) => void;
    const pending = new Promise<PageOcr>((complete) => (resolve = complete));
    api.getPageOcr.mockReturnValueOnce(pending);
    fixture.detectChanges();
    fixture.destroy();

    resolve(ready);
    await pending;
    await Promise.resolve();

    expect((fixture.componentInstance as any).status()).toBeNull();
  });
});
