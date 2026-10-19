using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SpamChinese
{
    public sealed class AutoTranslator : IDisposable
    {
        readonly TranslationStore store;
        readonly string cachePath;
        readonly ConcurrentQueue<Entry> queue = new ConcurrentQueue<Entry>();
        readonly ConcurrentDictionary<string, DateTime> attempted = new ConcurrentDictionary<string, DateTime>();
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        readonly Action<string> log;
        readonly Task worker;
        readonly string endpoint;
        readonly string provider;
        string bingIg, bingIid, bingKey, bingToken;
        DateTime bingSession;
        int bingRequest;
        public readonly ConcurrentQueue<string> Completed = new ConcurrentQueue<string>();
        public int Pending => queue.Count;
        public AutoTranslator(TranslationStore store, string cachePath, string endpoint, string provider, Action<string> log)
        {
            this.store = store; this.cachePath = cachePath; this.log = log;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new ArgumentException("Translation endpoint must use HTTPS");
            this.endpoint = endpoint;
            this.provider = provider;
            if (provider != "BingTranslate" && provider != "GoogleTranslate") throw new ArgumentException("Unknown translation provider");
            // Keep GET and POST on the same desktop session/request profile.
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Microsoft Windows 10.0.26200; zh-CN) PowerShell/7.6.5");
            worker = Task.Run(Work);
        }
        public void Enqueue(string key, string source)
        {
            if (!Tokens.Allowed(key, source) || queue.Count >= 250) return;
            string id = key + "\n" + Tokens.Hash(source);
            if (attempted.TryGetValue(id, out var last) && DateTime.UtcNow - last < TimeSpan.FromMinutes(10)) return;
            attempted[id] = DateTime.UtcNow;
            queue.Enqueue(new Entry { key = key, source = source, sourceHash = Tokens.Hash(source), status = "draft" });
        }
        async Task Work()
        {
            int failures = 0;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!queue.TryDequeue(out var entry)) { await Task.Delay(250, stop.Token).ConfigureAwait(false); continue; }
                    if(MechanicsTemplates.TryTranslate(entry.key,entry.source,out var local))
                    {
                        entry.text=local;entry.provider=MechanicsTemplates.Provider;entry.revision=MechanicsTemplates.Revision;
                        entry.generatedAt=DateTime.UtcNow.ToString("o");store.AddDraft(entry);store.SaveDrafts(cachePath);Completed.Enqueue(entry.key);continue;
                    }
                    string protectedText = Tokens.Protect(entry.source, out List<string> originals);
                    string translated = "";
                    var chunks = Tokens.Chunks(protectedText);
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        if (i > 0) await Task.Delay(1000, stop.Token).ConfigureAwait(false);
                        string chunk = chunks[i];
                        string leading = Regex.Match(chunk, @"^\s*").Value;
                        string trailing = Regex.Match(chunk, @"\s*$").Value;
                        string body = chunk.Trim();
                        translated += body.Length == 0 ? chunk : leading + (provider == "GoogleTranslate" ? await Google(body).ConfigureAwait(false) : await Bing(body).ConfigureAwait(false)) + trailing;
                    }
                    entry.text = Tokens.Restore(entry.source, translated, originals);
                    if (entry.text == null || !Tokens.Chinese(entry.text)) throw new InvalidOperationException("Translation did not preserve all game tokens or contain Chinese");
                    entry.provider = provider + " (unofficial/no-key)";
                    entry.revision = MechanicsTemplates.Revision;
                    entry.generatedAt = DateTime.UtcNow.ToString("o");
                    store.AddDraft(entry); store.SaveDrafts(cachePath);
                    Completed.Enqueue(entry.key);
                    failures = 0;
                    await Task.Delay(1000, stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    failures++;
                    log("Auto-translation unavailable; English retained (" + ex.GetType().Name + ").");
                    int delay = ex is RateLimitException rate ? rate.DelayMilliseconds : Math.Min(60000, 3000 * failures);
                    try { await Task.Delay(delay, stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        sealed class RateLimitException : Exception
        {
            public readonly int DelayMilliseconds;
            public RateLimitException(int delay) { DelayMilliseconds = delay; }
        }
        async Task<JArray> Data(HttpResponseMessage response)
        {
            using (response)
            {
                EnsureAvailable(response);
                var data = JToken.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (data is JObject error)
                {
                    if (error["statusCode"]?.Value<int>() == 429) throw new RateLimitException(600000);
                    throw new InvalidOperationException("Translation service rejected the request");
                }
                return data as JArray ?? throw new InvalidOperationException("Unexpected translation response");
            }
        }
        static void EnsureAvailable(HttpResponseMessage response)
        {
            if ((int)response.StatusCode == 429)
            {
                var retry = response.Headers.RetryAfter;
                double seconds = retry?.Delta?.TotalSeconds ?? (retry?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 600;
                throw new RateLimitException((int)(Math.Max(60, Math.Min(3600, seconds)) * 1000));
            }
            response.EnsureSuccessStatusCode();
        }
        async Task<string> Google(string text)
        {
            var data = await Data(await client.GetAsync(endpoint + "?client=gtx&sl=en&tl=zh-CN&dt=t&q=" + Uri.EscapeDataString(text), stop.Token).ConfigureAwait(false)).ConfigureAwait(false);
            string result = ""; foreach (var part in data[0]) result += part[0]?.Value<string>(); return result;
        }
        async Task<string> Bing(string text)
        {
            if (bingToken == null || DateTime.UtcNow - bingSession > TimeSpan.FromMinutes(8))
            {
                using (var response = await client.GetAsync("https://www.bing.com/translator", stop.Token).ConfigureAwait(false))
                {
                    EnsureAvailable(response);
                    string html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    bingIg = Regex.Match(html, "IG:\"([^\"]+)\"").Groups[1].Value;
                    bingIid = Regex.Match(html, "data-iid=\"([^\"]+)\"").Groups[1].Value;
                    var helper = Regex.Match(html, @"params_AbusePreventionHelper\s*=\s*\[(\d+),\s*""([^""]+)""");
                    if (bingIg.Length == 0 || bingIid.Length == 0 || !helper.Success) throw new InvalidOperationException("Bing normal page unavailable; no challenge bypass attempted");
                    bingKey = helper.Groups[1].Value; bingToken = helper.Groups[2].Value; bingSession = DateTime.UtcNow;
                }
            }
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://www.bing.com/ttranslatev3?isVertical=1&IG=" + Uri.EscapeDataString(bingIg) + "&IID=" + Uri.EscapeDataString(bingIid) + "." + ++bingRequest))
            {
                request.Headers.Referrer = new Uri("https://www.bing.com/translator");
                request.Headers.TryAddWithoutValidation("Origin", "https://www.bing.com");
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { { "fromLang", "en" }, { "to", "zh-Hans" }, { "text", text }, { "key", bingKey }, { "token", bingToken } });
                var data = await Data(await client.SendAsync(request, stop.Token).ConfigureAwait(false)).ConfigureAwait(false);
                return data[0]?["translations"]?[0]?["text"]?.Value<string>() ?? "";
            }
        }
        public void Dispose() { stop.Cancel(); client.Dispose(); }
    }
}
