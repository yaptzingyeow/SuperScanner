"""Turns the catalogue's variable fonts into true static Regular (400) and Bold (700) faces.

Variable files carry a default instance that is often Thin or Light; renderers that do not pick an
instance (ImageMagick/FreeType here) draw that default. Run with the fontTools environment:
    .task-tools/crop-runtime/Scripts/python.exe tools/font-catalogue/instance-variable-fonts.py
"""
import hashlib, io, json, os, shutil, urllib.parse, urllib.request
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

COMMIT = '23e54b51ddffbc7713c583748e3bd86f62b1fa4a'
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SERVER = os.path.join(ROOT, 'assets', 'fonts')
WEB = os.path.join(ROOT, 'apps', 'web', 'public', 'assets', 'fonts')
MANIFEST = os.path.join(SERVER, 'manifest.json')

# catalogue id -> (google/fonts folder, variable file, add a Bold face?)
VARIABLE = {
    'roboto': ('roboto', 'Roboto[wdth,wght].ttf', True),
    'open-sans': ('opensans', 'OpenSans[wdth,wght].ttf', True),
    'montserrat': ('montserrat', 'Montserrat[wght].ttf', True),
    'source-sans-3': ('sourcesans3', 'SourceSans3[wght].ttf', True),
    'oswald': ('oswald', 'Oswald[wght].ttf', True),
    'source-serif-4': ('sourceserif4', 'SourceSerif4[opsz,wght].ttf', True),
    'merriweather': ('merriweather', 'Merriweather[opsz,wdth,wght].ttf', True),
    'libre-baskerville': ('librebaskerville', 'LibreBaskerville[wght].ttf', True),
    'noto-sans-mono': ('notosansmono', 'NotoSansMono[wdth,wght].ttf', True),
    'caveat': ('caveat', 'Caveat[wght].ttf', True),
    'dancing-script': ('dancingscript', 'DancingScript[wght].ttf', True),
    # CJK bold would add ~40 MB; Regular only for now.
    'noto-sans-sc': ('notosanssc', 'NotoSansSC[wght].ttf', False),
    'noto-sans-tc': ('notosanstc', 'NotoSansTC[wght].ttf', False),
    'noto-sans-jp': ('notosansjp', 'NotoSansJP[wght].ttf', False),
    'noto-sans-kr': ('notosanskr', 'NotoSansKR[wght].ttf', False),
    'noto-naskh-arabic': ('notonaskharabic', 'NotoNaskhArabic[wght].ttf', True),
    'noto-sans-hebrew': ('notosanshebrew', 'NotoSansHebrew[wdth,wght].ttf', True),
    'noto-sans-thai': ('notosansthai', 'NotoSansThai[wdth,wght].ttf', True),
    'noto-sans-devanagari': ('notosansdevanagari', 'NotoSansDevanagari[wdth,wght].ttf', True),
    'noto-sans-tamil': ('notosanstamil', 'NotoSansTamil[wdth,wght].ttf', True),
    'noto-sans-bengali': ('notosansbengali', 'NotoSansBengali[wdth,wght].ttf', True),
}


def download(folder, name):
    url = f'https://raw.githubusercontent.com/google/fonts/{COMMIT}/ofl/{folder}/{urllib.parse.quote(name)}'
    with urllib.request.urlopen(url, timeout=300) as response:
        return response.read()


def instance(data, weight):
    font = TTFont(io.BytesIO(data))
    axes = {axis.axisTag: axis for axis in font['fvar'].axes}
    location = {}
    for tag, axis in axes.items():
        if tag == 'wght':
            location[tag] = max(axis.minValue, min(axis.maxValue, weight))
        elif tag == 'wdth':
            location[tag] = 100 if axis.minValue <= 100 <= axis.maxValue else axis.defaultValue
        else:
            location[tag] = axis.defaultValue  # opsz etc.: the designer's default
    static = instantiateVariableFont(font, location, updateFontNames=True)
    static['OS/2'].usWeightClass = weight
    out = io.BytesIO()
    static.save(out)
    return out.getvalue()


def main():
    manifest = json.load(open(MANIFEST, encoding='utf-8-sig'))
    faces = manifest['faces']
    for family_id, (folder, source, add_bold) in VARIABLE.items():
        regular = next(f for f in faces if f['catalogueId'] == family_id and f['weight'] == 400)
        variable = download(folder, source)
        wght = next(a for a in TTFont(io.BytesIO(variable))['fvar'].axes if a.axisTag == 'wght')
        targets = [(400, regular)]
        if add_bold and wght.maxValue >= 700 and not any(f['catalogueId'] == family_id and f['weight'] == 700 for f in faces):
            bold = dict(regular)
            bold.update(version=regular['version'].replace('-regular', '-bold'),
                        displayName=regular['displayName'].replace(' Regular', ' Bold'), weight=700,
                        webAssetPath=regular['webAssetPath'].replace('-Regular.ttf', '-Bold.ttf'),
                        rendererAssetPath=regular['rendererAssetPath'].replace('-Regular.ttf', '-Bold.ttf'))
            faces.insert(faces.index(regular) + 1, bold)
            targets.append((700, bold))
        for weight, face in targets:
            data = instance(variable, weight)
            server_path = os.path.join(ROOT, face['rendererAssetPath'])
            open(server_path, 'wb').write(data)
            if face.get('webDelivery') != 'api':
                shutil.copyfile(server_path, os.path.join(ROOT, 'apps', 'web', 'public', face['webAssetPath']))
            face['assetSha256Hex'] = hashlib.sha256(data).hexdigest()
            print(f'{family_id:22} {weight}  {len(data) / 1e6:5.1f} MB  {face["rendererAssetPath"]}')
    json.dump(manifest, open(MANIFEST, 'w', encoding='utf-8'), indent=2, ensure_ascii=False)
    open(MANIFEST, 'a', encoding='utf-8').write('\n')


if __name__ == '__main__':
    main()
