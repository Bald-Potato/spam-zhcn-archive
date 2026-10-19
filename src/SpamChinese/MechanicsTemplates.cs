using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpamChinese
{
    // Strictly recognized English clauses only. Unknown or conditional clauses are left to a draft/review.
    public static class MechanicsTemplates
    {
        public const string Provider="mechanics-template-v2";
        public const int Revision=2;
        const string Scalar=@"[+-]?(?:<(?:value|min|max|duration|chanceOfReset|limitedUse|missingHpBonus)[^>]*>|\d+(?:[.,]\d+)?)%?";
        const string ElementRef=@"(?:(?:FIRE|WATER|EARTH|NATURE|LIGHT|SHADOW)\s*)?\[(?:FEU|EAU|TERRE|NATURE|LUMIERE|OMBRE)\]";
        static readonly string Expr=@"(?:\["+Scalar+@"\s+to\s+"+Scalar+@"\]|\(?"+Scalar+@"\)?)";
        static readonly string Amount=Expr+@"\s+"+ElementRef;
        static readonly string Amounts=Amount+@"(?:(?:\s*,\s*|\s+and\s+)"+Amount+@")*";
        static readonly Regex Elements=new Regex(ElementRef,RegexOptions.IgnoreCase);
        public static readonly Regex ProtectedAtoms=new Regex(@"(?<amount>"+Amount+@")|(?<element>"+ElementRef+@")|"+Tokens.Pattern.ToString(),RegexOptions.IgnoreCase);
        static readonly Dictionary<string,string> cache=new Dictionary<string,string>(StringComparer.Ordinal);
        public static bool IsDescription(string key) => key.StartsWith("Card.AbilityDesc.",StringComparison.Ordinal) || key.StartsWith("Card.PassifDesc.",StringComparison.Ordinal) || key.StartsWith("Talent.Description.",StringComparison.Ordinal);
        public static bool TryTranslate(string key,string source,out string text)
        {
            text=null;if(!IsDescription(key) || string.IsNullOrEmpty(source)) return false;
            lock(cache) if(cache.TryGetValue(source,out text)) return text!=null;
            var lines=new List<string>();bool complete=true;
            foreach(string line in source.Replace("\r","").Split('\n'))
            {
                string result=Line(line.Trim());
                if(result==null) {complete=false;break;} lines.Add(result);
            }
            string candidate=complete ? string.Join("\n",lines) : null;
            if(candidate==null || !Tokens.Chinese(candidate) || !Tokens.Valid(source,candidate)) candidate=null;
            lock(cache) cache[source]=candidate;
            text=candidate;return text!=null;
        }
        static Match Match(string source,string pattern) => Regex.Match(source,"^(?:"+pattern+")$",RegexOptions.IgnoreCase);
        static string E(string source) => Elements.Replace(source,m=>Regex.Match(m.Value,@"\[[^\]]+\]").Value);
        static string Value(string source) => Regex.Replace(Regex.Replace(Regex.Replace(E(source),@"\s+to\s+","～",RegexOptions.IgnoreCase),@"\s+and\s+"," 和 ",RegexOptions.IgnoreCase),@",\s+","、");
        public static string CanonicalAtom(string source) => Value(source);
        static string RenderAmounts(string source,bool healing)
        {
            var matches=Regex.Matches(source,@"(?<number>"+Expr+@")\s+(?<element>"+ElementRef+@")",RegexOptions.IgnoreCase).Cast<Match>();
            return string.Join(" 和 ",matches.Select(m=>healing ? Value(m.Groups["number"].Value)+" 点生命（"+E(m.Groups["element"].Value)+"）" : Value(m.Groups["number"].Value)+" 点 "+E(m.Groups["element"].Value)+" 伤害"));
        }
        static string Line(string source)
        {
            if(source.Length==0) return "";
            if(Regex.IsMatch(source,@"^\[[A-Za-z][A-Za-z0-9_:.-]*\]$")) return source;
            string line=source.TrimEnd('.'); Match m;
            m=Match(line,@"Deal (?<amount>"+Amounts+@") damage(?<target> to all enemy cards| to all allied cards| to the targeted card| to the target| to a random enemy card)?(?<random> \[Random\]| \[RandomEnemies\])?(?<repeat> X\d+)?(?<randomAfter> \[Random\]| \[RandomEnemies\])?");
            if(m.Success)
            {
                string target=m.Groups["target"].Value.ToLowerInvariant();
                var targets=new Dictionary<string,string> {{" to all enemy cards","对所有敌方卡牌"},{" to all allied cards","对所有友方卡牌"},{" to the targeted card","对目标卡牌"},{" to the target","对目标"},{" to a random enemy card","对一张随机敌方卡牌"}};
                string text=(targets.TryGetValue(target,out var t)?t:"")+"造成 "+RenderAmounts(m.Groups["amount"].Value,false);
                if(m.Groups["repeat"].Success) text+="，重复 "+m.Groups["repeat"].Value.Trim().Substring(1)+" 次";
                text+="。";
                foreach(string group in new[]{"random","randomAfter"}) if(m.Groups[group].Success) text+=" "+m.Groups[group].Value.Trim();
                return text;
            }
            m=Match(line,@"Heal(?<caster> the caster)? (?<amount>"+Amounts+@")(?: to (?<all>all allied cards))?");
            if(m.Success) return (m.Groups["caster"].Success?"为施法者":m.Groups["all"].Success?"为所有友方卡牌":"")+"恢复 "+RenderAmounts(m.Groups["amount"].Value,true)+"。";
            // Buffs may list several elements. Each entire segment must match before accepting it.
            if(line.IndexOf(" bonus ",StringComparison.OrdinalIgnoreCase)>=0 && !line.StartsWith("Bonus ",StringComparison.OrdinalIgnoreCase))
            {
                var parts=Regex.Split(line,@"\s+&\s+");var result=new List<string>();
                foreach(var part in parts)
                {
                    var b=Match(part,@"(?<element>"+ElementRef+@") bonus (?<value>"+Expr+@")(?: (?<per>per turn))?");
                    if(!b.Success) {result.Clear();break;}
                    result.Add((b.Groups["per"].Success?"每回合":"")+E(b.Groups["element"].Value)+" 额外伤害 "+Value(b.Groups["value"].Value)+"。");
                }
                if(result.Count>0) return string.Join(" ",result);
            }
            m=Match(line,@"(?<element>"+ElementRef+@") resistance (?<value>"+Expr+@")");
            if(m.Success) return E(m.Groups["element"].Value)+" 抗性 "+Value(m.Groups["value"].Value)+"。";
            m=Match(line,@"Resistance to all elements (?<value>"+Expr+@")");
            if(m.Success) return "所有元素抗性 "+Value(m.Groups["value"].Value)+"。";
            m=Match(line,@"(?<stat>Critical|Accuracy|Caster's critical) (?<value>"+Expr+@")(?: on (?<all>all allied cards))?");
            if(m.Success) return (m.Groups["all"].Success?"所有友方卡牌的":m.Groups["stat"].Value.StartsWith("Caster",StringComparison.OrdinalIgnoreCase)?"施法者的":"")+(m.Groups["stat"].Value.Equals("Accuracy",StringComparison.OrdinalIgnoreCase)?"命中率":"暴击率")+" "+Value(m.Groups["value"].Value)+"。";
            m=Match(line,@"Echo \((?<value>"+Scalar+@")\)");
            if(m.Success) return "回响（"+m.Groups["value"].Value+"）。";
            m=Match(line,@"Base damage increases by (?<value>"+Expr+@")");
            if(m.Success) return "基础伤害增加 "+Value(m.Groups["value"].Value)+"。";
            m=Match(line,@"Increases (?<whose>its )?max HP by (?<value>"+Expr+@")(?: (?<element>"+ElementRef+@"))?");
            if(m.Success) return (m.Groups["whose"].Success?"自身的":"")+"最大生命值增加 "+Value(m.Groups["value"].Value)+(m.Groups["element"].Success?" "+E(m.Groups["element"].Value):"")+"。";
            m=Match(line,@"Apply (?<status>\[(?:Eternity|Temporality|Taunt|Vanish|Freeze|Smoked|Vampirism|AOE)\])");
            if(m.Success) return "施加 "+m.Groups["status"].Value+"。";
            m=Match(line,@"\[Cost (?<amount>"+Amount+@"(?:\s+&\s+"+Amount+@")*)\]");
            if(m.Success) return "消耗 "+Value(m.Groups["amount"].Value).Replace(" & "," 和 ")+"。";
            m=Match(line,@"\[Cost (?<value>"+Scalar+@") max HP\]");
            if(m.Success) return "消耗 "+m.Groups["value"].Value+" 最大生命值。";
            m=Match(line,@"\[Limited Use (?<counter>"+Scalar+@"/"+Scalar+@")\]");
            if(m.Success) return "限次（"+m.Groups["counter"].Value+"）";
            return null;
        }
    }
}
