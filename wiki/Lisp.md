# Guida al Lisp

La scheda **Lisp (CorbolPrompt)** è un piccolo **Common Lisp** interpretato, scritto per CorbolPrompt: si apre dalla freccia **⌄** accanto al +. Serve per calcoli, piccoli programmi e per automatizzare il sistema con un linguaggio comodo, senza installare niente.

```lisp
λ> (defun fact (n) (if (= n 0) 1 (* n (fact (- n 1)))))
fact
λ> (fact 30)
265252859812191058636308480000000
λ> (loop for f in (ls) when (string-suffix-p ".txt" f) collect f)
("note.txt" "todo.txt")
```

## La riga di comando
| Tasto | Azione |
|---|---|
| Invio | valuta; se le parentesi non sono chiuse si continua sulla riga sotto (prompt `..`) |
| ↑ ↓ | cronologia (salvata tra una sessione e l'altra) |
| Tab | completa il nome; due Tab elencano le possibilità |
| Ctrl+C | interrompe la valutazione (anche un ciclo infinito o un comando di sistema); mentre scrivi annulla la riga |
| Ctrl+L | pulisce lo schermo |
| Ctrl+A / Ctrl+E, Home / Fine | inizio / fine riga |
| Ctrl+← / Ctrl+→, Alt+B / Alt+F | parola precedente / successiva |
| Ctrl+W, Ctrl+Backspace | cancella la parola prima del cursore |
| Ctrl+K / Ctrl+U | cancella fino alla fine / all'inizio della riga |
| Ctrl+D | su una riga vuota chiude la scheda |

Si può incollare più di una forma insieme: vengono valutate una dopo l'altra. Il risultato compare in azzurro, gli errori in rosso. `(help)` mostra un riepilogo, `(exit)` chiude la scheda.

## Le basi
- **Numeri:** interi di qualunque grandezza (`(expt 2 200)`) e decimali. `/` dà un intero se la divisione è esatta, altrimenti un decimale: `(/ 10 4)` → `2.5`. Non ci sono le frazioni.
- **Stringhe** `"ciao"`, **caratteri** `#\a`, **simboli** e **keyword** `:nome`, **liste** `'(1 2 3)`, **vettori** `#(1 2 3)`.
- I simboli non distinguono maiuscole e minuscole e si stampano in minuscolo. `nil` è la lista vuota e il falso, `t` il vero.
- Come in Common Lisp le funzioni hanno uno spazio dei nomi separato: `#'quadrato`, `(funcall f x)`, `(apply #'+ lista)`. Per comodità una variabile che contiene una funzione si può anche chiamare direttamente.

## Funzioni e variabili
```lisp
(defun saluta (nome &optional (saluto "Ciao") &key punto)
  (format nil "~a, ~a~:[~;!~]" saluto nome punto))
(saluta "Mauro" "Buongiorno" :punto t)   ; → "Buongiorno, Mauro!"

(defvar *conta* 0)
(defparameter *limite* 10)
(let ((x 1) (y 2)) (+ x y))
(let* ((x 1) (y (+ x 1))) (list x y))
(flet ((doppio (x) (* 2 x))) (doppio 21))
(labels ((f (n) (if (= n 0) 0 (+ n (f (- n 1)))))) (f 100))
(lambda (x) (* x x))
```
Parametri: `&optional`, `&rest`/`&body`, `&key`, `&aux`, con valori predefiniti. La ricorsione in coda non consuma memoria: un ciclo scritto come funzione ricorsiva può girare milioni di volte. La ricorsione non in coda arriva a 20.000 chiamate annidate.

## Controllo
`if`, `when`, `unless`, `cond`, `case`/`ecase` (con `otherwise`), `and`, `or`, `not`, `progn`, `prog1`, `block`/`return-from`/`return`, `catch`/`throw`, `unwind-protect`.

Cicli: `dolist`, `dotimes`, `do`/`do*`, e `loop`:

```lisp
(loop for i from 1 to 10 collect (* i i))
(loop for x in '(3 1 4 1 5) maximize x)
(loop for (a b) in '((1 2) (3 4)) collect (+ a b))
(loop for c across "ciao" collect (char-upcase c))
(loop for i from 10 downto 0 by 2 do (print i))
(loop with s = 0 for i below 5 do (incf s i) finally (return s))
(loop for x in lista when (evenp x) collect x into pari finally (return pari))
```
Clausole di `loop`: `for … in / on / across / from / to / upto / below / downto / above / by / = … then`, `with`, `repeat`, `while`, `until`, `do`, `collect`, `append`, `sum`, `count`, `maximize`, `minimize`, `into`, `when` / `if` / `unless` (con `and` / `else` / `end`), `return`, `initially`, `finally`. Un `loop` senza parole chiave ripete il corpo finché non incontra `return`.

## Liste e sequenze
`car`, `cdr`, `cons`, `list`, `list*`, `append`, `reverse`, `length`, `nth`, `nthcdr`, `first` … `tenth`, `last`, `butlast`, `member`, `assoc`, `rassoc`, `getf`, `acons`, `copy-list`, `push`, `pushnew`, `pop`, `mapcar`, `mapc`, `mapcan`, `maplist`, `map`, `reduce`, `remove`, `remove-if`, `remove-if-not`, `remove-duplicates`, `find`, `find-if`, `position`, `count`, `count-if`, `every`, `some`, `sort` (stabile), `subseq`, `concatenate`, `coerce`, `search`, `elt`. Molte accettano `:test` e `:key`.

```lisp
(sort (list 3 1 2) #'<)                          ; → (1 2 3)
(remove-if #'oddp '(1 2 3 4 5 6))                ; → (2 4 6)
(reduce #'+ '(1 2 3 4) :initial-value 10)        ; → 20
(mapcar (lambda (x y) (+ x y)) '(1 2) '(10 20))  ; → (11 22)
```

## Stringhe e caratteri
`string-upcase`, `string-downcase`, `string-capitalize`, `string-trim`, `string=`, `string<`, `string-equal`, `char`, `char-code`, `code-char`, `parse-integer`, `write-to-string`, `prin1-to-string`, `princ-to-string`, `read-from-string`, `string-prefix-p`, `string-suffix-p`.

In più (non Common Lisp, ma comodi): `(split-string "a b c")`, `(split-string "a,b" ",")`, `(join '("a" "b") ", ")`, `(string-contains s "x")`, `(string-replace s "vecchio" "nuovo")`, `(lines testo)`.

## format
```lisp
(format t "Ciao ~a!~%" "mondo")                 ; stampa
(format nil "~a + ~a = ~a" 1 2 3)              ; → "1 + 2 = 3"
(format nil "~{~a~^, ~}" '(1 2 3))              ; → "1, 2, 3"
(format nil "~5d|~,2f|~s" 42 3.14159 "x")       ; → "   42|3.14|\"x\""
(format nil "~:d" 1234567)                      ; → "1,234,567"
(format nil "~a file~:p" 3)                     ; → "3 files"
```
Direttive: `~a ~s ~d ~b ~o ~x ~r ~f ~e ~$ ~c ~% ~& ~~ ~p ~* ~t ~{ ~} ~^ ~[ ~; ~] ~( ~)`, con larghezza, carattere di riempimento e i modificatori `:` e `@`.

## Tabelle hash, vettori, strutture
```lisp
(let ((h (make-hash-table)))
  (setf (gethash "rosso" h) 1)
  (incf (gethash "rosso" h))
  (gethash "rosso" h))                         ; → 2  (le chiavi si confrontano con equal)

(let ((v (make-array 3 :initial-element 0)))
  (setf (aref v 1) 9) v)                       ; → #(0 9 0)

(defstruct punto x (y 0))
(let ((p (make-punto :x 1)))
  (setf (punto-x p) 5)
  (list (punto-x p) (punto-y p) (punto-p p)))  ; → (5 0 t)
```
Tabelle: `gethash`, `remhash`, `clrhash`, `maphash`, `hash-table-count`, `hash-table-keys`, `hash-table-values`.

## Macro
```lisp
(defmacro finche (cond &body corpo)
  `(loop (unless ,cond (return)) ,@corpo))
(macroexpand-1 '(finche (< i 3) (incf i)))
```
Quasiquote con `,` e `,@`; `macroexpand`, `gensym`.

## Errori
```lisp
(handler-case (/ 1 0)
  (error (e) (format nil "errore: ~a" e)))     ; → "errore: divisione per zero"
(ignore-errors (car 5))                        ; → nil
(error "valore non valido: ~a" 42)
```
Ogni clausola di `handler-case` prende qualunque errore. Anche `assert`.

## Comandi per il sistema
| Funzione | Cosa fa |
|---|---|
| `(sh "dir /b")` | esegue con cmd.exe nella cartella corrente, mostra l'output mentre arriva, restituisce il codice di uscita |
| `(pwsh "Get-Date")` | lo stesso con PowerShell |
| `(run "git status")` | restituisce l'output come stringa, senza mostrarlo |
| `(run-lines "dir /b")` | l'output come lista di righe |
| `(cd "..")`, `(cd "~")`, `(pwd)` | cartella corrente (il nome della scheda la segue) |
| `(ls)`, `(ls "C:/")` | lista dei nomi; le cartelle finiscono con `/` |
| `(read-file f)`, `(read-lines f)`, `(cat f)` | leggere un file (UTF-8) |
| `(write-file f testo)`, `(append-file f testo)` | scrivere un file |
| `(file-exists-p f)`, `(directory-p d)`, `(file-size f)`, `(mkdir d)`, `(delete-file f)` | file e cartelle |
| `(getenv "PATH")`, `(setenv "X" "1")` | variabili d'ambiente |
| `(load "script.lisp")` | carica ed esegue un file Lisp |
| `(now)`, `(sleep 1.5)`, `(time (forma))` | data e ora, pausa, tempo di esecuzione |
| `(read-line)`, `(y-or-n-p "Continuo?")` | chiedere un dato all'utente |
| `(clear)`, `(exit)` | pulisce lo schermo, chiude la scheda |
| `(apropos "str")` | cerca funzioni per nome |

Ctrl+C interrompe anche un comando lanciato con `sh`, `pwsh` o `run`.

Esempio: i 5 file più grandi della cartella, in KB:
```lisp
(let ((files (remove-if (lambda (f) (string-suffix-p "/" f)) (ls))))
  (loop for f in (subseq (sort files #'> :key #'file-size) 0 (min 5 (length files)))
        do (format t "~8d KB  ~a~%" (floor (file-size f) 1024) f)))
```

## init.lisp
Se crei `%APPDATA%\CorbolPrompt\init.lisp`, viene caricato all'apertura di ogni scheda Lisp: è il posto per le tue funzioni e macro di tutti i giorni.

## Differenze da Common Lisp
Non ci sono: CLOS (`defclass`, `defmethod`), package, il sistema di condizioni con i restart, valori multipli (`values` restituisce il primo; `multiple-value-bind` lega solo il primo), variabili speciali (dinamiche: `defvar` crea una variabile globale normale), frazioni e numeri complessi, stream diversi dal terminale, reader macro. Le clausole `never`, `always` e `thereis` di `loop` non ci sono.

L'assistente conosce esattamente questo dialetto: chiedigli pure di scrivere o spiegare codice Lisp, i blocchi che propone si eseguono con un clic nella scheda Lisp.
