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
