import { describe, expect, it } from 'vitest';
import { markBoxAt, markSizeBox, markViewBoxPath } from './page-mark.models';

describe('page marks', () => {
  it('centers a physical square at a zoom-independent page point and keeps it inside edges', () => {
    expect(markBoxAt(0, 0, 2, 1)).toEqual({ x: 0, y: 0, width: .025, height: .05 });
    expect(markBoxAt(.5, .5, 2, 1)).toEqual({ x: .4875, y: .475, width: .025, height: .05 });
    expect(markBoxAt(1, 1, 2, 1)).toEqual({ x: .975, y: .95, width: .025, height: .05 });
  });

  it('resizes proportionally and preserves bounds', () => {
    expect(markSizeBox({ x: .97, y: .96, width: .025, height: .04 }, 2))
      .toEqual({ x: .95, y: .92, width: .05, height: .08 });
  });

  it('uses exact shared viewBox geometry', () => {
    expect(markViewBoxPath('Check')).toBe('M18 52 L42 76 L82 24');
    expect(markViewBoxPath('Cross')).toBe('M24 24 L76 76 M76 24 L24 76');
  });
});
