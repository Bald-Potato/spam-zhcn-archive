using System;
using System.IO;
using Newtonsoft.Json;
using SpamChinese;
class Tests
{
    static int assertions;
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--replay-v2") { ReplayRendering(); return; }
        if (args.Length > 0 && args[0] == "--rebuild-mechanics") { RebuildMechanics(args[1]); return; }
        string original = "Deal <value1D> [FEU] damage to {0} and <b>heal</b> {1}.";
        string translated = "对 {0} 造成 <value1D> [FEU] 伤害，并<b>治疗</b> {1}。";
        Check(Tokens.Valid(original, translated), "Valid reordered translation rejected");
        Check(!Tokens.Valid(original, translated.Replace("[FEU]", "[火]")), "Changed icon accepted");
        Check(!Tokens.Valid(original, translated.Replace("{1}", "{0}")), "Duplicated numeric argument accepted");
        Check(!Tokens.Valid(original, translated.Replace("</b>", "")), "Broken rich text accepted");
        Check(!Tokens.Valid("Damage X3 [Random]", "造成伤害 [Random]"), "Dropped literal quantity accepted");
        Check(!Tokens.Valid("Damage X3 [Random]", "造成3次伤害 [随机]"), "Mixed-case game keyword changed");
        Check(Tokens.Valid("Damage X3 [Random]", "造成3次伤害 [Random]"), "Preserved literal quantity rejected");
        Check(Tokens.Hash("first\r\nsecond") == Tokens.Hash("first\nsecond"), "Line ending normalization failed");
        string longSource = string.Concat(System.Linq.Enumerable.Repeat(original + "\n", 60));
        string longProtected = Tokens.Protect(longSource, out _);
        var chunks = Tokens.Chunks(longProtected);
        Check(string.Concat(chunks) == longProtected && System.Linq.Enumerable.All(chunks, c => c.Length <= 700), "Long translation segments lost data or exceeded limit");
        Check(System.Linq.Enumerable.All(chunks, c => !c.TrimEnd().EndsWith("ZXQ")), "Protected marker split across requests");
        string protectedText = Tokens.Protect(original, out var placeholders);
        Check(Tokens.Restore(original, protectedText.Replace("Deal", "造成"), placeholders) != null, "Protected tokens cannot roundtrip");
        Check(Tokens.Restore(original, protectedText.Replace("ZXQ0000QXZ", ""), placeholders) == null, "Lost token accepted");
        Check(!Tokens.Valid("Critical (+<value1>%)","暴击率（<value1>）"),"Dynamic percent/sign lost but accepted");
        string paired=Tokens.Protect("Deal <value1D> FIRE[FEU] and <value2D> WATER[EAU] damage",out var atoms);
        Check(atoms.Contains("<value1D> [FEU]") && atoms.Contains("<value2D> [EAU]"),"Value and element must stay in the same translation atom");
        Check(MechanicsTemplates.TryTranslate("Card.AbilityDesc.test","Deal [<min1D> to <max1D>] FIRE[FEU] damage to all enemy cards",out var mechanic) && mechanic.StartsWith("对所有敌方卡牌造成 [<min1D>～<max1D>] 点 [FEU] 伤害"),"Damage range incorrectly becomes a target count");
        Check(MechanicsTemplates.TryTranslate("Card.AbilityDesc.test","Deal <value1D> WATER[EAU] and <value2D> EARTH[TERRE] damage X3 [Random]\nBase damage increases by 2",out mechanic) && mechanic.Contains("重复 3 次") && Tokens.Valid("Deal <value1D> WATER[EAU] and <value2D> EARTH[TERRE] damage X3 [Random]\nBase damage increases by 2",mechanic),"Repeated multi-element ability lost a count or marker");
        Check(!MechanicsTemplates.TryTranslate("Card.AbilityDesc.test","Deal <value1D> FIRE[FEU] damage only if the target is NOT frozen",out _),"Unknown condition must not be discarded by a partial template match");
        Check(MechanicsTemplates.TryTranslate("Card.AbilityDesc.test","Heal the caster <value1D> NATURE[NATURE]",out mechanic) && mechanic=="为施法者恢复 <value1D> 点生命（[NATURE]）。","Healing order/target changed");
        Check(!Tokens.Allowed("Label.NewDeck", "New deck"), "Persisted default deck name sent to translation");
        Check(!Tokens.Allowed("Keyword.Label.echo", "echo|Echo"), "Matching rules sent to translation");
        Check(!Tokens.Allowed("Label.Url", "https://example.com"), "URL sent to translation");
        Check(!Tokens.Allowed("Orb.History.DateFormat", "MMM d"), "Executable date format sent to translation");
        Check(!Tokens.Allowed("Card.AbilityDesc.choice", "[Choice]"), "Internal keyword-only description sent to translation");
        string directory = Path.Combine(Path.GetTempPath(), "spam-zh-tests-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        var entry = new Entry { key = "Ability.Test", source = original, sourceHash = Tokens.Hash(original), text = translated, status = "verified" };
        string verified = Path.Combine(directory, "verified.json");
        File.WriteAllText(verified, JsonConvert.SerializeObject(new Catalog { entries = { entry } }));
        var store = new TranslationStore(); store.Load(verified, true);
        Check(store.Get(entry.key, original, out var state) == translated && state == TranslationState.Verified, "Verified entry missing");
        string updated = original.Replace("damage", "damage twice");
        Check(store.Get(entry.key, updated, out state) == updated && state == TranslationState.English, "Old verified entry reused after update");
        store.AddDraft(new Entry { key = entry.key, source = updated, sourceHash = Tokens.Hash(updated), text = translated, status = "draft" });
        Check(store.Get(entry.key, updated, out state) == translated && state == TranslationState.Draft, "Updated draft missing");
        Check(store.Get(entry.key, original, out state) == translated && state == TranslationState.Verified, "Draft overwrote verified entry");
        string cache = Path.Combine(directory, "drafts.json"); store.SaveDrafts(cache); store.SaveDrafts(cache);
        var reloaded = new TranslationStore(); reloaded.Load(cache, false);
        Check(reloaded.Get(entry.key, updated, out state) == translated && state == TranslationState.Draft, "Draft cache cannot survive restart");
        var revised=new Entry {key="Label.RevisionTest",source="Test",sourceHash=Tokens.Hash("Test"),text="新版",status="draft",revision=2};
        store.AddDraft(revised);store.AddDraft(new Entry {key=revised.key,source=revised.source,sourceHash=revised.sourceHash,text="旧版",status="draft"});
        Check(store.Get(revised.key,revised.source,out state)=="新版","Old cache overwrote the corrected seed revision");
        if (args.Length > 0)
        {
            var package = new TranslationStore(); package.Load(Path.Combine(args[0], "verified.json"), true); package.Load(Path.Combine(args[0], "drafts.seed.json"), false);
            Console.WriteLine("Pack accepted: " + package.VerifiedCount + " verified, " + package.DraftCount + " drafts");
            Check(package.VerifiedCount >= 400, "Reviewed pack rejected too many entries");
        }
        // Only known task-owned temporary files are removed, never arbitrary directories.
        foreach (string path in Directory.GetFiles(directory)) File.Delete(path); Directory.Delete(directory);
        Console.WriteLine("PASS: " + assertions + " integrity/update/cache boundary checks");
    }
    static void ReplayRendering()
    {
        int failed = 0;
        void Replay(bool ok, string label) { Console.WriteLine((ok ? "PASS " : "FAIL ") + label); if (!ok) failed++; }
        string link = "<link=\"kw:poison\"><color=#FFFF59>中毒</color></link>";
        var aliases = new System.Collections.Generic.Dictionary<string,string> { ["中毒"] = "poison", ["暴击"] = "critical" };
        Replay(Presentation.MarkKeywords(link, aliases) == link, "Already linked poison must not become another icon/link");
        var namedTriggers=Presentation.KeywordTriggers(new[]{"全体生命恢复"},"全体生命恢复",true,"ability.heal");
        Replay(!System.Linq.Enumerable.Contains(Presentation.KeywordAliases("治疗",true),"治疗") && !System.Linq.Enumerable.Contains(namedTriggers,"全体生命恢复") && System.Linq.Enumerable.Contains(namedTriggers,"“全体生命恢复”") && System.Linq.Enumerable.Contains(namedTriggers,"[ZH:ability.heal]"),"Ordinary healing prose bypasses neither alias nor native long-title restrictions");
        Replay(Presentation.Element("造成10点火焰[FEU]伤害", "FEU", "<sprite=4>", "火") == "造成10点<sprite=4>火伤害" && Presentation.Element("造成10点光元素伤害[LUMIERE]","LUMIERE","<sprite=8>","光")=="造成10点<sprite=8>光伤害" && Presentation.Element("造成10点土地 [TERRE] 伤害","TERRE","<sprite=6>","土")=="造成10点<sprite=6>土 伤害", "Element prose and legacy noun order survive with one element name/icon");
        string rendered = Presentation.Element("造成10点[FEU]伤害", "FEU", "<sprite=4>", "火");
        Replay(Presentation.Element(rendered,"FEU","<sprite=4>","火") == rendered,"A second render does not add another element icon");
        Replay(Presentation.Element("治疗[FEU]并获得[FEU]", "FEU","<sprite=4>","火") == "治疗<sprite=4>火并获得<sprite=4>火","Two legitimate element references stay two; surrounding Chinese survives");
        var mask=Presentation.MaskLinks(link+" 暴击");
        Replay(!mask.Text.Contains("中毒") && mask.Restore(mask.Text)==link+" 暴击","Native highlighter cannot see already decorated link contents");
        string draft=Presentation.DraftMarkup("伤害<sprite=4><color=red>中毒</color>","Underline");
        Replay(draft=="<u>伤害</u><sprite=4><color=red><u>中毒</u></color>","Draft underline never covers a sprite");
        Replay(Presentation.DraftMarkup(draft,"Underline")==draft,"Draft markup is idempotent");
        Replay(Presentation.RemoveForeignFonts("<font=latin><material=bold><color=red>中毒</color></material></font>")=="<color=red>中毒</color>","Foreign font/material spans are removed without losing color or text");
        using (var game = Mono.Cecil.AssemblyDefinition.ReadAssembly(@"E:\SteamLibrary\steamapps\common\SPAM\SPAM_Data\Managed\Assembly-CSharp.dll"))
        {
            var tox = System.Linq.Enumerable.First(game.MainModule.Types, t => t.FullName == "TcgEngine.ModificatorStatusData");
            var owner = System.Linq.Enumerable.First(game.MainModule.Types, t => t.FullName == tox.BaseType.FullName);
            var method = System.Linq.Enumerable.First(owner.Methods, m => m.Name == "GetDesc");
            Replay(System.Linq.Enumerable.Contains(NativeHookPlan.DescriptionTypes, method.DeclaringType.FullName), "Actual inherited Tox getter is in native hook plan: " + method.FullName);
        }
        if (failed > 0) throw new Exception(failed + " rendering regressions reproduced");
    }
    static void RebuildMechanics(string directory)
    {
        var sources=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(Path.Combine(directory,"sources.json")));
        var seed=JsonConvert.DeserializeObject<Catalog>(File.ReadAllText(Path.Combine(directory,"drafts.seed.json")));
        var entries=new System.Collections.Generic.Dictionary<string,Entry>();
        foreach(var e in seed.entries) if(e.sourceHash==Tokens.Hash(e.source) && Tokens.Valid(e.source,e.text)) entries[e.key+"\n"+e.sourceHash]=e;
        int count=0;
        foreach(var e in sources.entries)
        {
            if(!MechanicsTemplates.TryTranslate(e.key,e.source,out var text)) continue;
            entries[e.key+"\n"+e.sourceHash]=new Entry {key=e.key,source=e.source,sourceHash=e.sourceHash,text=text,status="draft",provider=MechanicsTemplates.Provider,revision=MechanicsTemplates.Revision,generatedAt=DateTime.UtcNow.ToString("o")};count++;
        }
        var sorted=System.Linq.Enumerable.OrderBy(entries.Values,e=>e.key,StringComparer.Ordinal);
        File.WriteAllText(Path.Combine(directory,"drafts.seed.json"),JsonConvert.SerializeObject(new Catalog {entries=new System.Collections.Generic.List<Entry>(sorted)},Formatting.Indented),new System.Text.UTF8Encoding(false));
        Console.WriteLine("Mechanics templates: "+count+" complete descriptions; all remain draft. Seed entries="+entries.Count);
    }
}
