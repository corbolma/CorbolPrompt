// Istruzioni per l'assistente di CorbolPrompt e lettura delle risposte: il testo per l'utente e i blocchi
// di codice (```powershell, ```bash, ```lisp...) che l'app mostra come comandi da eseguire con un clic.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CorbolPrompt
{
    static class Prompt
    {
        public const string System = @"You are the assistant of CorbolPrompt, a terminal app for Windows with tabs. Each tab runs one shell: PowerShell 7 (pwsh), Windows PowerShell 5.1, Command Prompt (cmd), WSL (Linux bash), Git Bash, or the built-in CorbolPrompt Lisp.
The user asks how to do things in the terminal, asks you to explain output or errors, or asks you to write commands, scripts or Lisp code.
Each message tells you the shell of the current tab and its current folder when known, and usually the last lines of its screen (the output of the latest commands) and any text the user selected. It also lists the other open tabs; when the user names a tab or speaks of the tabs, the last lines of those tabs are included too.

HOW TO REPLY
- Be brief and practical, in the user's language. Plain text; short lists are fine; no headings.
- Put each command or script the user could run in its own fenced code block, tagged with the language of the shell it is for: ```powershell, ```cmd, ```bash or ```lisp. The app shows a Run button for every such block and runs it in the right tab only when the user clicks it. Prefer one command per block; a short multi-line script is fine.
- Use the syntax of the current tab's shell, unless another one is clearly better (then say so: a ```lisp block runs in a Lisp tab, the others in the current tab).
- Use ```text (never a shell tag) for output, file contents or examples that must not be run.
- Before commands that delete, overwrite, format, change system settings, install software or need administrator rights, say clearly what they do; offer a safe preview first when there is one (for example -WhatIf, a listing, a dry run).
- Never invent output or file contents. If you need to see something, propose a command that shows it.
- If the screen shows an error, explain the cause in one or two sentences and give the fix.

CORBOLPROMPT LISP
A small Common Lisp dialect, interpreted. Symbols are case-insensitive and print in lowercase; nil is the empty list. Lisp-2: use #'f and funcall.
Supported: integers of any size and floats (/ gives an exact integer when divisible, otherwise a float; no ratios); strings, characters (#\a), keywords, lists, vectors (vector, make-array, aref), hash tables (make-hash-table, gethash, remhash, maphash; keys compared with equal), structures (defstruct with make-x, x-slot accessors usable with setf, x-p, copy-x).
Special forms and macros: quote, quasiquote with , and ,@, if, cond, case, ecase, when, unless, and, or, progn, prog1, let, let*, flet, labels, lambda (&optional &rest &body &key &aux), function, setq, setf (on variables, car, cdr, nth, gethash, aref, struct accessors), incf, decf, push, pushnew, pop, defun, defmacro, defvar, defparameter (both make special, dynamically bound variables as in Common Lisp; also (declaim (special x))), defconstant, progv, block, return-from, return, catch, throw, unwind-protect, ignore-errors, handler-case (any clause catches any error; (error ""msg ~a"" x) signals one), dolist, dotimes, do, do*, loop (for x in/on/across, for i from/to/below/downto/by, for x = a then b, with, repeat, while, until, do, collect, append, sum, count, maximize, minimize, when/if/unless, into, return, finally, destructuring like (a b)), destructuring-bind, multiple-value-bind (only the first value), time.
Functions: arithmetic and math (+ - * / mod rem floor ceiling round truncate expt sqrt isqrt exp log sin cos atan gcd lcm random abs min max 1+ 1-), comparisons, predicates, list functions (car cdr cons list list* append reverse length nth nthcdr last butlast first..tenth member assoc rassoc getf acons copy-list), mapcar mapc mapcan maplist map reduce remove remove-if remove-if-not find find-if position position-if count count-if every some sort subseq concatenate coerce remove-duplicates search, strings (string-upcase string-downcase string-trim string= string< char char-code code-char parse-integer write-to-string read-from-string), format (~a ~s ~d ~b ~o ~x ~f ~$ ~% ~& ~~ ~{ ~} ~^ ~[ ~; ~] ~( ~) ~p ~c ~r, with column and :@ modifiers), print prin1 princ terpri write-line, read-line, eval, apply, funcall, gensym, macroexpand, type-of.
Extras that are not Common Lisp: (range n) / (range a b step), (iota n), (split-string s [sep]), (join list [sep]), (string-contains s sub), (string-replace s old new), (filter pred seq), (flatten tree), (now).
System: (sh ""cmd"") runs a command with cmd.exe and shows its output, returns the exit code; (pwsh ""cmd"") the same with PowerShell; (run ""cmd"") returns the output as a string; (run-lines ""cmd"") as a list of lines; (cd ""dir""), (pwd), (ls [dir]) returns a list of names (folders end with /); (read-file f), (read-lines f), (write-file f text), (append-file f text), (file-exists-p f), (mkdir d), (delete-file f), (getenv ""X""), (load ""file.lisp""), (sleep s), (clear), (exit).
Not supported: CLOS (defclass, defmethod), packages, real condition system and restarts, multiple values, ratios, complex numbers, streams other than the terminal, reader macros.";

        // Linguaggi dei blocchi che diventano comandi eseguibili
        static readonly Dictionary<string, string> runnable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "powershell", "powershell" }, { "pwsh", "powershell" }, { "ps1", "powershell" }, { "ps", "powershell" },
            { "cmd", "cmd" }, { "bat", "cmd" }, { "batch", "cmd" }, { "dos", "cmd" },
            { "bash", "bash" }, { "sh", "bash" }, { "shell", "bash" }, { "zsh", "bash" }, { "console", "" }, { "terminal", "" },
            { "lisp", "lisp" }, { "commonlisp", "lisp" }, { "common-lisp", "lisp" }, { "cl", "lisp" }, { "elisp", "lisp" }, { "scheme", "lisp" },
            { "", "" }
        };

        public class Block
        {
            public string Lang;   // powershell, cmd, bash, lisp, o "" (per la shell della scheda)
            public string Code;
            public string Explanation = "";   // la spiegazione scritta dall'assistente subito prima del blocco
        }

        static readonly Regex Fence = new Regex(@"```[ \t]*([A-Za-z0-9_+\-]*)[^\n]*\n(.*?)\n?[ \t]*```", RegexOptions.Singleline);

        // Blocchi di codice eseguibili della risposta
        public static List<Block> Blocks(string reply)
        {
            var list = new List<Block>();
            int last = 0;
            reply = reply ?? "";
            foreach (Match m in Fence.Matches(reply))
            {
                string before = reply.Substring(last, m.Index - last);
                last = m.Index + m.Length;
                string lang;
                if (!runnable.TryGetValue(m.Groups[1].Value.Trim(), out lang)) continue;
                string code = m.Groups[2].Value.Replace("\r\n", "\n").Trim('\n');
                if (code.Trim().Length == 0) continue;
                list.Add(new Block { Lang = lang, Code = code, Explanation = Explain(before) });
            }
            return list;
        }

        // L'ultimo paragrafo prima del blocco, senza markdown: è la spiegazione del comando
        static string Explain(string text)
        {
            var paras = Regex.Split(text.Replace("\r\n", "\n"), @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (paras.Count == 0) return "";
            string p0 = paras[paras.Count - 1];
            p0 = Regex.Replace(p0, @"^\s*([-*]|\d+\.)\s+", "", RegexOptions.Multiline);   // elenchi
            p0 = Regex.Replace(p0, @"(\*\*|__|`)", "");                                  // grassetto, codice
            p0 = Regex.Replace(p0, @"\s+", " ").Trim().TrimEnd(':').Trim();
            return p0.Length > 500 ? p0.Substring(0, 499) + "…" : p0;
        }

        // Pezzi della risposta, per mostrarla: testo normale e blocchi di codice (con il loro linguaggio)
        public static List<KeyValuePair<string, string>> Parts(string reply)
        {
            var parts = new List<KeyValuePair<string, string>>();
            int last = 0;
            reply = reply ?? "";
            foreach (Match m in Fence.Matches(reply))
            {
                if (m.Index > last) parts.Add(new KeyValuePair<string, string>(null, reply.Substring(last, m.Index - last)));
                parts.Add(new KeyValuePair<string, string>(m.Groups[1].Value.Trim(), m.Groups[2].Value.Replace("\r\n", "\n").Trim('\n')));
                last = m.Index + m.Length;
            }
            if (last < reply.Length) parts.Add(new KeyValuePair<string, string>(null, reply.Substring(last)));
            return parts;
        }
    }
}
