// CorbolPrompt Lisp: un piccolo dialetto di Common Lisp, interpretato.
// Ciò che c'è: numeri interi grandi e decimali, stringhe, caratteri, simboli e keyword, liste, vettori, tabelle hash,
// strutture (defstruct); defun, defmacro con quasiquote, lambda e chiusure, let/let*, flet/labels, cond/case,
// loop (le forme più usate), dolist/dotimes/do, block/return, catch/throw, unwind-protect, handler-case,
// format (~a ~s ~d ~f ~% ~& ~{ ~} ~^ ...), più comandi per il sistema: (sh "dir"), (pwsh "Get-Date"), (run ...),
// (cd ...), (ls), (read-file ...). Spazio dei nomi separato per le funzioni (come Common Lisp): #'f, funcall.
// Ricorsione in coda senza consumare stack; l'esecuzione si interrompe con Ctrl+C (Interrupt).
// I simboli non distinguono maiuscole e minuscole e si stampano in minuscolo; nil è null.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace CorbolPrompt
{
    sealed class Sym
    {
        public static readonly object Unbound = new object();
        public readonly string Name;
        public object Value = Unbound;
        public object Function;    // LispFn o Macro
        public bool Constant;
        public bool Special;       // defvar/defparameter/(declaim (special ...)): legata dinamicamente
        public Sym(string name) { Name = name; }
        public bool IsKeyword { get { return Name.Length > 1 && Name[0] == ':'; } }
        public override string ToString() { return Name; }
    }

    sealed class Cons
    {
        public object Car, Cdr;
        public Cons(object car, object cdr) { Car = car; Cdr = cdr; }
    }

    abstract class LispFn { public string Name = "lambda"; }

    sealed class Builtin : LispFn
    {
        public Func<object[], object> F;
        public int Min, Max;   // Max < 0 = senza limite
    }

    sealed class Closure : LispFn
    {
        public Sym[] Req = new Sym[0], Opt = new Sym[0], Keys = new Sym[0], Aux = new Sym[0];
        public object[] OptDef = new object[0], KeyDef = new object[0], AuxDef = new object[0];
        public Sym Rest;
        public object Body;
        public Env Env;
        public Sym Block;          // defun: (return-from nome ...)
        public bool NeedsBlock;    // il corpo usa return-from/return: niente ricorsione in coda attraverso il blocco
    }

    sealed class Macro { public string Name; public Closure Fn; }

    sealed class LispStruct
    {
        public StructType Type;
        public object[] Slots;
    }

    sealed class StructType
    {
        public string Name;
        public Sym[] Slots;
        public object[] Defaults;
        public Env Env;
    }

    sealed class LispCondition
    {
        public string Message;
        public override string ToString() { return Message; }
    }

    class LispError : Exception { public LispError(string msg) : base(msg) { } }
    sealed class LispInterrupted : Exception { public LispInterrupted() : base("interrotto") { } }
    sealed class ReturnFrom : Exception { public Sym Name; public object Value; public ReturnFrom(Sym n, object v) { Name = n; Value = v; } }
    sealed class ThrowTag : Exception { public object Tag, Value; public ThrowTag(object t, object v) { Tag = t; Value = v; } }
    sealed class LispExit : Exception { }

    // Legami delle variabili speciali: il valore vive nel simbolo, qui si tiene quello di prima,
    // che Eval e Apply rimettono a posto quando escono (anche per errori e throw).
    sealed class DynStack
    {
        Sym[] syms = new Sym[16];
        object[] olds = new object[16];
        public int Count;

        public void Push(Sym s, object v)
        {
            if (Count == syms.Length) { Array.Resize(ref syms, Count * 2); Array.Resize(ref olds, Count * 2); }
            syms[Count] = s; olds[Count] = s.Value; Count++;
            s.Value = v;
        }

        public void Unwind(int mark)
        {
            while (Count > mark)
            {
                Count--;
                syms[Count].Value = olds[Count];
                syms[Count] = null; olds[Count] = null;
            }
        }
    }

    sealed class Env
    {
        public readonly Env Parent;
        public readonly DynStack Dyn;
        Sym[] keys = new Sym[4];
        object[] vals = new object[4];
        int n;
        public Dictionary<Sym, LispFn> Funs;   // flet / labels

        public Env(Env parent) { Parent = parent; Dyn = parent != null ? parent.Dyn : null; }
        public Env(DynStack dyn) { Dyn = dyn; }

        public void Define(Sym s, object v)
        {
            if (s.Special && Dyn != null) { Dyn.Push(s, v); return; }
            for (int i = 0; i < n; i++) if (keys[i] == s) { vals[i] = v; return; }
            if (n == keys.Length) { Array.Resize(ref keys, n * 2); Array.Resize(ref vals, n * 2); }
            keys[n] = s; vals[n] = v; n++;
        }

        public bool TryGet(Sym s, out object v)
        {
            if (s.Special) { v = s.Value == Sym.Unbound ? null : s.Value; return s.Value != Sym.Unbound; }
            for (var e = this; e != null; e = e.Parent)
                for (int i = 0; i < e.n; i++) if (e.keys[i] == s) { v = e.vals[i]; return true; }
            v = null;
            return false;
        }

        public bool TrySet(Sym s, object v)
        {
            if (s.Special) { s.Value = v; return true; }
            for (var e = this; e != null; e = e.Parent)
                for (int i = 0; i < e.n; i++) if (e.keys[i] == s) { e.vals[i] = v; return true; }
            return false;
        }

        public LispFn FindFun(Sym s)
        {
            for (var e = this; e != null; e = e.Parent)
            {
                LispFn f;
                if (e.Funs != null && e.Funs.TryGetValue(s, out f)) return f;
            }
            return null;
        }
    }

    // ===================================================================================
    // Lettura
    // ===================================================================================

    sealed class LispIncomplete : Exception { public LispIncomplete() : base("incompleto") { } }

    sealed class Reader
    {
        public static readonly object Eof = new object();
        readonly Interp it;
        readonly string s;
        int pos;

        public Reader(Interp it, string text) { this.it = it; s = text; }
        public int Pos { get { return pos; } }   // quanto testo è già stato letto (read da uno stream)

        void SkipSpace()
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (char.IsWhiteSpace(c)) { pos++; continue; }
                if (c == ';') { while (pos < s.Length && s[pos] != '\n') pos++; continue; }
                if (c == '#' && pos + 1 < s.Length && s[pos + 1] == '|')
                {
                    int depth = 1; pos += 2;
                    while (depth > 0)
                    {
                        if (pos + 1 >= s.Length) throw new LispIncomplete();
                        if (s[pos] == '|' && s[pos + 1] == '#') { depth--; pos += 2; }
                        else if (s[pos] == '#' && s[pos + 1] == '|') { depth++; pos += 2; }
                        else pos++;
                    }
                    continue;
                }
                break;
            }
        }

        public object Read()
        {
            SkipSpace();
            if (pos >= s.Length) return Eof;
            return ReadForm();
        }

        object ReadForm()
        {
            SkipSpace();
            if (pos >= s.Length) throw new LispIncomplete();
            char c = s[pos];
            switch (c)
            {
                case '(': pos++; return ReadList(')');
                case '[': pos++; return ReadList(']');
                case '{': pos++; return ReadBraces();
                case ')': case ']': case '}': pos++; throw new LispError("parentesi chiusa di troppo");
                case '\'': pos++; return List(it.Intern("quote"), ReadForm());
                case '`': pos++; return List(it.Intern("quasiquote"), ReadForm());
                case ',':
                    pos++;
                    if (pos < s.Length && s[pos] == '@') { pos++; return List(it.Intern("unquote-splicing"), ReadForm()); }
                    return List(it.Intern("unquote"), ReadForm());
                case '"': pos++; return ReadString();
                case '#': return ReadHash();
            }
            return ReadAtom();
        }

        static object List(params object[] items) { return Interp.ToList(items); }

        object ReadList(char close)
        {
            var items = new List<object>();
            object tail = null;
            while (true)
            {
                SkipSpace();
                if (pos >= s.Length) throw new LispIncomplete();
                char c = s[pos];
                if (c == ')' || c == ']') { pos++; break; }
                if (c == '.' && pos + 1 < s.Length && IsDelimiter(s[pos + 1]) && items.Count > 0)
                {
                    pos++;
                    tail = ReadForm();
                    SkipSpace();
                    if (pos >= s.Length) throw new LispIncomplete();
                    if (s[pos] != ')' && s[pos] != ']') throw new LispError("dopo il punto ci va un solo elemento");
                    pos++;
                    break;
                }
                items.Add(ReadForm());
            }
            object r = tail;
            for (int i = items.Count - 1; i >= 0; i--) r = new Cons(items[i], r);
            return r;
        }

        // {:a 1 :b (+ 1 1)} si legge come (list :a 1 :b (+ 1 1)): una plist
        object ReadBraces()
        {
            var items = new List<object> { it.Intern("list") };
            while (true)
            {
                SkipSpace();
                if (pos >= s.Length) throw new LispIncomplete();
                if (s[pos] == '}') { pos++; break; }
                items.Add(ReadForm());
            }
            return Interp.ToList(items, null);
        }

        // #fn(+ % 1) è (lambda (%) (+ % 1)); con %1 %2 ... più argomenti, %& il resto
        object ReadShortLambda()
        {
            var body = ReadList(')');
            int max = 0; bool plain = false, rest = false;
            Action<object> scan = null;
            scan = x =>
            {
                var c = x as Cons;
                if (c != null) { scan(c.Car); scan(c.Cdr); return; }
                var sym = x as Sym;
                if (sym == null || sym.Name.Length == 0 || sym.Name[0] != '%') return;
                if (sym.Name == "%") plain = true;
                else if (sym.Name == "%&") rest = true;
                else { int n; if (int.TryParse(sym.Name.Substring(1), out n) && n > 0 && n < 21) max = Math.Max(max, n); }
            };
            scan(body);
            var pars = new List<object>();
            object form = body;
            if (max == 0 && plain) pars.Add(it.Intern("%"));
            else
            {
                for (int i = 1; i <= max; i++) pars.Add(it.Intern("%" + i));
                if (plain)
                {
                    if (max == 0) pars.Add(it.Intern("%1"));
                    form = List(it.Intern("let"), List(List(it.Intern("%"), it.Intern("%1"))), body);
                }
            }
            if (rest) { pars.Add(it.Intern("&rest")); pars.Add(it.Intern("%&")); }
            return List(it.Intern("lambda"), Interp.ToList(pars, null), form);
        }

        string ReadString()
        {
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length) throw new LispIncomplete();
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\')
                {
                    if (pos >= s.Length) throw new LispIncomplete();
                    char e = s[pos++];
                    // come Common Lisp la barra rende letterale il carattere dopo; in più \n e \t per comodità
                    sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                }
                else sb.Append(c);
            }
        }

        object ReadHash()
        {
            if (pos + 1 >= s.Length) throw new LispIncomplete();
            char c = s[pos + 1];
            if (c == '\'') { pos += 2; return List(it.Intern("function"), ReadForm()); }
            if (c == '(') { pos += 2; var l = ReadList(')'); return Interp.ListToArray(l); }
            if (c == '\\')
            {
                pos += 2;
                if (pos >= s.Length) throw new LispIncomplete();
                int start = pos;
                pos++;
                while (pos < s.Length && !IsDelimiter(s[pos])) pos++;
                string name = s.Substring(start, pos - start);
                if (name.Length == 1) return name[0];
                switch (name.ToLowerInvariant())
                {
                    case "space": return ' ';
                    case "newline": case "linefeed": return '\n';
                    case "tab": return '\t';
                    case "return": return '\r';
                    case "nul": case "null": return '\0';
                    case "escape": case "esc": return '\x1b';
                    case "backspace": return '\b';
                }
                pos = start + 1;
                return name[0];
            }
            if (c == 'x' || c == 'X' || c == 'b' || c == 'B' || c == 'o' || c == 'O')
            {
                pos += 2;
                string tok = Token();
                int radix = char.ToLowerInvariant(c) == 'x' ? 16 : char.ToLowerInvariant(c) == 'b' ? 2 : 8;
                try
                {
                    BigInteger v = BigInteger.Zero;
                    bool neg = tok.StartsWith("-");
                    foreach (char d in tok.TrimStart('-', '+')) v = v * radix + Convert.ToInt32(d.ToString(), 16);
                    return Num.Norm(neg ? -v : v);
                }
                catch { throw new LispError("numero non valido: #" + c + tok); }
            }
            if (c == 'f' && pos + 3 < s.Length && s[pos + 2] == 'n' && s[pos + 3] == '(') { pos += 4; return ReadShortLambda(); }
            if (c == 't' || c == 'f')
            {
                pos += 2;
                return c == 't' ? (object)it.T : null;
            }
            throw new LispError("sintassi # non supportata: #" + c);
        }

        static bool IsDelimiter(char c) { return char.IsWhiteSpace(c) || "()[]{}\"';`,".IndexOf(c) >= 0; }

        string Token()
        {
            var sb = new StringBuilder();
            while (pos < s.Length && !IsDelimiter(s[pos]))
            {
                if (s[pos] == '|')
                {
                    pos++;
                    while (pos < s.Length && s[pos] != '|') sb.Append(s[pos++]);
                    if (pos >= s.Length) throw new LispIncomplete();
                    pos++;
                    continue;
                }
                sb.Append(s[pos++]);
            }
            return sb.ToString();
        }

        object ReadAtom()
        {
            string tok = Token();
            if (tok.Length == 0) { pos++; throw new LispError("carattere inatteso: " + s[pos - 1]); }
            object n = ParseNumber(tok);
            if (n != null) return n;
            string low = tok.ToLowerInvariant();
            if (low == "nil") return null;
            return it.Intern(low);
        }

        public static object ParseNumber(string tok)
        {
            var inv = CultureInfo.InvariantCulture;
            string t = tok.EndsWith(".") && tok.Length > 1 && char.IsDigit(tok[tok.Length - 2]) ? tok.Substring(0, tok.Length - 1) : tok;
            bool intLike = t.Length > 0 && t.Select((c, i) => char.IsDigit(c) || (i == 0 && (c == '-' || c == '+') && t.Length > 1)).All(b => b);
            if (intLike)
            {
                long l;
                if (long.TryParse(t, NumberStyles.AllowLeadingSign, inv, out l)) return l;
                BigInteger b;
                if (BigInteger.TryParse(t, NumberStyles.AllowLeadingSign, inv, out b)) return b;
            }
            int slash = tok.IndexOf('/');
            if (slash > 0)
            {
                object a = ParseNumber(tok.Substring(0, slash)), b = ParseNumber(tok.Substring(slash + 1));
                if (a != null && b != null && !(a is double) && !(b is double)) return Num.Div(a, b);
            }
            if (tok.Any(char.IsDigit) && (tok.Contains('.') || tok.Contains('e') || tok.Contains('E') || tok.Contains('d') || tok.Contains('D')))
            {
                string d = tok.Replace('d', 'e').Replace('D', 'e');
                double v;
                if (double.TryParse(d, NumberStyles.Float, inv, out v) && !d.EndsWith("e")) return v;
            }
            return null;
        }
    }

    // ===================================================================================
    // Numeri: long che diventano BigInteger quando non bastano, double
    // ===================================================================================

    static class Num
    {
        public static bool Is(object o) { return o is long || o is double || o is BigInteger; }

        public static object Norm(BigInteger b)
        {
            if (b >= long.MinValue && b <= long.MaxValue) return (long)b;
            return b;
        }

        static BigInteger Big(object o) { return o is long ? new BigInteger((long)o) : (BigInteger)o; }
        public static double Dbl(object o)
        {
            if (o is double) return (double)o;
            if (o is long) return (long)o;
            if (o is BigInteger) return (double)(BigInteger)o;
            throw new LispError("non è un numero: " + Printer.Print(o, true));
        }

        static void Check(object a, object b)
        {
            if (!Is(a)) throw new LispError("non è un numero: " + Printer.Print(a, true));
            if (!Is(b)) throw new LispError("non è un numero: " + Printer.Print(b, true));
        }

        public static object Add(object a, object b)
        {
            Check(a, b);
            if (a is double || b is double) return Dbl(a) + Dbl(b);
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b, r = unchecked(x + y);
                if (((x ^ r) & (y ^ r)) < 0) return Norm(new BigInteger(x) + y);
                return r;
            }
            return Norm(Big(a) + Big(b));
        }

        public static object Sub(object a, object b)
        {
            Check(a, b);
            if (a is double || b is double) return Dbl(a) - Dbl(b);
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b, r = unchecked(x - y);
                if (((x ^ y) & (x ^ r)) < 0) return Norm(new BigInteger(x) - y);
                return r;
            }
            return Norm(Big(a) - Big(b));
        }

        public static object Mul(object a, object b)
        {
            Check(a, b);
            if (a is double || b is double) return Dbl(a) * Dbl(b);
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b;
                try { return checked(x * y); } catch (OverflowException) { return Norm(new BigInteger(x) * y); }
            }
            return Norm(Big(a) * Big(b));
        }

        // Divisione: esatta tra interi se divisibili, altrimenti decimale
        public static object Div(object a, object b)
        {
            Check(a, b);
            if (a is double || b is double)
            {
                double d = Dbl(b);
                if (d == 0) throw new LispError("divisione per zero");
                return Dbl(a) / d;
            }
            var bb = Big(b);
            if (bb.IsZero) throw new LispError("divisione per zero");
            BigInteger rem;
            var q = BigInteger.DivRem(Big(a), bb, out rem);
            if (rem.IsZero) return Norm(q);
            return Dbl(a) / Dbl(b);
        }

        public static int Compare(object a, object b)
        {
            Check(a, b);
            if (a is double || b is double) return Dbl(a).CompareTo(Dbl(b));
            if (a is long && b is long) return ((long)a).CompareTo((long)b);
            return Big(a).CompareTo(Big(b));
        }

        public static bool IsInt(object o) { return o is long || o is BigInteger; }

        public static BigInteger ToBig(object o)
        {
            if (!IsInt(o)) throw new LispError("serve un intero: " + Printer.Print(o, true));
            return Big(o);
        }

        public static int ToInt(object o)
        {
            if (o is long && (long)o >= int.MinValue && (long)o <= int.MaxValue) return (int)(long)o;
            if (o is double) return (int)(double)o;
            throw new LispError("serve un intero piccolo: " + Printer.Print(o, true));
        }

        // floor / ceiling / round / truncate: quoziente intero
        public static object Rounded(object a, object b, string mode)
        {
            Check(a, b);
            if (a is double || b is double)
            {
                double q = Dbl(a) / Dbl(b);
                if (Dbl(b) == 0) throw new LispError("divisione per zero");
                double r = mode == "floor" ? Math.Floor(q) : mode == "ceiling" ? Math.Ceiling(q) : mode == "round" ? Math.Round(q, MidpointRounding.ToEven) : Math.Truncate(q);
                return Norm(new BigInteger(r));
            }
            var x = Big(a); var y = Big(b);
            if (y.IsZero) throw new LispError("divisione per zero");
            BigInteger rem;
            var qt = BigInteger.DivRem(x, y, out rem);   // troncato verso zero
            if (rem.IsZero) return Norm(qt);
            bool neg = (x.Sign < 0) != (y.Sign < 0);
            switch (mode)
            {
                case "floor": return Norm(neg ? qt - 1 : qt);
                case "ceiling": return Norm(neg ? qt : qt + 1);
                case "round":
                    var twice = BigInteger.Abs(rem) * 2;
                    int cmp = twice.CompareTo(BigInteger.Abs(y));
                    if (cmp > 0 || (cmp == 0 && !qt.IsEven)) return Norm(neg ? qt - 1 : qt + 1);
                    return Norm(qt);
                default: return Norm(qt);
            }
        }
    }

    // ===================================================================================
    // Stampa
    // ===================================================================================

    static class Printer
    {
        public static string Print(object o, bool readably)
        {
            var sb = new StringBuilder();
            Write(sb, o, readably, 0);
            return sb.ToString();
        }

        public static string Float(double d)
        {
            if (double.IsPositiveInfinity(d)) return "+inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            if (double.IsNaN(d)) return "nan";
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            int e = r.IndexOf('E');
            if (e >= 0)
            {
                string mant = r.Substring(0, e), exp = r.Substring(e + 1).TrimStart('+');
                if (!mant.Contains('.')) mant += ".0";
                return mant + "e" + exp;
            }
            return r.Contains('.') ? r : r + ".0";
        }

        static void Write(StringBuilder sb, object o, bool readably, int depth)
        {
            if (sb.Length > 200000) { sb.Append("..."); return; }
            if (o == null) { sb.Append("nil"); return; }
            var sym = o as Sym;
            if (sym != null) { sb.Append(sym.Name); return; }
            if (o is string)
            {
                if (!readably) { sb.Append((string)o); return; }
                sb.Append('"');
                foreach (char c in (string)o) { if (c == '"' || c == '\\') sb.Append('\\'); sb.Append(c); }
                sb.Append('"');
                return;
            }
            if (o is char)
            {
                char c = (char)o;
                if (!readably) { sb.Append(c); return; }
                sb.Append("#\\").Append(c == ' ' ? "Space" : c == '\n' ? "Newline" : c == '\t' ? "Tab" : c == '\r' ? "Return" : c.ToString());
                return;
            }
            if (o is long || o is BigInteger) { sb.Append(o.ToString()); return; }
            if (o is double) { sb.Append(Float((double)o)); return; }
            if (o is bool) { sb.Append((bool)o ? "t" : "nil"); return; }
            var c0 = o as Cons;
            if (c0 != null)
            {
                if (depth > 200) { sb.Append("(...)"); return; }
                var hs = c0.Car as Sym;
                if (hs != null && c0.Cdr is Cons && ((Cons)c0.Cdr).Cdr == null)
                {
                    string pre = hs.Name == "quote" ? "'" : hs.Name == "function" ? "#'" : hs.Name == "quasiquote" ? "`" : hs.Name == "unquote" ? "," : hs.Name == "unquote-splicing" ? ",@" : null;
                    if (pre != null) { sb.Append(pre); Write(sb, ((Cons)c0.Cdr).Car, readably, depth + 1); return; }
                }
                sb.Append('(');
                object x = c0;
                int count = 0;
                while (x is Cons)
                {
                    if (count > 0) sb.Append(' ');
                    if (++count > 100000) { sb.Append("..."); break; }
                    Write(sb, ((Cons)x).Car, readably, depth + 1);
                    x = ((Cons)x).Cdr;
                }
                if (x != null && !(x is Cons)) { sb.Append(" . "); Write(sb, x, readably, depth + 1); }
                sb.Append(')');
                return;
            }
            var arr = o as object[];
            if (arr != null)
            {
                sb.Append("#(");
                for (int i = 0; i < arr.Length; i++) { if (i > 0) sb.Append(' '); Write(sb, arr[i], readably, depth + 1); }
                sb.Append(')');
                return;
            }
            var st = o as LispStruct;
            if (st != null)
            {
                sb.Append("#S(").Append(st.Type.Name);
                for (int i = 0; i < st.Slots.Length; i++) { sb.Append(" :").Append(st.Type.Slots[i].Name).Append(' '); Write(sb, st.Slots[i], readably, depth + 1); }
                sb.Append(')');
                return;
            }
            var fn = o as LispFn;
            if (fn != null) { sb.Append("#<function ").Append(fn.Name).Append('>'); return; }
            var mac = o as Macro;
            if (mac != null) { sb.Append("#<macro ").Append(mac.Name).Append('>'); return; }
            var ht = o as Dictionary<object, object>;
            if (ht != null) { sb.Append("#<hash-table ").Append(ht.Count).Append('>'); return; }
            var cond = o as LispCondition;
            if (cond != null) { sb.Append(readably ? "#<error \"" + cond.Message + "\">" : cond.Message); return; }
            sb.Append(o.ToString());
        }
    }

    // Uguaglianza "equal" (anche per le chiavi delle tabelle hash)
    sealed class EqualComparer : IEqualityComparer<object>
    {
        public static readonly EqualComparer Instance = new EqualComparer();
        public new bool Equals(object a, object b) { return Interp.IsEqual(a, b); }
        public int GetHashCode(object o)
        {
            if (o == null) return 0;
            if (o is long || o is BigInteger || o is double) return Num.Dbl(o).GetHashCode();
            var c = o as Cons;
            if (c != null)
            {
                int h = 17, n = 0;
                object x = c;
                while (x is Cons && n++ < 8) { h = h * 31 + GetHashCode(((Cons)x).Car); x = ((Cons)x).Cdr; }
                return h;
            }
            return o.GetHashCode();
        }
    }

    // ===================================================================================
    // Interprete
    // ===================================================================================

    partial class Interp
    {
        const int MaxDepth = 20000;
        readonly Dictionary<string, Sym> symbols = new Dictionary<string, Sym>();
        public readonly Sym T;
        readonly DynStack dyn = new DynStack();
        public readonly Env Global;
        public Action<string> Out = delegate { };
        public Func<string> ReadLineHook;      // (read-line) dalla tastiera
        public volatile bool Interrupt;
        public string Cwd;
        public Action<string> CwdChanged;
        public bool AtLineStart = true;
        int depth;
        long steps;
        int gensym;
        readonly Random random = new Random();
        readonly Dictionary<Sym, KeyValuePair<StructType, int>> accessors = new Dictionary<Sym, KeyValuePair<StructType, int>>();

        public Interp()
        {
            Global = new Env(dyn);
            T = Intern("t");
            T.Value = T; T.Constant = true;
            Cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Builtins();
            MoreBuiltins();
            DataBuiltins();
            Shell();
            Prelude();
        }

        public Sym Intern(string name)
        {
            Sym s;
            lock (symbols)
            {
                if (!symbols.TryGetValue(name, out s))
                {
                    s = new Sym(name);
                    if (s.IsKeyword) { s.Value = s; s.Constant = true; }
                    symbols[name] = s;
                }
            }
            return s;
        }

        public IEnumerable<Sym> AllSymbols() { lock (symbols) return symbols.Values.ToList(); }

        public void Write(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            Out(s);
            AtLineStart = s[s.Length - 1] == '\n';
        }

        public void FreshLine() { if (!AtLineStart) Write("\n"); }

        // ---------- utilità per le liste ----------

        public static object ToList(IList<object> items, object tail = null)
        {
            object r = tail;
            for (int i = items.Count - 1; i >= 0; i--) r = new Cons(items[i], r);
            return r;
        }

        public static object ToList(IEnumerable<object> items) { return ToList(items.ToList(), null); }

        public static List<object> ListItems(object l)
        {
            var r = new List<object>();
            while (l != null)
            {
                var c = l as Cons;
                if (c == null) throw new LispError("non è una lista: " + Printer.Print(l, true));
                r.Add(c.Car);
                l = c.Cdr;
            }
            return r;
        }

        public static object[] ListToArray(object l) { return ListItems(l).ToArray(); }

        // Elementi di una sequenza: lista, vettore o stringa
        public static List<object> Seq(object o)
        {
            if (o == null || o is Cons) return ListItems(o);
            var a = o as object[];
            if (a != null) return a.ToList();
            var s = o as string;
            if (s != null) return s.Select(c => (object)c).ToList();
            throw new LispError("non è una sequenza: " + Printer.Print(o, true));
        }

        // Rimette gli elementi nello stesso tipo di sequenza di "like"
        public static object SameSeq(object like, List<object> items)
        {
            if (like is string) return new string(items.Select(c => c is char ? (char)c : '?').ToArray());
            if (like is object[]) return items.ToArray();
            return ToList(items, null);
        }

        static object Nth(Cons c, int n)
        {
            object x = c;
            for (int i = 0; i < n; i++) { var k = x as Cons; if (k == null) return null; x = k.Cdr; }
            var cc = x as Cons;
            return cc == null ? null : cc.Car;
        }

        static object NthCdr(object x, int n)
        {
            for (int i = 0; i < n && x != null; i++) { var k = x as Cons; if (k == null) return null; x = k.Cdr; }
            return x;
        }

        public static bool Truthy(object o) { return o != null; }

        public static bool IsEql(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a is long && b is long) return (long)a == (long)b;
            if (a is double && b is double) return (double)a == (double)b;
            if (a is BigInteger && b is BigInteger) return (BigInteger)a == (BigInteger)b;
            if (a is char && b is char) return (char)a == (char)b;
            return false;
        }

        public static bool IsEqual(object a, object b)
        {
            while (true)
            {
                if (IsEql(a, b)) return true;
                if (a is string && b is string) return (string)a == (string)b;
                var ca = a as Cons; var cb = b as Cons;
                if (ca != null && cb != null)
                {
                    if (!IsEqual(ca.Car, cb.Car)) return false;
                    a = ca.Cdr; b = cb.Cdr;
                    continue;
                }
                var va = a as object[]; var vb = b as object[];
                if (va != null && vb != null) return va.Length == vb.Length && va.Zip(vb, IsEqual).All(x => x);
                if (Num.Is(a) && Num.Is(b) && (a is double) == (b is double)) return Num.Compare(a, b) == 0;
                return false;
            }
        }

        // ---------- valutazione ----------

        public object EvalString(string text)
        {
            var r = new Reader(this, text);
            object result = null;
            while (true)
            {
                var form = r.Read();
                if (form == Reader.Eof) return result;
                result = Eval(form, Global);
            }
        }

        public object Eval(object x, Env env)
        {
            if (++depth > MaxDepth) { depth = 0; throw new LispError("troppa ricorsione (più di " + MaxDepth + " chiamate annidate)"); }
            int mark = dyn.Count;
            try { return EvalLoop(x, env); }
            finally { if (depth > 0) depth--; if (dyn.Count > mark) dyn.Unwind(mark); }
        }

        public void ResetDepth() { depth = 0; }

        object Lookup(Sym s, Env env)
        {
            object v;
            if (env.TryGet(s, out v)) return v;
            if (s.Value != Sym.Unbound) return s.Value;
            throw new LispError("variabile non definita: " + s.Name);
        }

        void SetVar(Sym s, object v, Env env)
        {
            if (s.Constant) throw new LispError("non si può cambiare la costante " + s.Name);
            if (!env.TrySet(s, v)) s.Value = v;
        }

        LispFn FindFunction(Sym s, Env env)
        {
            var f = env.FindFun(s);
            if (f != null) return f;
            f = s.Function as LispFn;
            if (f != null) return f;
            // indulgenza "alla Scheme": una variabile che contiene una funzione si può chiamare direttamente
            object v;
            if (env.TryGet(s, out v) && v is LispFn) return (LispFn)v;
            if (s.Value is LispFn) return (LispFn)s.Value;
            // (:name riga) legge il campo :name di una plist, tabella hash o struttura
            if (s.IsKeyword) return KeywordFn(s);
            return null;
        }

        static bool IsDeclare(object form)
        {
            var c = form as Cons;
            return c != null && c.Car is Sym && ((Sym)c.Car).Name == "declare";
        }

        // Valuta tutte le forme del corpo tranne l'ultima e restituisce l'ultima (da valutare in coda)
        object BodyTail(object body, Env env)
        {
            var b = body as Cons;
            while (b != null && IsDeclare(b.Car)) b = b.Cdr as Cons;
            if (b == null) return null;
            while (b.Cdr != null)
            {
                Eval(b.Car, env);
                b = b.Cdr as Cons;
                if (b == null) return null;
            }
            return b.Car;
        }

        public object Progn(object body, Env env) { return Eval(BodyTail(body, env), env); }

        object EvalLoop(object x, Env env)
        {
            while (true)
            {
                if ((++steps & 0x3FF) == 0 && Interrupt) throw new LispInterrupted();
                var sym = x as Sym;
                if (sym != null) return sym.IsKeyword ? sym : Lookup(sym, env);
                var c = x as Cons;
                if (c == null) return x;
                var head = c.Car as Sym;
                if (head != null)
                {
                    switch (head.Name)
                    {
                        case "quote": return Nth(c, 1);
                        case "if":
                            x = Truthy(Eval(Nth(c, 1), env)) ? Nth(c, 2) : Nth(c, 3);
                            continue;
                        case "progn": x = BodyTail(c.Cdr, env); continue;
                        case "locally": x = BodyTail(c.Cdr, env); continue;
                        case "let": case "let*":
                            {
                                var ne = new Env(env);
                                bool parallel = head.Name == "let";
                                var bindings = ListItems(Nth(c, 1));
                                // let: prima tutti i valori, poi i legami (una variabile speciale cambia subito il simbolo)
                                var vals = parallel ? new object[bindings.Count] : null;
                                for (int pass = parallel ? 0 : 1; pass < 2; pass++)
                                    for (int i = 0; i < bindings.Count; i++)
                                    {
                                        var bc = bindings[i] as Cons;
                                        Sym name = bc != null ? (Sym)bc.Car : (Sym)bindings[i];
                                        if (pass == 0) { vals[i] = bc != null ? Eval(Nth(bc, 1), env) : null; continue; }
                                        ne.Define(name, parallel ? vals[i] : bc != null ? Eval(Nth(bc, 1), ne) : null);
                                    }
                                env = ne;
                                x = BodyTail(NthCdr(c, 2), env);
                                continue;
                            }
                        case "flet": case "labels":
                            {
                                var ne = new Env(env) { Funs = new Dictionary<Sym, LispFn>() };
                                foreach (var d in ListItems(Nth(c, 1)))
                                {
                                    var dc = (Cons)d;
                                    var name = (Sym)dc.Car;
                                    var f = MakeClosure(Nth(dc, 1), NthCdr(dc, 2), head.Name == "labels" ? ne : env, name);
                                    ne.Funs[name] = f;
                                }
                                env = ne;
                                x = BodyTail(NthCdr(c, 2), env);
                                continue;
                            }
                        case "cond":
                            {
                                object next = null; bool found = false;
                                foreach (var cl in ListItems(c.Cdr))
                                {
                                    var clc = (Cons)cl;
                                    var test = Eval(clc.Car, env);
                                    if (!Truthy(test)) continue;
                                    found = true;
                                    if (clc.Cdr == null) return test;
                                    next = BodyTail(clc.Cdr, env);
                                    break;
                                }
                                if (!found) return null;
                                x = next;
                                continue;
                            }
                        case "case": case "ecase":
                            {
                                var key = Eval(Nth(c, 1), env);
                                object next = null; bool found = false;
                                foreach (var cl in ListItems(NthCdr(c, 2)))
                                {
                                    var clc = (Cons)cl;
                                    var keys = clc.Car;
                                    bool match;
                                    var ks = keys as Sym;
                                    if (ks == T || (ks != null && ks.Name == "otherwise")) match = true;
                                    else if (keys is Cons) match = ListItems(keys).Any(k => IsEql(k, key));
                                    else match = IsEql(keys, key);
                                    if (!match) continue;
                                    found = true;
                                    next = BodyTail(clc.Cdr, env);
                                    break;
                                }
                                if (!found)
                                {
                                    if (head.Name == "ecase") throw new LispError("ecase: nessun caso per " + Printer.Print(key, true));
                                    return null;
                                }
                                x = next;
                                continue;
                            }
                        case "and":
                            {
                                if (c.Cdr == null) return T;
                                var b = (Cons)c.Cdr;
                                bool stop = false;
                                while (b.Cdr != null)
                                {
                                    if (!Truthy(Eval(b.Car, env))) { stop = true; break; }
                                    b = (Cons)b.Cdr;
                                }
                                if (stop) return null;
                                x = b.Car;
                                continue;
                            }
                        case "or":
                            {
                                if (c.Cdr == null) return null;
                                var b = (Cons)c.Cdr;
                                while (b.Cdr != null)
                                {
                                    var v = Eval(b.Car, env);
                                    if (Truthy(v)) return v;
                                    b = (Cons)b.Cdr;
                                }
                                x = b.Car;
                                continue;
                            }
                        case "when": case "unless":
                            {
                                bool t = Truthy(Eval(Nth(c, 1), env));
                                if (t != (head.Name == "when")) return null;
                                x = BodyTail(NthCdr(c, 2), env);
                                continue;
                            }
                        case "lambda": return MakeClosure(Nth(c, 1), NthCdr(c, 2), env, null);
                        case "function":
                            {
                                var a = Nth(c, 1);
                                var asym = a as Sym;
                                if (asym != null)
                                {
                                    var f = FindFunction(asym, env);
                                    if (f == null) throw new LispError("funzione non definita: " + asym.Name);
                                    return f;
                                }
                                return Eval(a, env);
                            }
                        case "psetq": case "psetf":
                            {
                                // prima tutti i valori, poi gli assegnamenti: (psetq a b b a) scambia
                                var places = new List<object>(); var newVals = new List<object>();
                                for (var b = c.Cdr as Cons; b != null; b = ((Cons)b.Cdr).Cdr as Cons)
                                {
                                    if (!(b.Cdr is Cons)) throw new LispError(head.Name + ": manca il valore");
                                    places.Add(b.Car);
                                    newVals.Add(Eval(((Cons)b.Cdr).Car, env));
                                }
                                for (int i = 0; i < places.Count; i++) SetPlace(places[i], newVals[i], env);
                                return null;
                            }
                        case "setq": case "setf":
                            {
                                object v = null;
                                var b = c.Cdr as Cons;
                                while (b != null)
                                {
                                    var place = b.Car;
                                    var vc = b.Cdr as Cons;
                                    if (vc == null) throw new LispError(head.Name + ": manca il valore");
                                    v = Eval(vc.Car, env);
                                    SetPlace(place, v, env);
                                    b = vc.Cdr as Cons;
                                }
                                return v;
                            }
                        case "incf": case "decf":
                            {
                                var place = Nth(c, 1);
                                var delta = c.Cdr is Cons && ((Cons)c.Cdr).Cdr != null ? Eval(Nth(c, 2), env) : 1L;
                                var cur = Eval(place, env);
                                var v = head.Name == "incf" ? Num.Add(cur, delta) : Num.Sub(cur, delta);
                                SetPlace(place, v, env);
                                return v;
                            }
                        case "push":
                            {
                                var item = Eval(Nth(c, 1), env);
                                var v = new Cons(item, Eval(Nth(c, 2), env));
                                SetPlace(Nth(c, 2), v, env);
                                return v;
                            }
                        case "pushnew":
                            {
                                var item = Eval(Nth(c, 1), env);
                                var lst = Eval(Nth(c, 2), env);
                                if (ListItems(lst).Any(e => IsEqual(e, item))) return lst;
                                var v = new Cons(item, lst);
                                SetPlace(Nth(c, 2), v, env);
                                return v;
                            }
                        case "pop":
                            {
                                var lst = Eval(Nth(c, 1), env) as Cons;
                                if (lst == null) return null;
                                SetPlace(Nth(c, 1), lst.Cdr, env);
                                return lst.Car;
                            }
                        case "defun":
                            {
                                var name = (Sym)Nth(c, 1);
                                var body = NthCdr(c, 3);
                                var bc = body as Cons;
                                if (bc != null && bc.Car is string && bc.Cdr != null) body = bc.Cdr;   // stringa di documentazione
                                var f = MakeClosure(Nth(c, 2), body, env, name);
                                name.Function = f;
                                return name;
                            }
                        case "defmacro":
                            {
                                var name = (Sym)Nth(c, 1);
                                var f = MakeClosure(Nth(c, 2), NthCdr(c, 3), env, name);
                                name.Function = new Macro { Name = name.Name, Fn = f };
                                return name;
                            }
                        case "defvar": case "defparameter": case "defconstant":
                            {
                                var name = (Sym)Nth(c, 1);
                                if (head.Name != "defconstant") name.Special = true;
                                if (head.Name != "defvar" || name.Value == Sym.Unbound)
                                    if (c.Cdr is Cons && ((Cons)c.Cdr).Cdr != null) { name.Constant = false; name.Value = Eval(Nth(c, 2), env); }
                                    else if (name.Value == Sym.Unbound) name.Value = null;
                                if (head.Name == "defconstant") name.Constant = true;
                                return name;
                            }
                        case "defstruct": return DefStruct(c, env);
                        case "block":
                            {
                                var name = Nth(c, 1) as Sym;
                                try { return Progn(NthCdr(c, 2), env); }
                                catch (ReturnFrom rf) { if (rf.Name == name) return rf.Value; throw; }
                            }
                        case "return-from": throw new ReturnFrom(Nth(c, 1) as Sym, Eval(Nth(c, 2), env));
                        case "return": throw new ReturnFrom(null, Eval(Nth(c, 1), env));
                        case "catch":
                            {
                                var tag = Eval(Nth(c, 1), env);
                                try { return Progn(NthCdr(c, 2), env); }
                                catch (ThrowTag tt) { if (IsEql(tt.Tag, tag)) return tt.Value; throw; }
                            }
                        case "throw": throw new ThrowTag(Eval(Nth(c, 1), env), Eval(Nth(c, 2), env));
                        case "unwind-protect":
                            try { return Eval(Nth(c, 1), env); }
                            finally { Progn(NthCdr(c, 2), env); }
                        case "ignore-errors":
                            try { return Progn(c.Cdr, env); }
                            catch (Exception ex) { if (IsControl(ex)) throw; return null; }
                        case "handler-case": return HandlerCase(c, env);
                        case "quasiquote": return Quasi(Nth(c, 1), env);
                        case "dolist": return DoList(c, env);
                        case "dotimes": return DoTimes(c, env);
                        case "do": case "do*": return Do(c, env, head.Name == "do*");
                        case "loop": return Loop(c, env);
                        case "prog1":
                            {
                                var v = Eval(Nth(c, 1), env);
                                Progn(NthCdr(c, 2), env);
                                return v;
                            }
                        case "time":
                            {
                                var sw = Stopwatch.StartNew();
                                var v = Eval(Nth(c, 1), env);
                                FreshLine();
                                Write(string.Format(CultureInfo.InvariantCulture, "; tempo: {0:0.###} s\n", sw.Elapsed.TotalSeconds));
                                return v;
                            }
                        case "multiple-value-bind":
                            {
                                var ne = new Env(env);
                                var vars = ListItems(Nth(c, 1));
                                var v = Eval(Nth(c, 2), env);
                                for (int i = 0; i < vars.Count; i++) ne.Define((Sym)vars[i], i == 0 ? v : null);
                                env = ne;
                                x = BodyTail(NthCdr(c, 3), env);
                                continue;
                            }
                        case "destructuring-bind":
                            {
                                var f = MakeClosure(Nth(c, 1), null, env, null);
                                var ne = Bind(f, ListToArray(Eval(Nth(c, 2), env)));
                                env = ne;
                                x = BodyTail(NthCdr(c, 3), env);
                                continue;
                            }
                        case "the": x = Nth(c, 2); continue;
                        case "declaim": case "proclaim":
                            // (declaim (special a b)) / (proclaim '(special a b)): le altre dichiarazioni si ignorano
                            foreach (var d in ListItems(c.Cdr))
                            {
                                var dc = d as Cons;
                                if (dc != null && dc.Car is Sym && ((Sym)dc.Car).Name == "quote") dc = Nth(dc, 1) as Cons;
                                if (dc != null && dc.Car is Sym && ((Sym)dc.Car).Name == "special")
                                    foreach (var s in ListItems(dc.Cdr)) if (s is Sym) ((Sym)s).Special = true;
                            }
                            return null;
                        case "progv":
                            {
                                // (progv simboli valori corpo...): legami dinamici scelti a tempo di esecuzione
                                var syms = ListItems(Eval(Nth(c, 1), env));
                                var vals = ListItems(Eval(Nth(c, 2), env));
                                for (int i = 0; i < syms.Count; i++)
                                {
                                    var s = syms[i] as Sym;
                                    if (s == null) throw new LispError("progv: " + Printer.Print(syms[i], true) + " non è un simbolo");
                                    dyn.Push(s, i < vals.Count ? vals[i] : Sym.Unbound);
                                }
                                x = BodyTail(NthCdr(c, 3), env);
                                continue;
                            }
                        case "declare": case "in-package": case "defpackage": return null;
                        case "eval-when": x = BodyTail(NthCdr(c, 2), env); continue;
                    }
                }

                // ---- chiamata di funzione o macro ----
                LispFn fn = null;
                if (head != null)
                {
                    var mac = env.FindFun(head) == null ? head.Function as Macro : null;
                    if (mac != null) { x = Apply(mac.Fn, ListToArray(c.Cdr)); continue; }
                    fn = FindFunction(head, env);
                    if (fn == null) throw new LispError("funzione non definita: " + head.Name);
                }
                else
                {
                    fn = Eval(c.Car, env) as LispFn;
                    if (fn == null) throw new LispError("non è una funzione: " + Printer.Print(c.Car, true));
                }
                var args = new List<object>();
                for (object a = c.Cdr; a != null; a = ((Cons)a).Cdr)
                {
                    if (!(a is Cons)) throw new LispError("lista di argomenti non valida");
                    args.Add(Eval(((Cons)a).Car, env));
                }
                var clo = fn as Closure;
                if (clo != null && !clo.NeedsBlock)
                {
                    env = Bind(clo, args.ToArray());
                    x = BodyTail(clo.Body, env);
                    continue;
                }
                return Apply(fn, args.ToArray());
            }
        }

        public static bool IsControl(Exception ex)
        {
            return ex is ReturnFrom || ex is ThrowTag || ex is LispInterrupted || ex is LispExit || ex is LoopEnd;
        }

        public object Apply(LispFn fn, object[] args)
        {
            var b = fn as Builtin;
            if (b != null)
            {
                if (args.Length < b.Min || (b.Max >= 0 && args.Length > b.Max))
                    throw new LispError(string.Format("{0}: numero di argomenti sbagliato ({1})", b.Name, args.Length));
                return b.F(args);
            }
            var cl = (Closure)fn;
            if (++depth > MaxDepth) { depth = 0; throw new LispError("troppa ricorsione (più di " + MaxDepth + " chiamate annidate)"); }
            int mark = dyn.Count;
            try
            {
                var env = Bind(cl, args);
                if (!cl.NeedsBlock) return Progn(cl.Body, env);
                try { return Progn(cl.Body, env); }
                catch (ReturnFrom rf) { if (rf.Name == cl.Block) return rf.Value; throw; }
            }
            finally { if (depth > 0) depth--; if (dyn.Count > mark) dyn.Unwind(mark); }
        }

        public object Call(object f, params object[] args)
        {
            var fn = f as LispFn;
            if (fn == null)
            {
                var s = f as Sym;
                if (s != null) fn = FindFunction(s, Global);
                if (fn == null) throw new LispError("non è una funzione: " + Printer.Print(f, true));
            }
            return Apply(fn, args);
        }

        // ---------- funzioni utente ----------

        Closure MakeClosure(object lambdaList, object body, Env env, Sym name)
        {
            var c = new Closure { Body = body, Env = env, Block = name, Name = name != null ? name.Name : "lambda" };
            var req = new List<Sym>(); var opt = new List<Sym>(); var optDef = new List<object>();
            var keys = new List<Sym>(); var keyDef = new List<object>(); var aux = new List<Sym>(); var auxDef = new List<object>();
            string mode = "req";
            object l = lambdaList;
            while (l != null)
            {
                if (l is Sym) { c.Rest = (Sym)l; break; }   // (a b . rest)
                var lc = (Cons)l;
                var p = lc.Car;
                var ps = p as Sym;
                if (ps != null && ps.Name.StartsWith("&"))
                {
                    switch (ps.Name)
                    {
                        case "&optional": mode = "opt"; break;
                        case "&rest": case "&body": mode = "rest"; break;
                        case "&key": mode = "key"; break;
                        case "&aux": mode = "aux"; break;
                        case "&allow-other-keys": break;
                        default: throw new LispError("parametro non supportato: " + ps.Name);
                    }
                }
                else
                {
                    var pc = p as Cons;
                    Sym pn = pc != null ? (Sym)pc.Car : (Sym)p;
                    object def = pc != null ? Nth(pc, 1) : null;
                    switch (mode)
                    {
                        case "req": req.Add(pn); break;
                        case "opt": opt.Add(pn); optDef.Add(def); break;
                        case "rest": c.Rest = pn; break;
                        case "key": keys.Add(pn); keyDef.Add(def); break;
                        case "aux": aux.Add(pn); auxDef.Add(def); break;
                    }
                }
                l = lc.Cdr;
            }
            c.Req = req.ToArray(); c.Opt = opt.ToArray(); c.OptDef = optDef.ToArray();
            c.Keys = keys.ToArray(); c.KeyDef = keyDef.ToArray(); c.Aux = aux.ToArray(); c.AuxDef = auxDef.ToArray();
            c.NeedsBlock = name != null && Mentions(body, "return-from", 0);
            return c;
        }

        static bool Mentions(object form, string name, int depth)
        {
            if (depth > 500) return true;
            var s = form as Sym;
            if (s != null) return s.Name == name;
            var c = form as Cons;
            while (c != null)
            {
                if (Mentions(c.Car, name, depth + 1)) return true;
                if (c.Cdr is Sym) return ((Sym)c.Cdr).Name == name;
                c = c.Cdr as Cons;
            }
            return false;
        }

        Env Bind(Closure c, object[] args)
        {
            var env = new Env(c.Env);
            int i = 0;
            if (args.Length < c.Req.Length)
                throw new LispError(string.Format("{0}: servono almeno {1} argomenti, ne sono arrivati {2}", c.Name, c.Req.Length, args.Length));
            foreach (var p in c.Req) env.Define(p, args[i++]);
            for (int k = 0; k < c.Opt.Length; k++)
                env.Define(c.Opt[k], i < args.Length ? args[i++] : Eval(c.OptDef[k], env));
            if (c.Rest != null)
            {
                object rest = null;
                for (int k = args.Length - 1; k >= i; k--) rest = new Cons(args[k], rest);
                env.Define(c.Rest, rest);
            }
            if (c.Keys.Length > 0)
            {
                for (int k = 0; k < c.Keys.Length; k++)
                {
                    string kn = ":" + c.Keys[k].Name;
                    object v = Sym.Unbound;
                    for (int j = i; j + 1 < args.Length; j += 2)
                        if (args[j] is Sym && ((Sym)args[j]).Name == kn) { v = args[j + 1]; break; }
                    env.Define(c.Keys[k], v != Sym.Unbound ? v : Eval(c.KeyDef[k], env));
                }
            }
            else if (c.Rest == null && i < args.Length)
                throw new LispError(string.Format("{0}: troppi argomenti ({1})", c.Name, args.Length));
            for (int k = 0; k < c.Aux.Length; k++) env.Define(c.Aux[k], Eval(c.AuxDef[k], env));
            return env;
        }

        // ---------- setf ----------

        void SetPlace(object place, object v, Env env)
        {
            var s = place as Sym;
            if (s != null) { SetVar(s, v, env); return; }
            var c = place as Cons;
            var h = c != null ? c.Car as Sym : null;
            if (h == null) throw new LispError("setf: posto non valido: " + Printer.Print(place, true));
            switch (h.Name)
            {
                case "car": case "first": AsCons(Eval(Nth(c, 1), env)).Car = v; return;
                case "cdr": case "rest": AsCons(Eval(Nth(c, 1), env)).Cdr = v; return;
                case "second": AsCons(NthCdr(Eval(Nth(c, 1), env), 1)).Car = v; return;
                case "third": AsCons(NthCdr(Eval(Nth(c, 1), env), 2)).Car = v; return;
                case "nth": AsCons(NthCdr(Eval(Nth(c, 2), env), Num.ToInt(Eval(Nth(c, 1), env)))).Car = v; return;
                case "gethash":
                    {
                        var key = Eval(Nth(c, 1), env);
                        var ht = Eval(Nth(c, 2), env) as Dictionary<object, object>;
                        if (ht == null) throw new LispError("gethash: non è una tabella hash");
                        ht[key ?? NilKey] = v;
                        return;
                    }
                case "aref": case "elt": case "svref":
                    {
                        var seq = Eval(Nth(c, 1), env);
                        int i = Num.ToInt(Eval(Nth(c, 2), env));
                        var arr = seq as object[];
                        if (arr != null) { arr[i] = v; return; }
                        if (seq is Cons) { AsCons(NthCdr(seq, i)).Car = v; return; }
                        throw new LispError("setf aref: le stringhe non si modificano, usa un vettore");
                    }
                case "fourth": case "fifth": case "sixth": case "seventh": case "eighth": case "ninth": case "tenth":
                    {
                        int n = Array.IndexOf(new[] { "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth" }, h.Name) + 3;
                        AsCons(NthCdr(Eval(Nth(c, 1), env), n)).Car = v;
                        return;
                    }
                case "symbol-value": SetVar((Sym)Eval(Nth(c, 1), env), v, env); return;
                case "getf":
                    throw new LispError("setf getf non è supportato");
            }
            // cadr, cddr, caddr...: le lettere interne portano al cons, la prima dice se cambiare car o cdr
            if (h.Name.Length >= 4 && h.Name.Length <= 6 && h.Name[0] == 'c' && h.Name[h.Name.Length - 1] == 'r' &&
                h.Name.Substring(1, h.Name.Length - 2).All(ch => ch == 'a' || ch == 'd'))
            {
                string ops = h.Name.Substring(1, h.Name.Length - 2);
                object x = Eval(Nth(c, 1), env);
                for (int i = ops.Length - 1; i >= 1; i--) x = ops[i] == 'a' ? AsCons(x).Car : AsCons(x).Cdr;
                if (ops[0] == 'a') AsCons(x).Car = v; else AsCons(x).Cdr = v;
                return;
            }
            KeyValuePair<StructType, int> acc;
            if (accessors.TryGetValue(h, out acc))
            {
                var st = Eval(Nth(c, 1), env) as LispStruct;
                if (st == null || st.Type != acc.Key) throw new LispError(h.Name + ": non è una struttura " + acc.Key.Name);
                st.Slots[acc.Value] = v;
                return;
            }
            // macro: si espande e si riprova
            var mac = h.Function as Macro;
            if (mac != null) { SetPlace(Apply(mac.Fn, ListToArray(c.Cdr)), v, env); return; }
            throw new LispError("setf: posto non supportato: " + h.Name);
        }

        public static readonly object NilKey = new object();   // nil come chiave di una tabella hash

        static Cons AsCons(object o)
        {
            var c = o as Cons;
            if (c == null) throw new LispError("non è un cons: " + Printer.Print(o, true));
            return c;
        }

        // ---------- forme speciali più lunghe ----------

        object Quasi(object x, Env env)
        {
            var c = x as Cons;
            if (c == null)
            {
                var vec = x as object[];
                if (vec != null) return ListToArray(Quasi(ToList(vec.ToList(), null), env));
                return x;
            }
            var hs = c.Car as Sym;
            if (hs != null && hs.Name == "unquote") return Eval(Nth(c, 1), env);
            if (hs != null && hs.Name == "quasiquote") return x;   // annidate: non supportate, restano com'è
            var items = new List<object>();
            object tail = null;
            object cur = c;
            while (cur != null)
            {
                var cc = cur as Cons;
                if (cc == null) { tail = Quasi(cur, env); break; }
                var cs = cc.Car as Sym;
                if (cs != null && cs.Name == "unquote" && cc != c) { tail = Eval(Nth(cc, 1), env); break; }   // `(a . ,b)
                var item = cc.Car as Cons;
                if (item != null && item.Car is Sym && ((Sym)item.Car).Name == "unquote-splicing")
                    items.AddRange(ListItems(Eval(Nth(item, 1), env)));
                else items.Add(Quasi(cc.Car, env));
                cur = cc.Cdr;
            }
            return ToList(items, tail);
        }

        object HandlerCase(Cons c, Env env)
        {
            try { return Eval(Nth(c, 1), env); }
            catch (Exception ex)
            {
                if (IsControl(ex)) throw;
                string msg = ex is LispError ? ex.Message : ex is StackOverflowException ? "stack esaurito" : ex.Message;
                foreach (var cl in ListItems(NthCdr(c, 2)))
                {
                    var clc = (Cons)cl;
                    var vars = Nth(clc, 1);
                    var ne = new Env(env);
                    var vl = vars as Cons;
                    if (vl != null && vl.Car is Sym) ne.Define((Sym)vl.Car, new LispCondition { Message = msg });
                    return Progn(NthCdr(clc, 2), ne);
                }
                throw;
            }
        }

        object DoList(Cons c, Env env)
        {
            var spec = (Cons)Nth(c, 1);
            var v = (Sym)spec.Car;
            var lst = Eval(Nth(spec, 1), env);
            var body = NthCdr(c, 2);
            try
            {
                var ne = new Env(env);
                ne.Define(v, null);
                foreach (var item in Seq(lst))
                {
                    ne.TrySet(v, item);
                    Progn(body, ne);
                }
                ne.TrySet(v, null);
                return Eval(Nth(spec, 2), ne);
            }
            catch (ReturnFrom rf) { if (rf.Name == null) return rf.Value; throw; }
        }

        object DoTimes(Cons c, Env env)
        {
            var spec = (Cons)Nth(c, 1);
            var v = (Sym)spec.Car;
            var n = Eval(Nth(spec, 1), env);
            var body = NthCdr(c, 2);
            try
            {
                var ne = new Env(env);
                ne.Define(v, 0L);
                for (object i = 0L; Num.Compare(i, n) < 0; i = Num.Add(i, 1L))
                {
                    ne.TrySet(v, i);
                    Progn(body, ne);
                }
                ne.TrySet(v, n);
                return Eval(Nth(spec, 2), ne);
            }
            catch (ReturnFrom rf) { if (rf.Name == null) return rf.Value; throw; }
        }

        object Do(Cons c, Env env, bool sequential)
        {
            var specs = ListItems(Nth(c, 1)).Select(s => s is Cons ? (Cons)s : new Cons(s, null)).ToList();
            var end = (Cons)Nth(c, 2);
            var body = NthCdr(c, 3);
            try
            {
                var ne = new Env(env);
                foreach (var s in specs) ne.Define((Sym)s.Car, Eval(Nth(s, 1), sequential ? ne : env));
                while (true)
                {
                    if (Truthy(Eval(end.Car, ne))) return Progn(end.Cdr, ne);
                    Progn(body, ne);
                    if (sequential)
                    {
                        foreach (var s in specs) if (s.Cdr is Cons && ((Cons)s.Cdr).Cdr != null) ne.TrySet((Sym)s.Car, Eval(Nth(s, 2), ne));
                    }
                    else
                    {
                        var vals = specs.Select(s => s.Cdr is Cons && ((Cons)s.Cdr).Cdr != null ? Eval(Nth(s, 2), ne) : Sym.Unbound).ToList();
                        for (int i = 0; i < specs.Count; i++) if (vals[i] != Sym.Unbound) ne.TrySet((Sym)specs[i].Car, vals[i]);
                    }
                }
            }
            catch (ReturnFrom rf) { if (rf.Name == null) return rf.Value; throw; }
        }

        object DefStruct(Cons c, Env env)
        {
            var spec = Nth(c, 1);
            var name = spec is Cons ? (Sym)((Cons)spec).Car : (Sym)spec;
            var slots = new List<Sym>(); var defs = new List<object>();
            foreach (var s in ListItems(NthCdr(c, 2)))
            {
                if (s is string) continue;   // documentazione
                var sc = s as Cons;
                slots.Add(sc != null ? (Sym)sc.Car : (Sym)s);
                defs.Add(sc != null ? Nth(sc, 1) : null);
            }
            var type = new StructType { Name = name.Name, Slots = slots.ToArray(), Defaults = defs.ToArray(), Env = env };
            Def("make-" + name.Name, 0, -1, a =>
            {
                var st = new LispStruct { Type = type, Slots = new object[type.Slots.Length] };
                for (int i = 0; i < type.Slots.Length; i++)
                {
                    object v = Sym.Unbound;
                    for (int j = 0; j + 1 < a.Length; j += 2)
                        if (a[j] is Sym && ((Sym)a[j]).Name == ":" + type.Slots[i].Name) { v = a[j + 1]; break; }
                    st.Slots[i] = v != Sym.Unbound ? v : Eval(type.Defaults[i], type.Env);
                }
                return st;
            });
            Def(name.Name + "-p", 1, 1, a => { var st = a[0] as LispStruct; return st != null && st.Type == type ? (object)T : null; });
            Def("copy-" + name.Name, 1, 1, a => { var st = (LispStruct)a[0]; return new LispStruct { Type = st.Type, Slots = (object[])st.Slots.Clone() }; });
            for (int i = 0; i < slots.Count; i++)
            {
                int idx = i;
                var acc = Intern(name.Name + "-" + slots[i].Name);
                accessors[acc] = new KeyValuePair<StructType, int>(type, idx);
                Def(acc.Name, 1, 1, a =>
                {
                    var st = a[0] as LispStruct;
                    if (st == null || st.Type != type) throw new LispError(acc.Name + ": non è una struttura " + type.Name);
                    return st.Slots[idx];
                });
            }
            return name;
        }

        // ---------- loop ----------

        sealed class LoopVar { public object Var; public string Kind; public object Seq; public int Index; public object From, To, By; public bool Down, Below; public object Init, Then; public Cons Form; public bool First = true; }

        object Loop(Cons c, Env env)
        {
            var parts = ListItems(c.Cdr);
            // (loop forme...) senza parole chiave: ciclo infinito fino a return
            if (parts.Count == 0 || !(parts[0] is Sym) || !LoopKeyword((Sym)parts[0]))
            {
                try { while (true) Progn(c.Cdr, env); }
                catch (ReturnFrom rf) { if (rf.Name == null) return rf.Value; throw; }
                catch (LoopEnd) { return null; }
            }
            var ne = new Env(env);
            Sym loopName = null;              // loop named x: (return-from x ...)
            string boolKind = null;           // always / never / thereis
            var vars = new List<LoopVar>();
            var steps = new List<Func<bool>>();     // false = il ciclo finisce
            var finallyForms = new List<object>();
            object acc = null; var accList = new List<object>(); string accKind = null;
            Sym accInto = null;
            int pos = 0;
            Func<string> peek = () => pos < parts.Count && parts[pos] is Sym ? ((Sym)parts[pos]).Name : null;
            Func<object> next = () => { if (pos >= parts.Count) throw new LispError("loop: clausola incompleta"); return parts[pos++]; };
            Func<object, object> ev = form => Eval(form, ne);
            object result = null; bool done = false;

            // le azioni del corpo, nell'ordine
            var actions = new List<Action>();
            Action<Action> addAction = a => actions.Add(a);

            Func<Action> parseAction = null;
            parseAction = () =>
            {
                string k = peek();
                if (k == null) throw new LispError("loop: clausola non riconosciuta: " + Printer.Print(parts[pos], true));
                pos++;
                switch (k)
                {
                    case "do": case "doing":
                        {
                            var forms = new List<object>();
                            while (pos < parts.Count && !(parts[pos] is Sym && LoopKeyword((Sym)parts[pos]))) forms.Add(parts[pos++]);
                            var body = ToList(forms, null);
                            return () => Progn(body, ne);
                        }
                    case "collect": case "collecting": case "append": case "appending": case "nconc": case "sum": case "summing":
                    case "count": case "counting": case "maximize": case "maximizing": case "minimize": case "minimizing":
                        {
                            var form = next();
                            string kind = k.StartsWith("collect") ? "collect" : k.StartsWith("append") || k == "nconc" ? "append" : k.StartsWith("sum") ? "sum" :
                                          k.StartsWith("count") ? "count" : k.StartsWith("max") ? "max" : "min";
                            if (peek() == "into") { pos++; accInto = (Sym)next(); ne.Define(accInto, kind == "sum" || kind == "count" ? (object)0L : null); }
                            if (accKind == null) accKind = kind;
                            var into = accInto;
                            return () =>
                            {
                                var v = ev(form);
                                switch (kind)
                                {
                                    case "collect": accList.Add(v); break;
                                    case "append": accList.AddRange(ListItems(v)); break;
                                    case "sum": acc = Num.Add(acc ?? 0L, v); break;
                                    case "count": if (Truthy(v)) acc = Num.Add(acc ?? 0L, 1L); break;
                                    case "max": if (acc == null || Num.Compare(v, acc) > 0) acc = v; break;
                                    case "min": if (acc == null || Num.Compare(v, acc) < 0) acc = v; break;
                                }
                                if (into != null) ne.TrySet(into, kind == "collect" || kind == "append" ? ToList(accList, null) : acc);
                            };
                        }
                    case "when": case "if": case "unless":
                        {
                            var test = next();
                            var then = parseAction();
                            var more = new List<Action> { then };
                            while (peek() == "and") { pos++; more.Add(parseAction()); }
                            Action els = null;
                            if (peek() == "else") { pos++; els = parseAction(); }
                            if (peek() == "end") pos++;
                            bool neg = k == "unless";
                            return () =>
                            {
                                if (Truthy(ev(test)) != neg) foreach (var m in more) m();
                                else if (els != null) els();
                            };
                        }
                    case "return":
                        {
                            var form = next();
                            return () => { result = ev(form); done = true; throw new ReturnFrom(null, result); };
                        }
                    case "always": case "never": case "thereis":
                        {
                            var form = next();
                            if (boolKind == null) boolKind = k;
                            return () =>
                            {
                                var v = ev(form);
                                if (k == "thereis") { if (Truthy(v)) throw new ReturnFrom(null, v); }
                                else if (Truthy(v) == (k == "never")) throw new ReturnFrom(null, null);
                            };
                        }
                    case "while":
                        {
                            var form = next();
                            return () => { if (!Truthy(ev(form))) throw new LoopEnd(); };
                        }
                    case "until":
                        {
                            var form = next();
                            return () => { if (Truthy(ev(form))) throw new LoopEnd(); };
                        }
                }
                throw new LispError("loop: clausola non riconosciuta: " + k);
            };

            while (pos < parts.Count)
            {
                string k = peek();
                if (k == null) throw new LispError("loop: atteso una parola chiave, trovato " + Printer.Print(parts[pos], true));
                if (k == "for" || k == "as")
                {
                    pos++;
                    var lv = new LoopVar { Var = next() };
                    string how = peek(); pos++;
                    switch (how)
                    {
                        case "in": lv.Kind = "in"; lv.Init = next(); break;
                        case "on": lv.Kind = "on"; lv.Init = next(); break;
                        case "across": lv.Kind = "across"; lv.Init = next(); break;
                        case "=":
                            lv.Kind = "="; lv.Init = next();
                            if (peek() == "then") { pos++; lv.Then = next(); }
                            break;
                        case "from": case "upfrom": case "downfrom":
                            lv.Kind = "num"; lv.From = next(); lv.Down = how == "downfrom";
                            while (true)
                            {
                                string w = peek();
                                if (w == "to" || w == "upto") { pos++; lv.To = next(); }
                                else if (w == "below") { pos++; lv.To = next(); lv.Below = true; }
                                else if (w == "downto") { pos++; lv.To = next(); lv.Down = true; }
                                else if (w == "above") { pos++; lv.To = next(); lv.Down = true; lv.Below = true; }
                                else if (w == "by") { pos++; lv.By = next(); }
                                else break;
                            }
                            break;
                        case "below": case "to": case "upto":
                            lv.Kind = "num"; lv.From = 0L; lv.To = next(); lv.Below = how == "below";
                            if (peek() == "by") { pos++; lv.By = next(); }
                            break;
                        case "being":
                            {
                                // for k being the hash-keys of h [using (hash-value v)], for v being each hash-value in h
                                if (peek() == "the" || peek() == "each") pos++;
                                string what = peek(); pos++;
                                if (what == "hash-key" || what == "hash-keys") lv.Kind = "hash-keys";
                                else if (what == "hash-value" || what == "hash-values") lv.Kind = "hash-values";
                                else throw new LispError("loop for ... being: si possono scorrere hash-keys e hash-values, non " + (what ?? "questo"));
                                if (peek() != "of" && peek() != "in") throw new LispError("loop for ... being the " + what + ": manca of");
                                pos++;
                                lv.Init = next();
                                if (peek() == "using")
                                {
                                    pos++;
                                    var u = ListItems(next());
                                    if (u.Count != 2 || !(u[1] is Sym)) throw new LispError("loop: using vuole (hash-value v) o (hash-key k)");
                                    lv.Then = u[1];   // la variabile per l'altra metà della coppia
                                }
                                break;
                            }
                        default: throw new LispError("loop for: forma non supportata: " + how);
                    }
                    vars.Add(lv);
                    continue;
                }
                if (k == "with")
                {
                    pos++;
                    var v = (Sym)next();
                    object init = null;
                    if (peek() == "=") { pos++; init = next(); }
                    ne.Define(v, Eval(init, ne));
                    while (peek() == "and") { pos++; var v2 = (Sym)next(); object i2 = null; if (peek() == "=") { pos++; i2 = next(); } ne.Define(v2, Eval(i2, ne)); }
                    continue;
                }
                if (k == "named")
                {
                    pos++;
                    loopName = next() as Sym;
                    continue;
                }
                if (k == "repeat")
                {
                    pos++;
                    vars.Add(new LoopVar { Kind = "repeat", Init = next() });
                    continue;
                }
                if (k == "finally")
                {
                    pos++;
                    if (peek() == "return") { pos++; var f = next(); finallyForms.Add(List(Intern("return"), f)); continue; }
                    while (pos < parts.Count && !(parts[pos] is Sym && LoopKeyword((Sym)parts[pos]))) finallyForms.Add(parts[pos++]);
                    continue;
                }
                if (k == "initially")
                {
                    pos++;
                    while (pos < parts.Count && !(parts[pos] is Sym && LoopKeyword((Sym)parts[pos]))) Eval(parts[pos++], ne);
                    continue;
                }
                addAction(parseAction());
            }

            try
            {
                // valori iniziali delle variabili
                foreach (var lv in vars)
                {
                    switch (lv.Kind)
                    {
                        case "in": case "on": lv.Seq = ev(lv.Init); LoopBind(ne, lv.Var, null, true); break;
                        case "across": lv.Seq = Seq(ev(lv.Init)); LoopBind(ne, lv.Var, null, true); break;
                        case "=": LoopBind(ne, lv.Var, null, true); break;
                        case "num":
                            lv.Init = ev(lv.From); lv.To = lv.To != null ? ev(lv.To) : null; lv.By = lv.By != null ? ev(lv.By) : 1L;
                            LoopBind(ne, lv.Var, lv.Init, true);
                            break;
                        case "repeat": lv.Seq = ev(lv.Init); lv.Index = 0; break;
                        case "hash-keys": case "hash-values":
                            {
                                var h = ev(lv.Init) as Dictionary<object, object>;
                                if (h == null) throw new LispError("loop for ... being the " + lv.Kind + ": non è una tabella hash");
                                lv.Seq = h.Select(kv => (object)new Cons(kv.Key == NilKey ? null : kv.Key, kv.Value)).ToList();
                                LoopBind(ne, lv.Var, null, true);
                                if (lv.Then != null) LoopBind(ne, lv.Then, null, true);
                                break;
                            }
                    }
                }
                while (true)
                {
                    if (Interrupt) throw new LispInterrupted();
                    bool stop = false;
                    foreach (var lv in vars)
                    {
                        switch (lv.Kind)
                        {
                            case "in":
                                {
                                    var cc = lv.Seq as Cons;
                                    if (cc == null) { stop = true; break; }
                                    LoopBind(ne, lv.Var, cc.Car, false);
                                    lv.Seq = cc.Cdr;
                                    break;
                                }
                            case "on":
                                if (!(lv.Seq is Cons)) { stop = true; break; }
                                LoopBind(ne, lv.Var, lv.Seq, false);
                                lv.Seq = ((Cons)lv.Seq).Cdr;
                                break;
                            case "across":
                                {
                                    var l = (List<object>)lv.Seq;
                                    if (lv.Index >= l.Count) { stop = true; break; }
                                    LoopBind(ne, lv.Var, l[lv.Index++], false);
                                    break;
                                }
                            case "=":
                                LoopBind(ne, lv.Var, lv.First || lv.Then == null ? ev(lv.Init) : ev(lv.Then), false);
                                lv.First = false;
                                break;
                            case "num":
                                {
                                    object v;
                                    if (lv.First) { v = lv.Init; lv.First = false; }
                                    else
                                    {
                                        object curV; ne.TryGet((Sym)lv.Var, out curV);
                                        v = lv.Down ? Num.Sub(curV, lv.By) : Num.Add(curV, lv.By);
                                    }
                                    if (lv.To != null)
                                    {
                                        int cmp = Num.Compare(v, lv.To);
                                        if (lv.Down ? (lv.Below ? cmp <= 0 : cmp < 0) : (lv.Below ? cmp >= 0 : cmp > 0)) { stop = true; break; }
                                    }
                                    LoopBind(ne, lv.Var, v, false);
                                    break;
                                }
                            case "repeat":
                                if (Num.Compare((long)lv.Index, lv.Seq) >= 0) { stop = true; break; }
                                lv.Index++;
                                break;
                            case "hash-keys": case "hash-values":
                                {
                                    var l = (List<object>)lv.Seq;
                                    if (lv.Index >= l.Count) { stop = true; break; }
                                    var kv = (Cons)l[lv.Index++];
                                    bool keys = lv.Kind == "hash-keys";
                                    LoopBind(ne, lv.Var, keys ? kv.Car : kv.Cdr, false);
                                    if (lv.Then != null) LoopBind(ne, lv.Then, keys ? kv.Cdr : kv.Car, false);
                                    break;
                                }
                        }
                        if (stop) break;
                    }
                    if (stop) break;
                    try { foreach (var a in actions) a(); }
                    catch (LoopEnd) { break; }
                    if (vars.Count == 0 && actions.Count == 0) break;
                }
                object accResult = accKind == "collect" || accKind == "append" ? ToList(accList, null) : acc;
                if (accInto != null) { object iv; ne.TryGet(accInto, out iv); accResult = null; }
                if (boolKind != null && accKind == null) accResult = boolKind == "thereis" ? null : T;   // always/never arrivati in fondo
                if (finallyForms.Count > 0) { var fr = Progn(ToList(finallyForms, null), ne); if (accKind == null && boolKind == null) return fr; }
                return accResult;
            }
            catch (ReturnFrom rf)
            {
                if (rf.Name == null || (loopName != null && rf.Name == loopName)) return rf.Value;
                throw;
            }
        }

        // while/until e (loop-finish): il ciclo finisce passando da finally
        sealed class LoopEnd : Exception { public LoopEnd() : base("loop-finish fuori da un loop") { } }

        // for x / for (a b) / for (a . b): variabili semplici o destrutturate
        static void LoopBind(Env env, object pattern, object value, bool define)
        {
            if (pattern == null) return;
            var s = pattern as Sym;
            if (s != null) { if (define) env.Define(s, value); else env.TrySet(s, value); return; }
            var p = pattern as Cons;
            if (p == null) return;
            var v = value as Cons;
            LoopBind(env, p.Car, v != null ? v.Car : null, define);
            LoopBind(env, p.Cdr, v != null ? v.Cdr : null, define);
        }

        static object List(params object[] items) { return ToList(items); }

        static readonly HashSet<string> loopWords = new HashSet<string>
        {
            "for", "as", "with", "repeat", "do", "doing", "collect", "collecting", "append", "appending", "nconc", "sum", "summing",
            "count", "counting", "maximize", "maximizing", "minimize", "minimizing", "when", "if", "unless", "return", "while", "until",
            "finally", "initially", "and", "else", "end", "into", "named", "always", "never", "thereis"
        };

        static bool LoopKeyword(Sym s) { return loopWords.Contains(s.Name); }

        // ---------- registrazione delle funzioni ----------

        public void Def(string name, int min, int max, Func<object[], object> f)
        {
            var s = Intern(name);
            s.Function = new Builtin { Name = name, Min = min, Max = max, F = f };
        }

        object Bool(bool b) { return b ? (object)T : null; }
    }
}
