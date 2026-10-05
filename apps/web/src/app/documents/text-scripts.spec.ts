import { coversText, requiredScripts } from './text-scripts';

describe('text scripts', () => {
  it('finds the writing systems that letters need, ignoring digits and punctuation', () => {
    expect([...requiredScripts('RM 1,500')]).toEqual(['Latn']);
    expect([...requiredScripts('1,500 - 2026/10/05')]).toEqual([]);
    expect([...requiredScripts('租金 1,500')]).toEqual(['Hani']);
    expect([...requiredScripts('عقد')]).toEqual(['Arab']);
    expect([...requiredScripts('สวัสดี')]).toEqual(['Thai']);
    expect([...requiredScripts('한국어')]).toEqual(['Hang']);
  });

  it('a font covers text only when it has every needed script (no list = Latin)', () => {
    expect(coversText(['Hans', 'Hani', 'Latn'], '租金 RM 1,500')).toBe(true);
    expect(coversText(['Latn'], '租金')).toBe(false);
    expect(coversText(undefined, 'RM 1,500')).toBe(true);
    expect(coversText(undefined, 'عقد')).toBe(false);
  });
});
