// Preferiti: comandi salvati da riusare con un clic, in %APPDATA%\CorbolPrompt\favorites.json.
// Ognuno ha un nome, una descrizione (scritta a mano, presa dalla spiegazione dell'assistente o chiesta a lui), la shell a cui è destinato ("" = quella della scheda attiva, oppure powershell, cmd, bash, lisp)
// e il comando, anche su più righe. Finestra per aggiungerli, modificarli, riordinarli ed eliminarli.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CorbolPrompt
{
    class Favorite
    {
        public string Name = "", Lang = "", Code = "", Description = "";
        public Favorite Copy() { return new Favorite { Name = Name, Lang = Lang, Code = Code, Description = Description }; }
    }

    static class Favorites
    {
        static readonly string File_ = Path.Combine(Settings.Dir, "favorites.json");
        static List<Favorite> list;

        public static List<Favorite> All
        {
            get
            {
                if (list == null) Load();
                return list;
            }
        }

        static void Load()
        {
            list = new List<Favorite>();
            try
            {
                if (!File.Exists(File_)) return;
                var items = new JavaScriptSerializer().Deserialize<List<Favorite>>(File.ReadAllText(File_, Encoding.UTF8));
                if (items != null) list.AddRange(items.Where(f => f != null && !string.IsNullOrEmpty(f.Code)));
                foreach (var f in list) if (f.Description == null) f.Description = "";
            }
            catch { }
        }

        public static void Replace(List<Favorite> items)
        {
            list = items;
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(File_, new JavaScriptSerializer().Serialize(list), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Impossibile salvare i preferiti:", "Could not save the favorites:") + " " + ex.Message, "CorbolPrompt", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Linguaggi tra cui scegliere, con il nome da mostrare
        public static readonly string[] Langs = { "", "powershell", "cmd", "bash", "lisp" };
        public static string LangName(string lang)
        {
            switch (lang ?? "")
            {
                case "powershell": return "PowerShell";
                case "cmd": return L.T("Prompt dei comandi", "Command Prompt");
                case "bash": return "bash (WSL, Git Bash)";
                case "lisp": return "Lisp";
                default: return L.T("Qualsiasi (la scheda attiva)", "Any (the active tab)");
            }
        }

        // Nome proposto per un comando nuovo: la prima riga, accorciata
        public static string SuggestName(string code)
        {
            string first = (code ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            return first.Length > 40 ? first.Substring(0, 39) + "…" : first;
        }
    }

    // Gestione dei preferiti: elenco a sinistra, nome, shell e comando a destra. Le modifiche valgono con OK.
    class FavoritesForm : Form
    {
        readonly List<Favorite> items;
        readonly ListBox lst = new ListBox { IntegralHeight = false };
        readonly TextBox tName = new TextBox(), tCode = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, AcceptsTab = true, Font = new Font("Consolas", 10f) };
        readonly ComboBox cLang = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox tDesc = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, WordWrap = true };
        readonly Button bDescribe = new Button { AutoSize = true };
        System.Threading.CancellationTokenSource describing;
        readonly Button bNew = new Button(), bDel = new Button(), bUp = new Button(), bDown = new Button();
        bool loading;

        public FavoritesForm(Favorite add)
        {
            items = Favorites.All.Select(f => f.Copy()).ToList();
            Text = L.T("Preferiti", "Favorites");
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleDimensions = new SizeF(96f, 96f); AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            ClientSize = new Size(680, 480);
            MinimumSize = new Size(560, 420);

            lst.Location = new Point(14, 14); lst.Size = new Size(210, 380);
            lst.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            bNew.Text = L.T("Nuovo", "New"); bDel.Text = L.T("Elimina", "Delete"); bUp.Text = "↑"; bDown.Text = "↓";
            int by = 402;
            bNew.SetBounds(14, by, 70, 28); bDel.SetBounds(88, by, 70, 28); bUp.SetBounds(162, by, 30, 28); bDown.SetBounds(194, by, 30, 28);
            foreach (var b in new[] { bNew, bDel, bUp, bDown }) b.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

            var lName = new Label { Text = L.T("Nome:", "Name:"), AutoSize = true, Location = new Point(240, 18) };
            tName.SetBounds(320, 14, 346, 24); tName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var lLang = new Label { Text = L.T("Shell:", "Shell:"), AutoSize = true, Location = new Point(240, 52) };
            cLang.SetBounds(320, 48, 230, 24);
            foreach (var l in Favorites.Langs) cLang.Items.Add(Favorites.LangName(l));
            var lDesc = new Label { Text = L.T("Descrizione:", "Description:"), AutoSize = true, Location = new Point(240, 88) };
            bDescribe.Text = L.T("Descrivi con l'assistente", "Describe with the assistant");
            bDescribe.Location = new Point(666 - 180, 80); bDescribe.MinimumSize = new Size(180, 26);
            bDescribe.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bDescribe.Click += delegate { Describe(); };
            tDesc.SetBounds(240, 110, 426, 62); tDesc.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var lCode = new Label { Text = L.T("Comando:", "Command:"), AutoSize = true, Location = new Point(240, 182) };
            tCode.SetBounds(240, 202, 426, 192); tCode.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            var hint = new Label
            {
                Text = L.T("Nel menu ★: clic = esegue, Maiusc+clic = incolla.",
                           "In the ★ menu: click = run, Shift+click = paste."),
                AutoSize = false, ForeColor = Color.DimGray, Location = new Point(240, 398), Size = new Size(426, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };

            var ok = new Button { Text = "OK", Size = new Size(90, 30), Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            var cancel = new Button { Text = L.T("Annulla", "Cancel"), Size = new Size(90, 30), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
            ok.Location = new Point(ClientSize.Width - 200, ClientSize.Height - 42);
            cancel.Location = new Point(ClientSize.Width - 104, ClientSize.Height - 42);
            ok.Click += delegate
            {
                Store();
                // i preferiti senza comando si scartano; senza nome prendono la prima riga
                foreach (var f in items) if (f.Name.Trim().Length == 0) f.Name = Favorites.SuggestName(f.Code);
                Favorites.Replace(items.Where(f => f.Code.Trim().Length > 0).ToList());
                DialogResult = DialogResult.OK;
                Close();
            };
            CancelButton = cancel;

            Controls.AddRange(new Control[] { lst, bNew, bDel, bUp, bDown, lName, tName, lLang, cLang, lDesc, bDescribe, tDesc, lCode, tCode, hint, ok, cancel });
            FormClosed += delegate { if (describing != null) describing.Cancel(); };

            lst.SelectedIndexChanged += delegate { if (!loading) Show_(); };
            tName.TextChanged += delegate { if (loading) return; Store(); RefreshName(); };
            cLang.SelectedIndexChanged += delegate { if (!loading) Store(); };
            tCode.TextChanged += delegate { if (!loading) Store(); };
            tDesc.TextChanged += delegate { if (!loading) Store(); };
            bNew.Click += delegate { Add(new Favorite()); tName.Focus(); };
            bDel.Click += delegate
            {
                int i = lst.SelectedIndex;
                if (i < 0) return;
                items.RemoveAt(i);
                Fill(Math.Min(i, items.Count - 1));
            };
            bUp.Click += delegate { MoveItem(-1); };
            bDown.Click += delegate { MoveItem(1); };

            if (add != null) Add(add);
            else Fill(items.Count > 0 ? 0 : -1);
            Shown += delegate { if (add != null) { tName.Focus(); tName.SelectAll(); } };
        }

        void Add(Favorite f)
        {
            if (f.Name.Length == 0) f.Name = Favorites.SuggestName(f.Code);
            items.Add(f);
            Fill(items.Count - 1);
        }

        void MoveItem(int d)
        {
            int i = lst.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= items.Count) return;
            var f = items[i]; items.RemoveAt(i); items.Insert(j, f);
            Fill(j);
        }

        static string Label_(Favorite f) { return f.Name.Trim().Length > 0 ? f.Name : L.T("(senza nome)", "(no name)"); }

        void Fill(int select)
        {
            loading = true;
            lst.Items.Clear();
            foreach (var f in items) lst.Items.Add(Label_(f));
            lst.SelectedIndex = select;
            loading = false;
            Show_();
        }

        void RefreshName()
        {
            int i = lst.SelectedIndex;
            if (i < 0) return;
            loading = true;
            lst.Items[i] = Label_(items[i]);
            loading = false;
        }

        void Show_()
        {
            int i = lst.SelectedIndex;
            bool has = i >= 0;
            loading = true;
            tName.Enabled = cLang.Enabled = tCode.Enabled = tDesc.Enabled = bDel.Enabled = has;
            bDescribe.Enabled = has && describing == null;
            tDesc.Text = has ? (items[i].Description ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            bUp.Enabled = has && i > 0;
            bDown.Enabled = has && i < items.Count - 1;
            tName.Text = has ? items[i].Name : "";
            cLang.SelectedIndex = has ? Math.Max(0, Array.IndexOf(Favorites.Langs, items[i].Lang ?? "")) : -1;
            tCode.Text = has ? (items[i].Code ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
            loading = false;
        }

        void Store()
        {
            int i = lst.SelectedIndex;
            if (i < 0 || i >= items.Count) return;
            items[i].Name = tName.Text.Trim();
            items[i].Lang = Favorites.Langs[Math.Max(0, cLang.SelectedIndex)];
            items[i].Code = tCode.Text.Replace("\r\n", "\n").TrimEnd('\n');
            items[i].Description = tDesc.Text.Replace("\r\n", "\n").Trim();
        }

        // Fa scrivere all'assistente una descrizione breve del comando selezionato
        void Describe()
        {
            int i = lst.SelectedIndex;
            if (i < 0) return;
            Store();
            var fav = items[i];
            if (fav.Code.Trim().Length == 0) { tCode.Focus(); return; }
            string missing;
            var provider = AiSettings.Create(out missing);
            if (provider == null) { MessageBox.Show(this, missing, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string lang = L.Code == "it" ? "Italian" : L.Code == "en" ? "English" : "the language with code " + L.Code;
            var req = new AiRequest
            {
                System = "You write short descriptions of saved terminal commands for a list of favorites. Reply with the description only: " +
                         "one or two plain sentences in " + lang + " saying what the command does and anything important to know before running it " +
                         "(for example that it deletes files or needs administrator rights). No markdown, no code blocks, no quotes around the text."
            };
            req.Messages.Add(new AiMessage("user", "Shell: " + Favorites.LangName(fav.Lang) + "\nCommand:\n```\n" + fav.Code + "\n```"));
            describing = new System.Threading.CancellationTokenSource();
            var token = describing.Token;
            bDescribe.Enabled = false;
            bDescribe.Text = L.T("Sto scrivendo…", "Writing…");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                AiResult res;
                try { res = provider.Run(req, token); }
                catch (Exception ex) { res = new AiResult { Error = ex.Message }; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        describing = null;
                        bDescribe.Text = L.T("Descrivi con l'assistente", "Describe with the assistant");
                        bDescribe.Enabled = lst.SelectedIndex >= 0;
                        if (res.Cancelled) return;
                        if (!res.Ok) { MessageBox.Show(this, res.Error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                        fav.Description = res.Text.Trim().Trim('"');
                        if (lst.SelectedIndex >= 0 && items[lst.SelectedIndex] == fav) Show_();
                    });
                }
                catch { }
            });
        }
    }
}