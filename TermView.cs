// Disegno del terminale e tastiera: mostra lo schermo dell'emulatore (Terminal) con la cronologia scorrevole,
// traduce i tasti in sequenze VT per la sessione, selezione col mouse, copia e incolla.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CorbolPrompt
{
    // Un indirizzo web o un percorso trovato nel testo dello schermo (da… a… in righe assolute e colonne, estremi inclusi)
    class TermLink
    {
        public string Target;
        public bool IsUrl;
        public long L1, L2;
        public int C1, C2;
        public bool IsDir { get { return !IsUrl && Directory.Exists(Target); } }
    }

    class TermView : Control
    {
        public readonly Terminal Term;
        public ISession Session;
        public event Action Changed;          // nuovo output (titolo, cartella...): sul thread dell'interfaccia
        public event Action<int> ZoomRequested;
        public event Action<string, bool> UserInput;     // testo scritto o incollato (true) dall'utente: per "scrivi in tutte"
        public event Action<CmdMark> ExplainRequested;   // clic su un comando fallito o sul pulsante "Spiega l'errore"
        public event Action SearchChanged;               // risultati della ricerca cambiati (numero, posizione)
        public event Action<CmdMark> CommandDone;        // un comando è finito (sul thread dell'interfaccia)
        public string ShellId = "";                      // per scrivere i percorsi trascinati nel formato della shell
        DateTime lastEnter = DateTime.MinValue;

        // colori "Campbell" di Windows Terminal
        public static readonly Color Background = Color.FromArgb(12, 12, 12), Foreground = Color.FromArgb(204, 204, 204),
            CursorColor = Color.FromArgb(255, 255, 255), SelectionColor = Color.FromArgb(38, 79, 120);
        static readonly Color[] Palette = BuildPalette();

        // barra di scorrimento disegnata qui, scura come il terminale (quella di Windows sul nero quasi non si vede)
        int sbW;
        bool sbHover, sbDrag;
        int sbGrab;   // distanza del mouse dall'inizio del cursore della barra durante il trascinamento
        Font font, fontB, fontI, fontBI;
        int cw = 8, ch = 16, pad = 6;
        long viewTop;          // prima riga mostrata (indice assoluto = riga del documento + righe eliminate)
        bool follow = true;    // segue l'ultima riga
        int pending;
        readonly System.Windows.Forms.Timer blink = new System.Windows.Forms.Timer { Interval = 530 };
        bool blinkOn = true;
        // selezione (indici assoluti di riga, colonne)
        bool hasSel, selecting;
        long selL1, selL2; int selC1, selC2;
        float scale = 1f;

        // La rotellina di Windows va al controllo col focus, non a quello sotto il mouse: con le schede affiancate
        // scorreva solo il terminale attivo, e con il focus nel terminale la conversazione dell'assistente non scorreva
        // (e viceversa). Questo filtro la manda sempre a quello che sta sotto il puntatore, in tutta la finestra.
        sealed class WheelFilter : IMessageFilter
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
            [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != 0x020A && m.Msg != 0x020E) return false;   // WM_MOUSEWHEEL, WM_MOUSEHWHEEL
                long lp = (long)m.LParam;
                var pt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                IntPtr target = WindowFromPoint(pt);
                if (target == IntPtr.Zero || target == m.HWnd) return false;
                // solo finestre di questa app (la finestra sotto il mouse può essere un controllo interno senza Control:
                // FromChildHandle risale al primo che c'è), e non sotto una finestra di dialogo aperta sopra
                var c = Control.FromChildHandle(target);
                if (c == null || !c.Visible) return false;
                var f = c.FindForm();
                if (f == null || Form.ActiveForm != f) return false;
                SendMessage(target, m.Msg, m.WParam, m.LParam);   // i controlli che non scorrono la passano al contenitore
                return true;
            }
        }
        static bool wheelFilterOn;

        public TermView(Terminal term, float fontSize)
        {
            if (!wheelFilterOn) { wheelFilterOn = true; Application.AddMessageFilter(new WheelFilter()); }
            Term = term;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Background;
            Cursor = Cursors.IBeam;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            pad = (int)Math.Round(6 * scale);
            sbW = (int)Math.Round(12 * scale);
            SetFontSize(fontSize);
            blink.Tick += delegate { blinkOn = !blinkOn; InvalidateCursor(); };
            blink.Start();
            // pulsante che compare quando l'ultimo comando è fallito
            failBtn = new Button
            {
                Visible = false, FlatStyle = FlatStyle.Flat, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.FromArgb(120, 28, 36), ForeColor = Color.White, Font = new Font("Segoe UI", 9.5f),
                Cursor = Cursors.Hand, TabStop = false, UseMnemonic = false, Padding = new Padding(4, 1, 4, 1)
            };
            failBtn.FlatAppearance.BorderColor = Color.FromArgb(200, 70, 80);
            failBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 40, 50);
            failBtn.Click += delegate { var m = failMark; HideFail(); Focus(); if (m != null && ExplainRequested != null) ExplainRequested(m); };
            failBtn.SizeChanged += delegate { PlaceFailButton(); };
            Controls.Add(failBtn);
            term.CommandFinished += m => { if (m.Started == DateTime.MinValue) m.Started = lastEnter; finished = m; };
            // file trascinati sul terminale: si scrive il percorso
            AllowDrop = true;
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.UnicodeText)) e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) DropFiles(files);
            else
            {
                var text = e.Data.GetData(DataFormats.UnicodeText) as string;
                if (!string.IsNullOrEmpty(text)) Paste(text);
            }
            Focus();
        }

        public void DropFiles(string[] files) { Paste(string.Join(" ", files.Select(QuotePath)) + " "); }

        // Percorso scritto come lo vuole la shell della scheda: tra virgolette se serve, in WSL e Git Bash in formato Linux
        string QuotePath(string p)
        {
            if (ShellId == "wsl" || ShellId == "gitbash")
            {
                if (p.Length >= 2 && p[1] == ':') p = (ShellId == "wsl" ? "/mnt/" : "/") + char.ToLowerInvariant(p[0]) + p.Substring(2).Replace('\\', '/');
                return p.IndexOfAny(" '\"$`()&;!*?[]{}<>|#~".ToCharArray()) >= 0 ? "'" + p.Replace("'", "'\\''") + "'" : p;
            }
            if (ShellId == "lisp") return "\"" + p.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (ShellId == "pwsh" || ShellId == "powershell")
                return p.IndexOfAny(" '`$&(){}[];,@#".ToCharArray()) >= 0 ? "'" + p.Replace("'", "''") + "'" : p;
            return p.IndexOfAny(" &()[]{}^=;!'+,`~".ToCharArray()) >= 0 ? "\"" + p + "\"" : p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { blink.Dispose(); DisposeFonts(); tip.Dispose(); }
            base.Dispose(disposing);
        }

        void DisposeFonts()
        {
            foreach (var f in new[] { font, fontB, fontI, fontBI }) if (f != null) f.Dispose();
        }

        static string FontFamily()
        {
            foreach (var name in new[] { "Cascadia Mono", "Cascadia Code", "Consolas" })
                using (var f = new Font(name, 10f)) if (f.Name == name) return name;
            return "Courier New";
        }

        static string family;

        public void SetFontSize(float size)
        {
            if (family == null) family = FontFamily();
            DisposeFonts();
            font = new Font(family, size);
            fontB = new Font(family, size, FontStyle.Bold);
            fontI = new Font(family, size, FontStyle.Italic);
            fontBI = new Font(family, size, FontStyle.Bold | FontStyle.Italic);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            cw = Math.Max(1, (int)Math.Round(TextRenderer.MeasureText(new string('W', 40), font, Size.Empty, flags).Width / 40.0));
            ch = Math.Max(1, TextRenderer.MeasureText("Wg", font, Size.Empty, flags).Height);
            UpdateSize();
            Invalidate();
        }

        public int CellWidth { get { return cw; } }
        public int CellHeight { get { return ch; } }

        // ---------- dimensioni ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateSize();
            PlaceFailButton();
        }

        void UpdateSize()
        {
            if (Width <= 0 || Height <= 0) return;
            int cols = Math.Max(10, (Width - 2 * pad - sbW) / cw);
            int rows = Math.Max(3, (Height - 2 * pad) / ch);
            bool changed;
            lock (Term.Sync)
            {
                changed = cols != Term.Cols || rows != Term.Rows;
                if (changed) Term.Resize(cols, rows);
            }
            if (changed && Session != null) Session.Resize(cols, rows);
            UpdateBar();
        }

        // ---------- output ----------

        // Chiamata dal thread che legge la sessione dopo aver dato il testo all'emulatore
        public void OutputArrived()
        {
            if (Interlocked.Exchange(ref pending, 1) != 0) return;
            try { if (IsHandleCreated) BeginInvoke((Action)RefreshNow); else pending = 0; }
            catch { pending = 0; }
        }

        void RefreshNow()
        {
            pending = 0;
            if (IsDisposed) return;
            var m = Interlocked.Exchange(ref finished, null);
            if (m != null)
            {
                if (m.Failed) ShowFail(m); else HideFail();
                if (CommandDone != null) CommandDone(m);
            }
            if (searchText != null) UpdateSearch(false);
            UpdateBar();
            Invalidate();
            if (Changed != null) Changed();
        }

        // ---------- comandi falliti ----------

        readonly Button failBtn;
        CmdMark failMark, finished;

        void ShowFail(CmdMark m)
        {
            failMark = m;
            string cmd;
            lock (Term.Sync) cmd = Term.CommandText(m);
            failBtn.Text = "✦ " + L.T("Spiega l'errore", "Explain the error");
            tip.SetToolTip(failBtn, (cmd.Length > 80 ? cmd.Substring(0, 79) + "…" : cmd) + "\n" +
                L.F("Il comando è finito con il codice {0}: l'assistente spiega cosa è andato storto.", "The command exited with code {0}: the assistant explains what went wrong.", m.ExitCode));
            failBtn.Visible = true;
            PlaceFailButton();
        }

        void HideFail()
        {
            failMark = null;
            if (failBtn != null && failBtn.Visible) failBtn.Visible = false;
        }

        void PlaceFailButton()
        {
            if (failBtn == null || !failBtn.Visible) return;
            failBtn.Location = new Point(Math.Max(0, Width - sbW - pad - failBtn.Width), Math.Max(0, Height - pad - failBtn.Height));
        }

        // Il comando fallito sotto il punto (sul margine sinistro o nelle sue righe), o null
        public CmdMark FailedAt(Point p)
        {
            long l; int c;
            CellAt(p, out l, out c);
            lock (Term.Sync) return Term.AltActive ? null : Term.FailedAt(l);
        }

        // Segno rosso nel margine sinistro accanto ai comandi falliti
        void DrawMarks(Graphics g, int first)
        {
            if (Term.AltActive || Term.Marks.Count == 0) return;
            long top = first + Term.Dropped, bottom = top + Term.Rows;
            int w = Math.Max(2, pad / 2);
            var jm = jumpMark;
            if (jm != null && jm.Top >= top && jm.Top < bottom)
                using (var b = new SolidBrush(Color.FromArgb(97, 175, 239)))
                    g.FillRectangle(b, 1, pad + (int)(jm.Top - top) * ch, w, ch - 1);
            using (var b = new SolidBrush(Color.FromArgb(231, 72, 86)))
                foreach (var m in Term.Marks)
                {
                    if (!m.Failed) continue;
                    long a = Math.Max(top, m.InputLine), e = Math.Min(bottom, Math.Max(m.EndLine, m.InputLine + 1));
                    if (a >= e) continue;
                    g.FillRectangle(b, 1, pad + (int)(a - top) * ch, w, (int)(e - a) * ch - 1);
                }
        }

        // ---------- ricerca ----------

        class Hit { public long Line; public int C1, C2; }
        readonly List<Hit> hits = new List<Hit>();
        readonly Dictionary<long, List<Hit>> hitsByLine = new Dictionary<long, List<Hit>>();
        string searchText;
        bool searchCase;
        long searchVersion = -1;
        int hitCur = -1;
        public int SearchCount { get { return hits.Count; } }
        public int SearchIndex { get { return hitCur; } }

        public void SetSearch(string text, bool matchCase)
        {
            if (string.IsNullOrEmpty(text)) { ClearSearch(); return; }
            bool same = text == searchText && matchCase == searchCase;
            searchText = text;
            searchCase = matchCase;
            if (!same) { searchVersion = -1; hitCur = -1; }
            UpdateSearch(true);
        }

        public void ClearSearch()
        {
            if (searchText == null) return;
            searchText = null;
            hits.Clear();
            hitsByLine.Clear();
            hitCur = -1;
            Invalidate();
            if (SearchChanged != null) SearchChanged();
        }

        // Rifà la ricerca se lo schermo è cambiato; "select": sceglie il risultato più vicino al fondo di ciò che si vede
        void UpdateSearch(bool select)
        {
            long ver;
            lock (Term.Sync) ver = Term.Version + (Term.AltActive ? 1L << 40 : 0) + Term.Dropped * 7;
            if (ver == searchVersion && !select) return;
            Hit old = hitCur >= 0 && hitCur < hits.Count ? hits[hitCur] : null;
            int lastVisible;
            hits.Clear();
            hitsByLine.Clear();
            var cmp = searchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            lock (Term.Sync)
            {
                searchVersion = ver;
                lastVisible = FirstLine() + Term.Rows - 1;
                var map = new List<int>();
                for (int i = 0; i < Term.DocLines; i++)
                {
                    var ln = Term.DocLine(i);
                    map.Clear();
                    string s = CellText(ln, map);
                    int at = 0;
                    while ((at = s.IndexOf(searchText, at, cmp)) >= 0)
                    {
                        int endCol = map[at + searchText.Length - 1];
                        if ((ln.C[endCol].Attr & Terminal.Wide) != 0) endCol++;
                        var h = new Hit { Line = i + Term.Dropped, C1 = map[at], C2 = endCol };
                        hits.Add(h);
                        List<Hit> list;
                        if (!hitsByLine.TryGetValue(h.Line, out list)) hitsByLine[h.Line] = list = new List<Hit>();
                        list.Add(h);
                        at += searchText.Length;
                    }
                }
                hitCur = -1;
                if (old != null) hitCur = hits.FindIndex(h => h.Line == old.Line && h.C1 == old.C1);
                if (hitCur < 0 && hits.Count > 0)
                {
                    long limit = (old != null ? old.Line : lastVisible + Term.Dropped);
                    hitCur = hits.FindLastIndex(h => h.Line <= limit);
                    if (hitCur < 0) hitCur = 0;
                }
            }
            if (select && hitCur >= 0) ScrollToHit();
            Invalidate();
            if (SearchChanged != null) SearchChanged();
        }

        // Testo della riga con, per ogni carattere, la colonna da cui viene (i caratteri larghi occupano due colonne)
        static string CellText(Line ln, List<int> map)
        {
            var sb = new StringBuilder(ln.C.Length);
            for (int x = 0; x < ln.C.Length; x++)
            {
                var c = ln.C[x];
                if ((c.Attr & Terminal.WideTail) != 0) continue;
                if (c.Ext != null) { foreach (char k in c.Ext) { sb.Append(k); map.Add(x); } }
                else { sb.Append(c.Ch == '\0' ? ' ' : c.Ch); map.Add(x); }
            }
            return sb.ToString();
        }

        // Risultato successivo (up = verso l'alto, cioè più vecchio), girando in fondo
        public void FindNext(bool up)
        {
            if (searchText == null) return;
            UpdateSearch(false);
            if (hits.Count == 0) return;
            hitCur = hitCur < 0 ? hits.Count - 1 : (hitCur + (up ? -1 : 1) + hits.Count) % hits.Count;
            ScrollToHit();
            Invalidate();
            if (SearchChanged != null) SearchChanged();
        }

        void ScrollToHit()
        {
            var h = hits[hitCur];
            lock (Term.Sync)
            {
                int line = (int)(h.Line - Term.Dropped), first = FirstLine();
                if (line >= first && line < first + Term.Rows) return;   // già visibile
                int max = Math.Max(0, Term.DocLines - Term.Rows);
                int f = Math.Max(0, Math.Min(max, line - Term.Rows / 2));
                follow = f >= max;
                viewTop = f + Term.Dropped;
            }
            UpdateBar();
        }

        // 0 niente, 1 selezione, 2 risultato della ricerca, 3 risultato corrente
        int MarkOf(long line, int col)
        {
            if (IsSelected(line, col)) return 1;
            List<Hit> list;
            if (searchText == null || !hitsByLine.TryGetValue(line, out list)) return 0;
            foreach (var h in list)
                if (col >= h.C1 && col <= h.C2) return hitCur >= 0 && hitCur < hits.Count && hits[hitCur] == h ? 3 : 2;
            return 0;
        }

        // ---------- link: indirizzi web e percorsi ----------

        readonly ToolTip tip = new ToolTip();
        TermLink hoverLink;
        public TermLink MenuLink;     // link sotto il clic destro (per il menu)
        public CmdMark MenuFailed;    // comando fallito sotto il clic destro
        public CmdMark MenuCommand;   // comando (finito) sotto il clic destro

        // Il comando finito sotto il punto, o null
        public CmdMark CommandAt(Point p)
        {
            long l; int c;
            CellAt(p, out l, out c);
            lock (Term.Sync) return Term.AltActive ? null : Term.CommandAt(l);
        }

        // Porta in cima il comando precedente o successivo rispetto alla prima riga visibile
        public void JumpToCommand(bool up)
        {
            lock (Term.Sync)
            {
                int first = FirstLine();
                // con la vista in fondo, "precedente" parte dal prompt attuale: il primo salto va all'ultimo comando visibile o sopra
                long from = first + Term.Dropped;
                if (up && follow) from = Term.Dropped + Term.ScreenTop + Term.CurY;
                var m = Term.NextCommand(from, up);
                if (m == null)
                {
                    if (!up) { follow = true; }
                    else return;
                }
                else
                {
                    int max = Math.Max(0, Term.DocLines - Term.Rows);
                    int f = (int)Math.Max(0, Math.Min(max, m.Top - Term.Dropped));
                    follow = f >= max && !up;
                    viewTop = f + Term.Dropped;
                    jumpMark = m;
                }
            }
            UpdateBar();
            Invalidate();
        }

        CmdMark jumpMark;   // comando raggiunto con Ctrl+Maiusc+↑/↓: un segno chiaro nel margine
        static readonly Regex UrlRx = new Regex(@"\b(?:https?://|ftp://|www\.)[^\s""'<>`]+", RegexOptions.IgnoreCase);

        // Riga logica (righe spezzate dall'a capo automatico riunite) con, per ogni carattere, riga e colonna
        string LogicalLine(long line, List<long> lines, List<int> cols)
        {
            int i = (int)(line - Term.Dropped);
            if (Term.DocLine(i) == null) return null;
            while (i > 0 && Term.DocLine(i - 1) != null && Term.DocLine(i - 1).Wrapped) i--;
            var sb = new StringBuilder();
            var map = new List<int>();
            while (true)
            {
                var ln = Term.DocLine(i);
                if (ln == null) break;
                map.Clear();
                string s = CellText(ln, map);
                if (!ln.Wrapped) { int t = s.TrimEnd().Length; s = s.Substring(0, t); }
                sb.Append(s);
                for (int k = 0; k < s.Length; k++) { lines.Add(i + Term.Dropped); cols.Add(map[k]); }
                if (!ln.Wrapped) break;
                i++;
            }
            return sb.ToString();
        }

        public TermLink LinkAt(Point p)
        {
            if (p.X >= Width - sbW || p.X < pad) return null;
            long l; int c;
            CellAt(p, out l, out c);
            string cwd;
            var lines = new List<long>();
            var cols = new List<int>();
            string s;
            lock (Term.Sync)
            {
                s = LogicalLine(l, lines, cols);
                cwd = Term.Cwd;
            }
            if (string.IsNullOrEmpty(s)) return null;
            int at = -1;
            for (int k = 0; k < s.Length; k++) if (lines[k] == l && cols[k] == c) { at = k; break; }
            if (at < 0 || char.IsWhiteSpace(s[at])) return null;
            Func<int, int, string, bool, TermLink> make = (a, len, target, url) =>
                new TermLink { Target = target, IsUrl = url, L1 = lines[a], C1 = cols[a], L2 = lines[a + len - 1], C2 = cols[a + len - 1] };
            // indirizzo web
            foreach (Match m in UrlRx.Matches(s))
            {
                if (at < m.Index || at >= m.Index + m.Length) continue;
                string u = TrimEnd(m.Value);
                if (at >= m.Index + u.Length) return null;
                return make(m.Index, u.Length, u, true);
            }
            // percorso tra virgolette (può contenere spazi)
            foreach (char q in new[] { '"', '\'' })
            {
                int a = s.LastIndexOf(q, at), b = s.IndexOf(q, at + (s[at] == q ? 1 : 0));
                if (s[at] == q) continue;
                if (a < 0 || b < 0 || b - a < 3) continue;
                string inner = s.Substring(a + 1, b - a - 1);
                string full = ResolvePath(inner, cwd);
                if (full != null) return make(a + 1, inner.Length, full, false);
            }
            // parola sotto il mouse: percorso assoluto o nome nella cartella corrente
            int st = at, en = at;
            Func<char, bool> part = k => !char.IsWhiteSpace(k) && "\"'<>|`".IndexOf(k) < 0;
            while (st > 0 && part(s[st - 1])) st--;
            while (en + 1 < s.Length && part(s[en + 1])) en++;
            string word = s.Substring(st, en - st + 1);
            int lead = 0;
            while (lead < word.Length && "([{".IndexOf(word[lead]) >= 0) lead++;
            word = word.Substring(lead);
            string w2 = TrimEnd(word);
            // file.cs:12 o file.cs:12:5 (errori dei compilatori): si apre il file
            var pos = Regex.Match(w2, @"^(.+?)(?::\d+){1,2}:?$");
            string path = pos.Success ? pos.Groups[1].Value : w2;
            if (path.Length == 0 || at < st + lead || at >= st + lead + w2.Length) return null;
            string resolved = ResolvePath(path, cwd);
            if (resolved == null) return null;
            return make(st + lead, w2.Length, resolved, false);
        }

        static string TrimEnd(string u)
        {
            u = u.TrimEnd('.', ',', ';', ':', '!', '?', '\'', '"', ']', '}', '>');
            // parentesi chiusa in fondo: fa parte dell'indirizzo solo se ce n'è una aperta
            while (u.EndsWith(")") && u.Count(k => k == '(') < u.Count(k => k == ')')) u = u.Substring(0, u.Length - 1).TrimEnd('.', ',', ';', ':');
            return u;
        }

        // Percorso Windows esistente (assoluto, o relativo alla cartella della scheda); null se non lo è.
        // I percorsi di rete (\\server) no: controllarli può bloccare l'interfaccia per secondi.
        static string ResolvePath(string p, string cwd)
        {
            try
            {
                p = p.Trim();
                if (p.Length == 0 || p == "." || p == ".." || p.StartsWith("\\\\") || p.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || p.IndexOfAny(new[] { '*', '?' }) >= 0) return null;
                if (p.StartsWith("~\\") || p == "~") p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
                string full;
                if (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/')) full = p;
                else
                {
                    if (cwd == null || cwd.StartsWith("/") || cwd.StartsWith("\\\\") || p.StartsWith("/") || p.Contains(":")) return null;
                    full = Path.Combine(cwd, p);
                }
                full = Path.GetFullPath(full);
                return File.Exists(full) || Directory.Exists(full) ? full : null;
            }
            catch { return null; }
        }

        static readonly string[] Risky = { ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp",
                                           ".scr", ".lnk", ".reg", ".hta", ".cpl", ".jar", ".pif", ".url", ".application", ".appref-ms", ".msc", ".inf", ".sys", ".dll" };

        // Apre un link: indirizzi nel browser, cartelle in Esplora file, file con il loro programma;
        // i file che si eseguiranno (programmi, script) non si avviano: si mostrano selezionati in Esplora file
        public static void OpenLink(TermLink k)
        {
            try
            {
                if (k.IsUrl) { Process.Start(new ProcessStartInfo(k.Target.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + k.Target : k.Target) { UseShellExecute = true }); return; }
                if (Directory.Exists(k.Target)) { Process.Start("explorer.exe", "\"" + k.Target + "\""); return; }
                if (Risky.Contains(Path.GetExtension(k.Target).ToLowerInvariant())) { ShowInExplorer(k); return; }
                Process.Start(new ProcessStartInfo(k.Target) { UseShellExecute = true });
            }
            catch { }
        }

        public static void ShowInExplorer(TermLink k)
        {
            try { Process.Start("explorer.exe", "/select,\"" + k.Target + "\""); } catch { }
        }

        void SetHover(TermLink k)
        {
            bool same = (k == null && hoverLink == null) || (k != null && hoverLink != null && k.L1 == hoverLink.L1 && k.C1 == hoverLink.C1 && k.C2 == hoverLink.C2 && k.Target == hoverLink.Target);
            if (!same)
            {
                hoverLink = k;
                Invalidate();
                tip.SetToolTip(this, k == null ? null : (k.Target.Length > 120 ? k.Target.Substring(0, 119) + "…" : k.Target) + "\n" +
                    (k.IsUrl ? L.T("Ctrl+clic: apri nel browser", "Ctrl+click: open in the browser")
                     : k.IsDir ? L.T("Ctrl+clic: apri in Esplora file", "Ctrl+click: open in File Explorer")
                     : L.T("Ctrl+clic: apri il file", "Ctrl+click: open the file")));
            }
            UpdateCursor();
        }

        void UpdateCursor()
        {
            var want = sbHover ? Cursors.Default
                     : hoverLink != null && (ModifierKeys & Keys.Control) != 0 ? Cursors.Hand
                     : marginHover ? Cursors.Hand : Cursors.IBeam;
            if (Cursor != want) Cursor = want;
        }

        bool marginHover;

        int FirstLine()
        {
            int total = Term.DocLines, max = Math.Max(0, total - Term.Rows);
            if (follow || Term.AltActive) return max;
            long v = viewTop - Term.Dropped;
            return (int)Math.Max(0, Math.Min(max, v));
        }

        void UpdateBar() { Invalidate(new Rectangle(Width - sbW, 0, sbW, Height)); }

        // Cursore della barra: Rectangle.Empty se non c'è niente da scorrere
        Rectangle Thumb()
        {
            int total, rows, first;
            lock (Term.Sync) { total = Term.DocLines; rows = Term.Rows; first = FirstLine(); }
            if (total <= rows || Height <= 0) return Rectangle.Empty;
            int h = Math.Max((int)(28 * scale), Height * rows / total);
            int y = (int)((long)(Height - h) * first / Math.Max(1, total - rows));
            return new Rectangle(Width - sbW, y, sbW, h);
        }

        void DrawScrollbar(Graphics g)
        {
            var th = Thumb();
            if (th.IsEmpty) return;
            using (var b = new SolidBrush(Color.FromArgb(24, 24, 24))) g.FillRectangle(b, Width - sbW, 0, sbW, Height);
            int m = Math.Max(2, sbW / 4);
            var r = new Rectangle(th.X + m, th.Y + 2, th.Width - 2 * m, th.Height - 4);
            using (var b = new SolidBrush(sbDrag ? Color.FromArgb(170, 170, 170) : sbHover ? Color.FromArgb(140, 140, 140) : Color.FromArgb(95, 95, 95)))
            using (var p = Rounded(r, r.Width / 2))
            {
                var old = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillPath(b, p);
                g.SmoothingMode = old;
            }
        }

        static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            rad = Math.Max(1, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Porta il cursore della barra in modo che il suo bordo alto sia a y
        void ScrollToThumb(int y)
        {
            var th = Thumb();
            if (th.IsEmpty) return;
            lock (Term.Sync)
            {
                int max = Math.Max(0, Term.DocLines - Term.Rows);
                int first = (int)Math.Round((double)Math.Max(0, Math.Min(Height - th.Height, y)) * max / Math.Max(1, Height - th.Height));
                follow = first >= max;
                viewTop = first + Term.Dropped;
            }
            Invalidate();
        }

        public void ScrollLines(int delta)
        {
            lock (Term.Sync)
            {
                int max = Math.Max(0, Term.DocLines - Term.Rows);
                int first = Math.Max(0, Math.Min(max, FirstLine() + delta));
                follow = first >= max;
                viewTop = first + Term.Dropped;
            }
            UpdateBar();
            Invalidate();
        }

        public void ScrollToBottom()
        {
            if (follow) return;
            follow = true;
            UpdateBar();
            Invalidate();
        }

        // ---------- disegno ----------

        static Color[] BuildPalette()
        {
            var p = new Color[256];
            int[] basic = { 0x0C0C0C, 0xC50F1F, 0x13A10E, 0xC19C00, 0x0037DA, 0x881798, 0x3A96DD, 0xCCCCCC,
                            0x767676, 0xE74856, 0x16C60C, 0xF9F1A5, 0x3B78FF, 0xB4009E, 0x61D6D6, 0xF2F2F2 };
            for (int i = 0; i < 16; i++) p[i] = Color.FromArgb(basic[i] >> 16, (basic[i] >> 8) & 255, basic[i] & 255);
            int[] lv = { 0, 95, 135, 175, 215, 255 };
            for (int i = 0; i < 216; i++) p[16 + i] = Color.FromArgb(lv[i / 36], lv[(i / 6) % 6], lv[i % 6]);
            for (int i = 0; i < 24; i++) { int v = 8 + i * 10; p[232 + i] = Color.FromArgb(v, v, v); }
            return p;
        }

        static Color ColorOf(int c, bool isFg, ushort attr)
        {
            if (c < 0) return isFg ? Foreground : Background;
            if ((c & Terminal.Rgb) != 0) return Color.FromArgb((c >> 16) & 255, (c >> 8) & 255, c & 255);
            if (isFg && (attr & Terminal.Bold) != 0 && c < 8) c += 8;   // grassetto = colore chiaro
            return Palette[c & 255];
        }

        static readonly Color HitColor = Color.FromArgb(110, 92, 20), HitCurrent = Color.FromArgb(240, 170, 20), LinkColor = Color.FromArgb(97, 175, 239);

        // mark: 0 niente, 1 selezione, 2 risultato della ricerca, 3 risultato corrente
        void Colors(Cell c, int mark, out Color fgc, out Color bgc)
        {
            fgc = ColorOf(c.Fg, true, c.Attr);
            bgc = ColorOf(c.Bg, false, c.Attr);
            if ((c.Attr & Terminal.Inverse) != 0) { var t = fgc; fgc = bgc; bgc = t; }
            if ((c.Attr & Terminal.Faint) != 0) fgc = Color.FromArgb((fgc.R + bgc.R) / 2, (fgc.G + bgc.G) / 2, (fgc.B + bgc.B) / 2);
            if ((c.Attr & Terminal.Hidden) != 0) fgc = bgc;
            if (mark == 1) bgc = SelectionColor;
            else if (mark == 2) { bgc = HitColor; fgc = Color.White; }
            else if (mark == 3) { bgc = HitCurrent; fgc = Color.Black; }
        }

        Font FontOf(ushort a)
        {
            bool b = (a & Terminal.Bold) != 0, i = (a & Terminal.Italic) != 0;
            return b && i ? fontBI : b ? fontB : i ? fontI : font;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Background);
            lock (Term.Sync)
            {
                int first = FirstLine();
                for (int r = 0; r < Term.Rows; r++)
                {
                    var ln = Term.DocLine(first + r);
                    if (ln == null) break;
                    int y = pad + r * ch;
                    if (y > e.ClipRectangle.Bottom || y + ch < e.ClipRectangle.Top) continue;
                    DrawLine(g, ln, y, first + r + Term.Dropped);
                }
                int cy = Term.ScreenTop + Term.CurY - first;
                if (Term.CursorVisible && cy >= 0 && cy < Term.Rows) DrawCursor(g, cy);
                DrawMarks(g, first);
            }
            DrawScrollbar(g);
        }

        void DrawLine(Graphics g, Line ln, int y, long absLine)
        {
            int n = Math.Min(ln.C.Length, Term.Cols);
            // sfondi
            int x = 0;
            while (x < n)
            {
                Color fgc, bgc;
                Colors(ln.C[x], MarkOf(absLine, x), out fgc, out bgc);
                int start = x;
                x++;
                while (x < n)
                {
                    Color f2, b2;
                    Colors(ln.C[x], MarkOf(absLine, x), out f2, out b2);
                    if (b2 != bgc) break;
                    x++;
                }
                if (bgc != Background)
                    using (var b = new SolidBrush(bgc)) g.FillRectangle(b, pad + start * cw, y, (x - start) * cw, ch);
            }
            // testo: a gruppi di caratteri semplici con lo stesso stile, gli altri uno per uno nella loro cella
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var sb = new StringBuilder();
            x = 0;
            while (x < n)
            {
                var c = ln.C[x];
                if ((c.Attr & Terminal.WideTail) != 0) { x++; continue; }
                Color fgc, bgc;
                Colors(c, MarkOf(absLine, x), out fgc, out bgc);
                bool simple = c.Ext == null && c.Ch > ' ' && c.Ch < 0x7f && (c.Attr & Terminal.Wide) == 0;
                if (!simple)
                {
                    if (c.Ext != null || (c.Ch > ' ' && c.Ch != '\0'))
                    {
                        int w = (c.Attr & Terminal.Wide) != 0 ? 2 : 1;
                        TextRenderer.DrawText(g, c.Ext ?? c.Ch.ToString(), FontOf(c.Attr), new Rectangle(pad + x * cw, y, w * cw, ch), fgc,
                            flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoClipping);
                    }
                    Decorate(g, c.Attr, fgc, x, (c.Attr & Terminal.Wide) != 0 ? 2 : 1, y);
                    x++;
                    continue;
                }
                int start = x;
                sb.Clear();
                ushort style = (ushort)(c.Attr & (Terminal.Bold | Terminal.Italic | Terminal.Under | Terminal.Strike));
                while (x < n)
                {
                    var d = ln.C[x];
                    if (d.Ext != null || d.Ch < ' ' || d.Ch >= 0x7f || (d.Attr & (Terminal.Wide | Terminal.WideTail)) != 0) break;
                    if ((ushort)(d.Attr & (Terminal.Bold | Terminal.Italic | Terminal.Under | Terminal.Strike)) != style) break;
                    Color f2, b2;
                    Colors(d, MarkOf(absLine, x), out f2, out b2);
                    if (f2 != fgc) break;
                    sb.Append(d.Ch);
                    x++;
                }
                string s = sb.ToString();
                if (s.Trim().Length > 0) TextRenderer.DrawText(g, s, FontOf(c.Attr), new Point(pad + start * cw, y), fgc, flags);
                Decorate(g, c.Attr, fgc, start, x - start, y);
            }
            // link sotto il mouse: sottolineato
            var k = hoverLink;
            if (k != null && absLine >= k.L1 && absLine <= k.L2)
            {
                int a = absLine == k.L1 ? k.C1 : 0, b = absLine == k.L2 ? k.C2 : n - 1;
                using (var p = new Pen(LinkColor, Math.Max(1f, scale)))
                    g.DrawLine(p, pad + a * cw, y + ch - 1, pad + (b + 1) * cw - 1, y + ch - 1);
            }
        }

        void Decorate(Graphics g, ushort a, Color c, int x, int w, int y)
        {
            if ((a & (Terminal.Under | Terminal.Strike)) == 0) return;
            using (var p = new Pen(c, Math.Max(1f, scale)))
            {
                if ((a & Terminal.Under) != 0) g.DrawLine(p, pad + x * cw, y + ch - 2, pad + (x + w) * cw - 1, y + ch - 2);
                if ((a & Terminal.Strike) != 0) g.DrawLine(p, pad + x * cw, y + ch / 2, pad + (x + w) * cw - 1, y + ch / 2);
            }
        }

        void DrawCursor(Graphics g, int row)
        {
            int cx = Math.Min(Term.CurX, Term.Cols - 1);
            var ln = Term.DocLine(Term.ScreenTop + Term.CurY);
            var cell = ln != null && cx < ln.C.Length ? ln.C[cx] : new Cell { Ch = ' ', Fg = -1, Bg = -1 };
            int w = (cell.Attr & Terminal.Wide) != 0 ? 2 : 1;
            var r = new Rectangle(pad + cx * cw, pad + row * ch, w * cw, ch);
            bool focused = Focused;
            int shape = Term.CursorShape;
            if (!focused)
            {
                using (var p = new Pen(CursorColor)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                return;
            }
            if (!blinkOn) return;
            using (var b = new SolidBrush(CursorColor))
            {
                if (shape == 3 || shape == 4) { g.FillRectangle(b, r.X, r.Bottom - Math.Max(2, (int)(2 * scale)), r.Width, Math.Max(2, (int)(2 * scale))); return; }
                if (shape == 5 || shape == 6) { g.FillRectangle(b, r.X, r.Y, Math.Max(2, (int)(2 * scale)), r.Height); return; }
                g.FillRectangle(b, r);
            }
            string s = cell.Ext ?? (cell.Ch > ' ' ? cell.Ch.ToString() : null);
            if (s != null)
                TextRenderer.DrawText(g, s, FontOf(cell.Attr), r, Background, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
        }

        void InvalidateCursor()
        {
            int row;
            lock (Term.Sync) row = Term.ScreenTop + Term.CurY - FirstLine();
            Invalidate(new Rectangle(0, pad + row * ch, Width, ch));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); blinkOn = true; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ---------- selezione ----------

        bool IsSelected(long line, int col)
        {
            if (!hasSel) return false;
            long l1 = selL1, l2 = selL2; int c1 = selC1, c2 = selC2;
            if (l1 > l2 || (l1 == l2 && c1 > c2)) { long t = l1; l1 = l2; l2 = t; int u = c1; c1 = c2; c2 = u; }
            if (line < l1 || line > l2) return false;
            if (line == l1 && col < c1) return false;
            if (line == l2 && col > c2) return false;
            return true;
        }

        public bool HasSelection { get { return hasSel && (selL1 != selL2 || selC1 != selC2); } }

        public string SelectedText
        {
            get
            {
                if (!hasSel) return "";
                lock (Term.Sync)
                {
                    long l1 = selL1, l2 = selL2; int c1 = selC1, c2 = selC2;
                    if (l1 > l2 || (l1 == l2 && c1 > c2)) { long t = l1; l1 = l2; l2 = t; int u = c1; c1 = c2; c2 = u; }
                    var sb = new StringBuilder();
                    for (long l = l1; l <= l2; l++)
                    {
                        var ln = Term.DocLine((int)(l - Term.Dropped));
                        if (ln == null) continue;
                        int a = l == l1 ? c1 : 0, b = l == l2 ? c2 + 1 : ln.C.Length;
                        string s = Terminal.LineText(ln, a, b, false);
                        if (!(ln.Wrapped && l != l2)) s = s.TrimEnd();
                        sb.Append(s);
                        if (l != l2 && !ln.Wrapped) sb.Append("\r\n");
                    }
                    return sb.ToString();
                }
            }
        }

        public void ClearSelection() { if (hasSel) { hasSel = false; Invalidate(); } }

        public void SelectAll()
        {
            lock (Term.Sync)
            {
                selL1 = Term.Dropped; selC1 = 0;
                selL2 = Term.Dropped + Term.ScreenTop + Term.CurY; selC2 = Term.Cols - 1;
            }
            hasSel = true;
            Invalidate();
        }

        void CellAt(Point p, out long line, out int col)
        {
            lock (Term.Sync)
            {
                int row = Math.Max(0, Math.Min(Term.Rows - 1, (p.Y - pad) / ch));
                col = Math.Max(0, Math.Min(Term.Cols - 1, (p.X - pad) / cw));
                line = FirstLine() + row + Term.Dropped;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Right)
            {
                MenuLink = LinkAt(e.Location);
                MenuFailed = FailedAt(e.Location);
                MenuCommand = CommandAt(e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            // Ctrl+clic su un link: si apre
            if ((ModifierKeys & Keys.Control) != 0 && e.X < Width - sbW)
            {
                var k = LinkAt(e.Location);
                if (k != null) { OpenLink(k); return; }
            }
            // clic sul margine sinistro accanto a un comando fallito: l'assistente lo spiega
            if (e.X < pad)
            {
                var m = FailedAt(e.Location);
                if (m != null) { if (ExplainRequested != null) ExplainRequested(m); return; }
            }
            if (e.X >= Width - sbW)
            {
                var th = Thumb();
                if (th.IsEmpty) return;
                if (th.Contains(e.Location)) { sbDrag = true; sbGrab = e.Y - th.Y; Capture = true; Invalidate(); }
                else
                {
                    int rows;
                    lock (Term.Sync) rows = Term.Rows;
                    ScrollLines(e.Y < th.Y ? -(rows - 1) : rows - 1);   // clic sulla pista: una pagina
                }
                return;
            }
            long l; int c;
            CellAt(e.Location, out l, out c);
            if (e.Clicks == 2) { SelectWord(l, c); return; }
            if ((ModifierKeys & Keys.Shift) != 0 && hasSel) { selL2 = l; selC2 = c; Invalidate(); return; }
            hasSel = false;
            selL1 = selL2 = l; selC1 = selC2 = c;
            selecting = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (sbDrag) { ScrollToThumb(e.Y - sbGrab); return; }
            bool hov = e.X >= Width - sbW && !Thumb().IsEmpty;
            if (hov != sbHover) { sbHover = hov; UpdateBar(); }
            if (e.Button == MouseButtons.None)
            {
                bool mh = e.X < pad && FailedAt(e.Location) != null;
                if (mh != marginHover)
                {
                    marginHover = mh;
                    if (mh) tip.SetToolTip(this, L.T("Comando fallito: clic per chiedere all'assistente perché", "Failed command: click to ask the assistant why"));
                    else if (hoverLink == null) tip.SetToolTip(this, null);
                }
                // il link si cerca solo quando il mouse passa a un'altra cella
                long hl; int hc;
                CellAt(e.Location, out hl, out hc);
                if (hl != hoverL || hc != hoverC || hov) { hoverL = hl; hoverC = hc; SetHover(hov || mh ? null : LinkAt(e.Location)); }
                else UpdateCursor();
            }
            if (!selecting || e.Button != MouseButtons.Left) return;
            if (e.Y < 0) ScrollLines(-1); else if (e.Y > Height) ScrollLines(1);
            long l; int c;
            CellAt(e.Location, out l, out c);
            if (l != selL2 || c != selC2 || !hasSel)
            {
                selL2 = l; selC2 = c;
                hasSel = selL1 != selL2 || selC1 != selC2;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            selecting = false;
            if (sbDrag) { sbDrag = false; Capture = false; UpdateBar(); }
        }

        long hoverL = -1; int hoverC = -1;

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sbHover && !sbDrag) { sbHover = false; UpdateBar(); }
            marginHover = false;
            hoverL = -1;
            SetHover(null);
        }

        void SelectWord(long line, int col)
        {
            lock (Term.Sync)
            {
                var ln = Term.DocLine((int)(line - Term.Dropped));
                if (ln == null || col >= ln.C.Length) return;
                Func<int, bool> word = x => { char k = ln.C[x].Ch; return k > ' ' && " \t\"'`()[]{}<>|,;=".IndexOf(k) < 0; };
                if (!word(col)) return;
                int a = col, b = col;
                while (a > 0 && word(a - 1)) a--;
                while (b + 1 < ln.C.Length && word(b + 1)) b++;
                selL1 = selL2 = line; selC1 = a; selC2 = b;
            }
            hasSel = true;
            selecting = false;
            Invalidate();
        }

        // Movimenti della rotellina non ancora diventati righe: il touchpad manda tanti passi piccoli (meno di 120)
        int wheelRest, zoomRest;

        // Chi ha ricevuto l'ultimo movimento (0 schermo, 1 frecce, 2 programma): cambiando destinatario o verso, il resto si azzera
        int wheelKind;

        int WheelSteps(int kind, int amount)
        {
            if (kind != wheelKind || (wheelRest > 0) != (amount > 0)) wheelRest = 0;
            wheelKind = kind;
            wheelRest += amount;
            int n = wheelRest / 120;
            wheelRest -= n * 120;
            return n;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // gestita qui: altrimenti Windows la passa anche ai contenitori
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if ((ModifierKeys & Keys.Control) != 0)
            {
                zoomRest += e.Delta;
                int z = zoomRest / 120;
                zoomRest -= z * 120;
                if (z != 0 && ZoomRequested != null) ZoomRequested(z);
                return;
            }
            // righe per scatto come nelle impostazioni di Windows (di solito 3; "una schermata" = 3)
            int perNotch = SystemInformation.MouseWheelScrollLines;
            if (perNotch <= 0 || perNotch > 100) perNotch = 3;
            bool shift = (ModifierKeys & Keys.Shift) != 0;
            bool alt, mouse, sgr, altScroll;
            int cols, rows;
            lock (Term.Sync)
            {
                alt = Term.AltActive; mouse = Term.MouseOn; sgr = Term.MouseSgr; altScroll = Term.AltScroll;
                cols = Term.Cols; rows = Term.Rows;
            }
            // Il programma ha chiesto il mouse (Claude Code a schermo intero, vim con mouse=a, htop...): riceve la rotellina e
            // scorre lui quello che c'è sotto il puntatore. Con le frecce scorreva la cronologia dei comandi invece dei risultati.
            // Con Maiusc, o mentre si guarda la cronologia dello schermo, scorre invece il terminale.
            if (mouse && !shift && (alt || follow))
            {
                int notches = WheelSteps(2, e.Delta);
                if (notches == 0 || Session == null) return;
                int x = Math.Max(1, Math.Min(cols, (e.X - pad) / cw + 1)), y = Math.Max(1, Math.Min(rows, (e.Y - pad) / ch + 1));
                int button = notches > 0 ? 64 : 65;
                if ((ModifierKeys & Keys.Alt) != 0) button += 8;
                // formato vecchio: un carattere per coordinata, oltre 95 non sarebbe più un byte solo
                string ev = sgr ? "\x1b[<" + button + ";" + x + ";" + y + "M"
                                : "\x1b[M" + (char)(32 + button) + (char)(32 + Math.Min(x, 95)) + (char)(32 + Math.Min(y, 95));
                var all = new StringBuilder();
                for (int i = 0; i < Math.Abs(notches); i++) all.Append(ev);
                Session.Send(all.ToString());
                return;
            }
            if (alt && (shift || !altScroll)) return;   // lo schermo alternativo non ha cronologia
            int lines = WheelSteps(alt ? 1 : 0, e.Delta * perNotch);
            if (lines == 0) return;
            if (alt)
            {
                // schermo alternativo senza mouse (less, man...): la rotellina manda le frecce
                string k = lines > 0 ? (Term.AppCursor ? "\x1bOA" : "\x1b[A") : (Term.AppCursor ? "\x1bOB" : "\x1b[B");
                var sb = new StringBuilder();
                for (int i = 0; i < Math.Abs(lines); i++) sb.Append(k);
                if (Session != null) Session.Send(sb.ToString());
                return;
            }
            ScrollLines(-lines);
        }

        // ---------- tastiera ----------

        protected override bool IsInputKey(Keys keyData) { return true; }

        // Testo scritto dall'utente (tasti, comandi dell'assistente, preferiti): va alla sessione e, se richiesto, alle altre schede
        public void SendInput(string s)
        {
            if (Session == null || string.IsNullOrEmpty(s)) return;
            if (UserInput != null) UserInput(s, false);
            SendRaw(s);
        }

        void SendRaw(string s)
        {
            if (Session == null || string.IsNullOrEmpty(s)) return;
            ScrollToBottom();
            ClearSelection();
            blinkOn = true;
            if (s.IndexOf('\r') >= 0) { HideFail(); lastEnter = DateTime.Now; jumpMark = null; }   // un comando nuovo: il pulsante dell'errore di prima non serve più
            Session.Send(s);
        }

        public void Paste(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r\n", "\r").Replace('\n', '\r');
            if (UserInput != null) UserInput(text, true);
            PasteRaw(text);
        }

        void PasteRaw(string text)
        {
            bool bracketed;
            lock (Term.Sync) bracketed = Term.BracketedPaste;
            SendRaw(bracketed ? "\x1b[200~" + text + "\x1b[201~" : text);
        }

        // Testo scritto in un'altra scheda con "scrivi in tutte le schede": arriva qui come se fosse scritto qui
        public void Mirror(string s, bool paste)
        {
            if (paste) PasteRaw(s); else SendRaw(s);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey) UpdateCursor();
            base.OnKeyUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey) { UpdateCursor(); return; }
            if (e.KeyCode == Keys.Apps) { MenuLink = null; MenuFailed = null; MenuCommand = null; }   // menu da tastiera: niente link sotto il mouse
            // Ctrl+Maiusc+↑/↓: al comando precedente / successivo
            if (e.Control && e.Shift && !e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                bool alt;
                lock (Term.Sync) alt = Term.AltActive;
                if (!alt) { JumpToCommand(e.KeyCode == Keys.Up); e.Handled = e.SuppressKeyPress = true; return; }
            }
            if (e.Shift && !e.Control && (e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown))
            {
                int rows;
                lock (Term.Sync) rows = Term.Rows;
                ScrollLines(e.KeyCode == Keys.PageUp ? -(rows - 1) : rows - 1);
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            string s = KeySequence(e);
            if (s != null)
            {
                SendInput(s);
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (e.Handled) return;
            SendInput(e.KeyChar.ToString());
            e.Handled = true;
        }

        string KeySequence(KeyEventArgs e)
        {
            bool app;
            lock (Term.Sync) app = Term.AppCursor;
            int m = 1 + (e.Shift ? 1 : 0) + (e.Alt ? 2 : 0) + (e.Control ? 4 : 0);
            bool altGr = e.Control && e.Alt;   // AltGr (tastiera italiana: @ # [ ]) arriva come Ctrl+Alt: lo gestisce KeyPress
            switch (e.KeyCode)
            {
                case Keys.Up: return Cursor_('A', m, app);
                case Keys.Down: return Cursor_('B', m, app);
                case Keys.Right: return Cursor_('C', m, app);
                case Keys.Left: return Cursor_('D', m, app);
                case Keys.Home: return Cursor_('H', m, app);
                case Keys.End: return Cursor_('F', m, app);
                case Keys.Insert: return Tilde(2, m);
                case Keys.Delete: return Tilde(3, m);
                case Keys.PageUp: return Tilde(5, m);
                case Keys.PageDown: return Tilde(6, m);
                case Keys.F1: return Fn('P', m);
                case Keys.F2: return Fn('Q', m);
                case Keys.F3: return Fn('R', m);
                case Keys.F4: return Fn('S', m);
                case Keys.F5: return Tilde(15, m);
                case Keys.F6: return Tilde(17, m);
                case Keys.F7: return Tilde(18, m);
                case Keys.F8: return Tilde(19, m);
                case Keys.F9: return Tilde(20, m);
                case Keys.F10: return Tilde(21, m);
                case Keys.F11: return Tilde(23, m);
                case Keys.F12: return Tilde(24, m);
                case Keys.Back: return altGr ? null : (e.Alt ? "\x1b" : "") + (e.Control ? "\x08" : "\x7f");
                case Keys.Enter: return (e.Alt && !e.Control ? "\x1b" : "") + "\r";
                case Keys.Tab: return e.Shift ? "\x1b[Z" : "\t";
                case Keys.Escape: return "\x1b";
                case Keys.Space: return e.Control && !e.Alt ? "\0" : null;
            }
            // Alt+lettera / cifra / punto: ESC davanti (scorciatoie di bash e PSReadLine)
            if (e.Alt && !e.Control)
            {
                if (e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
                {
                    char c = (char)('a' + (e.KeyCode - Keys.A));
                    return "\x1b" + (e.Shift ? char.ToUpperInvariant(c) : c);
                }
                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) return "\x1b" + (char)('0' + (e.KeyCode - Keys.D0));
                if (e.KeyCode == Keys.OemPeriod) return "\x1b.";
            }
            return null;
        }

        static string Cursor_(char k, int m, bool app)
        {
            if (m > 1) return "\x1b[1;" + m + k;
            return (app ? "\x1bO" : "\x1b[") + k;
        }

        static string Tilde(int n, int m) { return m > 1 ? "\x1b[" + n + ";" + m + "~" : "\x1b[" + n + "~"; }
        static string Fn(char k, int m) { return m > 1 ? "\x1b[1;" + m + k : "\x1bO" + k; }
    }
}
