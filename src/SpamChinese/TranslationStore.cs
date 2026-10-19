using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace SpamChinese
{
    public enum TranslationState { English, Verified, Draft }
    public sealed class Entry
    {
        public string key;
        public string source;
        public string sourceHash;
        public string text;
        public string status;
        public string provider;
        public string generatedAt;
        public int revision;
    }
    public sealed class Catalog { public int schema = 1; public List<Entry> entries = new List<Entry>(); }

    // UI identifiers, formatting placeholders, and game tokens are never sent as prose.
    public static class Tokens
    {
        public static readonly Regex Pattern = new Regex(@"[+-]?<(?:value|min|max|chanceOfReset|limitedUse|duration|missingHpBonus)[^>\r\n]*>%?|<[^>\r\n]+>|\{\d+(?:,[^}:]+)?(?::[^}]+)?\}|\[(?:[A-Za-z][A-Za-z0-9_:.-]*)\]|[+-]?\d+(?:[.,]\d+)*%?", RegexOptions.Compiled);
        public static string Hash(string source)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes((source ?? "").Replace("\r", "")))).Replace("-", "").ToLowerInvariant();
        }
        public static bool Valid(string source, string translated)
        {
            if (string.IsNullOrWhiteSpace(translated) || translated.IndexOf('\0') >= 0) return false;
            var left = Pattern.Matches(source ?? "").Cast<Match>().Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal);
            var right = Pattern.Matches(translated).Cast<Match>().Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal);
            // Reordered numbered arguments are allowed; rich-text nesting must remain well formed.
            return left.SequenceEqual(right) && BalancedMarkup(translated);
        }
        static bool BalancedMarkup(string text)
        {
            var stack = new Stack<string>();
            foreach (Match m in Regex.Matches(text, @"</?(?:color|b|i|u|s|size|link|material|font)(?:=[^>]*)?>"))
            {
                string token = m.Value;
                string tag = Regex.Match(token, @"[a-z]+").Value;
                if (token.StartsWith("</", StringComparison.Ordinal)) { if (stack.Count == 0 || stack.Pop() != tag) return false; }
                else stack.Push(tag);
            }
            return stack.Count == 0;
        }
        public static string Protect(string source, out List<string> originals)
        {
            var list = new List<string>();
            string text = MechanicsTemplates.ProtectedAtoms.Replace(source, m => { list.Add(m.Groups["amount"].Success || m.Groups["element"].Success ? MechanicsTemplates.CanonicalAtom(m.Value) : m.Value); return "ZXQ" + (list.Count - 1).ToString("D4", CultureInfo.InvariantCulture) + "QXZ"; });
            originals = list; return text;
        }
        public static string Restore(string source, string translated, List<string> originals)
        {
            for (int i = 0; i < originals.Count; i++)
            {
                string token = "ZXQ" + i.ToString("D4", CultureInfo.InvariantCulture) + "QXZ";
                if (Regex.Matches(translated, Regex.Escape(token)).Count != 1) return null;
                translated = translated.Replace(token, originals[i]);
            }
            return Valid(source, translated) ? translated : null;
        }
        public static bool Chinese(string s) => !string.IsNullOrEmpty(s) && s.Any(c => c >= '\u3400' && c <= '\u9fff');
        public static List<string> Chunks(string text, int limit = 700)
        {
            var result = new List<string>();
            int offset = 0;
            while (text.Length - offset > limit)
            {
                int end = offset + limit - 1;
                int newline = text.LastIndexOf('\n', end, limit);
                int space = text.LastIndexOf(' ', end, limit);
                int boundary = newline >= offset + limit / 3 ? newline : space;
                if (boundary < offset) throw new InvalidDataException("Translation segment contains an overlong word");
                result.Add(text.Substring(offset, boundary - offset + 1)); offset = boundary + 1;
            }
            result.Add(text.Substring(offset)); return result;
        }
        public static bool Allowed(string key, string source)
        {
            if (string.IsNullOrWhiteSpace(source) || key == "Label.NewDeck" || key.StartsWith("Keyword.Label.") || key.StartsWith("CGU")) return false;
            if (key.EndsWith("DateFormat") || key.EndsWith("DayMonthFormat") || key.EndsWith("DateYearFormat") || key.EndsWith("TimeFormat")) return false;
            return source.Length <= 6000 && Regex.IsMatch(Pattern.Replace(source, ""), "[A-Za-z]{2}") && !source.Contains("http") && !source.Contains("@") && !key.EndsWith(".Url", StringComparison.OrdinalIgnoreCase);
        }
    }
    public sealed class TranslationStore
    {
        readonly Dictionary<string, Entry> verified = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, Entry> drafts = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly object sync = new object();
        public int VerifiedCount => verified.Count;
        public int DraftCount { get { lock (sync) return drafts.Count; } }
        public void Load(string path, bool isVerified)
        {
            if (!File.Exists(path)) return;
            var data = JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(path, Encoding.UTF8));
            if (data == null || data.schema != 1) throw new InvalidDataException("Unsupported translation schema: " + path);
            foreach (var e in data.entries ?? new List<Entry>())
            {
                if (e == null || string.IsNullOrEmpty(e.key) || e.sourceHash != Tokens.Hash(e.source) || !Tokens.Valid(e.source, e.text)) continue;
                if (isVerified && e.status != "verified") continue;
                if (!isVerified && e.status != "draft") continue;
                if (isVerified) verified[e.key] = e;
                else
                {
                    string id=e.key+"\n"+e.sourceHash;
                    if(!drafts.TryGetValue(id,out var old) || Priority(e)>Priority(old)) drafts[id]=e;
                }
            }
        }
        public string Get(string key, string source, out TranslationState state)
        {
            state = TranslationState.English;
            string hash = Tokens.Hash(source);
            if (verified.TryGetValue(key, out var e) && e.sourceHash == hash) { state = TranslationState.Verified; return e.text; }
            if (MechanicsTemplates.TryTranslate(key,source,out var mechanical)) {state=TranslationState.Draft;return mechanical;}
            lock (sync)
                if (drafts.TryGetValue(key + "\n" + hash, out e)) { state = TranslationState.Draft; return e.text; }
            return source;
        }
        public void AddDraft(Entry entry)
        {
            if (entry.status != "draft" || entry.sourceHash != Tokens.Hash(entry.source) || !Tokens.Valid(entry.source, entry.text)) throw new InvalidDataException("Invalid draft");
            lock (sync)
            {
                string id=entry.key+"\n"+entry.sourceHash;
                if(!drafts.TryGetValue(id,out var old) || Priority(entry)>Priority(old)) drafts[id]=entry;
            }
        }
        static int Priority(Entry entry) => entry.revision*10+(entry.provider==MechanicsTemplates.Provider?1:0);
        public void SaveDrafts(string path)
        {
            Catalog catalog;
            lock (sync) catalog = new Catalog { entries = drafts.Values.OrderBy(e => e.key, StringComparer.Ordinal).ToList() };
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(catalog, Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }
}
