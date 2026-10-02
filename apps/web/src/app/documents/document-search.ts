import { PageOcr } from './document.models';
import { flattenSelectableWords } from './ocr-selection';

export interface SearchHit {
  pageId: string;
  wordIds: string[];
}

export interface SearchResult {
  hits: SearchHit[];
  unrecognizedPageIds: string[];
}

const MIN_QUERY_LENGTH = 2;

export function normalizeSearchText(value: string): string {
  return value.normalize('NFD').replace(/\p{M}+/gu, '').toLowerCase().replace(/\s+/g, ' ').trim();
}

export function searchPages(query: string, ocrByPage: ReadonlyMap<string, PageOcr | null>): SearchResult {
  const needle = normalizeSearchText(query);
  const searching = needle.length >= MIN_QUERY_LENGTH;
  const hits: SearchHit[] = [];
  const seen = new Set<string>();
  const unrecognizedPageIds: string[] = [];

  for (const [pageId, ocr] of ocrByPage) {
    if (!ocr || ocr.state !== 'Ready') {
      unrecognizedPageIds.push(pageId);
      continue;
    }
    if (!searching) continue;

    const words = flattenSelectableWords(ocr.elements)
      .map((word) => ({ id: word.id, text: normalizeSearchText(word.text) }))
      .filter((word) => word.text.length > 0);
    let haystack = '';
    const ranges: { id: string; start: number; end: number }[] = [];
    for (const word of words) {
      if (haystack) haystack += ' ';
      ranges.push({ id: word.id, start: haystack.length, end: haystack.length + word.text.length });
      haystack += word.text;
    }
    for (let from = haystack.indexOf(needle); from >= 0; from = haystack.indexOf(needle, from + needle.length)) {
      const to = from + needle.length;
      const wordIds = ranges.filter((range) => range.start < to && range.end > from).map((range) => range.id);
      const key = pageId + '\u0000' + wordIds.join('\u0000');
      if (wordIds.length > 0 && !seen.has(key)) {
        seen.add(key);
        hits.push({ pageId, wordIds });
      }
    }
  }
  return { hits, unrecognizedPageIds };
}
