// Assistente di CorbolPrompt: spiega comandi, output ed errori del terminale e propone comandi (o codice Lisp)
// che l'utente esegue con un clic. Riceve la richiesta, la shell e la cartella della scheda e, se l'utente
// lo permette, le ultime righe dello schermo.
//   - Claude Code: il programma installato sul PC, con l'abbonamento dell'utente (niente chiave API).
//   - Claude: API Anthropic (POST /v1/messages), con la chiave dell'utente.
//   - Compatibile OpenAI: POST <url>/chat/completions (OpenAI, OpenRouter, Ollama, LM Studio...).
// Stesso schema di CorbolImg, CorbolPad e CorbolIp. Niente SDK: HTTP e JSON a mano, con JavaScriptSerializer.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CorbolPrompt
{
    class AiMessage
    {
        public string Role, Text;   // Role: "user" o "assistant"
        public List<byte[]> Jpegs = new List<byte[]>();   // immagini allegate (solo messaggi "user")
        public AiMessage(string role, string text) { Role = role; Text = text; }
    }

    class AiRequest
    {
        public string System;
        public List<AiMessage> Messages = new List<AiMessage>();
    }

    class AiResult
    {
        public string Text = "";
        public string Error;            // messaggio per l'utente, già tradotto; null = tutto bene
        public bool Cancelled;
        public bool Ok { get { return Error == null && !Cancelled; } }
    }

    interface IAiProvider
    {
        string Name { get; }
        // Bloccante: va chiamato da un thread in background.
        AiResult Run(AiRequest req, CancellationToken cancel);
    }

    static class AiJson
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static object Parse(string json)
        {
            try { return Json.DeserializeObject(json); } catch { return null; }
        }

        // Get(obj, "a", "b", 0, "c") = obj.a.b[0].c, o null
        public static object Get(object o, params object[] path)
        {
            foreach (var p in path)
            {
                if (o == null) return null;
                if (p is string)
                {
                    var d = o as IDictionary<string, object>;
                    object v;
                    o = d != null && d.TryGetValue((string)p, out v) ? v : null;
                }
                else
                {
                    var a = o as IList;
                    int i = (int)p;
                    o = a != null && i < a.Count ? a[i] : null;
                }
            }
            return o;
        }

        public static string Str(object o, params object[] path)
        {
            var v = Get(o, path);
            return v == null ? null : v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int Int(object o, string key, int def)
        {
            var v = Get(o, key);
            if (v is int) return (int)v;
            if (v is decimal) return (int)(decimal)v;
            if (v is double) return (int)(double)v;
            int r;
            return v is string && int.TryParse((string)v, out r) ? r : def;
        }

        // Il modello deve rispondere solo con un oggetto JSON; per sicurezza si prende il primo "{" e l'ultimo "}"
        public static object ExtractObject(string text)
        {
            if (text == null) return null;
            int a = text.IndexOf('{'), b = text.LastIndexOf('}');
            if (a < 0 || b <= a) return null;
            return Parse(text.Substring(a, b - a + 1)) as IDictionary<string, object>;
        }
    }

    // Parte comune delle API HTTP: richiesta JSON senza streaming (le risposte sono brevi), nuovi tentativi
    // se il servizio è sovraccarico, Stop che chiude la connessione.
    abstract class AiHttpProvider : IAiProvider
    {
        public int TimeoutMs = 600000;   // senza streaming la risposta arriva tutta alla fine
        public abstract string Name { get; }
        protected abstract string Url { get; }
        protected abstract void AddHeaders(HttpWebRequest req);
        protected abstract Dictionary<string, object> Body(AiRequest r);
        protected abstract string TextFrom(object response, out string error);

        public AiResult Run(AiRequest r, CancellationToken cancel)
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var res = new AiResult();
            byte[] body;
            try { body = Encoding.UTF8.GetBytes(AiJson.Json.Serialize(Body(r))); }
            catch (Exception ex) { res.Error = ex.Message; return res; }

            for (int attempt = 0; ; attempt++)
            {
                HttpWebRequest req = null;
                try
                {
                    req = (HttpWebRequest)WebRequest.Create(Url);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.UserAgent = "CorbolPrompt/" + AppVersion.Text;
                    req.Timeout = req.ReadWriteTimeout = TimeoutMs;
                    if (req.RequestUri.IsLoopback) req.Proxy = null;   // assistente locale: niente proxy
                    req.ServicePoint.Expect100Continue = false;
                    AddHeaders(req);
                    using (cancel.Register(delegate { try { req.Abort(); } catch { } }))
                    {
                        using (var st = req.GetRequestStream()) st.Write(body, 0, body.Length);
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            string err;
                            res.Text = TextFrom(AiJson.Parse(sr.ReadToEnd()), out err) ?? "";
                            if (err != null) res.Error = Name + ": " + err;
                        }
                    }
                    return res;
                }
                catch (WebException ex)
                {
                    if (cancel.IsCancellationRequested) { res.Cancelled = true; return res; }
                    var http = ex.Response as HttpWebResponse;
                    if (http == null) { res.Error = ConnectionError(ex); return res; }
                    int status = (int)http.StatusCode;
                    string text = "";
                    try { using (var sr = new StreamReader(http.GetResponseStream(), Encoding.UTF8)) text = sr.ReadToEnd(); } catch { }
                    http.Close();
                    if ((status == 429 || status >= 500) && attempt < 2)
                    {
                        cancel.WaitHandle.WaitOne(TimeSpan.FromSeconds(2 << attempt));
                        if (cancel.IsCancellationRequested) { res.Cancelled = true; return res; }
                        continue;
                    }
                    res.Error = HttpError(status, ErrorMessage(text));
                    return res;
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested) { res.Cancelled = true; return res; }
                    res.Error = Name + ": " + ex.Message;
                    return res;
                }
            }
        }

        string ConnectionError(WebException ex)
        {
            switch (ex.Status)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    return L.F("{0}: indirizzo non trovato. Controlla la connessione a Internet o l'indirizzo del servizio.",
                               "{0}: address not found. Check your Internet connection or the service address.", Name);
                case WebExceptionStatus.ConnectFailure:
                    return L.F("{0}: connessione rifiutata. Il servizio è avviato?", "{0}: connection refused. Is the service running?", Name);
                case WebExceptionStatus.Timeout:
                    return L.F("{0}: nessuna risposta (tempo scaduto).", "{0}: no response (timed out).", Name);
                default:
                    return Name + ": " + ex.Message;
            }
        }

        string HttpError(int status, string detail)
        {
            string what;
            if (status == 401) what = L.T("chiave API non valida o mancante", "invalid or missing API key");
            else if (status == 402) what = L.T("credito esaurito o problema di fatturazione", "out of credit or billing problem");
            else if (status == 403) what = L.T("accesso negato", "access denied");
            else if (status == 404) what = L.T("modello o indirizzo non trovato", "model or address not found");
            else if (status == 413) what = L.T("richiesta troppo grande", "request too large");
            else if (status == 429) what = L.T("troppe richieste: riprova tra poco", "too many requests: try again shortly");
            else if (status >= 500) what = L.T("servizio sovraccarico o in errore: riprova tra poco", "service overloaded or failing: try again shortly");
            else what = L.F("errore {0}", "error {0}", status);
            if (!string.IsNullOrEmpty(detail) && detail.Length < 400) what += " (" + detail.Trim() + ")";
            return Name + ": " + what;
        }

        static string ErrorMessage(string body)
        {
            var o = AiJson.Parse(body);
            return AiJson.Str(o, "error", "message") ?? (AiJson.Get(o, "error") as string) ?? AiJson.Str(o, "message")
                ?? (o == null && body != null && body.Length < 200 ? body : null);
        }
    }

    // API Anthropic: https://api.anthropic.com/v1/messages
    class ClaudeProvider : AiHttpProvider
    {
        public const string DefaultModel = "claude-opus-5-5";
        public static readonly string[] Models = { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };
        const string ApiVersion = "2026-10-21";

        public string ApiKey, Model = DefaultModel;
        public override string Name { get { return "Claude"; } }
        protected override string Url { get { return "https://api.anthropic.com/v1/messages"; } }

        protected override void AddHeaders(HttpWebRequest req)
        {
            req.Headers["x-api-key"] = ApiKey ?? "";
            req.Headers["anthropic-version"] = ApiVersion;
        }

        public static object Content(AiMessage m)
        {
            if (m.Jpegs.Count == 0) return m.Text;
            var parts = new List<object>();
            foreach (var j in m.Jpegs)
                parts.Add(new Dictionary<string, object> { { "type", "image" }, { "source", new Dictionary<string, object>
                    { { "type", "base64" }, { "media_type", "image/jpeg" }, { "data", Convert.ToBase64String(j) } } } });
            parts.Add(new Dictionary<string, object> { { "type", "text" }, { "text", m.Text } });
            return parts.ToArray();
        }

        protected override Dictionary<string, object> Body(AiRequest r)
        {
            var b = new Dictionary<string, object>
            {
                { "model", Model },
                { "max_tokens", 16000 },
                { "messages", r.Messages.Select(m => new Dictionary<string, object> { { "role", m.Role }, { "content", Content(m) } }).ToArray() }
            };
            if (!string.IsNullOrEmpty(r.System)) b["system"] = r.System;
            return b;
        }

        protected override string TextFrom(object o, out string error)
        {
            error = AiJson.Str(o, "stop_reason") == "refusal" ? L.T("ha rifiutato la richiesta.", "declined the request.") : null;
            var sb = new StringBuilder();
            var content = AiJson.Get(o, "content") as IList;
            if (content != null)
                foreach (var c in content) if (AiJson.Str(c, "type") == "text") sb.Append(AiJson.Str(c, "text"));
            return sb.ToString();
        }
    }

    // API "chat completions" compatibile OpenAI: <BaseUrl>/chat/completions
    class OpenAiProvider : AiHttpProvider
    {
        public string BaseUrl = "https://api.openai.com/v1", ApiKey, Model;
        public string DisplayName;                    // es. "Assistente locale"; vuoto = ricavato dall'indirizzo
        public int MaxTokens;                         // 0 = valore predefinito del servizio
        public Dictionary<string, object> Extra;      // campi aggiuntivi del corpo (es. per llama-server)

        public override string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(DisplayName)) return DisplayName;
                try { return new Uri(BaseUrl).Host; } catch { return "OpenAI"; }
            }
        }
        protected override string Url { get { return BaseUrl.TrimEnd('/') + "/chat/completions"; } }

        protected override void AddHeaders(HttpWebRequest req)
        {
            if (!string.IsNullOrEmpty(ApiKey)) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + ApiKey;
        }

        protected override Dictionary<string, object> Body(AiRequest r)
        {
            var msgs = new List<object>();
            if (!string.IsNullOrEmpty(r.System)) msgs.Add(new Dictionary<string, object> { { "role", "system" }, { "content", r.System } });
            foreach (var m in r.Messages)
            {
                object content = m.Text;
                if (m.Jpegs.Count > 0)
                {
                    var parts = new List<object> { new Dictionary<string, object> { { "type", "text" }, { "text", m.Text } } };
                    foreach (var j in m.Jpegs)
                        parts.Add(new Dictionary<string, object> { { "type", "image_url" }, { "image_url", new Dictionary<string, object>
                            { { "url", "data:image/jpeg;base64," + Convert.ToBase64String(j) } } } });
                    content = parts.ToArray();
                }
                msgs.Add(new Dictionary<string, object> { { "role", m.Role }, { "content", content } });
            }
            var b = new Dictionary<string, object> { { "model", Model ?? "" }, { "messages", msgs } };
            if (MaxTokens > 0) b["max_tokens"] = MaxTokens;
            if (Extra != null) foreach (var kv in Extra) b[kv.Key] = kv.Value;
            return b;
        }

        protected override string TextFrom(object o, out string error)
        {
            error = AiJson.Get(o, "error") != null ? (AiJson.Str(o, "error", "message") ?? L.T("errore", "error")) : null;
            return AiJson.Str(o, "choices", 0, "message", "content");
        }
    }

    // Claude Code installato sul PC, con l'abbonamento dell'utente. CorbolPrompt avvia "claude -p" senza strumenti
    // (non legge file né esegue comandi), senza server MCP e senza salvare la sessione, in una cartella vuota;
    // la conversazione e l'immagine arrivano su stdin in formato stream-json.
    class ClaudeCodeProvider : IAiProvider
    {
        public static readonly string[] Models = { "opus", "sonnet", "haiku" };
        public string Model = "";   // vuoto = il modello predefinito di Claude Code per l'account
        // Cartella di configurazione di Claude Code (CLAUDE_CONFIG_DIR), cioè l'account con cui gira:
        // vuoto = quella predefinita (%USERPROFILE%\.claude), o quella già impostata nell'ambiente
        public string ConfigDir = "";

        // Nome da mostrare nell'app, con l'account (email) con cui gira Claude Code.
        // Non va nei testi che possono finire in segnalazioni o rapporti: lì resta Name.
        public string DisplayName { get { return Name + " · " + (EmailOf(ConfigDir ?? "") ?? L.T("login non fatto", "not logged in")); } }
        public static string DisplayNameOf(IAiProvider p) { var c = p as ClaudeCodeProvider; return c != null ? c.DisplayName : p == null ? "" : p.Name; }
        public static string AccountEmail(string dir) { return EmailOf(dir ?? ""); }

        // ---------- account (una cartella di configurazione per ogni login) ----------

        static string Home { get { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } }

        public class Account
        {
            public string Dir;      // "" = predefinito
            public string Email;    // null = login non ancora fatto
            public string Label
            {
                get
                {
                    string who = Email ?? L.T("login non fatto", "not logged in");
                    return Dir.Length == 0 ? L.F("Predefinito ({0})", "Default ({0})", who) : who + "  ·  " + Path.GetFileName(Dir.TrimEnd('\\'));
                }
            }
        }

        static readonly Dictionary<string, KeyValuePair<DateTime, string>> emailCache = new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);

        // Email dell'account registrato in una cartella di configurazione, o null
        static string EmailOf(string dir)
        {
            try
            {
                // predefinito: %USERPROFILE%\.claude.json; con CLAUDE_CONFIG_DIR: <cartella>\.claude.json
                string env = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                string f = dir.Length > 0 ? Path.Combine(dir, ".claude.json")
                         : !string.IsNullOrEmpty(env) ? Path.Combine(env, ".claude.json") : Path.Combine(Home, ".claude.json");
                if (!File.Exists(f)) return null;
                // .claude.json può essere grande: si rilegge solo quando cambia
                var t = File.GetLastWriteTimeUtc(f);
                lock (emailCache)
                {
                    KeyValuePair<DateTime, string> c;
                    if (emailCache.TryGetValue(f, out c) && c.Key == t) return c.Value;
                }
                string email = AiJson.Str(AiJson.Parse(File.ReadAllText(f, Encoding.UTF8)), "oauthAccount", "emailAddress");
                lock (emailCache) emailCache[f] = new KeyValuePair<DateTime, string>(t, email);
                return email;
            }
            catch { return null; }
        }

        // Il predefinito più le cartelle %USERPROFILE%\.claude-* (create con AddAccount o a mano)
        public static List<Account> Accounts()
        {
            var list = new List<Account> { new Account { Dir = "", Email = EmailOf("") } };
            try
            {
                foreach (var d in Directory.GetDirectories(Home, ".claude-*").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    list.Add(new Account { Dir = d, Email = EmailOf(d) });
            }
            catch { }
            return list;
        }

        // Crea la cartella %USERPROFILE%\.claude-<nome> e apre Claude Code in una finestra per fare il login
        // con l'altro account. Restituisce la cartella, o null con l'errore in error.
        public static string AddAccount(string name, out string error)
        {
            error = null;
            name = new string((name ?? "").Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            if (name.Length == 0) { error = L.T("Scrivi un nome per l'account (es. lavoro).", "Enter a name for the account (e.g. work)."); return null; }
            string exe = FindExe();
            if (exe == null) { error = L.T("Claude Code non è installato su questo PC.", "Claude Code is not installed on this PC."); return null; }
            string dir = Path.Combine(Home, ".claude-" + name);
            try
            {
                Directory.CreateDirectory(dir);
                OpenLogin(exe, dir);
                return dir;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        // Finestra del prompt dei comandi con Claude Code sull'account della cartella: al primo avvio chiede il login
        public static void OpenLogin(string exe, string dir)
        {
            var psi = new ProcessStartInfo("cmd.exe", "/k \"\"" + exe + "\"\"")
            {
                UseShellExecute = false,
                WorkingDirectory = Home
            };
            psi.EnvironmentVariables.Remove("CLAUDECODE");
            psi.EnvironmentVariables.Remove("CLAUDE_CODE_ENTRYPOINT");
            if (dir.Length > 0) psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = dir;
            Process.Start(psi);
        }
        public string Name { get { return "Claude Code"; } }

        // claude.exe (installazione nativa) o claude.cmd (installazione con npm), o null
        public static string FindExe()
        {
            var dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(d => d.Trim().Length > 0));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            dirs.Add(Path.Combine(home, ".local", "bin"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
                foreach (var d in dirs)
                {
                    try
                    {
                        string f = Path.Combine(d.Trim().Trim('"'), name);
                        if (File.Exists(f)) return f;
                    }
                    catch { }
                }
            return null;
        }

        // "claude -p" riceve un solo messaggio: quelli precedenti vanno scritti nel testo, prima dell'ultimo
        static string UserText(AiRequest r)
        {
            if (r.Messages.Count == 1) return r.Messages[0].Text;
            var sb = new StringBuilder("<conversation_so_far>\n");
            for (int i = 0; i < r.Messages.Count - 1; i++)
            {
                var m = r.Messages[i];
                string tag = m.Role == "assistant" ? "assistant" : "user";
                sb.Append('<').Append(tag).Append(">\n").Append(m.Text).Append("\n</").Append(tag).Append(">\n");
            }
            sb.Append("</conversation_so_far>\n\n").Append(r.Messages[r.Messages.Count - 1].Text);
            return sb.ToString();
        }

        static void KillTree(Process p)
        {
            try
            {
                if (p == null || p.HasExited) return;
                using (var k = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }))
                    k.WaitForExit(5000);
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        public AiResult Run(AiRequest r, CancellationToken cancel)
        {
            var res = new AiResult();
            string exe = FindExe();
            if (exe == null)
            {
                res.Error = L.T("Claude Code non è installato su questo PC (comando claude non trovato).", "Claude Code is not installed on this PC (claude command not found).");
                return res;
            }
            string dir = Path.Combine(Path.GetTempPath(), "CorbolPrompt-claudecode");
            string sysFile = Path.Combine(dir, "system-" + Guid.NewGuid().ToString("N") + ".txt");
            var err = new StringBuilder();
            Process p = null;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(sysFile, r.System ?? "", new UTF8Encoding(false));
                string args = "-p --input-format stream-json --output-format stream-json --verbose --tools \"\" --no-session-persistence"
                            + " --strict-mcp-config --system-prompt-file \"" + sysFile + "\""
                            + (string.IsNullOrEmpty(Model) ? "" : " --model " + Model);
                bool cmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
                var psi = new ProcessStartInfo(cmd ? "cmd.exe" : exe, cmd ? "/c \"\"" + exe + "\" " + args + "\"" : args)
                {
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                // se CorbolPrompt è stato avviato da una sessione di Claude Code, quella nuova deve essere indipendente
                psi.EnvironmentVariables.Remove("CLAUDECODE");
                psi.EnvironmentVariables.Remove("CLAUDE_CODE_ENTRYPOINT");
                // l'account scelto nelle impostazioni (altrimenti quello predefinito di Claude Code)
                if (!string.IsNullOrEmpty(ConfigDir)) psi.EnvironmentVariables["CLAUDE_CONFIG_DIR"] = ConfigDir;
                p = Process.Start(psi);
                var proc = p;
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (err) err.AppendLine(e.Data); };
                p.BeginErrorReadLine();
                using (cancel.Register(delegate { KillTree(proc); }))
                {
                    var last = new AiMessage("user", UserText(r));
                    last.Jpegs.AddRange(r.Messages[r.Messages.Count - 1].Jpegs);
                    var line0 = AiJson.Json.Serialize(new Dictionary<string, object>
                    {
                        { "type", "user" },
                        { "message", new Dictionary<string, object> { { "role", "user" }, { "content", ClaudeProvider.Content(last) } } }
                    });
                    var input = Encoding.UTF8.GetBytes(line0 + "\n");
                    p.StandardInput.BaseStream.Write(input, 0, input.Length);
                    p.StandardInput.Close();

                    string line;
                    bool gotResult = false;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        var o = AiJson.Parse(line);
                        if (AiJson.Str(o, "type") != "result") continue;
                        gotResult = true;
                        bool isError = AiJson.Get(o, "is_error") is bool && (bool)AiJson.Get(o, "is_error");
                        string text = AiJson.Str(o, "result");
                        if (isError || AiJson.Str(o, "subtype") != "success")
                            res.Error = "Claude Code: " + (text ?? AiJson.Str(o, "subtype") ?? L.T("errore", "error"));
                        else res.Text = text ?? "";
                        break;
                    }
                    p.WaitForExit(10000);
                    if (cancel.IsCancellationRequested) { res.Cancelled = true; return res; }
                    if (!gotResult && res.Error == null)
                    {
                        string tail;
                        lock (err) tail = err.ToString().Trim();
                        if (tail.Length > 300) tail = tail.Substring(tail.Length - 300);
                        res.Error = L.F("Claude Code si è chiuso senza rispondere (codice {0}). Hai fatto il login in Claude Code?",
                                        "Claude Code closed without answering (code {0}). Are you logged in to Claude Code?", p.HasExited ? p.ExitCode : -1)
                                  + (tail.Length > 0 ? "\n" + tail : "");
                    }
                }
            }
            catch (Exception ex)
            {
                if (cancel.IsCancellationRequested) res.Cancelled = true;
                else res.Error = L.T("Impossibile avviare Claude Code:", "Could not start Claude Code:") + " " + ex.Message;
            }
            finally
            {
                KillTree(p);
                try { File.Delete(sysFile); } catch { }
            }
            return res;
        }
    }

    // Chiavi API in settings.txt cifrate con DPAPI: le legge solo lo stesso utente Windows sullo stesso PC
    static class Secret
    {
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CorbolPrompt.ai.v1");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith("dpapi:")) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(6)), Entropy, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }
    }

    // Impostazioni dell'assistente in settings.txt (chiavi "ai.*")
    static class AiSettings
    {
        public const string Claude = "claude", ClaudeCode = "claudecode", OpenAi = "openai", Local = "local";
        // Assistente locale: modello del catalogo e variante di llama.cpp ("auto" = scheda video se c'è, "cpu")
        public static string LocalModel { get { return Settings.Get("ai.local.model", AiCatalog.Recommended().Id); } set { Settings.Set("ai.local.model", value); } }
        public static string LocalVariant { get { return Settings.Get("ai.local.variant", "auto"); } set { Settings.Set("ai.local.variant", value); } }

        public static string Provider { get { return Settings.Get("ai.provider", ClaudeCode); } set { Settings.Set("ai.provider", value); } }
        public static string ClaudeKey { get { return Secret.Unprotect(Settings.Get("ai.claude.key", "")); } set { Settings.Set("ai.claude.key", Secret.Protect(value)); } }
        public static string ClaudeModel { get { return Settings.Get("ai.claude.model", ClaudeProvider.DefaultModel); } set { Settings.Set("ai.claude.model", value); } }
        public static string ClaudeCodeModel { get { return Settings.Get("ai.claudecode.model", ""); } set { Settings.Set("ai.claudecode.model", value); } }
        // Claude Code: account da usare = cartella di configurazione (CLAUDE_CONFIG_DIR); vuoto = predefinito
        public static string ClaudeCodeConfig { get { return Settings.Get("ai.claudecode.config", ""); } set { Settings.Set("ai.claudecode.config", value); } }
        public static string OpenAiUrl { get { return Settings.Get("ai.openai.url", "https://api.openai.com/v1"); } set { Settings.Set("ai.openai.url", value); } }
        public static string OpenAiKey { get { return Secret.Unprotect(Settings.Get("ai.openai.key", "")); } set { Settings.Set("ai.openai.key", Secret.Protect(value)); } }
        public static string OpenAiModel { get { return Settings.Get("ai.openai.model", ""); } set { Settings.Set("ai.openai.model", value); } }
        // Le ultime righe del terminale vanno all'assistente: così vede output ed errori
        public static bool SendScreen { get { return Settings.Get("ai.sendScreen", true); } set { Settings.Set("ai.sendScreen", value); } }

        // Provider configurato, o null con il motivo in "missing"
        public static IAiProvider Create(out string missing)
        {
            missing = null;
            if (Provider == Local)
            {
                missing = LocalAi.NotReadyReason();
                return missing == null ? LocalAi.Instance : null;
            }
            if (Provider == Claude)
            {
                string key = ClaudeKey;
                if (key.Length > 0) return new ClaudeProvider { ApiKey = key, Model = ClaudeModel };
                missing = L.T("Manca la chiave API di Claude.", "The Claude API key is missing.");
                return null;
            }
            if (Provider == OpenAi)
            {
                if (OpenAiModel.Length > 0 && OpenAiUrl.Length > 0) return new OpenAiProvider { BaseUrl = OpenAiUrl, ApiKey = OpenAiKey, Model = OpenAiModel };
                missing = L.T("Manca l'indirizzo o il modello del servizio compatibile OpenAI.", "The OpenAI-compatible service address or model is missing.");
                return null;
            }
            return new ClaudeCodeProvider { Model = ClaudeCodeModel, ConfigDir = ClaudeCodeConfig };
        }

        public static string Describe()
        {
            if (Provider == Local) return L.T("Assistente locale", "Local assistant") + " · " + LocalAi.ConfiguredModel().Name;
            if (Provider == Claude) return "Claude API · " + ClaudeModel;
            if (Provider == OpenAi) return (OpenAiModel.Length > 0 ? OpenAiModel : "OpenAI") + " · " + OpenAiUrl;
            return "Claude Code" + (ClaudeCodeModel.Length > 0 ? " · " + ClaudeCodeModel : "") + " · " + (ClaudeCodeProvider.AccountEmail(ClaudeCodeConfig) ?? L.T("login non fatto", "not logged in"));
        }
    }

    // Finestra delle impostazioni dell'assistente
    class AiSettingsForm : Form
    {
        readonly RadioButton rCode = new RadioButton(), rClaude = new RadioButton(), rOpen = new RadioButton(), rLocal = new RadioButton();
        readonly ComboBox cCodeModel = new ComboBox(), cClaudeModel = new ComboBox();
        readonly ComboBox cCodeAccount = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly List<string> accountDirs = new List<string>();   // cartella di ogni voce di cCodeAccount ("" = predefinito)
        readonly Label lAccount = new Label { AutoSize = false, ForeColor = Color.DimGray };
        readonly TextBox tClaudeKey = new TextBox { UseSystemPasswordChar = true }, tUrl = new TextBox(), tKey = new TextBox { UseSystemPasswordChar = true }, tModel = new TextBox();
        readonly CheckBox chImage = new CheckBox();
        // assistente locale
        readonly ComboBox cLocalModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox chGpu = new CheckBox();
        readonly Label lLocal = new Label { AutoSize = false, ForeColor = Color.DimGray };
        readonly Button btDownload = new Button();
        readonly ProgressBar bar = new ProgressBar { Maximum = 1000, Visible = false };
        readonly LinkLabel lnkRemove = new LinkLabel { AutoSize = true };
        readonly AiDownloader downloader = new AiDownloader();
        static readonly Color OkColor = Color.FromArgb(30, 130, 60), ErrColor = Color.Firebrick;

        public AiSettingsForm()
        {
            Text = L.T("Assistente IA", "AI assistant");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            ClientSize = new Size(480, 470);

            var intro = new Label
            {
                AutoSize = true, MaximumSize = new Size(440, 0), Location = new Point(20, 14), ForeColor = Color.DimGray,
                Text = L.T("L'assistente spiega comandi ed errori e propone comandi da eseguire: li esegui tu, con un clic. All'assistente vanno le tue richieste, la shell e la cartella della scheda, i nomi delle altre schede e, se lo permetti, le ultime righe del terminale (anche delle schede che nomini).",
                           "The assistant explains commands and errors and suggests commands to run: you run them, with a click. The assistant gets your requests, the shell and folder of the tab, the names of the other tabs and, if you allow it, the last lines of the terminal (also of the tabs you name)."),
                Font = Font
            };
            // le opzioni partono sotto il testo introduttivo, qualunque sia la sua altezza (lingua, righe)
            int y = intro.Top + intro.GetPreferredSize(new Size(440, 0)).Height + 16;
            rCode.Text = L.T("Claude Code (abbonamento Claude, nessuna chiave)", "Claude Code (Claude subscription, no key)");
            rCode.Location = new Point(20, y); rCode.AutoSize = true;
            var lCodeModel = new Label { Text = L.T("Modello:", "Model:"), AutoSize = true, Location = new Point(40, y + 30) };
            cCodeModel.Location = new Point(140, y + 26); cCodeModel.Width = 200;
            cCodeModel.Items.Add(L.T("(predefinito)", "(default)")); cCodeModel.Items.AddRange(ClaudeCodeProvider.Models);
            // account: uno per ogni login di Claude Code
            var lCodeAcc = new Label { Text = L.T("Account:", "Account:"), AutoSize = true, Location = new Point(40, y + 60) };
            cCodeAccount.Location = new Point(140, y + 56); cCodeAccount.Width = 310;
            cCodeAccount.DropDown += delegate { FillAccounts(SelectedAccount()); };   // dopo il login nella finestra compare l'email
            var btAddAcc = new Button { Text = L.T("Aggiungi...", "Add..."), Location = new Point(140, y + 86), Size = new Size(110, 26) };
            var btLoginAcc = new Button { Text = L.T("Accedi di nuovo...", "Log in again..."), Location = new Point(258, y + 86), Size = new Size(150, 26) };
            btAddAcc.Click += delegate { AddAccount(); };
            btLoginAcc.Click += delegate { LoginAgain(); };
            lAccount.Location = new Point(140, y + 116); lAccount.Size = new Size(310, 34); lAccount.Font = Font;
            y += 156;
            rClaude.Text = L.T("Claude (API Anthropic, con chiave)", "Claude (Anthropic API, with key)");
            rClaude.Location = new Point(20, y); rClaude.AutoSize = true;
            var lCk = new Label { Text = L.T("Chiave API:", "API key:"), AutoSize = true, Location = new Point(40, y + 30) };
            tClaudeKey.Location = new Point(140, y + 26); tClaudeKey.Width = 310;
            var lCm = new Label { Text = L.T("Modello:", "Model:"), AutoSize = true, Location = new Point(40, y + 60) };
            cClaudeModel.Location = new Point(140, y + 56); cClaudeModel.Width = 200; cClaudeModel.Items.AddRange(ClaudeProvider.Models);
            y += 94;
            rOpen.Text = L.T("Compatibile OpenAI (OpenAI, OpenRouter, Ollama, LM Studio...)", "OpenAI-compatible (OpenAI, OpenRouter, Ollama, LM Studio...)");
            rOpen.Location = new Point(20, y); rOpen.AutoSize = true;
            var lU = new Label { Text = L.T("Indirizzo:", "Address:"), AutoSize = true, Location = new Point(40, y + 30) };
            tUrl.Location = new Point(140, y + 26); tUrl.Width = 310;
            var lK = new Label { Text = L.T("Chiave API:", "API key:"), AutoSize = true, Location = new Point(40, y + 60) };
            tKey.Location = new Point(140, y + 56); tKey.Width = 310;
            var lM = new Label { Text = L.T("Modello:", "Model:"), AutoSize = true, Location = new Point(40, y + 90) };
            tModel.Location = new Point(140, y + 86); tModel.Width = 200;
            y += 126;

            // ---- assistente locale ----
            rLocal.Text = L.T("Assistente locale sul PC: gratis, i dati non escono", "Local assistant on this PC: free, data stays here");
            rLocal.Location = new Point(20, y); rLocal.AutoSize = true;
            var lLocalNote = new Label
            {
                Text = L.T("Ministral 3 (Mistral AI) o Qwen3.5 (Alibaba) con llama.cpp. Più lento e meno preciso di Claude: va bene per domande semplici. Serve un PC con almeno 16 GB di RAM.",
                           "Ministral 3 (Mistral AI) or Qwen3.5 (Alibaba) with llama.cpp. Slower and less precise than Claude: fine for simple questions. Needs a PC with at least 16 GB of RAM."),
                AutoSize = true, MaximumSize = new Size(420, 0), Location = new Point(38, y + 24), ForeColor = Color.DimGray, Font = Font
            };
            y += 24 + lLocalNote.GetPreferredSize(new Size(420, 0)).Height + 8;
            var lLm = new Label { Text = L.T("Modello:", "Model:"), AutoSize = true, Location = new Point(40, y + 4) };
            cLocalModel.Location = new Point(140, y); cLocalModel.Width = 310;
            foreach (var m in AiCatalog.Models)
                cLocalModel.Items.Add(m.Name + " · " + AiDownloader.FormatBytes(m.TotalSize) + (m == AiCatalog.Recommended() ? " · " + L.T("consigliato", "recommended") : ""));
            y += 32;
            chGpu.Text = L.T("Usa la scheda video (più veloce)", "Use the graphics card (faster)");
            chGpu.Location = new Point(40, y); chGpu.AutoSize = true;
            var tips = new ToolTip();
            tips.SetToolTip(chGpu, L.T("Se la scheda video dà problemi, l'assistente passa da solo al processore.", "If the graphics card fails, the assistant switches to the processor on its own."));
            y += 28;
            lLocal.Location = new Point(40, y); lLocal.Size = new Size(410, 48); lLocal.Font = Font;
            y += 52;
            btDownload.Text = L.T("Scarica", "Download");
            btDownload.Location = new Point(40, y); btDownload.Size = new Size(110, 28);
            bar.Location = new Point(160, y + 9); bar.Size = new Size(290, 10);
            lnkRemove.Location = new Point(160, y + 6);
            y += 40;
            btDownload.Click += delegate { StartDownload(); };
            lnkRemove.LinkClicked += delegate { RemoveFiles(); };
            downloader.Progress += st => { try { BeginInvoke((Action)(() => OnProgress(st))); } catch { } };
            cLocalModel.SelectedIndexChanged += delegate { UpdateLocal(); };
            chGpu.CheckedChanged += delegate { UpdateLocal(); };
            rLocal.CheckedChanged += delegate { UpdateLocal(); };
            FormClosing += delegate { if (downloader.Busy) downloader.Cancel(); };   // riprende da dove era arrivato la prossima volta
            chImage.Text = L.T("Manda all'assistente le ultime righe del terminale", "Send the assistant the last lines of the terminal");
            chImage.Location = new Point(20, y); chImage.AutoSize = true;
            var lImage = new Label
            {
                Text = L.T("Così vede l'output e gli errori dei comandi. Toglila se sullo schermo ci sono password o dati riservati.", "So it sees the output and errors of commands. Turn it off if there are passwords or confidential data on the screen."),
                AutoSize = true, MaximumSize = new Size(420, 0), Location = new Point(38, y + 24), ForeColor = Color.DimGray, Font = Font
            };
            ClientSize = new Size(ClientSize.Width, y + 24 + lImage.GetPreferredSize(new Size(420, 0)).Height + 64);

            var ok = new Button { Text = "OK", Size = new Size(90, 30), Location = new Point(ClientSize.Width - 200, ClientSize.Height - 44) };
            var cancel = new Button { Text = L.T("Annulla", "Cancel"), Size = new Size(90, 30), Location = new Point(ClientSize.Width - 104, ClientSize.Height - 44), DialogResult = DialogResult.Cancel };
            ok.Click += delegate { SaveAll(); DialogResult = DialogResult.OK; Close(); };
            cancel.Click += delegate { Close(); };
            AcceptButton = ok; CancelButton = cancel;
            Controls.AddRange(new Control[] { lCodeAcc, cCodeAccount, btAddAcc, btLoginAcc, lAccount });
            Controls.AddRange(new Control[] { intro, rCode, lCodeModel, cCodeModel, rClaude, lCk, tClaudeKey, lCm, cClaudeModel,
                                              rOpen, lU, tUrl, lK, tKey, lM, tModel, rLocal, lLocalNote, lLm, cLocalModel, chGpu, lLocal,
                                              btDownload, bar, lnkRemove, chImage, lImage, ok, cancel });

            string p = AiSettings.Provider;
            rCode.Checked = p == AiSettings.ClaudeCode; rClaude.Checked = p == AiSettings.Claude; rOpen.Checked = p == AiSettings.OpenAi; rLocal.Checked = p == AiSettings.Local;
            cLocalModel.SelectedIndex = Math.Max(0, Array.IndexOf(AiCatalog.Models, LocalAi.ConfiguredModel()));
            chGpu.Checked = AiSettings.LocalVariant != "cpu";
            UpdateLocal();
            string cm = AiSettings.ClaudeCodeModel;
            if (cm.Length == 0) cCodeModel.SelectedIndex = 0; else cCodeModel.Text = cm;
            FillAccounts(AiSettings.ClaudeCodeConfig);
            tClaudeKey.Text = AiSettings.ClaudeKey;
            cClaudeModel.Text = AiSettings.ClaudeModel;
            tUrl.Text = AiSettings.OpenAiUrl; tKey.Text = AiSettings.OpenAiKey; tModel.Text = AiSettings.OpenAiModel;
            chImage.Checked = AiSettings.SendScreen;
        }

        // ---------- account di Claude Code (uno per ogni login, ognuno nella sua cartella di configurazione) ----------

        string SelectedAccount()
        {
            int i = cCodeAccount.SelectedIndex;
            return i >= 0 && i < accountDirs.Count ? accountDirs[i] : "";
        }

        void FillAccounts(string select)
        {
            cCodeAccount.Items.Clear();
            accountDirs.Clear();
            foreach (var a in ClaudeCodeProvider.Accounts()) { cCodeAccount.Items.Add(a.Label); accountDirs.Add(a.Dir); }
            int i = accountDirs.FindIndex(x => string.Equals(x, select ?? "", StringComparison.OrdinalIgnoreCase));
            if (i < 0 && !string.IsNullOrEmpty(select))
            {
                // cartella salvata che non è più tra quelle trovate: si mostra lo stesso
                cCodeAccount.Items.Add(select); accountDirs.Add(select);
                i = accountDirs.Count - 1;
            }
            cCodeAccount.SelectedIndex = Math.Max(0, i);
        }

        void AccountNote(string text, bool error)
        {
            lAccount.Text = text;
            lAccount.ForeColor = error ? ErrColor : Color.DimGray;
        }

        // Chiede un nome, crea la cartella e apre Claude Code per il login con l'altro account
        void AddAccount()
        {
            string name = Prompt(L.T("Nome del nuovo account (es. lavoro). Si apre Claude Code in una finestra: fai il login con l'altra utenza, poi chiudi la finestra.",
                                     "Name of the new account (e.g. work). Claude Code opens in a window: log in with the other account, then close the window."));
            if (name == null) return;
            string err;
            string dir = ClaudeCodeProvider.AddAccount(name, out err);
            if (dir == null) { AccountNote(err, true); return; }
            FillAccounts(dir);
            AccountNote(L.T("Fai il login nella finestra di Claude Code; poi riapri l'elenco Account per vedere l'email.",
                            "Log in in the Claude Code window; then reopen the Account list to see the email."), false);
        }

        void LoginAgain()
        {
            string exe = ClaudeCodeProvider.FindExe();
            if (exe == null) { AccountNote(L.T("Claude Code non è installato su questo PC.", "Claude Code is not installed on this PC."), true); return; }
            try { ClaudeCodeProvider.OpenLogin(exe, SelectedAccount()); }
            catch (Exception ex) { AccountNote(ex.Message, true); return; }
            AccountNote(L.T("Nella finestra di Claude Code scrivi /login ed entra con l'utenza giusta, poi riapri l'elenco Account.",
                            "In the Claude Code window type /login and sign in with the right account, then reopen the Account list."), false);
        }

        string Prompt(string text)
        {
            using (var f = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                                      StartPosition = FormStartPosition.CenterParent, Font = Font, AutoScaleDimensions = new SizeF(96f, 96f), AutoScaleMode = AutoScaleMode.Dpi,
                                      ClientSize = new Size(400, 150), BackColor = Color.White })
            {
                var lbl = new Label { Text = text, AutoSize = false, Location = new Point(12, 10), Size = new Size(376, 54) };
                var txt = new TextBox { Location = new Point(12, 70), Width = 376 };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(196, 108), Size = new Size(92, 28) };
                var cancel = new Button { Text = L.T("Annulla", "Cancel"), DialogResult = DialogResult.Cancel, Location = new Point(296, 108), Size = new Size(92, 28) };
                f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
            }
        }

        // ---------- assistente locale ----------

        AiModel SelectedModel() { return AiCatalog.Models[Math.Max(0, cLocalModel.SelectedIndex)]; }
        string PackageVariant() { return chGpu.Checked ? "vulkan" : "cpu"; }

        void UpdateLocal()
        {
            if (downloader.Busy) return;
            var m = SelectedModel();
            string v = PackageVariant();
            bool model = AiDownloader.ModelReady(m);
            bool llama = AiDownloader.LlamaReady(v) || (chGpu.Checked && AiDownloader.LlamaReady("cpu"));
            string why = AiCatalog.Unsuitable(m);
            bar.Visible = false;
            if (model && llama)
            {
                string other = AiDownloader.LlamaFromOtherApp(v) ?? AiDownloader.LlamaFromOtherApp("cpu");
                lLocal.Text = L.T("Pronto.", "Ready.") + (other != null ? " " + L.F("Usa llama.cpp già scaricato da {0}.", "Uses llama.cpp already downloaded by {0}.", other) : "");
                lLocal.ForeColor = OkColor;
                btDownload.Visible = false;
            }
            else
            {
                long need = AiDownloader.ModelMissingBytes(m) + (llama ? 0 : AiDownloader.LlamaSize(v));
                lLocal.Text = why ?? (llama
                    ? L.F("Da scaricare: {0} (modello da Hugging Face; llama.cpp c'è già). Si scarica una volta sola.",
                          "To download: {0} (model from Hugging Face; llama.cpp is already here). Downloaded only once.", AiDownloader.FormatBytes(need))
                    : L.F("Da scaricare: {0} (modello da Hugging Face, llama.cpp da GitHub). Si scarica una volta sola.",
                          "To download: {0} (model from Hugging Face, llama.cpp from GitHub). Downloaded only once.", AiDownloader.FormatBytes(need)));
                lLocal.ForeColor = why != null ? ErrColor : Color.DimGray;
                btDownload.Visible = true;
                btDownload.Text = L.T("Scarica", "Download");
                btDownload.Enabled = why == null;
            }
            long used = AiDownloader.UsedBytes();
            lnkRemove.Visible = used > 0;
            lnkRemove.Left = btDownload.Visible ? 160 : 40;
            lnkRemove.Text = L.F("Elimina i file scaricati ({0})", "Delete downloaded files ({0})", AiDownloader.FormatBytes(used));
        }

        void StartDownload()
        {
            if (downloader.Busy) { downloader.Cancel(); return; }   // il pulsante diventa "Interrompi"
            btDownload.Text = L.T("Interrompi", "Stop");
            bar.Visible = true; bar.Value = 0;
            lnkRemove.Visible = false;
            lLocal.ForeColor = Color.DimGray;
            lLocal.Text = L.T("Avvio dello scaricamento…", "Starting the download…");
            downloader.Start(SelectedModel(), PackageVariant());
        }

        void OnProgress(AiDownloadStatus st)
        {
            if (IsDisposed) return;
            if (st.Finished)
            {
                UpdateLocal();
                if (st.Error != null) { lLocal.Text = st.Error; lLocal.ForeColor = ErrColor; }
                return;
            }
            string pct = st.Total > 0 ? " " + (st.Done * 100 / st.Total) + "% (" + AiDownloader.FormatBytes(st.Done) + " / " + AiDownloader.FormatBytes(st.Total) + ")" : "";
            lLocal.Text = st.Text + pct;
            lLocal.ForeColor = Color.DimGray;
            bar.Style = st.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            if (st.Total > 0) bar.Value = (int)Math.Min(1000, st.Done * 1000 / st.Total);
        }

        void RemoveFiles()
        {
            if (MessageBox.Show(this, L.F("Eliminare i file dell'assistente locale scaricati da CorbolPrompt ({0})? Si potranno riscaricare.",
                                          "Delete the local assistant files downloaded by CorbolPrompt ({0})? They can be downloaded again.", AiDownloader.FormatBytes(AiDownloader.UsedBytes())),
                                "CorbolPrompt", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            LocalAi.Instance.Stop();
            AiDownloader.RemoveAll();
            UpdateLocal();
        }

        void SaveAll()
        {
            AiSettings.Provider = rLocal.Checked ? AiSettings.Local : rClaude.Checked ? AiSettings.Claude : rOpen.Checked ? AiSettings.OpenAi : AiSettings.ClaudeCode;
            AiSettings.LocalModel = SelectedModel().Id;
            AiSettings.LocalVariant = chGpu.Checked ? "auto" : "cpu";
            if (AiSettings.Provider != AiSettings.Local) LocalAi.Instance.Stop();   // libera la memoria
            else LocalAi.Crashes = 0;                                               // scelta di nuovo: si riprova
            AiSettings.ClaudeCodeModel = cCodeModel.SelectedIndex == 0 ? "" : cCodeModel.Text.Trim();
            AiSettings.ClaudeCodeConfig = SelectedAccount();
            AiSettings.ClaudeKey = tClaudeKey.Text.Trim();
            AiSettings.ClaudeModel = cClaudeModel.Text.Trim().Length > 0 ? cClaudeModel.Text.Trim() : ClaudeProvider.DefaultModel;
            AiSettings.OpenAiUrl = tUrl.Text.Trim(); AiSettings.OpenAiKey = tKey.Text.Trim(); AiSettings.OpenAiModel = tModel.Text.Trim();
            AiSettings.SendScreen = chImage.Checked;
            Settings.Save();
        }
    }
}
