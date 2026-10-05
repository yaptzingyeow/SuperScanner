"""Reads each bundled font's character map and records which writing systems it covers in manifest.json.

A font covers a script when it has glyphs for that script's sample letters. Run after adding fonts:
    python tools/font-catalogue/font_scripts.py
"""
import json, os, struct

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
MANIFEST = os.path.join(ROOT, 'assets', 'fonts', 'manifest.json')

# ISO 15924 code -> sample letters every face for that script must contain.
SAMPLES = {
    'Latn': 'AaZzéñçøß',
    'Grek': 'ΑαΩωλ',
    'Cyrl': 'АаЯяЖж',
    'Hani': '中国租金協議',
    'Hira': 'あいうえお',
    'Kana': 'アイウエオ',
    'Hang': '한국어가나',
    'Arab': 'عقدإيجار',
    'Hebr': 'שלוםא',
    'Thai': 'สวัสดี',
    'Deva': 'नमस्ते',
    'Taml': 'வணக்கம',
    'Beng': 'নমস্কার',
}


def cmap(path):
    data = open(path, 'rb').read()
    tables = {}
    count = struct.unpack('>H', data[4:6])[0]
    for i in range(count):
        tag, _, offset, _ = struct.unpack('>4sIII', data[12 + 16 * i: 28 + 16 * i])
        tables[tag] = offset
    base = tables[b'cmap']
    codepoints = set()
    for i in range(struct.unpack('>H', data[base + 2: base + 4])[0]):
        platform, encoding, offset = struct.unpack('>HHI', data[base + 4 + 8 * i: base + 12 + 8 * i])
        sub = base + offset
        fmt = struct.unpack('>H', data[sub: sub + 2])[0]
        if fmt == 4 and platform in (0, 3):
            segs = struct.unpack('>H', data[sub + 6: sub + 8])[0] // 2
            ends = struct.unpack(f'>{segs}H', data[sub + 14: sub + 14 + 2 * segs])
            starts = struct.unpack(f'>{segs}H', data[sub + 16 + 2 * segs: sub + 16 + 4 * segs])
            for start, end in zip(starts, ends):
                if start != 0xFFFF:
                    codepoints.update(range(start, end + 1))
        elif fmt == 12 and platform in (0, 3):
            groups = struct.unpack('>I', data[sub + 12: sub + 16])[0]
            for g in range(groups):
                start, end, _ = struct.unpack('>III', data[sub + 16 + 12 * g: sub + 28 + 12 * g])
                codepoints.update(range(start, end + 1))
    return codepoints


def main():
    manifest = json.load(open(MANIFEST, encoding='utf-8-sig'))
    for face in manifest['faces']:
        points = cmap(os.path.join(ROOT, face['rendererAssetPath']))
        scripts = [code for code, sample in SAMPLES.items() if all(ord(ch) in points for ch in sample)]
        if 'Hani' in scripts:
            # Simplified/Traditional/Japanese/Korean style is a design choice the family name carries.
            scripts += {'noto-sans-sc': ['Hans'], 'noto-sans-tc': ['Hant'], 'noto-sans-jp': ['Jpan'],
                        'noto-sans-kr': ['Kore']}.get(face['catalogueId'], [])
        face['scripts'] = scripts
        print(f"{face['catalogueId']:22} {face['version']:30} {' '.join(scripts)}")
    json.dump(manifest, open(MANIFEST, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    open(MANIFEST, 'a', encoding='utf-8').write('\n')


if __name__ == '__main__':
    main()
