import { OcrElement, OcrPoint } from './document.models';
import {
  flattenSelectableWords,
  selectWordsInRegion,
  summarizeSelection,
} from './ocr-selection';

describe('OCR selection geometry', () => {
  const polygon = (left: number, top: number, right: number, bottom: number): OcrPoint[] => [
    { x: left, y: top },
    { x: right, y: top },
    { x: right, y: bottom },
    { x: left, y: bottom },
  ];

  const word = (
    id: string,
    text: string,
    readingOrder: number,
    points: OcrPoint[],
    textType: OcrElement['textType'] = 'Printed',
    confidence = .9,
  ): OcrElement => ({
    id,
    kind: 'Word',
    text,
    confidence,
    textType,
    readingOrder,
    polygon: points,
    children: [],
  });

  const line = (id: string, readingOrder: number, children: OcrElement[]): OcrElement => ({
    id,
    kind: 'Line',
    text: children.map((item) => item.text).join(' '),
    confidence: .9,
    textType: 'Printed',
    readingOrder,
    polygon: polygon(.1, .1, .9, .3),
    children,
  });

  const block = (id: string, readingOrder: number, children: OcrElement[]): OcrElement => ({
    id,
    kind: 'Block',
    text: children.map((item) => item.text).join(' '),
    confidence: .9,
    textType: 'Printed',
    readingOrder,
    polygon: polygon(.05, .05, .95, .4),
    children,
  });

  it('flattens valid words in hierarchy reading order with stable id tie-breaking', () => {
    const elements = [
      block('b2', 2, [line('l2', 1, [word('w4', 'Later', 1, polygon(.1, .4, .2, .5))])]),
      block('b1', 1, [
        line('l1', 1, [
          word('w2', 'Tzing', 2, polygon(.3, .1, .5, .2)),
          word('w1', 'Yap', 1, polygon(.1, .1, .25, .2)),
          word('w3b', 'Yeow', 3, polygon(.55, .1, .75, .2)),
          word('w3a', 'Before', 3, polygon(.76, .1, .9, .2)),
        ]),
      ]),
    ];

    expect(flattenSelectableWords(elements).map((item) => item.id)).toEqual([
      'w1', 'w2', 'w3a', 'w3b', 'w4',
    ]);
  });

  it('selects the same multi-word phrase for forward and reverse drag regions', () => {
    const words = flattenSelectableWords([
      block('b1', 1, [line('l1', 1, [
        word('w1', 'Yap', 1, polygon(.1, .1, .25, .2)),
        word('w2', 'Tzing', 2, polygon(.3, .1, .5, .2)),
        word('w3', 'Yeow', 3, polygon(.55, .1, .75, .2)),
      ])]),
    ]);

    expect(selectWordsInRegion(words, { x1: .08, y1: .08, x2: .8, y2: .25 })
      .map((item) => item.text)).toEqual(['Yap', 'Tzing', 'Yeow']);
    expect(selectWordsInRegion(words, { x1: .8, y1: .25, x2: .08, y2: .08 })
      .map((item) => item.text)).toEqual(['Yap', 'Tzing', 'Yeow']);
  });

  it('uses exact polygon intersection after the bounding-box pre-check', () => {
    const diamond = word('diamond', 'Diamond', 1, [
      { x: .5, y: .2 }, { x: .8, y: .5 }, { x: .5, y: .8 }, { x: .2, y: .5 },
    ]);
    const words = flattenSelectableWords([block('b1', 1, [line('l1', 1, [diamond])])]);

    expect(selectWordsInRegion(words, { x1: .2, y1: .2, x2: .3, y2: .3 })).toEqual([]);
    expect(selectWordsInRegion(words, { x1: .45, y1: .45, x2: .55, y2: .55 })
      .map((item) => item.id)).toEqual(['diamond']);
  });

  it('skips malformed polygons without hiding valid words', () => {
    const invalid = word('bad', 'Bad', 1, [{ x: 0, y: 0 }, { x: 1, y: 1 }]);
    const nonFinite = word('nan', 'NaN', 2, [
      { x: .1, y: .1 }, { x: Number.NaN, y: .1 }, { x: .2, y: .2 }, { x: .1, y: .2 },
    ]);
    const outside = word('outside', 'Outside', 3, polygon(-.1, .1, .2, .2));
    const valid = word('good', 'Good', 4, polygon(.3, .3, .5, .4));

    expect(flattenSelectableWords([block('b1', 1, [line('l1', 1, [
      invalid, nonFinite, outside, valid,
    ])])]).map((item) => item.id)).toEqual(['good']);
  });

  it.each([
    [['Printed', 'Printed'], 'Printed'],
    [['Handwritten', 'Handwritten'], 'Handwritten'],
    [['Printed', 'Handwritten'], 'Mixed'],
    [['Unknown', 'Unknown'], 'Unknown'],
    [['Printed', 'Unknown'], 'Mixed'],
  ] as const)('summarizes %j words as %s', (types, expectedType) => {
    const words = flattenSelectableWords([block('b1', 1, [line('l1', 1,
      types.map((type, index) => word(
        `w${index}`,
        index === 0 ? 'Hello' : 'world',
        index,
        polygon(.1 + index * .2, .1, .25 + index * .2, .2),
        type,
        index === 0 ? .8 : 1,
      )),
    )])]);

    expect(summarizeSelection(words)).toEqual({
      phrase: 'Hello world',
      wordCount: 2,
      averageConfidence: .9,
      textType: expectedType,
    });
  });

  it('returns no summary for an empty selection', () => {
    expect(summarizeSelection([])).toBeNull();
  });
});
