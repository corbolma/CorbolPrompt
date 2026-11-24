// Scheda Lisp: il ciclo leggi-valuta-stampa dell'interprete, con una riga di comando fatta qui
// (cronologia con le frecce, completamento con Tab, più righe finché le parentesi non sono chiuse,
// Ctrl+C interrompe). Scrive nell'emulatore come un programma qualunque, con sequenze VT.
// La valutazione gira in un thread con uno stack grande (ricorsioni profonde), uno per scheda.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CorbolPrompt
{
    class LispSession : ISession
    {
        public event Action<string> Output;
        public event Action<int> Exited;

        readonly Terminal term;
        public readonly Interp Interp = new Interp();
        readonly BlockingCollection<string> jobs = new BlockingCollection<string>();
        Thread worker;

        enum Mode { Editing, Evaluating, Reading }
        volatile Mode mode = Mode.Editing;
        readonly StringBuilder buf = new StringBuilder();
        int pos;
        string pending = "";                  // righe già scritte di una forma non ancora chiusa
        long startLine; int startCol;         // dove comincia il testo scritto (riga assoluta, colonna)
        readonly List<string> history = new List<string>();
        int histIndex = -1;
        string histSaved = "";
        bool lastTab;
        // (read-line) dal programma Lisp
        string readResult;
        readonly AutoResetEvent readDone = new AutoResetEvent(false);
        readonly string historyFile;

        const string Prompt = "\x1b[1;32mλ\x1b[0;32m>\x1b[0m ", PromptCont = "\x1b[90m.. \x1b[0m";

        public LispSession(Terminal term, string cwd)
        {
            this.term = term;
            if (cwd != null && Directory.Exists(cwd)) Interp.Cwd = cwd;
            Interp.Out = s => Emit(s);
            Interp.CwdChanged = d => term.SetDirectory(d);
            Interp.ReadLineHook = ReadLine;
            historyFile = Path.Combine(Settings.Dir, "lisp-history.txt");
            try { if (File.Exists(historyFile)) history.AddRange(File.ReadAllLines(historyFile, Encoding.UTF8).Select(l => l.Replace("\\n", "\n"))); } catch { }
        }

        public void Start(int cols, int rows)
        {
            term.SetDirectory(Interp.Cwd);
            worker = new Thread(WorkLoop, 256 * 1024 * 1024) { IsBackground = true, Name = "lisp" };
            worker.Start();
            Emit("\x1b[1mCorbolPrompt Lisp\x1b[0m  " + L.T("· (help) per l'aiuto · (exit) per chiudere", "· (help) for help · (exit) to close") + "\n");
            // init.lisp dell'utente, se c'è
            string init = Path.Combine(Settings.Dir, "init.lisp");
            if (File.Exists(init)) { mode = Mode.Evaluating; jobs.Add("(load \"" + init.Replace("\\", "\\\\") + "\")"); }
            else ShowPrompt();
        }

        // ---------- uscita ----------

        void Emit(string s)
        {
            if (string.IsNullOrEmpty(s) || Output == null) return;
            Output(s.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }

        // code >= 0: è appena finita una valutazione con quel codice (0 = ok, 1 = errore), segnata come in una shell (OSC 133 D)
        void ShowPrompt(int code = -1)
        {
            if (pending.Length > 0) Emit(PromptCont);
            else Emit((code >= 0 ? "\x1b]133;D;" + code + "\x07" : "") + "\x1b]133;A\x07" + Prompt + "\x1b]133;B\x07");
            MarkStart();
        }

        void MarkStart()
        {
            lock (term.Sync)
            {
                startLine = term.Dropped + term.ScreenTop + term.CurY;
                startCol = term.CurX;
            }
        }

        // ---------- valutazione ----------

        void WorkLoop()
        {
            foreach (var src in jobs.GetConsumingEnumerable())
            {
                Interp.Interrupt = false;
                Interp.ResetDepth();
                Interp.AtLineStart = true;
                Interp.QuietResult = false;
                lock (term.Sync) { Interp.TermCols = term.Cols; Interp.TermRows = term.Rows; }
                bool exit = false;
                bool init = src != null && src.StartsWith("(load \"");
                int code = init ? -1 : 1;   // errore, salvo che la valutazione finisca bene
                try
                {
                    var result = Interp.EvalString(src);
                    Interp.FreshLine();
                    if (!init)
                    {
                        Interp.Intern("*3").Value = Interp.Intern("*2").Value;
                        Interp.Intern("*2").Value = Interp.Intern("*1").Value;
                        Interp.Intern("*1").Value = result;
                        // liste di plist (per esempio da ps) come tabella; i dati restano in *1
                        string table = Interp.AutoTable(result);
                        if (table != null) Emit(table);
                        else if (!(result == null && Interp.QuietResult)) Emit("\x1b[96m" + Printer.Print(result, true) + "\x1b[0m\n");
                        code = 0;
                    }
                }
                catch (LispExit) { exit = true; }
                catch (LispInterrupted) { Interp.FreshLine(); Emit("\x1b[33m; " + L.T("interrotto", "interrupted") + "\x1b[0m\n"); code = init ? -1 : 130; }
                catch (LispError ex) { Interp.LastErrorSource = src; Interp.LastError = ex.Message; Interp.FreshLine(); Emit("\x1b[91m" + L.T("Errore: ", "Error: ") + ex.Message + "\x1b[0m\n"); }
                catch (ReturnFrom) { Interp.FreshLine(); Emit("\x1b[91m" + L.T("Errore: return fuori da un blocco", "Error: return outside a block") + "\x1b[0m\n"); }
                catch (ThrowTag) { Interp.FreshLine(); Emit("\x1b[91m" + L.T("Errore: throw senza catch", "Error: throw without catch") + "\x1b[0m\n"); }
                catch (InvalidCastException) { Interp.LastErrorSource = src; Interp.LastError = "value of the wrong type (for example a number where a list or a symbol is needed)"; Interp.FreshLine(); Emit("\x1b[91m" + L.T("Errore: valore del tipo sbagliato (per esempio un numero dove serve una lista o un simbolo)", "Error: value of the wrong type (for example a number where a list or a symbol is needed)") + "\x1b[0m\n"); }
                catch (Exception ex) { Interp.LastErrorSource = src; Interp.LastError = ex.Message; Interp.FreshLine(); Emit("\x1b[91m" + L.T("Errore: ", "Error: ") + ex.Message + "\x1b[0m\n"); }
                if (exit) { if (Exited != null) Exited(0); return; }
                lock (edLock)
                {
                    mode = Mode.Editing;
                    ShowPrompt(code);
                    if (typeAhead.Length > 0)
                    {
                        string s = typeAhead.ToString();
                        typeAhead.Clear();
                        SendLocked(s);
                    }
                }
            }
        }

        string ReadLine()
        {
            readResult = null;
            Interp.FreshLine();
            lock (edLock)
            {
                buf.Clear(); pos = 0;
                MarkStart();
                mode = Mode.Reading;
                if (typeAhead.Length > 0)
                {
                    string s = typeAhead.ToString();
                    typeAhead.Clear();
                    SendLocked(s);
                }
            }
            while (!readDone.WaitOne(100))
                if (Interp.Interrupt) { mode = Mode.Evaluating; return null; }
            return readResult;
        }

        // ---------- tastiera ----------

        // Tasti battuti mentre il Lisp valuta: si tengono e si usano appena torna il prompt (incolla di più forme,
        // blocchi dell'assistente con più forme)
        readonly object edLock = new object();
        readonly StringBuilder typeAhead = new StringBuilder();

        public void Send(string keys)
        {
            lock (edLock) SendLocked(keys);
        }

        void SendLocked(string keys)
        {
            int i = 0;
            while (i < keys.Length)
            {
                if (mode == Mode.Evaluating)
                {
                    string rest = keys.Substring(i);
                    if (rest.IndexOf('\x03') >= 0) { typeAhead.Clear(); Interp.Interrupt = true; }
                    else typeAhead.Append(rest);
                    return;
                }
                char c = keys[i++];
                if (c == '\x1b')
                {
                    // CSI o SS3: frecce, Home, End, Canc...
                    if (i < keys.Length && (keys[i] == '[' || keys[i] == 'O'))
                    {
                        int j = i + 1;
                        while (j < keys.Length && !(keys[j] >= 0x40 && keys[j] <= 0x7e)) j++;
                        if (j >= keys.Length) return;
                        string seq = keys.Substring(i + 1, j - i);
                        i = j + 1;
                        Key(seq);
                    }
                    else if (i < keys.Length) { Alt(keys[i]); i++; }
                    continue;
                }
                Char(c);
            }
        }

        void Key(string seq)
        {
            if (seq == "200~" || seq == "201~") return;   // incolla con le parentesi: si ignora il marcatore
            if (mode == Mode.Evaluating) return;
            char k = seq[seq.Length - 1];
            bool ctrl = seq.Contains(";5");
            switch (k)
            {
                case 'A': if (mode == Mode.Editing) History(-1); return;
                case 'B': if (mode == Mode.Editing) History(1); return;
                case 'C': if (ctrl) WordRight(); else if (pos < buf.Length) pos++; Render(); return;
                case 'D': if (ctrl) WordLeft(); else if (pos > 0) pos--; Render(); return;
                case 'H': pos = 0; Render(); return;
                case 'F': pos = buf.Length; Render(); return;
                case '~':
                    if (seq.StartsWith("3")) { if (pos < buf.Length) { buf.Remove(pos, 1); Render(); } }
                    else if (seq.StartsWith("1") || seq.StartsWith("7")) { pos = 0; Render(); }
                    else if (seq.StartsWith("4") || seq.StartsWith("8")) { pos = buf.Length; Render(); }
                    return;
            }
        }

        void Alt(char c)
        {
            if (mode == Mode.Evaluating) return;
            if (c == 'b') { WordLeft(); Render(); }
            else if (c == 'f') { WordRight(); Render(); }
            else if (c == '\x7f') { DeleteWordBack(); }
        }

        void Char(char c)
        {
            if (c == '\x03')   // Ctrl+C
            {
                if (mode == Mode.Evaluating) { Interp.Interrupt = true; return; }
                if (mode == Mode.Reading) { Interp.Interrupt = true; return; }
                pos = buf.Length; Render();
                Emit("\x1b[90m^C\x1b[0m\n");
                buf.Clear(); pos = 0; pending = ""; histIndex = -1;
                ShowPrompt();
                return;
            }
            if (mode == Mode.Evaluating) return;
            lastTab = c == '\t' && lastTab;
            switch (c)
            {
                case '\r': Enter(); return;
                case '\n': return;
                case '\x7f': if (pos > 0) { buf.Remove(--pos, 1); Render(); } return;
                case '\b': case '\x17': DeleteWordBack(); return;
                case '\x01': pos = 0; Render(); return;
                case '\x05': pos = buf.Length; Render(); return;
                case '\x02': if (pos > 0) pos--; Render(); return;
                case '\x06': if (pos < buf.Length) pos++; Render(); return;
                case '\x0b': buf.Length = pos; Render(); return;
                case '\x15': buf.Remove(0, pos); pos = 0; Render(); return;
                case '\x0c':   // Ctrl+L: pulisce lo schermo e riscrive la riga
                    Emit("\x1b[2J\x1b[3J\x1b[H");
                    Emit(mode == Mode.Reading ? "" : pending.Length > 0 ? PromptCont : Prompt);
                    MarkStart();
                    Render();
                    return;
                case '\x04': if (buf.Length == 0 && pending.Length == 0 && mode == Mode.Editing) { Emit("\n"); if (Exited != null) Exited(0); } return;
                case '\t': if (mode == Mode.Editing) Complete(); return;
            }
            if (c < ' ') return;
            buf.Insert(pos++, c);
            Render();
        }

        void WordLeft()
        {
            while (pos > 0 && !IsSymChar(buf[pos - 1])) pos--;
            while (pos > 0 && IsSymChar(buf[pos - 1])) pos--;
        }

        void WordRight()
        {
            while (pos < buf.Length && !IsSymChar(buf[pos])) pos++;
            while (pos < buf.Length && IsSymChar(buf[pos])) pos++;
        }

        void DeleteWordBack()
        {
            int end = pos;
            WordLeft();
            buf.Remove(pos, end - pos);
            Render();
        }

        static bool IsSymChar(char c) { return !char.IsWhiteSpace(c) && "()[]\"';`,".IndexOf(c) < 0; }

        void History(int dir)
        {
            if (history.Count == 0) return;
            if (histIndex < 0) { if (dir > 0) return; histSaved = buf.ToString(); histIndex = history.Count; }
            histIndex += dir;
            if (histIndex < 0) histIndex = 0;
            string text;
            if (histIndex >= history.Count) { histIndex = -1; text = histSaved; }
            else text = history[histIndex];
            // una voce su più righe si mette su una riga sola
            buf.Clear(); buf.Append(text.Replace("\r", "").Replace("\n", " "));
            pos = buf.Length;
            Render();
        }

        void Enter()
        {
            if (mode == Mode.Reading)
            {
                pos = buf.Length; Render();
                Emit("\n");
                readResult = buf.ToString();
                buf.Clear(); pos = 0;
                mode = Mode.Evaluating;
                Interp.AtLineStart = true;
                readDone.Set();
                return;
            }
            string full = pending + buf.ToString();
            bool complete = true;
            try
            {
                var r = new Reader(Interp, full);
                while (r.Read() != Reader.Eof) { }
            }
            catch (LispIncomplete) { complete = false; }
            catch { }
            pos = buf.Length; Render();
            Emit("\n");
            buf.Clear(); pos = 0; histIndex = -1;
            if (!complete)
            {
                pending = full + "\n";
                ShowPrompt();
                return;
            }
            pending = "";
            if (full.Trim().Length == 0) { ShowPrompt(); return; }
            if (history.Count == 0 || history[history.Count - 1] != full.Trim())
            {
                history.Add(full.Trim());
                SaveHistory();
            }
            mode = Mode.Evaluating;
            jobs.Add(full);
        }

        void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllLines(historyFile, history.Skip(Math.Max(0, history.Count - 500)).Select(h => h.Replace("\r", "").Replace("\n", "\\n")), new UTF8Encoding(false));
            }
            catch { }
        }

        // Tab: completa il simbolo prima del cursore; se ci sono più possibilità le elenca
        void Complete()
        {
            int start = pos;
            while (start > 0 && IsSymChar(buf[start - 1])) start--;
            string prefix = buf.ToString(start, pos - start).ToLowerInvariant();
            if (prefix.Length == 0) return;
            var names = Interp.AllSymbols().Where(s => s.Name.StartsWith(prefix) && (s.Function != null || s.Value != Sym.Unbound))
                              .Select(s => s.Name).Concat(SpecialForms.Where(n => n.StartsWith(prefix))).Distinct().OrderBy(n => n).ToList();
            if (names.Count == 0) return;
            string common = names[0];
            foreach (var n in names) { int k = 0; while (k < common.Length && k < n.Length && common[k] == n[k]) k++; common = common.Substring(0, k); }
            if (common.Length > prefix.Length || names.Count == 1)
            {
                string add = common.Substring(prefix.Length) + (names.Count == 1 ? " " : "");
                buf.Insert(pos, add); pos += add.Length;
                Render();
                lastTab = false;
                return;
            }
            if (!lastTab) { lastTab = true; return; }
            // secondo Tab: elenco sotto la riga, poi si riscrive la riga
            int p = pos;
            pos = buf.Length; Render();
            Emit("\n\x1b[90m" + string.Join("  ", names.Take(200)) + (names.Count > 200 ? "  ..." : "") + "\x1b[0m\n");
            Emit(pending.Length > 0 ? PromptCont : Prompt);
            MarkStart();
            pos = p;
            Render();
            lastTab = false;
        }

        static readonly string[] SpecialForms =
        {
            "quote", "if", "progn", "let", "let*", "flet", "labels", "cond", "case", "ecase", "and", "or", "when", "unless", "lambda", "function",
            "setq", "setf", "incf", "decf", "push", "pushnew", "pop", "defun", "defmacro", "defvar", "defparameter", "defconstant", "defstruct",
            "block", "return-from", "return", "catch", "throw", "unwind-protect", "ignore-errors", "handler-case", "dolist", "dotimes", "do",
            "loop", "prog1", "time", "multiple-value-bind", "destructuring-bind", "progv", "declaim"
        };

        // ---------- disegno della riga ----------

        // Riscrive il testo dall'inizio della riga di comando e mette il cursore al suo posto
        void Render()
        {
            var sb = new StringBuilder();
            long curLine; int cols;
            lock (term.Sync)
            {
                curLine = term.Dropped + term.ScreenTop + term.CurY;
                cols = term.Cols;
            }
            long up = curLine - startLine;
            if (up > 0) sb.Append("\x1b[").Append(Math.Min(up, 10000)).Append('A');
            sb.Append('\r');
            if (startCol > 0) sb.Append("\x1b[").Append(startCol).Append('C');
            sb.Append("\x1b[J");
            sb.Append(Highlight(buf.ToString()));
            if (Output != null) Output(sb.ToString());

            // posizione del cursore: colonne occupate dal testo prima di pos
            int width = 0;
            for (int i = 0; i < pos; i++) width += Terminal.IsWide(buf[i]) ? 2 : 1;
            int total = 0;
            for (int i = 0; i < buf.Length; i++) total += Terminal.IsWide(buf[i]) ? 2 : 1;
            long off = startCol + width, endOff = startCol + total;
            long targetLine = startLine + off / cols;
            int targetCol = (int)(off % cols);
            // il testo finisce proprio a fine riga: il cursore va all'inizio della riga sotto, che va creata
            if (off == endOff && endOff > 0 && endOff % cols == 0) Output("\r\n");
            long top;
            lock (term.Sync) top = term.Dropped + term.ScreenTop;
            long row = targetLine - top;
            if (row < 0) row = 0;
            Output("\x1b[" + (row + 1) + ";" + (targetCol + 1) + "H");
        }

        // Colori: stringhe, commenti, keyword
        static string Highlight(string s)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    int j = i + 1;
                    while (j < s.Length && s[j] != '"') { if (s[j] == '\\') j++; j++; }
                    j = Math.Min(s.Length, j + 1);
                    sb.Append("\x1b[33m").Append(s, i, j - i).Append("\x1b[0m");
                    i = j;
                }
                else if (c == ';')
                {
                    sb.Append("\x1b[90m").Append(s.Substring(i)).Append("\x1b[0m");
                    break;
                }
                else if (c == ':' && (i == 0 || !IsSymChar(s[i - 1])))
                {
                    int j = i + 1;
                    while (j < s.Length && IsSymChar(s[j])) j++;
                    sb.Append("\x1b[36m").Append(s, i, j - i).Append("\x1b[0m");
                    i = j;
                }
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        public void Resize(int cols, int rows) { }

        public void Close()
        {
            Interp.Interrupt = true;
            readDone.Set();
            try { jobs.CompleteAdding(); } catch { }
        }
    }
}
