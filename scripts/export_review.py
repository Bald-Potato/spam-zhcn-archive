"""Export a new review sheet without overwriting earlier user annotations."""
import csv,json,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
def main():
 data=ROOT/'data';entries={e['key']:e for e in json.loads((data/'drafts.seed.json').read_text(encoding='utf-8'))['entries']};entries.update({e['key']:e for e in json.loads((data/'verified.json').read_text(encoding='utf-8'))['entries']})
 target=ROOT/'review/手动验收记录-0.2.csv'
 if target.exists():raise SystemExit('Existing review sheet preserved: '+str(target))
 with target.open('w',encoding='utf-8-sig',newline='') as f:
  w=csv.writer(f);w.writerow(['编号','类别','文本标识','英文原文','当前译文','现有状态','修订译文','验收结论','问题备注','原文指纹','生成方式'])
  for i,e in enumerate(sorted(entries.values(),key=lambda x:x['key']),1):
   kind='界面/其他'
   for pre,label in [('Card.AbilityDesc.','技能说明'),('Card.PassifDesc.','被动/Tox说明'),('Card.Ability.','技能名称'),('Card.Passif.','被动/Tox名称'),('Card.Name.','卡牌名称'),('Talent.','天赋'),('Keyword.','关键词'),('TutoDesc.','教程'),('Menu.','菜单'),('Label.','界面标签')]:
    if e['key'].startswith(pre):kind=label;break
   w.writerow([i,kind,e['key'],e['source'],e['text'],'已校对' if e['status']=='verified' else '机译草稿','','未验收','',e['sourceHash'],e.get('provider','')])
 print('Review rows:',len(entries))
if __name__=='__main__':main()
