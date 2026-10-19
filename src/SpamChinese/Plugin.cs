using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.SimpleLocalization.Scripts;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace SpamChinese
{
    [BepInPlugin("com.spam.zhcn", "SPAM 简体中文", "0.2.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal static bool OriginalMode;
        internal static int EnglishScope;
        internal static int RenderingScope;
        internal static bool Ready;
        internal readonly TranslationStore Store = new TranslationStore();
        internal AutoTranslator Translator;
        internal string Root;
        Harmony harmony;
        ConfigEntry<bool> pluginEnabled;
        ConfigEntry<bool> automatic;
        ConfigEntry<KeyCode> originalKey;
        ConfigEntry<bool> diagnostics;
        ConfigEntry<string> draftStyle;
        internal TMP_FontAsset BodyFont, TitleFont;
        float nextCleanup;
        readonly Dictionary<int, HashSet<char>> prepared = new Dictionary<int, HashSet<char>>();
        internal string DraftMarking => draftStyle.Value;
        void Awake()
        {
            Instance = this;
            Root = Path.GetDirectoryName(Info.Location);
            pluginEnabled = Config.Bind("General", "Enabled", true, "启用汉化；游戏语言需选择 English。");
            automatic = Config.Bind("Translation", "AutoTranslate", true, "后台初翻未验收文本，失败保留英文。");
            originalKey = Config.Bind("General", "OriginalKey", KeyCode.F8, "按住查看英文，松开恢复中文。");
            diagnostics = Config.Bind("Diagnostics", "WriteUiSnapshots", false, "只供验收：将已绑定的游戏文字写入插件 snapshots 目录；不记录聊天/账户。");
            draftStyle = Config.Bind("Appearance", "DraftStyle", "Underline", new ConfigDescription("草稿与已校对文本共用字体；Underline=下划线，Italic=斜体，None=取消区分。修改后重启游戏。",new AcceptableValueList<string>("Underline","Italic","None")));
            var endpoint = Config.Bind("Translation", "GoogleCompatibleEndpoint", "https://translate.googleapis.com/translate_a/single", "兼容 Google 单句 JSON 格式的 HTTPS 接口。");
            var provider = Config.Bind("Translation", "Provider", "BingTranslate", "免密钥初翻服务：BingTranslate 或 GoogleTranslate；限流退避，失败保留英文。");
            if (!pluginEnabled.Value) { Logger.LogInfo("Disabled in configuration."); return; }
            try
            {
                Store.Load(Path.Combine(Root, "verified.json"), true);
                Store.Load(Path.Combine(Root, "drafts.seed.json"), false);
                try { Store.Load(Path.Combine(Root, "drafts.cache.json"), false); }
                catch (Exception ex) { Logger.LogWarning("Draft cache ignored: " + ex.Message); }
                harmony = new Harmony("com.spam.zhcn");
                Patches.Install(harmony);
                Logger.LogInfo("SPAM Chinese 0.2.0 initialized; data=" + Store.VerifiedCount + " verified, " + Store.DraftCount + " drafts. Hold " + originalKey.Value + " for original.");
                if (automatic.Value)
                    try { Translator = new AutoTranslator(Store, Path.Combine(Root, "drafts.cache.json"), endpoint.Value, provider.Value, s => Logger.LogWarning(s)); }
                    catch(Exception ex) { Logger.LogWarning("Auto-translation configuration ignored; packaged Chinese retained ("+ex.GetType().Name+")."); }
            }
            catch (Exception ex)
            {
                harmony?.UnpatchSelf(); harmony = null; Ready = false;
                Logger.LogError("Chinese plugin disabled safely: " + ex);
            }
        }
        void Start()
        {
            if (harmony == null) return;
            try
            {
                BodyFont = LoadFont("Body-ExtraBold.ttf", "SPAM Body ExtraBold");
                TitleFont = LoadFont("Title-KuaiLe.ttf", "SPAM Title KuaiLe");
                TitleFont.fallbackFontAssetTable = new List<TMP_FontAsset> {BodyFont};
                Ready = true;
                TMPro_EventManager.TEXT_CHANGED_EVENT.Add(Renderer.QueueChanged);
                LocalizationManager.Read();
                Patches.InvalidateKeywords();
                LocalizationManager.ForceLocalizationChanged();
                Renderer.Refresh();
                Renderer.ReconcileActive();
                Logger.LogInfo("Static Chinese fonts loaded: body ExtraBold 800, comic titles; drafts="+DraftMarking+".");
            }
            catch (Exception ex)
            {
                Ready = false; harmony.UnpatchSelf();
                Translator?.Dispose(); Translator = null;
                Logger.LogError("Font initialization failed; English retained: " + ex);
            }
        }
        TMP_FontAsset LoadFont(string filename, string name)
        {
            var font = TMP_FontAsset.CreateFontAsset(Path.Combine(Root, "fonts", filename), 0, 48, 6, GlyphRenderMode.SDFAA, 2048, 2048);
            if (font == null) throw new InvalidDataException("Unable to load " + filename);
            font.name = name; font.isMultiAtlasTexturesEnabled = true;
            if (!font.TryAddCharacters("中文汉化卡牌技能天赋回合生命伤害治疗护盾", out string missing, false) && !string.IsNullOrEmpty(missing))
                throw new InvalidDataException("Font lacks essential Chinese characters: " + filename);
            return font;
        }
        void Update()
        {
            if (!Ready) return;
            bool requested = Application.isFocused && Input.GetKey(originalKey.Value);
            if (requested != OriginalMode) SetOriginal(requested);
            bool completed = false;
            while (Translator != null && Translator.Completed.TryDequeue(out _)) completed = true;
            if (completed)
            {
                Patches.InvalidateKeywords(); Patches.InvalidateSearch();
                LocalizationManager.ForceLocalizationChanged(); Renderer.Refresh();
            }
            if (Input.GetKeyDown(KeyCode.F9) && diagnostics.Value) Renderer.Snapshot(Root, OriginalMode ? "english" : "chinese");
            if (Time.unscaledTime > nextCleanup) { Renderer.Cleanup(); nextCleanup = Time.unscaledTime + 15; }
        }
        void LateUpdate() { if(Active) Renderer.FlushChanged(); }
        internal void SetOriginal(bool value)
        {
            OriginalMode = value;
            Patches.InvalidateKeywords();
            try { LocalizationManager.ForceLocalizationChanged(); Renderer.Refresh(); }
            catch (Exception ex) { Logger.LogWarning("UI refresh failure: " + ex.GetType().Name); }
            if (diagnostics.Value) Renderer.Snapshot(Root, value ? "english" : "chinese");
        }
        void OnApplicationFocus(bool focus) { if (!focus && OriginalMode && Ready) SetOriginal(false); }
        void OnDestroy()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(Renderer.QueueChanged);
            Ready = false; Translator?.Dispose(); harmony?.UnpatchSelf(); Renderer.RestoreFonts();
            if (TitleFont) Destroy(TitleFont); if (BodyFont) Destroy(BodyFont);
        }
        internal TMP_FontAsset DisplayFont(TMP_FontAsset original)
        {
            return original && (original.name.IndexOf("Newyear",StringComparison.OrdinalIgnoreCase)>=0 || (original.faceInfo.familyName ?? "").IndexOf("Newyear",StringComparison.OrdinalIgnoreCase)>=0) ? TitleFont : BodyFont;
        }
        internal bool OwnsFont(TMP_FontAsset font) => font && (font==BodyFont || font==TitleFont);
        internal bool OwnsMaterial(Material material)
        {
            if(!material) return false;
            var texture=material.mainTexture;
            // TMP creates separate materials for every dynamic atlas page and fallback font.
            // Keeping a valid second-page atlas is essential: forcing page zero draws boxes/wrong glyphs.
            return texture && new[]{BodyFont,TitleFont}.Any(f=>f && f.atlasTextures.Any(t=>t==texture));
        }
        internal void PrepareCharacters(string text, TMP_FontAsset font)
        {
            if(!font) return;
            if(font!=BodyFont) PrepareCharacters(text,BodyFont);
            if(!prepared.TryGetValue(font.GetInstanceID(),out var known)) prepared[font.GetInstanceID()]=known=new HashSet<char>();
            string characters = new string(text.Where(c => c >= '\u3400' && c <= '\u9fff' && known.Add(c) && !font.HasCharacter(c)).Distinct().ToArray());
            if (characters.Length == 0) return;
            if (!font.TryAddCharacters(characters, out string missing, false) && !string.IsNullOrEmpty(missing))
            {
                var unavailable=missing.Where(c=>!BodyFont.HasCharacter(c)).ToArray();
                if(unavailable.Length>0) Logger.LogWarning("Chinese font missing glyph(s): " + string.Join(",", unavailable.Select(c => "U+" + ((int)c).ToString("X4"))));
            }
        }
        internal static bool Active => Ready && LocalizationManager.Language == "English";
        internal static T English<T>(Func<T> action)
        {
            EnglishScope++;
            try { return action(); } finally { EnglishScope--; }
        }
    }
}
