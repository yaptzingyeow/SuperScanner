"""Adds approved SIL OFL handwriting families to the pinned font catalogue.

Downloads static faces from google/fonts at the catalogue's pinned commit, checks each licence is
OFL 1.1, copies the binary to the server and web font folders, and appends hashed manifest entries.
Families already in the manifest are refused rather than replaced. Run from any directory:
    python tools/font-catalogue/import-handwriting-fonts.py
"""
import hashlib, json, os, re, shutil, urllib.parse, urllib.request

COMMIT = '23e54b51ddffbc7713c583748e3bd86f62b1fa4a'
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SERVER = os.path.join(ROOT, 'assets', 'fonts')
WEB = os.path.join(ROOT, 'apps', 'web', 'public', 'assets', 'fonts')
LICENSES = os.path.join(SERVER, 'licenses')
MANIFEST = os.path.join(SERVER, 'manifest.json')

# catalogue id -> (display name, google/fonts folder, regular file, bold file or None)
FAMILIES = {
    'kalam': ('Kalam', 'kalam', 'Kalam-Regular.ttf', 'Kalam-Bold.ttf'),
    'patrick-hand': ('Patrick Hand', 'patrickhand', 'PatrickHand-Regular.ttf', None),
    'indie-flower': ('Indie Flower', 'indieflower', 'IndieFlower-Regular.ttf', None),
    'shadows-into-light': ('Shadows Into Light', 'shadowsintolight', 'ShadowsIntoLight.ttf', None),
    'gloria-hallelujah': ('Gloria Hallelujah', 'gloriahallelujah', 'GloriaHallelujah.ttf', None),
    'architects-daughter': ('Architects Daughter', 'architectsdaughter', 'ArchitectsDaughter-Regular.ttf', None),
    'gochi-hand': ('Gochi Hand', 'gochihand', 'GochiHand-Regular.ttf', None),
    'covered-by-your-grace': ('Covered By Your Grace', 'coveredbyyourgrace', 'CoveredByYourGrace.ttf', None),
    'great-vibes': ('Great Vibes', 'greatvibes', 'GreatVibes-Regular.ttf', None),
    'sacramento': ('Sacramento', 'sacramento', 'Sacramento-Regular.ttf', None),
    'allura': ('Allura', 'allura', 'Allura-Regular.ttf', None),
    'kaushan-script': ('Kaushan Script', 'kaushanscript', 'KaushanScript-Regular.ttf', None),
}


def fetch(folder, name):
    url = f'https://raw.githubusercontent.com/google/fonts/{COMMIT}/ofl/{folder}/{urllib.parse.quote(name)}'
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def main():
    with open(MANIFEST, encoding='utf-8') as handle:
        manifest = json.load(handle)
    existing = {face['catalogueId'] for face in manifest['faces']}
    os.makedirs(LICENSES, exist_ok=True)
    for catalogue_id, (name, folder, regular, bold) in FAMILIES.items():
        if catalogue_id in existing:
            raise SystemExit(f'Font family already exists in manifest: {catalogue_id}')
        notice = fetch(folder, 'OFL.txt')
        if not re.search(rb'SIL OPEN FONT LICENSE\s*Version 1\.1', notice, re.IGNORECASE):
            raise SystemExit(f'Unexpected font license for {catalogue_id}')
        notice_path = os.path.join(LICENSES, f'{catalogue_id}-OFL.txt')
        with open(notice_path, 'wb') as handle:
            handle.write(notice)
        for style, source, weight in (('Regular', regular, 400), ('Bold', bold, 700)):
            if source is None:
                continue
            data = fetch(folder, source)
            local = f"{name.replace(' ', '')}-{style}.ttf"
            target = os.path.join(SERVER, local)
            with open(target, 'wb') as handle:
                handle.write(data)
            shutil.copyfile(target, os.path.join(WEB, local))
            manifest['faces'].append({
                'catalogueId': catalogue_id,
                'version': f'gfonts-{COMMIT[:8]}-{style.lower()}',
                'displayName': f'{name} {style}',
                'familyName': name,
                'category': 'Handwriting',
                'weight': weight,
                'style': 'Normal',
                'webFamilyName': f'ArksScanner {name} v1',
                'selectableForNewEdits': True,
                'assetSha256Hex': hashlib.sha256(data).hexdigest(),
                'licenseIdentifier': 'OFL-1.1',
                'licenseNoticePath': f'assets/fonts/licenses/{catalogue_id}-OFL.txt',
                'webAssetPath': f'assets/fonts/{local}',
                'rendererAssetPath': f'assets/fonts/{local}',
                'enabled': True,
                'scripts': ['Latn'],
            })
            print(f'Added {name} {style}')
    text = json.dumps(manifest, indent=2, ensure_ascii=False).replace('\n', '\r\n') + '\r\n'
    with open(MANIFEST, 'w', encoding='utf-8', newline='') as handle:
        handle.write(text)


if __name__ == '__main__':
    main()
