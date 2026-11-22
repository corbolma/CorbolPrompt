// Emulatore di terminale VT/xterm: legge il testo con le sequenze di escape (dalla console virtuale di Windows o
// dall'interprete Lisp) e tiene lo schermo (righe di celle con carattere, colori e stile) e la cronologia sopra.
// Supporta ciò che usano shell e programmi a schermo intero: movimenti del cursore, cancellazioni, colori
// 16/256/24 bit, area di scorrimento, schermo alternativo, a capo automatico, caratteri larghi, titolo e cartella (OSC).
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CorbolPrompt
{
    struct Cell
    {
        public char Ch;        // carattere (' ' = vuoto)
        public string Ext;     // carattere fuori dal piano base (emoji...), altrimenti null
        public int Fg, Bg;     // -1 = predefinito; 0-255 = tavolozza; 0x1000000 | rgb = colore diretto
        public ushort Attr;
    }

    class Line
    {
        public Cell[] C;
        public bool Wrapped;   // continua nella riga sotto (a capo automatico)
        public Line(int cols, Cell blank)
        {
            C = new Cell[cols];
            for (int i = 0; i < cols; i++) C[i] = blank;
        }
        public Line(Cell[] c) { C = c; }
    }

    class Terminal
    {
        public const ushort Bold = 1, Faint = 2, Italic = 4, Under = 8, Inverse = 16, Hidden = 32, Strike = 64, Wide = 128, WideTail = 256;
        public const int Rgb = 0x1000000;

        public readonly object Sync = new object();   // chi legge lo schermo (disegno) o scrive (output) lo prende
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public int MaxScrollback = 10000;

        readonly List<Line> scroll = new List<Line>();   // righe uscite dall'alto dello schermo principale
        Line[] screen, mainScreen, altScreen;
        public bool AltActive { get; private set; }
        public int CurX, CurY;
        bool wrapNext;
        int top, bottom;                       // area di scorrimento (righe incluse)
        int fg = -1, bg = -1; ushort attr;     // stile corrente
        // cursore salvato (ESC 7 / CSI s)
        int sX, sY, sFg = -1, sBg = -1; ushort sAttr; bool sOrigin;
        public bool AppCursor, BracketedPaste, CursorVisible = true, AutoWrap = true, OriginMode, InsertMode, AppKeypad;
        public int CursorShape;                // 0-2 blocco, 3-4 sottolineato, 5-6 barra (DECSCUSR)
        bool[] tabs;
        char g0 = 'B', g1 = 'B'; bool shiftOut;
        Cell lastPrinted; bool hasLast;
        char pendingHigh;

        public string Title = "", Cwd;
        public long Dropped;                   // righe di cronologia eliminate (per gli indici assoluti)
        public long Version;                   // cambia a ogni modifica: il disegno sa se rifare
        public event Action<string> Reply;     // risposte al programma (posizione del cursore, identità del terminale)
        public event Action TitleChanged, CwdChanged, Bell;

        public Terminal(int cols, int rows)
        {
            Cols = Math.Max(2, cols); Rows = Math.Max(1, rows);
            mainScreen = NewScreen(Cols, Rows);
            altScreen = NewScreen(Cols, Rows);
            screen = mainScreen;
            top = 0; bottom = Rows - 1;
            ResetTabs();
        }

        Line[] NewScreen(int cols, int rows)
        {
            var s = new Line[rows];
            for (int i = 0; i < rows; i++) s[i] = new Line(cols, BlankDefault);
            return s;
        }

        static readonly Cell BlankDefault = new Cell { Ch = ' ', Fg = -1, Bg = -1 };
        Cell Blank { get { return new Cell { Ch = ' ', Fg = -1, Bg = bg }; } }   // le cancellazioni usano lo sfondo corrente

        void ResetTabs()
        {
            tabs = new bool[Cols];
            for (int i = 8; i < Cols; i += 8) tabs[i] = true;
        }

        // ---------- lettura per il disegno e per l'assistente ----------

        // Righe del "documento": cronologia + schermo (sullo schermo alternativo solo lo schermo)
        public int DocLines { get { return AltActive ? Rows : scroll.Count + Rows; } }
        public int ScreenTop { get { return AltActive ? 0 : scroll.Count; } }
        public Line DocLine(int i)
        {
            int st = ScreenTop;
            if (i < st) return i >= 0 ? scroll[i] : null;
            i -= st;
            return i < Rows ? screen[i] : null;
        }

        // Testo delle righe [from, to) del documento, con le righe spezzate dall'a capo automatico riunite
        public string Text(int from, int to)
        {
            var sb = new StringBuilder();
            for (int i = Math.Max(0, from); i < Math.Min(to, DocLines); i++)
            {
                var ln = DocLine(i);
                sb.Append(LineText(ln, 0, ln.C.Length, true));
                if (!ln.Wrapped) sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string LineText(Line ln, int from, int to, bool trimEnd)
        {
            var sb = new StringBuilder();
            for (int x = Math.Max(0, from); x < Math.Min(to, ln.C.Length); x++)
            {
                var c = ln.C[x];
                if ((c.Attr & WideTail) != 0) continue;
                if (c.Ext != null) sb.Append(c.Ext); else sb.Append(c.Ch == '\0' ? ' ' : c.Ch);
            }
            string s = sb.ToString();
            return trimEnd && !ln.Wrapped ? s.TrimEnd() : s;
        }

        // Ultime righe con del testo, fino al cursore (per l'assistente)
        public string LastLines(int count)
        {
            int end = ScreenTop + CurY + 1;
            return Text(Math.Max(0, end - count), end).TrimEnd('\n', ' ');
        }

        public void ClearScrollback()
        {
            Dropped += scroll.Count;
            scroll.Clear();
            Version++;
        }

        // ---------- dimensioni ----------

        public void Resize(int cols, int rows)
        {
            cols = Math.Max(2, cols); rows = Math.Max(1, rows);
            if (cols == Cols && rows == Rows) return;
            // sullo schermo alternativo il cursore dello schermo principale è quello salvato
            int mainY = AltActive ? sY : CurY, altY = AltActive ? CurY : 0;
            mainScreen = ResizeScreen(mainScreen, cols, rows, true, ref mainY);
            altScreen = ResizeScreen(altScreen, cols, rows, false, ref altY);
            if (AltActive) { CurY = altY; sY = mainY; } else CurY = mainY;
            screen = AltActive ? altScreen : mainScreen;
            Cols = cols; Rows = rows;
            CurX = Math.Min(CurX, Cols - 1); CurY = Math.Min(CurY, Rows - 1);
            sX = Math.Min(sX, Cols - 1); sY = Math.Min(sY, Rows - 1);
            top = 0; bottom = Rows - 1;
            wrapNext = false;
            ResetTabs();
            Version++;
        }

        Line[] ResizeScreen(Line[] old, int cols, int rows, bool toScrollback, ref int cy)
        {
            var lines = new List<Line>(old);
            foreach (var ln in lines)
                if (ln.C.Length != cols)
                {
                    var c = new Cell[cols];
                    for (int x = 0; x < cols; x++) c[x] = x < ln.C.Length ? ln.C[x] : BlankDefault;
                    ln.C = c;
                }
            // meno righe: prima si tolgono quelle vuote sotto il cursore, poi quelle in alto (vanno nella cronologia)
            while (lines.Count > rows && lines.Count - 1 > cy && LineText(lines[lines.Count - 1], 0, cols, true).Length == 0) lines.RemoveAt(lines.Count - 1);
            while (lines.Count > rows)
            {
                if (toScrollback) PushScroll(lines[0]);
                lines.RemoveAt(0);
                cy--;
            }
            while (lines.Count < rows) lines.Add(new Line(cols, BlankDefault));
            cy = Math.Max(0, Math.Min(cy, rows - 1));
            return lines.ToArray();
        }

        void PushScroll(Line ln)
        {
            scroll.Add(ln);
            if (scroll.Count > MaxScrollback + 500)
            {
                int n = scroll.Count - MaxScrollback;
                scroll.RemoveRange(0, n);
                Dropped += n;
            }
        }

        // ---------- parser ----------

        enum St { Ground, Esc, EscInter, Csi, Osc, OscEsc, Str, StrEsc, Charset }
        St state = St.Ground;
        readonly StringBuilder oscBuf = new StringBuilder();
        readonly List<int> par = new List<int>();
        readonly List<bool> parSub = new List<bool>();   // il parametro era separato da ':' (sotto-parametro)
        int curPar; bool hasCur, curSub;
        char priv, inter;
        int charsetSlot;

        public void Feed(string s)
        {
            for (int i = 0; i < s.Length; i++) Step(s[i]);
            Version++;
        }

        void Step(char c)
        {
            switch (state)
            {
                case St.Ground:
                    if (c < 0x20 || c == 0x7f) Control(c);
                    else Print(c);
                    return;
                case St.Esc: EscChar(c); return;
                case St.EscInter:
                    if (c >= 0x20 && c <= 0x2f) return;
                    state = St.Ground; return;
                case St.Charset:
                    if (charsetSlot == 0) g0 = c; else g1 = c;
                    state = St.Ground; return;
                case St.Csi: CsiChar(c); return;
                case St.Osc:
                    if (c == 7) { Osc(oscBuf.ToString()); state = St.Ground; }
                    else if (c == 0x1b) state = St.OscEsc;
                    else if (oscBuf.Length < 65536) oscBuf.Append(c);
                    return;
                case St.OscEsc:
                    if (c == '\\') { Osc(oscBuf.ToString()); state = St.Ground; }
                    else { state = St.Esc; EscChar(c); }
                    return;
                case St.Str:   // DCS, SOS, PM, APC: si ignora fino a ST
                    if (c == 0x1b) state = St.StrEsc;
                    else if (c == 7) state = St.Ground;
                    return;
                case St.StrEsc:
                    state = c == '\\' ? St.Ground : St.Str;
                    return;
            }
        }

        void Control(char c)
        {
            switch ((int)c)
            {
                case 0x07: if (Bell != null) Bell(); break;
                case 0x08: if (wrapNext) wrapNext = false; else if (CurX > 0) CurX--; break;
                case 0x09: Tab(); break;
                case 0x0a: case 0x0b: case 0x0c: LineFeed(); break;
                case 0x0d: CurX = 0; wrapNext = false; break;
                case 0x0e: shiftOut = true; break;
                case 0x0f: shiftOut = false; break;
                case 0x1b: state = St.Esc; break;
            }
        }

        void EscChar(char c)
        {
            state = St.Ground;
            switch (c)
            {
                case '[': state = St.Csi; par.Clear(); parSub.Clear(); curPar = 0; hasCur = false; curSub = false; priv = '\0'; inter = '\0'; break;
                case ']': state = St.Osc; oscBuf.Clear(); break;
                case 'P': case 'X': case '^': case '_': state = St.Str; break;
                case '(': charsetSlot = 0; state = St.Charset; break;
                case ')': charsetSlot = 1; state = St.Charset; break;
                case '*': case '+': case '-': case '.': case '/': charsetSlot = 2; state = St.Charset; break;
                case '#': case '%': case ' ': state = St.EscInter; break;
                case '7': SaveCursor(); break;
                case '8': RestoreCursor(); break;
                case 'D': LineFeed(); break;
                case 'E': CurX = 0; LineFeed(); break;
                case 'M': ReverseIndex(); break;
                case 'H': if (CurX < Cols) tabs[CurX] = true; break;
                case 'c': FullReset(); break;
                case '=': AppKeypad = true; break;
                case '>': AppKeypad = false; break;
                case '\\': break;
            }
        }

        void CsiChar(char c)
        {
            if (c >= '0' && c <= '9') { curPar = Math.Min(curPar * 10 + (c - '0'), 1000000); hasCur = true; return; }
            if (c == ';' || c == ':')
            {
                par.Add(hasCur ? curPar : -1); parSub.Add(curSub);
                curPar = 0; hasCur = false; curSub = c == ':';
                return;
            }
            if (c >= '<' && c <= '?') { priv = c; return; }
            if (c >= 0x20 && c <= 0x2f) { inter = c; return; }
            if (c < 0x20) { Control(c); return; }   // controlli dentro una sequenza: si eseguono
            if (c >= 0x40 && c <= 0x7e)
            {
                if (hasCur || par.Count > 0) { par.Add(hasCur ? curPar : -1); parSub.Add(curSub); }
                state = St.Ground;
                try { Csi(c); } catch { }
                return;
            }
            state = St.Ground;
        }

        int P(int i, int def) { return i < par.Count && par[i] > 0 ? par[i] : def; }
        int P0(int i) { return i < par.Count && par[i] >= 0 ? par[i] : 0; }

        void Csi(char f)
        {
            if (priv == '?')
            {
                if (f == 'h' || f == 'l') { for (int i = 0; i < Math.Max(1, par.Count); i++) DecMode(P0(i), f == 'h'); }
                return;
            }
            if (priv == '>' || priv == '=' || priv == '<')
            {
                if (f == 'c' && priv == '>') Send("\x1b[>0;10;1c");
                return;
            }
            if (inter == ' ' && f == 'q') { CursorShape = P0(0); return; }
            if (inter == '!' && f == 'p') { SoftReset(); return; }
            if (inter != '\0') return;
            int n;
            switch (f)
            {
                case '@': InsertChars(P(0, 1)); break;
                case 'A': CurY = Math.Max(CurY >= top ? top : 0, CurY - P(0, 1)); wrapNext = false; break;
                case 'B': case 'e': CurY = Math.Min(CurY <= bottom ? bottom : Rows - 1, CurY + P(0, 1)); wrapNext = false; break;
                case 'C': case 'a': CurX = Math.Min(Cols - 1, CurX + P(0, 1)); wrapNext = false; break;
                case 'D': CurX = Math.Max(0, CurX - P(0, 1)); wrapNext = false; break;
                case 'E': CurY = Math.Min(bottom, CurY + P(0, 1)); CurX = 0; wrapNext = false; break;
                case 'F': CurY = Math.Max(top, CurY - P(0, 1)); CurX = 0; wrapNext = false; break;
                case 'G': case '`': CurX = Clamp(P(0, 1) - 1, 0, Cols - 1); wrapNext = false; break;
                case 'H': case 'f': MoveTo(P(1, 1) - 1, P(0, 1) - 1); break;
                case 'I': for (int i = 0; i < P(0, 1); i++) Tab(); break;
                case 'Z': for (int i = 0; i < P(0, 1); i++) BackTab(); break;
                case 'J': EraseDisplay(P0(0)); break;
                case 'K': EraseLine(P0(0)); break;
                case 'L': InsertLines(P(0, 1)); break;
                case 'M': DeleteLines(P(0, 1)); break;
                case 'P': DeleteChars(P(0, 1)); break;
                case 'S': ScrollUp(top, bottom, P(0, 1)); break;
                case 'T': ScrollDown(top, bottom, P(0, 1)); break;
                case 'X': n = P(0, 1); for (int x = CurX; x < Math.Min(Cols, CurX + n); x++) screen[CurY].C[x] = Blank; wrapNext = false; break;
                case 'b': if (hasLast) for (int i = 0; i < Math.Min(P(0, 1), 10000); i++) PutCell(lastPrinted); break;
                case 'c': Send("\x1b[?61;6;22c"); break;
                case 'd': CurY = Clamp(P(0, 1) - 1, 0, Rows - 1); wrapNext = false; break;
                case 'g': if (P0(0) == 0) { if (CurX < Cols) tabs[CurX] = false; } else if (P0(0) == 3) tabs = new bool[Cols]; break;
                case 'h': case 'l': if (P0(0) == 4) InsertMode = f == 'h'; break;
                case 'm': Sgr(); break;
                case 'n':
                    if (P0(0) == 5) Send("\x1b[0n");
                    else if (P0(0) == 6) Send("\x1b[" + ((OriginMode ? CurY - top : CurY) + 1) + ";" + (CurX + 1) + "R");
                    break;
                case 'r':
                    int t = P(0, 1) - 1, b = P(1, Rows) - 1;
                    if (t < b && b < Rows) { top = t; bottom = b; MoveTo(0, 0); }
                    break;
                case 's': SaveCursor(); break;
                case 'u': RestoreCursor(); break;
                case 't': break;   // operazioni sulla finestra: ignorate
            }
        }

        void DecMode(int m, bool on)
        {
            switch (m)
            {
                case 1: AppCursor = on; break;
                case 6: OriginMode = on; MoveTo(0, 0); break;
                case 7: AutoWrap = on; break;
                case 25: CursorVisible = on; break;
                case 47: case 1047: SetAlt(on, false); break;
                case 1049: SetAlt(on, true); break;
                case 2004: BracketedPaste = on; break;
            }
        }

        void SetAlt(bool on, bool saveCursor)
        {
            if (on == AltActive) return;
            if (on)
            {
                if (saveCursor) SaveCursor();
                altScreen = NewScreen(Cols, Rows);
                screen = altScreen;
                AltActive = true;
            }
            else
            {
                screen = mainScreen;
                AltActive = false;
                if (saveCursor) RestoreCursor();
            }
            top = 0; bottom = Rows - 1;
            wrapNext = false;
        }

        void MoveTo(int x, int y)
        {
            if (OriginMode) { y += top; CurY = Clamp(y, top, bottom); }
            else CurY = Clamp(y, 0, Rows - 1);
            CurX = Clamp(x, 0, Cols - 1);
            wrapNext = false;
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

        void SaveCursor() { sX = CurX; sY = CurY; sFg = fg; sBg = bg; sAttr = attr; sOrigin = OriginMode; }
        void RestoreCursor()
        {
            CurX = Clamp(sX, 0, Cols - 1); CurY = Clamp(sY, 0, Rows - 1);
            fg = sFg; bg = sBg; attr = sAttr; OriginMode = sOrigin;
            wrapNext = false;
        }

        void SoftReset()
        {
            CursorVisible = true; OriginMode = false; AutoWrap = true; InsertMode = false; AppCursor = false;
            top = 0; bottom = Rows - 1; fg = bg = -1; attr = 0;
        }

        void FullReset()
        {
            SoftReset();
            if (AltActive) SetAlt(false, false);
            mainScreen = NewScreen(Cols, Rows); screen = mainScreen;
            CurX = CurY = 0; wrapNext = false; BracketedPaste = false;
            g0 = g1 = 'B'; shiftOut = false;
            ResetTabs();
        }

        void Send(string s) { if (Reply != null) Reply(s); }

        // ---------- testo ----------

        static readonly string DecGraphics = "◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·";

        void Print(char c)
        {
            if (char.IsHighSurrogate(c)) { pendingHigh = c; return; }
            string ext = null;
            int w = 1;
            if (char.IsLowSurrogate(c))
            {
                if (pendingHigh == '\0') return;
                ext = new string(new[] { pendingHigh, c });
                pendingHigh = '\0';
                int cp = char.ConvertToUtf32(ext, 0);
                w = cp >= 0x1F000 ? 2 : 1;
                c = ' ';
            }
            else
            {
                pendingHigh = '\0';
                // segni che si combinano col carattere prima (accenti separati, selettori di variante): si attaccano a quello
                var cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark || cat == UnicodeCategory.EnclosingMark || c == '‍' || (c >= '︀' && c <= '️'))
                {
                    int px = wrapNext ? CurX : CurX - 1;
                    if (px >= 0 && px < Cols)
                    {
                        var pc = screen[CurY].C[px];
                        if ((pc.Attr & WideTail) != 0 && px > 0) px--;
                        pc = screen[CurY].C[px];
                        pc.Ext = (pc.Ext ?? pc.Ch.ToString()) + c;
                        screen[CurY].C[px] = pc;
                    }
                    return;
                }
                if ((shiftOut ? g1 : g0) == '0' && c >= 0x60 && c <= 0x7e) c = DecGraphics[c - 0x60];
                w = IsWide(c) ? 2 : 1;
            }
            var cell = new Cell { Ch = c, Ext = ext, Fg = fg, Bg = bg, Attr = (ushort)(attr | (w == 2 ? Wide : 0)) };
            lastPrinted = cell; hasLast = true;
            PutCell(cell);
        }

        void PutCell(Cell cell)
        {
            int w = (cell.Attr & Wide) != 0 ? 2 : 1;
            if (wrapNext)
            {
                if (AutoWrap) { screen[CurY].Wrapped = true; CurX = 0; LineFeed(); }
                wrapNext = false;
            }
            if (w == 2 && CurX >= Cols - 1)
            {
                if (AutoWrap && Cols > 1) { screen[CurY].C[CurX] = Blank; screen[CurY].Wrapped = true; CurX = 0; LineFeed(); }
                else w = 1;
            }
            var ln = screen[CurY].C;
            if (InsertMode) InsertChars(w);
            FixWide(ln, CurX);
            if (w == 2) FixWide(ln, CurX + 1);
            ln[CurX] = cell;
            if (w == 2) ln[CurX + 1] = new Cell { Ch = ' ', Fg = cell.Fg, Bg = cell.Bg, Attr = (ushort)((cell.Attr & ~Wide) | WideTail) };
            CurX += w;
            if (CurX >= Cols) { CurX = Cols - 1; wrapNext = true; }
        }

        // Si scrive sopra metà di un carattere largo: l'altra metà diventa vuota
        void FixWide(Cell[] ln, int x)
        {
            if (x < 0 || x >= ln.Length) return;
            if ((ln[x].Attr & WideTail) != 0 && x > 0) ln[x - 1] = Blank;
            if ((ln[x].Attr & Wide) != 0 && x + 1 < ln.Length) ln[x + 1] = Blank;
        }

        public static bool IsWide(char c)
        {
            return (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0x303E) || (c >= 0x3041 && c <= 0x33FF) ||
                   (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xA000 && c <= 0xA4CF) ||
                   (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE30 && c <= 0xFE4F) ||
                   (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6);
        }

        void Tab()
        {
            wrapNext = false;
            int x = CurX + 1;
            while (x < Cols - 1 && !tabs[x]) x++;
            CurX = Math.Min(x, Cols - 1);
        }

        void BackTab()
        {
            int x = CurX - 1;
            while (x > 0 && !tabs[x]) x--;
            CurX = Math.Max(0, x);
            wrapNext = false;
        }

        void LineFeed()
        {
            wrapNext = false;
            if (CurY == bottom) ScrollUp(top, bottom, 1);
            else if (CurY < Rows - 1) CurY++;
        }

        void ReverseIndex()
        {
            wrapNext = false;
            if (CurY == top) ScrollDown(top, bottom, 1);
            else if (CurY > 0) CurY--;
        }

        void ScrollUp(int t, int b, int n)
        {
            n = Math.Min(n, b - t + 1);
            for (int k = 0; k < n; k++)
            {
                var gone = screen[t];
                for (int y = t; y < b; y++) screen[y] = screen[y + 1];
                screen[b] = new Line(Cols, Blank);
                if (t == 0 && !AltActive) PushScroll(gone);
            }
        }

        void ScrollDown(int t, int b, int n)
        {
            n = Math.Min(n, b - t + 1);
            for (int k = 0; k < n; k++)
            {
                for (int y = b; y > t; y--) screen[y] = screen[y - 1];
                screen[t] = new Line(Cols, Blank);
            }
        }

        void InsertLines(int n)
        {
            if (CurY < top || CurY > bottom) return;
            ScrollDown(CurY, bottom, n);
            CurX = 0; wrapNext = false;
        }

        void DeleteLines(int n)
        {
            if (CurY < top || CurY > bottom) return;
            n = Math.Min(n, bottom - CurY + 1);
            for (int k = 0; k < n; k++)
            {
                for (int y = CurY; y < bottom; y++) screen[y] = screen[y + 1];
                screen[bottom] = new Line(Cols, Blank);
            }
            CurX = 0; wrapNext = false;
        }

        void InsertChars(int n)
        {
            var ln = screen[CurY].C;
            n = Math.Min(n, Cols - CurX);
            for (int x = Cols - 1; x >= CurX + n; x--) ln[x] = ln[x - n];
            for (int x = CurX; x < CurX + n; x++) ln[x] = Blank;
            wrapNext = false;
        }

        void DeleteChars(int n)
        {
            var ln = screen[CurY].C;
            n = Math.Min(n, Cols - CurX);
            for (int x = CurX; x < Cols - n; x++) ln[x] = ln[x + n];
            for (int x = Cols - n; x < Cols; x++) ln[x] = Blank;
            wrapNext = false;
        }

        void EraseLine(int mode)
        {
            var ln = screen[CurY];
            int a = mode == 0 ? CurX : 0, b = mode == 1 ? CurX + 1 : Cols;
            for (int x = a; x < Math.Min(b, Cols); x++) ln.C[x] = Blank;
            if (mode != 1) ln.Wrapped = false;
            wrapNext = false;
        }

        void EraseDisplay(int mode)
        {
            if (mode == 3) { ClearScrollback(); return; }
            if (mode == 0)
            {
                EraseLine(0);
                for (int y = CurY + 1; y < Rows; y++) screen[y] = new Line(Cols, Blank);
            }
            else if (mode == 1)
            {
                EraseLine(1);
                for (int y = 0; y < CurY; y++) screen[y] = new Line(Cols, Blank);
            }
            else if (mode == 2)
            {
                for (int y = 0; y < Rows; y++) screen[y] = new Line(Cols, Blank);
            }
            wrapNext = false;
        }

        // ---------- colori e stile (SGR) ----------

        void Sgr()
        {
            if (par.Count == 0) { fg = bg = -1; attr = 0; return; }
            for (int i = 0; i < par.Count; i++)
            {
                int p = par[i] < 0 ? 0 : par[i];
                switch (p)
                {
                    case 0: fg = bg = -1; attr = 0; break;
                    case 1: attr |= Bold; break;
                    case 2: attr |= Faint; break;
                    case 3: attr |= Italic; break;
                    case 4: attr |= Under; if (i + 1 < par.Count && parSub[i + 1]) { if (par[i + 1] == 0) attr &= unchecked((ushort)~Under); i++; } break;
                    case 5: case 6: break;   // lampeggio: no
                    case 7: attr |= Inverse; break;
                    case 8: attr |= Hidden; break;
                    case 9: attr |= Strike; break;
                    case 21: attr |= Under; break;
                    case 22: attr &= unchecked((ushort)~(Bold | Faint)); break;
                    case 23: attr &= unchecked((ushort)~Italic); break;
                    case 24: attr &= unchecked((ushort)~Under); break;
                    case 27: attr &= unchecked((ushort)~Inverse); break;
                    case 28: attr &= unchecked((ushort)~Hidden); break;
                    case 29: attr &= unchecked((ushort)~Strike); break;
                    case 38: fg = ExtColor(ref i, fg); break;
                    case 39: fg = -1; break;
                    case 48: bg = ExtColor(ref i, bg); break;
                    case 49: bg = -1; break;
                    case 58: ExtColor(ref i, 0); break;   // colore della sottolineatura: ignorato
                    default:
                        if (p >= 30 && p <= 37) fg = p - 30;
                        else if (p >= 40 && p <= 47) bg = p - 40;
                        else if (p >= 90 && p <= 97) fg = p - 90 + 8;
                        else if (p >= 100 && p <= 107) bg = p - 100 + 8;
                        break;
                }
            }
        }

        // 38;5;n  38;2;r;g;b  38:5:n  38:2::r:g:b  38:2:r:g:b
        int ExtColor(ref int i, int keep)
        {
            if (i + 1 < par.Count && parSub[i + 1])
            {
                var subs = new List<int>();
                int j = i + 1;
                while (j < par.Count && parSub[j]) { subs.Add(par[j]); j++; }
                i = j - 1;
                if (subs.Count >= 2 && subs[0] == 5) return Math.Max(0, Math.Min(255, subs[1]));
                if (subs.Count >= 4 && subs[0] == 2)
                {
                    int n = subs.Count;
                    return Rgb | (Byte(subs[n - 3]) << 16) | (Byte(subs[n - 2]) << 8) | Byte(subs[n - 1]);
                }
                return keep;
            }
            if (i + 2 < par.Count && par[i + 1] == 5) { int v = par[i + 2]; i += 2; return Math.Max(0, Math.Min(255, v)); }
            if (i + 4 < par.Count && par[i + 1] == 2)
            {
                int v = Rgb | (Byte(par[i + 2]) << 16) | (Byte(par[i + 3]) << 8) | Byte(par[i + 4]);
                i += 4;
                return v;
            }
            return keep;
        }

        static int Byte(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        // ---------- OSC: titolo e cartella corrente ----------

        void Osc(string s)
        {
            int semi = s.IndexOf(';');
            string code = semi < 0 ? s : s.Substring(0, semi), arg = semi < 0 ? "" : s.Substring(semi + 1);
            if (code == "0" || code == "2")
            {
                Title = arg;
                if (TitleChanged != null) TitleChanged();
            }
            else if (code == "9" && arg.StartsWith("9;"))
            {
                SetCwd(arg.Substring(2).Trim('"'));
            }
            else if (code == "7")
            {
                // file://host/percorso (shell Linux): si tiene il percorso così com'è
                try
                {
                    var u = new Uri(arg);
                    if (u.Scheme == "file") SetCwd(Uri.UnescapeDataString(u.AbsolutePath));
                }
                catch { }
            }
        }

        void SetCwd(string dir)
        {
            if (string.IsNullOrEmpty(dir) || dir == Cwd) return;
            Cwd = dir;
            if (CwdChanged != null) CwdChanged();
        }

        // Per l'interprete Lisp, che non manda sequenze OSC: imposta la cartella direttamente
        public void SetDirectory(string dir) { lock (Sync) SetCwd(dir); }
    }
}
