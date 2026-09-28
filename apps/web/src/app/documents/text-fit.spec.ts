import { describe, expect, it } from 'vitest';
import { fitSingleLine, type TextFitDraft } from './text-fit';

const draft = (text: string, boxWidth: number): TextFitDraft => ({
  text,
  box: { x: 0.1, y: 0.2, width: boxWidth, height: 0.2 },
  imageWidth: 1000,
  imageHeight: 1000,
  fontSizeNormalized: 0.05,
  letterSpacing: 0,
  minimumLetterSpacing: -0.02,
  minimumFontScale: 0.7,
});

const measure = (text: string, size: number) => ({
  width: [...text].length * size * 0.5,
  height: size,
});

describe('fitSingleLine', () => {
  it('keeps exact fit unchanged', () => {
    const result = fitSingleLine(measure, draft('Hello', 0.125));
    expect(result.fits).toBe(true);
    expect(result.fontScale).toBe(1);
    expect(result.letterSpacing).toBe(0);
  });

  it('reduces spacing before font size and reports overflow at both minima', () => {
    const result = fitSingleLine(measure, draft('A much longer replacement', 0.02));
    expect(result.steps[0].kind).toBe('letterSpacing');
    expect(result.fontScale).toBeGreaterThanOrEqual(0.7);
    expect(result.overflow).toBe(true);
  });

  it('matches the server Hello fixture', () => {
    const result = fitSingleLine(measure, draft('Hello', 0.1));
    expect(result.letterSpacing).toBeCloseTo(-0.02, 6);
    expect(result.fontScale).toBeCloseTo(100 / 121, 6);
    expect(result.width).toBeCloseTo(100, 6);
  });

  it('rejects empty text and invalid dimensions', () => {
    expect(() => fitSingleLine(measure, draft(' ', 0.1))).toThrow();
    expect(() => fitSingleLine(measure, { ...draft('Hello', 0.1), imageWidth: 0 })).toThrow();
  });

  it('keeps Unicode punctuation intact', () => {
    const result = fitSingleLine(measure, draft('It’s—fine', 0.3));
    expect(result.graphemeCount).toBe(9);
  });

  it('keeps large requested spacing when the box is wide enough', () => {
    const result = fitSingleLine(measure, { ...draft('AB', 0.8),
      letterSpacing: 1.55, minimumLetterSpacing: 1.55 });
    expect(result.fits).toBe(true);
    expect(result.letterSpacing).toBe(1.55);
  });

  it('reports overflow without silently shrinking large requested spacing', () => {
    const result = fitSingleLine(measure, { ...draft('AB', 0.05),
      letterSpacing: 1.55, minimumLetterSpacing: 1.55 });
    expect(result.overflow).toBe(true);
    expect(result.letterSpacing).toBe(1.55);
  });
});
