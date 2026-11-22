// CorbolPrompt - terminale a schede per Windows con un assistente IA e un interprete Lisp.
// Ogni scheda è una shell vera (PowerShell, cmd, WSL, Git Bash) in una console virtuale (ConPTY) o il Lisp integrato.
// L'assistente vede la shell, la cartella e (se permesso) le ultime righe dello schermo, e propone comandi
// che l'utente esegue con un clic.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CorbolPrompt
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);

        public const string AppUserModelId = "CorbolPrompt.App";

        [STAThread]
        static void Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(L.T("Si è verificato un errore:", "An error occurred:") + "\n" + e.Exception.Message, "CorbolPrompt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Settings.Load();
            L.Set(Settings.Get("lang", "auto"));
            // "CorbolPrompt.exe <cartella>" apre una scheda in quella cartella
            string dir = args.FirstOrDefault(a => !a.StartsWith("/") && Directory.Exists(a));
            Application.Run(new MainForm(dir));
        }
    }

    // Impostazioni in %APPDATA%\CorbolPrompt\settings.txt, righe "chiave=valore"
    static class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CorbolPrompt");
        static readonly string Path_ = Path.Combine(Dir, "settings.txt");
        static readonly Dictionary<string, string> values = new Dictionary<string, string>();

        public static void Load()
        {
            try
            {
                if (!File.Exists(Path_)) return;
                foreach (var ln in File.ReadAllLines(Path_, Encoding.UTF8))
                {
                    int i = ln.IndexOf('=');
                    if (i > 0) values[ln.Substring(0, i).Trim()] = ln.Substring(i + 1).Trim();
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(Path_, values.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value), new UTF8Encoding(false));
            }
            catch { }
        }

        public static string Get(string key, string def) { string v; return values.TryGetValue(key, out v) ? v : def; }
        public static int Get(string key, int def) { int v; return int.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; }
        public static bool Get(string key, bool def) { string v = Get(key, ""); return v == "1" ? true : v == "0" ? false : def; }
        public static void Set(string key, string v) { values[key] = v ?? ""; }
        public static void Set(string key, int v) { values[key] = v.ToString(CultureInfo.InvariantCulture); }
        public static void Set(string key, bool v) { values[key] = v ? "1" : "0"; }
    }

    // Una scheda: la shell, il suo schermo e la conversazione con l'assistente
    class TermTab
    {
        public ShellInfo Shell;
        public Terminal Term;
        public TermView View;
        public ISession Session;
        public bool Exited;
        public readonly List<AiMessage> History = new List<AiMessage>();
        public RichTextBox Chat;
        public List<Prompt.Block> Commands = new List<Prompt.Block>();
        public CancellationTokenSource AiCancel;
        public DateTime AiStart;
        public string InputText = "";
        public string CustomName = "";   // nome dato dall'utente; vuoto = nome automatico (shell e cartella)

        public bool Busy { get { return AiCancel != null; } }

        public string Cwd { get { lock (Term.Sync) return Term.Cwd; } }

        public string Title
        {
            get
            {
                if (CustomName.Length > 0) return CustomName;
                return AutoTitle;
            }
        }

        public string AutoTitle
        {
            get
            {
                string t, cwd;
                lock (Term.Sync) { t = Term.Title; cwd = Term.Cwd; }
                // il titolo dato dal programma (es. "utente@pc: ~/cartella" in WSL), se non è il percorso dell'exe
                if (!Shell.IsLisp && !string.IsNullOrWhiteSpace(t) && !t.Contains(":\\") && !t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return t.Trim().Length > 48 ? t.Trim().Substring(0, 47) + "…" : t.Trim();
                if (cwd == null) return Shell.Short;
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string folder = string.Equals(cwd.TrimEnd('\\'), home, StringComparison.OrdinalIgnoreCase) ? "~" : Path.GetFileName(cwd.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(folder)) folder = cwd;
                return Shell.Short + " · " + folder;
            }
        }

        public string Tooltip
        {
            get { string cwd = Cwd; return (CustomName.Length > 0 ? CustomName + "\n" : "") + Shell.Name + (cwd != null ? "\n" + cwd : "") + (Exited ? "\n" + L.T("(terminato)", "(exited)") : ""); }
        }
    }

    // Barra in alto scura come le schede
    class DarkRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) { base.OnRenderToolStripBackground(e); return; }
            using (var b = new SolidBrush(TabStrip.TabBar)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
        }

        void Hot(ToolStripItemRenderEventArgs e)
        {
            var btn = e.Item as ToolStripButton;
            bool on = e.Item.Selected || e.Item.Pressed || (btn != null && btn.Checked);
            if (!on) return;
            using (var b = new SolidBrush(btn != null && btn.Checked && !e.Item.Selected ? TabStrip.TabHover : TabStrip.Hover))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) { base.OnRenderButtonBackground(e); return; }
            Hot(e);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown) { base.OnRenderDropDownButtonBackground(e); return; }
            Hot(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!(e.ToolStrip is ToolStripDropDown)) e.TextColor = TabStrip.Text_;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (e.Item != null && !(e.Item.Owner is ToolStripDropDown)) e.ArrowColor = TabStrip.TextDim;
            base.OnRenderArrow(e);
        }
    }

    class MainForm : Form
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly TabStrip tabStrip = new TabStrip { Dock = DockStyle.Fill };
        readonly Panel topBar = new Panel { Dock = DockStyle.Top, BackColor = TabStrip.TabBar };
        readonly ToolStrip tools = new ToolStrip { Dock = DockStyle.None, GripStyle = ToolStripGripStyle.Hidden, AutoSize = true, Padding = new Padding(0, 0, 4, 0), CanOverflow = false, LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow };
        readonly SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, BackColor = TabStrip.TabBar, SplitterWidth = 4 };
        readonly Panel termHost = new Panel { Dock = DockStyle.Fill, BackColor = TermView.Background };
        // preferiti adatti alla shell della scheda attiva, come suggerimenti sotto il terminale
        readonly FlowLayoutPanel favBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, WrapContents = false, AutoScroll = false, BackColor = TabStrip.TabBar, Padding = new Padding(6, 3, 6, 3), Visible = false };
        readonly ContextMenuStrip tabMenu = new ContextMenuStrip(), listMenu = new ContextMenuStrip();
        ContextMenuStrip termMenu = new ContextMenuStrip();
        readonly ToolTip tips = new ToolTip();
        ToolStripButton tbAi;
        ToolStripDropDownButton more, tbFav;
        readonly ContextMenuStrip newMenu = new ContextMenuStrip();

        // pannello dell'assistente
        readonly Label aiTitle = new Label { AutoSize = true, Margin = new Padding(0) };
        readonly Label aiService = new Label { AutoSize = false, AutoEllipsis = true, ForeColor = Color.DimGray, Margin = new Padding(6, 0, 0, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        readonly Panel chatHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly FlowLayoutPanel cmdPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(0, 4, 0, 0) };
        readonly Label lbBusy = new Label { Dock = DockStyle.Bottom, Height = 0, ForeColor = Color.DimGray, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly TextBox input = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = false };
        readonly Button btSend = new Button(), btNewChat = new Button();
        readonly System.Windows.Forms.Timer busyTimer = new System.Windows.Forms.Timer { Interval = 1000 };

        TermTab cur;
        List<TermTab> Tabs { get { return tabStrip.Docs; } }
        float fontSize;
        bool closing;
        readonly bool ownsSession;   // la finestra aperta senza cartella riapre e salva le schede; quelle aperte su una cartella no

        public MainForm(string startDir)
        {
            Text = "CorbolPrompt";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Font = new Font("Segoe UI", 9f);
            BackColor = TabStrip.TabBar;
            KeyPreview = false;
            StartPosition = FormStartPosition.Manual;
            fontSize = Settings.Get("font.size", 11);
            RestoreBounds_();

            // ---- barra in alto: schede + pulsanti ----
            tools.Renderer = new DarkRenderer();
            tools.BackColor = TabStrip.TabBar;
            ownsSession = startDir == null;

            tbAi = new ToolStripButton("") { CheckOnClick = false };
            tbAi.Click += delegate { ToggleAssistant(); };
            more = new ToolStripDropDownButton("⋯") { ShowDropDownArrow = false, Font = new Font("Segoe UI", 11f) };
            tbFav = new ToolStripDropDownButton("") { ShowDropDownArrow = false };
            tbFav.DropDown.ShowItemToolTips = true;
            tbFav.DropDownOpening += delegate { FillFavMenu(); };
            tbFav.DropDownItems.Add("-");   // si riempie all'apertura
            tools.Items.AddRange(new ToolStripItem[] { tbFav, tbAi, more });
            // barra in alto e menu un po' più grandi del resto dell'interfaccia
            var big = new Font("Segoe UI", 10f);
            tools.Font = big;
            more.Font = new Font("Segoe UI", 12f);
            foreach (ToolStripDropDown dd in new ToolStripDropDown[] { tbFav.DropDown, more.DropDown, newMenu, tabMenu, listMenu }) dd.Font = big;
            topBar.Height = tabStrip.Height;
            topBar.Controls.Add(tabStrip);
            topBar.Controls.Add(tools);
            tools.BringToFront();
            // i pulsanti a destra; le schede occupano il resto
            EventHandler place = delegate
            {
                tools.Location = new Point(topBar.ClientSize.Width - tools.Width, Math.Max(0, (topBar.ClientSize.Height - tools.Height) / 2));
                topBar.Padding = new Padding(0, 0, tools.Width + 4, 0);
            };
            topBar.Resize += place;
            tools.SizeChanged += place;
            tabStrip.TabSelected += t => Activate(t);
            tabStrip.TabCloseRequested += t => CloseTab(t);
            tabStrip.NewRequested += delegate { NewTab(ShellInfo.Default, cur != null ? cur.Cwd : null); };
            tabStrip.TabContextMenu += ShowTabMenu;
            tabStrip.TabRenameRequested += BeginRename;
            tabStrip.ListRequested += ShowTabList;
            tabStrip.NewMenuRequested += p => { FillNewMenu(newMenu.Items); newMenu.Show(tabStrip, p); };

            // ---- pannello dell'assistente ----
            BuildAssistantPanel();
            split.Panel1.Controls.Add(termHost);
            split.Panel1.Controls.Add(favBar);
            favBar.Height = S(34);
            Controls.Add(split);
            Controls.Add(topBar);
            ApplyTexts();
            // aggiornamento trovato: compare la voce nel menu ⋯
            Action onUpdate = delegate { try { BeginInvoke((Action)ApplyTexts); } catch { } };
            Updater.Available += onUpdate;
            FormClosed += delegate { Updater.Available -= onUpdate; };

            Load += delegate
            {
                split.SplitterDistance = Math.Max(200, split.Width - S(Settings.Get("ai.width", 400)));
                split.Panel2Collapsed = !Settings.Get("ai.visible", true);
                tbAi.Checked = !split.Panel2Collapsed;
                if (startDir != null || !RestoreSession()) NewTab(ShellInfo.Default, startDir);
                UpdateService();
                Updater.CheckIfDue(this);
                // presentazione al primo avvio (una volta sola)
                if (!Settings.Get("welcome", false))
                {
                    Settings.Set("welcome", true);
                    Settings.Save();
                    var wt = new System.Windows.Forms.Timer { Interval = 700 };
                    wt.Tick += delegate { wt.Stop(); wt.Dispose(); WelcomeForm.ShowFor(this); };
                    wt.Start();
                }
            };
            HandleCreated += delegate
            {
                // barra del titolo scura (Windows 10 20H1 e successivi)
                try { int on = 1; if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(Handle, 19, ref on, 4); } catch { }
            };
            Activated += delegate { UpdateService(); };
            FormClosing += (s, e) => OnClosingForm();
            busyTimer.Tick += delegate { UpdateBusy(); };
        }

        int S(int v) { return (int)Math.Round(v * DeviceDpi / 96.0); }

        // ---------- finestra ----------

        void RestoreBounds_()
        {
            var parts = Settings.Get("window", "").Split(',');
            int x, y, w, h;
            if (parts.Length >= 4 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h))
            {
                var r = new Rectangle(x, y, Math.Max(400, w), Math.Max(300, h));
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) { Bounds = r; if (parts.Length > 4 && parts[4] == "max") WindowState = FormWindowState.Maximized; return; }
            }
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(wa.Width - 80, S(1280)), Math.Min(wa.Height - 80, S(780)));
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }

        void OnClosingForm()
        {
            closing = true;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            Settings.Set("window", b.X + "," + b.Y + "," + b.Width + "," + b.Height + (WindowState == FormWindowState.Maximized ? ",max" : ""));
            if (!split.Panel2Collapsed) Settings.Set("ai.width", (int)Math.Round((split.Width - split.SplitterDistance) * 96.0 / DeviceDpi));
            Settings.Set("ai.visible", !split.Panel2Collapsed);
            if (ownsSession) SaveSession();
            Settings.Save();
            foreach (var t in Tabs.ToList())
            {
                try { if (t.AiCancel != null) t.AiCancel.Cancel(); } catch { }
                try { t.Session.Close(); } catch { }
            }
        }

        // ---------- sessione: le schede si riaprono al prossimo avvio ----------

        void SaveSession()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                var lines = Tabs.Where(t => !t.Exited).Select(t => t.Shell.Id + "\t" + (t.Cwd ?? "") + "\t" + t.CustomName.Replace("\t", " ")).ToList();
                File.WriteAllLines(Path.Combine(Settings.Dir, "session.txt"), lines, new UTF8Encoding(false));
                Settings.Set("session.active", Math.Max(0, Tabs.Where(t => !t.Exited).ToList().IndexOf(cur)));
            }
            catch { }
        }

        bool RestoreSession()
        {
            try
            {
                string f = Path.Combine(Settings.Dir, "session.txt");
                if (!File.Exists(f)) return false;
                TermTab first = null;
                var opened = new List<TermTab>();
                foreach (var ln in File.ReadAllLines(f, Encoding.UTF8))
                {
                    var p = ln.Split('\t');
                    var shell = ShellInfo.Find(p[0]);
                    if (shell == null) continue;
                    string dir = p.Length > 1 && p[1].Length > 0 && !p[1].StartsWith("/") ? p[1] : null;
                    var t = NewTab(shell, dir);
                    if (t != null && p.Length > 2) t.CustomName = p[2].Trim();
                    if (t != null) { opened.Add(t); if (first == null) first = t; }
                }
                if (opened.Count == 0) return false;
                int a = Settings.Get("session.active", 0);
                Activate(a >= 0 && a < opened.Count ? opened[a] : first);
                return true;
            }
            catch { return false; }
        }

        // ---------- schede ----------

        TermTab NewTab(ShellInfo shell, string cwd)
        {
            var term = new Terminal(120, 30);
            var tab = new TermTab { Shell = shell, Term = term };
            var view = new TermView(term, fontSize) { Dock = DockStyle.Fill, Visible = false, ContextMenuStrip = termMenu };
            tab.View = view;
            termHost.Controls.Add(view);
            view.Changed += delegate { if (tab == cur) UpdateTitle(); tabStrip.Invalidate(); };
            view.ZoomRequested += n => Zoom(n);
            term.Reply += s => { var ses = tab.Session; if (ses != null) ses.Send(s); };
            term.CwdChanged += delegate { };
            Tabs.Insert(cur != null ? Tabs.IndexOf(cur) + 1 : Tabs.Count, tab);
            Activate(tab);
            StartSession(tab, cwd);
            return tab;
        }

        void StartSession(TermTab tab, string cwd)
        {
            ISession s = tab.Shell.IsLisp ? (ISession)new LispSession(tab.Term, cwd) : new PtySession(tab.Shell, cwd);
            s.Output += text =>
            {
                lock (tab.Term.Sync) tab.Term.Feed(text);
                tab.View.OutputArrived();
            };
            s.Exited += code =>
            {
                try { BeginInvoke((Action)(() => OnExited(tab, code))); } catch { }
            };
            tab.Session = s;
            tab.View.Session = s;
            tab.Exited = false;
            int cols, rows;
            lock (tab.Term.Sync) { cols = tab.Term.Cols; rows = tab.Term.Rows; }
            try { s.Start(cols, rows); }
            catch (Exception ex)
            {
                lock (tab.Term.Sync) tab.Term.Feed("\x1b[91m" + L.F("Impossibile avviare {0}: {1}", "Could not start {0}: {1}", tab.Shell.Name, ex.Message) + "\x1b[0m\r\n");
                tab.Exited = true;
                tab.View.OutputArrived();
            }
        }

        void OnExited(TermTab tab, int code)
        {
            if (closing || !Tabs.Contains(tab)) return;
            tab.Exited = true;
            // uscita normale ("exit"): si chiude la scheda; altrimenti resta aperta con il messaggio
            if (code == 0) { CloseTab(tab); return; }
            lock (tab.Term.Sync)
                tab.Term.Feed("\r\n\x1b[90m" + L.F("[processo terminato con codice {0}: Invio per riavviarlo, Ctrl+Maiusc+W per chiudere la scheda]",
                                                    "[process exited with code {0}: Enter to restart it, Ctrl+Shift+W to close the tab]", code) + "\x1b[0m\r\n");
            tab.View.OutputArrived();
            tabStrip.Invalidate();
        }

        void Restart(TermTab tab)
        {
            try { tab.Session.Close(); } catch { }
            lock (tab.Term.Sync) tab.Term.Feed("\r\n");
            StartSession(tab, tab.Cwd);
            tabStrip.Invalidate();
        }

        void Activate(TermTab tab)
        {
            if (tab == null) return;
            bool changed = cur != tab;
            if (cur != null && changed)
            {
                cur.InputText = input.Text;
                cur.View.Visible = false;
            }
            cur = tab;
            tab.View.Visible = true;
            tab.View.BringToFront();
            tabStrip.Active = tab;
            tabStrip.EnsureVisible(tab);
            if (changed)
            {
                ShowChat(tab);
                UpdateFavBar();
                input.Text = tab.InputText;
            }
            UpdateTitle();
            UpdateSendButton();
            if (!input.Focused) tab.View.Focus();
        }

        void CloseTab(TermTab tab)
        {
            if (!Tabs.Contains(tab)) return;
            if (tab.AiCancel != null) tab.AiCancel.Cancel();
            try { tab.Session.Close(); } catch { }
            int i = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            termHost.Controls.Remove(tab.View);
            tab.View.Dispose();
            if (tab.Chat != null) { chatHost.Controls.Remove(tab.Chat); tab.Chat.Dispose(); }
            if (Tabs.Count == 0) { cur = null; Close(); return; }
            if (tab == cur) { cur = null; Activate(Tabs[Math.Min(i, Tabs.Count - 1)]); }
            tabStrip.Invalidate();
        }

        void NextTab(int step)
        {
            if (Tabs.Count < 2 || cur == null) return;
            Activate(Tabs[(Tabs.IndexOf(cur) + step + Tabs.Count) % Tabs.Count]);
        }

        void UpdateTitle()
        {
            Text = cur == null ? "CorbolPrompt" : cur.Title + " — CorbolPrompt";
            tabStrip.Invalidate();
        }

        void FillNewMenu(ToolStripItemCollection items)
        {
            items.Clear();
            var def = ShellInfo.Default;
            foreach (var sh in ShellInfo.All())
            {
                var s = sh;
                var mi = new ToolStripMenuItem(s.Name + (s == def ? "  " + L.T("(predefinita)", "(default)") : ""));
                if (s == def) mi.ShortcutKeyDisplayString = L.T("Ctrl+Maiusc+T", "Ctrl+Shift+T");
                mi.Click += delegate { NewTab(s, cur != null && !s.IsLisp ? cur.Cwd : null); };
                items.Add(mi);
            }
        }

        void ShowTabMenu(TermTab t, Point p)
        {
            tabMenu.Items.Clear();
            var ren = new ToolStripMenuItem(L.T("Rinomina...", "Rename...")) { ShortcutKeyDisplayString = L.T("doppio clic", "double-click") };
            ren.Click += delegate { BeginRename(t); };
            var dup = new ToolStripMenuItem(L.T("Duplica scheda", "Duplicate tab"));
            dup.Click += delegate { NewTab(t.Shell, t.Cwd); };   // il nome no: due schede con lo stesso nome confondono
            var restart = new ToolStripMenuItem(L.T("Riavvia", "Restart"));
            restart.Click += delegate { Restart(t); };
            var close = new ToolStripMenuItem(L.T("Chiudi", "Close")) { ShortcutKeyDisplayString = L.T("Ctrl+Maiusc+W", "Ctrl+Shift+W") };
            close.Click += delegate { CloseTab(t); };
            var others = new ToolStripMenuItem(L.T("Chiudi le altre", "Close other tabs")) { Enabled = Tabs.Count > 1 };
            others.Click += delegate { foreach (var o in Tabs.Where(x => x != t).ToList()) CloseTab(o); };
            int i = Tabs.IndexOf(t);
            var right = new ToolStripMenuItem(L.T("Chiudi le schede a destra", "Close tabs to the right")) { Enabled = i < Tabs.Count - 1 };
            right.Click += delegate { foreach (var o in Tabs.Skip(i + 1).ToList()) CloseTab(o); };
            string cwd = t.Cwd;
            bool winDir = cwd != null && Directory.Exists(cwd);
            var copy = new ToolStripMenuItem(L.T("Copia il percorso della cartella", "Copy folder path")) { Enabled = cwd != null };
            copy.Click += delegate { try { Clipboard.SetText(cwd); } catch { } };
            var open = new ToolStripMenuItem(L.T("Apri la cartella in Esplora file", "Open folder in File Explorer")) { Enabled = winDir };
            open.Click += delegate { try { Process.Start("explorer.exe", "\"" + cwd + "\""); } catch { } };
            tabMenu.Items.AddRange(new ToolStripItem[] { ren, dup, restart, new ToolStripSeparator(), close, others, right, new ToolStripSeparator(), copy, open });
            tabMenu.Show(tabStrip, p);
        }

        // Rinomina sul posto: una casella sopra la scheda; Invio conferma, Esc annulla, vuoto = nome automatico
        TextBox renameBox;

        void BeginRename(TermTab tab)
        {
            if (renameBox != null) return;
            Activate(tab);
            tabStrip.EnsureVisible(tab);
            var r = tabStrip.TabBounds(tab);
            if (r.IsEmpty) return;
            var box = new TextBox
            {
                Text = tab.CustomName.Length > 0 ? tab.CustomName : tab.AutoTitle,
                Font = tabStrip.Font, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(45, 45, 45), ForeColor = TabStrip.Text_
            };
            int h = box.PreferredHeight;
            box.SetBounds(r.X + S(6), r.Y + (r.Height - h) / 2, Math.Max(S(140), r.Width - S(12)), h);
            renameBox = box;
            tips.SetToolTip(box, L.T("Invio = conferma, Esc = annulla. Vuoto = nome automatico.", "Enter = confirm, Esc = cancel. Empty = automatic name."));
            bool done = false;
            Action<bool> finish = ok =>
            {
                if (done) return;
                done = true;
                if (ok) tab.CustomName = box.Text.Trim() == tab.AutoTitle ? "" : box.Text.Trim();
                renameBox = null;
                tabStrip.Controls.Remove(box);
                box.Dispose();
                UpdateTitle();
                if (tab == cur) tab.View.Focus();
            };
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; finish(true); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; finish(false); }
            };
            box.LostFocus += delegate { BeginInvoke((Action)(() => finish(true))); };
            tabStrip.Controls.Add(box);
            box.BringToFront();
            box.Focus();
            box.SelectAll();
        }

        void ShowTabList()
        {
            listMenu.Items.Clear();
            foreach (var t in Tabs)
            {
                var tt = t;
                var mi = new ToolStripMenuItem(t.Title) { Checked = t == cur };
                mi.Click += delegate { Activate(tt); };
                listMenu.Items.Add(mi);
            }
            listMenu.Show(tabStrip, tabStrip.ListMenuLocation, ToolStripDropDownDirection.BelowLeft);
        }

        // ---------- menu ----------

        void BuildMoreMenu(ToolStripDropDownButton more)
        {
            var dd = more.DropDownItems;
            var newSub = new ToolStripMenuItem(L.T("Nuova scheda", "New tab"));
            newSub.DropDownOpening += delegate { FillNewMenu(newSub.DropDownItems); };
            newSub.DropDownItems.Add("-");
            var defShell = new ToolStripMenuItem(L.T("Shell predefinita", "Default shell"));
            defShell.DropDownOpening += delegate
            {
                defShell.DropDownItems.Clear();
                foreach (var sh in ShellInfo.All())
                {
                    var s = sh;
                    var mi = new ToolStripMenuItem(s.Name) { Checked = s == ShellInfo.Default };
                    mi.Click += delegate { Settings.Set("shell.default", s.Id); Settings.Save(); };
                    defShell.DropDownItems.Add(mi);
                }
            };
            defShell.DropDownItems.Add("-");
            var aiSettings = new ToolStripMenuItem(L.T("Impostazioni dell'assistente...", "Assistant settings..."));
            aiSettings.Click += delegate { ShowAiSettings(); };
            var bigger = new ToolStripMenuItem(L.T("Carattere più grande", "Larger text")) { ShortcutKeyDisplayString = "Ctrl++" };
            bigger.Click += delegate { Zoom(1); };
            var smaller = new ToolStripMenuItem(L.T("Carattere più piccolo", "Smaller text")) { ShortcutKeyDisplayString = "Ctrl+-" };
            smaller.Click += delegate { Zoom(-1); };
            var normal = new ToolStripMenuItem(L.T("Carattere normale", "Normal text")) { ShortcutKeyDisplayString = "Ctrl+0" };
            normal.Click += delegate { Zoom(0); };
            var lang = new ToolStripMenuItem(L.T("Lingua", "Language"));
            lang.DropDownOpening += delegate
            {
                lang.DropDownItems.Clear();
                var auto = new ToolStripMenuItem(L.T("Automatica (lingua di Windows)", "Automatic (Windows language)")) { Checked = L.Setting == "auto" };
                auto.Click += delegate { SetLanguage("auto"); };
                lang.DropDownItems.Add(auto);
                foreach (var l in L.All())
                {
                    var ll = l;
                    var mi = new ToolStripMenuItem(l.Name) { Checked = L.Setting == l.Code };
                    mi.Click += delegate { SetLanguage(ll.Code); };
                    lang.DropDownItems.Add(mi);
                }
            };
            lang.DropDownItems.Add("-");
            var showBar = new ToolStripMenuItem(L.T("Suggerisci i preferiti sotto il terminale", "Suggest favorites below the terminal")) { Checked = Settings.Get("fav.bar", true), CheckOnClick = true };
            showBar.CheckedChanged += delegate { Settings.Set("fav.bar", showBar.Checked); Settings.Save(); UpdateFavBar(); };
            var welcome = new ToolStripMenuItem(L.T("Benvenuto...", "Welcome tour..."));
            welcome.Click += delegate { WelcomeForm.ShowFor(this); };
            var guide = new ToolStripMenuItem(L.T("Guida", "User guide")) { ShortcutKeyDisplayString = "Ctrl+F1" };
            guide.Click += delegate { Help.Open(Help.Page("Guida-utente", "User-guide")); };
            var lispGuide = new ToolStripMenuItem(L.T("Guida al Lisp", "Lisp guide"));
            lispGuide.Click += delegate { Help.Open(Help.Page("Lisp", "Lisp-guide")); };
            var keys = new ToolStripMenuItem(L.T("Scorciatoie da tastiera", "Keyboard shortcuts"));
            keys.Click += delegate { ShowKeys(); };
            var check = new ToolStripMenuItem(L.T("Controlla aggiornamenti", "Check for updates"));
            check.Click += delegate { Updater.Check(this, true); };
            var autoUpd = new ToolStripMenuItem(L.T("Aggiorna automaticamente", "Update automatically")) { Checked = Updater.Auto, CheckOnClick = true };
            autoUpd.CheckedChanged += delegate { Updater.Auto = autoUpd.Checked; };
            var about = new ToolStripMenuItem(L.T("Informazioni su CorbolPrompt", "About CorbolPrompt"));
            about.Click += delegate { AboutForm.ShowFor(this, Icon); };
            if (Updater.Version != null)
            {
                var upd = new ToolStripMenuItem(L.F("Aggiorna a CorbolPrompt {0}...", "Update to CorbolPrompt {0}...", Updater.Version)) { Font = new Font(Font, FontStyle.Bold) };
                upd.Click += delegate { Updater.Offer(this); };
                dd.AddRange(new ToolStripItem[] { upd, new ToolStripSeparator() });
            }
            dd.AddRange(new ToolStripItem[] { newSub, defShell, new ToolStripSeparator(), aiSettings, new ToolStripSeparator(), bigger, smaller, normal,
                                              new ToolStripSeparator(), lang, showBar, new ToolStripSeparator(), welcome, guide, lispGuide, keys, new ToolStripSeparator(), check, autoUpd, about });
        }

        void SetLanguage(string code)
        {
            Settings.Set("lang", code);
            Settings.Save();
            L.Set(code);
            ApplyTexts();
        }

        // Tutti i testi della finestra nella lingua corrente: all'avvio e quando si cambia lingua (le schede restano aperte)
        void ApplyTexts()
        {
            tbFav.Text = "★ " + L.T("Preferiti", "Favorites");
            tbFav.ToolTipText = L.T("Comandi salvati (Ctrl+Maiusc+P)", "Saved commands (Ctrl+Shift+P)");
            tbAi.Text = "✦ " + L.T("Assistente", "Assistant");
            tbAi.ToolTipText = L.T("Mostra o nasconde l'assistente (Ctrl+Maiusc+A)", "Show or hide the assistant (Ctrl+Shift+A)");
            more.ToolTipText = Updater.Version != null ? L.F("Menu · è disponibile CorbolPrompt {0}", "Menu · CorbolPrompt {0} is available", Updater.Version) : L.T("Menu", "Menu");
            more.Text = Updater.Version != null ? "⋯ •" : "⋯";
            more.DropDownItems.Clear();
            BuildMoreMenu(more);
            BuildTermMenu();
            aiTitle.Text = L.T("Assistente", "Assistant");
            btNewChat.Text = L.T("Nuova", "New");
            tips.SetToolTip(btNewChat, L.T("Nuova conversazione: l'assistente dimentica quella di questa scheda", "New conversation: the assistant forgets the one of this tab"));
            tips.SetToolTip(input, L.T("Invio = chiedi, Maiusc+Invio = a capo, Esc = torna al terminale", "Enter = ask, Shift+Enter = new line, Esc = back to the terminal"));
            UpdateSendButton();
            UpdateService();
            UpdateBusy();
            foreach (var t in Tabs) if (t.Chat != null && t.History.Count == 0) Welcome(t);   // le conversazioni già iniziate restano com'erano
            if (cur != null) ShowCommands(cur);
            UpdateFavBar();
            UpdateTitle();
        }

        void ShowKeys()
        {
            MessageBox.Show(this, L.T(
                "Ctrl+Maiusc+T  nuova scheda\nCtrl+Maiusc+W  chiude la scheda\nCtrl+Tab / Ctrl+Maiusc+Tab  scheda successiva / precedente\n" +
                "Ctrl+C con del testo selezionato  copia (senza selezione interrompe il comando)\nCtrl+V o clic destro → Incolla  incolla\n" +
                "Ctrl+Maiusc+A  assistente\nCtrl+Maiusc+P  preferiti (Maiusc+clic incolla senza eseguire)\nMaiusc+PagSu / PagGiù o rotellina  scorre la cronologia dello schermo\nCtrl++ / Ctrl+- / Ctrl+0  dimensione del carattere\n" +
                "Doppio clic  seleziona una parola",
                "Ctrl+Shift+T  new tab\nCtrl+Shift+W  close the tab\nCtrl+Tab / Ctrl+Shift+Tab  next / previous tab\n" +
                "Ctrl+C with text selected  copy (without a selection it interrupts the command)\nCtrl+V or right click → Paste  paste\n" +
                "Ctrl+Shift+A  assistant\nCtrl+Shift+P  favorites (Shift+click pastes without running)\nShift+PgUp / PgDn or mouse wheel  scroll the screen history\nCtrl++ / Ctrl+- / Ctrl+0  text size\n" +
                "Double click  select a word"), L.T("Scorciatoie da tastiera", "Keyboard shortcuts"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void BuildTermMenu()
        {
            var old = termMenu;
            termMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 10f) };
            foreach (var t in Tabs) t.View.ContextMenuStrip = termMenu;
            if (old != null) old.Dispose();
            var copy = new ToolStripMenuItem(L.T("Copia", "Copy")) { ShortcutKeyDisplayString = "Ctrl+C" };
            copy.Click += delegate { Copy(); };
            var paste = new ToolStripMenuItem(L.T("Incolla", "Paste")) { ShortcutKeyDisplayString = "Ctrl+V" };
            paste.Click += delegate { Paste(); };
            var all = new ToolStripMenuItem(L.T("Seleziona tutto", "Select all"));
            all.Click += delegate { if (cur != null) cur.View.SelectAll(); };
            var ask = new ToolStripMenuItem(L.T("Chiedi all'assistente...", "Ask the assistant..."));
            ask.Click += delegate { FocusAssistant(); };
            var explain = new ToolStripMenuItem(L.T("Spiega con l'assistente", "Explain with the assistant"));
            explain.Click += delegate
            {
                if (cur == null) return;
                bool sel = cur.View.HasSelection;
                ShowAssistant();
                Ask(sel ? L.T("Spiegami il testo selezionato.", "Explain the selected text to me.")
                        : L.T("Spiegami cosa è successo nelle ultime righe dello schermo (se c'è un errore, come risolverlo).", "Explain what happened in the last lines of the screen (if there is an error, how to fix it)."));
            };
            var fav = new ToolStripMenuItem(L.T("Aggiungi ai preferiti...", "Add to favorites..."));
            fav.Click += delegate { if (cur != null && cur.View.HasSelection) AddFavorite(cur.View.SelectedText, cur.Shell.Lang, ""); };
            var clear = new ToolStripMenuItem(L.T("Cancella la cronologia dello schermo", "Clear the screen history"));
            clear.Click += delegate { if (cur != null) { lock (cur.Term.Sync) cur.Term.ClearScrollback(); cur.View.OutputArrived(); } };
            termMenu.Opening += delegate
            {
                bool sel = cur != null && cur.View.HasSelection;
                copy.Enabled = sel;
                fav.Enabled = sel;
                explain.Text = sel ? L.T("Spiega la selezione con l'assistente", "Explain the selection with the assistant") : L.T("Spiega lo schermo con l'assistente", "Explain the screen with the assistant");
            };
            termMenu.Items.AddRange(new ToolStripItem[] { copy, paste, all, fav, new ToolStripSeparator(), explain, ask, new ToolStripSeparator(), clear });
        }

        void Copy()
        {
            if (cur == null || !cur.View.HasSelection) return;
            try { Clipboard.SetText(cur.View.SelectedText); } catch { }
            cur.View.ClearSelection();
        }

        void Paste()
        {
            if (cur == null) return;
            string t = null;
            try { t = Clipboard.GetText(); } catch { }
            if (!string.IsNullOrEmpty(t)) cur.View.Paste(t);
            cur.View.Focus();
        }

        void Zoom(int n)
        {
            fontSize = n == 0 ? 11 : Math.Max(6, Math.Min(36, fontSize + n));
            Settings.Set("font.size", (int)fontSize);
            foreach (var t in Tabs) t.View.SetFontSize(fontSize);
        }

        // ---------- tastiera ----------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool inTerm = cur != null && cur.View.Focused;
            switch (keyData)
            {
                case Keys.Control | Keys.Shift | Keys.T: NewTab(ShellInfo.Default, cur != null ? cur.Cwd : null); return true;
                case Keys.Control | Keys.Shift | Keys.W: if (cur != null) CloseTab(cur); return true;
                case Keys.Control | Keys.Tab: NextTab(1); return true;
                case Keys.Control | Keys.Shift | Keys.Tab: NextTab(-1); return true;
                case Keys.Control | Keys.Shift | Keys.A: if (!split.Panel2Collapsed && input.Focused) { if (cur != null) cur.View.Focus(); } else FocusAssistant(); return true;
                case Keys.Control | Keys.Shift | Keys.P: tbFav.ShowDropDown(); return true;
                case Keys.Control | Keys.F1: Help.Open(Help.Page("Guida-utente", "User-guide")); return true;
                case Keys.Control | Keys.Shift | Keys.C: if (inTerm) { Copy(); return true; } break;
                case Keys.Control | Keys.Shift | Keys.V: if (inTerm) { Paste(); return true; } break;
                case Keys.Control | Keys.C: if (inTerm && cur.View.HasSelection) { Copy(); return true; } break;
                case Keys.Control | Keys.V: if (inTerm) { Paste(); return true; } break;
                case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Add: case Keys.Control | Keys.Shift | Keys.Oemplus: Zoom(1); return true;
                case Keys.Control | Keys.OemMinus: case Keys.Control | Keys.Subtract: Zoom(-1); return true;
                case Keys.Control | Keys.D0: case Keys.Control | Keys.NumPad0: if (inTerm) { Zoom(0); return true; } break;
                case Keys.Enter: if (inTerm && cur.Exited) { Restart(cur); return true; } break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- assistente ----------

        void BuildAssistantPanel()
        {
            var panel = split.Panel2;
            panel.BackColor = Color.White;
            panel.Padding = new Padding(8, 6, 8, 8);
            aiTitle.Font = new Font(Font, FontStyle.Bold);
            var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, AutoSize = true, Padding = new Padding(0, 0, 0, 6), BackColor = Color.White };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            aiService.Height = aiTitle.PreferredHeight;
            header.Controls.Add(aiTitle, 0, 0);
            header.Controls.Add(aiService, 1, 0);
            aiService.Cursor = Cursors.Hand;
            aiService.Click += delegate { ShowAiSettings(); };

            var inputRow = new Panel { Dock = DockStyle.Bottom, Height = S(84), Padding = new Padding(0, 6, 0, 0), BackColor = Color.White };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, Width = S(96), WrapContents = false, Padding = new Padding(6, 0, 0, 0), BackColor = Color.White };
            btSend.Size = new Size(S(88), S(30)); btSend.Margin = new Padding(0, 0, 0, 4);
            btNewChat.Size = new Size(S(88), S(26)); btNewChat.Margin = new Padding(0);
            btSend.Click += delegate { SendOrStop(); };
            btNewChat.Click += delegate { NewConversation(); };
            buttons.Controls.Add(btSend);
            buttons.Controls.Add(btNewChat);
            input.Font = new Font("Segoe UI", 9.5f);
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendOrStop(); }
                else if (e.KeyCode == Keys.Escape && cur != null) { e.SuppressKeyPress = true; cur.View.Focus(); }
            };
            inputRow.Controls.Add(input);
            inputRow.Controls.Add(buttons);
            lbBusy.Font = Font;

            panel.Controls.Add(chatHost);
            panel.Controls.Add(cmdPanel);
            panel.Controls.Add(lbBusy);
            panel.Controls.Add(inputRow);
            panel.Controls.Add(header);
            cmdPanel.Height = 0;
            cmdPanel.Resize += delegate { foreach (Control c in cmdPanel.Controls) c.Width = cmdPanel.ClientSize.Width - 2; };
        }

        void UpdateService()
        {
            string s = AiSettings.Describe();
            aiService.Text = s;
            tips.SetToolTip(aiService, s + "\n" + L.T("Clic per cambiare servizio o account", "Click to change service or account"));
        }

        void ShowAiSettings()
        {
            using (var f = new AiSettingsForm()) f.ShowDialog(this);
            UpdateService();
        }

        void ToggleAssistant()
        {
            if (split.Panel2Collapsed) FocusAssistant();
            else { split.Panel2Collapsed = true; tbAi.Checked = false; if (cur != null) cur.View.Focus(); }
        }

        void ShowAssistant()
        {
            if (!split.Panel2Collapsed) return;
            split.Panel2Collapsed = false;
            tbAi.Checked = true;
            split.SplitterDistance = Math.Max(200, split.Width - S(Settings.Get("ai.width", 400)));
        }

        void FocusAssistant()
        {
            ShowAssistant();
            input.Focus();
        }

        RichTextBox ChatOf(TermTab t)
        {
            if (t.Chat != null) return t.Chat;
            t.Chat = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Segoe UI", 9.5f), DetectUrls = true };
            t.Chat.LinkClicked += (s, e) => { try { Process.Start(e.LinkText); } catch { } };
            chatHost.Controls.Add(t.Chat);
            Welcome(t);
            return t.Chat;
        }

        void Welcome(TermTab t)
        {
            var c = t.Chat;
            c.Clear();
            c.SelectionColor = Color.Gray;
            c.AppendText(t.Shell.IsLisp
                ? L.T("Chiedimi del Lisp: \"scrivi una funzione che conta le parole di un file\", \"cosa fa loop?\", \"perché questo errore?\". Il codice che propongo lo esegui con un clic.\n",
                      "Ask me about Lisp: \"write a function that counts the words in a file\", \"what does loop do?\", \"why this error?\". You run the code I suggest with a click.\n")
                : L.T("Chiedimi un comando o di spiegare quello che vedi: \"come trovo i file più grandi di 1 GB?\", \"cosa vuol dire questo errore?\", \"crea un repository git qui\". I comandi che propongo li esegui tu, con un clic.\n",
                      "Ask me for a command or to explain what you see: \"how do I find files larger than 1 GB?\", \"what does this error mean?\", \"create a git repository here\". You run the commands I suggest, with a click.\n"));
        }

        void ShowChat(TermTab t)
        {
            var chat = ChatOf(t);
            foreach (Control c in chatHost.Controls) c.Visible = c == chat;
            ShowCommands(t);
            UpdateBusy();
        }

        void Say(TermTab t, string who, string text, Color color)
        {
            var c = ChatOf(t);
            c.SelectionStart = c.TextLength;
            c.SelectionFont = new Font(c.Font, FontStyle.Bold);
            c.SelectionColor = color;
            c.AppendText("\n" + who + "\n");
            c.SelectionFont = c.Font;
            c.SelectionColor = Color.FromArgb(30, 30, 30);
            c.AppendText(text.Trim() + "\n");
            c.SelectionStart = c.TextLength;
            c.ScrollToCaret();
        }

        void SayReply(TermTab t, string reply)
        {
            var c = ChatOf(t);
            c.SelectionStart = c.TextLength;
            c.SelectionFont = new Font(c.Font, FontStyle.Bold);
            c.SelectionColor = Color.FromArgb(22, 130, 12);
            c.AppendText("\n" + L.T("Assistente", "Assistant") + "\n");
            var mono = new Font("Consolas", 9f);
            foreach (var part in Prompt.Parts(reply))
            {
                c.SelectionStart = c.TextLength;
                if (part.Key == null)
                {
                    c.SelectionFont = c.Font;
                    c.SelectionColor = Color.FromArgb(30, 30, 30);
                    c.SelectionBackColor = Color.White;
                    c.AppendText(part.Value.Trim('\n') + "\n");
                }
                else
                {
                    c.SelectionFont = mono;
                    c.SelectionColor = Color.FromArgb(20, 20, 20);
                    c.SelectionBackColor = Color.FromArgb(238, 240, 242);
                    c.AppendText(part.Value + "\n");
                    c.SelectionStart = c.TextLength;
                    c.SelectionBackColor = Color.White;
                }
            }
            c.SelectionStart = c.TextLength;
            c.SelectionBackColor = Color.White;
            c.ScrollToCaret();
        }

        void ShowCommands(TermTab t)
        {
            cmdPanel.SuspendLayout();
            foreach (Control c in cmdPanel.Controls.Cast<Control>().ToList()) { cmdPanel.Controls.Remove(c); c.Dispose(); }
            int h = 0;
            foreach (var b in t.Commands.Take(8))
            {
                var block = b;
                string first = block.Code.Split('\n')[0].Trim();
                int lines = block.Code.Split('\n').Length;
                bool lisp = block.Lang == "lisp";
                var bt = new Button
                {
                    Text = "▶  " + (lisp && !t.Shell.IsLisp ? "[Lisp] " : "") + first + (lines > 1 ? "  " + L.F("(+{0} righe)", "(+{0} lines)", lines - 1) : ""),
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true,
                    Font = new Font("Consolas", 9f),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(240, 247, 240),
                    ForeColor = Color.FromArgb(20, 20, 20),
                    Height = S(28),
                    Width = cmdPanel.ClientSize.Width - 2,
                    Margin = new Padding(0, 0, 0, 3),
                    Cursor = Cursors.Hand,
                    UseMnemonic = false
                };
                bt.FlatAppearance.BorderColor = Color.FromArgb(170, 210, 170);
                tips.SetToolTip(bt, (block.Code.Length > 1500 ? block.Code.Substring(0, 1500) + "…" : block.Code) + "\n\n" +
                    L.T("Clic: esegue nel terminale · clic destro: incolla senza eseguire, copia, preferiti", "Click: runs in the terminal · right click: paste without running, copy, favorites"));
                bt.Click += delegate { RunBlock(block, true); };
                var menu = new ContextMenuStrip();
                menu.Items.Add(L.T("Esegui", "Run"), null, delegate { RunBlock(block, true); });
                menu.Items.Add(L.T("Incolla senza eseguire", "Paste without running"), null, delegate { RunBlock(block, false); });
                menu.Items.Add(L.T("Copia", "Copy"), null, delegate { try { Clipboard.SetText(block.Code.Replace("\n", "\r\n")); } catch { } });
                menu.Items.Add(L.T("Aggiungi ai preferiti...", "Add to favorites..."), null, delegate { AddFavorite(block.Code, block.Lang, block.Explanation); });
                bt.ContextMenuStrip = menu;
                cmdPanel.Controls.Add(bt);
                h += bt.Height + 3;
            }
            cmdPanel.Height = t.Commands.Count == 0 ? 0 : Math.Min(h + 6, S(160));
            cmdPanel.ResumeLayout();
        }

        // Esegue un comando proposto: nella scheda attiva, o in una scheda Lisp per il codice Lisp
        void RunBlock(Prompt.Block block, bool execute)
        {
            if (cur == null) return;
            var target = cur;
            if (block.Lang == "lisp" && !cur.Shell.IsLisp)
                target = Tabs.FirstOrDefault(t => t.Shell.IsLisp && !t.Exited) ?? NewTab(ShellInfo.Find("lisp"), cur.Cwd);
            if (target.Exited) Restart(target);
            Activate(target);
            string code = block.Code.Replace("\r\n", "\n");
            if (execute) target.View.SendInput(code.Replace("\n", "\r") + "\r");
            else target.View.Paste(code);
            target.View.Focus();
        }

        // ---------- preferiti ----------

        void FillFavMenu()
        {
            var items = tbFav.DropDownItems;
            items.Clear();
            var favs = Favorites.All;
            if (favs.Count == 0)
                items.Add(new ToolStripMenuItem(L.T("Nessun preferito: seleziona un comando nel terminale, tasto destro → Aggiungi ai preferiti",
                                                    "No favorites yet: select a command in the terminal, right click → Add to favorites")) { Enabled = false });
            var mine = favs.Where(FitsCurrent).ToList();
            var others = favs.Where(f => !FitsCurrent(f)).ToList();
            if (favs.Count > 0 && mine.Count == 0)
                items.Add(new ToolStripMenuItem(L.F("Nessun preferito per {0}", "No favorites for {0}", cur != null ? cur.Shell.Short : "")) { Enabled = false });
            foreach (var f in mine) items.Add(FavItem(f, false));
            if (others.Count > 0)
            {
                var sub = new ToolStripMenuItem(L.T("Altre shell", "Other shells"));
                sub.DropDown.ShowItemToolTips = true;
                foreach (var f in others) sub.DropDownItems.Add(FavItem(f, true));
                items.Add(sub);
            }
            items.Add(new ToolStripSeparator());
            bool sel = cur != null && cur.View.HasSelection;
            var add = new ToolStripMenuItem(L.T("Aggiungi la selezione...", "Add the selection...")) { Enabled = sel };
            add.Click += delegate { AddFavorite(cur.View.SelectedText, cur.Shell.Lang, ""); };
            var manage = new ToolStripMenuItem(L.T("Gestisci i preferiti...", "Manage favorites..."));
            manage.Click += delegate { ManageFavorites(null); };
            items.Add(add);
            items.Add(manage);
        }

        // Il preferito va bene per la scheda attiva: è per la sua shell o per qualsiasi shell
        bool FitsCurrent(Favorite f) { return f.Lang.Length == 0 || (cur != null && cur.Shell.Lang == f.Lang); }

        string FavTip(Favorite f)
        {
            return (f.Description.Length > 0 ? Wrap(f.Description, 70) + "\n\n" : "") +
                   (f.Code.Length > 800 ? f.Code.Substring(0, 800) + "…" : f.Code) + "\n\n" +
                   L.T("Clic: esegue · Maiusc+clic: incolla senza eseguire", "Click: run · Shift+click: paste without running");
        }

        void RunFavorite(Favorite f) { RunBlock(new Prompt.Block { Lang = f.Lang, Code = f.Code }, (ModifierKeys & Keys.Shift) == 0); }

        ToolStripMenuItem FavItem(Favorite f, bool tag)
        {
            var mi = new ToolStripMenuItem(f.Name + (tag && f.Lang.Length > 0 ? "   [" + Favorites.LangName(f.Lang) + "]" : "")) { ToolTipText = FavTip(f) };
            mi.Click += delegate { RunFavorite(f); };
            return mi;
        }

        // Barra sotto il terminale: i preferiti della shell attiva (nascosta se non ce ne sono o se l'utente la spegne)
        void UpdateFavBar()
        {
            var mine = cur == null ? new List<Favorite>() : Favorites.All.Where(FitsCurrent).ToList();
            bool show = Settings.Get("fav.bar", true) && mine.Count > 0;
            favBar.SuspendLayout();
            foreach (Control c in favBar.Controls.Cast<Control>().ToList()) { favBar.Controls.Remove(c); c.Dispose(); }
            if (show)
            {
                var star = new Label { Text = "★", AutoSize = true, ForeColor = TabStrip.Accent, Margin = new Padding(0, S(4), S(4), 0), Font = new Font("Segoe UI", 10f) };
                tips.SetToolTip(star, L.F("Preferiti per {0}: clic esegue, Maiusc+clic incolla", "Favorites for {0}: click runs, Shift+click pastes", cur.Shell.Short));
                favBar.Controls.Add(star);
                foreach (var f in mine)
                {
                    var fv = f;
                    string name = f.Name.Length > 32 ? f.Name.Substring(0, 31) + "…" : f.Name;
                    var b = new Button
                    {
                        Text = name, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, UseMnemonic = false,
                        BackColor = Color.FromArgb(45, 45, 45), ForeColor = TabStrip.Text_, Font = new Font("Segoe UI", 10f),
                        Margin = new Padding(0, 0, S(6), 0), Padding = new Padding(S(4), 0, S(4), 0), Cursor = Cursors.Hand, TabStop = false
                    };
                    b.FlatAppearance.BorderColor = Color.FromArgb(75, 75, 75);
                    b.FlatAppearance.MouseOverBackColor = TabStrip.Hover;
                    tips.SetToolTip(b, FavTip(f));
                    b.Click += delegate { RunFavorite(fv); };
                    var menu = new ContextMenuStrip();
                    menu.Items.Add(L.T("Esegui", "Run"), null, delegate { RunBlock(new Prompt.Block { Lang = fv.Lang, Code = fv.Code }, true); });
                    menu.Items.Add(L.T("Incolla senza eseguire", "Paste without running"), null, delegate { RunBlock(new Prompt.Block { Lang = fv.Lang, Code = fv.Code }, false); });
                    menu.Items.Add(L.T("Gestisci i preferiti...", "Manage favorites..."), null, delegate { ManageFavorites(null); });
                    b.ContextMenuStrip = menu;
                    favBar.Controls.Add(b);
                }
            }
            // altezza della barra in base ai pulsanti (cambia con i DPI)
            int h = 0;
            foreach (Control c in favBar.Controls) h = Math.Max(h, c.GetPreferredSize(Size.Empty).Height);
            if (h > 0) favBar.Height = h + favBar.Padding.Vertical + 2;
            favBar.Visible = show;
            favBar.ResumeLayout();
        }

        // A capo ogni tanti caratteri, per i suggerimenti (che altrimenti sono una riga lunghissima)
        static string Wrap(string s, int width)
        {
            var sb = new StringBuilder();
            int col = 0;
            foreach (var w in s.Split(' '))
            {
                if (col > 0 && col + w.Length > width) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(w);
                col += w.Length;
            }
            return sb.ToString();
        }

        // Testo selezionato o comando dell'assistente: si apre la finestra con il preferito nuovo da completare
        void AddFavorite(string code, string lang, string description)
        {
            code = (code ?? "").Replace("\r\n", "\n").Trim('\n', ' ');
            if (code.Length == 0) return;
            // per il Lisp e per le shell si propone la shell da cui viene; per i blocchi senza linguaggio, qualsiasi
            ManageFavorites(new Favorite { Code = code, Lang = Favorites.Langs.Contains(lang ?? "") ? lang ?? "" : "", Description = description ?? "" });
        }

        void ManageFavorites(Favorite add)
        {
            using (var f = new FavoritesForm(add)) f.ShowDialog(this);
            UpdateFavBar();
            if (cur != null) cur.View.Focus();
        }

        void NewConversation()
        {
            if (cur == null) return;
            if (cur.AiCancel != null) cur.AiCancel.Cancel();
            cur.History.Clear();
            cur.Commands.Clear();
            Welcome(cur);
            ShowCommands(cur);
        }

        void SendOrStop()
        {
            if (cur == null) return;
            if (cur.Busy) { cur.AiCancel.Cancel(); return; }
            string text = input.Text.Trim();
            if (text.Length == 0) return;
            input.Clear();
            Ask(text);
        }

        static readonly Regex TabWords = new Regex(@"\b(schede|tabs|altra scheda|other tab)\b", RegexOptions.IgnoreCase);

        // Testo per l'assistente: scheda, shell, cartella, ultime righe, selezione, altre schede, richiesta
        string Context(TermTab t, string request)
        {
            var sb = new StringBuilder();
            string cwd = t.Cwd;
            sb.Append("Current tab: \"").Append(t.Title).Append("\", shell: ").Append(t.Shell.Name).Append(" (code blocks: ```").Append(t.Shell.Lang).Append(")");
            if (cwd != null) sb.Append(", current folder: ").Append(cwd);
            sb.Append(".\n");
            if (t.Exited) sb.Append("The shell of this tab has exited.\n");
            if (AiSettings.SendScreen)
            {
                string screen;
                lock (t.Term.Sync) screen = t.Term.LastLines(80);
                if (screen.Length > 10000) screen = screen.Substring(screen.Length - 10000);
                sb.Append("Last lines of its screen:\n```text\n").Append(screen.Length > 0 ? screen : "(empty)").Append("\n```\n");
                string sel = t.View.HasSelection ? t.View.SelectedText : "";
                if (sel.Length > 0)
                {
                    if (sel.Length > 6000) sel = sel.Substring(0, 6000) + "…";
                    sb.Append("Text the user selected on the screen:\n```text\n").Append(sel).Append("\n```\n");
                }
            }
            var others = Tabs.Where(o => o != t).ToList();
            if (others.Count > 0)
            {
                sb.Append("Other open tabs: ").Append(string.Join(", ", others.Select(o => "\"" + o.Title + "\" (" + o.Shell.Name + (o.Cwd != null ? ", " + o.Cwd : "") + ")"))).Append(".\n");
                if (AiSettings.SendScreen)
                {
                    bool all = TabWords.IsMatch(request);
                    int room = 12000;
                    foreach (var o in others)
                    {
                        if (!(all || Mentions(request, o.Title) || Mentions(request, o.Shell.Short))) continue;
                        string s;
                        lock (o.Term.Sync) s = o.Term.LastLines(40);
                        if (s.Length > room) continue;
                        sb.Append("Last lines of tab \"").Append(o.Title).Append("\":\n```text\n").Append(s).Append("\n```\n");
                        room -= s.Length;
                    }
                }
            }
            sb.Append("\nRequest: ").Append(request);
            return sb.ToString();
        }

        static bool Mentions(string text, string title)
        {
            if (string.IsNullOrEmpty(title) || title.Length < 2) return false;
            int i = text.IndexOf(title, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                int e = i + title.Length;
                bool after = e >= text.Length || !char.IsLetterOrDigit(text[e]);
                if (before && after) return true;
                i = text.IndexOf(title, i + 1, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        void Ask(string text)
        {
            var t = cur;
            if (t == null || t.Busy) return;
            ShowAssistant();
            string missing;
            var provider = AiSettings.Create(out missing);
            Say(t, L.T("Tu", "You"), text, Color.FromArgb(37, 99, 235));
            if (provider == null)
            {
                Say(t, L.T("Assistente", "Assistant"), missing + " " + L.T("Configuralo dal menu ⋯ → Impostazioni dell'assistente.", "Set it up from the ⋯ menu → Assistant settings."), Color.Firebrick);
                return;
            }
            var req = new AiRequest { System = Prompt.System };
            foreach (var m in t.History.Skip(Math.Max(0, t.History.Count - 12))) req.Messages.Add(m);
            req.Messages.Add(new AiMessage("user", Context(t, text)));
            t.AiCancel = new CancellationTokenSource();
            t.AiStart = DateTime.Now;
            var token = t.AiCancel.Token;
            string who = ClaudeCodeProvider.DisplayNameOf(provider);
            UpdateSendButton();
            UpdateBusy();
            busyTimer.Start();
            tabStrip.Invalidate();
            ThreadPool.QueueUserWorkItem(delegate
            {
                AiResult res;
                try { res = provider.Run(req, token); }
                catch (Exception ex) { res = new AiResult { Error = ex.Message }; }
                try { BeginInvoke((Action)(() => OnAnswer(t, text, res))); } catch { }
            });
        }

        void OnAnswer(TermTab t, string question, AiResult res)
        {
            t.AiCancel = null;
            UpdateSendButton();
            UpdateBusy();
            tabStrip.Invalidate();
            if (!Tabs.Contains(t)) return;
            if (res.Cancelled) { Say(t, L.T("Assistente", "Assistant"), L.T("(interrotto)", "(stopped)"), Color.Gray); return; }
            if (!res.Ok) { Say(t, L.T("Assistente", "Assistant"), res.Error, Color.Firebrick); return; }
            t.History.Add(new AiMessage("user", question));
            t.History.Add(new AiMessage("assistant", res.Text));
            SayReply(t, res.Text);
            t.Commands = Prompt.Blocks(res.Text);
            if (t == cur) ShowCommands(t);
        }

        void UpdateSendButton()
        {
            bool busy = cur != null && cur.Busy;
            btSend.Text = busy ? L.T("Ferma", "Stop") : L.T("Chiedi", "Ask");
        }

        void UpdateBusy()
        {
            bool busy = cur != null && cur.Busy;
            if (!busy)
            {
                lbBusy.Height = 0;
                if (!Tabs.Any(t => t.Busy)) busyTimer.Stop();
                return;
            }
            string missing;
            var p = AiSettings.Create(out missing);
            int secs = (int)(DateTime.Now - cur.AiStart).TotalSeconds;
            lbBusy.Text = L.F("Sta scrivendo… {1} s · {0}", "Writing… {1} s · {0}", p != null ? ClaudeCodeProvider.DisplayNameOf(p) : "", secs);
            lbBusy.Height = S(22);
        }
    }

    // Finestra "Informazioni", come quella di CorbolPad
    class AboutForm : Form
    {
        public static void ShowFor(IWin32Window owner, Icon icon)
        {
            using (var f = new AboutForm(icon)) f.ShowDialog(owner);
        }

        // Le immagini in corbolprompt.ico sono PNG: si legge direttamente quella della misura voluta
        static Image IconImage(int px)
        {
            try
            {
                using (var st = typeof(AboutForm).Assembly.GetManifestResourceStream("CorbolPrompt.corbolprompt.ico"))
                using (var ms = new MemoryStream())
                {
                    st.CopyTo(ms);
                    byte[] d = ms.ToArray();
                    int n = BitConverter.ToUInt16(d, 4);
                    for (int i = 0; i < n; i++)
                    {
                        int e = 6 + 16 * i;
                        if (d[e] != px) continue;
                        int size = BitConverter.ToInt32(d, e + 8), off = BitConverter.ToInt32(d, e + 12);
                        using (var img = Image.FromStream(new MemoryStream(d, off, size))) return new Bitmap(img);
                    }
                }
            }
            catch { }
            return null;
        }

        static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
        }

        AboutForm(Icon icon)
        {
            Text = L.T("Informazioni su CorbolPrompt", "About CorbolPrompt");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            ClientSize = new Size(400, 220);
            if (icon != null) Icon = icon;

            var accent = Color.FromArgb(22, 130, 60);
            var pic = new PictureBox { Location = new Point(20, 20), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom };
            pic.Image = IconImage(64);
            var title = new Label { Text = "CorbolPrompt " + AppVersion.Text, AutoSize = true, Location = new Point(82, 18), Font = new Font("Segoe UI Semibold", 14f) };
            var desc = new Label
            {
                Text = L.T("Terminale a schede con un assistente IA e un interprete Lisp",
                           "Tabbed terminal with an AI assistant and a Lisp interpreter"),
                AutoSize = true, MaximumSize = new Size(300, 0), Location = new Point(84, 50), ForeColor = Color.DimGray
            };
            var author = new Label { Text = "© 2026 " + AppVersion.Author, AutoSize = true, Location = new Point(84, 90) };
            string lic = L.T("Licenza MIT", "MIT License");
            var repo = new LinkLabel
            {
                Text = lic + "  ·  GitHub", AutoSize = true, Location = new Point(84, 112),
                LinkColor = accent, ActiveLinkColor = accent, LinkBehavior = LinkBehavior.HoverUnderline
            };
            repo.Links.Clear();
            repo.Links.Add(0, lic.Length, AppVersion.RepoUrl + "/blob/main/LICENSE");
            repo.Links.Add(lic.Length + 5, 6, AppVersion.RepoUrl);
            repo.LinkClicked += (s, e) => Open((string)e.Link.LinkData);
            string folder = L.T("Cartella delle impostazioni", "Settings folder");
            var links = new LinkLabel
            {
                Text = folder, AutoSize = true, Location = new Point(84, 112),
                LinkColor = accent, ActiveLinkColor = accent, LinkBehavior = LinkBehavior.HoverUnderline
            };
            links.LinkClicked += delegate { try { Directory.CreateDirectory(Settings.Dir); } catch { } Open(Settings.Dir); };

            var ok = new Button { Text = "OK", Size = new Size(90, 30), DialogResult = DialogResult.OK };
            AcceptButton = CancelButton = ok;
            Controls.AddRange(new Control[] { pic, title, desc, author, repo, links, ok });

            Load += delegate
            {
                author.Top = desc.Bottom + 14;
                repo.Top = author.Bottom + 4;
                links.Top = repo.Bottom + 4;
                ok.Location = new Point(ClientSize.Width - 16 - ok.Width, links.Bottom + 16);
                ClientSize = new Size(ClientSize.Width, ok.Bottom + 14);
            };
        }
    }
}