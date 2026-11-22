// Assistente locale (ripreso da CorbolSys/CorbolPad): Ministral 3 o Qwen3.5 con llama-server (llama.cpp) in un
// processo separato, sul PC. Niente chiave API e i dati non escono dal computer.
//   - Modello, proiettore di visione e llama-server si scaricano solo quando l'utente lo chiede, in
//     %LOCALAPPDATA%\CorbolPrompt\ai; se un'altra app Corbol ha già scaricato llama-server si usa quello.
//   - Ogni file si controlla con il suo SHA-256; un download interrotto riprende da dove era arrivato.
//   - llama-server parte alla prima richiesta e si chiude dopo 5 minuti senza richieste (libera 6-8 GB di RAM).
//   - Ascolta solo su 127.0.0.1, su una porta libera scelta ogni volta, con una chiave casuale.
//   - Un "job object" di Windows lo chiude anche se CorbolPrompt si chiude male.
//   - La risposta è vincolata a JSON valido (grammatica di llama-server): i modelli piccoli sbagliano meno.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CorbolPrompt
{
    class AiModel
    {
        public string Id, Name, Maker, FileName, Url, Sha256, License;
        public long Size, MinRam;
        // proiettore di visione (mmproj): permette al modello di vedere le viste e le foto
        public string VisionFile, VisionUrl, VisionSha256;
        public long VisionSize;
        public bool Qwen;    // Qwen3.5 "pensa" prima di rispondere: qui si spegne
        public long TotalSize { get { return Size + VisionSize; } }
    }

    // Modelli proposti per l'assistente locale. Solo licenze Apache 2.0 o MIT, scaricabili senza account.
    // Quantizzazione Q4_K_M di unsloth; l'URL punta a un commit preciso del repository, così il file non può
    // cambiare sotto i piedi e lo SHA-256 resta valido. Entrambi vedono le immagini con il loro mmproj.
    static class AiCatalog
    {
        public const string Ministral = "ministral-3-8b";
        public const string Qwen = "qwen3.5-9b";

        public static readonly AiModel[] Models =
        {
            new AiModel
            {
                Id = Ministral, Name = "Ministral 3 8B", Maker = "Mistral AI", License = "Apache 2.0",
                FileName = "Ministral-3-8B-Instruct-2512-Q4_K_M.gguf",
                Url = "https://huggingface.co/unsloth/Ministral-3-8B-Instruct-2512-GGUF/resolve/3731507ec3e867db16d620f73e14d689125758f4/Ministral-3-8B-Instruct-2512-Q4_K_M.gguf",
                Size = 5198386720,
                Sha256 = "5dbc3647eb563b9f8d3c70ec3d906cce84b86bb35c5e0b8a36e7df3937ab7174",
                VisionFile = "Ministral-3-8B-Instruct-2512-mmproj-F16.gguf",
                VisionUrl = "https://huggingface.co/unsloth/Ministral-3-8B-Instruct-2512-GGUF/resolve/3731507ec3e867db16d620f73e14d689125758f4/mmproj-F16.gguf",
                VisionSize = 857077920,
                VisionSha256 = "35f84351450a8935b4bb8f683c123f9a4949699baaf9e206db7816cd7b78f74d",
                MinRam = 16L << 30
            },
            new AiModel
            {
                Id = Qwen, Name = "Qwen3.5 9B", Maker = "Alibaba", License = "Apache 2.0", Qwen = true,
                FileName = "Qwen3.5-9B-Q4_K_M.gguf",
                Url = "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b6810b007914f3a7950a8d1b469d598a5/Qwen3.5-9B-Q4_K_M.gguf",
                Size = 5680522464,
                Sha256 = "03b74727a860a56338e042c4420bb3f04b2fec5734175f4cb9fa853daf52b7e8",
                VisionFile = "Qwen3.5-9B-mmproj-F16.gguf",
                VisionUrl = "https://huggingface.co/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b6810b007914f3a7950a8d1b469d598a5/mmproj-F16.gguf",
                VisionSize = 918166080,
                VisionSha256 = "f70dc3509053962b0d0d3ee8a7eacebf5d60aa560cad78254ae8698516ae029f",
                MinRam = 16L << 30
            }
        };

        public static AiModel Find(string id)
        {
            return Models.FirstOrDefault(m => m.Id == id);
        }

        // Il modello consigliato: Ministral 3 8B (vede le immagini, risposte JSON ordinate)
        public static AiModel Recommended() { return Models[0]; }

        // Motivo per cui questo PC non è adatto al modello, o null. La RAM "da 16 GB" vista da Windows è un po' meno.
        public static string Unsuitable(AiModel m)
        {
            long ram = TotalRam();
            if (ram > 0 && ram < m.MinRam * 15 / 16)
                return L.F("RAM insufficiente: servono almeno {0} GB", "Not enough RAM: at least {0} GB needed", m.MinRam >> 30);
            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORYSTATUSEX
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

        public static long TotalRam()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
            return GlobalMemoryStatusEx(ref m) ? (long)m.ullTotalPhys : 0;
        }
    }

    // Stato da mostrare nelle impostazioni
    class AiDownloadStatus
    {
        public string Text;          // cosa sta facendo, già tradotto
        public long Done, Total;     // byte, per la barra di avanzamento (Total = 0: indeterminato)
        public bool Finished;        // il lavoro è terminato (bene se Error == null)
        public string Error;         // motivo dell'errore, già tradotto
    }

    class AiDownloader
    {
        // Versione di llama.cpp provata con CorbolPad e CorbolSys, fissata con dimensione e SHA-256 dei pacchetti (presi dalla
        // pagina della release su GitHub). Un aggiornamento di llama.cpp non cambia nulla finché non si cambia qui.
        // Per aggiornare: https://github.com/ggml-org/llama.cpp/releases/tag/<tag> → asset "llama-<tag>-bin-win-<cpu|vulkan>-x64.zip".
        public const string LlamaTag = "b12110";
        class LlamaPackage { public string Variant; public long Size; public string Sha256; }
        static readonly LlamaPackage[] LlamaPackages =
        {
            new LlamaPackage { Variant = "cpu",    Size = 24708445, Sha256 = "98901e327c252875accaf2d1bf364e10e868843b0b63468baad1aa945cdc5da2" },
            new LlamaPackage { Variant = "vulkan", Size = 37661146, Sha256 = "9732a5e48887ca54d0bfe4af4324dc85fd56cd8cda19b52df3542fa6ebe6874f" }
        };

        const long MinFree = 2L << 30;     // spazio da lasciare libero sul disco dopo i download
        const int Retries = 5;             // tentativi per file, ognuno riprende da dove era arrivato

        // Cartella di CorbolPrompt (qui si scarica e si cancella) e cartelle delle altre app Corbol (solo lettura:
        // se hanno già scaricato llama-server, CorbolPrompt lo usa senza scaricarlo di nuovo e non lo cancella mai)
        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CorbolPrompt", "ai"); }
        }
        static string AiDir(string app)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), app, "ai");
        }
        static readonly string[] Others = { "CorbolPad", "CorbolIp", "CorbolSys", "CorbolSup", "Corbol3D", "CorbolImg", "CorbolMusic", "AppPad", "AppIP" };
        static string[] Roots { get { return new[] { Root }.Concat(Others.Select(AiDir)).ToArray(); } }
        public static string ModelsDir { get { return Path.Combine(Root, "models"); } }
        public static string LlamaDir { get { return Path.Combine(Root, "llama"); } }

        static string UserAgent { get { return "CorbolPrompt/" + AppVersion.Text; } }

        // Chiamato dal thread del download: chi aggiorna l'interfaccia deve usare BeginInvoke
        public event Action<AiDownloadStatus> Progress;

        Thread thread;
        volatile bool cancel;
        public bool Busy { get { return thread != null && thread.IsAlive; } }

        // ---- Cosa c'è già sul disco ----

        static string Verified(string file, long size, string sha)
        {
            foreach (var root in Roots)
            {
                string f = Path.Combine(root, "models", file);
                try
                {
                    if (new FileInfo(f).Length == size && File.ReadAllText(f + ".sha256").Trim() == sha) return f;
                }
                catch { }
            }
            return null;
        }

        // Percorso del modello se scaricato e verificato, altrimenti null
        public static string ModelPath(AiModel m) { return Verified(m.FileName, m.Size, m.Sha256); }

        // Percorso del proiettore di visione se scaricato e verificato, altrimenti null
        public static string VisionPath(AiModel m) { return m.VisionFile == null ? null : Verified(m.VisionFile, m.VisionSize, m.VisionSha256); }

        public static bool ModelReady(AiModel m) { return ModelPath(m) != null && (m.VisionFile == null || VisionPath(m) != null); }

        // Byte ancora da scaricare per il modello (file principale e visione)
        public static long ModelMissingBytes(AiModel m)
        {
            return (ModelPath(m) == null ? m.Size : 0) + (m.VisionFile != null && VisionPath(m) == null ? m.VisionSize : 0);
        }

        // Percorso di llama-server.exe adatto alla variante ("cpu" o "vulkan"), di CorbolPrompt o di un'altra app Corbol, o null.
        // Il pacchetto per GPU (Vulkan) contiene anche il motore per processore, quindi va bene pure per "cpu".
        public static string ServerExe(string variant)
        {
            foreach (var d in InstalledLlamaDirs())
            {
                string n = Path.GetFileName(d);
                int i = n.IndexOf('-');
                string v = i > 0 ? n.Substring(i + 1) : "";
                if (v != variant && !(variant == "cpu" && v == "vulkan")) continue;
                string exe = FindServer(d);
                if (exe != null) return exe;
            }
            return null;
        }

        public static bool LlamaReady(string variant) { return ServerExe(variant) != null; }

        // Il llama-server viene dalla cartella di un'altra app: nome dell'app, o null
        public static string LlamaFromOtherApp(string variant)
        {
            string exe = ServerExe(variant);
            if (exe == null) return null;
            foreach (var o in Others)
                if (exe.StartsWith(AiDir(o) + "\\", StringComparison.OrdinalIgnoreCase)) return o;
            return null;
        }

        // Cartelle llama\<tag>-<variante> complete di CorbolPrompt e delle altre app, la versione più recente per prima
        static IEnumerable<string> InstalledLlamaDirs()
        {
            var all = new List<string>();
            foreach (var root in Roots)
            {
                string dir = Path.Combine(root, "llama");
                try { if (Directory.Exists(dir)) all.AddRange(Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "ok.txt")))); }
                catch { }
            }
            return all.OrderByDescending(d => TagNumber(Path.GetFileName(d)));
        }

        static string FindServer(string dir)
        {
            try { return Directory.GetFiles(dir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault(); }
            catch { return null; }
        }

        static int TagNumber(string name)
        {
            var m = Regex.Match(name, "^b(\\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        // Dimensione del pacchetto di llama.cpp da scaricare per la variante ("cpu" o "vulkan")
        public static long LlamaSize(string variant)
        {
            var p = LlamaPackages.FirstOrDefault(x => x.Variant == variant);
            return p != null ? p.Size : 0;
        }

        // Spazio occupato dai file scaricati da CorbolPrompt (quelli delle altre app non contano: sono loro)
        public static long UsedBytes()
        {
            try
            {
                if (!Directory.Exists(Root)) return 0;
                return new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { return 0; }
        }

        // ---- Pulizia ----

        // Cancella i file scaricati da CorbolPrompt (GB); quelli delle altre app restano. llama-server deve essere già chiuso.
        public static void RemoveAll()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
        }

        // Cancella i modelli del catalogo diversi da quello scelto (e i download a metà di quelli)
        public static void RemoveOtherModels(AiModel keep)
        {
            if (!Directory.Exists(ModelsDir)) return;
            foreach (var f in Directory.GetFiles(ModelsDir))
            {
                string n = Path.GetFileName(f);
                if (keep != null && (n.StartsWith(keep.FileName, StringComparison.OrdinalIgnoreCase) ||
                                     (keep.VisionFile != null && n.StartsWith(keep.VisionFile, StringComparison.OrdinalIgnoreCase)))) continue;
                try { File.Delete(f); } catch { }
            }
        }

        public static string FormatBytes(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0") + " MB";
            return Math.Max(1, b >> 10) + " KB";
        }

        // ---- Download ----

        // Scarica in background quel che manca: llama-server nella variante indicata ("cpu" o "vulkan") e,
        // se model != null, il modello con la sua visione. Se è già tutto a posto termina subito.
        public void Start(AiModel model, string variant)
        {
            if (Busy) return;
            cancel = false;
            thread = new Thread(delegate () { Run(model, variant); });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Name = "CorbolPrompt AI download";
            thread.Start();
        }

        // Interrompe: il file a metà resta, il prossimo Start riprende da lì
        public void Cancel() { cancel = true; }

        void Report(string text, long done, long total)
        {
            var h = Progress;
            if (h != null) h(new AiDownloadStatus { Text = text, Done = done, Total = total });
        }

        void Run(AiModel model, string variant)
        {
            string error = null;
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                if (variant != "vulkan") variant = "cpu";
                if (model != null)
                {
                    string why = AiCatalog.Unsuitable(model);
                    if (why != null) throw new AiDownloadException(why);
                    RemoveOtherModels(model);
                }
                // Prima il modello (HuggingFace), poi llama.cpp (GitHub): se uno dei due siti non risponde,
                // intanto si porta a casa l'altro, che è il file più grande
                if (model != null)
                {
                    if (ModelPath(model) == null) DownloadFile(model.FileName, model.Url, model.Size, model.Sha256, model.Name);
                    if (model.VisionFile != null && VisionPath(model) == null)
                        DownloadFile(model.VisionFile, model.VisionUrl, model.VisionSize, model.VisionSha256, model.Name + " (" + L.T("visione", "vision") + ")");
                }
                if (!LlamaReady(variant)) InstallLlama(variant);
            }
            catch (OperationCanceledException) { error = L.T("Scaricamento interrotto", "Download stopped"); }
            catch (AiDownloadException ex) { error = ex.Message; }
            catch (Exception ex) { error = ex.Message; }

            var h = Progress;
            if (h != null) h(new AiDownloadStatus
            {
                Finished = true, Error = error,
                Text = error ?? L.T("Assistente locale pronto", "Local assistant ready")
            });
        }

        void DownloadFile(string fileName, string url, long size, string sha, string what)
        {
            Directory.CreateDirectory(ModelsDir);
            string final = Path.Combine(ModelsDir, fileName);
            string part = final + ".part";
            try { File.Delete(final + ".sha256"); } catch { }
            if (File.Exists(final) && !File.Exists(part)) File.Move(final, part); // file presente ma non verificato: si ricontrolla
            Download(url, part, size, L.F("Scaricamento di {0}", "Downloading {0}", what));
            Verify(part, sha, what);
            if (File.Exists(final)) File.Delete(final);
            File.Move(part, final);
            File.WriteAllText(final + ".sha256", sha);
        }

        // llama-server per Windows x64 dalla release ufficiale di llama.cpp indicata in LlamaTag
        void InstallLlama(string variant)
        {
            var pkg = LlamaPackages.First(p => p.Variant == variant);
            string tag = LlamaTag;
            string assetName = "llama-" + tag + "-bin-win-" + variant + "-x64.zip";
            string url = "https://github.com/ggml-org/llama.cpp/releases/download/" + tag + "/" + assetName;

            Directory.CreateDirectory(LlamaDir);
            string zip = Path.Combine(LlamaDir, assetName + ".part");
            foreach (var f in Directory.GetFiles(LlamaDir, "*.part"))
                if (!f.Equals(zip, StringComparison.OrdinalIgnoreCase)) try { File.Delete(f); } catch { }

            Download(url, zip, pkg.Size, L.F("Scaricamento di llama.cpp {0}", "Downloading llama.cpp {0}", tag));
            Verify(zip, pkg.Sha256, "llama.cpp");

            Report(L.T("Estrazione di llama.cpp…", "Extracting llama.cpp…"), 0, 0);
            string dir = Path.Combine(LlamaDir, tag + "-" + variant);
            string tmp = dir + ".tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp);
            if (FindServer(tmp) == null) throw new AiDownloadException(L.T("llama-server.exe non trovato nel pacchetto di llama.cpp", "llama-server.exe not found in the llama.cpp package"));
            File.WriteAllText(Path.Combine(tmp, "ok.txt"), assetName + "\r\nsha256:" + pkg.Sha256 + "\r\n");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.Move(tmp, dir);
            File.Delete(zip);

            foreach (var d in Directory.GetDirectories(LlamaDir))
                if (!d.Equals(dir, StringComparison.OrdinalIgnoreCase)) try { Directory.Delete(d, true); } catch { }
        }

        // Scarica url in part, riprendendo da quanto c'è già. Controlla prima lo spazio libero.
        void Download(string url, string part, long size, string label)
        {
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have > size) { File.Delete(part); have = 0; }
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(part))).AvailableFreeSpace;
            if (free < size - have + MinFree)
                throw new AiDownloadException(L.F("Spazio insufficiente: servono {0} liberi sul disco",
                                                  "Not enough space: {0} free needed on the disk", FormatBytes(size - have + MinFree)));

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    DownloadOnce(url, part, size, label);
                    return;
                }
                catch (Exception ex)
                {
                    if (cancel || ex is AiDownloadException || attempt >= Retries) throw;
                    if (!(ex is WebException || ex is IOException)) throw;
                    Report(label + " — " + L.T("nuovo tentativo…", "retrying…"), File.Exists(part) ? new FileInfo(part).Length : 0, size);
                    for (int i = 0; i < 50 && !cancel; i++) Thread.Sleep(100);
                    if (cancel) throw new OperationCanceledException();
                }
            }
        }

        void DownloadOnce(string url, string part, long size, string label)
        {
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (have == size) return;

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = UserAgent;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true; // HuggingFace e GitHub rimandano a una CDN
            if (have > 0) req.AddRange(have);

            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                bool append = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;   // 200 invece di 206: si ricomincia
                if (!append) have = 0;
                using (var src = resp.GetResponseStream())
                using (var dst = new FileStream(part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16))
                {
                    var buf = new byte[1 << 20];
                    int lastTick = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancel) throw new OperationCanceledException();
                        if (have + n > size) throw new AiDownloadException(L.T("Il file scaricato è più grande del previsto", "The downloaded file is larger than expected"));
                        dst.Write(buf, 0, n);
                        have += n;
                        if (Environment.TickCount - lastTick > 250)
                        {
                            lastTick = Environment.TickCount;
                            Report(label, have, size);
                        }
                    }
                }
            }
            if (have != size) throw new IOException(L.T("Scaricamento incompleto", "Incomplete download"));
            Report(label, have, size);
        }

        // Confronta lo SHA-256 del file; se non torna lo cancella, così il prossimo tentativo riparte da zero
        void Verify(string file, string sha, string what)
        {
            long total = new FileInfo(file).Length, done = 0;
            string label = L.F("Verifica di {0}…", "Verifying {0}…", what);
            Report(label, 0, total);
            SHA256 h;
            try { h = new SHA256Cng(); } catch { h = SHA256.Create(); }
            using (h)
            using (var st = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var buf = new byte[1 << 20];
                int n, lastTick = 0;
                while ((n = st.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancel) throw new OperationCanceledException();
                    h.TransformBlock(buf, 0, n, null, 0);
                    done += n;
                    if (Environment.TickCount - lastTick > 250) { lastTick = Environment.TickCount; Report(label, done, total); }
                }
                h.TransformFinalBlock(buf, 0, 0);
                string got = BitConverter.ToString(h.Hash).Replace("-", "").ToLowerInvariant();
                if (got != sha)
                {
                    st.Close();
                    try { File.Delete(file); } catch { }
                    throw new AiDownloadException(L.F("{0}: il file scaricato non corrisponde a quello pubblicato (SHA-256 diverso)",
                                                      "{0}: the downloaded file does not match the published one (SHA-256 mismatch)", what));
                }
            }
        }
    }

    // Errore con un messaggio già pronto per l'utente: niente nuovi tentativi
    class AiDownloadException : Exception
    {
        public AiDownloadException(string msg) : base(msg) { }
    }

    class LocalAi : IAiProvider
    {
        public static readonly LocalAi Instance = new LocalAi();

        // Istruzioni (~4000 token) + modello attuale + viste (~1500 token) + risposta (fino a 8000): serve spazio
        const int ContextTokens = 20480;
        const int MaxTokens = 8192;
        const int StartTimeoutMs = 240000;          // caricamento di 6 GB dal disco
        public const int MaxCrashes = 2;
        public static int IdleMinutes = 5;

        readonly object gate = new object();
        Process proc;
        IntPtr job;
        int port;
        string key, modelPath, visionPath, variant;
        volatile bool busy;
        DateTime lastUse;
        System.Threading.Timer idleTimer;
        readonly LinkedList<string> log = new LinkedList<string>();   // ultime righe di llama-server, per gli errori

        public string Name { get { return L.T("Assistente locale", "Local assistant"); } }

        // Avvisi per l'interfaccia ("Avvio dell'assistente locale…", null = niente da dire); arrivano da altri thread
        public event Action<string> StatusChanged;

        public bool Running { get { lock (gate) return proc != null && !proc.HasExited; } }

        public static string LogPath { get { return Path.Combine(AiDownloader.Root, "llama-server.log"); } }

        // Chiusure improvvise durante una risposta, contate in settings.txt
        public static int Crashes
        {
            get { return Settings.Get("ai.local.crashes", 0); }
            set { Settings.Set("ai.local.crashes", value); Settings.Save(); }
        }

        public static AiModel ConfiguredModel() { return AiCatalog.Find(AiSettings.LocalModel) ?? AiCatalog.Recommended(); }

        // Motivo per cui l'assistente locale non può partire, o null
        public static string NotReadyReason()
        {
            if (Crashes >= MaxCrashes)
                return L.F("Assistente locale spento: si è chiuso all'improvviso {0} volte. Riattivalo dalle impostazioni, magari senza GPU.",
                           "Local assistant turned off: it closed unexpectedly {0} times. Turn it back on in the settings, perhaps without the GPU.", MaxCrashes);
            if (!AiDownloader.ModelReady(ConfiguredModel()) || !AiDownloader.LlamaReady(AiSettings.LocalVariant == "auto" ? "cpu" : AiSettings.LocalVariant))
                return L.T("L'assistente locale non è ancora scaricato: completa il download dalle impostazioni (menu in alto a destra → Assistente IA).",
                           "The local assistant is not downloaded yet: finish the download in the settings (menu at the top right → AI assistant).");
            return null;
        }

        // "auto": scheda video (Vulkan) se il pacchetto c'è, processore se la GPU ha già fatto cadere llama-server in questa sessione
        volatile bool gpuFailed;

        static string ResolveVariant(string setting, bool gpuFailed)
        {
            if (setting != "auto") return setting;
            return !gpuFailed && AiDownloader.LlamaReady("vulkan") ? "vulkan" : "cpu";
        }

        // ---- Richiesta ----

        public AiResult Run(AiRequest req, CancellationToken cancel)
        {
            string why = NotReadyReason();
            if (why != null) return new AiResult { Error = why };

            AiResult res;
            for (int attempt = 0; ; attempt++)
            {
                OpenAiProvider p;
                try { p = EnsureRunning(cancel, ResolveVariant(AiSettings.LocalVariant, gpuFailed)); }
                catch (OperationCanceledException) { return new AiResult { Cancelled = true }; }
                catch (Exception ex) { return new AiResult { Error = ex.Message }; }

                busy = true;
                lastUse = DateTime.Now;
                try { res = p.Run(req, cancel); }
                finally { busy = false; lastUse = DateTime.Now; }
                // Chiuso all'improvviso (a volte per il driver della scheda video): si riavvia e si rifà la richiesta
                // una volta, sul processore se stava usando la GPU in modalità automatica
                if (attempt == 0 && !res.Cancelled && res.Error != null && !Running)
                {
                    if (variant == "vulkan" && AiSettings.LocalVariant == "auto") gpuFailed = true;
                    continue;
                }
                break;
            }

            if (!res.Cancelled && res.Error != null && !Running)
            {
                int n = Crashes + 1;
                Crashes = n;
                res.Error = L.T("L'assistente locale si è chiuso all'improvviso.", "The local assistant closed unexpectedly.")
                          + (n >= MaxCrashes ? "\n" + NotReadyReason() : "") + LastLog();
            }
            else if (res.Ok && Crashes > 0) Crashes = 0;   // le chiusure contano solo se consecutive
            return res;
        }

        OpenAiProvider Client()
        {
            var m = ConfiguredModel();
            var extra = new Dictionary<string, object>
            {
                // temperatura bassa: comandi precisi, non fantasia
                { "temperature", 0.2 }, { "top_p", 0.9 }, { "min_p", 0.0 }
            };
            // Qwen3.5 "pensa" prima di rispondere: qui rallenterebbe di minuti
            if (m.Qwen) extra["chat_template_kwargs"] = new Dictionary<string, object> { { "enable_thinking", false } };
            return new OpenAiProvider
            {
                BaseUrl = "http://127.0.0.1:" + port + "/v1",
                ApiKey = key,
                Model = "local",
                DisplayName = Name + " (" + m.Name + ")",
                TimeoutMs = 30 * 60 * 1000,   // senza streaming la risposta arriva tutta alla fine: su CPU può volerci molto
                MaxTokens = MaxTokens,
                Extra = extra
            };
        }

        // ---- Avvio e arresto ----

        OpenAiProvider EnsureRunning(CancellationToken cancel, string forceVariant)
        {
            lock (gate)
            {
                var m = ConfiguredModel();
                string v = forceVariant ?? AiSettings.LocalVariant, exe = AiDownloader.ServerExe(v);
                string model = AiDownloader.ModelPath(m), vision = (string)null;   // CorbolPrompt non manda immagini
                if (proc != null && !proc.HasExited && model == modelPath && vision == visionPath && v == variant) return Client();
                StopLocked();

                Status(L.T("Avvio dell'assistente locale…", "Starting the local assistant…"));
                port = FreePort();
                key = RandomKey();
                modelPath = model;
                visionPath = vision;
                variant = v;
                lock (log) log.Clear();

                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = Args(model, vision, v, port, key),
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (s, e) => AddLog(e.Data);
                p.ErrorDataReceived += (s, e) => AddLog(e.Data);
                try { p.Start(); }
                catch (Exception ex) { throw new Exception(L.T("Impossibile avviare llama-server:", "Could not start llama-server:") + " " + ex.Message); }
                proc = p;
                AttachToJob(p);
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }   // il PC resta reattivo
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                // Pronto quando /health risponde 200 (503 mentre carica il modello)
                var sw = Stopwatch.StartNew();
                while (true)
                {
                    if (cancel.IsCancellationRequested) { StopLocked(); throw new OperationCanceledException(); }
                    if (p.HasExited)
                    {
                        proc = null;
                        throw new Exception(L.F("llama-server si è chiuso durante l'avvio (codice {0}).", "llama-server closed while starting (code {0}).", p.ExitCode) + LastLog());
                    }
                    if (Healthy()) break;
                    if (sw.ElapsedMilliseconds > StartTimeoutMs)
                    {
                        StopLocked();
                        throw new Exception(L.T("L'assistente locale non è partito in tempo.", "The local assistant did not start in time.") + LastLog());
                    }
                    cancel.WaitHandle.WaitOne(250);
                }
                lastUse = DateTime.Now;
                if (idleTimer == null) idleTimer = new System.Threading.Timer(delegate { StopIfIdle(); }, null, 30000, 30000);
                Status(L.T("L'assistente locale sta scrivendo…", "The local assistant is writing…"));
                return Client();
            }
        }

        static string Args(string model, string vision, string variant, int port, string key)
        {
            var a = new StringBuilder();
            a.Append("-m \"").Append(model).Append("\"");
            if (vision != null) a.Append(" --mmproj \"").Append(vision).Append("\"");   // vede le viste e le foto
            a.Append(" --host 127.0.0.1 --port ").Append(port).Append(" --api-key ").Append(key);
            a.Append(" -c ").Append(ContextTokens);
            a.Append(" --jinja");                                        // modello di chat del file .gguf
            a.Append(" --reasoning off");                                // niente "ragionamento": qui rallenta e basta
            a.Append(" -np 1");                                          // una richiesta alla volta: tutto il contesto per lei
            a.Append(variant == "vulkan" ? " -ngl 999" : " -ngl 0");     // strati del modello sulla GPU
            int threads = Math.Max(1, Environment.ProcessorCount / 2);   // circa i core fisici; il resto agli altri programmi
            a.Append(" -t ").Append(threads);
            return a.ToString();
        }

        bool Healthy()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/health");
                req.Timeout = 2000;
                req.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
                req.Proxy = null;
                using (var resp = (HttpWebResponse)req.GetResponse()) return resp.StatusCode == HttpStatusCode.OK;
            }
            catch { return false; }
        }

        // Chiude llama-server (per esempio prima di cancellare i file o cambiare modello)
        public void Stop()
        {
            lock (gate) StopLocked();
        }

        void StopLocked()
        {
            var p = proc;
            proc = null;
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(5000); } } catch { }
            try { p.Dispose(); } catch { }
            Status(null);
        }

        void StopIfIdle()
        {
            if (busy || !Monitor.TryEnter(gate)) return;
            try
            {
                if (proc != null && !busy && DateTime.Now - lastUse > TimeSpan.FromMinutes(IdleMinutes)) StopLocked();
            }
            finally { Monitor.Exit(gate); }
        }

        void Status(string s)
        {
            var h = StatusChanged;
            if (h != null) try { h(s); } catch { }
        }

        // ---- Registro di llama-server ----

        void AddLog(string line)
        {
            if (line == null) return;
            lock (log)
            {
                log.AddLast(line);
                while (log.Count > 200) log.RemoveFirst();
            }
        }

        // Ultime righe utili del registro, da aggiungere a un messaggio di errore; le salva anche su file
        string LastLog()
        {
            string[] lines;
            lock (log) { lines = new string[log.Count]; log.CopyTo(lines, 0); }
            try { Directory.CreateDirectory(AiDownloader.Root); File.WriteAllLines(LogPath, lines, new UTF8Encoding(false)); } catch { }
            var tail = new List<string>();
            for (int i = lines.Length - 1; i >= 0 && tail.Count < 4; i--)
            {
                string l = lines[i].Trim();
                if (l.Length == 0) continue;
                if (l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || tail.Count == 0)
                    tail.Insert(0, l.Length > 200 ? l.Substring(0, 200) + "…" : l);
            }
            return tail.Count == 0 ? "" : "\n\n" + string.Join("\n", tail);
        }

        // ---- Utilità ----

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        static string RandomKey()
        {
            var b = new byte[24];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }

        // Job object con "chiudi i processi quando si chiude il job": l'handle lo tiene CorbolPrompt, quindi
        // se CorbolPrompt termina (anche in crash) Windows chiude llama-server
        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }
        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
        const int JobObjectExtendedLimitInformation = 9;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        void AttachToJob(Process p)
        {
            try
            {
                if (job == IntPtr.Zero)
                {
                    job = CreateJobObject(IntPtr.Zero, null);
                    var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                    info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                    SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
                }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch { }
        }
    }
}
