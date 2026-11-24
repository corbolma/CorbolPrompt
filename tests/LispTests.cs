// Test automatici del Lisp di CorbolPrompt.
// Si lanciano con test-lisp.bat, che compila questo file insieme ai sorgenti dell'app
// (non entra in CorbolPrompt.exe). Ogni controllo valuta un'espressione e confronta
// il risultato stampato (come lo stamperebbe il REPL) con quello atteso.
// Per aggiungere un controllo: Check("(espressione)", "risultato atteso");
// Un risultato atteso che comincia con "ERRORE: " vuol dire che ci si aspetta quell'errore.
using System;
using System.IO;
using System.Text;

namespace CorbolPrompt
{
    static class LispTests
    {
        static int fail, total;
        static Interp it;
        static StringBuilder term;
        static string section;

        static void Section(string name)
        {
            section = name;
            it = new Interp();
            term = new StringBuilder();
            it.Out = s => term.Append(s);
            it.Cwd = Path.GetTempPath();
        }

        static string Run(string code)
        {
            try { return Printer.Print(it.EvalString(code), true); }
            catch (Exception e) { it.ResetDepth(); return "ERRORE: " + e.Message; }
        }

        // expected == null: basta che non dia errore
        static void Check(string code, string expected)
        {
            total++;
            string got = Run(code);
            bool ok = expected == null ? !got.StartsWith("ERRORE: ") : got == expected;
            if (ok) return;
            fail++;
            Console.WriteLine("FALLITO [" + section + "] " + code.Replace("\n", " "));
            Console.WriteLine("    dato:   " + got.Replace("\n", "\\n"));
            if (expected != null) Console.WriteLine("    atteso: " + expected.Replace("\n", "\\n"));
        }

        // controlla ciò che il codice scrive sul terminale
        static void CheckOutput(string code, string expected)
        {
            term.Clear();
            string got = Run(code);
            total++;
            if (!got.StartsWith("ERRORE: ") && term.ToString() == expected) return;
            fail++;
            Console.WriteLine("FALLITO [" + section + "] output di " + code.Replace("\n", " "));
            Console.WriteLine("    dato:   " + (got.StartsWith("ERRORE: ") ? got : term.ToString().Replace("\n", "\\n")));
            Console.WriteLine("    atteso: " + expected.Replace("\n", "\\n"));
        }

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Base();
            Dynamic();
            Lists();
            Types();
            Places();
            Streams();
            Loop();
            Data();
            PowerShellData();
            Ai();
            GuideExamples();
            Console.WriteLine(fail == 0 ? "TUTTI OK (" + total + " controlli)" : fail + " FALLITI su " + total);
            return fail == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------------------
        static void Base()
        {
            Section("base");
            Check("(+ 1 2 3)", "6");
            Check("(* 1.5 2)", "3.0");
            Check("(expt 2 100)", "1267650600228229401496703205376");
            Check("(- (expt 2 64) (expt 2 64))", "0");
            Check("(list (floor 7 2) (mod -7 2) (round 2.5))", "(3 1 2)");
            Check("(defun fact (n) (if (<= n 1) 1 (* n (fact (- n 1))))) (fact 20)", "2432902008176640000");
            Check("(fact 25)", "15511210043330985984000000");
            Check("(let ((x 1)) (let ((x 2) (y x)) (list x y)))", "(2 1)");
            Check("(defmacro doppio (x) `(* 2 ,x)) (doppio (+ 1 2))", "6");
            Check("(format nil \"~a + ~s = ~d\" 1 \"due\" 3)", "\"1 + \\\"due\\\" = 3\"");
            Check("(string-upcase \"àbc\")", "\"ÀBC\"");
            Check("(let ((h (make-hash-table :test #'equal))) (setf (gethash \"k\" h) 5) (gethash \"k\" h))", "5");
            Check("(handler-case (error \"boom ~a\" 1) (error (e) (format nil \"preso: ~a\" e)))", "\"preso: boom 1\"");
            Check("(car 5)", "ERRORE: non è un cons: 5");
            Check("(defun count-down (n) (if (= n 0) 'fatto (count-down (- n 1)))) (count-down 300000)", "fatto");
        }

        // ---------------------------------------------------------------------------
        static void Dynamic()
        {
            Section("variabili dinamiche");
            Run("(defvar *x* 1) (defun get-x () *x*)");
            Check("(let ((*x* 2)) (get-x))", "2");
            Check("(get-x)", "1");
            Check("(let ((*x* 2)) (setq *x* 3) (get-x))", "3");
            Check("*x*", "1");
            Check("(progn (ignore-errors (let ((*x* 9)) (error \"boom\"))) *x*)", "1");
            Check("(catch 'k (let ((*x* 5)) (throw 'k (get-x))))", "5");
            Check("*x*", "1");
            Check("(let ((*x* 10) (y *x*)) y)", "1");
            Check("(let* ((*x* 10) (y *x*)) y)", "10");
            Check("(defun with-x (*x*) (get-x)) (with-x 42)", "42");
            Check("*x*", "1");
            Check("(mapcar #'with-x '(7 8))", "(7 8)");
            Check("(defun deep (n) (if (= n 0) (get-x) (let ((*x* n)) (deep (- n 1))))) (deep 3)", "1");
            Check("(defun deep2 (n) (let ((*x* (+ *x* 1))) (if (= n 0) (get-x) (deep2 (- n 1))))) (deep2 4)", "6");
            Check("*x*", "1");
            Check("(dolist (*x* '(a b)) (get-x)) *x*", "1");
            Check("(let ((acc nil)) (dolist (*x* '(a b)) (push (get-x) acc)) acc)", "(b a)");
            Check("(defparameter *y* 'g) (defun get-y () *y*) (funcall (lambda () (let ((*y* 'l)) (get-y))))", "l");
            Check("*y*", "g");
            Check("(let ((z 1)) (let ((f (lambda () z))) (let ((z 2)) (funcall f))))", "1");
            Check("(progv '(*x*) '(77) (get-x))", "77");
            Check("*x*", "1");
            Check("(declaim (special *w*)) (setq *w* 'top) (defun get-w () *w*) (let ((*w* 'in)) (get-w))", "in");
            Check("(defun loopy () (loop for *x* from 1 to 3 collect (get-x))) (loopy)", "(1 2 3)");
            Check("*x*", "1");
            Check("(handler-case (with-x 1 2) (error () *x*))", "1");
            Check("(defvar *x* 99) *x*", "1");
            Check("(defparameter *y* 'nuovo) *y*", "nuovo");
        }

        // ---------------------------------------------------------------------------
        static void Lists()
        {
            Section("liste e insiemi");
            Check("(sort (union '(1 2 3) '(2 3 4)) #'<)", "(1 2 3 4)");
            Check("(sort (intersection '(1 2 3) '(2 3 4)) #'<)", "(2 3)");
            Check("(set-difference '(1 2 3 4) '(2 4))", "(1 3)");
            Check("(sort (set-exclusive-or '(1 2 3) '(2 3 4)) #'<)", "(1 4)");
            Check("(subsetp '(1 2) '(3 2 1))", "t");
            Check("(subsetp '(1 5) '(3 2 1))", "nil");
            Check("(sort (union '(\"a\") '(\"a\" \"b\") :test #'string=) #'string<)", "(\"a\" \"b\")");
            Check("(intersection '((a 1) (b 2)) '((b 9)) :key #'car)", "((b 2))");
            Check("(adjoin 1 '(1 2))", "(1 2)");
            Check("(adjoin 0 '(1 2))", "(0 1 2)");
            Check("(adjoin '(a 5) '((a 1)) :key #'car)", "((a 1))");
            Check("(subst 'x 'b '(a b (c b)))", "(a x (c x))");
            Check("(subst-if 0 #'numberp '(a 1 (b 2)))", "(a 0 (b 0))");
            Check("(sublis '((a . 1) (b . 2)) '(a (b c)))", "(1 (2 c))");
            Check("(member-if #'evenp '(1 3 4 5))", "(4 5)");
            Check("(member-if-not #'oddp '(1 3 4 5))", "(4 5)");
            Check("(assoc-if #'evenp '((1 . a) (2 . b)))", "(2 . b)");
            Check("(rassoc-if #'stringp '((1 . a) (2 . \"b\")))", "(2 . \"b\")");
            Check("(mapcon (lambda (l) (list (length l))) '(a b c))", "(3 2 1)");
            Check("(let ((r nil)) (mapl (lambda (l) (push (car l) r)) '(1 2 3)) r)", "(3 2 1)");
            Check("(copy-alist '((a . 1)))", "((a . 1))");
            Check("(tree-equal '(1 (2 \"x\")) '(1 (2 \"x\")) :test #'equal)", "t");
            Check("(tree-equal '(1 (2 3)) '(1 (2 4)))", "nil");
        }

        // ---------------------------------------------------------------------------
        static void Types()
        {
            Section("tipi");
            Check("(mapcar (lambda (x) (typecase x (integer 'int) (string 'str) ((or symbol character) 'sym-chr) (t 'altro))) (list 1 \"a\" 'b #\\c 1.5))", "(int str sym-chr sym-chr altro)");
            Check("(typecase 5 (string 's))", "nil");
            Check("(typecase 5 (integer))", "nil");
            Check("(etypecase 5 (string 's))", "ERRORE: etypecase: 5 non è di nessuno dei tipi (string)");
            Check("(typep (expt 2 100) 'integer)", "t");
            Check("(typep 5 '(integer 0 10))", "t");
            Check("(typep 50 '(integer 0 10))", "nil");
            Check("(typep 5 '(integer 0 *))", "t");
            Check("(typep 'a '(member a b))", "t");
            Check("(typep 3 '(and number (not float)))", "t");
            Check("(typep nil 'boolean)", "t");
            Check("(typep '(1) 'cons)", "t");
            Check("(typep 1.0 'double-float)", "t");
            Check("(typep :k 'symbol)", "t");
            Check("(defstruct punto x y) (typep (make-punto) 'punto)", "t");
            Check("(let ((x 5)) (check-type x integer) x)", "5");
            Check("(let ((x \"a\")) (check-type x integer))", "ERRORE: x vale \"a\", che non è di tipo integer");
        }

        // ---------------------------------------------------------------------------
        static void Places()
        {
            Section("setf e scambi");
            Check("(let ((a 1) (b 2) (c 3)) (rotatef a b c) (list a b c))", "(2 3 1)");
            Check("(let ((v (vector 1 2))) (rotatef (aref v 0) (aref v 1)) v)", "#(2 1)");
            Check("(let ((a 1) (b 2)) (list (shiftf a b 9) a b))", "(1 2 9)");
            Check("(let ((a 1) (b 2)) (psetq a b b a) (list a b))", "(2 1)");
            Check("(let ((l (list 1 2))) (psetf (car l) (cadr l) (cadr l) (car l)) l)", "(2 1)");
            Check("(let ((l (list 1 2 3 4))) (setf (cadr l) 'b (caddr l) 'c (cddr l) '(z)) l)", "(1 b z)");
            Check("(let ((l (list 1 2 3 4 5))) (setf (fourth l) 40 (fifth l) 50) l)", "(1 2 3 40 50)");
            Check("(let ((l (list (list 1 2)))) (setf (cadar l) 9) l)", "((1 9))");
        }

        // ---------------------------------------------------------------------------
        static void Streams()
        {
            Section("stream e file");
            string tmp = Path.Combine(Path.GetTempPath(), "corbol-lisp-test-" + Guid.NewGuid().ToString("N") + ".txt").Replace("\\", "/");
            try
            {
                Check("(with-output-to-string (s) (format s \"x=~a\" 1) (princ \"!\" s) (terpri s) (write-string \"fine\" s))", "\"x=1!\nfine\"");
                Check("(with-output-to-string (s) (print 'a s) (prin1 \"b\" s) (write-char #\\c s) (write-line \"d\" s) (fresh-line s) (fresh-line s))", "\"\na \\\"b\\\"cd\n\"");
                Check("(with-output-to-string (*standard-output*) (format t \"ciao\") (princ 1))", "\"ciao1\"");
                Check("(with-output-to-string (s) (write 'x :stream s))", "\"x\"");
                Check("(let ((s (make-string-output-stream))) (write-string \"ab\" s) (list (get-output-stream-string s) (get-output-stream-string s)))", "(\"ab\" \"\")");
                Check("(with-input-from-string (s \"uno\ndue\") (list (read-line s) (read-line s) (read-line s nil :fine)))", "(\"uno\" \"due\" :fine)");
                Check("(with-input-from-string (s \"ab\") (list (peek-char nil s) (read-char s) (read-char s) (read-char s nil nil)))", "(#\\a #\\a #\\b nil)");
                Check("(with-input-from-string (s \"(1 2) foo\") (list (read s) (read s) (read s nil :eof)))", "((1 2) foo :eof)");
                Check("(with-input-from-string (s \"\") (read-line s))", "ERRORE: read-line: fine dello stream");
                Check("(streamp (make-string-output-stream))", "t");
                Check("(with-open-file (f \"" + tmp + "\" :direction :output :if-exists :supersede) (format f \"riga 1~%\") (write-line \"riga 2\" f)) (read-file \"" + tmp + "\")", "\"riga 1\nriga 2\n\"");
                Check("(with-open-file (f \"" + tmp + "\" :direction :output :if-exists :append) (write-line \"riga 3\" f)) (with-open-file (f \"" + tmp + "\") (loop for l = (read-line f nil) while l collect l))", "(\"riga 1\" \"riga 2\" \"riga 3\")");
                Check("(with-open-file (f \"" + tmp + "\" :direction :output) (write-line \"x\" f))", "ERRORE: il file esiste già: " + Path.GetFullPath(tmp) + " (usa :if-exists :supersede o :append)");
                Check("(with-open-file (f \"" + tmp + ".nonce\" :if-does-not-exist nil) f)", "nil");
                Check("(with-open-file (f \"" + tmp + ".nonce\") f)", "ERRORE: file inesistente: " + Path.GetFullPath(tmp + ".nonce"));
                Check("(progn (ignore-errors (with-open-file (f \"" + tmp + "\" :direction :output :if-exists :supersede) (write-line \"parziale\" f) (error \"boom\"))) (read-file \"" + tmp + "\"))", "\"parziale\n\"");
                CheckOutput("(format t \"terminale\") (princ 1 t) (write-string \"!\")", "terminale1!");
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        // ---------------------------------------------------------------------------
        static void Loop()
        {
            Section("loop");
            Check("(loop for i from 1 to 5 collect (* i i))", "(1 4 9 16 25)");
            Check("(loop for x in '(1 2 3) sum x)", "6");
            Check("(loop for x in '(2 4 6) always (evenp x))", "t");
            Check("(loop for x in '(2 3 6) always (evenp x))", "nil");
            Check("(loop for x in '(1 3) never (evenp x))", "t");
            Check("(loop for x in '(1 4 5) thereis (and (evenp x) (* x 10)))", "40");
            Check("(loop for x in '(1 3) thereis (evenp x))", "nil");
            Check("(loop named esterno for i from 1 do (loop for j from 1 do (when (= (* i j) 6) (return-from esterno (list i j)))))", "(1 6)");
            Check("(loop for i from 1 collect i do (when (= i 3) (loop-finish)))", "(1 2 3)");
            Check("(loop for i from 1 do (when (> i 2) (loop-finish)) finally (return (* i 100)))", "300");
            Check("(let ((h (make-hash-table))) (setf (gethash 'a h) 1 (gethash 'b h) 2) (sort (loop for k being the hash-keys of h collect k) #'string<))", "(a b)");
            Check("(let ((h (make-hash-table))) (setf (gethash 'a h) 1 (gethash 'b h) 2) (loop for v being each hash-value in h sum v))", "3");
            Check("(let ((h (make-hash-table))) (setf (gethash 'a h) 1) (loop for k being the hash-keys of h using (hash-value v) collect (cons k v)))", "((a . 1))");
            Check("(let ((h (make-hash-table))) (setf (gethash nil h) 1) (loop for k being the hash-keys of h collect k))", "(nil)");
            Check("(handler-case (loop for i from 1 do (when (= i 2) (loop-finish))) (error () 'preso))", "nil");
            Check("(loop for x in '(1 2 3) for y = (* x x) collect y)", "(1 4 9)");
        }

        // ---------------------------------------------------------------------------
        static void Data()
        {
            Section("dati e JSON");
            // lettura del JSON: chiavi in kebab-case, array come liste
            Check("(json-parse \"{\\\"ProcessName\\\":\\\"x\\\",\\\"WorkingSet64\\\":123,\\\"WS(M)\\\":1.5,\\\"user_name\\\":\\\"\\\\u00e8\\\"}\")",
                "(:process-name \"x\" :working-set64 123 :ws-m 1.5 :user-name \"è\")");
            Check("(json-parse \"[1, -2.5e1, true, false, null, \\\"a\\\\nb\\\", [], {}]\")", "(1 -25.0 t nil nil \"a\nb\" nil nil)");
            Check("(json-parse \"123456789012345678901234567890\")", "123456789012345678901234567890");
            Check("(json-parse \"{\\\"PSComputerName\\\":1,\\\"CPU\\\":2,\\\"id\\\":3}\")", "(:ps-computer-name 1 :cpu 2 :id 3)");
            Check("(json-parse \"\\\"/Date(0)/\\\"\")", Printer.Print(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss"), true));
            Check("(json-parse \"[1, 2\")", "ERRORE: JSON non valido (manca ',' o ']', al carattere 6)");
            Check("(json-parse \"{\\\"a\\\":1} x\")", "ERRORE: JSON non valido (testo in più dopo la fine, al carattere 9)");
            // scrittura
            Check("(json-encode '(:name \"a\" :list (1 2.5 \"x\") :ok t :no :false :niente nil :sub (:x 1)))",
                "\"{\\\"name\\\":\\\"a\\\",\\\"list\\\":[1,2.5,\\\"x\\\"],\\\"ok\\\":true,\\\"no\\\":false,\\\"niente\\\":null,\\\"sub\\\":{\\\"x\\\":1}}\"");
            Check("(json-encode \"a\\\"b\\nc\")", "\"\\\"a\\\\\\\"b\\\\nc\\\"\"");
            Check("(json-encode (vector 1 'sym :kw))", "\"[1,\\\"sym\\\",\\\"kw\\\"]\"");
            Check("(json-encode '((:a 1) (:a 2)) :pretty t)", "\"[\n  {\n    \\\"a\\\": 1\n  },\n  {\n    \\\"a\\\": 2\n  }\n]\"");
            Check("(let ((h (make-hash-table))) (setf (gethash :x h) 1) (json-encode h))", "\"{\\\"x\\\":1}\"");
            Check("(defstruct pt x y) (json-encode (make-pt :x 1 :y 2))", "\"{\\\"x\\\":1,\\\"y\\\":2}\"");
            Check("(json-parse (json-encode '((:a 1 :b (2 3)) (:a \"z\"))))", "((:a 1 :b (2 3)) (:a \"z\"))");

            // keyword come funzioni
            Check("(:name '(:name \"pippo\" :id 3))", "\"pippo\"");
            Check("(:manca '(:name \"pippo\") 'predefinito)", "predefinito");
            Check("(mapcar :id '((:id 1) (:id 2)))", "(1 2)");
            Check("(let ((h (make-hash-table))) (setf (gethash :k h) 9) (:k h))", "9");
            Check("(:y (make-pt :x 1 :y 2))", "2");
            Check("(:a 5)", "ERRORE: :a: 5 non è una plist, una tabella hash o una struttura");
            Check("(sort (list '(:n 2) '(:n 1)) #'< :key :n)", "((:n 1) (:n 2))");

            // sintassi {…} e #fn(…)
            Check("{:a 1 :b (+ 1 1)}", "(:a 1 :b 2)");
            Check("{}", "nil");
            Check("(:b {:a 1 :b 2})", "2");
            Check("(funcall #fn(+ % 1) 41)", "42");
            Check("(funcall #fn(list %1 %2 %&) 1 2 3 4)", "(1 2 (3 4))");
            Check("(funcall #fn(list % %2) 1 2)", "(1 2)");
            Check("(funcall #fn(+ 1 2))", "3");
            Check("(mapcar #fn(* % %) '(1 2 3))", "(1 4 9)");
            Check("(list #f #t)", "(nil t)");
            Check("'(a}", "ERRORE: parentesi chiusa di troppo");

            // -> e ->>
            Check("(-> 5 (- 1) (* 10))", "40");
            Check("(->> 5 (- 1) (* 10))", "-40");
            Check("(-> '(:a (:b 7)) :a :b)", "7");
            Check("(->> '(5 3 8 1) (filter #'oddp) (mapcar #fn(* % 10)))", "(50 30 10)");
            Check("(macroexpand-1 '(->> x (f a) g))", "(g (f a x))");

            // funzioni sulle righe
            Run("(defparameter *r* '((:name \"b\" :cpu 10) (:name \"a\" :cpu 30) (:name \"c\" :cpu 20) (:name \"a\" :cpu 5)))");
            Check("(mapcar :cpu (sort-by :cpu *r*))", "(5 10 20 30)");
            Check("(mapcar :cpu (sort-by :cpu *r* :desc t))", "(30 20 10 5)");
            Check("(mapcar :cpu (sort-by :cpu :desc t *r*))", "(30 20 10 5)");
            Check("(mapcar :name (sort-by :name *r*))", "(\"a\" \"a\" \"b\" \"c\")");
            Check("(sort-by #'identity '(3 nil \"B\" 1 \"a\"))", "(nil 1 3 \"a\" \"B\")");
            Check("(mapcar :cpu *r*)", "(10 30 20 5)");   // sort-by non modifica i dati
            Check("(take 2 '(1 2 3))", "(1 2)");
            Check("(take 9 '(1 2))", "(1 2)");
            Check("(drop 2 '(1 2 3))", "(3)");
            Check("(take 2 \"ciao\")", "\"ci\"");
            Check("(count-by :name *r*)", "((\"a\" . 2) (\"b\" . 1) (\"c\" . 1))");
            Check("(mapcar #fn(cons (car %) (length (cdr %))) (group-by :name *r*))", "((\"b\" . 1) (\"a\" . 2) (\"c\" . 1))");
            Check("(sum-by :cpu *r*)", "65");
            Check("(sum-by :manca *r*)", "0");
            Check("(select :name *r*)", "((:name \"b\") (:name \"a\") (:name \"c\") (:name \"a\"))");
            Check("(select :cpu :name '(:name \"x\" :cpu 1 :altro 2))", "(:cpu 1 :name \"x\")");
            Check("(select \"name\" *r*)", "ERRORE: select: i campi vanno indicati come keyword (:nome), non \"name\"");
            Check("(plist-keys '(:a 1 :b 2))", "(:a :b)");
            Check("(list (plistp '(:a 1)) (plistp '(:a)) (plistp '(1 2)) (plistp nil))", "(t nil nil nil)");
            Check("(->> *r* (filter #fn(> (:cpu %) 8)) (sort-by :cpu :desc t) (take 2) (mapcar :name))", "(\"a\" \"c\")");

            // tabella
            CheckOutput("(table '((:name \"b\" :cpu 10) (:name \"aaa\" :cpu 5)))",
                "\x1b[1mname  cpu\x1b[0m\n\x1b[2m────  ───\x1b[0m\nb      10\naaa     5\n\x1b[2m(2 rows)\x1b[0m\n");
            CheckOutput("(table :cpu '((:name \"b\" :cpu 10)))", "\x1b[1mcpu\x1b[0m\n\x1b[2m───\x1b[0m\n 10\n");
            CheckOutput("(table '((1 \"x\") (22 \"yy\")))", " 1  x\n22  yy\n\x1b[2m(2 rows)\x1b[0m\n");
            CheckOutput("(table '((:d \"2026-11-24T09:04:21.757+01:00\" :v nil)))", "\x1b[1md                 v\x1b[0m\n\x1b[2m────────────────  ─\x1b[0m\n2026-11-24 09:04\n");
            CheckOutput("(table nil)", "\x1b[2m(no rows)\x1b[0m\n");
            CheckOutput("(table '((:a 1) nil (:b 2)))", "\x1b[1ma  b\x1b[0m\n\x1b[2m─  ─\x1b[0m\n1\n\n   2\n\x1b[2m(3 rows)\x1b[0m\n");

            // larghezza del terminale: si stringono le colonne larghe, poi si nascondono le ultime
            it.TermCols = 21;
            CheckOutput("(table '((:id 1 :testo \"una descrizione lunga\")))", "\x1b[1mid  testo\x1b[0m\n\x1b[2m──  ────────────────\x1b[0m\n 1  una descrizione…\n");
            it.TermCols = 12;
            CheckOutput("(table '((:aaaa 1 :bbbb 2 :cccc 3)))", "\x1b[1maaaa  bbbb\x1b[0m\n\x1b[2m────  ────\x1b[0m\n   1     2\n\x1b[2m(1 hidden columns: cccc)\x1b[0m\n");
            it.TermCols = 0;
            // table non fa stampare nil al REPL; i risultati normali sì
            Run("(table '((:a 1)))"); CheckValue("QuietResult dopo table", it.QuietResult, true);
            it.QuietResult = false; Run("(+ 1 2)"); CheckValue("QuietResult dopo (+ 1 2)", it.QuietResult, false);

            // tabella automatica del REPL: solo per liste di plist, tabelle hash o strutture
            CheckValue("AutoTable di (1 2)", it.AutoTable(it.EvalString("'(1 2)")), null);
            CheckValue("AutoTable di una plist sola", it.AutoTable(it.EvalString("'(:a 1 :b 2)")), null);
            CheckValue("AutoTable di nil", it.AutoTable(null), null);
            CheckValue("AutoTable di liste miste", it.AutoTable(it.EvalString("'((:a 1) 5)")), null);
            CheckValue("AutoTable di plist", it.AutoTable(it.EvalString("'((:a 1) (:a 22))")), "\x1b[1m a\x1b[0m\n\x1b[2m──\x1b[0m\n 1\n22\n\x1b[2m(2 rows)\x1b[0m\n");
            CheckValue("AutoTable di strutture", it.AutoTable(it.EvalString("(list (make-pt :x 1 :y 2))")), "\x1b[1mx  y\x1b[0m\n\x1b[2m─  ─\x1b[0m\n1  2\n");
            string many = (string)it.AutoTable(it.EvalString("(loop for i from 1 to 40 collect (list :n i))"));
            CheckValue("AutoTable di 40 righe", many.EndsWith("\n30\n\x1b[2m(showing 30 of 40 rows, all of them: (table *1))\x1b[0m\n"), true);
        }

        // ---------------------------------------------------------------------------
        // IA con un modello finto: risposte preparate in coda, richieste registrate
        static readonly System.Collections.Generic.Queue<string> replies = new System.Collections.Generic.Queue<string>();
        static readonly System.Collections.Generic.List<AiRequest> requests = new System.Collections.Generic.List<AiRequest>();

        static void Model(params string[] r) { replies.Clear(); requests.Clear(); foreach (var x in r) replies.Enqueue(x); }
        static string LastUser() { var m = requests[requests.Count - 1].Messages; return m[m.Count - 1].Text; }

        static void Ai()
        {
            Section("IA (modello finto)");
            it.AiHook = req => { requests.Add(req); return replies.Count > 0 ? replies.Dequeue() : "nessuna risposta preparata"; };

            // ask
            Model("  Roma \n");
            Check("(ask \"Capitale d'Italia?\")", "\"Roma\"");
            CheckValue("ask: la domanda arriva al modello", LastUser(), "Capitale d'Italia?");
            Model("due");
            Check("(ask \"Quante righe?\" '((:a 1) (:a 2)))", "\"due\"");
            CheckValue("ask: il dato arriva in JSON", LastUser(), "Quante righe?\n\nData:\n[{\"a\":1},{\"a\":2}]");

            // ask-data
            Model("```json\n[{\"Nome\":\"Roma\",\"Abitanti\":2800000},{\"Nome\":\"Milano\",\"Abitanti\":1400000}]\n```");
            Check("(ask-data \"Due città\" :fields '(:nome :abitanti))", "((:nome \"Roma\" :abitanti 2800000) (:nome \"Milano\" :abitanti 1400000))");
            CheckValue("ask-data: i campi sono nelle istruzioni", requests[0].System.Contains("exactly these keys: nome, abitanti"), true);
            Model("Ecco: {\"citta\": [{\"nome\": \"Roma\"}]}");
            Check("(ask-data \"Una città\" :fields '(:nome))", "((:nome \"Roma\"))");
            Model("{\"nome\": \"Roma\"}");
            Check("(ask-data \"Una città\" :fields '(:nome))", "((:nome \"Roma\"))");
            Model("{\"totale\": 3}");
            Check("(ask-data \"Il totale\")", "(:totale 3)");
            Model("non è json", "[1, 2]");
            Check("(ask-data \"Due numeri\")", "(1 2)");
            CheckValue("ask-data: riprova dopo un JSON sbagliato", requests[0].Messages.Count, 3);
            Model("no", "ancora no");
            Check("(ask-data \"Due numeri\")", "ERRORE: ask-data: la risposta non è JSON valido (JSON non valido (carattere inatteso 'a', al carattere 1)): ancora no");

            // ai-defun: il primo tentativo sbaglia, il secondo va
            Model("```lisp\n(defun somma (a b) (* a b))\n```", "Corretta:\n```lisp\n(defun somma (a b)\n  (+ a b))\n```");
            Check("(ai-defun somma (a b) \"somma due numeri\" :examples ((somma 2 3) 5 (somma 10 1) 11))", "somma");
            Check("(somma 20 22)", "42");
            CheckValue("ai-defun: due richieste", requests.Count, 2);
            CheckValue("ai-defun: l'errore torna al modello", LastUser().StartsWith("(somma 2 3) returns 6, but it should return 5."), true);
            CheckValue("ai-defun: la richiesta contiene gli esempi", requests[0].Messages[0].Text, "Write (defun somma (a b) ...) that does this: somma due numeri\nExamples (call => expected result):\n(somma 2 3) => 5\n(somma 10 1) => 11\n");
            CheckValue("ai-defun: istruzioni con il dialetto", requests[0].System.StartsWith("CORBOLPROMPT LISP"), true);
            Check("(ai-source 'somma)", "\"(defun somma (a b)\n  (+ a b))\n\"");
            Check("(ai-source 'mai-vista)", "nil");

            // risultati attesi valutati, liste confrontate con equal, numeri con =
            Model("```lisp\n(defun pari (l) (remove-if-not #'evenp l))\n```");
            Check("(ai-defun pari (l) \"i numeri pari\" :examples ((pari '(1 2 3 4)) '(2 4)))", "pari");
            Model("```lisp\n(defun meta (x) (/ x 2.0))\n```");
            Check("(ai-defun meta (x) \"la metà\" :examples ((meta 4) 2))", "meta");

            // tre tentativi sbagliati: errore, e la definizione di prima resta
            Run("(defun vecchia () 'prima)");
            Model("(defun vecchia () 1)", "(defun altra () 2)", "(defun vecchia (");
            Check("(ai-defun vecchia () \"restituisce 2\" :examples ((vecchia) 2))", "ERRORE: ai-defun: dopo 3 tentativi la funzione non va: The code is incomplete: the parentheses are not balanced.");
            Check("(vecchia)", "prima");
            CheckValue("ai-defun: nome sbagliato segnalato", requests[0].Messages[4].Text.StartsWith("The block must contain exactly one form, (defun vecchia ...)"), true);
            Model("(defun nuova () (car 5))", "(defun nuova () (car 5))", "(defun nuova () (car 5))");
            Check("(ai-defun nuova () \"x\" :examples ((nuova) 1))", "ERRORE: ai-defun: dopo 3 tentativi la funzione non va: (nuova) signals an error: non è un cons: 5");
            Check("(fboundp 'nuova)", "nil");
            Model("(defun due-forme () 1) (print 2)");
            Check("(ai-defun due-forme () \"x\")", "ERRORE: ai-defun: dopo 3 tentativi la funzione non va: The block must contain exactly one form, (defun due-forme ...), and nothing else.");

            // con funzioni di sistema la definisce ma non la prova: sh non deve partire
            Model("```lisp\n(defun pulisci () (sh \"echo NON-DEVE-PARTIRE\"))\n```");
            term.Clear();
            Check("(ai-defun pulisci () \"pulisce\" :examples ((pulisci) 0))", "pulisci");
            CheckValue("ai-defun: sh non eseguito", term.ToString().Contains("NON-DEVE-PARTIRE\n"), false);
            CheckValue("ai-defun: avviso per le funzioni di sistema", term.ToString().Contains("it uses sh: not tried on the examples"), true);
            Check("(ai-defun x () \"d\" :examples (a))", "ERRORE: ai-defun: gli esempi vanno a coppie: (chiamata risultato-atteso ...)");

            // why
            it.LastError = null;
            CheckOutput("(why)", "No error to explain.\n");
            it.LastErrorSource = "(car 5)"; it.LastError = "non è un cons: 5";
            Model("car vuole una lista.\n```lisp\n(car '(5))\n```");
            CheckOutput("(why)", "car vuole una lista.\n```lisp\n(car '(5))\n```\n");
            CheckValue("why: codice ed errore arrivano al modello", LastUser(), "Code:\n(car 5)\n\nError: non è un cons: 5");
            CheckValue("why: lingua dell'app", requests[0].System.EndsWith("reply in English."), true);
            it.AiHook = null;
        }

        static void CheckValue(string what, object got, object expected)
        {
            total++;
            if (Equals(got, expected)) return;
            fail++;
            Console.WriteLine("FALLITO [" + section + "] " + what);
            Console.WriteLine("    dato:   " + (got == null ? "null" : got.ToString().Replace("\n", "\\n")));
            Console.WriteLine("    atteso: " + (expected == null ? "null" : expected.ToString().Replace("\n", "\\n")));
        }

        // ---------------------------------------------------------------------------
        // Usa PowerShell davvero (c'è su ogni Windows): solo comandi veloci e con risultato fisso.
        static void PowerShellData()
        {
            Section("ps (PowerShell come dati)");
            Check("(ps \"[pscustomobject]@{Nome='x'; Valore=1; Lista=@(1,2)}\")", "((:nome \"x\" :valore 1 :lista (1 2)))");
            Check("(ps \"1, 2, 3\")", "(1 2 3)");
            Check("(ps \"'è'\")", "(\"è\")");
            Check("(ps \"$null\")", "nil");
            Check("(ps \"@{a=1}\")", "((:a 1))");
            Check("(ps \"[System.DayOfWeek]::Monday | ForEach-Object { [pscustomobject]@{Giorno=$_} }\")", "((:giorno \"Monday\"))");
            Check("(->> (ps \"1..5 | ForEach-Object { [pscustomobject]@{N=$_; Pari=($_ % 2 -eq 0)} }\") (filter :pari) (mapcar :n))", "(2 4)");
            Check("(:name (car (ps \"Get-Item $env:WINDIR\")))", "\"Windows\"");
            Check("(ps \"throw 'fallito apposta'\")", "ERRORE: ps: fallito apposta");
            // il messaggio di PowerShell cambia con la lingua di Windows: lo si chiede a PowerShell stesso
            object missing = it.EvalString("(:m (car (ps \"try { Get-Item C:/non/esiste/proprio } catch { [pscustomobject]@{M=$_.Exception.Message} }\")))");
            Check("(ps \"Get-Item C:/non/esiste/proprio\")", "ERRORE: ps: " + missing);
        }

        // ---------------------------------------------------------------------------
        // Esempi presi da wiki/Lisp-guide.md: se cambiano lì, aggiornarli qui.
        static void GuideExamples()
        {
            Section("esempi della guida");
            string note = Path.Combine(Path.GetTempPath(), "corbol-note-prova.txt");
            try
            {
                Check("(with-output-to-string (s)\n  (format s \"~a righe\" 3) (terpri s))", "\"3 righe\n\"");
                Check("(with-input-from-string (s \"uno\ndue\")\n  (list (read-line s) (read-line s)))", "(\"uno\" \"due\")");
                Check("(with-open-file (f \"corbol-note-prova.txt\" :direction :output :if-exists :supersede)\n  (format f \"Fatto il ~a~%\" (now)))", null);
                Check("(with-open-file (f \"corbol-note-prova.txt\")\n  (length (loop for riga = (read-line f nil) while riga collect riga)))", "1");
                Check("(loop for x in '(2 4 6) always (evenp x))", "t");
                Check("(loop named fuori for i from 1 do (loop for j from 1 do (when (= (* i j) 6) (return-from fuori (list i j)))))", "(1 6)");
                Check("(let ((a 1) (b 2)) (psetq a b b a) (list a b))", "(2 1)");
                Check("(typecase 'x ((or symbol character) 'ok))", "ok");
                Run("(defvar *rientro* 0) (defun stampa (s) (format t \"~a~a~%\" (make-string *rientro* :initial-element #\\space) s))");
                CheckOutput("(let ((*rientro* 4)) (stampa \"rientrato\")) (stampa \"normale\")", "    rientrato\nnormale\n");
            }
            finally { try { File.Delete(note); } catch { } }
        }
    }
}
