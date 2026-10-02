export type EditToolId = 'crop' | 'clean' | 'text' | 'add' | 'sign' | 'mark' | 'ocr';

export interface EditTool {
  id: EditToolId;
  label: string;
  title: string;
  body: string;
  steps: readonly [string, string, string];
  action: string;
  /** Route segment under /documents/:id/pages/:pageId. */
  path: 'crop' | 'clean' | 'text';
  /** Value of ?tool= understood by the text editor. */
  query?: 'add' | 'signature' | 'mark' | 'ocr';
}

/** Tools of the page editor, which opens inside the workspace instead of on its own page. */
export function opensInWorkspace(tool: EditTool): boolean {
  return tool.path === 'text';
}

export const EDIT_TOOLS: readonly EditTool[] = [
  { id: 'crop', label: 'Crop & look', path: 'crop', title: 'Crop & look', action: 'Open crop & look',
    body: 'Move the four corners, rotate the page, or switch between Magic scan and the other looks. Your original photo is kept.',
    steps: ['Drag each corner to the paper edge, or use Auto detect.', 'Choose a look; Magic scan is the default for photos.', 'Save to update the page.'] },
  { id: 'clean', label: 'Clean page', path: 'clean', title: 'Clean page', action: 'Start cleaning',
    body: 'Remove punch holes, staple marks or small debris. Only the areas you select change.',
    steps: ['Find punch holes, or paint over a mark.', 'Preview the cleanup.', 'Apply, or undo later from History.'] },
  { id: 'text', label: 'Edit text', path: 'text', title: 'Edit printed text', action: 'Select words',
    body: 'Select recognized words and type a replacement. The background behind the old text is rebuilt from the paper around it.',
    steps: ['Drag across the words you want to change.', 'Type the new text and pick a font.', 'Preview the exact result, then apply.'] },
  { id: 'add', label: 'Add text', path: 'text', query: 'add', title: 'Add text', action: 'Place a text box',
    body: 'Place new printed text anywhere on the page, in any of the bundled fonts.',
    steps: ['Click where the text should go.', 'Type and style it.', 'Preview, then apply.'] },
  { id: 'sign', label: 'Signature', path: 'text', query: 'signature', title: 'Add a signature', action: 'Add signature',
    body: 'Upload a signature photo or draw one, then place and resize it. A visual signature only, not a certified e-signature.',
    steps: ['Upload or draw.', 'Drag it into place and resize.', 'Save.'] },
  { id: 'mark', label: 'Tick / Cross', path: 'text', query: 'mark', title: 'Tick / Cross', action: 'Start placing marks',
    body: 'Click a checkbox on the form to place a tick or cross. Change colour, size and stroke.',
    steps: ['Choose tick or cross.', 'Click each box on the page.', 'Save the marks.'] },
  { id: 'ocr', label: 'Recognize text', path: 'text', query: 'ocr', title: 'Recognize text', action: 'Recognize this page',
    body: 'Reads the words on this page so you can select, edit and search them, and export a searchable PDF.',
    steps: ['Run recognition on this page.', 'Wait a few seconds.', 'Select or edit words.'] },
];
