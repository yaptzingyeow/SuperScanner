import { CropState } from './document.models';
import { cropGuidance } from './import-review.component';

describe('cropGuidance (fallback when the server sends none)', () => {
  const state = (source: string, confidence: number | null) => ({ source, confidence, status: 'Ready' }) as unknown as CropState;

  it('trusts clear edges from the automatic detector as it does the AI model', () => {
    expect(cropGuidance(state('Automatic', .99))).toBe('accurate');
    expect(cropGuidance(state('Automatic', .85))).toBe('accurate');
    expect(cropGuidance(state('Automatic', .8))).toBe('verify');
    expect(cropGuidance(state('Ai', .8))).toBe('accurate');
    expect(cropGuidance(state('FullImage', 0))).toBe('manual');
  });
});
