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
}
