import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { vi } from 'vitest';
import { DocumentsApiService, UploadStatusDto } from './documents-api.service';
import { UploadStatusComponent } from './upload-status.component';

describe('UploadStatusComponent', () => {
  let fixture: ComponentFixture<UploadStatusComponent>;
  let responses: UploadStatusDto[];
  let calls: number;

  beforeEach(async () => {
    vi.useFakeTimers();
    calls = 0;
    responses = [
      { uploadId: 'up-1', state: 'PendingValidation' },
      { uploadId: 'up-1', state: 'Accepted' },
    ];
    await TestBed.configureTestingModule({
      imports: [UploadStatusComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap({ documentId: 'doc-1', uploadId: 'up-1' }) },
          },
        },
        {
          provide: DocumentsApiService,
          useValue: {
            getUploadStatus: () =>
              Promise.resolve(responses[Math.min(calls++, responses.length - 1)]),
          },
        },
      ],
    }).compileComponents();
    fixture = TestBed.createComponent(UploadStatusComponent);
  });

  afterEach(() => {
    fixture.destroy();
    vi.useRealTimers();
  });

  it('reloads and polls pending validation until accepted', async () => {
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Validating securely');

    await vi.advanceTimersByTimeAsync(2000);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Scan accepted');
    expect(calls).toBe(2);
  });

  it('shows a safe message for rejected validation', async () => {
    responses = [{ uploadId: 'up-1', state: 'Rejected', errorCode: 'malware_detected' }];
    fixture.detectChanges();
    await vi.advanceTimersByTimeAsync(0);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('could not be accepted');
    expect(fixture.nativeElement.textContent).not.toContain('malware_detected');
  });
});
