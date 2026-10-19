"""Generate reviewed UI entries and separately labelled machine drafts. No account/chat data."""
import datetime, hashlib, http.cookiejar, json, pathlib, re, time, urllib.parse, urllib.request, urllib.error
ROOT = pathlib.Path(__file__).resolve().parents[1]
TOKEN = re.compile(r'[+-]?<(?:value|min|max|chanceOfReset|limitedUse|duration|missingHpBonus)[^>\r\n]*>%?|<[^>\r\n]+>|\{\d+(?:,[^}:]+)?(?::[^}]+)?\}|\[(?:[A-Za-z][A-Za-z0-9_:.-]*)\]|[+-]?\d+(?:[.,]\d+)*%?')
# Keep amount/element pairs intact, matching the runtime protection rules.
SCALAR = r'[+-]?(?:<(?:value|min|max|duration|chanceOfReset|limitedUse|missingHpBonus)[^>]*>|\d+(?:[.,]\d+)?)%?'
ELEMENT = r'(?:(?:FIRE|WATER|EARTH|NATURE|LIGHT|SHADOW)\s*)?\[(?:FEU|EAU|TERRE|NATURE|LUMIERE|OMBRE)\]'
EXPR = r'(?:\['+SCALAR+r'\s+to\s+'+SCALAR+r'\]|\(?'+SCALAR+r'\)?)'
ATOMS = re.compile(r'(?P<amount>'+EXPR+r'\s+'+ELEMENT+r')|(?P<element>'+ELEMENT+r')|'+TOKEN.pattern, re.IGNORECASE)
REVISION = 2
OPENER = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
OPENER.addheaders = [('User-Agent','Mozilla/5.0 (Windows NT 10.0; Microsoft Windows 10.0.26200; zh-CN) PowerShell/7.6.5')]
BING = {}
def save(name, entries):
    path = ROOT / 'data' / name
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps({'schema': 1, 'entries': entries}, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(path)

def protect(source):
    tokens = []
    def replace(m):
        value = m.group()
        if m.group('amount') or m.group('element'):
            value = re.sub(ELEMENT, lambda e: re.search(r'\[[^\]]+\]',e.group()).group(), value, flags=re.IGNORECASE)
            value = re.sub(r'\s+to\s+', '～', value, flags=re.IGNORECASE)
            value = re.sub(r'\s+and\s+', ' 和 ', value, flags=re.IGNORECASE)
            value = re.sub(r',\s+', '、', value)
        tokens.append(value); return 'ZXQ%04dQXZ' % (len(tokens)-1)
    return ATOMS.sub(replace, source), tokens

def valid(source, text):
    if not text or '\0' in text or sorted(TOKEN.findall(source)) != sorted(TOKEN.findall(text)): return False
    stack = []
    for match in re.finditer(r'</?(?:color|b|i|u|s|size|link|material|font)(?:=[^>]*)?>', text):
        tag = re.search('[a-z]+', match.group()).group()
        if match.group().startswith('</'):
            if not stack or stack.pop() != tag: return False
        else: stack.append(tag)
    return not stack

def restore(source, text, tokens):
    for i, token in enumerate(tokens):
        marker = 'ZXQ%04dQXZ' % i
        if text.count(marker) != 1: return None
        text = text.replace(marker, token)
    if not valid(source, text): return None
    return text.strip() if re.search('[\u3400-\u9fff]', text) else None

def translate(text):
    if not BING or time.time()-BING['time']>480:
        with OPENER.open('https://www.bing.com/translator',timeout=25) as r: html=r.read().decode('utf-8')
        ig=re.search(r'IG:"([^"]+)"',html)
        iid=re.search(r'data-iid="([^"]+)"',html)
        helper=re.search(r'params_AbusePreventionHelper\s*=\s*\[(\d+),\s*"([^"]+)"',html)
        if not(ig and iid and helper): raise RuntimeError('Bing normal page unavailable')
        BING.update(ig=ig[1],iid=iid[1],key=helper[1],token=helper[2],time=time.time(),count=0)
    BING['count']+=1
    url='https://www.bing.com/ttranslatev3?'+urllib.parse.urlencode({'isVertical':1,'IG':BING['ig'],'IID':BING['iid']+'.'+str(BING['count'])})
    data=urllib.parse.urlencode({'fromLang':'en','text':text,'to':'zh-Hans','key':BING['key'],'token':BING['token']}).encode()
    request=urllib.request.Request(url,data=data,headers={'Origin':'https://www.bing.com','Referer':'https://www.bing.com/translator','Content-Type':'application/x-www-form-urlencoded','Accept':'*/*'})
    with OPENER.open(request, timeout=25) as r: result=json.load(r)
    if not isinstance(result,list) or not result or 'translations' not in result[0]:
        first = result[0] if isinstance(result,list) and result else result
        raise RuntimeError('Bing response status: '+str(first.get('statusCode','unknown'))+' '+str(first.get('errorMessage',''))[:160])
    return result[0]['translations'][0]['text']

def main():
    sources = json.loads((ROOT/'data/sources.json').read_text(encoding='utf-8'))['entries']
    curated = json.loads((ROOT/'data/curated.json').read_text(encoding='utf-8'))
    reviewed, pending = [], []
    seed = ROOT/'data/drafts.seed.json'
    drafts = json.loads(seed.read_text(encoding='utf-8'))['entries'] if seed.exists() else []
    drafts = [e for e in drafts if valid(e['source'],e['text'])]
    cached = {e['key']+'\n'+e['sourceHash'] for e in drafts}
    for entry in sources:
        key, source = entry['key'], entry['source']
        if key == 'Label.NewDeck' or key.startswith('Keyword.Label.') or not source or 'http' in source or '@' in source: continue
        key_text = curated['keys'].get(key) if curated.get('keySourceHashes', {}).get(key) == entry['sourceHash'] else None
        text = key_text or curated['exact'].get(source)
        if text and valid(source, text):
            reviewed.append({**entry, 'text': text, 'status':'verified', 'provider':'curated-source-review'})
        elif key+'\n'+entry['sourceHash'] not in cached and not ('Lore' in entry['sheet'] or 'CGU' in entry['sheet']) and re.search('[A-Za-z]{2}', source) and len(source)<4500:
            pending.append(entry)
    save('verified.json', reviewed)
    print('Reviewed UI/game terms:', len(reviewed), 'Remaining initial drafts:', len(pending), flush=True)
    if '--curated-only' in __import__('sys').argv: return
    # Batches reduce requests; every segment and every token is checked independently.
    groups, group, size = [], [], 0
    for entry in pending:
        protected, _ = protect(entry['source'])
        estimated = len(protected) + 22
        if group and size + estimated > 800:
            groups.append(group); group=[]; size=0
        group.append(entry); size += estimated
    if group: groups.append(group)
    failures = 0
    for batch_number, batch in enumerate(groups):
        segments=[]
        for i, entry in enumerate(batch):
            protected, tokens = protect(entry['source'])
            segments.append((entry, protected, tokens))
        request='\n\n'.join('QQB%04dBQQ\n%s' % (i, segment[1]) for i, segment in enumerate(segments))
        try:
            result=translate(request)
            parsed=re.split(r'QQB\s*(\d{4})\s*BQQ\s*', result)
            values={int(parsed[i]):parsed[i+1].strip() for i in range(1,len(parsed)-1,2)}
            for i,(entry,protected,tokens) in enumerate(segments):
                text=restore(entry['source'],values.get(i,''),tokens)
                if text:
                    drafts.append({**entry,'text':text,'status':'draft','revision':REVISION,'provider':'BingTranslate (unofficial/no-key)','generatedAt':datetime.datetime.now(datetime.timezone.utc).isoformat()})
            save('drafts.seed.json', drafts)
            failures=0
            print('Batch', batch_number+1,'/',len(groups),'drafts:',len(drafts), flush=True)
        except Exception as ex:
            failures+=1; print('Initial translation retry deferred:',type(ex).__name__,getattr(ex,'code',''),str(ex) if isinstance(ex,RuntimeError) else '', 'characters=',len(request),flush=True)
            if isinstance(ex, urllib.error.HTTPError) and ex.code == 429: print('Rate-limited; stop pre-generation and retain English.',flush=True); break
            if failures>=4: break
        time.sleep(0.8)
    print('Seed finished; verified=',len(reviewed),'draft=',len(drafts),flush=True)
if __name__=='__main__':
    import sys
    if '--check' in sys.argv:
        try: print(translate('Play'))
        except urllib.error.HTTPError as e: print('HTTP',e.code,e.reason)
    else: main()
