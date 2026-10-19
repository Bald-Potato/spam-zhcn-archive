using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Assets.SimpleLocalization.Scripts;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;

namespace SpamChinese
{
    internal sealed class Source
    {
        public string Key;
        public Func<string> Generate;
        public TranslationState State;
    }
    internal sealed class Binding
    {
        public TMP_Text Text;
        public Source Source;
        public string Literal;
        public string LastPresented;
        public TMP_FontAsset OriginalFont;
        public Material OriginalMaterial;
        public bool AutoSize;
        public float Min, Max;
        public TranslationState State => Source?.State ?? TranslationState.Draft;
    }
    internal static class Renderer
    {
        static readonly Dictionary<string, Source> recent = new Dictionary<string, Source>(StringComparer.Ordinal);
        static readonly ConditionalWeakTable<string, Source> exactSources = new ConditionalWeakTable<string, Source>();
        static readonly HashSet<string> ambiguous = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<int, Binding> bindings = new Dictionary<int, Binding>();
        static readonly Dictionary<int,TMP_Text> changed = new Dictionary<int,TMP_Text>();
        internal static Source LastTutorial;
        internal static void RememberExpandedTutorial(string value)
        {
            if (LastTutorial == null) return;
            var template=LastTutorial; var source=new Source {Key=template.Key,State=template.State};
            source.Generate=()=> {string result=template.Generate();TcgEngine.AbilityData.ReplaceDescWithLogo(ref result);source.State=template.State;return result;};
            Remember(value,source);
        }
        internal static void Remember(string value, Source source)
        {
            if (string.IsNullOrEmpty(value) || value.Length>16000 || source==null || Plugin.RenderingScope>0) return;
            exactSources.Remove(value); exactSources.Add(value,source);
            if (recent.TryGetValue(value,out var old) && old.Key!=source.Key)
            {
                bool equivalent=Patches.EnglishSources.TryGetValue(old.Key,out var a) && Patches.EnglishSources.TryGetValue(source.Key,out var b) && a==b;
                if(!equivalent) {ambiguous.Add(value);return;}
            }
            if(recent.Count>12000) {recent.Clear();ambiguous.Clear();}
            recent[value]=source;
        }
        internal static void Propagate(string original,string result)
        {
            if(original!=null && result!=null && exactSources.TryGetValue(original,out var source)) Remember(result,source);
        }
        static Source FindSource(string value)
        {
            if(string.IsNullOrEmpty(value)) return null;
            if(exactSources.TryGetValue(value,out var source)) return source;
            return !ambiguous.Contains(value) && recent.TryGetValue(value,out source) ? source : null;
        }
        static Binding Bind(TMP_Text text)
        {
            if(bindings.TryGetValue(text.GetInstanceID(),out var binding)) return binding;
            binding=new Binding {Text=text,OriginalFont=text.font,OriginalMaterial=text.fontSharedMaterial,AutoSize=text.enableAutoSizing,Min=text.fontSizeMin,Max=text.fontSizeMax};
            bindings[text.GetInstanceID()]=binding;return binding;
        }
        internal static bool BeforeSet(TMP_Text text,ref string value,object[] formatArgs=null)
        {
            if(!Plugin.Active || Plugin.RenderingScope>0 || !text) return true;
            if(string.IsNullOrEmpty(value))
            {
                if(bindings.TryGetValue(text.GetInstanceID(),out var empty)) {RestoreStyle(empty);bindings.Remove(text.GetInstanceID());}
                return true;
            }
            if(UserText(text)) {ApplyUserFont(text,value);return true;}
            // A rendered getter has already expanded the live numbers/icons. Preserve that actual result.
            Source source=FindSource(value); bool generate=false;
            if(source==null)
            {
                var localized=text.GetComponent<LocalizedTMPUGUI>();
                var localized3D=text.GetComponent<LocalizedTMP>();
                var button=text.GetComponentInParent<Spam.MenuV3.ButtonTextController>();
                var toggle=text.GetComponentInParent<Spam.MenuV3.ToggleTextController>();
                var line=text.GetComponentInParent<Spam.MenuV3.TextLineController>();
                if(localized!=null) source=Patches.Template(localized.Key,formatArgs);
                if(source==null && localized3D!=null) source=Patches.Template(localized3D.LocalizationKey,formatArgs);
                if(source==null && button!=null && button.ButtonText==text) source=Patches.Template(button.TextKey,button.TextArgs?.ToArray());
                if(source==null && toggle!=null && toggle.TextComponent==text) source=Patches.Template(toggle.TextKey,toggle.TextArgs?.ToArray());
                if(source==null && line!=null && line.TitleText==text) source=Patches.Template(line.TextKey,null) ?? FindSource(line.TextKey);
                if(source==null && Patches.EnglishKeys.TryGetValue(value,out var key) && key!=null) source=Patches.Template(key,formatArgs);
                generate=source!=null;
            }
            if(source!=null && formatArgs!=null && Patches.EnglishSources.ContainsKey(source.Key)) {source=Patches.Template(source.Key,formatArgs);generate=true;}
            if(source==null && !Tokens.Chinese(value))
            {
                if(bindings.TryGetValue(text.GetInstanceID(),out var old)) {RestoreStyle(old);bindings.Remove(text.GetInstanceID());}
                return true;
            }
            var binding=Bind(text);binding.Source=source;
            if(generate) value=source.Generate();
            if(source==null && value==binding.LastPresented) value=binding.Literal;
            binding.Literal=value;
            value=Present(binding,value);
            binding.LastPresented=value;
            if(source!=null) Remember(value,source);
            return true;
        }
        static string Present(Binding binding,string value)
        {
            var text=binding.Text;
            if(Tokens.Chinese(value))
            {
                value=Presentation.RemoveForeignFonts(value);
                var font=Plugin.Instance.DisplayFont(binding.OriginalFont);
                Plugin.Instance.PrepareCharacters(value,font);
                Plugin.RenderingScope++;
                try
                {
                    bool fontChanged=text.font!=font;
                    if(fontChanged) text.font=font;
                    if(fontChanged || !Plugin.Instance.OwnsMaterial(text.fontSharedMaterial)) text.fontSharedMaterial=font.material;
                    if(!binding.AutoSize && text.fontSize>=12 && !text.enableAutoSizing)
                    {text.fontSizeMin=Math.Max(10,text.fontSize*0.75f);text.fontSizeMax=text.fontSize;text.enableAutoSizing=true;}
                }
                finally {Plugin.RenderingScope--;}
                if(!Plugin.OriginalMode && binding.State==TranslationState.Draft) value=Presentation.DraftMarkup(value,Plugin.Instance.DraftMarking);
            }
            else RestoreStyle(binding);
            return value;
        }
        static bool PrivateText(TMP_Text text)
        {
            if(text.GetComponentInParent<Spam.MenuV3.Sujet042Dialogue>()!=null) return false;
            if(text.GetComponentsInParent<MonoBehaviour>(true).Any(c=>c && (c.GetType().Name=="UsernameController" || c.GetType().Name=="ChatBubbleMessageController"))) return true;
            for(var t=text.transform;t;t=t.parent)
                if(t.name.IndexOf("ChatBubble",StringComparison.OrdinalIgnoreCase)>=0 || t.name.IndexOf("Username",StringComparison.OrdinalIgnoreCase)>=0 || t.name.IndexOf("ChatMessage",StringComparison.OrdinalIgnoreCase)>=0) return true;
            return false;
        }
        static bool UserText(TMP_Text text)
        {
            var input=text.GetComponentInParent<TMP_InputField>();
            return input!=null && input.textComponent==text || PrivateText(text);
        }
        static void ApplyUserFont(TMP_Text text,string value)
        {
            if(!Tokens.Chinese(value)) return;
            var font=Plugin.Instance.BodyFont;Plugin.Instance.PrepareCharacters(value,font);
            Plugin.RenderingScope++;
            try {bool fontChanged=text.font!=font;if(fontChanged) text.font=font;if(fontChanged || !Plugin.Instance.OwnsMaterial(text.fontSharedMaterial)) text.fontSharedMaterial=font.material;}
            finally {Plugin.RenderingScope--;}
        }
        static void RestoreStyle(Binding binding)
        {
            if(!binding.Text) return;
            Plugin.RenderingScope++;
            try
            {
                if(binding.Text.font!=binding.OriginalFont) binding.Text.font=binding.OriginalFont;
                if(binding.OriginalMaterial && binding.Text.fontSharedMaterial!=binding.OriginalMaterial) binding.Text.fontSharedMaterial=binding.OriginalMaterial;
                binding.Text.enableAutoSizing=binding.AutoSize;binding.Text.fontSizeMin=binding.Min;binding.Text.fontSizeMax=binding.Max;
            }
            finally {Plugin.RenderingScope--;}
        }
        internal static void MaterialAssigned(TMP_Text text,ref Material assigned)
        {
            if(Plugin.RenderingScope>0 || !Plugin.Active || !text || !assigned || Plugin.Instance.OwnsMaterial(assigned)) return;
            if(UserText(text) && Tokens.Chinese(text.text)) {assigned=Plugin.Instance.BodyFont.material;return;}
            if(!bindings.TryGetValue(text.GetInstanceID(),out var binding)) return;
            binding.OriginalMaterial=assigned;
            if(Tokens.Chinese(text.text)) assigned=Plugin.Instance.DisplayFont(binding.OriginalFont).material;
        }
        internal static void FontAssigned(TMP_Text text,TMP_FontAsset assigned)
        {
            if(Plugin.RenderingScope>0 || !Plugin.Active || !text || !assigned || Plugin.Instance.OwnsFont(assigned)) return;
            if(UserText(text)) {ApplyUserFont(text,text.text);return;}
            if(!bindings.TryGetValue(text.GetInstanceID(),out var binding)) return;
            binding.OriginalFont=assigned;
            if(Tokens.Chinese(text.text))
            {
                Plugin.RenderingScope++;
                try {text.font=Plugin.Instance.DisplayFont(assigned);text.fontSharedMaterial=text.font.material;}
                finally {Plugin.RenderingScope--;}
            }
        }
        internal static void QueueChanged(UnityEngine.Object value)
        {
            if(Plugin.Active && Plugin.RenderingScope==0 && value is TMP_Text text && text && text.gameObject.activeInHierarchy) changed[text.GetInstanceID()]=text;
        }
        internal static void ReconcileActive()
        {
            foreach(var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)) QueueChanged(text);
            FlushChanged();
        }
        internal static void FlushChanged()
        {
            var pending=changed.Values.ToArray();changed.Clear();
            foreach(var text in pending)
            {
                if(!text || !text.gameObject.activeInHierarchy) continue;
                string before=text.text, value=before;
                BeforeSet(text,ref value);
                if(value!=before) {Plugin.RenderingScope++;try {text.text=value;} finally {Plugin.RenderingScope--;}}
            }
        }
        internal static void Refresh()
        {
            foreach(var binding in bindings.Values.ToArray())
            {
                if(!binding.Text || !binding.Text.gameObject.activeInHierarchy) continue;
                try
                {
                    string raw=binding.Source!=null ? binding.Source.Generate() : binding.Literal;
                    string value=Present(binding,raw);
                    Plugin.RenderingScope++;
                    try {binding.Text.text=value;binding.Text.SetAllDirty();}
                    finally {Plugin.RenderingScope--;}
                    if(binding.Source!=null) Remember(value,binding.Source);
                }
                catch { /* Source object may have been destroyed with the previous panel. */ }
            }
        }
        internal static void RestoreFonts() {foreach(var binding in bindings.Values) RestoreStyle(binding);}
        internal static void Cleanup()
        {foreach(int id in bindings.Where(p=>!p.Value.Text).Select(p=>p.Key).ToArray()) bindings.Remove(id);}
        internal static void Snapshot(string root,string mode)
        {
            var rows=bindings.Values.Where(b=>b.Text && b.Text.gameObject.activeInHierarchy && !UserText(b.Text)).Select(b=>new {
                key=b.Source?.Key ?? "[font-only]",text=b.Text.text,state=b.State.ToString(),font=b.Text.font?.name,
                material=b.Text.fontSharedMaterial?.name,overflow=b.Text.isTextOverflowing,fontSize=b.Text.fontSize,
                missingGlyphs=b.Text.textInfo.characterInfo.Take(b.Text.textInfo.characterCount).Count(c=>c.character>=0x3400 && c.character<=0x9fff && c.textElement!=null && c.textElement.unicode==0x25a1)
            }).ToArray();
            Directory.CreateDirectory(Path.Combine(root,"snapshots"));
            File.WriteAllText(Path.Combine(root,"snapshots",DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+"-"+mode+".json"),JsonConvert.SerializeObject(rows,Formatting.Indented),new UTF8Encoding(false));
        }
    }
}
