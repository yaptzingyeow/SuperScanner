/** Local light-paper removal, not a general photographic background remover. */
export function removeLightBackground(source: Uint8ClampedArray, strength: number, keepOriginal: boolean): Uint8ClampedArray {
  const result = new Uint8ClampedArray(source);
  if (keepOriginal) return result;
  const threshold = 250 - Math.max(0, Math.min(1, strength)) * 110;
  for (let i = 0; i < result.length; i += 4) {
    const lightness = Math.min(result[i], result[i + 1], result[i + 2]);
    const opacity = Math.max(0, Math.min(1, (255 - lightness) / (255 - threshold)));
    result[i + 3] = Math.round(source[i + 3] * opacity);
  }
  return result;
}

export function canvasPng(canvas: HTMLCanvasElement): Promise<Blob> {
  return new Promise((resolve, reject) => canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('Could not prepare signature.')), 'image/png'));
}

export async function prepareSignature(file: File, removalStrength: number, keepOriginal: boolean): Promise<Blob> {
  if (!['image/png', 'image/jpeg'].includes(file.type)) throw new Error('Choose a PNG or JPEG image.');
  if (file.size > 5 * 1024 * 1024) throw new Error('Choose an image smaller than 5 MB.');
  const url = URL.createObjectURL(file);
  try {
    const image = new Image();
    await new Promise<void>((resolve, reject) => { image.onload = () => resolve(); image.onerror = () => reject(new Error('This image could not be opened.')); image.src = url; });
    if (image.naturalWidth * image.naturalHeight > 12_000_000) throw new Error('Choose an image under 12 megapixels.');
    const canvas = document.createElement('canvas');
    canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Image editing is not supported by this browser.');
    context.drawImage(image, 0, 0);
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
    pixels.data.set(removeLightBackground(pixels.data, removalStrength, keepOriginal));
    if (!pixels.data.some((value, index) => index % 4 === 3 && value > 0))
      throw new Error('No visible signature remains. Reduce removal strength or keep the original.');
    context.putImageData(pixels, 0, 0);
    return await canvasPng(canvas);
  } finally { URL.revokeObjectURL(url); }
}
