/** Which writing systems (ISO 15924) text needs; mirrors TextScripts on the server. */
const RANGES: [number, number, string][] = [
  [0x0041, 0x024F, 'Latn'], [0x1E00, 0x1EFF, 'Latn'],
  [0x0370, 0x03FF, 'Grek'], [0x1F00, 0x1FFF, 'Grek'],
  [0x0400, 0x052F, 'Cyrl'],
  [0x0590, 0x05FF, 'Hebr'],
  [0x0600, 0x06FF, 'Arab'], [0x0750, 0x077F, 'Arab'], [0x08A0, 0x08FF, 'Arab'], [0xFB50, 0xFDFF, 'Arab'], [0xFE70, 0xFEFF, 'Arab'],
  [0x0900, 0x097F, 'Deva'],
  [0x0980, 0x09FF, 'Beng'],
  [0x0B80, 0x0BFF, 'Taml'],
  [0x0E00, 0x0E7F, 'Thai'],
  [0x1100, 0x11FF, 'Hang'], [0x3130, 0x318F, 'Hang'], [0xAC00, 0xD7AF, 'Hang'],
  [0x3040, 0x309F, 'Hira'],
  [0x30A0, 0x30FF, 'Kana'], [0x31F0, 0x31FF, 'Kana'],
  [0x3400, 0x4DBF, 'Hani'], [0x4E00, 0x9FFF, 'Hani'], [0xF900, 0xFAFF, 'Hani'], [0x20000, 0x2FA1F, 'Hani'],
];

export function requiredScripts(text: string): Set<string> {
  const scripts = new Set<string>();
  for (const character of text) {
    if (!/[\p{L}\p{M}]/u.test(character)) continue;
    const code = character.codePointAt(0)!;
    const range = RANGES.find(([from, to]) => code >= from && code <= to);
    if (range) scripts.add(range[2]);
  }
  return scripts;
}

export function coversText(fontScripts: readonly string[] | undefined, text: string): boolean {
  const available = fontScripts?.length ? fontScripts : ['Latn'];
  return [...requiredScripts(text)].every((script) => available.includes(script));
}
