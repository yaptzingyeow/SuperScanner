/** A user's own watermark on exported pages, e.g. "FOR GOVERNMENT USE ONLY". Mirrors the server's ExportWatermark. */
export interface ExportWatermarkSettings {
  text: string;
  /** Single: once per page. Tiled: repeated across the whole page. */
  layout: 'Single' | 'Tiled';
  fontId: string;
  bold: boolean;
  /** #RRGGBB */
  color: string;
  /** 0.05 – 1 */
  opacity: number;
  /** Text height as % of the page's shorter side, 2 – 25. */
  sizePercent: number;
  /** Counter-clockwise rotation, -90 – 90. */
  angleDegrees: number;
  /** Gap between repeated copies in text heights, 0.5 – 4 (Tiled). */
  spacing: number;
  /** Where a Single watermark sits. */
  position: 'Top' | 'Center' | 'Bottom';
}

export interface WatermarkFont {
  id: string;
  label: string;
  /** CSS font-family for the live preview (same TTF the PDF uses). */
  family: string;
  hasBold: boolean;
}

export const WATERMARK_FONTS: readonly WatermarkFont[] = [
  { id: 'noto-sans', label: 'Noto Sans', family: 'Arks WM Noto Sans', hasBold: true },
  { id: 'liberation-sans', label: 'Arial style', family: 'Arks WM Liberation Sans', hasBold: true },
  { id: 'carlito', label: 'Calibri style', family: 'Arks WM Carlito', hasBold: true },
  { id: 'poppins', label: 'Poppins', family: 'Arks WM Poppins', hasBold: true },
  { id: 'lato', label: 'Lato', family: 'Arks WM Lato', hasBold: true },
  { id: 'oswald', label: 'Oswald (stamp)', family: 'Arks WM Oswald', hasBold: false },
  { id: 'montserrat', label: 'Montserrat', family: 'Arks WM Montserrat', hasBold: false },
  { id: 'noto-serif', label: 'Noto Serif', family: 'Arks WM Noto Serif', hasBold: true },
  { id: 'liberation-serif', label: 'Times style', family: 'Arks WM Liberation Serif', hasBold: true },
  { id: 'liberation-mono', label: 'Typewriter', family: 'Arks WM Liberation Mono', hasBold: true },
  { id: 'caveat', label: 'Handwriting', family: 'Arks WM Caveat', hasBold: false },
  { id: 'dancing-script', label: 'Script', family: 'Arks WM Dancing Script', hasBold: false },
];

export const WATERMARK_PRESETS: readonly string[] = [
  'FOR GOVERNMENT USE ONLY',
  'FOR IC VERIFICATION ONLY',
  'CONFIDENTIAL',
  'COPY',
  'DRAFT',
  'SAMPLE',
];

export const WATERMARK_COLORS: readonly { value: string; label: string }[] = [
  { value: '#C62828', label: 'Red' },
  { value: '#1A237E', label: 'Navy' },
  { value: '#212121', label: 'Black' },
  { value: '#757575', label: 'Grey' },
  { value: '#2E7D32', label: 'Green' },
  { value: '#EF6C00', label: 'Orange' },
];

export const DEFAULT_WATERMARK: ExportWatermarkSettings = {
  text: 'FOR GOVERNMENT USE ONLY',
  layout: 'Single',
  fontId: 'noto-sans',
  bold: true,
  color: '#C62828',
  opacity: 0.25,
  sizePercent: 8,
  angleDegrees: 35,
  spacing: 1.5,
  position: 'Center',
};

export function watermarkFont(id: string): WatermarkFont {
  return WATERMARK_FONTS.find((font) => font.id === id) ?? WATERMARK_FONTS[0];
}

/** Settings as the server stores them (trimmed text, bold only where the font has it, upper-case colour). */
export function normalizeWatermark(settings: ExportWatermarkSettings): ExportWatermarkSettings {
  return {
    ...settings,
    text: settings.text.trim(),
    bold: settings.bold && watermarkFont(settings.fontId).hasBold,
    color: settings.color.toUpperCase(),
  };
}

/** True when two exports would carry the same watermark (both none counts as the same). */
export function sameWatermark(a: ExportWatermarkSettings | null | undefined, b: ExportWatermarkSettings | null | undefined): boolean {
  if (!a || !b) return !a && !b;
  const x = normalizeWatermark(a);
  const y = normalizeWatermark(b);
  return x.text === y.text && x.layout === y.layout && x.fontId === y.fontId && x.bold === y.bold &&
    x.color === y.color && Math.abs(x.opacity - y.opacity) < 1e-6 && Math.abs(x.sizePercent - y.sizePercent) < 1e-6 &&
    Math.abs(x.angleDegrees - y.angleDegrees) < 1e-6 && Math.abs(x.spacing - y.spacing) < 1e-6 &&
    (x.layout === 'Tiled' || x.position === y.position);
}
