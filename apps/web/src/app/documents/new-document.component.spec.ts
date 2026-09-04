import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { NewDocumentComponent } from './new-document.component';
import { UploadService } from './upload.service';

describe('NewDocumentComponent', () => {
  let fixture: ComponentFixture<NewDocumentComponent>;
  let finishUpload: (value: unknown) => void;
  let submissions: Array<{ title: string; file: File }>;
  let navigations: unknown[][];

  beforeEach(async () => {
    submissions = [];
    navigations = [];
    const upload = {
      progress: signal({ stage: 'idle', percent: 0 }),
      upload: (title: string, file: File) => {
        submissions.push({ title, file });
        return new Promise((resolve) => {
          finishUpload = resolve;
        });
      },
    };
    await TestBed.configureTestingModule({
      imports: [NewDocumentComponent],
      providers: [provideRouter([]), { provide: UploadService, useValue: upload }],
    }).compileComponents();
    vi.spyOn(TestBed.inject(Router), 'navigate').mockImplementation((commands) => {
      navigations.push([...commands]);
      return Promise.resolve(true);
    });
    fixture = TestBed.createComponent(NewDocumentComponent);
    fixture.detectChanges();
  });

  it('shows the selected file name and size', () => {
    fixture.componentInstance.selectFile(
      new File(['%PDF-1.7'], 'application.pdf', { type: 'application/pdf' }),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('application.pdf');
    expect(fixture.nativeElement.textContent).toContain('8 B');
  });

  it('submits through the native form and disables duplicate submission', async () => {
    const file = new File(['%PDF-1.7'], 'form.pdf', { type: 'application/pdf' });
    fixture.componentInstance.selectFile(file);
    fixture.componentInstance.setTitle('Application form');
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('form') as HTMLFormElement).dispatchEvent(
      new Event('submit', { bubbles: true, cancelable: true }),
    );
    await Promise.resolve();
    fixture.detectChanges();

    expect(submissions).toEqual([{ title: 'Application form', file }]);
    expect(
      (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled,
    ).toBe(true);

    finishUpload({ documentId: 'doc-1', uploadId: 'up-1', state: 'PendingValidation' });
    await fixture.whenStable();
    expect(navigations).toEqual([['/documents', 'doc-1', 'uploads', 'up-1']]);
  });

  it('shows a safe validation message without provider details', async () => {
    const upload = TestBed.inject(UploadService) as unknown as { upload: () => Promise<never> };
    upload.upload = () => Promise.reject({ code: 'unsupported_type', detail: 'provider secret' });
    fixture.componentInstance.selectFile(new File(['text'], 'notes.txt', { type: 'text/plain' }));
    fixture.componentInstance.setTitle('Notes');

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Choose a PDF, JPEG, PNG, or HEIC file');
    expect(fixture.nativeElement.textContent).not.toContain('provider secret');
  });
});
