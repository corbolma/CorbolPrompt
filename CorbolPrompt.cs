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

    // Domande fatte all'assistente, comuni a tutte le schede e ricordate tra un avvio e l'altro:
    // %APPDATA%\CorbolPrompt\questions.txt, una per riga (a capo scritto \n, barra rovesciata \\)
    static class QuestionHistory
    {
        const int Max = 200;
        static readonly string Path_ = Path.Combine(Settings.Dir, "questions.txt");
        static List<string> items;

        public static List<string> Items
        {
            get
            {
                if (items != null) return items;
                items = new List<string>();
                try
                {
                    if (File.Exists(Path_))
                        foreach (var ln in File.ReadAllLines(Path_, Encoding.UTF8))
                            if (ln.Length > 0) items.Add(Unescape(ln));
                }
                catch { }
                return items;
            }
        }

        public static void Add(string q)
        {
            var l = Items;
            l.Remove(q);   // la stessa domanda rifatta torna in fondo, senza doppioni
            l.Add(q);
            if (l.Count > Max) l.RemoveRange(0, l.Count - Max);
            Save();
        }

        public static void Clear() { Items.Clear(); Save(); }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllLines(Path_, Items.Select(Escape), new UTF8Encoding(false));
            }
            catch { }
        }

        static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }

        static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length) { i++; sb.Append(s[i] == 'n' ? "\r\n" : s[i].ToString()); }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }
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
        public Panel Pane;      // riquadro della vista affiancata (barretta col nome + terminale), creato quando serve
        public Label Header;
        public List<Prompt.Block> Commands = new List<Prompt.Block>();
        public CancellationTokenSource AiCancel;
        public DateTime AiStart;
        public string InputText = "";
        public string CustomName = "";   // nome dato dall'utente; vuoto = nome automatico (shell e cartella)
        public int Notice;               // comando lungo finito senza che la scheda fosse attiva: 1 riuscito, 2 fallito

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
            if (!(e.ToolStrip is ToolStripDropDown)) e.TextColor = e.Item.Tag is Color ? (Color)e.Item.Tag : TabStrip.Text_;   // Tag: colore speciale
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
        ToolStripButton tbTile, tbBroadcast, tbFind;
        bool tiled;       // tutte le schede affiancate in riquadri ridimensionabili
        bool broadcast;   // con le schede affiancate: quello che si scrive va a tutte (non si salva: è facile dimenticarlo acceso)
        // barra di ricerca sopra il terminale (Ctrl+Maiusc+F)
        readonly Panel findBar = new Panel { Dock = DockStyle.Top, BackColor = TabStrip.TabBar, Visible = false };
        readonly TextBox findBox = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(45, 45, 45), ForeColor = TabStrip.Text_ };
        readonly Label findCount = new Label { AutoSize = true, ForeColor = TabStrip.TextDim, BackColor = TabStrip.TabBar };
        readonly CheckBox findCase = new CheckBox { Appearance = Appearance.Button, Text = "Aa", AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = TabStrip.Text_, TabStop = false };
        readonly Button findUp = new Button(), findDown = new Button(), findClose = new Button();
        readonly ContextMenuStrip newMenu = new ContextMenuStrip();

        // pannello dell'assistente
        readonly Label aiTitle = new Label { AutoSize = true, Margin = new Padding(0) };
        readonly Label aiService = new Label { AutoSize = false, AutoEllipsis = true, ForeColor = Color.DimGray, Margin = new Padding(6, 0, 0, 0), Anchor = AnchorStyles.Left | AnchorStyles.Right };
        readonly Panel chatHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly FlowLayoutPanel cmdPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Color.White, Padding = new Padding(0, 4, 0, 0) };
        readonly Label lbBusy = new Label { Dock = DockStyle.Bottom, Height = 0, ForeColor = Color.DimGray, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly TextBox input = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = false };
        readonly Button btSend = new Button(), btNewChat = new Button(), btHistory = new Button(), btMic = new Button();
        int histPos = -1;        // domanda della cronologia mostrata con ↑/↓ (-1 = nessuna: si sta scrivendo)
        string histDraft = "";   // quello che si stava scrivendo prima di sfogliare la cronologia
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
            tbTile = new ToolStripButton("▦") { CheckOnClick = false };
            tbTile.Click += delegate { ToggleTiled(); };
            tbBroadcast = new ToolStripButton("⇶") { CheckOnClick = false };
            tbBroadcast.Click += delegate { ToggleBroadcast(); };
            tbFind = new ToolStripButton("") { CheckOnClick = false };
            tbFind.Click += delegate { if (findBar.Visible) CloseFind(); else OpenFind(); };
            tools.Items.AddRange(new ToolStripItem[] { tbBroadcast, tbTile, tbFind, tbFav, tbAi, more });
            // barra in alto e menu un po' più grandi del resto dell'interfaccia
            var big = new Font("Segoe UI", 10f);
            tools.Font = big;
            more.Font = new Font("Segoe UI", 12f);
            foreach (ToolStripDropDown dd in new ToolStripDropDown[] { tbFav.DropDown, more.DropDown, newMenu, tabMenu, listMenu }) dd.Font = big;
            foreach (ToolStripDropDown dd in new ToolStripDropDown[] { newMenu, tabMenu, listMenu }) KeepOnScreen(dd);
            KeepOnScreen(tbFav);
            KeepOnScreen(more);
            // Con un menu della barra aperto, passare col mouse su un altro pulsante (es. Assistente) lo chiudeva:
            // WinForms lo chiude come se l'app avesse perso il focus. Se l'app è ancora attiva e non si è cliccato,
            // resta aperto; si chiude cliccando di nuovo il pulsante, cliccando altrove, con Esc o cambiando finestra.
            foreach (var dd in new[] { more.DropDown, tbFav.DropDown })
                dd.Closing += (s, e) =>
                {
                    if (e.CloseReason == ToolStripDropDownCloseReason.AppFocusChange && MouseButtons == MouseButtons.None && ActiveForm == this)
                        e.Cancel = true;
                };
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
            tabStrip.Reordered += delegate { if (tiled) PlaceViews(); };
            tiled = Settings.Get("view.tiled", false);
            tabStrip.ListRequested += ShowTabList;
            tabStrip.NewMenuRequested += p => { FillNewMenu(newMenu.Items); newMenu.Show(tabStrip, p); };

            // ---- pannello dell'assistente ----
            BuildAssistantPanel();
            split.Panel1.Controls.Add(termHost);
            split.Panel1.Controls.Add(favBar);
            favBar.Height = S(34);
            BuildFindBar();
            split.Panel1.Controls.Add(findBar);
            Controls.Add(split);
            Controls.Add(topBar);
            ApplyTexts();
            // aggiornamento trovato: compare la voce nel menu ⋯
            Action onUpdate = delegate { try { BeginInvoke((Action)ApplyTexts); } catch { } };
            Updater.Available += onUpdate;
            FormClosed += delegate { Updater.Available -= onUpdate; };

            Load += delegate
            {
                split.SplitterDistance = Math.Max(200, split.Width - S(AiWidth));
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
            Activated += delegate
            {
                UpdateService();
                if (cur != null && cur.Notice != 0) { cur.Notice = 0; tabStrip.Invalidate(); }
                if (notifier != null) notifier.Visible = false;
            };
            FormClosing += (s, e) => OnClosingForm();
            split.SplitterMoved += delegate { TrackAiWidth(); };
            // tornando dalla riduzione a icona il pannello riprende la sua larghezza
            Resize += delegate { if (WindowState != FormWindowState.Minimized && !split.Panel2Collapsed && split.Width > S(500)) { int want = Math.Max(200, split.Width - S(AiWidth)); if (Math.Abs(split.SplitterDistance - want) > S(40) && split.Width - split.SplitterDistance < S(150)) split.SplitterDistance = want; } };
            busyTimer.Tick += delegate { UpdateBusy(); };
        }

        int S(int v) { return (int)Math.Round(v * DeviceDpi / 96.0); }

        // Larghezza dell'assistente (a 96 DPI): l'ultima buona, presa quando si sposta il bordo o si ridimensiona la finestra.
        // Con la finestra ridotta a icona il pannello diventa strettissimo: quella misura non si tiene (né si salva).
        int aiWidth = -1;
        int AiWidth
        {
            get { return Math.Max(250, aiWidth > 0 ? aiWidth : Settings.Get("ai.width", 400)); }
        }

        void TrackAiWidth()
        {
            if (WindowState == FormWindowState.Minimized || split.Panel2Collapsed || split.Width < S(500) || !IsHandleCreated) return;
            int w = (int)Math.Round((split.Width - split.SplitterDistance) * 96.0 / DeviceDpi);
            if (w >= 150) aiWidth = w;
        }

        // ---------- ricerca nel testo ----------

        void BuildFindBar()
        {
            var f = new Font("Segoe UI", 10f);
            findBox.Font = f;
            findCount.Font = f;
            findCase.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            findCase.BackColor = TabStrip.TabBar;
            findCase.TextAlign = ContentAlignment.MiddleCenter;
            findCase.FlatAppearance.BorderColor = Color.FromArgb(75, 75, 75);
            findCase.FlatAppearance.CheckedBackColor = TabStrip.Accent;
            findCase.FlatAppearance.MouseOverBackColor = TabStrip.Hover;
            foreach (var b in new[] { findUp, findDown, findClose })
            {
                b.FlatStyle = FlatStyle.Flat;
                b.ForeColor = TabStrip.Text_;
                b.BackColor = TabStrip.TabBar;
                b.Font = f;
                b.TabStop = false;
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = TabStrip.Hover;
                b.Cursor = Cursors.Hand;
            }
            findUp.Text = "↑"; findDown.Text = "↓"; findClose.Text = "✕";
            findUp.Click += delegate { FindNext(true); };
            findDown.Click += delegate { FindNext(false); };
            findClose.Click += delegate { CloseFind(); };
            findCase.CheckedChanged += delegate { DoFind(); findBox.Focus(); };
            findBox.TextChanged += delegate { DoFind(); };
            findBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(!e.Shift); }
                else if (e.KeyCode == Keys.F3) { e.SuppressKeyPress = true; FindNext(!e.Shift); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CloseFind(); }
            };
            findBar.Controls.AddRange(new Control[] { findBox, findCase, findUp, findDown, findCount, findClose });
            findBar.Resize += delegate { LayoutFindBar(); };
            findBar.VisibleChanged += delegate { LayoutFindBar(); };
        }

        void LayoutFindBar()
        {
            int h = findBox.PreferredHeight;
            findBar.Height = h + S(10);
            int y = S(5), x = S(8);
            findBox.SetBounds(x, y, S(280), h);
            x = findBox.Right + S(6);
            foreach (Control c in new Control[] { findCase, findUp, findDown })
            {
                c.SetBounds(x, y, Math.Max(S(30), c is CheckBox ? c.PreferredSize.Width : S(30)), h);
                x = c.Right + S(2);
            }
            findCount.Location = new Point(x + S(8), y + (h - findCount.Height) / 2);
            findClose.SetBounds(findBar.ClientSize.Width - S(8) - S(30), y, S(30), h);
        }

        void OpenFind()
        {
            if (cur == null) return;
            // testo selezionato su una riga sola: diventa la ricerca
            if (cur.View.HasSelection)
            {
                string sel = cur.View.SelectedText;
                if (sel.Length > 0 && sel.Length < 200 && sel.IndexOf('\n') < 0) findBox.Text = sel;
            }
            findBar.Visible = true;
            tbFind.Checked = true;
            findBox.Focus();
            findBox.SelectAll();
            DoFind();
        }

        void CloseFind()
        {
            findBar.Visible = false;
            tbFind.Checked = false;
            foreach (var t in Tabs) t.View.ClearSearch();
            if (cur != null) cur.View.Focus();
        }

        void DoFind()
        {
            if (cur == null || !findBar.Visible) return;
            if (findBox.Text.Length == 0) cur.View.ClearSearch();
            else cur.View.SetSearch(findBox.Text, findCase.Checked);
            UpdateFindCount();
        }

        void FindNext(bool up)
        {
            if (cur == null) return;
            if (findBox.Text.Length > 0 && cur.View.SearchCount == 0) DoFind();
            cur.View.FindNext(up);
            UpdateFindCount();
        }

        void UpdateFindCount()
        {
            if (cur == null || !findBar.Visible) return;
            int n = cur.View.SearchCount, i = cur.View.SearchIndex;
            findCount.Text = findBox.Text.Length == 0 ? "" : n == 0 ? L.T("Nessun risultato", "No results") : L.F("{0} di {1}", "{0} of {1}", i + 1, n);
            findCount.ForeColor = findBox.Text.Length > 0 && n == 0 ? Color.FromArgb(240, 110, 110) : TabStrip.TextDim;
        }

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
            Settings.Set("ai.width", AiWidth);
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
            view.Changed += delegate { if (tab == cur) UpdateTitle(); else UpdateHeader(tab); tabStrip.Invalidate(); };
            // vista affiancata: un clic in un riquadro rende attiva la sua scheda
            view.GotFocus += delegate { if (tiled && cur != tab && Tabs.Contains(tab)) Activate(tab); };
            view.ZoomRequested += n => Zoom(n);
            view.ExplainRequested += m => ExplainCommand(tab, m);
            view.ShellId = shell.Id;
            view.CommandDone += m => OnCommandDone(tab, m);
            view.SearchChanged += delegate { if (tab == cur) UpdateFindCount(); };
            // scrivi in tutte le schede affiancate: quello che si scrive in quella attiva va anche alle altre
            view.UserInput += (s, paste) =>
            {
                if (!broadcast || !tiled || tab != cur) return;
                foreach (var o in Tabs) if (o != tab && !o.Exited && o.Session != null) o.View.Mirror(s, paste);
            };
            term.Reply += s => { var ses = tab.Session; if (ses != null) ses.Send(s); };
            term.CwdChanged += delegate { };
            Tabs.Insert(cur != null ? Tabs.IndexOf(cur) + 1 : Tabs.Count, tab);
            if (tiled) PlaceViews();   // prima della sessione: la shell parte già con la misura del riquadro
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
                if (!tiled) cur.View.Visible = false;
            }
            cur = tab;
            tab.Notice = 0;
            tab.View.Visible = true;
            if (!tiled) tab.View.BringToFront();
            UpdateHeaders();
            tabStrip.Active = tab;
            tabStrip.EnsureVisible(tab);
            if (changed)
            {
                ShowChat(tab);
                UpdateFavBar();
                input.Text = tab.InputText;
                // la ricerca aperta segue la scheda attiva
                if (findBar.Visible) { foreach (var t in Tabs) if (t != tab) t.View.ClearSearch(); DoFind(); }
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
            if (tab.View.Parent != null) tab.View.Parent.Controls.Remove(tab.View);
            tab.View.Dispose();
            if (tab.Pane != null) { if (tab.Pane.Parent != null) tab.Pane.Parent.Controls.Remove(tab.Pane); tab.Pane.Dispose(); tab.Pane = null; }
            if (tiled && Tabs.Count > 0) PlaceViews();
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
            if (cur != null) UpdateHeader(cur);
        }

        // ---------- vista affiancata ----------

        static readonly Color HeaderBack = Color.FromArgb(36, 36, 36), HeaderActive = Color.FromArgb(18, 92, 40),
                              HeaderBroadcast = Color.FromArgb(110, 62, 0), HeaderBroadcastActive = Color.FromArgb(190, 105, 0);

        void ToggleTiled()
        {
            tiled = !tiled;
            Settings.Set("view.tiled", tiled);
            Settings.Save();
            if (!tiled) broadcast = false;
            PlaceViews();
            UpdateTileButtons();
            if (cur != null) { UpdateHeaders(); cur.View.Focus(); }
        }

        // Scrivi in tutte le schede: ha senso solo vedendole tutte, quindi accende anche la vista affiancata
        void ToggleBroadcast()
        {
            if (!tiled && !broadcast) ToggleTiled();
            broadcast = !broadcast;
            UpdateTileButtons();
            UpdateHeaders();
            if (cur != null) cur.View.Focus();
        }

        void UpdateTileButtons()
        {
            tbTile.Checked = tiled;
            tbBroadcast.Visible = tiled;
            tbBroadcast.Checked = broadcast;
            tbBroadcast.Tag = broadcast ? (object)Color.FromArgb(255, 170, 60) : null;
            tbBroadcast.Invalidate();
        }

        void EnsurePane(TermTab tab)
        {
            if (tab.Pane != null) return;
            var pane = new Panel { Dock = DockStyle.Fill, BackColor = TermView.Background, Margin = new Padding(0) };
            var header = new Label
            {
                Dock = DockStyle.Top, Height = S(24), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(S(8), 0, 0, 0), Font = new Font("Segoe UI", 9.5f), BackColor = HeaderBack, ForeColor = TabStrip.TextDim, UseMnemonic = false
            };
            header.MouseDown += (s, e) =>
            {
                Activate(tab);
                if (e.Button == MouseButtons.Right) ShowTabMenu(tab, tabStrip.PointToClient(Cursor.Position));
            };
            header.DoubleClick += delegate { BeginRename(tab); };
            tab.Pane = pane;
            tab.Header = header;
            pane.Controls.Add(header);
        }

        void UpdateHeader(TermTab t)
        {
            if (t.Header == null) return;
            bool bc = broadcast && !t.Exited;
            string text = (bc ? "⇶  " : "") + t.Title + (t.CustomName.Length > 0 ? "   ·   " + t.AutoTitle : "");
            if (t.Header.Text != text) t.Header.Text = text;
            bool act = t == cur;
            t.Header.BackColor = bc ? (act ? HeaderBroadcastActive : HeaderBroadcast) : act ? HeaderActive : HeaderBack;
            t.Header.ForeColor = act || bc ? Color.White : TabStrip.TextDim;
        }

        void UpdateHeaders() { foreach (var t in Tabs) UpdateHeader(t); }

        // Mette i terminali al loro posto: uno alla volta, oppure tutti in una griglia di riquadri con bordi trascinabili
        void PlaceViews()
        {
            termHost.SuspendLayout();
            foreach (var t in Tabs)
            {
                if (t.View.Parent != null) t.View.Parent.Controls.Remove(t.View);
                if (t.Pane != null && t.Pane.Parent != null) t.Pane.Parent.Controls.Remove(t.Pane);
            }
            // restano solo i contenitori della griglia di prima: si buttano (terminali e riquadri sono già staccati)
            foreach (var c in termHost.Controls.Cast<Control>().ToList()) { termHost.Controls.Remove(c); c.Dispose(); }
            SplitContainer root = null;
            if (!tiled)
            {
                foreach (var t in Tabs)
                {
                    t.View.Dock = DockStyle.Fill;
                    t.View.Visible = t == cur;
                    termHost.Controls.Add(t.View);
                }
                if (cur != null) cur.View.BringToFront();
            }
            else
            {
                var panes = new List<Control>();
                foreach (var t in Tabs)
                {
                    EnsurePane(t);
                    t.View.Dock = DockStyle.Fill;
                    t.View.Visible = true;
                    t.Pane.Controls.Add(t.View);
                    t.View.BringToFront();   // il terminale riempie lo spazio sotto la barretta
                    panes.Add(t.Pane);
                }
                // griglia: colonne = radice quadrata arrotondata in su; l'ultima riga può avere meno riquadri, più larghi
                int n = panes.Count, cols = (int)Math.Ceiling(Math.Sqrt(n)), rowsN = (int)Math.Ceiling(n / (double)cols);
                var rows = new List<Control>();
                for (int r = 0; r < rowsN; r++) rows.Add(Chain(panes.Skip(r * cols).Take(cols).ToList(), Orientation.Vertical));
                var top = Chain(rows, Orientation.Horizontal);
                top.Dock = DockStyle.Fill;
                termHost.Controls.Add(top);
                root = top as SplitContainer;
                UpdateHeaders();
            }
            termHost.ResumeLayout(true);
            if (root != null) Balance(root);
        }

        // Catena di riquadri affiancati (Vertical) o impilati (Horizontal) con bordi trascinabili
        Control Chain(List<Control> items, Orientation o)
        {
            if (items.Count == 1) { items[0].Dock = DockStyle.Fill; return items[0]; }
            var sc = new SplitContainer { Dock = DockStyle.Fill, Orientation = o, SplitterWidth = S(5), BackColor = Color.FromArgb(70, 70, 70), Tag = items.Count };
            sc.Panel1.BackColor = sc.Panel2.BackColor = TermView.Background;
            sc.Panel1MinSize = sc.Panel2MinSize = S(60);
            items[0].Dock = DockStyle.Fill;
            sc.Panel1.Controls.Add(items[0]);
            var rest = Chain(items.Skip(1).ToList(), o);
            sc.Panel2.Controls.Add(rest);
            return sc;
        }

        // Parti uguali: ogni catena dà al primo riquadro 1/k dello spazio, poi si continua nel resto
        void Balance(Control c)
        {
            var sc = c as SplitContainer;
            if (sc != null)
            {
                int k = sc.Tag is int ? (int)sc.Tag : 2;
                int size = sc.Orientation == Orientation.Vertical ? sc.Width : sc.Height;
                int dist = (size - sc.SplitterWidth * (k - 1)) / k;
                try { if (dist > sc.Panel1MinSize && size - dist - sc.SplitterWidth > sc.Panel2MinSize) sc.SplitterDistance = dist; } catch { }
                sc.PerformLayout();
                foreach (Control child in sc.Panel1.Controls) Balance(child);
                foreach (Control child in sc.Panel2.Controls) Balance(child);
            }
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
            var tileItem = new ToolStripMenuItem(L.T("Affianca tutte le schede", "Show all tabs side by side")) { Checked = tiled, ShortcutKeyDisplayString = L.T("Ctrl+Maiusc+G", "Ctrl+Shift+G") };
            tileItem.Click += delegate { ToggleTiled(); };
            var bcItem = new ToolStripMenuItem(L.T("Scrivi in tutte le schede affiancate", "Type in all tiled tabs")) { ShortcutKeyDisplayString = L.T("Ctrl+Maiusc+B", "Ctrl+Shift+B") };
            bcItem.Click += delegate { ToggleBroadcast(); };
            var findItem = new ToolStripMenuItem(L.T("Cerca nel testo...", "Find in text...")) { ShortcutKeyDisplayString = L.T("Ctrl+Maiusc+F", "Ctrl+Shift+F") };
            findItem.Click += delegate { OpenFind(); };
            more.DropDownOpening += delegate { tileItem.Checked = tiled; bcItem.Checked = broadcast; };
            var showBar = new ToolStripMenuItem(L.T("Suggerisci i preferiti sotto il terminale", "Suggest favorites below the terminal")) { Checked = Settings.Get("fav.bar", true), CheckOnClick = true };
            showBar.CheckedChanged += delegate { Settings.Set("fav.bar", showBar.Checked); Settings.Save(); UpdateFavBar(); };
            var notify = new ToolStripMenuItem(L.T("Avvisa quando finisce un comando lungo", "Notify when a long command finishes")) { Checked = Settings.Get("notify.done", true), CheckOnClick = true };
            notify.CheckedChanged += delegate { Settings.Set("notify.done", notify.Checked); Settings.Save(); };
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
                                              new ToolStripSeparator(), findItem, tileItem, bcItem, lang, showBar, notify, new ToolStripSeparator(), welcome, guide, lispGuide, keys, new ToolStripSeparator(), check, autoUpd, about });
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
            UpdateTileButtons();
            tbTile.ToolTipText = L.T("Affianca tutte le schede (Ctrl+Maiusc+G)", "Show all tabs side by side (Ctrl+Shift+G)");
            tbBroadcast.ToolTipText = L.T("Scrivi in tutte le schede affiancate: quello che digiti va a tutte insieme (Ctrl+Maiusc+B)",
                                          "Type in all tiled tabs: what you type goes to all of them at once (Ctrl+Shift+B)");
            findBox.AccessibleName = L.T("Cerca", "Find");
            tbFind.Text = "🔍 " + L.T("Cerca", "Find");
            tbFind.ToolTipText = L.T("Cerca nel testo della scheda (Ctrl+Maiusc+F)", "Find in the tab's text (Ctrl+Shift+F)");
            tips.SetToolTip(findBox, L.T("Cerca nel testo della scheda · Invio = precedente (più in alto), Maiusc+Invio = successivo, Esc = chiudi",
                                         "Find in the tab's text · Enter = previous (higher up), Shift+Enter = next, Esc = close"));
            tips.SetToolTip(findCase, L.T("Maiuscole e minuscole distinte", "Match case"));
            tips.SetToolTip(findUp, L.T("Precedente (Invio)", "Previous (Enter)"));
            tips.SetToolTip(findDown, L.T("Successivo (Maiusc+Invio)", "Next (Shift+Enter)"));
            tips.SetToolTip(findClose, L.T("Chiudi (Esc)", "Close (Esc)"));
            UpdateFindCount();
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
            btNewChat.Text = L.T("Nuova chat", "New chat");
            btHistory.Text = "🕘 " + L.T("Recenti", "Recent");
            btMic.Text = "🎤 " + L.T("Detta", "Dictate");
            tips.SetToolTip(btMic, L.T("Detta la domanda (dettatura vocale di Windows, Win+H)", "Dictate the question (Windows voice typing, Win+H)"));
            tips.SetToolTip(btNewChat, L.T("Nuova conversazione: l'assistente dimentica quella di questa scheda", "New conversation: the assistant forgets the one of this tab"));
            tips.SetToolTip(input, L.T("Invio = chiedi, Maiusc+Invio = a capo, ↑/↓ = domande già fatte, Esc = torna al terminale", "Enter = ask, Shift+Enter = new line, ↑/↓ = previous questions, Esc = back to the terminal"));
            tips.SetToolTip(btHistory, L.T("Domande recenti", "Recent questions"));
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
                "Ctrl+Maiusc+A  assistente\nCtrl+Maiusc+P  preferiti (Maiusc+clic incolla senza eseguire)\nCtrl+Maiusc+G  affianca tutte le schede / una alla volta\n" +
                "Ctrl+Maiusc+B  scrivi in tutte le schede affiancate\nCtrl+Maiusc+F  cerca nel testo (F3 / Maiusc+F3 risultato precedente / successivo)\nCtrl+Maiusc+↑ / ↓  comando precedente / successivo\n" +
                "Maiusc+PagSu / PagGiù o rotellina  scorre la cronologia dello schermo\nCtrl++ / Ctrl+- / Ctrl+0  dimensione del carattere\n" +
                "Doppio clic  seleziona una parola\nCtrl+clic su un indirizzo o un percorso  lo apre\nClic sul segno rosso a sinistra di un comando  l'assistente spiega l'errore\nTrascinare un file sul terminale  ne scrive il percorso",
                "Ctrl+Shift+T  new tab\nCtrl+Shift+W  close the tab\nCtrl+Tab / Ctrl+Shift+Tab  next / previous tab\n" +
                "Ctrl+C with text selected  copy (without a selection it interrupts the command)\nCtrl+V or right click → Paste  paste\n" +
                "Ctrl+Shift+A  assistant\nCtrl+Shift+P  favorites (Shift+click pastes without running)\nCtrl+Shift+G  all tabs side by side / one at a time\n" +
                "Ctrl+Shift+B  type in all tiled tabs\nCtrl+Shift+F  find in text (F3 / Shift+F3 previous / next result)\nCtrl+Shift+↑ / ↓  previous / next command\n" +
                "Shift+PgUp / PgDn or mouse wheel  scroll the screen history\nCtrl++ / Ctrl+- / Ctrl+0  text size\n" +
                "Double click  select a word\nCtrl+click on an address or a path  open it\nClick the red mark left of a command  the assistant explains the error\nDrag a file onto the terminal  types its path"), L.T("Scorciatoie da tastiera", "Keyboard shortcuts"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Con più schermi WinForms mette il menu sullo schermo dove ne cade la parte più grande, non su quello del pulsante:
        // vicino al bordo tra due monitor menu e sottomenu finivano sull'altro schermo. Spostarli dopo l'apertura non serve
        // (WinForms ignora la nuova posizione): si sceglie la direzione PRIMA che si aprano, in modo che ci stiano
        // sullo schermo della voce da cui partono (destra o sinistra, sotto o sopra). Vale per i sottomenu di ogni livello.
        readonly HashSet<ToolStripDropDown> onScreenMenus = new HashSet<ToolStripDropDown>();
        readonly HashSet<ToolStripDropDownItem> onScreenItems = new HashSet<ToolStripDropDownItem>();

        // menu col tasto destro (ContextMenuStrip): i suoi sottomenu
        void KeepOnScreen(ToolStripDropDown dd)
        {
            if (!onScreenMenus.Add(dd)) return;
            dd.Disposed += delegate { onScreenMenus.Remove(dd); };
            dd.Opened += delegate { KeepChildrenOnScreen(dd); };
        }

        void KeepChildrenOnScreen(ToolStripDropDown dd)
        {
            foreach (var it in dd.Items.OfType<ToolStripDropDownItem>()) KeepOnScreen(it);
        }

        // pulsante della barra o voce con sottomenu
        void KeepOnScreen(ToolStripDropDownItem item)
        {
            if (!onScreenItems.Add(item)) return;
            item.Disposed += delegate { onScreenItems.Remove(item); };
            // registrato dopo chi riempie il menu all'apertura: la misura è quella vera
            item.DropDownOpening += delegate { ChooseDropDownDirection(item); };
            item.DropDownOpened += delegate { KeepChildrenOnScreen(item.DropDown); };   // anche i sottomenu creati all'apertura
        }

        static void ChooseDropDownDirection(ToolStripDropDownItem item)
        {
            if (item.Owner == null) return;
            var anchor = item.Owner.RectangleToScreen(item.Bounds);
            var wa = Screen.FromRectangle(anchor).WorkingArea;
            var sz = item.DropDown.GetPreferredSize(Size.Empty);
            if (item.IsOnDropDown)
                item.DropDownDirection = anchor.Right + sz.Width <= wa.Right || anchor.Left - sz.Width < wa.Left
                    ? ToolStripDropDownDirection.Right : ToolStripDropDownDirection.Left;
            else
            {
                bool right = anchor.Left + sz.Width <= wa.Right;
                bool below = anchor.Bottom + sz.Height <= wa.Bottom || anchor.Top - sz.Height < wa.Top;
                item.DropDownDirection = below ? (right ? ToolStripDropDownDirection.BelowRight : ToolStripDropDownDirection.BelowLeft)
                                               : (right ? ToolStripDropDownDirection.AboveRight : ToolStripDropDownDirection.AboveLeft);
            }
        }

        void BuildTermMenu()
        {
            var old = termMenu;
            termMenu = new ContextMenuStrip { Font = new Font("Segoe UI", 10f) };
            KeepOnScreen(termMenu);
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
            var find = new ToolStripMenuItem(L.T("Cerca nel testo...", "Find in text...")) { ShortcutKeyDisplayString = L.T("Ctrl+Maiusc+F", "Ctrl+Shift+F") };
            find.Click += delegate { OpenFind(); };
            // voci che compaiono solo se il clic destro è su un link o su un comando fallito
            var openLink = new ToolStripMenuItem("") { ShortcutKeyDisplayString = L.T("Ctrl+clic", "Ctrl+click"), Font = new Font(termMenu.Font, FontStyle.Bold) };
            openLink.Click += delegate { if (cur != null && cur.View.MenuLink != null) TermView.OpenLink(cur.View.MenuLink); };
            var copyLink = new ToolStripMenuItem("");
            copyLink.Click += delegate { if (cur != null && cur.View.MenuLink != null) try { Clipboard.SetText(cur.View.MenuLink.Target); } catch { } };
            var showLink = new ToolStripMenuItem(L.T("Mostra in Esplora file", "Show in File Explorer"));
            showLink.Click += delegate { if (cur != null && cur.View.MenuLink != null) TermView.ShowInExplorer(cur.View.MenuLink); };
            var explainFail = new ToolStripMenuItem(L.T("Spiega l'errore di questo comando", "Explain this command's error")) { Font = new Font(termMenu.Font, FontStyle.Bold) };
            explainFail.Click += delegate { if (cur != null && cur.View.MenuFailed != null) ExplainCommand(cur, cur.View.MenuFailed); };
            var copyCmd = new ToolStripMenuItem(L.T("Copia il comando", "Copy the command"));
            copyCmd.Click += delegate { var m = cur != null ? cur.View.MenuCommand : null; if (m == null) return; string s; lock (cur.Term.Sync) s = cur.Term.CommandText(m); try { if (s.Length > 0) Clipboard.SetText(s); } catch { } };
            var copyOut = new ToolStripMenuItem(L.T("Copia l'output del comando", "Copy the command's output"));
            copyOut.Click += delegate { var m = cur != null ? cur.View.MenuCommand : null; if (m == null) return; string s; lock (cur.Term.Sync) s = cur.Term.CommandOutput(m, 100000); try { if (s.Length > 0) Clipboard.SetText(s.Replace("\n", "\r\n")); } catch { } };
            var sepLink = new ToolStripSeparator();
            termMenu.Opening += delegate
            {
                bool sel = cur != null && cur.View.HasSelection;
                copy.Enabled = sel;
                fav.Enabled = sel;
                explain.Text = sel ? L.T("Spiega la selezione con l'assistente", "Explain the selection with the assistant") : L.T("Spiega lo schermo con l'assistente", "Explain the screen with the assistant");
                var k = cur != null ? cur.View.MenuLink : null;
                var m = cur != null ? cur.View.MenuFailed : null;
                openLink.Visible = copyLink.Visible = k != null;
                showLink.Visible = k != null && !k.IsUrl;
                explainFail.Visible = m != null;
                var mc = cur != null ? cur.View.MenuCommand : null;
                copyCmd.Visible = copyOut.Visible = mc != null;
                sepLink.Visible = k != null || m != null || mc != null;
                if (k != null)
                {
                    openLink.Text = k.IsUrl ? L.T("Apri il link", "Open link") : k.IsDir ? L.T("Apri la cartella", "Open folder") : L.T("Apri il file", "Open file");
                    copyLink.Text = k.IsUrl ? L.T("Copia il link", "Copy link") : L.T("Copia il percorso", "Copy path");
                }
            };
            termMenu.Items.AddRange(new ToolStripItem[] { openLink, copyLink, showLink, explainFail, copyCmd, copyOut, sepLink,
                copy, paste, all, fav, new ToolStripSeparator(), find, explain, ask, new ToolStripSeparator(), clear });
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
                case Keys.Control | Keys.Shift | Keys.G: ToggleTiled(); return true;
                case Keys.Control | Keys.Shift | Keys.B: ToggleBroadcast(); return true;
                case Keys.Control | Keys.Shift | Keys.F: OpenFind(); return true;
                case Keys.F3: case Keys.Shift | Keys.F3: if (findBar.Visible && findBox.Text.Length > 0) { FindNext(keyData == Keys.F3); return true; } break;
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
            // Pulsanti sotto la domanda, grandi e col testo scritto, in 4 colonne uguali che seguono la larghezza del pannello
            // (come in Corbol3D): detta, domande recenti, nuova conversazione, Chiedi (che diventa Ferma mentre l'assistente lavora)
            var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = S(44), ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 6, 0, 0), BackColor = Color.White };
            for (int i = 0; i < 4; i++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            foreach (var b in new[] { btMic, btHistory, btNewChat, btSend })
            {
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(0, 0, S(6), 0);
                b.UseMnemonic = false;
            }
            btSend.Margin = new Padding(0);
            btSend.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btSend.Click += delegate { SendOrStop(); };
            btNewChat.Click += delegate { NewConversation(); };
            btHistory.Click += delegate { ShowQuestionMenu(); };
            btMic.Click += delegate { Dictate(); };
            buttons.Controls.Add(btMic, 0, 0);
            buttons.Controls.Add(btHistory, 1, 0);
            buttons.Controls.Add(btNewChat, 2, 0);
            buttons.Controls.Add(btSend, 3, 0);
            input.Font = new Font("Segoe UI", 9.5f);
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; SendOrStop(); }
                else if (e.KeyCode == Keys.Escape && cur != null) { e.SuppressKeyPress = true; cur.View.Focus(); }
                // ↑ sulla prima riga / ↓ sull'ultima: sfoglia le domande già fatte, come nel terminale
                else if (e.KeyCode == Keys.Up && e.Modifiers == Keys.None && input.GetLineFromCharIndex(input.SelectionStart) == 0)
                { if (BrowseQuestions(-1)) e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && e.Modifiers == Keys.None && input.GetLineFromCharIndex(input.SelectionStart) == input.GetLineFromCharIndex(input.TextLength))
                { if (BrowseQuestions(1)) e.Handled = e.SuppressKeyPress = true; }
            };
            // se si modifica una domanda ripresa, diventa la nuova bozza
            input.TextChanged += delegate { if (histPos >= 0 && !browsing) histPos = -1; };
            inputRow.Controls.Add(input);
            lbBusy.Font = Font;

            panel.Controls.Add(chatHost);
            panel.Controls.Add(cmdPanel);
            panel.Controls.Add(lbBusy);
            panel.Controls.Add(inputRow);
            panel.Controls.Add(buttons);   // aggiunto dopo la domanda: prende posto prima, in fondo al pannello
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
            split.SplitterDistance = Math.Max(200, split.Width - S(AiWidth));
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
            QuestionHistory.Add(text);
            histPos = -1; histDraft = "";
            input.Clear();
            Ask(text);
        }

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);

        // Dettatura: cursore in fondo alla domanda e dettatura vocale di Windows (Win+H), che scrive nel campo col focus
        void Dictate()
        {
            FocusAssistant();
            input.SelectionStart = input.TextLength;
            const byte LWin = 0x5B, H = 0x48;
            const int KeyUp = 2;
            keybd_event(LWin, 0, 0, IntPtr.Zero);
            keybd_event(H, 0, 0, IntPtr.Zero);
            keybd_event(H, 0, KeyUp, IntPtr.Zero);
            keybd_event(LWin, 0, KeyUp, IntPtr.Zero);
        }

        bool browsing;   // il testo lo sta cambiando la cronologia, non l'utente

        // dir -1 = domanda precedente, +1 = successiva; false se non c'è niente da sfogliare (la freccia muove il cursore)
        bool BrowseQuestions(int dir)
        {
            var items = QuestionHistory.Items;
            if (items.Count == 0) return false;
            int pos;
            if (histPos < 0)
            {
                if (dir > 0) return false;
                histDraft = input.Text;
                pos = items.Count - 1;
            }
            else
            {
                pos = histPos + dir;
                if (pos < 0) return true;   // già alla più vecchia
            }
            browsing = true;
            if (pos >= items.Count) { histPos = -1; input.Text = histDraft; }
            else { histPos = pos; input.Text = items[pos]; }
            browsing = false;
            input.SelectionStart = input.TextLength;
            return true;
        }

        void ShowQuestionMenu()
        {
            var menu = new ContextMenuStrip();
            var items = QuestionHistory.Items;
            if (items.Count == 0) menu.Items.Add(new ToolStripMenuItem(L.T("Nessuna domanda fatta finora", "No questions asked yet")) { Enabled = false });
            for (int i = items.Count - 1; i >= Math.Max(0, items.Count - 25); i--)
            {
                string q = items[i];
                string label = q.Replace("\r", "").Replace("\n", " ⏎ ");
                if (label.Length > 80) label = label.Substring(0, 79) + "…";
                var mi = new ToolStripMenuItem(label.Replace("&", "&&"));
                if (q.Length > 80 || q.Contains("\n")) mi.ToolTipText = q.Length > 600 ? q.Substring(0, 600) + "…" : q;
                mi.Click += delegate
                {
                    browsing = true; input.Text = q; browsing = false;
                    histPos = -1;
                    FocusAssistant();
                    input.SelectionStart = input.TextLength;
                };
                menu.Items.Add(mi);
            }
            if (items.Count > 0)
            {
                menu.Items.Add(new ToolStripSeparator());
                var clear = new ToolStripMenuItem(L.T("Cancella la cronologia delle domande", "Clear the question history"));
                clear.Click += delegate { QuestionHistory.Clear(); histPos = -1; };
                menu.Items.Add(clear);
            }
            menu.Closed += delegate { BeginInvoke((Action)menu.Dispose); };
            KeepOnScreen(menu);
            menu.Show(btHistory, Point.Empty, ToolStripDropDownDirection.AboveRight);
        }

        static readonly Regex TabWords = new Regex(@"\b(schede|tabs|altra scheda|other tab)\b", RegexOptions.IgnoreCase);

        // ---------- avviso a fine comando ----------

        [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASHWINFO fi);
        [StructLayout(LayoutKind.Sequential)] struct FLASHWINFO { public int cbSize; public IntPtr hwnd; public int dwFlags, uCount, dwTimeout; }

        NotifyIcon notifier;
        TermTab notifyTab;
        const int LongCommandSeconds = 10;

        // Un comando lungo (almeno 10 s) è finito: se la scheda non è quella attiva le compare un pallino;
        // se CorbolPrompt non è in primo piano, la barra delle applicazioni lampeggia e arriva una notifica di Windows
        void OnCommandDone(TermTab tab, CmdMark m)
        {
            if (closing || !Tabs.Contains(tab) || m.Started == DateTime.MinValue) return;
            var took = DateTime.Now - m.Started;
            if (took.TotalSeconds < LongCommandSeconds) return;
            bool front = ActiveForm == this && WindowState != FormWindowState.Minimized;
            if (front && tab == cur) return;
            if (tab != cur || !front) { tab.Notice = m.Failed ? 2 : 1; tabStrip.Invalidate(); }
            if (front || !Settings.Get("notify.done", true)) return;
            try
            {
                var fi = new FLASHWINFO { cbSize = Marshal.SizeOf(typeof(FLASHWINFO)), hwnd = Handle, dwFlags = 3 | 12, uCount = 0, dwTimeout = 0 };   // finché non si torna alla finestra
                FlashWindowEx(ref fi);
            }
            catch { }
            string cmd;
            lock (tab.Term.Sync) cmd = tab.Term.CommandText(m);
            if (cmd.Length > 80) cmd = cmd.Substring(0, 79) + "…";
            string when = took.TotalMinutes >= 1 ? L.F("{0} min {1} s", "{0} min {1} s", (int)took.TotalMinutes, took.Seconds) : L.F("{0} s", "{0} s", (int)took.TotalSeconds);
            string title = m.Failed ? L.F("Comando fallito (codice {0})", "Command failed (exit code {0})", m.ExitCode) : L.T("Comando finito", "Command finished");
            if (notifier == null)
            {
                notifier = new NotifyIcon { Icon = Icon, Text = "CorbolPrompt" };
                notifier.BalloonTipClicked += delegate { BringBack(notifyTab); };
                notifier.Click += delegate { BringBack(notifyTab); };
                notifier.BalloonTipClosed += delegate { notifier.Visible = false; };
                FormClosed += delegate { notifier.Visible = false; notifier.Dispose(); };
            }
            notifyTab = tab;
            notifier.Visible = true;
            notifier.ShowBalloonTip(8000, title, cmd + "\n" + tab.Title + " · " + when, m.Failed ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        void BringBack(TermTab tab)
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            if (tab != null && Tabs.Contains(tab)) Activate(tab);
            if (notifier != null) notifier.Visible = false;
        }

        // Comando fallito (segno rosso o pulsante "Spiega l'errore"): all'assistente vanno il comando, il codice e il suo output
        void ExplainCommand(TermTab t, CmdMark m)
        {
            if (t == null || !Tabs.Contains(t)) return;
            string cmd, output;
            lock (t.Term.Sync) { cmd = t.Term.CommandText(m); output = t.Term.CommandOutput(m, 150); }
            if (output.Length > 8000) output = "…" + output.Substring(output.Length - 8000);
            if (t != cur) Activate(t);
            if (t.Busy) { ShowAssistant(); return; }
            string shortCmd = cmd.Length > 60 ? cmd.Substring(0, 59) + "…" : cmd;
            Ask(m.ExitCode == 1 ? L.F("Perché {0} non ha funzionato?", "Why did {0} fail?", shortCmd.Length > 0 ? "`" + shortCmd + "`" : L.T("il comando", "the command"))
                                : L.F("Perché {0} non ha funzionato (codice {1})?", "Why did {0} fail (exit code {1})?", shortCmd.Length > 0 ? "`" + shortCmd + "`" : L.T("il comando", "the command"), m.ExitCode),
                "The user ran this command and it failed with exit code " + m.ExitCode + ":\n```\n" + cmd + "\n```\nIts output:\n```text\n" +
                (output.Length > 0 ? output : "(no output)") + "\n```\nExplain briefly what went wrong and, if possible, give the corrected command.\n");
        }

        // Testo per l'assistente: scheda, shell, cartella, ultime righe, selezione, altre schede, richiesta
        string Context(TermTab t, string request, string details = null)
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
            if (details != null) sb.Append("\n").Append(details);
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

        // details: informazioni in più per l'assistente che non si mostrano nella conversazione (es. l'output di un comando fallito)
        void Ask(string text, string details = null)
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
            req.Messages.Add(new AiMessage("user", Context(t, text, details)));
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