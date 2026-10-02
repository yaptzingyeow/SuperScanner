const LOAD_TIMEOUT_MS = 30000;
/** Cleanup fallback for browsers that never fire `afterprint` for the frame or the page. */
const CLEANUP_FALLBACK_MS = 60000;

/**
 * Loads the PDF in a hidden iframe and opens the browser print dialog. Resolves once the dialog was opened;
 * the iframe and its URL are cleaned up on `afterprint` (frame or parent window), or after 60 s at the latest,
 * because removing them straight after print() can make Chromium print a blank page.
 */
export function printPdf(blob: Blob, doc: Document = document): Promise<void> {
  return new Promise((resolve, reject) => {
    const url = URL.createObjectURL(blob);
    const frame = doc.createElement('iframe');
    frame.setAttribute('aria-hidden', 'true');
    frame.tabIndex = -1;
    frame.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden';
    let settled = false;
    let cleaned = false;
    let fallback: ReturnType<typeof setTimeout> | undefined;
    const listeners: EventTarget[] = [];
    const loadTimer = setTimeout(() => fail(new Error('Timed out loading the PDF for printing')), LOAD_TIMEOUT_MS);

    const cleanup = (): void => {
      if (cleaned) return;
      cleaned = true;
      clearTimeout(loadTimer);
      clearTimeout(fallback);
      for (const target of listeners) target.removeEventListener('afterprint', cleanup);
      frame.remove();
      URL.revokeObjectURL(url);
    };
    const fail = (error: unknown): void => {
      cleanup();
      if (settled) return;
      settled = true;
      reject(error);
    };

    frame.onload = () => {
      if (settled) return;
      clearTimeout(loadTimer);
      try {
        const target = frame.contentWindow;
        if (!target) throw new Error('Print frame unavailable');
        for (const source of [target, doc.defaultView]) {
          if (!source || typeof source.addEventListener !== 'function') continue;
          source.addEventListener('afterprint', cleanup);
          listeners.push(source);
        }
        fallback = setTimeout(cleanup, CLEANUP_FALLBACK_MS);
        target.focus();
        target.print();
        settled = true;
        resolve();
      } catch (error) {
        fail(error);
      }
    };
    frame.onerror = () => fail(new Error('Could not load the PDF for printing'));
    frame.src = url;
    doc.body.appendChild(frame);
  });
}
