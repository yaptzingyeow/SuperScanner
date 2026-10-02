import { ScanFilterId } from './document.models';

export interface ScanLook {
  id: ScanFilterId;
  label: string;
  help: string;
}

/** The seven looks offered when importing and editing pages, in display order. */
export const SCAN_LOOKS: readonly ScanLook[] = [
  { id: 'Magic', label: 'Magic scan', help: 'Recommended. Flattens the page, turns paper white and ink crisp, clears desk edges and punch holes. Colourful cards keep their colours.' },
  { id: 'Original', label: 'Original', help: 'The flattened photo with its own colours and lighting. Nothing is cleaned.' },
  { id: 'Document', label: 'Document', help: 'Gentle contrast and sharpening.' },
  { id: 'Bright', label: 'Bright', help: 'Lightens a dark photo while keeping its colours.' },
  { id: 'RemoveShadows', label: 'No shadow', help: 'Evens out uneven lighting while keeping ink colours and faint strokes.' },
  { id: 'Grayscale', label: 'Grayscale', help: 'Removes colour and keeps shades of grey.' },
  { id: 'BlackAndWhite', label: 'Black & white', help: 'Crisp black text on white. Faint handwriting may disappear.' },
];

/**
 * Shown for pages saved with a look that is no longer offered (Smart clean,
 * Clean content). The server still renders those filters, so such pages keep
 * working until the user picks another look.
 */
export const RETIRED_LOOK_HELP = 'This page uses a look that is no longer offered. Choose one of the looks above to replace it.';

/** The offered look a stored filter belongs to, or null for a retired filter. */
export function lookForFilter(filter: ScanFilterId | string | null | undefined): ScanLook | null {
  return SCAN_LOOKS.find((look) => look.id === filter) ?? null;
}
