// Lisp di CorbolPrompt: funzioni per Windows.
// Appunti (clipboard, set-clipboard), notifiche (notify), web (http-get, http-post, http-json),
// ripetizioni (watch ridisegna un risultato sul posto, every ripete un'azione), finestre di scelta (choose-file, choose-folder).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CorbolPrompt
{
    partial class Interp
    {
        // Li imposta l'app: eseguire sul thread dell'interfaccia (con la finestra come proprietaria dei dialoghi)
        // e mostrare una notifica di Windows. Senza (nei test) si usa un thread STA a parte e la notifica diventa una riga di testo.
        public Func<Func<IWin32Window, object>, object> UiInvoke;
        public Action<string, string> NotifyHook;
        public int TermRows;

        object OnUi(Func<IWin32Window, object> f)
        {
            if (UiInvoke != null) return UiInvoke(f);
            object r = null;
            Exception err = null;
            var t = new Thread(() => { try { r = f(null); } catch (Exception e) { err = e; } });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            if (err != null) throw new LispError(err.Message);
            return r;
        }

        void WinBuiltins()
        {
            // ---- appunti ----
            Def("clipboard", 0, 0, a => OnUi(w => Clipboard.ContainsText() ? Clipboard.GetText() : null));
            Def("set-clipboard", 1, 1, a =>
            {
                string s = ClipboardText(a[0]);
                OnUi(w => { if (s.Length == 0) Clipboard.Clear(); else Clipboard.SetText(s); return null; });
                return a[0];
            });

            // ---- notifica ----
            Def("notify", 1, 2, a =>
            {
                string title = Printer.Print(a[0], false), text = a.Length > 1 ? Printer.Print(a[1], false) : "";
                if (NotifyHook != null) NotifyHook(title, text);
                else { FreshLine(); Write("\x1b[93m🔔 " + title + (text.Length > 0 ? " — " + text : "") + "\x1b[0m\n"); }
                return null;
            });

            // ---- web ----
            // (http-get url [:headers (:accept "text/csv")] [:timeout 30]) → il testo della risposta
            Def("http-get", 1, -1, a => Http("GET", Str(a[0]), null, KeyArg(a, 1, ":headers", null), KeyArg(a, 1, ":timeout", null)));
            // (http-post url corpo ...): un corpo che non è una stringa va come JSON
            Def("http-post", 2, -1, a => Http("POST", Str(a[0]), a[1], KeyArg(a, 2, ":headers", null), KeyArg(a, 2, ":timeout", null)));
            // (http-json url [:body dato] ...): la risposta letta come json-parse; con :body è una POST
            Def("http-json", 1, -1, a =>
            {
                var body = KeyArg(a, 1, ":body", null);
                string text = Http(body != null ? "POST" : "GET", Str(a[0]), body, KeyArg(a, 1, ":headers", null), KeyArg(a, 1, ":timeout", null), true);
                return new JsonReader(this, text).ReadAll();
            });

            // ---- ripetere ----
            Def("watch-fn", 3, 3, a => Watch(Num.Dbl(a[0]), a[1] == null ? 0 : Num.ToInt(a[1]), a[2]));
            EvalString(@"
(defmacro watch (seconds &rest body)
  (let ((times nil))
    (when (eq (car body) :times) (setq times (cadr body) body (cddr body)))
    `(block nil (watch-fn ,seconds ,times (lambda () ,@body)))))
(defmacro every (seconds &rest body)
  `(block nil (loop (progn ,@body) (sleep ,seconds))))");

            // ---- scegliere file e cartelle ----
            // (choose-file [:title "..."] [:filter "*.csv"] [:save t] [:multiple t]) → il percorso, o nil se si annulla
            Def("choose-file", 0, -1, a =>
            {
                string title = KeyArg(a, 0, ":title", null) as string, filter = KeyArg(a, 0, ":filter", null) as string;
                bool save = Truthy(KeyArg(a, 0, ":save", null)), multi = Truthy(KeyArg(a, 0, ":multiple", null));
                string dir = Cwd;
                return OnUi(w =>
                {
                    FileDialog d = save ? (FileDialog)new SaveFileDialog() : new OpenFileDialog { Multiselect = multi };
                    using (d)
                    {
                        if (title != null) d.Title = title;
                        d.Filter = DialogFilter(filter);
                        if (dir != null && Directory.Exists(dir)) d.InitialDirectory = dir;
                        if (d.ShowDialog(w) != DialogResult.OK) return null;
                        return multi && !save ? ToList(d.FileNames.Cast<object>().ToList(), null) : d.FileName;
                    }
                });
            });
            Def("choose-folder", 0, -1, a =>
            {
                string title = KeyArg(a, 0, ":title", null) as string, dir = Cwd;
                return OnUi(w =>
                {
                    using (var d = new FolderBrowserDialog())
                    {
                        if (title != null) d.Description = title;
                        if (dir != null && Directory.Exists(dir)) d.SelectedPath = dir;
                        return d.ShowDialog(w) == DialogResult.OK ? d.SelectedPath : null;
                    }
                });
            });
        }

        // "*.csv" → "*.csv|*.csv"; "Testo|*.txt" resta com'è; niente → tutti i file
        static string DialogFilter(string f)
        {
            if (string.IsNullOrEmpty(f)) return L.T("Tutti i file", "All files") + "|*.*";
            return f.Contains("|") ? f : f + "|" + f;
        }

        // Testo per gli appunti: una lista di plist (o di liste) diventa testo separato da tabulazioni,
        // con l'intestazione, così si incolla in Excel; una stringa resta com'è; il resto come lo stampa princ.
        public string ClipboardText(object x)
        {
            var s = x as string;
            if (s != null) return s;
            var c = x as Cons;
            if (c != null && !IsPlist(x))
            {
                var rows = Seq(x);
                Func<object, string> cell = v => v == null ? "" : Printer.Print(v, false).Replace("\t", " ").Replace("\r", "").Replace("\n", " ");
                var sb = new StringBuilder();
                if (rows.All(r => r != null && IsRecord(r)))
                {
                    var cols = new List<Sym>();
                    var seen = new HashSet<Sym>();
                    foreach (var r in rows)
                        foreach (var k in r is Dictionary<object, object> ? ((Dictionary<object, object>)r).Keys.Select(k => k as Sym ?? Intern(":" + Str(k)))
                            : r is LispStruct ? ((LispStruct)r).Type.Slots.Select(sl => Intern(":" + sl.Name))
                            : PlistPairs(r).Select(p => p.Key))
                            if (seen.Add(k)) cols.Add(k);
                    sb.Append(string.Join("\t", cols.Select(k => k.Name.TrimStart(':')))).Append("\r\n");
                    foreach (var r in rows) sb.Append(string.Join("\t", cols.Select(k => cell(GetField(r, k, null))))).Append("\r\n");
                    return sb.ToString();
                }
                if (rows.All(r => r is Cons))
                {
                    foreach (var r in rows) sb.Append(string.Join("\t", Seq(r).Select(cell))).Append("\r\n");
                    return sb.ToString();
                }
                return string.Join("\r\n", rows.Select(cell)) + "\r\n";
            }
            return Printer.Print(x, false).Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        // ---------- HTTP ----------

        // :content-type → "Content-Type"; una stringa resta com'è
        static string HeaderName(object k)
        {
            var s = k as Sym;
            if (s == null) return Printer.Print(k, false);
            return string.Join("-", s.Name.TrimStart(':').Split('-').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p.Substring(1)));
        }

        string Http(string method, string url, object body, object headers, object timeout, bool json = false)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                throw new LispError("http: indirizzo non valido (serve http:// o https://): " + url);
            byte[] data = null;
            string contentType = null;
            if (body != null)
            {
                var bs = body as string;
                if (bs != null) { data = Encoding.UTF8.GetBytes(bs); contentType = "text/plain; charset=utf-8"; }
                else { var sb = new StringBuilder(); JsonWrite(sb, body, -1); data = Encoding.UTF8.GetBytes(sb.ToString()); contentType = "application/json"; }
            }
            var hs = new List<KeyValuePair<string, string>>();
            if (headers != null)
            {
                if (headers is Dictionary<object, object>) foreach (var kv in (Dictionary<object, object>)headers) hs.Add(new KeyValuePair<string, string>(HeaderName(kv.Key), Printer.Print(kv.Value, false)));
                else if (IsPlist(headers)) foreach (var p in PlistPairs(headers)) hs.Add(new KeyValuePair<string, string>(HeaderName(p.Key), Printer.Print(p.Value, false)));
                else throw new LispError("http: :headers va dato come plist, per esempio '(:accept \"text/csv\")");
            }
            int ms = (int)(1000 * (timeout == null ? 30 : Num.Dbl(timeout)));

            string result = null, error = null;
            HttpWebRequest req = null;
            var th = new Thread(() =>
            {
                try
                {
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    req = (HttpWebRequest)WebRequest.Create(uri);
                    req.Method = method;
                    req.Timeout = ms;
                    req.ReadWriteTimeout = ms;
                    req.UserAgent = "CorbolPrompt-Lisp";
                    req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    if (json) req.Accept = "application/json";
                    if (contentType != null) req.ContentType = contentType;
                    foreach (var h in hs)
                    {
                        switch (h.Key.ToLowerInvariant())
                        {
                            case "content-type": req.ContentType = h.Value; break;
                            case "accept": req.Accept = h.Value; break;
                            case "user-agent": req.UserAgent = h.Value; break;
                            case "referer": req.Referer = h.Value; break;
                            default: req.Headers[h.Key] = h.Value; break;
                        }
                    }
                    if (data != null) { req.ContentLength = data.Length; using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length); }
                    using (var resp = (HttpWebResponse)req.GetResponse()) result = ReadBody(resp);
                }
                catch (WebException e)
                {
                    var resp = e.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        string text = "";
                        try { text = ReadBody(resp); } catch { }
                        text = text.Trim();
                        if (text.Length > 200) text = text.Substring(0, 199) + "…";
                        error = (int)resp.StatusCode + " " + resp.StatusDescription + (text.Length > 0 ? ": " + text : "");
                    }
                    else error = e.Status == WebExceptionStatus.Timeout ? L.F("nessuna risposta in {0} s", "no answer within {0} s", FmtNum(ms / 1000.0)) : e.Message;
                }
                catch (Exception e) { error = e.Message; }
            }) { IsBackground = true };
            th.Start();
            while (!th.Join(100))
                if (Interrupt) { try { if (req != null) req.Abort(); } catch { } th.Join(2000); throw new LispInterrupted(); }
            if (error != null) throw new LispError("http: " + error + " (" + method + " " + url + ")");
            return result;
        }

        static string ReadBody(HttpWebResponse resp)
        {
            Encoding enc = Encoding.UTF8;
            try { if (!string.IsNullOrEmpty(resp.CharacterSet) && resp.ContentType != null && resp.ContentType.IndexOf("charset", StringComparison.OrdinalIgnoreCase) >= 0) enc = Encoding.GetEncoding(resp.CharacterSet); } catch { }
            using (var s = resp.GetResponseStream())
            using (var r = new StreamReader(s, enc)) return r.ReadToEnd();
        }

        // ---------- watch ----------

        static readonly Regex Ansi = new Regex("\x1b\\[[0-9;?]*[A-Za-z]");

        // Ripete fn ogni "seconds" e ridisegna sul posto quello che stampa e il risultato (come tabella, se lo è).
        // Ctrl+C la ferma senza errore; times > 0 la ferma dopo tante volte.
        object Watch(double seconds, int times, object fn)
        {
            int prev = 0;
            var oldOut = Out;
            try
            {
                for (int n = 1; ; n++)
                {
                    var start = DateTime.Now;
                    var cap = new StringBuilder();
                    Out = s => cap.Append(s);
                    AtLineStart = true;
                    QuietResult = false;
                    try
                    {
                        var r = Call(fn);
                        if (!AtLineStart) cap.Append('\n');
                        string table = AutoTable(r);
                        if (table != null) cap.Append(table);
                        else if (!(r == null && QuietResult)) cap.Append("\x1b[96m" + Printer.Print(r, true) + "\x1b[0m\n");
                    }
                    catch (LispError e) { if (cap.Length > 0 && cap[cap.Length - 1] != '\n') cap.Append('\n'); cap.Append("\x1b[91m" + L.T("Errore: ", "Error: ") + e.Message + "\x1b[0m\n"); }
                    finally { Out = oldOut; }

                    var lines = cap.ToString().TrimEnd('\n').Split('\n').ToList();
                    int room = TermRows > 3 ? TermRows - 2 : int.MaxValue;
                    if (lines.Count > room)
                    {
                        int more = lines.Count - (room - 1);
                        lines = lines.Take(room - 1).ToList();
                        lines.Add("\x1b[2m(" + L.F("altre {0} righe", "{0} more lines", more) + ")\x1b[0m");
                    }
                    string head = "\x1b[2m" + L.F("ogni {0} s · {1} · Ctrl+C per fermare", "every {0} s · {1} · Ctrl+C to stop",
                        seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), DateTime.Now.ToString("HH:mm:ss")) + "\x1b[0m";
                    lines.Insert(0, head);
                    var frame = new StringBuilder();
                    if (prev > 0) frame.Append("\x1b[" + prev + "F\x1b[J");
                    foreach (var l in lines) frame.Append(l).Append('\n');
                    Out(frame.ToString());
                    AtLineStart = true;
                    prev = lines.Sum(l => { int len = Ansi.Replace(l, "").Length; return TermCols > 0 && len > TermCols ? (len + TermCols - 1) / TermCols : 1; });

                    if (times > 0 && n >= times) break;
                    var until = start.AddSeconds(seconds);
                    do
                    {
                        if (Interrupt) throw new LispInterrupted();
                        Thread.Sleep(Math.Max(1, Math.Min(100, (int)(until - DateTime.Now).TotalMilliseconds)));
                    } while (DateTime.Now < until);
                    if (Interrupt) throw new LispInterrupted();
                }
            }
            catch (LispInterrupted) { Interrupt = false; }
            QuietResult = true;
            return null;
        }
    }
}
