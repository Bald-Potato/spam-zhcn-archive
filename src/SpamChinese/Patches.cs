using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Assets.SimpleLocalization.Scripts;
using HarmonyLib;
using Spam.Data;
using TMPro;
using UnityEngine;

namespace SpamChinese
{
    internal static class Patches
    {
        internal static readonly Dictionary<string, string> EnglishSources = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static readonly Dictionary<string, string> EnglishKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, KeywordData> keywordAliases = new Dictionary<string, KeywordData>(StringComparer.Ordinal);
        static object aliasesFor;
        [ThreadStatic] static Stack<List<TranslationState>> captures;
        [ThreadStatic] static int metadataReads;
        static HarmonyMethod Method(string name) => new HarmonyMethod(typeof(Patches), name);
        internal static void Install(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(LocalizationManager), "Read"), postfix: Method(nameof(ReadPostfix)));
            h.Patch(AccessTools.Method(typeof(LocalizationManager), "Localize", new[] { typeof(string) }), postfix: Method(nameof(LocalizePostfix)));
            h.Patch(AccessTools.Method(typeof(LocalizationManager), "Localize", new[] { typeof(string), typeof(object[]) }), postfix: Method(nameof(FormatPostfix)));
            h.Patch(AccessTools.Method(typeof(LocalizationManager), "AllEntries", new[] { typeof(string) }), postfix: Method(nameof(AllEntriesPostfix)));
            h.Patch(AccessTools.PropertySetter(typeof(TMP_Text), "text"), prefix: Method(nameof(TextPrefix)));
            h.Patch(AccessTools.PropertySetter(typeof(TMP_Text), "font"), postfix: Method(nameof(FontPostfix)));
            h.Patch(AccessTools.PropertySetter(typeof(TMP_Text), "fontSharedMaterial"), prefix: Method(nameof(MaterialPrefix)));
            h.Patch(AccessTools.PropertySetter(typeof(TMP_Text), "fontMaterial"), prefix: Method(nameof(MaterialPrefix)));
            h.Patch(AccessTools.Method(typeof(Spam.Extensions.StringExtensions), "ToUnaccented"), postfix: Method(nameof(UnaccentedPostfix)));
            foreach (var m in typeof(TMP_Text).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "SetText" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(string)))
                h.Patch(m, prefix: Method(nameof(SetTextPrefix)));
            h.Patch(AccessTools.Method(typeof(TcgEngine.AbilityData), "ReplaceElementLogo"), prefix: Method(nameof(ElementPrefix)));
            h.Patch(AccessTools.PropertyGetter(typeof(KeywordData), "Triggers"), prefix: Method(nameof(TriggerPrefix)), postfix: Method(nameof(TriggerPostfix)), finalizer: Method(nameof(TriggerFinalizer)));
            foreach (var m in typeof(KeywordHighlighter).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Highlight" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(string)))
                h.Patch(m, prefix: Method(nameof(HighlightPrefix)), postfix: Method(nameof(HighlightPostfix)));

            var assembly = typeof(LocalizationManager).Assembly;
            foreach (string typename in NativeHookPlan.DescriptionTypes)
            {
                var type = assembly.GetType(typename);
                if (type == null) continue;
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (m.ReturnType == typeof(string) && !m.ContainsGenericParameters && !m.GetParameters().Any(p => p.ParameterType.IsByRef) &&
                        (m.Name.Contains("GetDesc") || m.Name == "GetTitle" || m.Name == "get_Description" || m.Name == "TitleWithSubtitle" || typename == "HistoryLocalization" && m.Name == "Get"))
                        h.Patch(m, prefix: Method(nameof(CompositePrefix)), postfix: Method(nameof(CompositePostfix)), finalizer: Method(nameof(CompositeFinalizer)));
            }
            // The tutorial source remains a localization key, even after icons are expanded.
            h.Patch(AccessTools.Method(typeof(TcgEngine.UI.DialogueBulleController), "OnTutorialTextReceive"), prefix: Method(nameof(TutorialPrefix)));
        }
        static void ReadPostfix()
        {
            if (EnglishSources.Count != 0 || !LocalizationManager.Dictionary.TryGetValue("English", out var dictionary)) return;
            foreach (var kv in dictionary)
            {
                EnglishSources[kv.Key] = kv.Value;
                if (EnglishKeys.ContainsKey(kv.Value)) EnglishKeys[kv.Value] = null;
                else EnglishKeys[kv.Value] = kv.Key;
            }
        }
        internal static Source Template(string key, object[] args)
        {
            if (string.IsNullOrEmpty(key) || !EnglishSources.ContainsKey(key)) return null;
            var source = new Source { Key = key };
            source.Generate = () =>
            {
                if (!EnglishSources.TryGetValue(key, out var original)) original = key;
                var translated = Plugin.Instance.Store.Get(key, original, out var state);
                source.State = state;
                if (state == TranslationState.English && Plugin.EnglishScope == 0) Plugin.Instance.Translator?.Enqueue(key, original);
                string result = Plugin.OriginalMode || Plugin.EnglishScope > 0 || !Plugin.Active ? original : translated;
                if (args == null || args.Length == 0) return result;
                try { return string.Format(result, args); }
                catch (FormatException)
                {
                    try { return string.Format(original, args); }
                    catch (FormatException) { return original; }
                }
            };
            return source;
        }
        static void AddState(TranslationState state)
        {
            if (metadataReads==0 && captures != null) foreach (var list in captures) list.Add(state);
        }
        static void LocalizePostfix(string localizationKey, ref string __result)
        {
            if (!Plugin.Active || string.IsNullOrEmpty(localizationKey) || !EnglishSources.ContainsKey(localizationKey) || localizationKey == "Label.NewDeck" || localizationKey.StartsWith("Keyword.Label.", StringComparison.Ordinal)) return;
            var source = Template(localizationKey, null);
            __result = source.Generate(); AddState(source.State);
            if (localizationKey.StartsWith("TutoDesc.", StringComparison.Ordinal)) Renderer.LastTutorial = source;
            if (Plugin.EnglishScope == 0) Renderer.Remember(__result, source);
        }
        static void FormatPostfix(string localizationKey, object[] args, ref string __result)
        {
            if (!Plugin.Active || string.IsNullOrEmpty(localizationKey) || !EnglishSources.ContainsKey(localizationKey) || localizationKey == "Label.NewDeck") return;
            var source = Template(localizationKey, args?.ToArray());
            __result = source.Generate(); AddState(source.State);
            if (localizationKey.StartsWith("TutoDesc.", StringComparison.Ordinal)) Renderer.LastTutorial = source;
            if (Plugin.EnglishScope == 0) Renderer.Remember(__result, source);
        }
        static void AllEntriesPostfix(string localizationKey, List<string> __result)
        {
            if (!Plugin.Active || string.IsNullOrEmpty(localizationKey) || !EnglishSources.TryGetValue(localizationKey, out var original)) return;
            string translation = Plugin.Instance.Store.Get(localizationKey, original, out var state);
            if (state != TranslationState.English && !__result.Contains(translation)) __result.Add(translation);
        }
        static bool TextPrefix(TMP_Text __instance, ref string value) => Renderer.BeforeSet(__instance, ref value);
        static void UnaccentedPostfix(string __0, string __result) { if (Plugin.Active) Renderer.Propagate(__0, __result); }
        static void SetTextPrefix(TMP_Text __instance, ref string __0, object[] __args)
        {
            var args = __args.Skip(1).Where(a => a is float || a is double || a is int).ToArray();
            Renderer.BeforeSet(__instance, ref __0, args.Length == 0 ? null : args);
        }
        static void FontPostfix(TMP_Text __instance, TMP_FontAsset value) => Renderer.FontAssigned(__instance, value);
        static void MaterialPrefix(TMP_Text __instance, ref Material __0) => Renderer.MaterialAssigned(__instance, ref __0);
        static bool ElementPrefix(ref string desc, string element, string sprite, string elementId, string coloredTermMaterial)
        {
            if (!Plugin.Active || Plugin.EnglishScope > 0 || Plugin.OriginalMode || !Tokens.Chinese(desc)) return true;
            if (desc.IndexOf("[" + element + "]",StringComparison.Ordinal) < 0) return false;
            string name=(string)AccessTools.Method(typeof(TcgEngine.AbilityData),"ElementNameTag").Invoke(null,new object[]{elementId,coloredTermMaterial});
            desc=Presentation.Element(desc,element,sprite,name);
            return false;
        }
        internal static void InvalidateKeywords()
        {
            keywordAliases.Clear();
            aliasesFor = null;
            if (AccessTools.Field(typeof(KeywordHighlighter), "_needles")?.GetValue(null) is IDictionary dictionary) dictionary.Clear();
        }
        internal static void InvalidateSearch()
        {
            var field = AccessTools.Field(typeof(Spam.Data.CardData), "_searchIndex");
            if (field == null) return;
            foreach (var card in Spam.Data.CardData.All) if (card) field.SetValue(card, null);
        }
        static void TriggerPostfix(KeywordData __instance, ref IReadOnlyList<string> __result)
        {
            if (!Plugin.Active || Plugin.EnglishScope > 0 || Plugin.OriginalMode) return;
            string title = __instance.Title;
            __result=Presentation.KeywordTriggers(__result,title,__instance.TriggersOnTitle,__instance.LinkId);
            if (Tokens.Chinese(title) && title.Length >= 2)
            {
                // Chinese has no Latin word boundary: a skill named "治疗" must not decorate every healing verb.
                foreach(string alias in Presentation.KeywordAliases(title,__instance.TriggersOnTitle)) AddAlias(alias,__instance);
            }
        }
        static void TriggerPrefix(out bool __state) { __state=Plugin.Active;if(__state) metadataReads++; }
        static Exception TriggerFinalizer(Exception __exception,bool __state) {if(__state) metadataReads--;return __exception;}
        static void AddAlias(string title,KeywordData keyword)
        {
            if(keywordAliases.TryGetValue(title,out var old) && old!=keyword) keywordAliases[title]=null;
            else if(!keywordAliases.ContainsKey(title)) keywordAliases[title]=keyword;
        }
        static void HighlightPrefix(ref string __0, out Presentation.LinkMask __state)
        {
            __state = null;
            if (!Plugin.Active || Plugin.EnglishScope > 0 || Plugin.OriginalMode || !Tokens.Chinese(__0)) return;
            __state = Presentation.MaskLinks(__0);
            __0 = __state.Text;
            // Build through the native keyword list; explicit markers retain native links and colors.
            var allProperty = AccessTools.Property(typeof(KeywordHighlighter), "All");
            var all = allProperty?.GetValue(null);
            if (!ReferenceEquals(aliasesFor, all) && all is IEnumerable list)
            {
                foreach (KeywordData keyword in list) { var ignored = keyword.Triggers; }
                aliasesFor = all;
            }
            var aliases = keywordAliases.Where(p => p.Value != null).ToDictionary(p=>p.Key,p=>p.Value.LinkId,StringComparer.Ordinal);
            __0=Presentation.MarkKeywords(__0,aliases);
        }
        static void HighlightPostfix(ref string __result, Presentation.LinkMask __state) { if (__state != null) __result=__state.Restore(__result); }
        static void CompositePrefix(out List<TranslationState> __state)
        {
            __state = null;
            if (!Plugin.Active || Plugin.EnglishScope > 0 || Plugin.RenderingScope > 0 || metadataReads>0) return;
            if (captures == null) captures = new Stack<List<TranslationState>>();
            __state = new List<TranslationState>(); captures.Push(__state);
        }
        static void CompositePostfix(object __instance, MethodBase __originalMethod, object[] __args, string __result, List<TranslationState> __state)
        {
            if (__state == null || !Plugin.Active || Plugin.EnglishScope > 0 || Plugin.RenderingScope > 0) return;
            object[] arguments = __args.ToArray();
            string key = __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name + ":" + RuntimeHelpers.GetHashCode(__instance ?? __originalMethod) + ":" + string.Join(",", arguments.Select(a => a == null ? "null" : RuntimeHelpers.GetHashCode(a).ToString()));
            var source = new Source { Key = key, State = Aggregate(__state) };
            source.Generate = () =>
            {
                var localStates = new List<TranslationState>();
                if (captures == null) captures = new Stack<List<TranslationState>>();
                captures.Push(localStates);
                Plugin.RenderingScope++;
                try
                {
                    string value = (string)__originalMethod.Invoke(__instance, arguments);
                    source.State = Aggregate(localStates); return value;
                }
                finally { Plugin.RenderingScope--; captures.Pop(); }
            };
            Renderer.Remember(__result, source);
        }
        static TranslationState Aggregate(List<TranslationState> states)
        {
            if (states.Any(s => s == TranslationState.Draft)) return TranslationState.Draft;
            if (states.Any(s => s == TranslationState.Verified)) return states.Any(s => s == TranslationState.English) ? TranslationState.Draft : TranslationState.Verified;
            return TranslationState.English;
        }
        static Exception CompositeFinalizer(Exception __exception, List<TranslationState> __state)
        {
            if (__state != null && captures != null && captures.Count > 0 && ReferenceEquals(captures.Peek(), __state)) captures.Pop();
            return __exception;
        }
        static void TutorialPrefix(ref string text)
        {
            if (!Plugin.Active) return;
            // A template is remembered during the immediately preceding tutorial localization call.
            // The record is expanded with native icon processing without restarting the bubble timer.
            Renderer.RememberExpandedTutorial(text);
        }
    }
}
