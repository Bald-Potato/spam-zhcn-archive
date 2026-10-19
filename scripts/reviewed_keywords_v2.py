"""The 0.2 source-reviewed glossary. Require the exact reviewed English before accepting it."""
import json,pathlib
ROOT=pathlib.Path(__file__).resolve().parents[1]
ROWS={
 'Keyword.Title.bonus':('Bonus damage','额外伤害'),
 'Keyword.Title.echo':('Echo','回响'),
 'Keyword.Title.limitedUse':('Limited Use','限次'),
 'Keyword.Title.cost':('Cost','消耗'),
 'Keyword.Title.critical':('Critical','暴击率'),
 'Keyword.Title.criticalDamage':('Critical Power','暴击倍率'),
 'Keyword.Title.accuracy':('Accuracy','命中率'),
 'Keyword.Title.random':('Random','随机'),
 'Keyword.Title.randomEnemies':('Random enemies','随机敌方'),
 'Keyword.Title.choice':('Choice','选择'),
 'Keyword.Desc.echo':('Chance for the ability to remain available after use','使用后，有一定概率保持该技能可用。'),
 'Keyword.Desc.bonus':('Additional damage not affected by elemental bonuses, but affected by Berserk and resistances','不受元素加成影响的额外伤害，但仍受狂暴与抗性影响。'),
 'Keyword.Desc.eternity':("Increases the target's cooldowns by 1",'使目标的冷却增加 1。'),
 'Keyword.Desc.temporality':("Decreases the target's cooldowns by 1",'使目标的冷却减少 1。'),
 'Keyword.Desc.random':('The target or effect is picked at random','随机决定目标或效果。'),
 'Keyword.Desc.randomEnemies':('Targets are picked at random among enemy cards','从敌方卡牌中随机选择目标。'),
 'Keyword.Desc.choice':('The player chooses among several offered options','玩家从给出的多个选项中作出选择。'),
 'Keyword.Desc.resistance':('Percentage of damage reduced for each element','对应元素伤害被减免的百分比。'),
 'Keyword.Desc.critical':("Chance for an ability's damage or healing to be critical",'技能伤害或治疗发生暴击的概率。'),
 'Keyword.Desc.criticalDamage':('Multiplier applied to critical damage and healing','适用于暴击伤害与暴击治疗的倍率。'),
 'Keyword.Desc.limitedUse':('Uses remaining out of the allowed total','剩余可用次数与允许使用的总次数。'),
}
def main():
 p=ROOT/'data';sources={e['key']:e for e in json.loads((p/'sources.json').read_text(encoding='utf-8'))['entries']};path=p/'curated.json';curated=json.loads(path.read_text(encoding='utf-8'));count=0
 for key,(english,text) in ROWS.items():
  e=sources.get(key)
  if not e or e['source']!=english:continue
  curated['keys'][key]=text;curated['keySourceHashes'][key]=e['sourceHash'];count+=1
 path.write_text(json.dumps(curated,ensure_ascii=False,indent=2),encoding='utf-8');print('Source-reviewed keyword entries:',count)
if __name__=='__main__':main()
