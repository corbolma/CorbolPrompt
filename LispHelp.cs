// Aiuto mentre si scrive nel REPL Lisp: gli argomenti delle funzioni e delle forme speciali
// (la riga grigia sotto il prompt) e il nome da suggerire mentre si scrive un simbolo.
// Le funzioni scritte in Lisp (defun, defmacro, lambda) hanno gli argomenti presi dalla definizione;
// per quelle fatte in C# c'è l'elenco qui sotto, e per quelle che mancano si usa il numero di argomenti.
// Compatibile con il compilatore csc di .NET Framework 4.x (C# 5).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CorbolPrompt
{
    partial class Interp
    {
        // nome, poi gli argomenti; i gruppi tra parentesi valgono come un argomento solo
        const string SignatureText = @"
quote object
if test then &optional else
progn &body forms
let ((var value)...) &body body
let* ((var value)...) &body body
flet ((name (args) body)...) &body body
labels ((name (args) body)...) &body body
cond (test form...)...
case keyform (key form...)...
ecase keyform (key form...)...
and &rest forms
or &rest forms
when test &body forms
unless test &body forms
lambda (args) &body body
function name
setq var value ...
setf place value ...
psetq var value ...
psetf place value ...
incf place &optional delta
decf place &optional delta
push item place
pushnew item place &key test key
pop place
defun name (args) &body body
defmacro name (args) &body body
defvar name &optional value doc
defparameter name value &optional doc
defconstant name value &optional doc
defstruct name &rest slots
block name &body body
return-from name &optional value
return &optional value
catch tag &body body
throw tag value
unwind-protect form &body cleanup
ignore-errors &body forms
handler-case form (type (var) form...)...
dolist (var list &optional result) &body body
dotimes (var count &optional result) &body body
do ((var init step)...) (end-test result...) &body body
loop &rest clauses
prog1 first &body forms
time form
multiple-value-bind (vars...) values-form &body body
destructuring-bind (args) expression &body body
progv symbols values &body body
declaim &rest declarations
ask question &optional data
ask-data request &optional data &key fields
ai-source name
why
json-parse text
json-encode data &key pretty
read-json file
ps command &key depth all
take n rows
drop n rows
sort-by key rows &key desc
group-by key rows
count-by key rows
sum-by key rows
select &rest keys-then-rows
filter predicate rows
plist-keys plist
plistp x
table &rest columns-then-rows
bar &optional label-key value-key data
hist &rest key-options-then-data
sparkline &optional key data
+ &rest numbers
* &rest numbers
- number &rest numbers
/ number &rest numbers
1+ number
1- number
= number &rest numbers
< number &rest numbers
> number &rest numbers
<= number &rest numbers
>= number &rest numbers
/= number &rest numbers
min number &rest numbers
max number &rest numbers
abs number
mod number divisor
rem number divisor
expt base power
sqrt number
isqrt number
exp number
log number &optional base
sin radians
cos radians
tan radians
atan y &optional x
float number &optional prototype
gcd &rest integers
lcm &rest integers
random limit
parse-integer string &key start end radix junk-allowed
parse-number string
eq x y
eql x y
equal x y
equalp x y
typep object type
type-of object
cons car cdr
list &rest objects
list* object &rest objects
make-list size &key initial-element
nth n list
nthcdr n list
elt sequence index
last list &optional n
butlast list &optional n
append &rest lists
nconc &rest lists
member item list &key test key
assoc item alist &key test key
rassoc item alist &key test key
acons key value alist
pairlis keys values &optional alist
getf plist indicator &optional default
rplaca cons object
rplacd cons object
range end-or-start &optional end step
iota count &optional start step
funcall function &rest args
apply function &rest args
mapcar function list &rest lists
mapc function list &rest lists
mapcan function list &rest lists
maplist function list
mapl function list &rest lists
mapcon function list &rest lists
map result-type function sequence &rest sequences
reduce function sequence &key initial-value from-end key
remove-if predicate sequence &key key count
remove-if-not predicate sequence &key key count
delete-if predicate sequence &key key count
remove item sequence &key test key count
delete item sequence &key test key count
remove-duplicates sequence &key test key
find item sequence &key test key
find-if predicate sequence &key key
find-if-not predicate sequence &key key
position item sequence &key test key
position-if predicate sequence &key key
count item sequence &key test key
count-if predicate sequence &key key
every predicate sequence &rest sequences
some predicate sequence &rest sequences
notany predicate sequence &rest sequences
notevery predicate sequence &rest sequences
sort sequence predicate &key key
stable-sort sequence predicate &key key
subseq sequence start &optional end
concatenate result-type &rest sequences
coerce object result-type
vector &rest objects
make-array size &key initial-element initial-contents
aref array index
svref vector index
intern name &optional package
make-symbol name
gensym &optional prefix
set symbol value
char string index
schar string index
char= char &rest chars
char-equal char &rest chars
char< a b
char> a b
string-trim chars string
string-left-trim chars string
string-right-trim chars string
string= a b &key start1 end1 start2 end2
string-equal a b &key start1 end1 start2 end2
string/= a b
string< a b
string> a b
string<= a b
string>= a b
string-lessp a b
search needle sequence &key test start2
string-prefix-p prefix string
string-suffix-p suffix string
string-contains string substring
string-replace string old new
split-string string &optional separator
join list &optional separator
string-join list &optional separator
lines string
make-string size &key initial-element
write-to-string object &key escape
read-from-string string
make-hash-table &key test
gethash key table &optional default
remhash key table
clrhash table
hash-table-count table
maphash function table
hash-table-keys table
hash-table-values table
eval form
macroexpand-1 form
macroexpand form
constantly value
complement function
values &rest objects
error format-string &rest args
warn format-string &rest args
print object &optional stream
prin1 object &optional stream
princ object &optional stream
write object &key stream escape
pprint object &optional stream
terpri &optional stream
fresh-line &optional stream
write-line string &optional stream
write-string string &optional stream
write-char char &optional stream
format destination control-string &rest args
read-line &optional stream eof-error-p eof-value
read &optional stream eof-error-p eof-value
y-or-n-p &optional question
sleep seconds
cd &optional directory
ls &optional directory
directory &optional directory
read-file file
cat file
read-lines file
write-file file text
append-file file text
file-exists-p file
probe-file file
directory-p directory
mkdir directory
delete-file file
file-size file
getenv name
setenv name value
load file
sh command
pwsh command
run command
run-lines command
exit &optional code
quit &optional code
apropos text
union list1 list2 &key test key
intersection list1 list2 &key test key
set-difference list1 list2 &key test key
set-exclusive-or list1 list2 &key test key
subsetp list1 list2 &key test key
adjoin item list &key test key
member-if predicate list &key key
member-if-not predicate list &key key
assoc-if predicate alist &key key
assoc-if-not predicate alist &key key
rassoc-if predicate alist &key key
rassoc-if-not predicate alist &key key
subst new old tree &key test
subst-if new predicate tree &key key
subst-if-not new predicate tree &key key
sublis alist tree &key test
tree-equal x y &key test
make-string-input-stream string &optional start end
get-output-stream-string stream
open file &key direction if-exists if-does-not-exist
close stream
read-char &optional stream eof-error-p eof-value
peek-char &optional peek-type stream eof-error-p eof-value
unread-char char &optional stream
listen &optional stream
set-clipboard x
notify title &optional text
http-get url &key headers timeout
http-post url body &key headers timeout
http-json url &key body headers timeout
choose-file &key title filter save multiple
choose-folder &key title
";

        static Dictionary<string, string> signatures;

        static Dictionary<string, string> Signatures
        {
            get
            {
                if (signatures != null) return signatures;
                var d = new Dictionary<string, string>();
                foreach (var raw in SignatureText.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    int sp = line.IndexOf(' ');
                    d[sp < 0 ? line : line.Substring(0, sp)] = sp < 0 ? "" : line.Substring(sp + 1);
                }
                return signatures = d;
            }
        }

        // Gli argomenti di una funzione, di una macro o di una forma speciale come "(nome arg...)", o null se il nome non è noto
        public string Signature(string name)
        {
            string args;
            Sym s = null;
            lock (symbols) symbols.TryGetValue(name, out s);
            var f = s != null ? s.Function : null;
            var m = f as Macro;
            var c = m != null ? m.Fn : f as Closure;
            // una funzione scritta in Lisp: gli argomenti della definizione (anche se ridefinisce una predefinita)
            if (c != null) args = ClosureArgs(c);
            else if (Signatures.TryGetValue(name, out args)) { }
            else if (f is Builtin) args = ArityArgs((Builtin)f);
            else return null;
            return "(" + name + (args.Length > 0 ? " " + args : "") + ")";
        }

        static string ClosureArgs(Closure c)
        {
            var parts = new List<string>();
            parts.AddRange(c.Req.Select(p => p.Name));
            if (c.Opt.Length > 0) { parts.Add("&optional"); parts.AddRange(c.Opt.Select(p => p.Name)); }
            if (c.Rest != null) { parts.Add("&rest"); parts.Add(c.Rest.Name); }
            if (c.Keys.Length > 0) { parts.Add("&key"); parts.AddRange(c.Keys.Select(p => p.Name)); }
            return string.Join(" ", parts);
        }

        static string ArityArgs(Builtin b)
        {
            var parts = new List<string>();
            string[] names = { "x", "y", "z" };
            for (int i = 0; i < b.Min; i++) parts.Add(i < names.Length ? names[i] : "arg" + (i + 1));
            if (b.Max < 0) parts.Add("&rest args");
            else if (b.Max > b.Min)
            {
                parts.Add("&optional");
                for (int i = b.Min; i < b.Max; i++) parts.Add(i < names.Length ? names[i] : "arg" + (i + 1));
            }
            return string.Join(" ", parts);
        }

        // Divide gli argomenti al primo livello: "(var value)..." resta un pezzo solo
        static List<string> SplitArgs(string args)
        {
            var parts = new List<string>();
            int depth = 0, start = -1;
            for (int i = 0; i <= args.Length; i++)
            {
                char ch = i < args.Length ? args[i] : ' ';
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                if (ch == ' ' && depth == 0) { if (start >= 0) { parts.Add(args.Substring(start, i - start)); start = -1; } }
                else if (start < 0) start = i;
            }
            return parts;
        }

        // ---------- dove si trova il cursore ----------

        sealed class Frame
        {
            public bool NoCall;                              // '( `( #( { o simili: non è una chiamata
            public readonly List<string> Items = new List<string>();   // gli elementi già scritti; le liste sono "(...)"
        }

        // Il testo scritto fino al cursore, letto quanto basta: le liste aperte e i loro elementi.
        // partial è il simbolo che si sta scrivendo proprio al cursore ("" se dopo uno spazio).
        static List<Frame> OpenForms(string text, out string partial)
        {
            var frames = new List<Frame>();
            var tok = new StringBuilder();
            bool prefix = false;   // ' ` , #' o # appena prima: la lista che si apre non è una chiamata
            partial = "";
            Action endTok = () =>
            {
                if (tok.Length > 0 && frames.Count > 0) frames[frames.Count - 1].Items.Add(tok.ToString());
                tok.Clear();
            };
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '"')
                {
                    endTok();
                    int j = i + 1;
                    while (j < text.Length && text[j] != '"') { if (text[j] == '\\') j++; j++; }
                    if (j >= text.Length) { partial = null; return frames; }   // dentro una stringa: niente aiuto
                    if (frames.Count > 0) frames[frames.Count - 1].Items.Add("\"\"");
                    i = j + 1; prefix = false;
                    continue;
                }
                if (c == ';')
                {
                    endTok();
                    int j = text.IndexOf('\n', i);
                    if (j < 0) { partial = null; return frames; }   // dentro un commento
                    i = j + 1;
                    continue;
                }
                if (c == '#' && i + 1 < text.Length && text[i + 1] == '\\' && tok.Length == 0)
                {
                    // un carattere: #\a #\space #\(
                    int j = i + 3;
                    while (j < text.Length && IsSymbolChar(text[j])) j++;
                    if (frames.Count > 0) frames[frames.Count - 1].Items.Add("#\\");
                    i = Math.Min(j, text.Length); prefix = false;
                    continue;
                }
                if (c == '(' || c == '[' || c == '{')
                {
                    bool noCall = prefix || c == '{' || tok.Length > 0;   // tok: #fn( o #(
                    tok.Clear();
                    frames.Add(new Frame { NoCall = noCall });
                    prefix = false;
                    i++;
                    continue;
                }
                if (c == ')' || c == ']' || c == '}')
                {
                    endTok();
                    if (frames.Count > 0) frames.RemoveAt(frames.Count - 1);
                    if (frames.Count > 0) frames[frames.Count - 1].Items.Add("(...)");
                    prefix = false;
                    i++;
                    continue;
                }
                if (char.IsWhiteSpace(c)) { endTok(); prefix = false; i++; continue; }
                if (tok.Length == 0 && (c == '\'' || c == '`' || c == ','))
                {
                    prefix = true;
                    if (c == ',' && i + 1 < text.Length && text[i + 1] == '@') i++;
                    i++;
                    continue;
                }
                if (tok.Length == 0 && c == '#' && i + 1 < text.Length && text[i + 1] == '\'') { prefix = true; i += 2; continue; }
                tok.Append(c);
                i++;
            }
            partial = tok.ToString();
            return frames;
        }

        static bool IsSymbolChar(char c) { return !char.IsWhiteSpace(c) && "()[]{}\"';`,".IndexOf(c) < 0; }

        // La riga d'aiuto per il cursore alla fine di text: la firma della chiamata più interna che ne ha una,
        // con l'argomento che si sta scrivendo indicato da hlStart/hlLen (hlLen = 0: nessuno).
        // guess: il nome suggerito per il simbolo che si sta scrivendo, se è il nome della funzione.
        public string ArgHint(string text, string guess, out int hlStart, out int hlLen)
        {
            hlStart = 0; hlLen = 0;
            string partial;
            var frames = OpenForms(text, out partial);
            if (partial == null) return null;
            for (int f = frames.Count - 1; f >= 0; f--)
            {
                var fr = frames[f];
                if (fr.NoCall) continue;
                bool inner = f == frames.Count - 1;
                string name;
                int arg;   // quale argomento si sta scrivendo, da 0; -1 = il nome della funzione
                string cur = inner ? partial : "(...)";
                if (fr.Items.Count == 0)
                {
                    if (!inner || partial.Length == 0) continue;
                    name = partial.ToLowerInvariant();
                    if (guess != null && Signature(name) == null) name = guess;
                    arg = -1;
                }
                else
                {
                    name = fr.Items[0].ToLowerInvariant();
                    arg = fr.Items.Count - 1;
                }
                string sig = Signature(name);
                if (sig == null) continue;
                if (arg >= 0) Highlight(sig, name, arg, fr.Items, cur, out hlStart, out hlLen);
                return sig;
            }
            return null;
        }

        // Trova nella firma il parametro che corrisponde all'argomento numero arg
        static void Highlight(string sig, string name, int arg, List<string> items, string cur, out int hlStart, out int hlLen)
        {
            hlStart = 0; hlLen = 0;
            string args = sig.Substring(name.Length + 1, sig.Length - name.Length - 2).TrimStart();
            int baseOff = name.Length + 2;
            var parts = SplitArgs(args);
            var offs = new List<int>();
            int at = 0;
            foreach (var p in parts) { at = args.IndexOf(p, at, StringComparison.Ordinal); offs.Add(at); at += p.Length; }

            var positional = new List<int>();
            string mode = "req";
            int keyStart = -1, restAt = -1;
            bool pairs = false;   // "var value ..." (setq, setf): le coppie si ripetono
            for (int k = 0; k < parts.Count; k++)
            {
                string p = parts[k];
                if (p == "&optional") { mode = "opt"; continue; }
                if (p == "&rest" || p == "&body") { mode = "rest"; continue; }
                if (p == "&key") { mode = "key"; keyStart = k + 1; continue; }
                if (mode == "rest") { if (restAt < 0) restAt = k; continue; }
                if (mode == "key") continue;
                if (p == "...") { pairs = true; continue; }
                positional.Add(k);
            }
            int target = -1;
            // la keyword che si sta scrivendo, o il valore di quella appena scritta (:test …)
            if (keyStart >= 0)
            {
                string kw = cur.StartsWith(":") ? cur.Substring(1) : arg >= 1 && items.Count > arg && items[arg].StartsWith(":") ? items[arg].Substring(1) : null;
                if (kw != null)
                    for (int k = keyStart; k < parts.Count && !parts[k].StartsWith("&"); k++)
                        if (parts[k].StartsWith(kw, StringComparison.OrdinalIgnoreCase)) { target = k; break; }
            }
            if (target < 0)
            {
                if (arg < positional.Count) target = positional[arg];
                else if (restAt >= 0) target = restAt;
                else if (pairs && positional.Count > 0) target = positional[arg % positional.Count];
                else if (positional.Count > 0 && parts[positional[positional.Count - 1]].EndsWith("...")) target = positional[positional.Count - 1];
            }
            if (target < 0) return;
            hlStart = baseOff + offs[target];
            hlLen = parts[target].Length;
        }

        // Il nome da suggerire per un simbolo che comincia con prefix: il più corto, poi in ordine alfabetico
        public string GuessSymbol(string prefix, IEnumerable<string> extra)
        {
            prefix = prefix.ToLowerInvariant();
            string best = null;
            foreach (var n in AllSymbols().Where(s => s.Function != null || s.Value != Sym.Unbound).Select(s => s.Name).Concat(extra ?? new string[0]))
            {
                if (n.Length <= prefix.Length || !n.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (best == null || n.Length < best.Length || (n.Length == best.Length && string.CompareOrdinal(n, best) < 0)) best = n;
            }
            return best;
        }
    }
}
