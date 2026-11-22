// Disegno del terminale e tastiera: mostra lo schermo dell'emulatore (Terminal) con la cronologia scorrevole,
// traduce i tasti in sequenze VT per la sessione, selezione col mouse, copia e incolla.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CorbolPrompt
{
    class TermView : Control
    {
        public readonly Terminal Term;
        public ISession Session;
        public event Action Changed;          // nuovo output (titolo, cartella...): sul thread dell'interfaccia
        public event Action<int> ZoomRequested;

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

        public TermView(Terminal term, float fontSize)
        {
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { blink.Dispose(); DisposeFonts(); }
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
            UpdateBar();
            Invalidate();
            if (Changed != null) Changed();
        }

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

        void Colors(Cell c, bool selected, out Color fgc, out Color bgc)
        {
            fgc = ColorOf(c.Fg, true, c.Attr);
            bgc = ColorOf(c.Bg, false, c.Attr);
            if ((c.Attr & Terminal.Inverse) != 0) { var t = fgc; fgc = bgc; bgc = t; }
            if ((c.Attr & Terminal.Faint) != 0) fgc = Color.FromArgb((fgc.R + bgc.R) / 2, (fgc.G + bgc.G) / 2, (fgc.B + bgc.B) / 2);
            if ((c.Attr & Terminal.Hidden) != 0) fgc = bgc;
            if (selected) bgc = SelectionColor;
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
                Colors(ln.C[x], IsSelected(absLine, x), out fgc, out bgc);
                int start = x;
                x++;
                while (x < n)
                {
                    Color f2, b2;
                    Colors(ln.C[x], IsSelected(absLine, x), out f2, out b2);
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
                Colors(c, IsSelected(absLine, x), out fgc, out bgc);
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
                    Colors(d, IsSelected(absLine, x), out f2, out b2);
                    if (f2 != fgc) break;
                    sb.Append(d.Ch);
                    x++;
                }
                string s = sb.ToString();
                if (s.Trim().Length > 0) TextRenderer.DrawText(g, s, FontOf(c.Attr), new Point(pad + start * cw, y), fgc, flags);
                Decorate(g, c.Attr, fgc, start, x - start, y);
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
            if (e.Button != MouseButtons.Left) return;
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
            if (hov != sbHover) { sbHover = hov; Cursor = hov ? Cursors.Default : Cursors.IBeam; UpdateBar(); }
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

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sbHover && !sbDrag) { sbHover = false; Cursor = Cursors.IBeam; UpdateBar(); }
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

        protected override void OnMouseWheel(MouseEventArgs e)
        {
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
            wheelRest += e.Delta * perNotch;
            int lines = wheelRest / 120;
            wheelRest -= lines * 120;
            if (lines == 0) return;
            bool alt;
            lock (Term.Sync) alt = Term.AltActive;
            if (alt)
            {
                // schermo alternativo (less, man, vim...): la rotellina manda le frecce
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

        public void SendInput(string s)
        {
            if (Session == null || string.IsNullOrEmpty(s)) return;
            ScrollToBottom();
            ClearSelection();
            blinkOn = true;
            Session.Send(s);
        }

        public void Paste(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.Replace("\r\n", "\r").Replace('\n', '\r');
            bool bracketed;
            lock (Term.Sync) bracketed = Term.BracketedPaste;
            SendInput(bracketed ? "\x1b[200~" + text + "\x1b[201~" : text);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
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
