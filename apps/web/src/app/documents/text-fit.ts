export const TEXT_LAYOUT_VERSION = 'layout-v1';

export interface TextFitDraft {
  text: string;
  box: { x: number; y: number; width: number; height: number };
  imageWidth: number;
  imageHeight: number;
  fontSizeNormalized: number;
  letterSpacing: number;
  minimumLetterSpacing: number;
  minimumFontScale: number;
}

export interface TextMeasurement {
  width: number;
  height: number;
}

export interface TextFitResult {
  fits: boolean;
  overflow: boolean;
  fontSize: number;
  fontScale: number;
  letterSpacing: number;
  width: number;
  height: number;
  graphemeCount: number;
  steps: ReadonlyArray<{ kind: 'letterSpacing' | 'fontSize'; value: number }>;
}

export function fitSingleLine(
  measure: (text: string, fontSizePixels: number) => TextMeasurement,
  draft: TextFitDraft,
): TextFitResult {
  if (!draft.text.trim() || /[\r\n]/u.test(draft.text) || draft.text.length > 4000) {
    throw new Error('A nonempty single-line replacement is required.');
  }
  const b = draft.box;
  if (![b.x, b.y, b.width, b.height].every(Number.isFinite) ||
      b.x < 0 || b.y < 0 || b.width <= 0 || b.height <= 0 ||
      b.x + b.width > 1 || b.y + b.height > 1 ||
      !Number.isInteger(draft.imageWidth) || !Number.isInteger(draft.imageHeight) ||
      draft.imageWidth <= 0 || draft.imageWidth > 20000 ||
      draft.imageHeight <= 0 || draft.imageHeight > 20000 ||
      !Number.isFinite(draft.fontSizeNormalized) ||
      draft.fontSizeNormalized <= 0 || draft.fontSizeNormalized > 1 ||
      !Number.isFinite(draft.letterSpacing) ||
      draft.letterSpacing < -0.1 || draft.letterSpacing > 3 ||
      !Number.isFinite(draft.minimumLetterSpacing) ||
      draft.minimumLetterSpacing < -0.1 ||
      draft.minimumLetterSpacing > draft.letterSpacing ||
      !Number.isFinite(draft.minimumFontScale) ||
      draft.minimumFontScale <= 0 || draft.minimumFontScale > 1) {
    throw new Error('Invalid text fitting dimensions or style.');
  }

  const count = [...draft.text].length;
  const initialPixels = draft.fontSizeNormalized * draft.imageHeight;
  const availableWidth = b.width * draft.imageWidth;
  const availableHeight = b.height * draft.imageHeight;
  const measured = measure(draft.text, initialPixels);
  if (!Number.isFinite(measured.width) || measured.width < 0 ||
      !Number.isFinite(measured.height) || measured.height <= 0) {
    throw new Error('Text metrics must be finite and nonnegative.');
  }

  const steps: Array<{ kind: 'letterSpacing' | 'fontSize'; value: number }> = [];
  let spacing = draft.letterSpacing;
  let fullWidth = measured.width + Math.max(0, count - 1) * spacing * initialPixels;
  if (fullWidth > availableWidth && count > 1) {
    const needed = (availableWidth - measured.width) / ((count - 1) * initialPixels);
    spacing = Math.min(draft.letterSpacing, Math.max(draft.minimumLetterSpacing, needed));
    if (spacing < draft.letterSpacing) {
      steps.push({ kind: 'letterSpacing', value: spacing });
    }
    fullWidth = measured.width + (count - 1) * spacing * initialPixels;
  }

  const requiredScale = Math.min(1,
    Math.min(fullWidth <= 0 ? 1 : availableWidth / fullWidth,
      availableHeight / measured.height));
  const scale = Math.min(1, Math.max(draft.minimumFontScale, requiredScale));
  if (scale < 1) {
    steps.push({ kind: 'fontSize', value: scale });
  }
  const width = fullWidth * scale;
  const height = measured.height * scale;
  const fits = width <= availableWidth + 0.000001 &&
    height <= availableHeight + 0.000001;
  return {
    fits,
    overflow: !fits,
    fontSize: draft.fontSizeNormalized * scale,
    fontScale: scale,
    letterSpacing: spacing,
    width,
    height,
    graphemeCount: count,
    steps,
  };
}
