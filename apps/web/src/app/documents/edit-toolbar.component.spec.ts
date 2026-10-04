import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { EditPanelComponent, EditToolbarComponent } from './edit-toolbar.component';
import { TextEditService } from './text-edit.service';

describe('EditToolbarComponent', () => {
  const history = (canUndo: boolean, canRedo = false) => ({ canUndo, canRedo, activeRevisionId: 'r1', entries: [] });
  let service: { history: ReturnType<typeof vi.fn>; switchRevision: ReturnType<typeof vi.fn>; jumpTo: ReturnType<typeof vi.fn> };

  async function setup(historyResult: unknown = history(false)) {
    service = {
      history: vi.fn(),
      switchRevision: vi.fn().mockResolvedValue(history(false, true)),
      jumpTo: vi.fn().mockResolvedValue(history(false, true)),
    };
    if (historyResult instanceof Error) service.history.mockRejectedValue(historyResult);
    else service.history.mockResolvedValue(historyResult);
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: TextEditService, useValue: service }],
    });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    const fixture = TestBed.createComponent(EditToolbarComponent);
    fixture.componentRef.setInput('documentId', 'doc-1');
    fixture.componentRef.setInput('pageId', 'p1');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const button = (label: string) => [...el.querySelectorAll<HTMLButtonElement>('button')]
      .find((b) => (b.getAttribute('aria-label') ?? b.textContent?.replace(/\s+/g, ' ').trim()) === label)!;
    return { fixture, el, navigate, button };
  }

  it('crop and clean navigate to their own editors for the selected page', async () => {
    const { button, navigate } = await setup();
    for (const [label, segment] of [['Crop & look', 'crop'], ['Clean page', 'clean']]) {
      navigate.mockClear();
      button(label).click();
      expect(navigate, label).toHaveBeenCalledWith(['/documents', 'doc-1', 'pages', 'p1', segment], undefined);
    }
  });

  it('text, add text, signature, tick / cross and recognize start in the workspace without navigating', async () => {
    const { fixture, button, navigate } = await setup();
    const started: (string | undefined)[] = [];
    fixture.componentInstance.startTool.subscribe((tool) => started.push(tool.query));
    for (const label of ['Edit text', 'Add text', 'Signature', 'Tick / Cross', 'Recognize text']) button(label).click();
    expect(navigate).not.toHaveBeenCalled();
    expect(started).toEqual([undefined, 'add', 'signature', 'mark', 'ocr']);
  });

  it('undo is disabled when history cannot undo', async () => {
    const { button } = await setup(history(false, true));
    expect(button('Undo').disabled).toBe(true);
    expect(button('Redo').disabled).toBe(false);
  });

  it('undo switches the revision and asks the workspace to reload', async () => {
    const { fixture, button } = await setup(history(true));
    const changed = vi.fn();
    fixture.componentInstance.pageChanged.subscribe(changed);
    button('Undo').click();
    await fixture.whenStable();
    expect(service.switchRevision).toHaveBeenCalledWith('doc-1', 'p1', 'undo', 'r1');
    expect(changed).toHaveBeenCalled();
  });

  it('history lists every saved version and jumps to the one clicked', async () => {
    const { fixture, el, button } = await setup({
      canUndo: true, canRedo: false, activeRevisionId: 'r2', originalRevisionId: 'r0',
      entries: [
        { id: 'e1', sourceRevisionId: 'r0', state: 'Succeeded', resultRevisionId: 'r1', originalText: 'Yap', replacementText: 'Tan', completedAt: '2026-10-05T01:00:00Z' },
        { id: 'e2', sourceRevisionId: 'r1', state: 'Succeeded', resultRevisionId: 'r2', originalText: 'Ali', replacementText: '', completedAt: '2026-10-05T02:00:00Z' },
        { id: 'e3', sourceRevisionId: 'r2', state: 'Failed', resultRevisionId: null, originalText: 'x', replacementText: 'y' },
        { id: 'e4', sourceRevisionId: 'r2', state: 'Succeeded', resultRevisionId: 'r3', originalText: '', replacementText: 'heloo' },
      ],
    });
    button('History').click();
    await fixture.whenStable();
    fixture.detectChanges();

    const items = [...el.querySelectorAll<HTMLButtonElement>('[data-history-version]')];
    expect(items.map((b) => b.textContent!.replace(/\s+/g, ' ').trim().split(' · ')[0])).toEqual([
      'Added “heloo”', 'Deleted “Ali”', 'Replaced “Yap” with “Tan”', 'Original page',
    ]);
    expect(items[1].getAttribute('aria-current')).toBe('true');
    expect(items[1].disabled).toBe(true);

    items[3].click();
    await fixture.whenStable();
    expect(service.jumpTo).toHaveBeenCalledWith('doc-1', 'p1', 'r0', 'r2');
  });

  it('history says so when a page has no saved changes', async () => {
    const { fixture, el, button } = await setup({ canUndo: false, canRedo: false, activeRevisionId: null, entries: [] });
    button('History').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('.history-panel')?.textContent).toContain('No saved changes on this page yet.');
    expect(el.querySelectorAll('[data-history-version]').length).toBe(0);
  });

  it('history load failure shows the safe message', async () => {
    const { fixture, el, button } = await setup(new Error('500'));
    button('History').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Edit history is not available right now.');
  });

  it('panel shows the selected tool title, description and three steps', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(EditPanelComponent);
    fixture.componentRef.setInput('tool', 'sign');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('h2')?.textContent).toContain('Add a signature');
    expect(el.querySelectorAll('ol li')).toHaveLength(3);
  });
});
