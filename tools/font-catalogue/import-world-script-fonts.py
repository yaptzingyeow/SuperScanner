"""Adds Noto fonts for non-Latin scripts to the bundled catalogue (server + web copies, manifest, licences).

Same source and pinned commit as import-approved-fonts.ps1 (google/fonts, OFL-1.1). Run once:
    python tools/font-catalogue/import-world-script-fonts.py
"""
import hashlib, json, os, re, shutil, urllib.parse, urllib.request

COMMIT = '23e54b51ddffbc7713c583748e3bd86f62b1fa4a'
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SERVER = os.path.join(ROOT, 'assets', 'fonts')
WEB = os.path.join(ROOT, 'apps', 'web', 'public', 'assets', 'fonts')
LICENSES = os.path.join(SERVER, 'licenses')
MANIFEST = os.path.join(SERVER, 'manifest.json')

# (catalogue id, display name, google/fonts folder, file, category, ISO 15924 scripts it covers)
FAMILIES = [
    ('noto-sans-sc', 'Noto Sans SC', 'notosanssc', 'NotoSansSC[wght].ttf', 'SansSerif', ['Hans', 'Hani', 'Latn']),
    ('noto-sans-tc', 'Noto Sans TC', 'notosanstc', 'NotoSansTC[wght].ttf', 'SansSerif', ['Hant', 'Hani', 'Latn']),
    ('noto-sans-jp', 'Noto Sans JP', 'notosansjp', 'NotoSansJP[wght].ttf', 'SansSerif', ['Jpan', 'Hira', 'Kana', 'Hani', 'Latn']),
    ('noto-sans-kr', 'Noto Sans KR', 'notosanskr', 'NotoSansKR[wght].ttf', 'SansSerif', ['Kore', 'Hang', 'Hani', 'Latn']),
    ('noto-naskh-arabic', 'Noto Naskh Arabic', 'notonaskharabic', 'NotoNaskhArabic[wght].ttf', 'Serif', ['Arab']),
    ('noto-sans-hebrew', 'Noto Sans Hebrew', 'notosanshebrew', 'NotoSansHebrew[wdth,wght].ttf', 'SansSerif', ['Hebr']),
    ('noto-sans-thai', 'Noto Sans Thai', 'notosansthai', 'NotoSansThai[wdth,wght].ttf', 'SansSerif', ['Thai']),
    ('noto-sans-devanagari', 'Noto Sans Devanagari', 'notosansdevanagari', 'NotoSansDevanagari[wdth,wght].ttf', 'SansSerif', ['Deva']),
    ('noto-sans-tamil', 'Noto Sans Tamil', 'notosanstamil', 'NotoSansTamil[wdth,wght].ttf', 'SansSerif', ['Taml']),
    ('noto-sans-bengali', 'Noto Sans Bengali', 'notosansbengali', 'NotoSansBengali[wdth,wght].ttf', 'SansSerif', ['Beng']),
]


def fetch(folder, name):
    url = f'https://raw.githubusercontent.com/google/fonts/{COMMIT}/ofl/{folder}/{urllib.parse.quote(name)}'
    with urllib.request.urlopen(url, timeout=120) as response:
        return response.read()


def sha256(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()


def main():
    os.makedirs(LICENSES, exist_ok=True)
    os.makedirs(WEB, exist_ok=True)
    manifest = json.load(open(MANIFEST, encoding='utf-8-sig'))
    existing = {face['catalogueId'] for face in manifest['faces']}
    for family_id, name, folder, source, category, scripts in FAMILIES:
        if family_id in existing:
            print('skip (already present):', family_id)
            continue
        licence = fetch(folder, 'OFL.txt')
        if not re.search(rb'(?is)SIL OPEN FONT LICENSE\s*Version 1\.1', licence):
            raise SystemExit(f'Unexpected licence for {family_id}')
        open(os.path.join(LICENSES, f'{family_id}-OFL.txt'), 'wb').write(licence)
        local = name.replace(' ', '') + '-Regular.ttf'
        server_path = os.path.join(SERVER, local)
        open(server_path, 'wb').write(fetch(folder, source))
        # Large world-script fonts keep one copy; the API serves it to the web app (webDelivery: api).
        digest = sha256(server_path)
        manifest['faces'].append({
            'catalogueId': family_id,
            'version': f'gfonts-{COMMIT[:8]}-regular',
            'displayName': f'{name} Regular',
            'familyName': name,
            'category': category,
            'weight': 400,
            'style': 'Normal',
            'webFamilyName': f'ArksScanner {name} v1',
            'selectableForNewEdits': True,
            'assetSha256Hex': digest,
            'licenseIdentifier': 'OFL-1.1',
            'licenseNoticePath': f'assets/fonts/licenses/{family_id}-OFL.txt',
            'webAssetPath': f'assets/fonts/{local}',
            'rendererAssetPath': f'assets/fonts/{local}',
            'enabled': True,
            'scripts': scripts,
            'webDelivery': 'api',
        })
        print(f'added {family_id}: {os.path.getsize(server_path) / 1e6:.1f} MB')
    json.dump(manifest, open(MANIFEST, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    open(MANIFEST, 'a', encoding='utf-8').write('\n')


if __name__ == '__main__':
    main()
