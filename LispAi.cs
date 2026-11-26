// L'IA dentro il Lisp di CorbolPrompt, con il servizio scelto nelle impostazioni dell'assistente:
//   (ask "domanda" [dato])                  risposta in testo
//   (ask-data "richiesta" [dato] :fields (:a :b))   risposta come dati Lisp (JSON letto con json-parse)
//   (ai-defun nome (args) "cosa fa" :examples ((nome 2 3) 5 ...))
//        il modello scrive la defun, il Lisp la legge, la prova sugli esempi e riprova con l'errore
//   (ai-source 'nome)                        il testo della funzione scritta dal modello
//   (why)                                    spiega l'ultimo errore del REPL
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace CorbolPrompt
{
    partial class Interp
    {
        // Per i test: un modello finto che riceve la richiesta e restituisce il testo della risposta
        public Func<AiRequest, string> AiHook;

        // L'ultimo errore del REPL, per (why)
        public string LastErrorSource, LastError;

        readonly Dictionary<Sym, string> aiSources = new Dictionary<Sym, string>();

        // Funzioni che il codice scritto da ai-defun non deve usare se lo si prova da solo sugli esempi
        static readonly HashSet<string> systemFns = new HashSet<string>
        {
            "sh", "pwsh", "run", "run-lines", "ps", "cd", "delete-file", "write-file", "append-file", "with-open-file", "open",
            "mkdir", "setenv", "load", "eval", "exit", "quit", "intern", "read-from-string", "symbol-function", "fdefinition",
            "ask", "ask-data", "ai-defun", "why",
            "clipboard", "set-clipboard", "notify", "http-get", "http-post", "http-json", "watch", "watch-fn",
            "choose-file", "choose-folder"
        };

        const int AiDefunAttempts = 3;

        void AiBuiltins()
        {
            Def("ask", 1, -1, a =>
            {
                var req = new AiRequest { System = "You are called from a CorbolPrompt Lisp program running on Windows; the answer goes back to the program as a string. Answer briefly, in plain text without markdown, in the language of the question." };
                req.Messages.Add(new AiMessage("user", Str(a[0]) + DataText(a, 1)));
                return AiText(req).Trim();
            });

            Def("ask-data", 1, -1, a =>
            {
                bool hasData = a.Length > 1 && !(a[1] is Sym && ((Sym)a[1]).IsKeyword);
                var fields = KeyArg(a, hasData ? 2 : 1, ":fields", null);   // il dato, se c'è, sta prima di :fields
                var keys = fields == null ? new List<string>() : ListItems(fields).Select(f => f is Sym ? ((Sym)f).Name.TrimStart(':') : Str(f)).ToList();
                var sys = new StringBuilder("You are called from a CorbolPrompt Lisp program; your reply is parsed as JSON. Reply with JSON only: no prose, no markdown, no code fences.");
                if (keys.Count > 0) sys.Append(" Reply with a JSON array of objects, each with exactly these keys: " + string.Join(", ", keys) + ". Use numbers for numeric values and null when a value is unknown.");
                var req = new AiRequest { System = sys.ToString() };
                req.Messages.Add(new AiMessage("user", Str(a[0]) + DataText(a, 1)));
                for (int attempt = 0; ; attempt++)
                {
                    string text = AiText(req);
                    try
                    {
                        object r = new JsonReader(this, JsonPart(text)).ReadAll();
                        // {"items": [...]} quando si voleva un elenco: si prende l'elenco
                        if (keys.Count > 0 && IsPlist(r) && PlistPairs(r).Count == 1 && PlistPairs(r)[0].Value is Cons && !IsPlist(PlistPairs(r)[0].Value))
                            r = PlistPairs(r)[0].Value;
                        if (keys.Count > 0 && IsPlist(r)) r = new Cons(r, null);
                        return r;
                    }
                    catch (LispError ex)
                    {
                        if (attempt >= 1) throw new LispError("ask-data: la risposta non è JSON valido (" + ex.Message + "): " + Shorten(text, 200));
                        req.Messages.Add(new AiMessage("assistant", text));
                        req.Messages.Add(new AiMessage("user", "That is not valid JSON (" + ex.Message + "). Reply again with JSON only."));
                    }
                }
            });

            // (ai-defun nome (args) "descrizione" :examples ((nome 1 2) 3 ...)): macro, gli argomenti non si valutano
            Def("ai-defun-fn", 3, -1, a => AiDefun(a[0] as Sym, a[1], Str(a[2]), KeyArg(a, 3, ":examples", null)));
            EvalString("(defmacro ai-defun (name args description &rest options) `(ai-defun-fn ',name ',args ,description ,@(mapcar (lambda (o) (list 'quote o)) options)))");

            Def("ai-source", 1, 1, a =>
            {
                var s = a[0] as Sym;
                string src;
                return s != null && aiSources.TryGetValue(s, out src) ? src : null;
            });

            Def("why", 0, 0, a =>
            {
                FreshLine();
                if (LastError == null) { Write(L.T("Nessun errore da spiegare.", "No error to explain.") + "\n"); QuietResult = true; return null; }
                var req = new AiRequest { System = LispDialect() + "\n\nThe user ran some code in the CorbolPrompt Lisp REPL and got an error. Explain the cause in one to three sentences and show the corrected code in one ```lisp block. Be brief, plain text, reply in " + LanguageName() + "." };
                req.Messages.Add(new AiMessage("user", "Code:\n" + LastErrorSource + "\n\nError: " + LastError));
                Write(AiText(req).Trim() + "\n");
                QuietResult = true;
                return null;
            });
        }

        // ---------- ai-defun ----------

        object AiDefun(Sym name, object args, string description, object examples)
        {
            if (name == null || name.IsKeyword) throw new LispError("ai-defun: serve il nome della funzione");
            var ex = ListItems(examples);
            if (ex.Count % 2 != 0) throw new LispError("ai-defun: gli esempi vanno a coppie: (chiamata risultato-atteso ...)");
            var pairs = new List<KeyValuePair<object, object>>();
            for (int i = 0; i < ex.Count; i += 2) pairs.Add(new KeyValuePair<object, object>(ex[i], ex[i + 1]));

            string head = "(defun " + name.Name + " " + (args == null ? "()" : Printer.Print(args, true)) + " ...)";
            var user = new StringBuilder("Write " + head + " that does this: " + description + "\n");
            if (pairs.Count > 0)
            {
                user.Append("Examples (call => expected result):\n");
                foreach (var p in pairs) user.Append(Printer.Print(p.Key, true) + " => " + Printer.Print(p.Value, true) + "\n");
            }
            var req = new AiRequest
            {
                System = LispDialect() + "\n\nYou write one function for this dialect. Reply with only one ```lisp block that contains exactly one (defun ...) form and nothing else; " +
                         "helper functions go inside it with labels or flet. Use only what the dialect supports. Do not call system functions (sh, pwsh, run, ps, file functions) unless the description asks for it."
            };
            req.Messages.Add(new AiMessage("user", user.ToString()));

            var previous = name.Function;
            try { return AiDefunAttemptLoop(name, req, pairs); }
            catch (LispInterrupted) { name.Function = previous; throw; }
            catch (LispError) { name.Function = previous; throw; }   // niente funzione a metà
        }

        object AiDefunAttemptLoop(Sym name, AiRequest req, List<KeyValuePair<object, object>> pairs)
        {
            string lastProblem = null, code = null;
            for (int attempt = 1; attempt <= AiDefunAttempts; attempt++)
            {
                string reply = AiText(req);
                code = LispBlock(reply);
                string problem = null;
                object form = null;
                try
                {
                    var forms = new List<object>();
                    var rd = new Reader(this, code);
                    for (var f = rd.Read(); f != Reader.Eof; f = rd.Read()) forms.Add(f);
                    var c = forms.Count == 1 ? forms[0] as Cons : null;
                    if (c == null || c.Car != Intern("defun") || !(c.Cdr is Cons) || ((Cons)c.Cdr).Car != name)
                        problem = "The block must contain exactly one form, (defun " + name.Name + " ...), and nothing else.";
                    else form = c;
                }
                catch (LispIncomplete) { problem = "The code is incomplete: the parentheses are not balanced."; }
                catch (LispError e) { problem = "The code cannot be read: " + e.Message; }

                bool untested = false;
                if (problem == null)
                {
                    Eval(form, Global);   // definisce soltanto: il corpo non viene eseguito
                    var sys = UsedSymbols(form).Where(s => systemFns.Contains(s.Name)).Select(s => s.Name).Distinct().ToList();
                    if (RepeatingEvery(form)) sys.Add("every");
                    if (sys.Count > 0 && pairs.Count > 0) untested = true;
                    else problem = RunExamples(pairs);
                    if (problem == null)
                    {
                        aiSources[name] = code;
                        FreshLine();
                        Write("\x1b[36m" + code.TrimEnd() + "\x1b[0m\n");
                        if (untested)
                            Write("\x1b[33m; " + L.F("usa {0}: non l'ho provata sugli esempi, controllala prima di usarla", "it uses {0}: not tried on the examples, check it before using it", string.Join(" ", sys)) + "\x1b[0m\n");
                        else if (pairs.Count > 0)
                            Write("\x1b[2m; " + L.F("provata su {0} esempi", "tried on {0} examples", pairs.Count) + (attempt > 1 ? " (" + L.F("tentativo {0}", "attempt {0}", attempt) + ")" : "") + "\x1b[0m\n");
                        return name;
                    }
                }
                lastProblem = problem;
                req.Messages.Add(new AiMessage("assistant", reply));
                req.Messages.Add(new AiMessage("user", problem + "\nReply again with the whole corrected function in one ```lisp block."));
            }
            FreshLine();
            if (code != null) Write("\x1b[2m" + code.TrimEnd() + "\x1b[0m\n");
            throw new LispError("ai-defun: dopo " + AiDefunAttempts + " tentativi la funzione non va: " + lastProblem);
        }

        // null se tutti gli esempi danno il risultato atteso, altrimenti il problema (in inglese, per il modello)
        string RunExamples(List<KeyValuePair<object, object>> pairs)
        {
            foreach (var p in pairs)
            {
                string call = Printer.Print(p.Key, true);
                object expected, got;
                try { expected = Eval(p.Value, Global); }
                catch (LispError e) { throw new LispError("ai-defun: il risultato atteso " + Printer.Print(p.Value, true) + " dà errore: " + e.Message); }
                try { got = Eval(p.Key, Global); }
                catch (LispError e) { ResetDepth(); return call + " signals an error: " + e.Message; }
                catch (InvalidCastException) { ResetDepth(); return call + " signals an error: a value of the wrong type (for example a number where a list is needed)."; }
                bool same = IsEqual(got, expected) || (Num.Is(got) && Num.Is(expected) && Num.Compare(got, expected) == 0);
                if (!same) return call + " returns " + Printer.Print(got, true) + ", but it should return " + Printer.Print(expected, true) + ".";
            }
            return null;
        }

        // (every secondi ...) ripete per sempre; (every #'pred lista) è la every di Common Lisp.
        // Se il primo argomento non è chiaramente una funzione si considera la forma che ripete.
        static bool RepeatingEvery(object x)
        {
            var c = x as Cons;
            if (c == null) return false;
            var head = c.Car as Sym;
            if (head != null && head.Name == "every")
            {
                var arg = c.Cdr is Cons ? ((Cons)c.Cdr).Car : null;
                var ac = arg as Cons;
                var argHead = ac != null ? ac.Car as Sym : null;
                bool isFn = (argHead != null && (argHead.Name == "function" || argHead.Name == "lambda"))
                            || (arg is Sym && ((Sym)arg).IsKeyword);
                if (!isFn) return true;
            }
            return RepeatingEvery(c.Car) || RepeatingEvery(c.Cdr);
        }

        static IEnumerable<Sym> UsedSymbols(object x)
        {
            var c = x as Cons;
            if (c != null) return UsedSymbols(c.Car).Concat(UsedSymbols(c.Cdr));
            var s = x as Sym;
            return s != null ? new[] { s } : Enumerable.Empty<Sym>();
        }

        // ---------- chiamata al modello ----------

        string AiText(AiRequest req)
        {
            if (AiHook != null) return AiHook(req) ?? "";
            string missing;
            var provider = AiSettings.Create(out missing);
            if (provider == null)
                throw new LispError("IA non configurata: " + missing + " " + L.T("Configurala dal menu ⋯ → Impostazioni dell'assistente.", "Set it up from the ⋯ menu → Assistant settings."));
            var cts = new CancellationTokenSource();
            AiResult res = null;
            var th = new Thread(() =>
            {
                try { res = provider.Run(req, cts.Token); }
                catch (Exception e) { res = new AiResult { Error = e.Message }; }
            }) { IsBackground = true };
            bool waiting = AtLineStart;
            if (waiting) Write("\x1b[2m✦ " + provider.Name + "…\x1b[0m");
            th.Start();
            try
            {
                while (!th.Join(100))
                {
                    if (Interrupt) { cts.Cancel(); th.Join(3000); throw new LispInterrupted(); }
                }
            }
            finally
            {
                if (waiting) { Write("\r\x1b[K"); AtLineStart = true; }
            }
            if (res.Cancelled) throw new LispInterrupted();
            if (res.Error != null) throw new LispError("IA: " + res.Error);
            return res.Text ?? "";
        }

        // Il dato passato a ask o ask-data, in JSON (o il testo così com'è)
        string DataText(object[] a, int from)
        {
            if (a.Length <= from || (a[from] is Sym && ((Sym)a[from]).IsKeyword)) return "";
            var d = a[from];
            string text;
            if (d is string) text = (string)d;
            else { var sb = new StringBuilder(); JsonWrite(sb, d, -1); text = sb.ToString(); }
            return "\n\nData:\n" + text;
        }

        // La parte che riguarda il Lisp nelle istruzioni dell'assistente: il dialetto esatto
        static string LispDialect()
        {
            string s = Prompt.System;
            int i = s.IndexOf("CORBOLPROMPT LISP", StringComparison.Ordinal);
            return i >= 0 ? s.Substring(i) : s;
        }

        static string LanguageName()
        {
            try { return CultureInfo.GetCultureInfo(L.Code).EnglishName; } catch { return "English"; }
        }

        // Il codice di un blocco ```lisp (o del primo blocco ```), altrimenti tutta la risposta
        static string LispBlock(string text)
        {
            int start = text.IndexOf("```", StringComparison.Ordinal);
            if (start < 0) return text.Trim();
            int nl = text.IndexOf('\n', start);
            if (nl < 0) return text.Trim();
            int end = text.IndexOf("```", nl, StringComparison.Ordinal);
            return (end < 0 ? text.Substring(nl + 1) : text.Substring(nl + 1, end - nl - 1)).Trim() + "\n";
        }

        // Il JSON in una risposta: senza recinti ``` e senza testo prima o dopo
        static string JsonPart(string text)
        {
            string t = text.Trim();
            if (t.StartsWith("```")) { int nl = t.IndexOf('\n'); int end = t.LastIndexOf("```", StringComparison.Ordinal); if (nl > 0 && end > nl) t = t.Substring(nl + 1, end - nl - 1).Trim(); }
            int a = t.IndexOfAny(new[] { '[', '{' });
            if (a > 0)
            {
                char close = t[a] == '[' ? ']' : '}';
                int b = t.LastIndexOf(close);
                if (b > a) t = t.Substring(a, b - a + 1);
            }
            return t;
        }

        static string Shorten(string s, int max) { s = s.Replace("\r", "").Replace('\n', ' ').Trim(); return s.Length > max ? s.Substring(0, max - 1) + "…" : s; }
    }
}
