import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { vi } from 'vitest';
import { NewDocumentComponent } from './new-document.component';
import { UploadService } from './upload.service';

describe('NewDocumentComponent', () => {
  let fixture: ComponentFixture<NewDocumentComponent>;
  let finishUpload: (value: unknown) => void;
  let submissions: Array<{ title: string; files: readonly File[] }>;
  let navigations: unknown[][];

  beforeEach(async () => {
    submissions = [];
    navigations = [];
    const upload = {
      progress: signal({ stage: 'idle', percent: 0 }),
      items: signal([]),
      startDocument: (title: string, files: readonly File[]) => {
        submissions.push({ title, files });
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

    expect(submissions).toEqual([{ title: 'Application form', files: [file] }]);
    expect(
      (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).disabled,
    ).toBe(true);

    finishUpload({ documentId: 'doc-1', uploadIds: ['up-1'] });
    await fixture.whenStable();
    expect(navigations).toEqual([['/documents', 'doc-1', 'import']]);
  });

  it('shows a safe validation message without provider details', async () => {
    const upload = TestBed.inject(UploadService) as unknown as {
      startDocument: () => Promise<never>;
    };
    upload.startDocument = () => Promise.reject({ code: 'unsupported_type', detail: 'provider secret' });
    fixture.componentInstance.selectFile(new File(['text'], 'notes.txt', { type: 'text/plain' }));
    fixture.componentInstance.setTitle('Notes');

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Choose a PDF, JPEG, PNG, or HEIC file');
    expect(fixture.nativeElement.textContent).not.toContain('provider secret');
  });

  describe('multi-file', () => {
    const files = (...names: string[]) =>
      names.map((name) => new File(['x'], name, { type: 'image/png' }));

    it('title defaults to the first file name without extension when left empty', async () => {
      const service = TestBed.inject(UploadService) as unknown as Record<string, unknown>;
      const started: Array<{ title: string; files: readonly File[] }> = [];
      service['startDocument'] = async (title: string, list: readonly File[]) => {
        started.push({ title, files: list });
        return { documentId: 'doc-1', uploadIds: ['up-1'] };
      };
      fixture.componentInstance.selectFiles(files('Passport scan.final.png'));

      await fixture.componentInstance.submit();

      expect(started[0].title).toBe('Passport scan.final');
    });

    it('selecting three files uploads all three and navigates to import with their upload ids', async () => {
      const service = TestBed.inject(UploadService) as unknown as Record<string, unknown>;
      const started: Array<{ title: string; files: readonly File[] }> = [];
      service['startDocument'] = async (title: string, list: readonly File[]) => {
        started.push({ title, files: list });
        return { documentId: 'doc-9', uploadIds: ['u1', 'u2', 'u3'] };
      };
      const navigate = TestBed.inject(Router).navigate as ReturnType<typeof vi.fn>;
      fixture.componentInstance.selectFiles(files('a.png', 'b.png', 'c.png'));
      fixture.componentInstance.setTitle('Receipts');

      await fixture.componentInstance.submit();

      expect(started[0].files.map((f) => f.name)).toEqual(['a.png', 'b.png', 'c.png']);
      expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-9', 'import'], {
        queryParams: { uploads: 'u1,u2,u3' },
      });
    });

    it('a rejected file is reported while the others continue', async () => {
      const items = signal<unknown[]>([]);
      const service = TestBed.inject(UploadService) as unknown as Record<string, unknown>;
      service['items'] = items;
      service['startDocument'] = async () => {
        items.set([
          { fileName: 'big.png', stage: 'error', percent: 0, errorCode: 'file_too_large' },
          { fileName: 'ok.png', stage: 'accepted', percent: 100, uploadId: 'u2' },
        ]);
        return { documentId: 'doc-2', uploadIds: ['u2'] };
      };
      const navigate = TestBed.inject(Router).navigate as ReturnType<typeof vi.fn>;
      navigate.mockClear();
      fixture.componentInstance.selectFiles(files('big.png', 'ok.png'));

      await fixture.componentInstance.submit();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('big.png');
      expect(fixture.nativeElement.textContent).toContain('smaller than 25 MB');
      expect(navigate).not.toHaveBeenCalled();

      const button = Array.from(
        fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>,
      ).find((b) => b.textContent?.trim() === 'Continue to import')!;
      button.click();
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith(['/documents', 'doc-2', 'import'], {
        queryParams: { uploads: 'u2' },
      });
    });

    it('starts uploading as soon as files are chosen', async () => {
      const service = TestBed.inject(UploadService) as unknown as Record<string, unknown>;
      const started: string[] = [];
      service['startDocument'] = async (title: string) => {
        started.push(title);
        return { documentId: 'doc-4', uploadIds: ['u1'] };
      };
      const input = fixture.nativeElement.querySelector('#document-file') as HTMLInputElement;
      Object.defineProperty(input, 'files', { value: files('scan.png') });

      input.dispatchEvent(new Event('change'));
      await fixture.whenStable();

      expect(started).toEqual(['scan']);
    });

    it('lists server-rejected items as rejected and withholds their ids', async () => {
      const service = TestBed.inject(UploadService) as unknown as Record<string, unknown>;
      service['items'] = signal([
        {
          fileName: 'bad.pdf',
          stage: 'rejected',
          percent: 100,
          errorCode: 'upload_failed',
          uploadId: 'u1',
        },
        { fileName: 'ok.png', stage: 'validating', percent: 100, uploadId: 'u2' },
      ]);
      service['startDocument'] = async () => ({ documentId: 'doc-5', uploadIds: ['u1', 'u2'] });
      fixture.componentInstance.selectFiles(files('bad.pdf', 'ok.png'));

      await fixture.componentInstance.submit();
      fixture.detectChanges();

      expect(fixture.nativeElement.textContent).toContain('bad.pdf');
      expect(fixture.nativeElement.textContent).toContain('Continue to import');
    });

    it('stays on the page when every file fails', async () => {
      const service = TestBed.inject(UploadService) as unknown as Record<string, unknown>;
      service['items'] = signal([
        { fileName: 'big.png', stage: 'error', percent: 0, errorCode: 'file_too_large' },
      ]);
      service['startDocument'] = async () => ({ documentId: 'doc-3', uploadIds: [] });
      const navigate = TestBed.inject(Router).navigate as ReturnType<typeof vi.fn>;
      navigate.mockClear();
      fixture.componentInstance.selectFiles(files('big.png'));

      await fixture.componentInstance.submit();
      fixture.detectChanges();

      expect(navigate).not.toHaveBeenCalled();
      expect(fixture.nativeElement.textContent).toContain('smaller than 25 MB');
    });
  });
});
