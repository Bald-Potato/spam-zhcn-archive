"""Render actual font outlines to an offline SVG specimen, not a game screenshot."""
import pathlib,sys
ROOT=pathlib.Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'analysis-local/python'))
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
fonts=[TTFont(ROOT/'vendor/font-sources/NotoSansSC-wght.ttf'),TTFont(ROOT/'data/fonts/Body-ExtraBold.ttf'),TTFont(ROOT/'data/fonts/Title-KuaiLe.ttf')]
parts=['<svg xmlns="http://www.w3.org/2000/svg" width="1240" height="520" viewBox="0 0 1240 520"><rect width="1240" height="520" fill="#13082a"/>']
def line(text,fontid,x,y,size,fill='#f2efff',underline=False):
    start=x;font=fonts[fontid];glyphs=font.getGlyphSet();cmap=font.getBestCmap();scale=size/font['head'].unitsPerEm
    for char in text:
        glyphname=cmap.get(ord(char))
        if not glyphname:continue
        glyph=glyphs[glyphname];pen=SVGPathPen(glyphs);glyph.draw(pen)
        parts.append(f'<path d="{pen.getCommands()}" fill="{fill}" transform="translate({x:.2f},{y}) scale({scale:.6f},{-scale:.6f})"/>');x+=glyph.width*scale
    if underline:parts.append(f'<path d="M{start},{y+5}H{x:.2f}" stroke="{fill}" stroke-width="1.7"/>')
line('0.2 字体预览（字体轮廓示意，非游戏截图）',1,38,52,29)
line('旧版：变量字体默认 Thin 100',1,38,107,21,fill='#aca5c8')
line('对所有敌方卡牌造成 10 点火属性伤害，重复 3 次。',0,38,157,31)
line('新版正文：静态 ExtraBold 800',1,38,215,21,fill='#aca5c8')
line('对所有敌方卡牌造成 10 点火属性伤害，重复 3 次。',1,38,265,31)
line('新版标题：站酷快乐体',1,38,323,21,fill='#aca5c8')
line('火精灵　火焰吐息　卡牌收藏　开始对战',2,38,382,43)
line('机译用下划线区分，图标不加下划线：',1,38,440,21,fill='#aca5c8')
line('恢复 10 点生命。该效果尚未验收。',1,38,486,29,underline=True)
parts.append('</svg>');(ROOT/'artifacts/字体预览-0.2.svg').write_text(''.join(parts),encoding='utf-8')
print('Font outline specimen saved')
