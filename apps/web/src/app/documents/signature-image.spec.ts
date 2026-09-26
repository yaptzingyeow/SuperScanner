import { describe, expect, it } from 'vitest';
import { removeLightBackground, prepareSignature } from './signature-image';

describe('signature image processing', () => {
  it('removes white paper but preserves dark ink and original transparent pixels', () => {
    const source = new Uint8ClampedArray([255,255,255,255, 20,20,20,255, 0,0,0,0]);
    const result = removeLightBackground(source, .5, false);
    expect([...result]).toEqual([255,255,255,0, 20,20,20,255, 0,0,0,0]);
    expect(source[3]).toBe(255);
  });
  it('strength changes only draft alpha and keep original preserves all pixels', () => {
    const source = new Uint8ClampedArray([200,200,200,180]);
    expect(removeLightBackground(source, .9, false)[3]).toBeLessThan(removeLightBackground(source, .1, false)[3]);
    expect([...removeLightBackground(source, .9, true)]).toEqual([200,200,200,180]);
  });
  it('rejects SVG before decoding', async () => {
    await expect(prepareSignature(new File(['<svg/>'], 'ink.svg', { type: 'image/svg+xml' }), .5, false)).rejects.toThrow();
  });
});
