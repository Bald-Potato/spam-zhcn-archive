using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SpamChinese
{
    // Pure rendering rules shared with the offline replay tests; no Unity/game state.
    public static class Presentation
    {
        public static string[] KeywordAliases(string title,bool named)
        {
            return named ? new[]{"\""+title+"\"","“"+title+"”","「"+title+"」","『"+title+"』"} : new[]{title};
        }
        public static IReadOnlyList<string> KeywordTriggers(IEnumerable<string> originals,string title,bool named,string linkId)
        {
            // Native Triggers uses bare titles of >=5 chars, even when the title is Chinese.
            // Replace that path too, otherwise native matching bypasses the alias restriction.
            var triggers=(named && Tokens.Chinese(title) ? KeywordAliases(title,true) : originals).ToList();
            triggers.Add("[ZH:"+linkId+"]");return triggers;
        }
        public static string Element(string text, string element, string sprite, string name)
        {
            string words;
            switch (element)
            {
                case "FEU": words = @"(?<![A-Za-z])FIRE|火属性|火元素|火系|火焰|火|炎|焰"; break;
                case "EAU": words = @"(?<![A-Za-z])WATER|水属性|水元素|水系|水"; break;
                case "TERRE": words = @"(?<![A-Za-z])EARTH|土属性|土元素|土系|地属性|地元素|地系|大地|地球|土地|土|地"; break;
                case "NATURE": words = @"(?<![A-Za-z])NATURE|自然属性|自然元素|自然系|自然"; break;
                case "LUMIERE": words = @"(?<![A-Za-z])LIGHT|光属性|光元素|光系|光明|圣光|光线|光|轻"; break;
                case "OMBRE": words = @"(?<![A-Za-z])SHADOW|暗影属性|暗属性|暗元素|暗系|暗影|影子|黑暗|阴影|黑影|暗|影"; break;
                default: return text;
            }
            string pattern = @"(?:(?:" + words + @")\s*(?<after>伤害)?\s*)?" + Regex.Escape("[" + element + "]");
            // Only an explicit element label is consumed. Never use the native \p{L}+ boundary on Chinese prose.
            return MapPlain(text, s => Regex.Replace(s, pattern, m => sprite.Trim() + name + m.Groups["after"].Value, RegexOptions.IgnoreCase));
        }
        public static string MarkKeywords(string text, IReadOnlyDictionary<string, string> aliases)
        {
            if (aliases.Count == 0) return text;
            string pattern = string.Join("|", aliases.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
            var protectedLinks = MaskLinks(text);
            string marked = Regex.Replace(protectedLinks.Text, @"<[^>]+>|\[[^\]]+\]|(?:" + pattern + ")", m =>
                m.Value.StartsWith("<") || m.Value.StartsWith("[") ? m.Value : "[ZH:" + aliases[m.Value] + "]");
            return protectedLinks.Restore(marked);
        }
        public static string RemoveForeignFonts(string text) => Regex.Replace(text, @"</?(?:font|material)(?:\s*=[^>]*)?>", "", RegexOptions.IgnoreCase);
        public sealed class LinkMask
        {
            public string Text;
            internal readonly Dictionary<string,string> spans = new Dictionary<string,string>();
            public string Restore(string text) { foreach (var span in spans) text = text.Replace(span.Key, span.Value); return text; }
        }
        public static LinkMask MaskLinks(string text)
        {
            var result = new LinkMask(); var output = new StringBuilder(); int offset = 0, start = -1, depth = 0, token = 0xE000;
            foreach (Match match in Regex.Matches(text, @"</?link(?:\s*=[^>]*)?>", RegexOptions.IgnoreCase))
            {
                if (!match.Value.StartsWith("</", StringComparison.Ordinal)) { if (depth++ == 0) start = match.Index; }
                else if (depth > 0 && --depth == 0)
                {
                    while (text.IndexOf((char)token) >= 0) token++;
                    string placeholder = ((char)token++).ToString();
                    output.Append(text, offset, start - offset).Append(placeholder);
                    int end = match.Index + match.Length;
                    result.spans[placeholder] = text.Substring(start, end - start); offset = end;
                }
            }
            output.Append(text, offset, text.Length - offset); result.Text = output.ToString(); return result;
        }
        static string MapPlain(string text, Func<string,string> convert)
        {
            var output = new StringBuilder(); int offset = 0;
            foreach (Match tag in Regex.Matches(text, @"<[^>]+>"))
            {
                output.Append(convert(text.Substring(offset, tag.Index-offset))).Append(tag.Value); offset = tag.Index+tag.Length;
            }
            return output.Append(convert(text.Substring(offset))).ToString();
        }
        public static string DraftMarkup(string text, string mode)
        {
            if (mode == "None") return text;
            string marker = mode == "Italic" ? "i" : "u";
            var output = new StringBuilder(); int depth = 0, offset = 0;
            void Append(string plain)
            {
                if (depth > 0) { output.Append(plain); return; }
                foreach (string part in Regex.Split(plain, @"(\r?\n)"))
                {
                    string core = part.Trim();
                    if (core.Length == 0) { output.Append(part); continue; }
                    int first = part.IndexOf(core, StringComparison.Ordinal);
                    output.Append(part,0,first).Append('<').Append(marker).Append('>').Append(core).Append("</").Append(marker).Append('>').Append(part,first+core.Length,part.Length-first-core.Length);
                }
            }
            foreach (Match tag in Regex.Matches(text, @"<[^>]+>"))
            {
                Append(text.Substring(offset,tag.Index-offset)); output.Append(tag.Value);
                if (Regex.IsMatch(tag.Value, "^<"+marker+"(?:[ >])", RegexOptions.IgnoreCase)) depth++;
                else if (tag.Value.Equals("</"+marker+">",StringComparison.OrdinalIgnoreCase)) depth = Math.Max(0,depth-1);
                offset=tag.Index+tag.Length;
            }
            Append(text.Substring(offset)); return output.ToString();
        }
    }
}
