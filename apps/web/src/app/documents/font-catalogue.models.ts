export interface FontFaceEntry {
  catalogueId: string;
  version: string;
  displayName: string;
  familyName: string;
  category: 'SansSerif' | 'Serif' | 'Monospace' | 'Handwriting';
  weight: number;
  style: 'Normal' | 'Italic';
  webFamilyName: string;
  webAssetUrl: string;
  enabled: boolean;
  /** ISO 15924 scripts the font can draw (absent = Latin only). */
  scripts?: string[];
}
