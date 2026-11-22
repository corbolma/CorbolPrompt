// Sessioni dei terminali: una shell vera (PowerShell, cmd, WSL, Git Bash...) attaccata a una console
// virtuale di Windows (ConPTY, Windows 10 1809 e successivi), oppure l'interprete Lisp integrato.
// ConPTY traduce la console di Windows in sequenze VT (come un terminale Linux): l'app le legge dalla
// pipe di uscita e le passa all'emulatore (Terminal.cs); i tasti vanno nella pipe di ingresso come sequenze VT.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CorbolPrompt
{
    interface ISession
    {
        // Testo da mostrare (con sequenze VT), da un thread qualunque
        event Action<string> Output;
        // Il programma è terminato (codice di uscita), da un thread qualunque
        event Action<int> Exited;
        void Start(int cols, int rows);
        void Send(string keys);          // tasti o testo incollato, già tradotti in sequenze VT
        void Resize(int cols, int rows);
        void Close();
    }

    // Una shell che si può aprire in una scheda
    class ShellInfo
    {
        public string Id, Exe, Args, Lang;   // Lang: linguaggio dei blocchi di codice dell'assistente
        string name;
        // il nome del Prompt dei comandi segue la lingua dell'interfaccia
        public string Name { get { return Id == "cmd" ? L.T("Prompt dei comandi", "Command Prompt") : name; } set { name = value; } }
        public bool IsLisp { get { return Id == "lisp"; } }
        public string Short { get { return IsLisp ? "Lisp" : Name; } }

        static List<ShellInfo> all;

        public static List<ShellInfo> All()
        {
            if (all != null) return all;
            var list = new List<ShellInfo>();
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pwsh = FindInPath("pwsh.exe") ?? Existing(Path.Combine(pf, @"PowerShell\7\pwsh.exe"));
            if (pwsh != null) list.Add(new ShellInfo { Id = "pwsh", Name = "PowerShell", Exe = pwsh, Args = "-NoLogo", Lang = "powershell" });
            string ps = Existing(Path.Combine(sys, @"WindowsPowerShell\v1.0\powershell.exe"));
            if (ps != null) list.Add(new ShellInfo { Id = "powershell", Name = "Windows PowerShell", Exe = ps, Args = "-NoLogo", Lang = "powershell" });
            list.Add(new ShellInfo { Id = "cmd", Name = L.T("Prompt dei comandi", "Command Prompt"), Exe = Path.Combine(sys, "cmd.exe"), Args = "", Lang = "cmd" });
            string wsl = Existing(Path.Combine(sys, "wsl.exe"));
            if (wsl != null) list.Add(new ShellInfo { Id = "wsl", Name = "WSL (Linux)", Exe = wsl, Args = "--cd ~", Lang = "bash" });
            string gitBash = Existing(Path.Combine(pf, @"Git\bin\bash.exe"));
            if (gitBash != null) list.Add(new ShellInfo { Id = "gitbash", Name = "Git Bash", Exe = gitBash, Args = "--login -i", Lang = "bash" });
            list.Add(new ShellInfo { Id = "lisp", Name = "Lisp (CorbolPrompt)", Lang = "lisp" });
            all = list;
            return all;
        }

        public static ShellInfo Find(string id) { return All().FirstOrDefault(s => s.Id == id); }
        public static ShellInfo Default
        {
            get { return Find(Settings.Get("shell.default", "")) ?? All()[0]; }
        }

        static string Existing(string f) { return File.Exists(f) ? f : null; }

        static string FindInPath(string exe)
        {
            foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                try
                {
                    string f = Path.Combine(d.Trim().Trim('"'), exe);
                    if (d.Trim().Length > 0 && File.Exists(f)) return f;
                }
                catch { }
            }
            return null;
        }
    }

    class PtySession : ISession
    {
        public event Action<string> Output;
        public event Action<int> Exited;

        readonly ShellInfo shell;
        readonly string cwd;
        IntPtr hpc = IntPtr.Zero, attrList = IntPtr.Zero, hProcess = IntPtr.Zero;
        FileStream input, output;
        readonly object writeLock = new object();
        int closed;

        public PtySession(ShellInfo shell, string cwd)
        {
            this.shell = shell;
            this.cwd = cwd != null && Directory.Exists(cwd) ? cwd : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public int ProcessId { get; private set; }

        // Per le prove: CORBOLPROMPT_PTYLOG=<file> scrive lì l'output grezzo della console virtuale
        static readonly string DebugLog = Environment.GetEnvironmentVariable("CORBOLPROMPT_PTYLOG");

        public void Start(int cols, int rows)
        {
            SafeFileHandle inRead, inWrite, outRead, outWrite;
            if (!CreatePipe(out inRead, out inWrite, IntPtr.Zero, 0) || !CreatePipe(out outRead, out outWrite, IntPtr.Zero, 0))
                throw new Win32Exception();
            int hr = CreatePseudoConsole(Size(cols, rows), inRead, outWrite, 0, out hpc);
            if (hr != 0) throw new Win32Exception(hr, L.T("Questa versione di Windows non ha la console virtuale (ConPTY): serve Windows 10 1809 o successivo.",
                                                         "This version of Windows has no virtual console (ConPTY): Windows 10 1809 or later is needed."));
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attrList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(attrList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception();

            var si = new STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;   // niente handle ereditati: tutto passa dalla console virtuale
            si.lpAttributeList = attrList;
            IntPtr env = Marshal.StringToHGlobalUni(EnvironmentBlock());
            PROCESS_INFORMATION pi;
            try
            {
                string cmdline = "\"" + shell.Exe + "\"" + (StartArgs().Length > 0 ? " " + StartArgs() : "");
                if (!CreateProcessW(null, new StringBuilder(cmdline), IntPtr.Zero, IntPtr.Zero, false,
                                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, env, cwd, ref si, out pi))
                    throw new Win32Exception();
            }
            finally { Marshal.FreeHGlobal(env); }
            CloseHandle(pi.hThread);
            hProcess = pi.hProcess;
            ProcessId = pi.dwProcessId;
            // le estremità date alla console virtuale non servono più qui
            inRead.Dispose();
            outWrite.Dispose();
            input = new FileStream(inWrite, FileAccess.Write, 1);
            output = new FileStream(outRead, FileAccess.Read, 1);

            var reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty read" };
            reader.Start();
            var waiter = new Thread(WaitLoop) { IsBackground = true, Name = "pty wait" };
            waiter.Start();
        }

        // Argomenti: PowerShell e cmd scrivono la cartella corrente nel prompt (OSC 9;9), così l'app la conosce,
        // e segnano inizio del prompt, inizio del comando e fine del comando con il suo codice (OSC 133 A/B/D).
        // D si manda solo se dall'ultimo prompt è stato eseguito un comando (la cronologia è cresciuta): Invio a vuoto no.
        // Codice: 0 se $? è vero, altrimenti $LASTEXITCODE se è cambiato (programma esterno), altrimenti 1 (errore di un cmdlet).
        string StartArgs()
        {
            if (shell.Id == "pwsh" || shell.Id == "powershell")
            {
                const string script =
                    "$global:__cpPrompt = $function:prompt; $global:__cpH = -1; $global:__cpL = $global:LASTEXITCODE; " +
                    "function global:prompt { $ok = $?; $lc = $global:LASTEXITCODE; $e = [char]27; $b = [char]7; $s = ''; " +
                    "$h = (Get-History -Count 1).Id; if (-not $h) { $h = 0 }; " +
                    "if ($global:__cpH -ge 0 -and $h -ne $global:__cpH) { " +
                    "$c = if ($ok) { 0 } elseif ($lc -ne $global:__cpL -and $lc) { $lc } else { 1 }; $s += \"$e]133;D;$c$b\" }; " +
                    "$global:__cpH = $h; $global:__cpL = $lc; " +
                    "$p = & $global:__cpPrompt; " +
                    "if ($PWD.Provider.Name -eq 'FileSystem') { $s += \"$e]9;9;$($PWD.ProviderPath)$e\\\" }; " +
                    "$s + \"$e]133;A$b\" + $p + \"$e]133;B$b\" }";
                return shell.Args + " -NoExit -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            }
            return shell.Args;
        }

        string EnvironmentBlock()
        {
            var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) vars[(string)e.Key] = (string)e.Value;
            vars["TERM_PROGRAM"] = "CorbolPrompt";
            vars["COLORTERM"] = "truecolor";
            // la shell non deve credersi dentro Claude Code se l'app è stata avviata da lì
            vars.Remove("CLAUDECODE");
            vars.Remove("CLAUDE_CODE_ENTRYPOINT");
            // cmd: cartella e segni di inizio prompt / inizio comando (il codice di uscita nel prompt di cmd non c'è)
            if (shell.Id == "cmd") vars["PROMPT"] = "$E]9;9;$P$E\\$E]133;A$E\\" + (vars.ContainsKey("PROMPT") ? vars["PROMPT"] : "$P$G") + "$E]133;B$E\\";
            var sb = new StringBuilder();
            foreach (var kv in vars) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\0');
            sb.Append('\0');
            return sb.ToString();
        }

        void ReadLoop()
        {
            var dec = new UTF8Encoding(false).GetDecoder();
            var buf = new byte[65536];
            var chars = new char[65536 + 4];
            try
            {
                while (true)
                {
                    int n = output.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    int c = dec.GetChars(buf, 0, n, chars, 0);
                    if (c > 0 && DebugLog != null)
                        try { File.AppendAllText(DebugLog, new string(chars, 0, c).Replace("\x1b", "\\e").Replace("\r", "\\r").Replace("\n", "\\n\n").Replace("\a", "\\a") + "‖", Encoding.UTF8); } catch { }
                    if (c > 0 && Output != null) Output(new string(chars, 0, c));
                }
            }
            catch { }
        }

        void WaitLoop()
        {
            WaitForSingleObject(hProcess, INFINITE);
            uint code;
            if (!GetExitCodeProcess(hProcess, out code)) code = 0;
            Thread.Sleep(100);   // l'ultimo output arriva prima della chiusura
            Close();
            if (Exited != null) Exited((int)code);
        }

        public void Send(string keys)
        {
            if (string.IsNullOrEmpty(keys) || input == null || closed != 0) return;
            lock (writeLock)
            {
                // un thread solo scrive, nell'ordine dei tasti: se la shell non legge l'interfaccia non si blocca
                try { writeQueue.Add(Encoding.UTF8.GetBytes(keys)); } catch (InvalidOperationException) { return; }   // sessione chiusa
                if (writer == null)
                {
                    writer = new Thread(delegate ()
                    {
                        try
                        {
                            foreach (var b in writeQueue.GetConsumingEnumerable()) { input.Write(b, 0, b.Length); input.Flush(); }
                        }
                        catch { }
                    }) { IsBackground = true, Name = "pty write" };
                    writer.Start();
                }
            }
        }

        readonly System.Collections.Concurrent.BlockingCollection<byte[]> writeQueue = new System.Collections.Concurrent.BlockingCollection<byte[]>();
        Thread writer;

        public void Resize(int cols, int rows)
        {
            if (hpc != IntPtr.Zero && closed == 0) ResizePseudoConsole(hpc, Size(cols, rows));
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            var h = hpc;
            hpc = IntPtr.Zero;
            // ClosePseudoConsole chiude anche i programmi della console; può attendere che l'output sia letto
            var t = new Thread(delegate ()
            {
                try { writeQueue.CompleteAdding(); } catch { }
                try { if (h != IntPtr.Zero) ClosePseudoConsole(h); } catch { }
                try { if (input != null) input.Dispose(); } catch { }
                try { if (attrList != IntPtr.Zero) { DeleteProcThreadAttributeList(attrList); Marshal.FreeHGlobal(attrList); attrList = IntPtr.Zero; } } catch { }
                try { if (hProcess != IntPtr.Zero) { CloseHandle(hProcess); hProcess = IntPtr.Zero; } } catch { }
            }) { IsBackground = true };
            t.Start();
        }

        static COORD Size(int cols, int rows) { return new COORD { X = (short)Math.Max(2, cols), Y = (short)Math.Max(1, rows) }; }

        // ---------- Win32 ----------
        const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000, CREATE_UNICODE_ENVIRONMENT = 0x00000400, INFINITE = 0xFFFFFFFF;
        const int STARTF_USESTDHANDLES = 0x00000100, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

        [StructLayout(LayoutKind.Sequential)] struct COORD { public short X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public int cb;
            public IntPtr lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true)] static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint flags, out IntPtr hPC);
        [DllImport("kernel32.dll")] static extern int ResizePseudoConsole(IntPtr hPC, COORD size);
        [DllImport("kernel32.dll")] static extern void ClosePseudoConsole(IntPtr hPC);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CreatePipe(out SafeFileHandle r, out SafeFileHandle w, IntPtr sa, int size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr ret);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    }
}
