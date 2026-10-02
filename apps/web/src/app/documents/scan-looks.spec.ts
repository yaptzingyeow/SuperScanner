import { SCAN_LOOKS, lookForFilter } from './scan-looks';

describe('scan looks', () => {
  it('lists the seven looks in the agreed order with their filter ids', () => {
    expect(SCAN_LOOKS.map((look) => [look.label, look.id])).toEqual([
      ['Magic scan', 'Magic'],
      ['Original', 'Original'],
      ['Document', 'Document'],
      ['Bright', 'Bright'],
      ['No shadow', 'RemoveShadows'],
      ['Grayscale', 'Grayscale'],
      ['Black & white', 'BlackAndWhite'],
    ]);
  });

  it('no longer offers Smart clean or Clean content', () => {
    const labels = SCAN_LOOKS.map((look) => look.label);
    expect(labels).not.toContain('Smart clean');
    expect(labels).not.toContain('Clean content');
  });

  it('finds the look of an offered filter', () => {
    expect(lookForFilter('Bright')?.label).toBe('Bright');
  });

  it('returns null for retired filters that existing pages may still use', () => {
    expect(lookForFilter('CleanDocumentStrong')).toBeNull();
    expect(lookForFilter('CleanDocument')).toBeNull();
    expect(lookForFilter('ContentClean')).toBeNull();
  });
});
