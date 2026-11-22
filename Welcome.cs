// Presentazione per chi usa CorbolPrompt per la prima volta: 4 pagine (schede e shell, assistente, preferiti, Lisp).
// Compare una volta sola al primo avvio (setting welcome=1) e si riapre dal menu ⋯ → Benvenuto.
// Le illustrazioni sono disegnate qui, con i colori dell'app e i testi nella sua lingua. Come in CorbolIp.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CorbolPrompt
{
    // Guida: la wiki su GitHub nella lingua dell'app (italiano, altrimenti inglese)
    static class Help
    {
        const string Wiki = "https://github.com/corbolma/CorbolPrompt/wiki/";

        public static string Page(string it, string en) { return Wiki + (L.Code == "it" ? it : en); }

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }

    class WelcomeForm : Form
    {
        static WelcomeForm open;

        public static void ShowFor(Form owner)
        {
            if (open == null || open.IsDisposed) open = new WelcomeForm(owner.Icon);
            if (!open.Visible) open.Show(owner);
            open.Activate();
        }

        const int Pages = 4;
        readonly Panel art;
        readonly Label lblTitle, lblText;
        readonly Button btnBack, btnNext;
        readonly LinkLabel lnkSkip, lnkGuide;
        int page;

        static readonly Color ArtBack = Color.FromArgb(243, 245, 248), Accent = Color.FromArgb(22, 163, 74);
        static readonly Color TermBack = Color.FromArgb(12, 12, 12), Bar = Color.FromArgb(32, 32, 32), Fg = Color.FromArgb(204, 204, 204);

        WelcomeForm(Icon icon)
        {
            Text = L.T("Benvenuto in CorbolPrompt", "Welcome to CorbolPrompt");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            if (icon != null) Icon = icon;

            art = new DoubleBufferedPanel { Location = new Point(0, 0), Size = new Size(560, 230), BackColor = ArtBack };
            art.Paint += (s, e) => DrawArt(e.Graphics, art.ClientSize);
            lblTitle = new Label { AutoSize = true, Location = new Point(24, 246), Font = new Font("Segoe UI Semibold", 12f) };
            lblText = new Label { AutoSize = false, Location = new Point(24, 282), Size = new Size(512, 150), Font = new Font("Segoe UI", 10.5f) };
            lnkGuide = new LinkLabel { Text = L.T("Apri la guida completa", "Open the full guide"), AutoSize = false, Location = new Point(24, 438), Size = new Size(260, 26),
                                       TextAlign = ContentAlignment.MiddleLeft, LinkBehavior = LinkBehavior.AlwaysUnderline, Visible = false, LinkColor = Accent, ActiveLinkColor = Accent };
            lnkGuide.LinkClicked += delegate { Help.Open(Help.Page("Guida-utente", "User-guide")); };
            lnkSkip = new LinkLabel { Text = L.T("Salta", "Skip"), AutoSize = false, Location = new Point(24, 485), Size = new Size(200, 26),
                                      TextAlign = ContentAlignment.MiddleLeft, LinkBehavior = LinkBehavior.AlwaysUnderline, LinkColor = Accent, ActiveLinkColor = Accent };
            lnkSkip.LinkClicked += delegate { Close(); };
            btnBack = new Button { Text = L.T("Indietro", "Back"), Location = new Point(332, 482), Size = new Size(100, 32) };
            btnNext = new Button { Location = new Point(440, 482), Size = new Size(100, 32) };
            btnBack.Click += delegate { if (page > 0) { page--; ShowPage(); } };
            btnNext.Click += delegate { if (page < Pages - 1) { page++; ShowPage(); } else Close(); };
            AcceptButton = btnNext;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Right && page < Pages - 1) { page++; ShowPage(); }
                else if (e.KeyCode == Keys.Left && page > 0) { page--; ShowPage(); }
            };
            Controls.AddRange(new Control[] { art, lblTitle, lblText, lnkGuide, lnkSkip, btnBack, btnNext });
            ClientSize = new Size(560, 528);
            ShowPage();
            Shown += delegate { btnNext.Focus(); };
        }

        void ShowPage()
        {
            btnBack.Enabled = page > 0;
            btnNext.Text = page < Pages - 1 ? L.T("Avanti", "Next") : L.T("Inizia", "Start");
            lnkSkip.Visible = page < Pages - 1;
            lnkGuide.Visible = page == Pages - 1;
            switch (page)
            {
                case 0:
                    lblTitle.Text = L.T("Schede e shell", "Tabs and shells");
                    lblText.Text = L.T("Ogni scheda è un terminale vero: PowerShell, Prompt dei comandi, WSL (Linux), Git Bash o il Lisp integrato. " +
                                       "Il + apre la shell predefinita, la freccia accanto fa scegliere quale. Alla riapertura tornano le schede di prima, " +
                                       "nelle loro cartelle. Tasto destro sul terminale per copiare, incollare e chiedere all'assistente.",
                                       "Each tab is a real terminal: PowerShell, Command Prompt, WSL (Linux), Git Bash or the built-in Lisp. " +
                                       "The + opens the default shell, the arrow next to it lets you choose. When you reopen CorbolPrompt the tabs come back, " +
                                       "in their folders. Right-click the terminal to copy, paste and ask the assistant.");
                    break;
                case 1:
                    lblTitle.Text = L.T("L'assistente", "The assistant");
                    lblText.Text = L.T("A destra c'è l'assistente IA: chiedigli un comando o di spiegare un errore. Vede la shell, la cartella e le ultime righe " +
                                       "dello schermo. I comandi che propone diventano pulsanti ▶: li esegui tu, con un clic, solo se vuoi. " +
                                       "Il servizio (Claude Code, API, assistente locale) si sceglie in alto nel pannello.",
                                       "On the right there is the AI assistant: ask it for a command or to explain an error. It sees the shell, the folder and the last " +
                                       "lines of the screen. The commands it suggests become ▶ buttons: you run them, with a click, only if you want. " +
                                       "Choose the service (Claude Code, API, local assistant) at the top of the panel.");
                    break;
                case 2:
                    lblTitle.Text = L.T("I preferiti", "Favorites");
                    lblText.Text = L.T("Salva i comandi che usi spesso: selezionali nel terminale, tasto destro → Aggiungi ai preferiti, oppure dal tasto destro " +
                                       "su un comando dell'assistente (la descrizione si scrive da sola). Sotto il terminale compaiono i preferiti della shell " +
                                       "che stai usando: clic per eseguire, Maiusc+clic per incollare.",
                                       "Save the commands you use often: select them in the terminal, right click → Add to favorites, or right-click " +
                                       "a command from the assistant (the description writes itself). Below the terminal you see the favorites for the shell " +
                                       "you are using: click to run, Shift+click to paste.");
                    break;
                default:
                    lblTitle.Text = L.T("Il Lisp", "Lisp");
                    lblText.Text = L.T("La scheda Lisp è un piccolo Common Lisp: defun, loop, format, numeri grandi quanto vuoi, e comandi per il sistema " +
                                       "come (sh \"dir\"), (cd \"..\") e (ls). Frecce per la cronologia, Tab per completare, Ctrl+C per interrompere. " +
                                       "Scrivi (help) per un riepilogo; l'assistente conosce questo dialetto.",
                                       "The Lisp tab is a small Common Lisp: defun, loop, format, numbers as big as you like, and system commands " +
                                       "like (sh \"dir\"), (cd \"..\") and (ls). Arrows for history, Tab to complete, Ctrl+C to interrupt. " +
                                       "Type (help) for a summary; the assistant knows this dialect.");
                    break;
            }
            art.Invalidate();
        }

        // ---------- illustrazioni ----------

        void DrawArt(Graphics g, Size sz)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            float u = sz.Height / 230f;
            switch (page)
            {
                case 0: DrawTabs(g, sz, u); break;
                case 1: DrawAssistant(g, sz, u); break;
                case 2: DrawFavorites(g, sz, u); break;
                default: DrawLisp(g, sz, u); break;
            }
            float d = 7 * u, gap = 8 * u, w = Pages * d + (Pages - 1) * gap, x = (sz.Width - w) / 2, y = sz.Height - 14 * u;
            for (int i = 0; i < Pages; i++)
                using (var br = new SolidBrush(i == page ? Accent : Color.FromArgb(200, 205, 212)))
                    g.FillEllipse(br, x + i * (d + gap), y, d, d);
        }

        // Finestra scura con ombra: restituisce l'area dello schermo del terminale
        RectangleF Window(Graphics g, RectangleF r, float u, string[] tabs, int active)
        {
            using (var sh = new SolidBrush(Color.FromArgb(45, 0, 0, 0))) g.FillRectangle(sh, r.X + 4 * u, r.Y + 4 * u, r.Width, r.Height);
            using (var b = new SolidBrush(Bar)) g.FillRectangle(b, r);
            float th = 26 * u, x = r.X + 6 * u;
            using (var f = new Font("Segoe UI", 8.5f))
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    float w = g.MeasureString(tabs[i], f).Width + 22 * u;
                    var tr = new RectangleF(x, r.Y + 4 * u, w, th - 4 * u);
                    if (i == active) using (var b = new SolidBrush(TermBack)) g.FillRectangle(b, tr);
                    using (var b = new SolidBrush(i == active ? Color.White : Color.FromArgb(190, 190, 190)))
                        g.DrawString(tabs[i], f, b, tr.X + 8 * u, tr.Y + (tr.Height - f.GetHeight(g)) / 2);
                    x += w;
                }
                using (var p = new Pen(Color.FromArgb(190, 190, 190), 1.3f * u))
                {
                    float cx = x + 12 * u, cy = r.Y + 4 * u + (th - 4 * u) / 2;
                    g.DrawLine(p, cx - 5 * u, cy, cx + 5 * u, cy); g.DrawLine(p, cx, cy - 5 * u, cx, cy + 5 * u);
                    float ax = cx + 18 * u;
                    g.DrawLines(p, new[] { new PointF(ax - 4 * u, cy - 2 * u), new PointF(ax, cy + 2 * u), new PointF(ax + 4 * u, cy - 2 * u) });
                }
            }
            var screen = new RectangleF(r.X, r.Y + th, r.Width, r.Height - th);
            using (var b = new SolidBrush(TermBack)) g.FillRectangle(b, screen);
            return screen;
        }

        void Lines(Graphics g, RectangleF screen, float u, params KeyValuePair<string, Color>[] lines)
        {
            var clip = g.Clip;
            g.SetClip(screen);   // le righe lunghe restano dentro lo schermo
            using (var f = new Font("Consolas", 9f))
            {
                float y = screen.Y + 6 * u, lh = f.GetHeight(g) * 1.05f;
                foreach (var l in lines)
                {
                    using (var b = new SolidBrush(l.Value)) g.DrawString(l.Key, f, b, screen.X + 8 * u, y);
                    y += lh;
                }
            }
            g.Clip = clip;
        }

        static KeyValuePair<string, Color> Ln(string s, Color c) { return new KeyValuePair<string, Color>(s, c); }

        void DrawTabs(Graphics g, Size sz, float u)
        {
            var r = new RectangleF(40 * u, 14 * u, sz.Width - 80 * u, 180 * u);
            var screen = Window(g, r, u, new[] { "PowerShell · ~", "mauro@pc: ~", "Lisp" }, 0);
            Lines(g, screen, u,
                Ln("PS C:\\Users\\mauro> git status", Fg),
                Ln("On branch main", Fg),
                Ln("Changes not staged for commit:", Fg),
                Ln("        modified:   README.md", Color.FromArgb(231, 72, 86)),
                Ln("PS C:\\Users\\mauro> ", Fg));
            // menu della freccia: le shell
            var items = new[] { "PowerShell", "Windows PowerShell", L.T("Prompt dei comandi", "Command Prompt"), "WSL (Linux)", "Lisp (CorbolPrompt)" };
            using (var f = new Font("Segoe UI", 8.5f))
            {
                float lh = f.GetHeight(g) * 1.3f, w = 0;
                foreach (var it in items) w = Math.Max(w, g.MeasureString(it, f).Width);
                w += 26 * u;
                var m = new RectangleF(r.X + 250 * u, r.Y + 24 * u, w, items.Length * lh + 8 * u);
                using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(sh, m.X + 3 * u, m.Y + 3 * u, m.Width, m.Height);
                g.FillRectangle(Brushes.White, m);
                using (var pen = new Pen(Color.FromArgb(200, 205, 212))) g.DrawRectangle(pen, m.X, m.Y, m.Width, m.Height);
                float y = m.Y + 4 * u;
                for (int i = 0; i < items.Length; i++)
                {
                    if (i == 3) using (var hb = new SolidBrush(Color.FromArgb(220, 240, 226))) g.FillRectangle(hb, m.X + 2 * u, y, m.Width - 4 * u, lh);
                    g.DrawString(items[i], f, Brushes.Black, m.X + 12 * u, y + (lh - f.GetHeight(g)) / 2);
                    y += lh;
                }
            }
        }

        void DrawAssistant(Graphics g, Size sz, float u)
        {
            var r = new RectangleF(24 * u, 14 * u, 270 * u, 180 * u);
            var screen = Window(g, r, u, new[] { L.T("Prompt dei comandi", "Command Prompt") }, 0);
            Lines(g, screen, u,
                Ln("C:\\progetto> git statuss", Fg),
                Ln("git: 'statuss' is not a git", Color.FromArgb(231, 72, 86)),
                Ln("command. See 'git --help'.", Color.FromArgb(231, 72, 86)),
                Ln("C:\\progetto> git status", Fg),
                Ln("On branch main", Fg));
            // pannello dell'assistente
            var p = new RectangleF(r.Right + 14 * u, r.Y, sz.Width - r.Right - 38 * u, r.Height);
            using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(sh, p.X + 4 * u, p.Y + 4 * u, p.Width, p.Height);
            g.FillRectangle(Brushes.White, p);
            using (var f = new Font("Segoe UI", 8.5f))
            using (var fb = new Font("Segoe UI", 8.5f, FontStyle.Bold))
            {
                float x = p.X + 10 * u, y = p.Y + 8 * u, lh = f.GetHeight(g);
                g.DrawString(L.T("Assistente", "Assistant"), fb, Brushes.Black, x, y); y += lh * 1.5f;
                using (var b = new SolidBrush(Color.FromArgb(37, 99, 235))) g.DrawString(L.T("Tu", "You"), fb, b, x, y); y += lh;
                g.DrawString(L.T("perché questo errore?", "why this error?"), f, Brushes.Black, x, y); y += lh * 1.4f;
                using (var b = new SolidBrush(Color.FromArgb(22, 130, 12))) g.DrawString(L.T("Assistente", "Assistant"), fb, b, x, y); y += lh;
                g.DrawString(L.T("Un refuso: il comando", "A typo: the right"), f, Brushes.Black, x, y); y += lh;
                g.DrawString(L.T("giusto è git status.", "command is git status."), f, Brushes.Black, x, y);
                var bt = new RectangleF(x, p.Bottom - 34 * u, p.Width - 20 * u, 24 * u);
                using (var b = new SolidBrush(Color.FromArgb(240, 247, 240))) g.FillRectangle(b, bt);
                using (var pen = new Pen(Color.FromArgb(170, 210, 170))) g.DrawRectangle(pen, bt.X, bt.Y, bt.Width, bt.Height);
                using (var fm = new Font("Consolas", 8.5f)) g.DrawString("▶  git status", fm, Brushes.Black, bt.X + 6 * u, bt.Y + (bt.Height - fm.GetHeight(g)) / 2);
            }
        }

        void DrawFavorites(Graphics g, Size sz, float u)
        {
            var r = new RectangleF(40 * u, 14 * u, sz.Width - 80 * u, 180 * u);
            var screen = Window(g, r, u, new[] { "PowerShell · progetto" }, 0);
            Lines(g, screen, u, Ln("PS C:\\progetto> ", Fg));
            // barra dei preferiti in fondo
            float bh = 28 * u;
            var bar = new RectangleF(screen.X, screen.Bottom - bh, screen.Width, bh);
            using (var b = new SolidBrush(Bar)) g.FillRectangle(b, bar);
            using (var f = new Font("Segoe UI", 8.5f))
            {
                float x = bar.X + 8 * u;
                using (var b = new SolidBrush(Accent)) g.DrawString("★", f, b, x, bar.Y + (bh - f.GetHeight(g)) / 2);
                x += 18 * u;
                foreach (var name in new[] { L.T("Stato git", "Git status"), L.T("File più grandi", "Largest files"), L.T("Spazio libero", "Free space") })
                {
                    float w = g.MeasureString(name, f).Width + 14 * u;
                    var c = new RectangleF(x, bar.Y + 4 * u, w, bh - 8 * u);
                    using (var b = new SolidBrush(Color.FromArgb(45, 45, 45))) g.FillRectangle(b, c);
                    using (var pen = new Pen(Color.FromArgb(75, 75, 75))) g.DrawRectangle(pen, c.X, c.Y, c.Width, c.Height);
                    using (var b = new SolidBrush(Color.FromArgb(240, 240, 240))) g.DrawString(name, f, b, c.X + 7 * u, c.Y + (c.Height - f.GetHeight(g)) / 2);
                    x += w + 6 * u;
                }
                // suggerimento con la descrizione
                string tip = L.T("I 10 file più grandi della cartella", "The 10 largest files in the folder");
                float tw = g.MeasureString(tip, f).Width + 16 * u;
                var t = new RectangleF(screen.X + 110 * u, bar.Y - 40 * u, tw, f.GetHeight(g) + 10 * u);
                g.FillRectangle(Brushes.White, t);
                using (var pen = new Pen(Color.FromArgb(120, 120, 120))) g.DrawRectangle(pen, t.X, t.Y, t.Width, t.Height);
                g.DrawString(tip, f, Brushes.Black, t.X + 8 * u, t.Y + 5 * u);
            }
        }

        void DrawLisp(Graphics g, Size sz, float u)
        {
            var r = new RectangleF(40 * u, 14 * u, sz.Width - 80 * u, 180 * u);
            var screen = Window(g, r, u, new[] { "Lisp · ~" }, 0);
            Color green = Color.FromArgb(22, 198, 12), cyan = Color.FromArgb(97, 214, 214);
            Lines(g, screen, u,
                Ln("λ> (defun fact (n) (if (= n 0) 1 (* n (fact (- n 1)))))", Fg),
                Ln("fact", cyan),
                Ln("λ> (fact 30)", Fg),
                Ln("265252859812191058636308480000000", cyan),
                Ln("λ> (remove-if-not #'evenp (range 12))", Fg),
                Ln("(0 2 4 6 8 10)", cyan),
                Ln("λ> ", green));
        }

        class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel() { DoubleBuffered = true; ResizeRedraw = true; }
        }
    }
}
