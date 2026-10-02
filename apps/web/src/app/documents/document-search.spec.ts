import { OcrElement, PageOcr } from './document.models';
import { searchPages } from './document-search';

const word = (id: string, text: string, order: number): OcrElement => ({
  id, kind: 'Word', text, confidence: 0.9, textType: 'Printed', readingOrder: order,
  polygon: [{ x: 0.1, y: 0.1 }, { x: 0.2, y: 0.1 }, { x: 0.2, y: 0.2 }, { x: 0.1, y: 0.2 }], children: [],
});
const ocr = (words: [string, string][], state: PageOcr['state'] = 'Ready'): PageOcr => ({
  state, elementCount: words.length, canRetry: false,
  elements: [{
    id: 'b', kind: 'Block', text: '', confidence: 1, textType: 'Printed', readingOrder: 0, polygon: [],
    children: [{
      id: 'l', kind: 'Line', text: '', confidence: 1, textType: 'Printed', readingOrder: 0, polygon: [],
      children: words.map(([id, text], i) => word(id, text, i)),
    }],
  }],
});

describe('searchPages', () => {
  const pages = new Map<string, PageOcr | null>([
    ['p1', ocr([['a', 'Appointment'], ['b', 'Letter'], ['c', 'Café']])],
    ['p2', ocr([], 'Queued')],
    ['p3', null],
  ]);

  it('finds a phrase spanning two words', () => {
    expect(searchPages('appointment letter', pages).hits).toEqual([{ pageId: 'p1', wordIds: ['a', 'b'] }]);
  });

  it('is case-insensitive and diacritic-insensitive', () => {
    expect(searchPages('LETTER', pages).hits).toEqual([{ pageId: 'p1', wordIds: ['b'] }]);
    expect(searchPages('cafe', pages).hits).toEqual([{ pageId: 'p1', wordIds: ['c'] }]);
  });

  it('dedupes hits that cover the same words', () => {
    const result = searchPages('an', new Map([['p1', ocr([['a', 'banana']])]]));
    expect(result.hits).toEqual([{ pageId: 'p1', wordIds: ['a'] }]);
  });

  it('reports pages without recognized text', () => {
    expect(searchPages('letter', pages).unrecognizedPageIds).toEqual(['p2', 'p3']);
  });

  it('returns no hits for a one-character query', () => {
    const result = searchPages('a', pages);
    expect(result.hits).toEqual([]);
  });
});
