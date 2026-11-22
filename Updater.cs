// Controllo aggiornamenti: legge l'ultima release da GitHub e propone solo l'installer pubblicato nel repository,
// verificandone lo SHA-256 scritto nelle note della release ("SHA-256: ..."). Come in CorbolImg.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CorbolPrompt
{
    static class Updater
    {
        static int checking;
        public static string Version, Url, Sha;
        public static event Action Available;

        // Controllo automatico attivo di serie, al massimo una volta al giorno
        public static bool Auto { get { return Settings.Get("autoUpdate", true); } set { Settings.Set("autoUpdate", value); Settings.Save(); } }

        public static void CheckIfDue(Control ui)
        {
            if (!Auto) return;
            DateTime last;
            if (DateTime.TryParse(Settings.Get("lastUpdCheck", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out last) &&
                DateTime.Now - last < TimeSpan.FromHours(24)) return;
            Check(ui, false);
        }

        public static void Check(Control ui, bool manual)
        {
            if (Interlocked.Exchange(ref checking, 1) == 1) return;
            Settings.Set("lastUpdCheck", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            Settings.Save();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string tag = null, url = null, sha = null;
                try
                {
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    var req = (HttpWebRequest)WebRequest.Create(AppVersion.RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest");
                    req.Timeout = 10000;
                    req.UserAgent = "CorbolPrompt/" + AppVersion.Text;
                    req.Accept = "application/vnd.github+json";
                    string json;
                    using (var resp = req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream())) json = sr.ReadToEnd();
                    var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9]+(\\.[0-9]+){1,3})\"");
                    if (m.Success) tag = m.Groups[1].Value;
                    m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(" + Regex.Escape(AppVersion.RepoUrl + "/releases/download/") + "[^\"]+\\.exe)\"");
                    if (m.Success) url = m.Groups[1].Value;
                    m = Regex.Match(json, "SHA-256:\\s*`?([0-9a-fA-F]{64})");
                    if (m.Success) sha = m.Groups[1].Value.ToLowerInvariant();
                }
                catch { }
                checking = 0;
                try { ui.BeginInvoke(new Action(delegate { OnResult(ui, tag, url, sha, manual); })); } catch { }
            });
        }

        static void OnResult(Control ui, string tag, string url, string sha, bool manual)
        {
            System.Version latest;
            if (tag == null || !System.Version.TryParse(tag, out latest))
            {
                if (manual) Msg(ui, L.T("Impossibile controllare gli aggiornamenti. Verifica la connessione a Internet e riprova.",
                                        "Could not check for updates. Check your Internet connection and try again."), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (latest > new System.Version(AppVersion.Text))
            {
                Version = tag; Url = url; Sha = sha;
                if (Available != null) Available();
                if (manual) Offer(ui);
            }
            else if (manual)
                Msg(ui, L.F("Hai già l'ultima versione di CorbolPrompt ({0}).", "You already have the latest version of CorbolPrompt ({0}).", AppVersion.Text),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static void Offer(Control ui)
        {
            if (Version == null) return;
            string page = AppVersion.RepoUrl + "/releases/tag/v" + Version;
            if (Url == null) { OpenUrl(page); return; }
            var r = Msg(ui, L.F("È disponibile CorbolPrompt {0} (installata: {1}).\n\nScaricare e installare ora? L'installer chiuderà CorbolPrompt durante l'aggiornamento: i comandi in corso si interrompono, le schede si riaprono nelle loro cartelle.\n\nNovità: {2}",
                                "CorbolPrompt {0} is available (installed: {1}).\n\nDownload and install it now? The installer will close CorbolPrompt during the update: running commands stop, the tabs reopen in their folders.\n\nWhat's new: {2}",
                                Version, AppVersion.Text, page), MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) Download(ui, Url, Sha, Version, page);
        }

        static void Download(Control ui, string url, string sha, string ver, string page)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = Path.Combine(Path.GetTempPath(), "CorbolPrompt-Setup-" + ver + ".exe");
                string err = null;
                try
                {
                    try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                    using (var wc = new WebClient())
                    {
                        wc.Headers[HttpRequestHeader.UserAgent] = "CorbolPrompt/" + AppVersion.Text;
                        wc.DownloadFile(url, file);
                    }
                    if (sha != null)
                    {
                        // Confronta con lo SHA-256 scritto nelle note della release
                        string got;
                        using (var st = File.OpenRead(file))
                        using (var h = SHA256.Create())
                            got = BitConverter.ToString(h.ComputeHash(st)).Replace("-", "").ToLowerInvariant();
                        if (got != sha)
                        {
                            File.Delete(file);
                            err = L.T("il file scaricato non corrisponde a quello pubblicato (SHA-256 diverso).", "the downloaded file does not match the published one (SHA-256 mismatch).");
                        }
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                try
                {
                    ui.BeginInvoke(new Action(delegate
                    {
                        if (err == null)
                        {
                            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); return; }
                            catch (Exception ex) { err = ex.Message; }
                        }
                        if (Msg(ui, L.F("Aggiornamento non riuscito: {0}\n\nAprire la pagina di download?", "Update failed: {0}\n\nOpen the download page?", err),
                                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                            OpenUrl(page);
                    }));
                }
                catch { }
            });
        }

        public static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        static DialogResult Msg(Control owner, string text, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return MessageBox.Show(owner, text, "CorbolPrompt", buttons, icon);
        }
    }
}
