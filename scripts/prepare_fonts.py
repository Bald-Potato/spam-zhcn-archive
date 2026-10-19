"""Build portable static fonts. Run with py -3.13; never install system fonts."""
import hashlib, json, pathlib, sys, urllib.request
ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT/'analysis-local/python'))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

def main():
    target=ROOT/'data/fonts'; upstream=ROOT/'vendor/font-sources'; upstream.mkdir(parents=True,exist_ok=True)
    variable=upstream/'NotoSansSC-wght.ttf'
    if not variable.exists():
        old=target/'Verified.ttf'
        if old.exists(): variable.write_bytes(old.read_bytes())
        else: urllib.request.urlretrieve('https://raw.githubusercontent.com/google/fonts/main/ofl/notosanssc/NotoSansSC%5Bwght%5D.ttf',variable)
    body=target/'Body-ExtraBold.ttf'
    if not body.exists():
        font=TTFont(variable); before=set(font.getBestCmap())
        instantiateVariableFont(font,{'wght':800},inplace=True,optimize=True,updateFontNames=True)
        font.save(body); check=TTFont(body)
        assert 'fvar' not in check and check['OS/2'].usWeightClass==800 and set(check.getBestCmap())==before
    url='https://raw.githubusercontent.com/google/fonts/a606d6f3181c62b792c8c1463d309c6a8abb8a12/ofl/zcoolkuaile/ZCOOLKuaiLe-Regular.ttf'
    title=target/'Title-KuaiLe.ttf'
    if not title.exists(): urllib.request.urlretrieve(url,title)
    assert hashlib.sha256(title.read_bytes()).hexdigest()=='812a6fc1fe54b6d73a419245c32dfeba8aa33104d5be90d1cf6af082007cb71d'
    license=target/'zcoolkuaile-OFL.txt'
    if not license.exists(): urllib.request.urlretrieve('https://raw.githubusercontent.com/google/fonts/a606d6f3181c62b792c8c1463d309c6a8abb8a12/ofl/zcoolkuaile/OFL.txt',license)
    provenance=[{'file':body.name,'upstream':'https://github.com/google/fonts/tree/main/ofl/notosanssc','upstreamSha256':hashlib.sha256(variable.read_bytes()).hexdigest(),'conversion':'fontTools 4.67.0 instantiateVariableFont wght=800; no subsetting','sha256':hashlib.sha256(body.read_bytes()).hexdigest()}, {'file':title.name,'url':url,'sha256':hashlib.sha256(title.read_bytes()).hexdigest()}]
    (target/'sources-v2.json').write_text(json.dumps(provenance,ensure_ascii=False,indent=2),encoding='utf-8')
    texts=''.join(e['text'] for name in ['verified.json','drafts.seed.json'] for e in json.loads((ROOT/'data'/name).read_text(encoding='utf-8'))['entries'])
    needed={ord(c) for c in texts if '\u3400'<=c<='\u9fff'}
    body_cmap=set(TTFont(body).getBestCmap()); title_cmap=set(TTFont(title).getBestCmap())
    assert not needed-body_cmap
    print('Static body weight:800; CJK required:',len(needed),'body missing:0; title fallback needed:', ''.join(map(chr,sorted(needed-title_cmap))))
if __name__=='__main__':main()
