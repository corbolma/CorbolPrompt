// Funzioni predefinite di CorbolPrompt Lisp: numeri, liste, sequenze, stringhe, caratteri, tabelle hash,
// format, e i comandi per il sistema (sh, pwsh, run, cd, ls, read-file...).
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
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
    // Stream di testo: in uscita su una stringa o su un file, in ingresso da una stringa o da un file.
    // Il terminale non è uno stream: t e nil (o *standard-output* che vale t) scrivono lì.
    sealed class LispStream
    {
        public TextWriter W;          // uscita
        public TextReader R;          // ingresso (read lo sostituisce con il resto del testo)
        public IDisposable File;      // il file da chiudere, se c'è
        public string Name;
        public bool AtLineStart = true;
        public bool Closed;

        public void Write(string s)
        {
            if (Closed) throw new LispError("lo stream è chiuso: " + Name);
            if (string.IsNullOrEmpty(s)) return;
            W.Write(s);
            AtLineStart = s[s.Length - 1] == '\n';
        }

        public void Close()
        {
            if (Closed) return;
            Closed = true;
            if (W != null) W.Flush();
            if (File != null) File.Dispose();
        }

        public override string ToString() { return "#<stream " + Name + ">"; }
    }

    partial class Interp
    {
        static string Str(object o)
        {
            var s = o as string;
            if (s != null) return s;
            if (o is char) return o.ToString();
            var sym = o as Sym;
            if (sym != null) return sym.Name.TrimStart(':');
            if (o == null) return "nil";
            throw new LispError("serve una stringa: " + Printer.Print(o, true));
        }

        static char Chr(object o)
        {
            if (o is char) return (char)o;
            var s = o as string;
            if (s != null && s.Length == 1) return s[0];
            throw new LispError("serve un carattere: " + Printer.Print(o, true));
        }

        // Valore di una keyword negli argomenti (:test, :key, :initial-value...) a partire da "from"
        static object KeyArg(object[] a, int from, string key, object def)
        {
            for (int i = from; i + 1 < a.Length; i += 2)
                if (a[i] is Sym && ((Sym)a[i]).Name == key) return a[i + 1];
            return def;
        }

        Func<object, object, bool> TestOf(object[] a, int from)
        {
            var test = KeyArg(a, from, ":test", null);
            var key = KeyArg(a, from, ":key", null);
            Func<object, object> k = key == null ? (Func<object, object>)(x => x) : x => Call(key, x);
            if (test == null) return (x, y) => IsEql(x, k(y)) || (x is string && IsEqual(x, k(y)));
            return (x, y) => Truthy(Call(test, x, k(y)));
        }

        Func<object, object> KeyFn(object[] a, int from)
        {
            var key = KeyArg(a, from, ":key", null);
            return key == null ? (Func<object, object>)(x => x) : x => Call(key, x);
        }

        void Builtins()
        {
            Intern("pi").Value = Math.PI; Intern("pi").Constant = true;
            Intern("most-positive-fixnum").Value = long.MaxValue;
            Intern("most-negative-fixnum").Value = long.MinValue;

            // ---- numeri ----
            Def("+", 0, -1, a => { object r = 0L; foreach (var x in a) r = Num.Add(r, x); return r; });
            Def("*", 0, -1, a => { object r = 1L; foreach (var x in a) r = Num.Mul(r, x); return r; });
            Def("-", 1, -1, a => { if (a.Length == 1) return Num.Sub(0L, a[0]); object r = a[0]; for (int i = 1; i < a.Length; i++) r = Num.Sub(r, a[i]); return r; });
            Def("/", 1, -1, a => { if (a.Length == 1) return Num.Div(1L, a[0]); object r = a[0]; for (int i = 1; i < a.Length; i++) r = Num.Div(r, a[i]); return r; });
            Def("1+", 1, 1, a => Num.Add(a[0], 1L));
            Def("1-", 1, 1, a => Num.Sub(a[0], 1L));
            Func<Func<int, bool>, Func<object[], object>> cmp = ok => a =>
            {
                for (int i = 0; i + 1 < a.Length; i++) if (!ok(Num.Compare(a[i], a[i + 1]))) return null;
                if (a.Length == 1) Num.Compare(a[0], a[0]);
                return T;
            };
            Def("=", 1, -1, cmp(c => c == 0));
            Def("<", 1, -1, cmp(c => c < 0));
            Def(">", 1, -1, cmp(c => c > 0));
            Def("<=", 1, -1, cmp(c => c <= 0));
            Def(">=", 1, -1, cmp(c => c >= 0));
            Def("/=", 1, -1, a => { for (int i = 0; i < a.Length; i++) for (int j = i + 1; j < a.Length; j++) if (Num.Compare(a[i], a[j]) == 0) return null; return T; });
            Def("min", 1, -1, a => a.Aggregate((x, y) => Num.Compare(y, x) < 0 ? y : x));
            Def("max", 1, -1, a => a.Aggregate((x, y) => Num.Compare(y, x) > 0 ? y : x));
            Def("abs", 1, 1, a => Num.Compare(a[0], 0L) < 0 ? Num.Sub(0L, a[0]) : a[0]);
            Def("mod", 2, 2, a => { var q = Num.Rounded(a[0], a[1], "floor"); return Num.Sub(a[0], Num.Mul(q, a[1])); });
            Def("rem", 2, 2, a => { var q = Num.Rounded(a[0], a[1], "truncate"); return Num.Sub(a[0], Num.Mul(q, a[1])); });
            foreach (var mode in new[] { "floor", "ceiling", "round", "truncate" })
            {
                string m = mode;
                Def(m, 1, 2, a => Num.Rounded(a[0], a.Length > 1 ? a[1] : 1L, m));
            }
            Def("expt", 2, 2, a =>
            {
                if (Num.IsInt(a[0]) && Num.IsInt(a[1]) && Num.Compare(a[1], 0L) >= 0)
                {
                    var e = Num.ToBig(a[1]);
                    if (e > 100000) throw new LispError("expt: esponente troppo grande");
                    return Num.Norm(BigInteger.Pow(Num.ToBig(a[0]), (int)e));
                }
                return Math.Pow(Num.Dbl(a[0]), Num.Dbl(a[1]));
            });
            Def("sqrt", 1, 1, a =>
            {
                double d = Math.Sqrt(Num.Dbl(a[0]));
                if (Num.IsInt(a[0]) && d == Math.Floor(d) && d < 1e15 && Num.Compare(Num.Mul((long)d, (long)d), a[0]) == 0) return (long)d;
                return d;
            });
            Def("isqrt", 1, 1, a => Num.Norm(new BigInteger(Math.Floor(Math.Sqrt(Num.Dbl(a[0]))))));
            Def("exp", 1, 1, a => Math.Exp(Num.Dbl(a[0])));
            Def("log", 1, 2, a => a.Length > 1 ? Math.Log(Num.Dbl(a[0]), Num.Dbl(a[1])) : Math.Log(Num.Dbl(a[0])));
            Def("sin", 1, 1, a => Math.Sin(Num.Dbl(a[0])));
            Def("cos", 1, 1, a => Math.Cos(Num.Dbl(a[0])));
            Def("tan", 1, 1, a => Math.Tan(Num.Dbl(a[0])));
            Def("atan", 1, 2, a => a.Length > 1 ? Math.Atan2(Num.Dbl(a[0]), Num.Dbl(a[1])) : Math.Atan(Num.Dbl(a[0])));
            Def("float", 1, 2, a => Num.Dbl(a[0]));
            Def("gcd", 0, -1, a => { BigInteger r = 0; foreach (var x in a) r = BigInteger.GreatestCommonDivisor(r, Num.ToBig(x)); return Num.Norm(r); });
            Def("lcm", 0, -1, a => { BigInteger r = 1; foreach (var x in a) { var b = Num.ToBig(x); if (b.IsZero) return 0L; r = BigInteger.Abs(r * b) / BigInteger.GreatestCommonDivisor(r, b); } return Num.Norm(r); });
            Def("random", 1, 1, a =>
            {
                if (a[0] is double) return random.NextDouble() * (double)a[0];
                long n = (long)Num.Dbl(a[0]);
                if (n <= 0) throw new LispError("random: serve un numero positivo");
                return (long)(random.NextDouble() * n);
            });
            Def("numberp", 1, 1, a => Bool(Num.Is(a[0])));
            Def("integerp", 1, 1, a => Bool(Num.IsInt(a[0])));
            Def("floatp", 1, 1, a => Bool(a[0] is double));
            Def("zerop", 1, 1, a => Bool(Num.Compare(a[0], 0L) == 0));
            Def("plusp", 1, 1, a => Bool(Num.Compare(a[0], 0L) > 0));
            Def("minusp", 1, 1, a => Bool(Num.Compare(a[0], 0L) < 0));
            Def("evenp", 1, 1, a => Bool(Num.ToBig(a[0]).IsEven));
            Def("oddp", 1, 1, a => Bool(!Num.ToBig(a[0]).IsEven));
            Def("parse-integer", 1, -1, a =>
            {
                var s = Str(a[0]).Trim();
                var radix = KeyArg(a, 1, ":radix", 10L);
                bool junk = Truthy(KeyArg(a, 1, ":junk-allowed", null));
                try
                {
                    if (Num.ToInt(radix) == 10) { var n = Reader.ParseNumber(s); if (Num.IsInt(n)) return n; }
                    else return Convert.ToInt64(s, Num.ToInt(radix));
                }
                catch { }
                if (junk) return null;
                throw new LispError("parse-integer: non è un intero: \"" + s + "\"");
            });
            Def("parse-number", 1, 1, a => Reader.ParseNumber(Str(a[0]).Trim()));

            // ---- uguaglianza e tipi ----
            Def("eq", 2, 2, a => Bool(IsEql(a[0], a[1])));
            Def("eql", 2, 2, a => Bool(IsEql(a[0], a[1])));
            Def("equal", 2, 2, a => Bool(IsEqual(a[0], a[1])));
            Def("equalp", 2, 2, a => Bool(IsEqual(a[0], a[1]) || (a[0] is string && a[1] is string && string.Equals((string)a[0], (string)a[1], StringComparison.OrdinalIgnoreCase)) ||
                                                    (Num.Is(a[0]) && Num.Is(a[1]) && Num.Compare(a[0], a[1]) == 0)));
            Def("not", 1, 1, a => Bool(a[0] == null));
            Def("null", 1, 1, a => Bool(a[0] == null));
            Def("atom", 1, 1, a => Bool(!(a[0] is Cons)));
            Def("consp", 1, 1, a => Bool(a[0] is Cons));
            Def("listp", 1, 1, a => Bool(a[0] == null || a[0] is Cons));
            Def("symbolp", 1, 1, a => Bool(a[0] == null || a[0] is Sym));
            Def("keywordp", 1, 1, a => Bool(a[0] is Sym && ((Sym)a[0]).IsKeyword));
            Def("stringp", 1, 1, a => Bool(a[0] is string));
            Def("characterp", 1, 1, a => Bool(a[0] is char));
            Def("functionp", 1, 1, a => Bool(a[0] is LispFn));
            Def("vectorp", 1, 1, a => Bool(a[0] is object[] || a[0] is string));
            Def("arrayp", 1, 1, a => Bool(a[0] is object[] || a[0] is string));
            Def("hash-table-p", 1, 1, a => Bool(a[0] is Dictionary<object, object>));
            Def("boundp", 1, 1, a => Bool(a[0] is Sym && ((Sym)a[0]).Value != Sym.Unbound));
            Def("fboundp", 1, 1, a => Bool(a[0] is Sym && ((Sym)a[0]).Function != null));
            Def("type-of", 1, 1, a => Intern(TypeName(a[0])));
            Def("typep", 2, 3, a => Bool(TypeP(a[0], a[1])));

            // ---- liste ----
            Def("cons", 2, 2, a => new Cons(a[0], a[1]));
            Def("car", 1, 1, a => a[0] == null ? null : AsCons(a[0]).Car);
            Def("cdr", 1, 1, a => a[0] == null ? null : AsCons(a[0]).Cdr);
            Def("first", 1, 1, a => a[0] == null ? null : AsCons(a[0]).Car);
            Def("rest", 1, 1, a => a[0] == null ? null : AsCons(a[0]).Cdr);
            string[] nths = { "second", "third", "fourth", "fifth", "sixth", "seventh", "eighth", "ninth", "tenth" };
            for (int i = 0; i < nths.Length; i++)
            {
                int n = i + 1;
                Def(nths[i], 1, 1, a => { var c = NthCdr(a[0], n) as Cons; return c == null ? null : c.Car; });
            }
            foreach (var name in new[] { "caar", "cadr", "cdar", "cddr", "caddr", "cdddr", "cadddr" })
            {
                string ops = name.Substring(1, name.Length - 2);
                Def(name, 1, 1, a =>
                {
                    object x = a[0];
                    for (int i = ops.Length - 1; i >= 0; i--) { if (x == null) return null; x = ops[i] == 'a' ? AsCons(x).Car : AsCons(x).Cdr; }
                    return x;
                });
            }
            Def("list", 0, -1, a => ToList(a.ToList(), null));
            Def("list*", 1, -1, a => ToList(a.Take(a.Length - 1).ToList(), a[a.Length - 1]));
            Def("make-list", 1, -1, a => ToList(Enumerable.Repeat(KeyArg(a, 1, ":initial-element", null), Num.ToInt(a[0])).ToList(), null));
            Def("length", 1, 1, a => a[0] is string ? ((string)a[0]).Length : a[0] is object[] ? ((object[])a[0]).Length :
                                     a[0] is Dictionary<object, object> ? ((Dictionary<object, object>)a[0]).Count : (object)(long)ListItems(a[0]).Count);
            Def("list-length", 1, 1, a => (long)ListItems(a[0]).Count);
            Def("nth", 2, 2, a => { var c = NthCdr(a[1], Num.ToInt(a[0])) as Cons; return c == null ? null : c.Car; });
            Def("nthcdr", 2, 2, a => NthCdr(a[1], Num.ToInt(a[0])));
            Def("elt", 2, 2, a => Elt(a[0], Num.ToInt(a[1])));
            Def("last", 1, 2, a =>
            {
                var items = ListItems(a[0]);
                int n = a.Length > 1 ? Num.ToInt(a[1]) : 1;
                return NthCdr(a[0], Math.Max(0, items.Count - n));
            });
            Def("butlast", 1, 2, a => { var l = ListItems(a[0]); int n = a.Length > 1 ? Num.ToInt(a[1]) : 1; return ToList(l.Take(Math.Max(0, l.Count - n)).ToList(), null); });
            Def("append", 0, -1, a =>
            {
                if (a.Length == 0) return null;
                var items = new List<object>();
                for (int i = 0; i < a.Length - 1; i++) items.AddRange(ListItems(a[i]));
                return ToList(items, a[a.Length - 1]);
            });
            Def("nconc", 0, -1, a => Call(Intern("append"), a));
            Def("reverse", 1, 1, a => { var l = Seq(a[0]); l.Reverse(); return SameSeq(a[0], l); });
            Def("nreverse", 1, 1, a => { var l = Seq(a[0]); l.Reverse(); return SameSeq(a[0], l); });
            Def("copy-list", 1, 1, a => ToList(ListItems(a[0]), null));
            Def("copy-seq", 1, 1, a => SameSeq(a[0], Seq(a[0])));
            Def("copy-tree", 1, 1, a => CopyTree(a[0]));
            Def("member", 2, -1, a =>
            {
                var test = TestOf(a, 2);
                for (object l = a[1]; l is Cons; l = ((Cons)l).Cdr) if (test(a[0], ((Cons)l).Car)) return l;
                return null;
            });
            Def("assoc", 2, -1, a =>
            {
                var test = TestOf(a, 2);
                foreach (var e in ListItems(a[1])) { var c = e as Cons; if (c != null && test(a[0], c.Car)) return c; }
                return null;
            });
            Def("rassoc", 2, -1, a =>
            {
                var test = TestOf(a, 2);
                foreach (var e in ListItems(a[1])) { var c = e as Cons; if (c != null && test(a[0], c.Cdr)) return c; }
                return null;
            });
            Def("acons", 3, 3, a => new Cons(new Cons(a[0], a[1]), a[2]));
            Def("pairlis", 2, 3, a => ToList(ListItems(a[0]).Zip(ListItems(a[1]), (k, v) => (object)new Cons(k, v)).ToList(), a.Length > 2 ? a[2] : null));
            Def("getf", 2, 3, a => { var l = ListItems(a[0]); for (int i = 0; i + 1 < l.Count; i += 2) if (IsEql(l[i], a[1])) return l[i + 1]; return a.Length > 2 ? a[2] : null; });
            Def("rplaca", 2, 2, a => { AsCons(a[0]).Car = a[1]; return a[0]; });
            Def("rplacd", 2, 2, a => { AsCons(a[0]).Cdr = a[1]; return a[0]; });
            Def("range", 1, 3, a =>
            {
                // (range fine) (range inizio fine) (range inizio fine passo): non è Common Lisp, ma è comodo
                object from = a.Length > 1 ? a[0] : 0L, to = a.Length > 1 ? a[1] : a[0], by = a.Length > 2 ? a[2] : 1L;
                var r = new List<object>();
                bool down = Num.Compare(by, 0L) < 0;
                if (Num.Compare(by, 0L) == 0) throw new LispError("range: il passo non può essere 0");
                for (object x = from; down ? Num.Compare(x, to) > 0 : Num.Compare(x, to) < 0; x = Num.Add(x, by))
                {
                    r.Add(x);
                    if (r.Count > 10000000) throw new LispError("range: troppi elementi");
                }
                return ToList(r, null);
            });
            Def("iota", 1, 3, a =>
            {
                int n = Num.ToInt(a[0]);
                object start = a.Length > 1 ? a[1] : 0L, step = a.Length > 2 ? a[2] : 1L;
                var r = new List<object>();
                object x = start;
                for (int i = 0; i < n; i++) { r.Add(x); x = Num.Add(x, step); }
                return ToList(r, null);
            });

            // ---- funzioni di ordine superiore ----
            Def("funcall", 1, -1, a => Call(a[0], a.Skip(1).ToArray()));
            Def("apply", 1, -1, a =>
            {
                var args = a.Skip(1).Take(a.Length - 2).ToList();
                if (a.Length > 1) args.AddRange(ListItems(a[a.Length - 1]));
                return Call(a[0], args.ToArray());
            });
            Def("mapcar", 2, -1, a =>
            {
                var lists = a.Skip(1).Select(Seq).ToList();
                int n = lists.Min(l => l.Count);
                var r = new List<object>(n);
                for (int i = 0; i < n; i++) r.Add(Call(a[0], lists.Select(l => l[i]).ToArray()));
                return ToList(r, null);
            });
            Def("mapc", 2, -1, a =>
            {
                var lists = a.Skip(1).Select(Seq).ToList();
                int n = lists.Min(l => l.Count);
                for (int i = 0; i < n; i++) Call(a[0], lists.Select(l => l[i]).ToArray());
                return a[1];
            });
            Def("mapcan", 2, -1, a =>
            {
                var lists = a.Skip(1).Select(Seq).ToList();
                int n = lists.Min(l => l.Count);
                var r = new List<object>();
                for (int i = 0; i < n; i++) r.AddRange(ListItems(Call(a[0], lists.Select(l => l[i]).ToArray())));
                return ToList(r, null);
            });
            Def("maplist", 2, 2, a =>
            {
                var r = new List<object>();
                for (object l = a[1]; l is Cons; l = ((Cons)l).Cdr) r.Add(Call(a[0], l));
                return ToList(r, null);
            });
            Def("map", 3, -1, a =>
            {
                var lists = a.Skip(2).Select(Seq).ToList();
                int n = lists.Min(l => l.Count);
                var r = new List<object>(n);
                for (int i = 0; i < n; i++) r.Add(Call(a[1], lists.Select(l => l[i]).ToArray()));
                string type = a[0] == null ? "nil" : Str(a[0]);
                if (type == "nil") return null;
                if (type == "string") return new string(r.Select(Chr).ToArray());
                if (type == "vector") return r.ToArray();
                return ToList(r, null);
            });
            Def("reduce", 2, -1, a =>
            {
                var items = Seq(a[1]);
                var keyFn = KeyFn(a, 2);
                items = items.Select(keyFn).ToList();
                if (Truthy(KeyArg(a, 2, ":from-end", null))) items.Reverse();
                object init = KeyArg(a, 2, ":initial-value", Sym.Unbound);
                bool fromEnd = Truthy(KeyArg(a, 2, ":from-end", null));
                if (init == Sym.Unbound)
                {
                    if (items.Count == 0) return Call(a[0]);
                    init = items[0];
                    items.RemoveAt(0);
                }
                object acc = init;
                foreach (var x in items) acc = fromEnd ? Call(a[0], x, acc) : Call(a[0], acc, x);
                return acc;
            });
            Def("remove-if", 2, -1, a => { var k = KeyFn(a, 2); return SameSeq(a[1], Seq(a[1]).Where(x => !Truthy(Call(a[0], k(x)))).ToList()); });
            Def("remove-if-not", 2, -1, a => { var k = KeyFn(a, 2); return SameSeq(a[1], Seq(a[1]).Where(x => Truthy(Call(a[0], k(x)))).ToList()); });
            Def("delete-if", 2, -1, a => { var k = KeyFn(a, 2); return SameSeq(a[1], Seq(a[1]).Where(x => !Truthy(Call(a[0], k(x)))).ToList()); });
            Def("filter", 2, 2, a => SameSeq(a[1], Seq(a[1]).Where(x => Truthy(Call(a[0], x))).ToList()));
            Def("remove", 2, -1, a => { var t = TestOf(a, 2); return SameSeq(a[1], Seq(a[1]).Where(x => !t(a[0], x)).ToList()); });
            Def("delete", 2, -1, a => { var t = TestOf(a, 2); return SameSeq(a[1], Seq(a[1]).Where(x => !t(a[0], x)).ToList()); });
            Def("remove-duplicates", 1, -1, a =>
            {
                var t = TestOf(a, 1);
                var r = new List<object>();
                foreach (var x in Seq(a[0])) if (!r.Any(y => t(x, y) || IsEqual(x, y))) r.Add(x);
                return SameSeq(a[0], r);
            });
            Def("find", 2, -1, a => { var t = TestOf(a, 2); return Seq(a[1]).FirstOrDefault(x => t(a[0], x)); });
            Def("find-if", 2, -1, a => { var k = KeyFn(a, 2); return Seq(a[1]).FirstOrDefault(x => Truthy(Call(a[0], k(x)))); });
            Def("find-if-not", 2, -1, a => { var k = KeyFn(a, 2); return Seq(a[1]).FirstOrDefault(x => !Truthy(Call(a[0], k(x)))); });
            Def("position", 2, -1, a => { var t = TestOf(a, 2); var l = Seq(a[1]); int i = l.FindIndex(x => t(a[0], x)); return i < 0 ? null : (object)(long)i; });
            Def("position-if", 2, -1, a => { var k = KeyFn(a, 2); var l = Seq(a[1]); int i = l.FindIndex(x => Truthy(Call(a[0], k(x)))); return i < 0 ? null : (object)(long)i; });
            Def("count", 2, -1, a => { var t = TestOf(a, 2); return (long)Seq(a[1]).Count(x => t(a[0], x)); });
            Def("count-if", 2, -1, a => { var k = KeyFn(a, 2); return (long)Seq(a[1]).Count(x => Truthy(Call(a[0], k(x)))); });
            Def("every", 2, -1, a =>
            {
                var lists = a.Skip(1).Select(Seq).ToList();
                int n = lists.Min(l => l.Count);
                for (int i = 0; i < n; i++) if (!Truthy(Call(a[0], lists.Select(l => l[i]).ToArray()))) return null;
                return T;
            });
            Def("some", 2, -1, a =>
            {
                var lists = a.Skip(1).Select(Seq).ToList();
                int n = lists.Min(l => l.Count);
                for (int i = 0; i < n; i++) { var v = Call(a[0], lists.Select(l => l[i]).ToArray()); if (Truthy(v)) return v; }
                return null;
            });
            Def("notany", 2, -1, a => Bool(!Truthy(Call(Intern("some"), a))));
            Def("notevery", 2, -1, a => Bool(!Truthy(Call(Intern("every"), a))));
            Def("sort", 2, -1, a => SortSeq(a));
            Def("stable-sort", 2, -1, a => SortSeq(a));
            Def("subseq", 2, 3, a =>
            {
                int start = Num.ToInt(a[1]);
                var s = a[0] as string;
                if (s != null)
                {
                    int end = a.Length > 2 && a[2] != null ? Num.ToInt(a[2]) : s.Length;
                    if (start < 0 || end > s.Length || start > end) throw new LispError("subseq: indici fuori dalla stringa");
                    return s.Substring(start, end - start);
                }
                var l = Seq(a[0]);
                int e2 = a.Length > 2 && a[2] != null ? Num.ToInt(a[2]) : l.Count;
                if (start < 0 || e2 > l.Count || start > e2) throw new LispError("subseq: indici fuori dalla sequenza");
                return SameSeq(a[0], l.Skip(start).Take(e2 - start).ToList());
            });
            Def("concatenate", 1, -1, a =>
            {
                string type = Str(a[0]);
                var items = a.Skip(1).SelectMany(Seq).ToList();
                if (type == "string") return new string(items.Select(Chr).ToArray());
                if (type == "vector") return items.ToArray();
                return ToList(items, null);
            });
            Def("coerce", 2, 2, a =>
            {
                string type = Str(a[1]);
                if (type == "string") return a[0] is string ? a[0] : new string(Seq(a[0]).Select(Chr).ToArray());
                if (type == "list") return ToList(Seq(a[0]), null);
                if (type == "vector") return Seq(a[0]).ToArray();
                if (type == "float" || type == "double-float" || type == "single-float") return Num.Dbl(a[0]);
                if (type == "character") return Chr(a[0]);
                throw new LispError("coerce: tipo non supportato: " + type);
            });

            // ---- vettori ----
            Def("vector", 0, -1, a => a.ToArray());
            Def("make-array", 1, -1, a =>
            {
                int n = a[0] is Cons ? Num.ToInt(((Cons)a[0]).Car) : Num.ToInt(a[0]);
                var init = KeyArg(a, 1, ":initial-element", null);
                var arr = new object[n];
                for (int i = 0; i < n; i++) arr[i] = init;
                var contents = KeyArg(a, 1, ":initial-contents", null);
                if (contents != null) { var l = Seq(contents); for (int i = 0; i < Math.Min(n, l.Count); i++) arr[i] = l[i]; }
                return arr;
            });
            Def("aref", 2, 2, a => Elt(a[0], Num.ToInt(a[1])));
            Def("svref", 2, 2, a => Elt(a[0], Num.ToInt(a[1])));

            // ---- simboli ----
            Def("symbol-name", 1, 1, a => a[0] == null ? "nil" : ((Sym)a[0]).Name);
            Def("symbol-value", 1, 1, a => Lookup((Sym)a[0], Global));
            Def("symbol-function", 1, 1, a => ((Sym)a[0]).Function);
            Def("intern", 1, 2, a => Intern(Str(a[0]).ToLowerInvariant()));
            Def("make-symbol", 1, 1, a => new Sym(Str(a[0])));
            Def("gensym", 0, 1, a => new Sym("#:" + (a.Length > 0 ? Str(a[0]) : "g") + (++gensym)));
            Def("set", 2, 2, a => { SetVar((Sym)a[0], a[1], Global); return a[1]; });
            Def("fdefinition", 1, 1, a => ((Sym)a[0]).Function);
            Def("makunbound", 1, 1, a => { ((Sym)a[0]).Value = Sym.Unbound; return a[0]; });
            Def("fmakunbound", 1, 1, a => { ((Sym)a[0]).Function = null; return a[0]; });

            // ---- caratteri ----
            Def("char-code", 1, 1, a => (long)Chr(a[0]));
            Def("code-char", 1, 1, a => (char)Num.ToInt(a[0]));
            Def("char-upcase", 1, 1, a => char.ToUpperInvariant(Chr(a[0])));
            Def("char-downcase", 1, 1, a => char.ToLowerInvariant(Chr(a[0])));
            Def("alpha-char-p", 1, 1, a => Bool(char.IsLetter(Chr(a[0]))));
            Def("digit-char-p", 1, 1, a => { char c = Chr(a[0]); return char.IsDigit(c) ? (object)(long)(c - '0') : null; });
            Def("alphanumericp", 1, 1, a => Bool(char.IsLetterOrDigit(Chr(a[0]))));
            Def("upper-case-p", 1, 1, a => Bool(char.IsUpper(Chr(a[0]))));
            Def("lower-case-p", 1, 1, a => Bool(char.IsLower(Chr(a[0]))));
            Def("char=", 1, -1, a => Bool(a.All(x => Chr(x) == Chr(a[0]))));
            Def("char-equal", 1, -1, a => Bool(a.All(x => char.ToLowerInvariant(Chr(x)) == char.ToLowerInvariant(Chr(a[0])))));
            Def("char<", 2, 2, a => Bool(Chr(a[0]) < Chr(a[1])));
            Def("char>", 2, 2, a => Bool(Chr(a[0]) > Chr(a[1])));
            Def("char", 2, 2, a => Str(a[0])[Num.ToInt(a[1])]);
            Def("schar", 2, 2, a => Str(a[0])[Num.ToInt(a[1])]);

            // ---- stringhe ----
            Def("string", 1, 1, a => Str(a[0]));
            Def("string-upcase", 1, 1, a => Str(a[0]).ToUpperInvariant());
            Def("string-downcase", 1, 1, a => Str(a[0]).ToLowerInvariant());
            Def("string-capitalize", 1, 1, a => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Str(a[0]).ToLowerInvariant()));
            Def("string-trim", 2, 2, a => Str(a[1]).Trim(Seq(a[0]).Select(Chr).ToArray()));
            Def("string-left-trim", 2, 2, a => Str(a[1]).TrimStart(Seq(a[0]).Select(Chr).ToArray()));
            Def("string-right-trim", 2, 2, a => Str(a[1]).TrimEnd(Seq(a[0]).Select(Chr).ToArray()));
            Def("string=", 2, -1, a => Bool(Str(a[0]) == Str(a[1])));
            Def("string-equal", 2, -1, a => Bool(string.Equals(Str(a[0]), Str(a[1]), StringComparison.OrdinalIgnoreCase)));
            Def("string/=", 2, -1, a => Bool(Str(a[0]) != Str(a[1])));
            Def("string<", 2, -1, a => Bool(string.CompareOrdinal(Str(a[0]), Str(a[1])) < 0));
            Def("string>", 2, -1, a => Bool(string.CompareOrdinal(Str(a[0]), Str(a[1])) > 0));
            Def("string<=", 2, -1, a => Bool(string.CompareOrdinal(Str(a[0]), Str(a[1])) <= 0));
            Def("string>=", 2, -1, a => Bool(string.CompareOrdinal(Str(a[0]), Str(a[1])) >= 0));
            Def("string-lessp", 2, -1, a => Bool(string.Compare(Str(a[0]), Str(a[1]), StringComparison.OrdinalIgnoreCase) < 0));
            Def("search", 2, -1, a =>
            {
                if (a[1] is string) { int i = ((string)a[1]).IndexOf(Str(a[0]), StringComparison.Ordinal); return i < 0 ? null : (object)(long)i; }
                var hay = Seq(a[1]); var needle = Seq(a[0]);
                for (int i = 0; i + needle.Count <= hay.Count; i++)
                    if (needle.Select((x, j) => IsEqual(x, hay[i + j])).All(b => b)) return (long)i;
                return null;
            });
            Def("string-prefix-p", 2, 2, a => Bool(Str(a[1]).StartsWith(Str(a[0]), StringComparison.Ordinal)));
            Def("string-suffix-p", 2, 2, a => Bool(Str(a[1]).EndsWith(Str(a[0]), StringComparison.Ordinal)));
            Def("string-contains", 2, 2, a => Bool(Str(a[0]).Contains(Str(a[1]))));
            Def("string-replace", 3, 3, a => Str(a[0]).Replace(Str(a[1]), Str(a[2])));
            Def("split-string", 1, 2, a =>
            {
                string s = Str(a[0]);
                var parts = a.Length > 1 ? s.Split(new[] { Str(a[1]) }, StringSplitOptions.None) : s.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                return ToList(parts.Select(p => (object)p).ToList(), null);
            });
            Def("join", 1, 2, a => string.Join(a.Length > 1 ? Str(a[1]) : " ", Seq(a[0]).Select(x => Printer.Print(x, false))));
            Def("string-join", 1, 2, a => string.Join(a.Length > 1 ? Str(a[1]) : " ", Seq(a[0]).Select(x => Printer.Print(x, false))));
            Def("lines", 1, 1, a => ToList(Str(a[0]).Replace("\r\n", "\n").Split('\n').Select(p => (object)p).ToList(), null));
            Def("make-string", 1, -1, a => new string(KeyArg(a, 1, ":initial-element", null) is char ? (char)KeyArg(a, 1, ":initial-element", ' ') : ' ', Num.ToInt(a[0])));
            Def("write-to-string", 1, -1, a => Printer.Print(a[0], true));
            Def("prin1-to-string", 1, 1, a => Printer.Print(a[0], true));
            Def("princ-to-string", 1, 1, a => Printer.Print(a[0], false));
            Def("read-from-string", 1, 1, a => { var r = new Reader(this, Str(a[0])).Read(); return r == Reader.Eof ? null : r; });
            Def("number-to-string", 1, 1, a => Printer.Print(a[0], false));

            // ---- tabelle hash ----
            Def("make-hash-table", 0, -1, a => new Dictionary<object, object>(EqualComparer.Instance));
            Def("gethash", 2, 3, a =>
            {
                var ht = a[1] as Dictionary<object, object>;
                if (ht == null) throw new LispError("gethash: non è una tabella hash");
                object v;
                return ht.TryGetValue(a[0] ?? NilKey, out v) ? v : (a.Length > 2 ? a[2] : null);
            });
            Def("remhash", 2, 2, a => Bool(((Dictionary<object, object>)a[1]).Remove(a[0] ?? NilKey)));
            Def("clrhash", 1, 1, a => { ((Dictionary<object, object>)a[0]).Clear(); return a[0]; });
            Def("hash-table-count", 1, 1, a => (long)((Dictionary<object, object>)a[0]).Count);
            Def("maphash", 2, 2, a =>
            {
                foreach (var kv in ((Dictionary<object, object>)a[1]).ToList()) Call(a[0], kv.Key == NilKey ? null : kv.Key, kv.Value);
                return null;
            });
            Def("hash-table-keys", 1, 1, a => ToList(((Dictionary<object, object>)a[0]).Keys.Select(k => k == NilKey ? null : k).ToList(), null));
            Def("hash-table-values", 1, 1, a => ToList(((Dictionary<object, object>)a[0]).Values.ToList(), null));

            // ---- valutazione ----
            Def("eval", 1, 1, a => Eval(a[0], Global));
            Def("macroexpand-1", 1, 1, a =>
            {
                var c = a[0] as Cons;
                var m = c != null && c.Car is Sym ? ((Sym)c.Car).Function as Macro : null;
                return m == null ? a[0] : Apply(m.Fn, ListToArray(c.Cdr));
            });
            Def("macroexpand", 1, 1, a =>
            {
                object x = a[0];
                for (int i = 0; i < 100; i++)
                {
                    var c = x as Cons;
                    var m = c != null && c.Car is Sym ? ((Sym)c.Car).Function as Macro : null;
                    if (m == null) break;
                    x = Apply(m.Fn, ListToArray(c.Cdr));
                }
                return x;
            });
            Def("identity", 1, 1, a => a[0]);
            Def("constantly", 1, 1, a => { var v = a[0]; return new Builtin { Name = "constantly", Min = 0, Max = -1, F = b => v }; });
            Def("complement", 1, 1, a => { var f = a[0]; return new Builtin { Name = "complement", Min = 0, Max = -1, F = b => Bool(!Truthy(Call(f, b))) }; });
            Def("values", 0, -1, a => a.Length > 0 ? a[0] : null);
            Def("error", 1, -1, a =>
            {
                string msg = a[0] is string ? Format((string)a[0], a.Skip(1).ToArray()) : Printer.Print(a[0], false);
                throw new LispError(msg);
            });
            Def("warn", 1, -1, a => { FreshLine(); Write("Attenzione: " + Format(Str(a[0]), a.Skip(1).ToArray()) + "\n"); return null; });

            // ---- uscita ----
            // lo stream è l'argomento facoltativo dopo l'oggetto; t e nil vogliono dire *standard-output*
            Func<object[], int, object> sa = (a, i) => a.Length > i ? a[i] : null;
            Def("print", 1, 2, a => { WriteTo(sa(a, 1), "\n" + Printer.Print(a[0], true) + " "); return a[0]; });
            Def("prin1", 1, 2, a => { WriteTo(sa(a, 1), Printer.Print(a[0], true)); return a[0]; });
            Def("princ", 1, 2, a => { WriteTo(sa(a, 1), Printer.Print(a[0], false)); return a[0]; });
            Def("write", 1, -1, a => { WriteTo(KeyArg(a, 1, ":stream", null), Printer.Print(a[0], KeyArg(a, 1, ":escape", T) != null)); return a[0]; });
            Def("pprint", 1, 2, a => { WriteTo(sa(a, 1), "\n" + Printer.Print(a[0], true)); return null; });
            Def("terpri", 0, 1, a => { WriteTo(sa(a, 0), "\n"); return null; });
            Def("fresh-line", 0, 1, a => Bool(FreshLineTo(sa(a, 0))));
            Def("write-line", 1, 2, a => { WriteTo(sa(a, 1), Str(a[0]) + "\n"); return a[0]; });
            Def("write-string", 1, 2, a => { WriteTo(sa(a, 1), Str(a[0])); return a[0]; });
            Def("write-char", 1, 2, a => { WriteTo(sa(a, 1), Chr(a[0]).ToString()); return a[0]; });
            Def("finish-output", 0, 1, a => { var s = OutStream(sa(a, 0)); if (s != null) s.W.Flush(); return null; });
            Def("force-output", 0, 1, a => { var s = OutStream(sa(a, 0)); if (s != null) s.W.Flush(); return null; });
            Def("format", 2, -1, a =>
            {
                string s = Format(Str(a[1]), a.Skip(2).ToArray());
                if (a[0] == null) return s;
                WriteTo(a[0], s);
                return null;
            });
            Def("read-line", 0, -1, a =>
            {
                if (a.Length > 0 && a[0] != null && a[0] != T)
                {
                    var line = InStream(a[0], "read-line").R.ReadLine();
                    return line ?? AtEof(a, 1, "read-line");
                }
                if (ReadLineHook == null) throw new LispError("read-line: nessun input disponibile");
                var s = ReadLineHook();
                if (s == null) throw new LispInterrupted();
                AtLineStart = true;
                return s;
            });
            Def("read", 0, -1, a =>
            {
                if (a.Length > 0 && a[0] != null && a[0] != T)
                {
                    // legge una forma da uno stream: il resto del testo torna nello stream
                    var ls = InStream(a[0], "read");
                    string rest = ls.R.ReadToEnd();
                    var rd = new Reader(this, rest);
                    object form;
                    try { form = rd.Read(); }
                    catch (LispIncomplete) { throw new LispError("read: forma incompleta alla fine dello stream"); }
                    ls.R = new StringReader(rest.Substring(rd.Pos));
                    return form == Reader.Eof ? AtEof(a, 1, "read") : form;
                }
                if (ReadLineHook == null) throw new LispError("read: nessun input disponibile");
                var s = ReadLineHook();
                if (s == null) throw new LispInterrupted();
                AtLineStart = true;
                var r = new Reader(this, s).Read();
                return r == Reader.Eof ? null : r;
            });
            Def("y-or-n-p", 0, -1, a =>
            {
                if (a.Length > 0) Write(Format(Str(a[0]), a.Skip(1).ToArray()) + " (s/n) ");
                var s = ReadLineHook != null ? ReadLineHook() : null;
                AtLineStart = true;
                return Bool(s != null && (s.Trim().StartsWith("s", StringComparison.OrdinalIgnoreCase) || s.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)));
            });

            // ---- tempo ----
            Def("get-universal-time", 0, 0, a => (long)(DateTime.UtcNow - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds);
            Def("get-internal-real-time", 0, 0, a => Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000));
            Intern("internal-time-units-per-second").Value = 1000L;
            Def("sleep", 1, 1, a =>
            {
                var until = DateTime.Now.AddSeconds(Num.Dbl(a[0]));
                while (DateTime.Now < until) { if (Interrupt) throw new LispInterrupted(); Thread.Sleep(Math.Max(1, Math.Min(100, (int)(until - DateTime.Now).TotalMilliseconds))); }
                return null;
            });
            Def("now", 0, 0, a => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        }

        static string TypeName(object o)
        {
            if (o == null) return "null";
            if (o is long) return "integer";
            if (o is BigInteger) return "bignum";
            if (o is double) return "float";
            if (o is string) return "string";
            if (o is char) return "character";
            if (o is Sym) return ((Sym)o).IsKeyword ? "keyword" : "symbol";
            if (o is Cons) return "cons";
            if (o is object[]) return "vector";
            if (o is LispFn) return "function";
            if (o is Dictionary<object, object>) return "hash-table";
            if (o is LispStruct) return ((LispStruct)o).Type.Name;
            if (o is LispStream) return "stream";
            return "t";
        }

        static object Elt(object seq, int i)
        {
            var s = seq as string;
            if (s != null) { if (i < 0 || i >= s.Length) throw new LispError("indice fuori dalla stringa: " + i); return s[i]; }
            var arr = seq as object[];
            if (arr != null) { if (i < 0 || i >= arr.Length) throw new LispError("indice fuori dal vettore: " + i); return arr[i]; }
            var c = NthCdr(seq, i) as Cons;
            if (c == null) throw new LispError("indice fuori dalla lista: " + i);
            return c.Car;
        }

        static object CopyTree(object o)
        {
            var c = o as Cons;
            return c == null ? o : new Cons(CopyTree(c.Car), CopyTree(c.Cdr));
        }

        object SortSeq(object[] a)
        {
            var items = Seq(a[0]);
            var keyFn = KeyFn(a, 2);
            var pred = a[1];
            // ordinamento stabile (merge sort di LINQ) con il predicato "minore di"
            var sorted = items.Select((x, i) => new { x, i, k = keyFn(x) }).ToList();
            sorted.Sort((p, q) =>
            {
                if (Truthy(Call(pred, p.k, q.k))) return -1;
                if (Truthy(Call(pred, q.k, p.k))) return 1;
                return p.i.CompareTo(q.i);
            });
            return SameSeq(a[0], sorted.Select(p => p.x).ToList());
        }

        // ---------- format ----------

        public string Format(string ctl, object[] args)
        {
            var sb = new StringBuilder();
            int ai = 0;
            bool lineStart = AtLineStart;
            FormatInto(sb, ctl, args, ref ai, ref lineStart);
            return sb.ToString();
        }

        void FormatInto(StringBuilder sb, string ctl, object[] args, ref int ai, ref bool lineStart)
        {
            int i = 0;
            Func<object> nextArg = null;
            int aiLocal = ai;
            nextArg = () =>
            {
                if (aiLocal >= args.Length) throw new LispError("format: mancano argomenti per \"" + ctl + "\"");
                return args[aiLocal++];
            };
            while (i < ctl.Length)
            {
                char c = ctl[i++];
                if (c != '~') { sb.Append(c); continue; }
                // parametri: numeri, 'c, v, separati da virgole; modificatori : e @
                var prms = new List<object>();
                var cur = new StringBuilder();
                bool colon = false, at = false;
                while (i < ctl.Length)
                {
                    char p = ctl[i];
                    if (char.IsDigit(p) || (p == '-' && cur.Length == 0)) { cur.Append(p); i++; }
                    else if (p == '\'' && i + 1 < ctl.Length) { prms.Add(ctl[i + 1]); i += 2; }
                    else if (p == ',') { prms.Add(cur.Length > 0 ? (object)long.Parse(cur.ToString(), CultureInfo.InvariantCulture) : null); cur.Clear(); i++; }
                    else if (p == 'v' || p == 'V') { prms.Add(nextArg()); i++; }
                    else if (p == ':') { colon = true; i++; }
                    else if (p == '@') { at = true; i++; }
                    else break;
                }
                if (cur.Length > 0) prms.Add(long.Parse(cur.ToString(), CultureInfo.InvariantCulture));
                if (i >= ctl.Length) throw new LispError("format: direttiva incompleta");
                char d = char.ToLowerInvariant(ctl[i++]);
                Func<int, int, int> pInt = (k, def) => k < prms.Count && prms[k] != null && Num.Is(prms[k]) ? Num.ToInt(prms[k]) : def;
                switch (d)
                {
                    case 'a': case 's':
                        {
                            var v = nextArg();
                            string s = v == null && colon ? "()" : Printer.Print(v, d == 's');
                            int min = pInt(0, 0);
                            sb.Append(at ? s.PadLeft(min) : s.PadRight(min));
                            break;
                        }
                    case 'd': case 'b': case 'o': case 'x':
                        {
                            var v = nextArg();
                            string s;
                            if (Num.IsInt(v))
                            {
                                var b = Num.ToBig(v);
                                int radix = d == 'd' ? 10 : d == 'b' ? 2 : d == 'o' ? 8 : 16;
                                s = ToRadix(b, radix);
                                if (colon && radix == 10)
                                {
                                    string digits = BigInteger.Abs(b).ToString();
                                    var g = new StringBuilder();
                                    for (int k = 0; k < digits.Length; k++) { if (k > 0 && (digits.Length - k) % 3 == 0) g.Append(','); g.Append(digits[k]); }
                                    s = (b.Sign < 0 ? "-" : "") + g;
                                }
                                if (at && b.Sign >= 0) s = "+" + s;
                            }
                            else s = Printer.Print(v, false);
                            int min = pInt(0, 0);
                            char pad = prms.Count > 1 && prms[1] is char ? (char)prms[1] : ' ';
                            sb.Append(s.PadLeft(min, pad));
                            break;
                        }
                    case 'f': case 'e': case 'g': case '$':
                        {
                            var v = nextArg();
                            double x = Num.Dbl(v);
                            int w = pInt(0, 0), dgt = d == '$' ? pInt(0, 2) : pInt(1, -1);
                            if (d == '$') w = pInt(1, 0);
                            string s = dgt >= 0 ? x.ToString("F" + dgt, CultureInfo.InvariantCulture) : Printer.Float(x);
                            if (at && x >= 0) s = "+" + s;
                            sb.Append(s.PadLeft(w));
                            break;
                        }
                    case 'c':
                        {
                            var v = nextArg();
                            char ch = Chr(v);
                            sb.Append(colon && ch == ' ' ? "Space" : ch.ToString());
                            break;
                        }
                    case '%': sb.Append('\n', Math.Max(1, pInt(0, 1))); break;
                    case '&':
                        {
                            bool atStart = sb.Length == 0 ? lineStart : sb[sb.Length - 1] == '\n';
                            if (!atStart) sb.Append('\n');
                            int n = pInt(0, 1);
                            if (n > 1) sb.Append('\n', n - 1);
                            break;
                        }
                    case '~': sb.Append('~', Math.Max(1, pInt(0, 1))); break;
                    case '|': sb.Append('\f'); break;
                    case 't': sb.Append(' ', Math.Max(1, pInt(0, 1))); break;
                    case 'r':
                        {
                            var v = nextArg();
                            int radix = pInt(0, 10);
                            sb.Append(Num.IsInt(v) ? ToRadix(Num.ToBig(v), radix) : Printer.Print(v, false));
                            break;
                        }
                    case 'p':
                        {
                            object v = colon ? (aiLocal > 0 ? args[aiLocal - 1] : null) : nextArg();
                            bool one = v is long && (long)v == 1;
                            sb.Append(at ? (one ? "y" : "ies") : (one ? "" : "s"));
                            break;
                        }
                    case '*':
                        if (at) aiLocal = pInt(0, 0);
                        else if (colon) aiLocal = Math.Max(0, aiLocal - pInt(0, 1));
                        else aiLocal += pInt(0, 1);
                        break;
                    case '\n':
                        while (i < ctl.Length && char.IsWhiteSpace(ctl[i]) && ctl[i] != '\n') i++;
                        break;
                    case '(':
                        {
                            int close = FindClose(ctl, i, '(', ')');
                            string inner = ctl.Substring(i, close - i);
                            i = close + 2;
                            var tmp = new StringBuilder();
                            bool ls = lineStart;
                            FormatInto(tmp, inner, args, ref aiLocal, ref ls);
                            string s = tmp.ToString();
                            if (colon && at) s = s.ToUpperInvariant();
                            else if (colon) s = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
                            else if (at) s = s.Length > 0 ? char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant() : s;
                            else s = s.ToLowerInvariant();
                            sb.Append(s);
                            break;
                        }
                    case '[':
                        {
                            int close = FindClose(ctl, i, '[', ']');
                            var clauses = SplitClauses(ctl.Substring(i, close - i));
                            i = close + 2;
                            string chosen;
                            if (colon) chosen = Truthy(nextArg()) ? (clauses.Count > 1 ? clauses[1] : "") : clauses[0];
                            else if (at) { var v = args.Length > aiLocal ? args[aiLocal] : null; if (Truthy(v)) chosen = clauses[0]; else { aiLocal++; chosen = ""; } }
                            else
                            {
                                var v = nextArg();
                                int idx = Num.Is(v) ? Num.ToInt(v) : 0;
                                chosen = idx >= 0 && idx < clauses.Count ? clauses[idx] : "";
                            }
                            bool ls = lineStart;
                            FormatInto(sb, chosen, args, ref aiLocal, ref ls);
                            break;
                        }
                    case '{':
                        {
                            int close = FindClose(ctl, i, '{', '}');
                            string inner = ctl.Substring(i, close - i);
                            i = close + 2;
                            var items = at ? args.Skip(aiLocal).ToList() : Seq(nextArg());
                            if (at) aiLocal = args.Length;
                            var arr = items.ToArray();
                            int k = 0, guard = 0;
                            while (k < arr.Length && guard++ < 100000)
                            {
                                if (colon)
                                {
                                    var sub = ListToArray(arr[k++]);
                                    int si = 0; bool ls = lineStart;
                                    FormatInto(sb, inner, sub, ref si, ref ls);
                                    if (sb.Length > 0 && StopMarker(sb) && k >= arr.Length) { RemoveStop(sb); break; }
                                    RemoveStop(sb);
                                }
                                else
                                {
                                    int before = k;
                                    bool ls = lineStart;
                                    FormatInto(sb, inner, arr, ref k, ref ls);
                                    bool stopped = StopMarker(sb);
                                    RemoveStop(sb);
                                    if (stopped || k == before) break;
                                }
                            }
                            break;
                        }
                    case '^':
                        // fine degli argomenti: si ferma l'iterazione di ~{ (segnale per il chiamante)
                        if (aiLocal >= args.Length) { sb.Append('￿'); ai = aiLocal; return; }
                        break;
                    case ';': case ']': case '}': case ')': break;
                    case 'w': sb.Append(Printer.Print(nextArg(), true)); break;
                    case 'i': case '_': break;
                    default: throw new LispError("format: direttiva non supportata: ~" + d);
                }
            }
            ai = aiLocal;
        }

        static bool StopMarker(StringBuilder sb) { return sb.Length > 0 && sb[sb.Length - 1] == '￿'; }
        static void RemoveStop(StringBuilder sb) { if (StopMarker(sb)) sb.Length--; }

        static int FindClose(string s, int from, char open, char close)
        {
            int depth = 1;
            for (int i = from; i < s.Length - 1; i++)
            {
                if (s[i] != '~') continue;
                int j = i + 1;
                while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ',' || s[j] == ':' || s[j] == '@' || s[j] == '\'')) { if (s[j] == '\'') j++; j++; }
                if (j >= s.Length) break;
                if (s[j] == open) depth++;
                else if (s[j] == close && --depth == 0) return i;
                i = j;
            }
            throw new LispError("format: manca ~" + close);
        }

        static List<string> SplitClauses(string s)
        {
            var r = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length - 1; i++)
            {
                if (s[i] != '~') continue;
                char n = s[i + 1];
                if (n == '[' || n == '{' || n == '(') depth++;
                else if (n == ']' || n == '}' || n == ')') depth--;
                else if (n == ';' && depth == 0) { r.Add(s.Substring(start, i - start)); start = i + 2; }
                i++;
            }
            r.Add(s.Substring(start));
            return r;
        }

        static string ToRadix(BigInteger b, int radix)
        {
            if (radix == 10) return b.ToString();
            if (b.IsZero) return "0";
            bool neg = b.Sign < 0;
            b = BigInteger.Abs(b);
            var sb = new StringBuilder();
            while (!b.IsZero) { int d = (int)(b % radix); sb.Insert(0, "0123456789abcdefghijklmnopqrstuvwxyz"[d]); b /= radix; }
            return (neg ? "-" : "") + sb;
        }

        // ---------- sistema: comandi, cartelle e file ----------

        string FullPath(string p)
        {
            p = Environment.ExpandEnvironmentVariables(p ?? "");
            if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\")) p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            return Path.GetFullPath(Path.Combine(Cwd, p));
        }

        void Shell()
        {
            Def("cd", 0, 1, a =>
            {
                string dir = FullPath(a.Length > 0 ? Str(a[0]) : "~");
                if (!Directory.Exists(dir)) throw new LispError("cartella inesistente: " + dir);
                Cwd = dir;
                if (CwdChanged != null) CwdChanged(dir);
                return dir;
            });
            Def("pwd", 0, 0, a => Cwd);
            Def("ls", 0, 1, a =>
            {
                string dir = FullPath(a.Length > 0 ? Str(a[0]) : ".");
                if (!Directory.Exists(dir)) throw new LispError("cartella inesistente: " + dir);
                var items = Directory.GetDirectories(dir).Select(d => Path.GetFileName(d) + "/").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Concat(Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                return ToList(items.Select(x => (object)x).ToList(), null);
            });
            Def("directory", 0, 1, a => Call(Intern("ls"), a));
            Def("read-file", 1, 1, a => File.ReadAllText(FullPath(Str(a[0])), Encoding.UTF8));
            Def("cat", 1, 1, a => { Write(File.ReadAllText(FullPath(Str(a[0])), Encoding.UTF8)); FreshLine(); return null; });
            Def("read-lines", 1, 1, a => ToList(File.ReadAllLines(FullPath(Str(a[0])), Encoding.UTF8).Select(l => (object)l).ToList(), null));
            Def("write-file", 2, 2, a => { File.WriteAllText(FullPath(Str(a[0])), Printer.Print(a[1], false), new UTF8Encoding(false)); return a[0]; });
            Def("append-file", 2, 2, a => { File.AppendAllText(FullPath(Str(a[0])), Printer.Print(a[1], false), new UTF8Encoding(false)); return a[0]; });
            Def("file-exists-p", 1, 1, a => { string p = FullPath(Str(a[0])); return File.Exists(p) || Directory.Exists(p) ? (object)p : null; });
            Def("probe-file", 1, 1, a => { string p = FullPath(Str(a[0])); return File.Exists(p) || Directory.Exists(p) ? (object)p : null; });
            Def("directory-p", 1, 1, a => Bool(Directory.Exists(FullPath(Str(a[0])))));
            Def("mkdir", 1, 1, a => { var p = FullPath(Str(a[0])); Directory.CreateDirectory(p); return p; });
            Def("delete-file", 1, 1, a => { File.Delete(FullPath(Str(a[0]))); return T; });
            Def("file-size", 1, 1, a => new FileInfo(FullPath(Str(a[0]))).Length);
            Def("getenv", 1, 1, a => Environment.GetEnvironmentVariable(Str(a[0])));
            Def("setenv", 2, 2, a => { Environment.SetEnvironmentVariable(Str(a[0]), a[1] == null ? null : Str(a[1])); return a[1]; });
            Def("load", 1, -1, a =>
            {
                string p = FullPath(Str(a[0]));
                if (!File.Exists(p) && File.Exists(p + ".lisp")) p += ".lisp";
                EvalString(File.ReadAllText(p, Encoding.UTF8));
                return T;
            });
            // (sh "dir /b") mostra l'output mentre arriva e restituisce il codice di uscita
            Def("sh", 1, 1, a => (long)RunProcess("cmd.exe", "/d /s /c \"chcp 65001 >nul & " + Str(a[0]) + "\"", true, null));
            Def("pwsh", 1, 1, a => (long)RunProcess(PowerShellExe(), "-NoLogo -NoProfile -NonInteractive -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " + Str(a[0]).Replace("\"", "\\\"") + "\"", true, null));
            // (run "git status") restituisce l'output come stringa, senza mostrarlo
            Def("run", 1, 1, a =>
            {
                var sb = new StringBuilder();
                RunProcess("cmd.exe", "/d /s /c \"chcp 65001 >nul & " + Str(a[0]) + "\"", false, sb);
                return sb.ToString().TrimEnd('\r', '\n');
            });
            Def("run-lines", 1, 1, a =>
            {
                var sb = new StringBuilder();
                RunProcess("cmd.exe", "/d /s /c \"chcp 65001 >nul & " + Str(a[0]) + "\"", false, sb);
                return ToList(sb.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(l => (object)l).ToList(), null);
            });
            Def("clear", 0, 0, a => { Write("\x1b[2J\x1b[3J\x1b[H"); AtLineStart = true; return null; });
            Def("exit", 0, 1, a => { throw new LispExit(); });
            Def("quit", 0, 1, a => { throw new LispExit(); });
            Def("apropos", 1, 1, a =>
            {
                string q = Str(a[0]).ToLowerInvariant();
                var names = AllSymbols().Where(s => s.Name.Contains(q) && (s.Function != null || s.Value != Sym.Unbound)).Select(s => s.Name).OrderBy(n => n).ToList();
                FreshLine();
                Write(string.Join("  ", names) + "\n");
                return null;
            });
            Def("help", 0, 0, a => { FreshLine(); Write(HelpText()); return null; });
        }

        static string PowerShellExe()
        {
            var s = ShellInfo.Find("pwsh") ?? ShellInfo.Find("powershell");
            return s != null ? s.Exe : "powershell.exe";
        }

        int RunProcess(string exe, string args, bool show, StringBuilder capture, StringBuilder errCapture = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Cwd
            };
            using (var p = new Process { StartInfo = psi })
            {
                var gate = new object();
                DataReceivedEventHandler h = (s, e) =>
                {
                    if (e.Data == null) return;
                    lock (gate)
                    {
                        if (capture != null) capture.Append(e.Data).Append('\n');
                        if (show) Write(e.Data + "\n");
                    }
                };
                p.OutputDataReceived += h;
                if (errCapture == null) p.ErrorDataReceived += h;
                else p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) errCapture.Append(e.Data).Append('\n'); };
                p.Start();
                p.StandardInput.Close();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(100))
                {
                    if (Interrupt)
                    {
                        try { Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(3000); } catch { }
                        throw new LispInterrupted();
                    }
                }
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ---------- stream ----------

        Sym stdOut;   // *standard-output*: t (il terminale) o uno stream

        LispStream OutStream(object stream)
        {
            if (stream == null || stream == T) stream = stdOut.Value;
            if (stream == null || stream == T || stream == Sym.Unbound) return null;
            var ls = stream as LispStream;
            if (ls == null) throw new LispError("non è uno stream: " + Printer.Print(stream, true));
            if (ls.W == null) throw new LispError("non è uno stream di uscita: " + ls);
            return ls;
        }

        void WriteTo(object stream, string s)
        {
            var ls = OutStream(stream);
            if (ls == null) Write(s); else ls.Write(s);
        }

        // Va a capo se non si è già all'inizio della riga; dice se l'ha fatto
        bool FreshLineTo(object stream)
        {
            var ls = OutStream(stream);
            bool atStart = ls == null ? AtLineStart : ls.AtLineStart;
            if (!atStart) WriteTo(stream, "\n");
            return !atStart;
        }

        static LispStream InStream(object stream, string who)
        {
            var ls = stream as LispStream;
            if (ls == null || ls.R == null) throw new LispError(who + ": non è uno stream di ingresso: " + Printer.Print(stream, true));
            if (ls.Closed) throw new LispError(who + ": lo stream è chiuso: " + ls.Name);
            return ls;
        }

        // Fine dello stream: a[i] = eof-error-p (se manca, errore), a[i+1] = il valore da restituire
        static object AtEof(object[] a, int i, string who)
        {
            if (a.Length <= i || a[i] != null) throw new LispError(who + ": fine dello stream");
            return a.Length > i + 1 ? a[i + 1] : null;
        }

        LispStream Open(object[] a)
        {
            string p = FullPath(Str(a[0]));
            var dirArg = KeyArg(a, 1, ":direction", null);
            string dir = dirArg == null ? "input" : Str(dirArg);
            object ine = KeyArg(a, 1, ":if-does-not-exist", Sym.Unbound);
            bool exists = File.Exists(p);
            if (dir == "output" || dir == "io")
            {
                object ie = KeyArg(a, 1, ":if-exists", Sym.Unbound);
                string ifExists = ie == Sym.Unbound ? "error" : ie == null ? "nil" : Str(ie);
                if (exists && ifExists == "nil") return null;
                if (exists && ifExists == "error") throw new LispError("il file esiste già: " + p + " (usa :if-exists :supersede o :append)");
                if (!exists && ine == null) return null;
                if (!exists && ine is Sym && Str(ine) == "error") throw new LispError("file inesistente: " + p);
                var w = new StreamWriter(p, exists && ifExists == "append", new UTF8Encoding(false));
                return new LispStream { W = w, File = w, Name = p };
            }
            if (dir != "input" && dir != "probe") throw new LispError("open: :direction può essere :input, :output o :io, non " + dir);
            if (!exists)
            {
                if (ine == null) return null;
                throw new LispError("file inesistente: " + p);
            }
            if (dir == "probe") return new LispStream { Name = p, Closed = true };
            var r = new StreamReader(p, Encoding.UTF8, true);
            return new LispStream { R = r, File = r, Name = p };
        }

        // ---------- tipi ----------

        bool TypeP(object x, object type)
        {
            var tc = type as Cons;
            if (tc != null)
            {
                string h = Str(tc.Car);
                var args = ListItems(tc.Cdr);
                switch (h)
                {
                    case "or": return args.Any(t => TypeP(x, t));
                    case "and": return args.All(t => TypeP(x, t));
                    case "not": return args.Count == 1 && !TypeP(x, args[0]);
                    case "member": return args.Any(v => IsEql(v, x));
                    case "eql": return args.Count == 1 && IsEql(args[0], x);
                    case "satisfies": return args.Count == 1 && Truthy(Call(args[0], x));
                    case "mod": return (x is long || x is BigInteger) && Num.Compare(x, 0L) >= 0 && Num.Compare(x, args[0]) < 0;
                    case "integer": case "float": case "double-float": case "single-float": case "real": case "number": case "rational":
                        if (!TypeP(x, tc.Car)) return false;
                        return InRange(x, args.Count > 0 ? args[0] : null, true) && InRange(x, args.Count > 1 ? args[1] : null, false);
                    case "unsigned-byte": case "signed-byte": case "string": case "simple-string": case "vector": case "simple-vector":
                    case "array": case "simple-array": case "cons":
                        return TypeP(x, tc.Car);   // dimensioni e tipi degli elementi non si controllano
                    case "function": return x is LispFn;
                }
                throw new LispError("tipo non riconosciuto: " + Printer.Print(type, true));
            }
            string want = Str(type);
            switch (want)
            {
                case "t": return true;
                case "nil": return false;
                case "null": return x == null;
                case "integer": return x is long || x is BigInteger;
                case "fixnum": return x is long;
                case "bignum": return x is BigInteger;
                case "rational": return x is long || x is BigInteger;
                case "float": case "double-float": case "single-float": case "short-float": case "long-float": return x is double;
                case "number": case "real": return Num.Is(x);
                case "bit": return x is long && ((long)x == 0 || (long)x == 1);
                case "unsigned-byte": return (x is long || x is BigInteger) && Num.Compare(x, 0L) >= 0;
                case "signed-byte": return x is long || x is BigInteger;
                case "string": case "simple-string": case "base-string": return x is string;
                case "character": case "base-char": case "standard-char": return x is char;
                case "symbol": return x == null || x is Sym;
                case "keyword": return x is Sym && ((Sym)x).IsKeyword;
                case "boolean": return x == null || x == T;
                case "cons": return x is Cons;
                case "list": return x == null || x is Cons;
                case "atom": return !(x is Cons);
                case "vector": case "array": case "simple-array": return x is object[] || x is string;
                case "simple-vector": return x is object[];
                case "sequence": return x == null || x is Cons || x is string || x is object[];
                case "function": case "compiled-function": return x is LispFn;
                case "hash-table": return x is Dictionary<object, object>;
                case "stream": return x is LispStream;
                case "structure-object": return x is LispStruct;
                case "condition": case "error": case "simple-error": return x is LispCondition;
            }
            return TypeName(x) == want;   // nome di una struttura (defstruct)
        }

        // Estremo di un intervallo di tipo: * o assente = nessun limite, (n) = escluso
        static bool InRange(object x, object bound, bool lower)
        {
            if (bound == null || (bound is Sym && ((Sym)bound).Name == "*")) return true;
            bool exclusive = bound is Cons;
            if (exclusive) bound = ((Cons)bound).Car;
            int cmp = Num.Compare(x, bound);
            return lower ? (exclusive ? cmp > 0 : cmp >= 0) : (exclusive ? cmp < 0 : cmp <= 0);
        }

        // ---------- insiemi e alberi ----------

        // :test e :key per gli insiemi: la chiave si applica a entrambi gli elementi
        Func<object, object, bool> SetTest(object[] a, int from)
        {
            var test = KeyArg(a, from, ":test", null);
            var k = KeyFn(a, from);
            if (test == null) return (x, y) => { var kx = k(x); var ky = k(y); return IsEql(kx, ky) || (kx is string && IsEqual(kx, ky)); };
            return (x, y) => Truthy(Call(test, k(x), k(y)));
        }

        static object SubstTree(object tree, Func<object, bool> match, Func<object, object> replacement)
        {
            if (match(tree)) return replacement(tree);
            var c = tree as Cons;
            if (c == null) return tree;
            return new Cons(SubstTree(c.Car, match, replacement), SubstTree(c.Cdr, match, replacement));
        }

        bool TreeEqual(object x, object y, Func<object, object, bool> test)
        {
            var cx = x as Cons; var cy = y as Cons;
            if (cx != null || cy != null) return cx != null && cy != null && TreeEqual(cx.Car, cy.Car, test) && TreeEqual(cx.Cdr, cy.Cdr, test);
            return test(x, y);
        }

        // Funzioni aggiunte dopo: insiemi, liste, stream e file
        void MoreBuiltins()
        {
            stdOut = Intern("*standard-output*");
            stdOut.Value = T; stdOut.Special = true;

            // ---- insiemi ----
            Def("union", 2, -1, a =>
            {
                var t = SetTest(a, 2); var l2 = ListItems(a[1]);
                return ToList(ListItems(a[0]).Where(x => !l2.Any(y => t(x, y))).Concat(l2).ToList(), null);
            });
            Def("intersection", 2, -1, a => { var t = SetTest(a, 2); var l2 = ListItems(a[1]); return ToList(ListItems(a[0]).Where(x => l2.Any(y => t(x, y))).ToList(), null); });
            Def("set-difference", 2, -1, a => { var t = SetTest(a, 2); var l2 = ListItems(a[1]); return ToList(ListItems(a[0]).Where(x => !l2.Any(y => t(x, y))).ToList(), null); });
            Def("set-exclusive-or", 2, -1, a =>
            {
                var t = SetTest(a, 2); var l1 = ListItems(a[0]); var l2 = ListItems(a[1]);
                return ToList(l1.Where(x => !l2.Any(y => t(x, y))).Concat(l2.Where(y => !l1.Any(x => t(x, y)))).ToList(), null);
            });
            Def("subsetp", 2, -1, a => { var t = SetTest(a, 2); var l2 = ListItems(a[1]); return Bool(ListItems(a[0]).All(x => l2.Any(y => t(x, y)))); });
            Def("adjoin", 2, -1, a => { var t = SetTest(a, 2); return ListItems(a[1]).Any(y => t(a[0], y)) ? a[1] : new Cons(a[0], a[1]); });
            Alias("nunion", "union"); Alias("nintersection", "intersection"); Alias("nset-difference", "set-difference"); Alias("nset-exclusive-or", "set-exclusive-or");

            // ---- liste e alberi ----
            Def("member-if", 2, -1, a => { var k = KeyFn(a, 2); for (object l = a[1]; l is Cons; l = ((Cons)l).Cdr) if (Truthy(Call(a[0], k(((Cons)l).Car)))) return l; return null; });
            Def("member-if-not", 2, -1, a => { var k = KeyFn(a, 2); for (object l = a[1]; l is Cons; l = ((Cons)l).Cdr) if (!Truthy(Call(a[0], k(((Cons)l).Car)))) return l; return null; });
            Def("assoc-if", 2, -1, a => { var k = KeyFn(a, 2); return ListItems(a[1]).OfType<Cons>().FirstOrDefault(c => Truthy(Call(a[0], k(c.Car)))); });
            Def("assoc-if-not", 2, -1, a => { var k = KeyFn(a, 2); return ListItems(a[1]).OfType<Cons>().FirstOrDefault(c => !Truthy(Call(a[0], k(c.Car)))); });
            Def("rassoc-if", 2, -1, a => { var k = KeyFn(a, 2); return ListItems(a[1]).OfType<Cons>().FirstOrDefault(c => Truthy(Call(a[0], k(c.Cdr)))); });
            Def("rassoc-if-not", 2, -1, a => { var k = KeyFn(a, 2); return ListItems(a[1]).OfType<Cons>().FirstOrDefault(c => !Truthy(Call(a[0], k(c.Cdr)))); });
            Def("copy-alist", 1, 1, a => ToList(ListItems(a[0]).Select(e => e is Cons ? new Cons(((Cons)e).Car, ((Cons)e).Cdr) : e).ToList(), null));
            Def("subst", 3, -1, a => { var t = TestOf(a, 3); return SubstTree(a[2], x => t(a[1], x), x => a[0]); });
            Def("subst-if", 3, -1, a => { var k = KeyFn(a, 3); return SubstTree(a[2], x => Truthy(Call(a[1], k(x))), x => a[0]); });
            Def("subst-if-not", 3, -1, a => { var k = KeyFn(a, 3); return SubstTree(a[2], x => !Truthy(Call(a[1], k(x))), x => a[0]); });
            Def("sublis", 2, -1, a =>
            {
                var t = TestOf(a, 2);
                var alist = ListItems(a[0]).OfType<Cons>().ToList();
                Func<object, Cons> find = x => alist.FirstOrDefault(e => t(e.Car, x));
                return SubstTree(a[1], x => find(x) != null, x => find(x).Cdr);
            });
            Alias("nsubst", "subst"); Alias("nsubst-if", "subst-if"); Alias("nsubst-if-not", "subst-if-not"); Alias("nsublis", "sublis");
            Def("tree-equal", 2, -1, a =>
            {
                var test = KeyArg(a, 2, ":test", null);
                return Bool(TreeEqual(a[0], a[1], test == null ? (Func<object, object, bool>)IsEql : (x, y) => Truthy(Call(test, x, y))));
            });
            // mapl e mapcon: come maplist, ma sulle code delle liste; mapl restituisce la prima lista, mapcon unisce i risultati
            Func<object[], List<object>> mapTails = a =>
            {
                var tails = a.Skip(1).ToArray();
                var results = new List<object>();
                while (tails.All(t => t is Cons))
                {
                    results.Add(Call(a[0], tails));
                    for (int i = 0; i < tails.Length; i++) tails[i] = ((Cons)tails[i]).Cdr;
                }
                return results;
            };
            Def("mapl", 2, -1, a => { mapTails(a); return a[1]; });
            Def("mapcon", 2, -1, a => ToList(mapTails(a).SelectMany(ListItems).ToList(), null));

            // ---- stream e file ----
            Def("make-string-output-stream", 0, -1, a => new LispStream { W = new StringWriter(), Name = "string-output" });
            Def("get-output-stream-string", 1, 1, a =>
            {
                var ls = a[0] as LispStream;
                var sw = ls != null ? ls.W as StringWriter : null;
                if (sw == null) throw new LispError("get-output-stream-string: serve uno stream fatto da make-string-output-stream");
                string s = sw.ToString();
                sw.GetStringBuilder().Clear();
                return s;
            });
            Def("make-string-input-stream", 1, 3, a =>
            {
                string s = Str(a[0]);
                int start = a.Length > 1 && a[1] != null ? Num.ToInt(a[1]) : 0;
                int end = a.Length > 2 && a[2] != null ? Num.ToInt(a[2]) : s.Length;
                return new LispStream { R = new StringReader(s.Substring(start, end - start)), Name = "string-input" };
            });
            Def("open", 1, -1, a => Open(a));
            Def("close", 1, -1, a => { var ls = a[0] as LispStream; if (ls == null) return null; ls.Close(); return T; });
            Def("streamp", 1, 1, a => Bool(a[0] is LispStream));
            Def("input-stream-p", 1, 1, a => Bool(a[0] is LispStream && ((LispStream)a[0]).R != null));
            Def("output-stream-p", 1, 1, a => Bool(a[0] is LispStream && ((LispStream)a[0]).W != null));
            Def("open-stream-p", 1, 1, a => Bool(a[0] is LispStream && !((LispStream)a[0]).Closed));
            Def("read-char", 0, -1, a =>
            {
                if (a.Length == 0 || a[0] == null || a[0] == T)
                {
                    // dalla tastiera: si legge una riga e se ne prende il primo carattere
                    if (ReadLineHook == null) throw new LispError("read-char: nessun input disponibile");
                    var line = ReadLineHook();
                    if (line == null) throw new LispInterrupted();
                    AtLineStart = true;
                    return line.Length > 0 ? line[0] : '\n';
                }
                int ch = InStream(a[0], "read-char").R.Read();
                return ch < 0 ? AtEof(a, 1, "read-char") : (object)(char)ch;
            });
            Def("peek-char", 0, -1, a =>
            {
                var ls = InStream(a.Length > 1 ? a[1] : null, "peek-char");
                if (a.Length > 0 && a[0] == T) while (ls.R.Peek() >= 0 && char.IsWhiteSpace((char)ls.R.Peek())) ls.R.Read();
                int ch = ls.R.Peek();
                return ch < 0 ? AtEof(a, 2, "peek-char") : (object)(char)ch;
            });
            Def("unread-char", 1, 2, a =>
            {
                var ls = InStream(a.Length > 1 ? a[1] : null, "unread-char");
                ls.R = new StringReader(Chr(a[0]) + ls.R.ReadToEnd());
                return null;
            });
            Def("listen", 0, 1, a => Bool(a.Length > 0 && a[0] is LispStream && ((LispStream)a[0]).R != null && ((LispStream)a[0]).R.Peek() >= 0));
            Def("loop-finish", 0, 0, a => { throw new LoopEnd(); });
        }

        void Alias(string name, string existing) { Intern(name).Function = Intern(existing).Function; }

        // Funzioni scritte in Lisp
        void Prelude()
        {
            EvalString(@"
(defmacro assert (test &optional places msg) `(unless ,test (error ,(or msg ""asserzione fallita: ~s"") ',test)))
(defun string-empty-p (s) (= (length s) 0))
(defun flatten (x) (cond ((null x) nil) ((atom x) (list x)) (t (append (flatten (car x)) (flatten (cdr x))))))
(defun compose (&rest fns) (lambda (x) (reduce (lambda (acc f) (funcall f acc)) (reverse fns) :initial-value x)))
(defun alist-get (key alist) (cdr (assoc key alist :test #'equal)))
(defmacro typecase (keyform &rest clauses)
  (let ((k (gensym)))
    `(let ((,k ,keyform))
       (cond ,@(mapcar (lambda (c) (list* (if (member (car c) '(t otherwise)) t (list 'typep k (list 'quote (car c)))) nil (cdr c))) clauses)))))
(defmacro etypecase (keyform &rest clauses)
  (let ((k (gensym)))
    `(let ((,k ,keyform))
       (typecase ,k ,@clauses (t (error ""etypecase: ~s non è di nessuno dei tipi ~s"" ,k ',(mapcar #'car clauses)))))))
(defmacro ctypecase (&rest args) `(etypecase ,@args))
(defmacro check-type (place type &optional desc)
  `(unless (typep ,place ',type) (error ""~a vale ~s, che non è di tipo ~a"" ',place ,place ,(or desc (list 'quote type)))))
(defmacro rotatef (&rest places)
  (when (cdr places)
    (let ((tmp (gensym)) (pairs nil))
      (loop for tail on (append places (list tmp)) while (cdr tail) do (push (car tail) pairs) (push (cadr tail) pairs))
      `(let ((,tmp ,(car places))) (setf ,@(nreverse pairs)) nil))))
(defmacro shiftf (&rest args)
  (let ((tmp (gensym)) (pairs nil))
    (loop for tail on args while (cdr tail) do (push (car tail) pairs) (push (cadr tail) pairs))
    `(let ((,tmp ,(car args))) (setf ,@(nreverse pairs)) ,tmp)))
(defmacro with-output-to-string (spec &body body)
  `(let ((,(car spec) (make-string-output-stream))) ,@body (get-output-stream-string ,(car spec))))
(defmacro with-input-from-string (spec &body body)
  `(let ((,(car spec) (make-string-input-stream ,(cadr spec)))) ,@body))
(defmacro with-open-file (spec &body body)
  (let ((v (car spec)))
    `(let ((,v (open ,@(cdr spec)))) (unwind-protect (progn ,@body) (when ,v (close ,v))))))
(defmacro with-open-stream (spec &body body)
  `(let ((,(car spec) ,(cadr spec))) (unwind-protect (progn ,@body) (close ,(car spec)))))
");
        }

        public string HelpText()
        {
            return L.T(
@"CorbolPrompt Lisp: un piccolo Common Lisp. Esempi:
  (+ 1 2 3)                         (defun quadrato (x) (* x x))
  (mapcar #'quadrato '(1 2 3))      (loop for i from 1 to 5 collect (* i i))
  (format t ""Ciao ~a!~%"" ""mondo"")    (let ((h (make-hash-table))) (setf (gethash 'a h) 1) h)
  (defstruct punto x y)             (dolist (f (ls)) (print f))
Sistema:
  (sh ""dir"")  esegue con cmd e mostra l'output     (pwsh ""Get-Date"")  con PowerShell
  (run ""git status"")  restituisce l'output come stringa    (run-lines ""..."") come lista di righe
  (cd ""..""), (pwd), (ls), (read-file ""f.txt""), (write-file ""f.txt"" ""testo""), (load ""script.lisp"")
Altro: (apropos ""str"") cerca funzioni, (time (forma)), (clear), (exit).
Tasti: frecce su/giù = cronologia, Tab = completa, Ctrl+C = interrompe, Ctrl+L = pulisce lo schermo.
",
@"CorbolPrompt Lisp: a small Common Lisp. Examples:
  (+ 1 2 3)                         (defun square (x) (* x x))
  (mapcar #'square '(1 2 3))        (loop for i from 1 to 5 collect (* i i))
  (format t ""Hello ~a!~%"" ""world"")   (let ((h (make-hash-table))) (setf (gethash 'a h) 1) h)
  (defstruct point x y)             (dolist (f (ls)) (print f))
System:
  (sh ""dir"")  runs with cmd and shows the output   (pwsh ""Get-Date"")  with PowerShell
  (run ""git status"")  returns the output as a string    (run-lines ""..."") as a list of lines
  (cd ""..""), (pwd), (ls), (read-file ""f.txt""), (write-file ""f.txt"" ""text""), (load ""script.lisp"")
More: (apropos ""str"") finds functions, (time (form)), (clear), (exit).
Keys: up/down arrows = history, Tab = complete, Ctrl+C = interrupt, Ctrl+L = clear the screen.
");
        }
    }
}
