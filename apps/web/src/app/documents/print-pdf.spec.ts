import { vi } from 'vitest';
import { printPdf } from './print-pdf';

describe('printPdf', () => {
  function setup() {
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:pdf');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const print = vi.fn();
    const frame = window.document.createElement('iframe');
    const origCreate = window.document.createElement.bind(window.document);
    const parent = new EventTarget();
    const doc = {
      body: window.document.body,
      defaultView: parent,
      createElement: (tag: string) => (tag === 'iframe' ? frame : origCreate(tag)),
    } as unknown as Document;
    const target = Object.assign(new EventTarget(), { print, focus: vi.fn() });
    Object.defineProperty(frame, 'contentWindow', { value: target });
    const restore = () => { create.mockRestore(); revoke.mockRestore(); };
    return { revoke, print, frame, doc, target, parent, restore };
  }

  afterEach(() => vi.useRealTimers());

  it('prints once loaded and keeps the iframe until the frame reports afterprint', async () => {
    const { revoke, print, frame, doc, target, restore } = setup();

    const done = printPdf(new Blob(['x'], { type: 'application/pdf' }), doc);
    expect(frame.src).toBe('blob:pdf');
    expect(window.document.body.contains(frame)).toBe(true);
    frame.onload?.(new Event('load'));
    await done;

    expect(print).toHaveBeenCalledTimes(1);
    expect(window.document.body.contains(frame)).toBe(true);
    expect(revoke).not.toHaveBeenCalledWith('blob:pdf');

    target.dispatchEvent(new Event('afterprint'));
    expect(window.document.body.contains(frame)).toBe(false);
    expect(revoke).toHaveBeenCalledWith('blob:pdf');
    restore();
  });

  it('cleans up when the parent window reports afterprint', async () => {
    const { revoke, frame, doc, parent, target, restore } = setup();

    const done = printPdf(new Blob(['x']), doc);
    frame.onload?.(new Event('load'));
    await done;
    parent.dispatchEvent(new Event('afterprint'));
    target.dispatchEvent(new Event('afterprint'));

    expect(window.document.body.contains(frame)).toBe(false);
    // Other specs may revoke their own URLs late; count only this print's URL.
    expect(revoke.mock.calls.filter(([url]) => url === 'blob:pdf')).toHaveLength(1);
    restore();
  });

  it('cleans up after 60 s when no afterprint event arrives', async () => {
    vi.useFakeTimers();
    const { revoke, frame, doc, restore } = setup();

    const done = printPdf(new Blob(['x']), doc);
    frame.onload?.(new Event('load'));
    await done;
    await vi.advanceTimersByTimeAsync(59999);
    expect(window.document.body.contains(frame)).toBe(true);
    await vi.advanceTimersByTimeAsync(1);

    expect(window.document.body.contains(frame)).toBe(false);
    expect(revoke).toHaveBeenCalledWith('blob:pdf');
    restore();
  });

  it('rejects and cleans up when the iframe never loads', async () => {
    vi.useFakeTimers();
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:never');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const before = window.document.querySelectorAll('iframe').length;
    const done = printPdf(new Blob(['x']));
    const caught = done.catch((e: Error) => e.message);
    await vi.advanceTimersByTimeAsync(30000);
    expect(await caught).toContain('Timed out');
    expect(window.document.querySelectorAll('iframe').length).toBe(before);
    expect(revoke).toHaveBeenCalledWith('blob:never');
  });

  it('rejects and cleans up at once when printing throws', async () => {
    const { revoke, print, frame, doc, restore } = setup();
    print.mockImplementation(() => { throw new Error('blocked'); });

    const done = printPdf(new Blob(['x']), doc);
    frame.onload?.(new Event('load'));

    await expect(done).rejects.toThrow('blocked');
    expect(window.document.body.contains(frame)).toBe(false);
    expect(revoke).toHaveBeenCalledWith('blob:pdf');
    restore();
  });
});
