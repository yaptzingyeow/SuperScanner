import { OcrElement, OcrPoint } from './document.models';

export interface SelectionRegion {
  x1: number;
  y1: number;
  x2: number;
  y2: number;
}

export interface SelectableOcrWord extends OcrElement {
  blockOrder: number;
  lineOrder: number;
}

export interface OcrSelectionSummary {
  phrase: string;
  wordCount: number;
  averageConfidence: number;
  textType: 'Printed' | 'Handwritten' | 'Mixed' | 'Unknown';
}

interface NormalizedRegion {
  left: number;
  top: number;
  right: number;
  bottom: number;
}

export function flattenSelectableWords(elements: OcrElement[]): SelectableOcrWord[] {
  const words: SelectableOcrWord[] = [];
  for (const block of elements.filter((element) => element.kind === 'Block')) {
    for (const line of block.children.filter((element) => element.kind === 'Line')) {
      for (const candidate of line.children.filter((element) => element.kind === 'Word')) {
        if (!isUsablePolygon(candidate.polygon)) continue;
        words.push({ ...candidate, blockOrder: block.readingOrder, lineOrder: line.readingOrder });
      }
    }
  }
  return words.sort(compareWords);
}

export function selectWordsInRegion(
  words: SelectableOcrWord[],
  region: SelectionRegion,
): SelectableOcrWord[] {
  const normalized = normalizeRegion(region);
  return words.filter((word) => polygonIntersectsRegion(word.polygon, normalized)).sort(compareWords);
}

export function summarizeSelection(words: SelectableOcrWord[]): OcrSelectionSummary | null {
  if (words.length === 0) return null;
  const ordered = [...words].sort(compareWords);
  const types = new Set(ordered.map((word) => word.textType));
  const textType = types.size === 1 ? ordered[0].textType : 'Mixed';
  return {
    phrase: ordered.map((word) => word.text).join(' '),
    wordCount: ordered.length,
    averageConfidence: ordered.reduce((total, word) => total + word.confidence, 0) / ordered.length,
    textType,
  };
}

function compareWords(left: SelectableOcrWord, right: SelectableOcrWord): number {
  return left.blockOrder - right.blockOrder ||
    left.lineOrder - right.lineOrder ||
    left.readingOrder - right.readingOrder ||
    left.id.localeCompare(right.id);
}

function isUsablePolygon(points: OcrPoint[]): boolean {
  return points.length === 4 && points.every((point) =>
    Number.isFinite(point.x) && Number.isFinite(point.y) &&
    point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1,
  );
}

function normalizeRegion(region: SelectionRegion): NormalizedRegion {
  return {
    left: Math.min(region.x1, region.x2),
    top: Math.min(region.y1, region.y2),
    right: Math.max(region.x1, region.x2),
    bottom: Math.max(region.y1, region.y2),
  };
}

function polygonIntersectsRegion(points: OcrPoint[], region: NormalizedRegion): boolean {
  const xs = points.map((point) => point.x);
  const ys = points.map((point) => point.y);
  if (Math.max(...xs) < region.left || Math.min(...xs) > region.right ||
      Math.max(...ys) < region.top || Math.min(...ys) > region.bottom) return false;

  if (points.some((point) => pointInRegion(point, region))) return true;

  const corners: OcrPoint[] = [
    { x: region.left, y: region.top },
    { x: region.right, y: region.top },
    { x: region.right, y: region.bottom },
    { x: region.left, y: region.bottom },
  ];
  if (corners.some((corner) => pointInPolygon(corner, points))) return true;

  const polygonEdges = points.map((point, index) => [point, points[(index + 1) % points.length]] as const);
  const rectangleEdges = corners.map((point, index) => [point, corners[(index + 1) % corners.length]] as const);
  return polygonEdges.some(([start, end]) =>
    rectangleEdges.some(([otherStart, otherEnd]) =>
      segmentsIntersect(start, end, otherStart, otherEnd),
    ),
  );
}

function pointInRegion(point: OcrPoint, region: NormalizedRegion): boolean {
  return point.x >= region.left && point.x <= region.right &&
    point.y >= region.top && point.y <= region.bottom;
}

function pointInPolygon(point: OcrPoint, polygon: OcrPoint[]): boolean {
  let inside = false;
  for (let current = 0, previous = polygon.length - 1; current < polygon.length; previous = current++) {
    const a = polygon[current];
    const b = polygon[previous];
    if (pointOnSegment(point, a, b)) return true;
    if ((a.y > point.y) !== (b.y > point.y) &&
        point.x < ((b.x - a.x) * (point.y - a.y)) / (b.y - a.y) + a.x) inside = !inside;
  }
  return inside;
}

function segmentsIntersect(a: OcrPoint, b: OcrPoint, c: OcrPoint, d: OcrPoint): boolean {
  const o1 = orientation(a, b, c);
  const o2 = orientation(a, b, d);
  const o3 = orientation(c, d, a);
  const o4 = orientation(c, d, b);
  if (o1 !== o2 && o3 !== o4) return true;
  return (o1 === 0 && pointOnSegment(c, a, b)) ||
    (o2 === 0 && pointOnSegment(d, a, b)) ||
    (o3 === 0 && pointOnSegment(a, c, d)) ||
    (o4 === 0 && pointOnSegment(b, c, d));
}

function orientation(a: OcrPoint, b: OcrPoint, c: OcrPoint): -1 | 0 | 1 {
  const value = (b.y - a.y) * (c.x - b.x) - (b.x - a.x) * (c.y - b.y);
  if (Math.abs(value) < Number.EPSILON * 16) return 0;
  return value > 0 ? 1 : -1;
}

function pointOnSegment(point: OcrPoint, start: OcrPoint, end: OcrPoint): boolean {
  return point.x >= Math.min(start.x, end.x) && point.x <= Math.max(start.x, end.x) &&
    point.y >= Math.min(start.y, end.y) && point.y <= Math.max(start.y, end.y) &&
    orientation(start, end, point) === 0;
}
