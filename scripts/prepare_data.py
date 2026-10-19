import csv, hashlib, json, pathlib, urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
def get_json(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'SPAM-Chinese-development/0.1'})
    with urllib.request.urlopen(request, timeout=60) as r:
        obj = json.load(r)
        return json.loads(obj) if isinstance(obj, str) else obj

def download_fonts():
    target = ROOT / 'data' / 'fonts'
    target.mkdir(parents=True, exist_ok=True)
    provenance = []
    for folder, filename in [('notosanssc', 'Verified.ttf'), ('notoserifsc', 'Draft.ttf')]:
        listing = get_json('https://api.github.com/repos/google/fonts/contents/ofl/' + folder)
        for item in listing:
            if item['name'].endswith('.ttf') or item['name'] == 'OFL.txt':
                path = target / (filename if item['name'].endswith('.ttf') else folder + '-OFL.txt')
                if not path.exists():
                    urllib.request.urlretrieve(item['download_url'], path)
                provenance.append({'file': path.name, 'url': item['download_url'], 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    (target / 'sources.json').write_text(json.dumps(provenance, ensure_ascii=False, indent=2), encoding='utf-8')

def export_sources():
    entries = []
    for path in sorted((ROOT / 'analysis-local' / 'text-assets').glob('*.txt')):
        with path.open(encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                key, source = (row.get('Key') or '').strip(), (row.get('English') or '').replace('\r', '').strip()
                if not key: continue
                entries.append({'key': key, 'source': source, 'sourceHash': hashlib.sha256(source.encode()).hexdigest(), 'sheet': path.stem})
    assert len({e['key'] for e in entries}) == len(entries), 'Duplicate localization key'
    (ROOT / 'data' / 'sources.json').write_text(json.dumps({'schema': 1, 'entries': entries}, ensure_ascii=False, indent=2), encoding='utf-8')
    print('Exported', len(entries), 'source entries')

if __name__ == '__main__':
    export_sources()
    print('Prepare portable v2 fonts separately with: py -3.13 scripts/prepare_fonts.py')
    print('Portable fonts ready')
