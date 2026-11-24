// Dati strutturati nel Lisp di CorbolPrompt: JSON, l'output di PowerShell come liste di plist (ps),
// le keyword usate come funzioni (:name riga), le funzioni per lavorare sulle righe
// (take, drop, sort-by, group-by, count-by, sum-by, select) e la stampa a tabella (table).
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CorbolPrompt
{
    partial class Interp
    {
        // ---------- keyword come funzioni: (:name riga) = (getf riga :name) ----------

        readonly Dictionary<Sym, Builtin> keywordFns = new Dictionary<Sym, Builtin>();

        LispFn KeywordFn(Sym k)
        {
            Builtin b;
            lock (keywordFns)
            {
                if (keywordFns.TryGetValue(k, out b)) return b;
                b = new Builtin { Name = k.Name, Min = 1, Max = 2, F = a => GetField(a[0], k, a.Length > 1 ? a[1] : null) };
                keywordFns[k] = b;
            }
            return b;
        }

        // Il valore di un campo in una plist, una tabella hash o una struttura
        object GetField(object o, Sym key, object def)
        {
            if (o == null) return def;
            if (o is Cons)
            {
                for (object l = o; l is Cons && ((Cons)l).Cdr is Cons; l = ((Cons)((Cons)l).Cdr).Cdr)
                    if (((Cons)l).Car == key) return ((Cons)((Cons)l).Cdr).Car;
                return def;
            }
            var ht = o as Dictionary<object, object>;
            if (ht != null)
            {
                object v;
                if (ht.TryGetValue(key, out v)) return v;
                if (ht.TryGetValue(key.Name.Substring(1), out v)) return v;
                return def;
            }
            var st = o as LispStruct;
            if (st != null)
            {
                string n = key.Name.Substring(1);
                for (int i = 0; i < st.Type.Slots.Length; i++) if (st.Type.Slots[i].Name == n) return st.Slots[i];
                return def;
            }
            throw new LispError(key.Name + ": " + Printer.Print(o, true) + " non è una plist, una tabella hash o una struttura");
        }

        // Una plist: (:chiave valore :chiave valore ...)
        static bool IsPlist(object o)
        {
            var c = o as Cons;
            if (c == null) return false;
            for (object l = o; l != null; l = ((Cons)((Cons)l).Cdr).Cdr)
            {
                var lc = l as Cons;
                if (lc == null || !(lc.Car is Sym) || !((Sym)lc.Car).IsKeyword || !(lc.Cdr is Cons)) return false;
            }
            return true;
        }

        static List<KeyValuePair<Sym, object>> PlistPairs(object o)
        {
            var r = new List<KeyValuePair<Sym, object>>();
            for (object l = o; l is Cons && ((Cons)l).Cdr is Cons; l = ((Cons)((Cons)l).Cdr).Cdr)
                r.Add(new KeyValuePair<Sym, object>((Sym)((Cons)l).Car, ((Cons)((Cons)l).Cdr).Car));
            return r;
        }

        // Funzione di chiave: una keyword (campo) o una funzione
        Func<object, object> FieldFn(object key)
        {
            var k = key as Sym;
            if (k != null && k.IsKeyword) return x => GetField(x, k, null);
            return x => Call(key, x);
        }

        // Confronto "naturale" per sort-by: nil prima, poi numeri, poi testo (senza maiuscole/minuscole)
        static int NaturalCompare(object a, object b)
        {
            if (a == null || b == null) return a == null ? (b == null ? 0 : -1) : 1;
            bool na = Num.Is(a), nb = Num.Is(b);
            if (na && nb) return Num.Compare(a, b);
            if (na != nb) return na ? -1 : 1;
            return string.Compare(Printer.Print(a, false), Printer.Print(b, false), StringComparison.CurrentCultureIgnoreCase);
        }

        void DataBuiltins()
        {
            // ---- JSON ----
            Def("json-parse", 1, 1, a => new JsonReader(this, Str(a[0])).ReadAll());
            Def("json-encode", 1, -1, a =>
            {
                var sb = new StringBuilder();
                JsonWrite(sb, a[0], Truthy(KeyArg(a, 1, ":pretty", null)) ? 0 : -1);
                return sb.ToString();
            });
            Def("read-json", 1, 1, a => new JsonReader(this, System.IO.File.ReadAllText(FullPath(Str(a[0])), Encoding.UTF8)).ReadAll());

            // ---- PowerShell come dati: (ps "Get-Process | select Name, Id") ----
            // senza Select-Object prende le colonne che PowerShell mostrerebbe; :all t tutte le proprietà (può essere lento)
            Def("ps", 1, -1, a => RunPsData(Str(a[0]), Num.ToInt(KeyArg(a, 1, ":depth", 2L)), Truthy(KeyArg(a, 1, ":all", null))));

            // ---- righe di dati (il dato va per ultimo, così funziona con ->>) ----
            Def("take", 2, 2, a => { int n = Num.ToInt(a[0]); return SameSeq(a[1], Seq(a[1]).Take(Math.Max(0, n)).ToList()); });
            Def("drop", 2, 2, a => { int n = Num.ToInt(a[0]); return SameSeq(a[1], Seq(a[1]).Skip(Math.Max(0, n)).ToList()); });
            Def("sort-by", 2, -1, a =>
            {
                // (sort-by :cpu righe :desc t) oppure, comodo con ->>, (sort-by :cpu :desc t righe)
                var key = FieldFn(a[0]);
                bool optsFirst = a.Length == 4 && a[1] is Sym && ((Sym)a[1]).Name == ":desc";
                var data = optsFirst ? a[3] : a[1];
                bool desc = Truthy(optsFirst ? a[2] : KeyArg(a, 2, ":desc", null));
                var keyed = Seq(data).Select((x, i) => new { x, i, k = key(x) }).ToList();
                keyed.Sort((p, q) => { int c = NaturalCompare(p.k, q.k); if (desc) c = -c; return c != 0 ? c : p.i.CompareTo(q.i); });
                return SameSeq(data, keyed.Select(p => p.x).ToList());
            });
            Def("group-by", 2, 2, a =>
            {
                var key = FieldFn(a[0]);
                var groups = new List<KeyValuePair<object, List<object>>>();
                var index = new Dictionary<object, int>(EqualComparer.Instance);
                foreach (var x in Seq(a[1]))
                {
                    var k = key(x);
                    int i;
                    if (!index.TryGetValue(k ?? NilKey, out i)) { i = groups.Count; index[k ?? NilKey] = i; groups.Add(new KeyValuePair<object, List<object>>(k, new List<object>())); }
                    groups[i].Value.Add(x);
                }
                return ToList(groups.Select(g => (object)new Cons(g.Key, ToList(g.Value, null))).ToList(), null);
            });
            Def("count-by", 2, 2, a =>
            {
                var key = FieldFn(a[0]);
                var counts = new List<KeyValuePair<object, long>>();
                var index = new Dictionary<object, int>(EqualComparer.Instance);
                foreach (var x in Seq(a[1]))
                {
                    var k = key(x);
                    int i;
                    if (!index.TryGetValue(k ?? NilKey, out i)) { i = counts.Count; index[k ?? NilKey] = i; counts.Add(new KeyValuePair<object, long>(k, 0)); }
                    counts[i] = new KeyValuePair<object, long>(k, counts[i].Value + 1);
                }
                // dal più frequente; a parità, nell'ordine in cui sono comparsi
                return ToList(counts.Select((c, i) => new { c, i }).OrderByDescending(p => p.c.Value).ThenBy(p => p.i)
                    .Select(p => (object)new Cons(p.c.Key, p.c.Value)).ToList(), null);
            });
            Def("sum-by", 2, 2, a =>
            {
                var key = FieldFn(a[0]);
                object r = 0L;
                foreach (var x in Seq(a[1])) { var v = key(x); if (v != null) r = Num.Add(r, v); }
                return r;
            });
            // (select :name :id righe): tiene solo quei campi; funziona anche su una sola plist
            Def("select", 2, -1, a =>
            {
                var keys = a.Take(a.Length - 1).ToList();
                foreach (var k in keys) if (!(k is Sym) || !((Sym)k).IsKeyword) throw new LispError("select: i campi vanno indicati come keyword (:nome), non " + Printer.Print(k, true));
                var data = a[a.Length - 1];
                Func<object, object> pick = row =>
                {
                    var r = new List<object>();
                    foreach (Sym k in keys) { r.Add(k); r.Add(GetField(row, k, null)); }
                    return ToList(r, null);
                };
                if (IsPlist(data) || data is Dictionary<object, object> || data is LispStruct) return pick(data);
                return SameSeq(data, Seq(data).Select(pick).ToList());
            });
            // *1 *2 *3: gli ultimi tre risultati del REPL
            foreach (var n in new[] { "*1", "*2", "*3" }) Intern(n).Value = null;
            Def("plist-keys", 1, 1, a => ToList(PlistPairs(a[0]).Select(p => (object)p.Key).ToList(), null));
            Def("plistp", 1, 1, a => Bool(IsPlist(a[0])));

            // ---- tabella: (table righe) o (table :name :id righe) ----
            Def("table", 1, -1, a =>
            {
                var cols = a.Take(a.Length - 1).ToList();
                foreach (var k in cols) if (!(k is Sym) || !((Sym)k).IsKeyword) throw new LispError("table: le colonne vanno indicate come keyword (:nome), non " + Printer.Print(k, true));
                var data = a[a.Length - 1];
                if (IsPlist(data) || data is Dictionary<object, object> || data is LispStruct) data = new Cons(data, null);
                FreshLine();
                Write(RenderTable(Seq(data), cols.Cast<Sym>().ToList(), 0, TermCols - 1));
                QuietResult = true;
                return null;
            });

            EvalString(@"
(defmacro -> (x &rest forms)
  (dolist (f forms x)
    (setq x (if (consp f) (list* (car f) x (cdr f)) (list f x)))))
(defmacro ->> (x &rest forms)
  (dolist (f forms x)
    (setq x (if (consp f) (append f (list x)) (list f x)))))
");
        }

        // ---------- PowerShell ----------

        // Per gli oggetti "grezzi" (non passati da Select-Object) prende le colonne della vista a tabella
        // di PowerShell, cioè quelle che si vedono in console: leggere tutte le proprietà di oggetti come
        // i processi richiede anche un minuto. Senza vista usa DefaultDisplayPropertySet, se c'è.
        // Gli enum diventano testo; i valori calcolati dalla vista ("{0:N2}" -f ...) tornano numeri.
        const string PsColumns = @"
$__cache = @{}
function __cpValue($v, $calc) {
  if ($v -is [enum]) { return $v.ToString() }
  if ($v -is [datetime]) { return ([datetime]$v).ToString('o') }   # come PowerShell 7, anche in 5.1
  if ($v -is [string]) { $v = $v -replace ""\x1b\[[0-9;]*m"", ''; if ($v -eq '') { return $null } }
  if ($calc -and $v -is [string]) {
    $d = 0.0
    if ([double]::TryParse($v.Trim(), [Globalization.NumberStyles]::Number, [Globalization.CultureInfo]::CurrentCulture, [ref]$d)) {
      if ($v -notmatch '[\.,]' -and [math]::Abs($d) -lt 9e18) { return [long]$d }
      return $d
    }
  }
  return $v
}
function __cpProp($name, $p) { @{ n = [string]$name; e = [scriptblock]::Create('__cpValue $_.''' + $p.Replace(""'"", ""''"") + ''' $false') } }
function __cpColumns($o) {
  $t = $o.PSObject.TypeNames[0]
  if ($__cache.ContainsKey($t)) { return $__cache[$t] }
  $cols = $null
  foreach ($tn in $o.PSObject.TypeNames) {
    $fd = Get-FormatData -TypeName $tn -ErrorAction SilentlyContinue
    if (-not $fd) { continue }
    $view = @($fd.FormatViewDefinition | Where-Object { $_.Control -is [System.Management.Automation.TableControl] })[0]
    if (-not $view) { continue }
    $headers = $view.Control.Headers
    $row = $view.Control.Rows[0]
    $cols = @(for ($i = 0; $i -lt $row.Columns.Count; $i++) {
      $e = $row.Columns[$i].DisplayEntry
      $label = if ($i -lt $headers.Count -and $headers[$i].Label) { $headers[$i].Label } else { $e.Value }
      if ($o.PSObject.Properties[$label]) { __cpProp $label $label }   # il valore vero, non quello formattato
      elseif ($e.ValueType -eq 'Property') { __cpProp $label $e.Value }
      else { @{ n = [string]$label; e = [scriptblock]::Create('__cpValue (& {' + $e.Value + '}) $true') } }
    })
    break
  }
  if (-not $cols) {
    $dd = $o.PSStandardMembers.DefaultDisplayPropertySet.ReferencedPropertyNames
    if ($dd) { $cols = @($dd | ForEach-Object { __cpProp $_ $_ }) }
  }
  if (-not $cols) {
    # nessuna vista: tutte le proprietà, con gli oggetti annidati ridotti a testo
    $cols = @($o.PSObject.Properties | Where-Object { $_.MemberType -match 'Property' -and $_.Name -notmatch '^PS(Path|ParentPath|ChildName|Drive|Provider|IsContainer)$' } | ForEach-Object {
      $p = $_.Name.Replace(""'"", ""''"")
      @{ n = [string]$_.Name; e = [scriptblock]::Create('try { $v = $_.''' + $p + '''; if ($v -eq $null -or $v -is [string] -or $v -is [ValueType]) { __cpValue $v $false } else { ""$v"" } } catch { $null }') }
    })
  }
  $__cache[$t] = $cols
  return $cols
}
function __cpRaw($o) {
  return -not ($o -is [string] -or $o -is [ValueType] -or $o -is [System.Collections.IDictionary] -or
    $o.PSObject.TypeNames -contains 'System.Management.Automation.PSCustomObject')
}
$r = @($r | ForEach-Object { if ($null -eq $_) { return }; if (__cpRaw $_) { $c = __cpColumns $_; if ($c) { $_ | Select-Object -Property $c } else { $_ } } else { $_ } })
";

        object RunPsData(string command, int depth, bool all)
        {
            string script =
                "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8\n" +
                "if ($PSStyle) { $PSStyle.OutputRendering = 'PlainText' }\n" +
                "try {\n$r = @(\n" + command + "\n)\n" +
                (all ? "" : PsColumns) +
                "$__j = @{ InputObject = $r; Depth = " + Math.Max(0, depth) + "; Compress = $true; WarningAction = 'SilentlyContinue' }\n" +
                "if ($PSVersionTable.PSVersion.Major -ge 7) { $__j.EnumsAsStrings = $true }\n" +
                "if ($r.Count -eq 0) { '[]' } else { ConvertTo-Json @__j }\n" +
                "} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var output = new StringBuilder();
            var errors = new StringBuilder();
            int code = RunProcess(PowerShellExe(), "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded, false, output, errors);
            string err = errors.ToString().Trim();
            if (code != 0 || output.Length == 0) throw new LispError("ps: " + (err.Length > 0 ? err : "PowerShell è uscito con il codice " + code));
            object r = new JsonReader(this, output.ToString()).ReadAll();
            if (r != null && !(r is Cons && !IsPlist(r))) r = new Cons(r, null);   // un solo oggetto: sempre una lista di righe
            return r;
        }

        // ---------- JSON in uscita ----------

        // indent < 0: tutto su una riga
        void JsonWrite(StringBuilder sb, object o, int indent)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o == T) { sb.Append("true"); return; }
            var sym = o as Sym;
            if (sym != null)
            {
                if (sym.Name == ":false") { sb.Append("false"); return; }
                if (sym.Name == ":null") { sb.Append("null"); return; }
                JsonString(sb, sym.IsKeyword ? sym.Name.Substring(1) : sym.Name);
                return;
            }
            if (o is string) { JsonString(sb, (string)o); return; }
            if (o is char) { JsonString(sb, o.ToString()); return; }
            if (o is long || o is BigInteger) { sb.Append(o.ToString()); return; }
            if (o is double)
            {
                double d = (double)o;
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new LispError("json-encode: " + Printer.Float(d) + " non si può scrivere in JSON");
                sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var ht = o as Dictionary<object, object>;
            if (ht != null) { JsonObject(sb, ht.Select(kv => new KeyValuePair<string, object>(kv.Key == NilKey ? "nil" : Str(kv.Key), kv.Value)).ToList(), indent); return; }
            var st = o as LispStruct;
            if (st != null) { JsonObject(sb, st.Type.Slots.Select((s, i) => new KeyValuePair<string, object>(s.Name, st.Slots[i])).ToList(), indent); return; }
            if (IsPlist(o)) { JsonObject(sb, PlistPairs(o).Select(p => new KeyValuePair<string, object>(p.Key.Name.Substring(1), p.Value)).ToList(), indent); return; }
            if (o is Cons || o is object[])
            {
                var items = Seq(o);
                sb.Append('[');
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (indent >= 0) NewLine(sb, indent + 1);
                    JsonWrite(sb, items[i], indent < 0 ? -1 : indent + 1);
                }
                if (indent >= 0 && items.Count > 0) NewLine(sb, indent);
                sb.Append(']');
                return;
            }
            JsonString(sb, Printer.Print(o, false));
        }

        void JsonObject(StringBuilder sb, List<KeyValuePair<string, object>> fields, int indent)
        {
            sb.Append('{');
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (indent >= 0) NewLine(sb, indent + 1);
                JsonString(sb, fields[i].Key);
                sb.Append(indent >= 0 ? ": " : ":");
                JsonWrite(sb, fields[i].Value, indent < 0 ? -1 : indent + 1);
            }
            if (indent >= 0 && fields.Count > 0) NewLine(sb, indent);
            sb.Append('}');
        }

        static void NewLine(StringBuilder sb, int indent) { sb.Append('\n').Append(' ', indent * 2); }

        static void JsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // "WorkingSet64" -> working-set64, "PSComputerName" -> ps-computer-name, "user_name" -> user-name
        public static string KebabCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c))   // "CPU(s)" -> cpu-s, "user_name" -> user-name
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
                    continue;
                }
                if (char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != '-')
                {
                    char p = name[i - 1];
                    if (char.IsLower(p) || char.IsDigit(p) || (char.IsUpper(p) && i + 1 < name.Length && char.IsLower(name[i + 1]))) sb.Append('-');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            string r = sb.ToString().TrimEnd('-');
            return r.Length > 0 ? r : "_";
        }

        // ---------- tabella ----------

        // Larghezza del terminale (la imposta il REPL; 0 = senza limite) e "il risultato non va stampato":
        // table e cat hanno già scritto quello che serviva, il nil che restituiscono non dice niente.
        public int TermCols;
        public bool QuietResult;

        static bool IsRecord(object r) { return IsPlist(r) || r is Dictionary<object, object> || r is LispStruct; }

        // Il REPL mostra come tabella una lista di plist, tabelle hash o strutture
        public string AutoTable(object result)
        {
            var c = result as Cons;
            if (c == null) return null;
            var rows = new List<object>();
            for (object l = c; l != null; l = ((Cons)l).Cdr)
            {
                var lc = l as Cons;
                if (lc == null || !IsRecord(lc.Car)) return null;
                rows.Add(lc.Car);
            }
            return RenderTable(rows, new List<Sym>(), 30, TermCols - 1);
        }

        // maxRows, maxWidth: 0 o meno = senza limite
        string RenderTable(List<object> rows, List<Sym> cols, int maxRows, int maxWidth)
        {
            const int maxCell = 48, minCell = 6;
            if (rows.Count == 0) return "\x1b[2m(" + L.T("nessuna riga", "no rows") + ")\x1b[0m\n";
            bool keyed = rows.All(r => r == null || IsRecord(r));
            var headers = new List<string>();
            var cells = new List<List<object>>();
            var shown = maxRows > 0 && rows.Count > maxRows ? rows.Take(maxRows).ToList() : rows;
            if (keyed)
            {
                if (cols.Count == 0)
                {
                    var seen = new HashSet<Sym>();
                    foreach (var r in rows)
                    {
                        if (r == null) continue;
                        IEnumerable<Sym> ks = r is Dictionary<object, object> ? ((Dictionary<object, object>)r).Keys.Select(k => k as Sym ?? Intern(":" + Str(k)))
                            : r is LispStruct ? ((LispStruct)r).Type.Slots.Select(s => Intern(":" + s.Name))
                            : PlistPairs(r).Select(p => p.Key);
                        foreach (var k in ks) if (seen.Add(k)) cols.Add(k);
                    }
                }
                headers = cols.Select(c => c.Name.TrimStart(':')).ToList();
                foreach (var r in shown) cells.Add(cols.Select(c => GetField(r, c, null)).ToList());
            }
            else
            {
                foreach (var r in shown) cells.Add(r is Cons || r is object[] ? Seq(r) : new List<object> { r });
                int n = cells.Max(c => c.Count);
                for (int i = 0; i < n; i++) headers.Add("");
            }
            int ncol = headers.Count;
            var text = cells.Select(row => Enumerable.Range(0, ncol).Select(i => Cell(i < row.Count ? row[i] : null, maxCell)).ToList()).ToList();
            var right = Enumerable.Range(0, ncol).Select(i => cells.All(row => i >= row.Count || row[i] == null || Num.Is(row[i]))).ToList();
            var width = Enumerable.Range(0, ncol).Select(i => Math.Max(headers[i].Length, text.Count == 0 ? 0 : text.Max(row => row[i].Length))).ToList();

            // stare nella larghezza del terminale: prima si stringono le colonne più larghe, poi si nascondono le ultime
            int visible = ncol;
            if (maxWidth > 0)
            {
                Func<int> total = () => width.Take(visible).Sum() + 2 * (visible - 1);
                while (total() > maxWidth)
                {
                    int widest = 0;
                    for (int i = 1; i < visible; i++) if (width[i] > width[widest]) widest = i;
                    if (width[widest] <= minCell) break;
                    width[widest]--;
                }
                while (visible > 1 && total() > maxWidth) visible--;
            }

            var sb = new StringBuilder();
            Func<string, int, bool, string> pad = (s, w, r) =>
            {
                if (s.Length > w) s = w > 1 ? s.Substring(0, w - 1) + "…" : s.Substring(0, w);
                return r ? s.PadLeft(w) : s.PadRight(w);
            };
            if (headers.Any(h => h.Length > 0))
            {
                sb.Append("\x1b[1m").Append(string.Join("  ", Enumerable.Range(0, visible).Select(i => pad(headers[i], width[i], right[i]))).TrimEnd()).Append("\x1b[0m\n");
                sb.Append("\x1b[2m").Append(string.Join("  ", width.Take(visible).Select(w => new string('─', w)))).Append("\x1b[0m\n");
            }
            foreach (var row in text)
                sb.Append(string.Join("  ", Enumerable.Range(0, visible).Select(i => pad(row[i], width[i], right[i]))).TrimEnd()).Append('\n');

            var notes = new List<string>();
            if (shown.Count < rows.Count) notes.Add(L.F("mostrate {0} di {1} righe, tutte: (table *1)", "showing {0} of {1} rows, all of them: (table *1)", shown.Count, rows.Count));
            else if (rows.Count > 1) notes.Add(L.F("{0} righe", "{0} rows", rows.Count));
            if (visible < ncol)
            {
                var hidden = headers.Skip(visible).ToList();
                string names = string.Join(" ", hidden.Take(6)) + (hidden.Count > 6 ? " …" : "");
                notes.Add(L.F("{0} colonne nascoste: {1}", "{0} hidden columns: {1}", hidden.Count, names));
            }
            if (notes.Count > 0) sb.Append("\x1b[2m(" + string.Join("; ", notes) + ")\x1b[0m\n");
            return sb.ToString();
        }

        static string Cell(object v, int max)
        {
            if (v == null) return "";
            // date ISO ("2026-11-24T09:04:21.757+01:00") in breve: 2026-11-24 09:04
            var iso = v as string;
            if (iso != null && iso.Length >= 16 && iso[4] == '-' && iso[7] == '-' && iso[10] == 'T' && iso[13] == ':' && char.IsDigit(iso[0]) && char.IsDigit(iso[15]))
                return iso.Substring(0, 10) + " " + iso.Substring(11, 5);
            string s = Printer.Print(v, false).Replace("\r\n", " ").Replace('\n', ' ').Replace('\t', ' ');
            return s.Length > max ? s.Substring(0, max - 1) + "…" : s;
        }
    }

    // Lettore JSON: oggetti -> plist con chiavi keyword in kebab-case, array -> liste,
    // true -> t, false e null -> nil, numeri interi -> interi (anche grandi), gli altri -> double.
    // Le date "/Date(ms)/" di PowerShell 5 diventano testo "aaaa-mm-ggThh:mm:ss" (ora locale).
    sealed class JsonReader
    {
        readonly Interp it;
        readonly string s;
        int pos;

        public JsonReader(Interp it, string s) { this.it = it; this.s = s; }

        public object ReadAll()
        {
            var v = Value();
            Space();
            if (pos < s.Length) throw Error("testo in più dopo la fine");
            return v;
        }

        LispError Error(string what) { return new LispError("JSON non valido (" + what + ", al carattere " + (pos + 1) + ")"); }

        void Space() { while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\n' || s[pos] == '\r' || s[pos] == '﻿')) pos++; }

        object Value()
        {
            Space();
            if (pos >= s.Length) throw Error("finisce troppo presto");
            char c = s[pos];
            if (c == '{')
            {
                pos++;
                var items = new List<object>();
                Space();
                if (pos < s.Length && s[pos] == '}') { pos++; return null; }
                while (true)
                {
                    Space();
                    if (pos >= s.Length || s[pos] != '"') throw Error("serve il nome di un campo");
                    pos++;
                    string key = StringBody();
                    Space();
                    if (pos >= s.Length || s[pos] != ':') throw Error("manca ':'");
                    pos++;
                    items.Add(it.Intern(":" + Interp.KebabCase(key)));
                    items.Add(Value());
                    Space();
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    if (pos < s.Length && s[pos] == '}') { pos++; break; }
                    throw Error("manca ',' o '}'");
                }
                return Interp.ToList(items, null);
            }
            if (c == '[')
            {
                pos++;
                var items = new List<object>();
                Space();
                if (pos < s.Length && s[pos] == ']') { pos++; return null; }
                while (true)
                {
                    items.Add(Value());
                    Space();
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    if (pos < s.Length && s[pos] == ']') { pos++; break; }
                    throw Error("manca ',' o ']'");
                }
                return Interp.ToList(items, null);
            }
            if (c == '"') { pos++; return DateOrString(StringBody()); }
            if (Word("true")) return it.T;
            if (Word("false") || Word("null")) return null;
            if (c == '-' || char.IsDigit(c)) return Number();
            throw Error("carattere inatteso '" + c + "'");
        }

        bool Word(string w)
        {
            if (string.CompareOrdinal(s, pos, w, 0, w.Length) != 0) return false;
            pos += w.Length;
            return true;
        }

        object Number()
        {
            int start = pos;
            if (s[pos] == '-') pos++;
            while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            bool frac = false;
            if (pos < s.Length && s[pos] == '.') { frac = true; pos++; while (pos < s.Length && char.IsDigit(s[pos])) pos++; }
            if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
            {
                frac = true; pos++;
                if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                while (pos < s.Length && char.IsDigit(s[pos])) pos++;
            }
            string tok = s.Substring(start, pos - start);
            if (tok == "-" || tok.Length == 0) throw Error("numero non valido");
            if (frac)
            {
                double d;
                if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw Error("numero non valido");
                return d;
            }
            long l;
            if (long.TryParse(tok, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
            return Num.Norm(BigInteger.Parse(tok, CultureInfo.InvariantCulture));
        }

        string StringBody()
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw Error("stringa non chiusa");
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= s.Length) throw Error("stringa non chiusa");
                char e = s[pos++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (pos + 4 > s.Length || !int.TryParse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Error("\\u non valido");
                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }

        static object DateOrString(string v)
        {
            if (v.StartsWith("/Date(") && v.EndsWith(")/"))
            {
                string inner = v.Substring(6, v.Length - 8);
                int cut = inner.IndexOfAny(new[] { '+', '-' }, 1);
                long ms;
                if (long.TryParse(cut > 0 ? inner.Substring(0, cut) : inner, out ms))
                    return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            }
            return v;
        }
    }
}
