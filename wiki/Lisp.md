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

Le variabili create con `defvar` e `defparameter` sono *speciali* (dinamiche), come in Common Lisp: un `let` che le lega cambia il valore anche per le funzioni chiamate dentro, e alla fine del `let` torna quello di prima (anche se c'è un errore).
```lisp
(defvar *rientro* 0)
(defun stampa (s) (format t "~a~a~%" (make-string *rientro* :initial-element #\space) s))
(let ((*rientro* 4)) (stampa "rientrato"))   ; 4 spazi
(stampa "normale")                            ; nessuno
```
Anche `(declaim (special x))` rende speciale una variabile e `(progv '(a b) '(1 2) ...)` lega simboli scelti mentre il programma gira.

## Controllo
`if`, `when`, `unless`, `cond`, `case`/`ecase` (con `otherwise`), `and`, `or`, `not`, `progn`, `prog1`, `block`/`return-from`/`return`, `catch`/`throw`, `unwind-protect`, `typecase`/`etypecase` (i tipi: `integer`, `string`, `(or symbol character)`, `(integer 0 10)`, `(member a b)`, i nomi delle strutture…), `check-type`.

Assegnamenti: `setq`/`setf` (su variabili, `car`, `cdr`, `cadr` e gli altri `c…r`, `nth`, `first` … `tenth`, `gethash`, `aref`, campi delle strutture), `psetq`/`psetf` (in parallelo: `(psetq a b b a)` scambia), `rotatef`, `shiftf`, `incf`, `decf`.

Cicli: `dolist`, `dotimes`, `do`/`do*`, e `loop`:

```lisp
(loop for i from 1 to 10 collect (* i i))
(loop for x in '(3 1 4 1 5) maximize x)
(loop for (a b) in '((1 2) (3 4)) collect (+ a b))
(loop for c across "ciao" collect (char-upcase c))
(loop for i from 10 downto 0 by 2 do (print i))
(loop with s = 0 for i below 5 do (incf s i) finally (return s))
(loop for x in lista when (evenp x) collect x into pari finally (return pari))
(loop for x in '(2 4 6) always (evenp x))         ; → t  (anche never e thereis)
(loop for k being the hash-keys of h using (hash-value v) collect (cons k v))
(loop named fuori for i from 1 do (loop for j from 1 do (when (= (* i j) 6) (return-from fuori (list i j)))))
```
Clausole di `loop`: `for … in / on / across / from / to / upto / below / downto / above / by / = … then`, `with`, `repeat`, `while`, `until`, `do`, `collect`, `append`, `sum`, `count`, `maximize`, `minimize`, `into`, `when` / `if` / `unless` (con `and` / `else` / `end`), `return`, `always`, `never`, `thereis`, `named`, `initially`, `finally`; `for … being the hash-keys / hash-values of … using (hash-value v)` per le tabelle hash; `(loop-finish)` chiude il ciclo passando da `finally`. Un `loop` senza parole chiave ripete il corpo finché non incontra `return`.

## Liste e sequenze
`car`, `cdr`, `cons`, `list`, `list*`, `append`, `reverse`, `length`, `nth`, `nthcdr`, `first` … `tenth`, `last`, `butlast`, `member`, `assoc`, `rassoc`, `getf`, `acons`, `copy-list`, `push`, `pushnew`, `pop`, `mapcar`, `mapc`, `mapcan`, `maplist`, `map`, `reduce`, `remove`, `remove-if`, `remove-if-not`, `remove-duplicates`, `find`, `find-if`, `position`, `count`, `count-if`, `every`, `some`, `sort` (stabile), `subseq`, `concatenate`, `coerce`, `search`, `elt`, `member-if`, `assoc-if`, `rassoc-if`, `copy-alist`, `mapl`, `mapcon`, `subst`, `subst-if`, `sublis`, `tree-equal`. Molte accettano `:test` e `:key`.

Insiemi (liste usate come insiemi): `union`, `intersection`, `set-difference`, `set-exclusive-or`, `subsetp`, `adjoin`, anche loro con `:test` e `:key`.

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

## Stream e file
Oltre al terminale si può scrivere su una stringa o su un file, e leggere da una stringa o da un file. `print`, `princ`, `prin1`, `write-string`, `write-line`, `write-char`, `terpri`, `fresh-line` e `format` accettano lo stream; `t` (o niente) è `*standard-output*`, cioè il terminale.
```lisp
(with-output-to-string (s)
  (format s "~a righe" 3) (terpri s))          ; → "3 righe\n"

(with-input-from-string (s "uno\ndue")
  (list (read-line s) (read-line s)))          ; → ("uno" "due")

(with-open-file (f "note.txt" :direction :output :if-exists :supersede)
  (format f "Fatto il ~a~%" (now)))

(with-open-file (f "note.txt")
  (loop for riga = (read-line f nil) while riga collect riga))
```
`:if-exists` può essere `:supersede` (riscrive), `:append` (aggiunge in fondo), `:error` (il predefinito, come in Common Lisp) o `nil`; `:if-does-not-exist nil` restituisce `nil` invece di dare errore. I file sono in UTF-8 e `with-open-file` li chiude anche se c'è un errore. Ci sono anche `open`, `close`, `read-char`, `peek-char`, `unread-char`, `read` (legge una forma dallo stream), `make-string-output-stream`, `get-output-stream-string`, `make-string-input-stream`, `streamp`, e `(let ((*standard-output* s)) …)` per mandare su `s` anche quello che si scriverebbe sul terminale.

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

## Dati dalla shell
`(ps "...")` esegue un comando PowerShell e restituisce il risultato **come dati**: una lista di plist, una per oggetto, con i campi in minuscolo e col trattino (`WorkingSet64` → `:working-set64`, `WS(M)` → `:ws-m`).
```lisp
λ> (take 2 (ps "Get-Service"))
status   name     display-name
───────  ───────  ─────────────────────────
Stopped  AarSvc   Agent Activation Runtime
Running  Appinfo  Informazioni applicazioni
(2 righe)
λ> (car *1)
(:status "Stopped" :name "AarSvc" :display-name "Agent Activation Runtime")
λ> (->> (ps "Get-Process") (sort-by :ws-m :desc t) (take 3))
npm-k     pm-m     ws-m   cpu-s     id  si  process-name
─────  ───────  ───────  ──────  ─────  ──  ──────────────────
  237  1741.14  1773.14  311.84   4524   1  VBCSCompiler
  397  1797.93  1637.74  499.02  13700   1  devenv
    0     2.62  1042.31     0.0   7868   0  Memory Compression
(3 righe)
```
Senza `Select-Object`, `ps` prende le colonne che PowerShell mostrerebbe in console (leggere tutte le proprietà di oggetti come i processi richiede anche un minuto). Con `Select-Object` decidi tu, e i nomi non cambiano tra PowerShell 5.1 e 7: negli script è la scelta più sicura. `(ps "..." :all t)` prende tutte le proprietà (lento); `:depth n` decide quanto scendere negli oggetti annidati (2).
Gli enum diventano testo (`"Running"`), le date testo ISO (`"2026-11-24T09:04:21+01:00"`). Un errore di PowerShell diventa un errore Lisp, che `handler-case` può prendere.

**Tabella automatica.** Quando il risultato è una lista di plist, tabelle hash o strutture, il REPL lo mostra come tabella: al massimo 30 righe, adattata alla larghezza della finestra (le colonne troppo larghe si accorciano con `…`, quelle che non ci stanno vengono nascoste e nominate in fondo). I dati veri restano in `*1` (e i due risultati prima in `*2`, `*3`): `(table *1)` mostra tutte le righe, `(print *1)` la lista com'è, `(select :name :id *1)` solo alcune colonne.

| | |
|---|---|
| `(:name riga)` | una keyword usata come funzione legge quel campo (plist, tabella hash o struttura); `(:name riga "predefinito")`; vale anche `(mapcar :name righe)` e `:key :name` |
| `{:a 1 :b (+ 1 1)}` | una plist; si legge come `(list :a 1 :b (+ 1 1))`, quindi i valori vengono calcolati |
| `#fn(> (:cpu %) 10)` | funzione breve: `%` è l'argomento; `%1 %2` per più argomenti, `%&` per il resto |
| `(-> x (f a) g)`, `(->> x (f a) g)` | concatenano i passi: `->` mette il valore come primo argomento (`(g (f x a))`), `->>` come ultimo (`(g (f a x))`) |
| `(filter pred righe)` | le righe per cui `pred` è vero; con una keyword, quelle in cui il campo non è nil |
| `(sort-by :campo righe)`, `(sort-by :campo :desc t righe)` | ordina (senza modificare l'originale): nil, poi numeri, poi testo senza badare alle maiuscole |
| `(take n righe)`, `(drop n righe)` | le prime n, tutte tranne le prime n |
| `(select :name :id righe)` | tiene solo quei campi (anche su una sola plist) |
| `(count-by :campo righe)` | quante righe per ogni valore, dal più frequente: `(("Stopped" . 177) ("Running" . 152))` |
| `(group-by :campo righe)` | le righe raggruppate per valore: `((valore riga riga...) ...)` |
| `(sum-by :campo righe)` | la somma del campo (i nil non contano) |
| `(table righe)`, `(table :name :id righe)` | stampa una tabella allineata con tutte le righe; funziona anche con liste di liste |
| `(bar dati)`, `(bar :name :cpu righe)` | un grafico a barre: i dati possono essere coppie `(etichetta . n)` come quelle di `count-by`, liste `(etichetta n)`, una plist, una tabella hash o una lista di numeri; con le righe si indicano il campo dell'etichetta e quello del valore |
| `(hist numeri)`, `(hist :cpu righe)`, `:bins n` | un istogramma: quanti valori cadono in ogni fascia; le fasce hanno larghezze tonde (0–20, 20–40…), con `:bins` sono esattamente n |
| `(sparkline numeri)`, `(sparkline :cpu righe)` | una stringa come `"▁▃▅█▂"`, da mettere anche in `format` |

```lisp
λ> (bar (count-by :status (ps "Get-Service")))
Stopped  ██████████████████████████████████████████████████ 175
Running  ████████████████████████████████████████████ 154
λ> (hist :working-set64 (ps "Get-Process | Select-Object WorkingSet64"))
 0–500M  ██████████████████████████████████████████████████ 300
500M–1G  ▍ 2
1G–1.5G  0
1.5G–2G  ▏ 1
λ> (format t "andamento: ~a~%" (sparkline '(3 5 9 4 2 8 12 7)))
andamento: ▂▃▆▂▁▅█▅
```

| | |
|---|---|
| `(json-parse testo)`, `(read-json file)` | dal JSON: oggetti → plist, array → liste, `true` → t, `false`/`null` → nil |
| `(json-encode dato)`, `(json-encode dato :pretty t)` | al JSON: plist, tabelle hash e strutture → oggetti, liste e vettori → array; `:false` scrive `false` |

In tutte queste funzioni il dato va per ultimo, così si concatenano con `->>`:
```lisp
(->> (ps "Get-ChildItem -File")
     (filter #fn(> (:length %) 1000000))
     (sort-by :length :desc t)
     (table :name :length :last-write-time))
(->> (ps "Get-Service") (count-by :status))
(json-encode (ps "Get-Process | Select-Object -First 3 Name, Id") :pretty t)
```
Nota: dato che le chiavi diventano minuscole, `json-encode` non ricrea le maiuscole originali (`:process-name` → `"process-name"`).

## L'IA nel linguaggio
Per cominciare, con esempi semplici: [[Assistente IA|Assistente-IA#i-comandi-dellia-nel-lisp]].

Queste funzioni usano il servizio scelto in **⋯ → Impostazioni dell'assistente** (Claude Code, API Claude, servizio compatibile OpenAI o modello locale). Ctrl+C interrompe l'attesa.

| | |
|---|---|
| `(ask "domanda")`, `(ask "domanda" dato)` | la risposta come stringa; il dato (una lista, delle righe di `ps`, un testo) arriva al modello in JSON |
| `(ask-data "richiesta")`, `(ask-data "richiesta" dato :fields '(:a :b))` | la risposta come **dati**: il modello risponde in JSON e il Lisp lo legge come `json-parse`; con `:fields` è sempre una lista di plist con quei campi, che il REPL mostra come tabella |
| `(ai-defun nome (arg...) "cosa fa" :examples ((nome 1 2) 3 ...))` | il modello scrive la funzione, il Lisp la definisce e la prova sugli esempi; se un esempio sbaglia o dà errore, gli rimanda il problema e riprova (fino a 3 volte). Il codice viene mostrato |
| `(ai-source 'nome)` | il testo della funzione scritta da `ai-defun`, per salvarla: `(append-file (concatenate 'string (getenv "APPDATA") "/CorbolPrompt/init.lisp") (ai-source 'nome))` |
| `(why)` | spiega l'ultimo errore del REPL e propone la correzione |

```lisp
λ> (ask-data "Le 3 città più popolose d'Italia" :fields '(:citta :abitanti))
citta    abitanti
──────  ────────
Roma     2750000
Milano   1370000
Napoli    910000
(3 righe)
λ> (ask "Quale di questi processi è il più strano?" (take 20 (ps "Get-Process")))
λ> (ai-defun giorni-tra (a b) "giorni tra due date \"aaaa-mm-gg\" (b meno a)"
     :examples ((giorni-tra "2026-01-01" "2026-01-31") 30
                (giorni-tra "2024-02-01" "2024-03-01") 29))
(defun giorni-tra (a b) ...)
; provata su 2 esempi
giorni-tra
λ> (giorni-tra "2026-01-01" "2026-12-25")
358
```
Negli esempi di `ai-defun` si alternano una chiamata e il risultato atteso; il risultato si valuta, quindi una lista va scritta con l'apice: `(pari '(1 2 3 4)) '(2 4)`. I numeri si confrontano con `=` (2 e 2.0 vanno bene), il resto con `equal`.
Sicurezza: `ai-defun` accetta solo una `defun` con il nome giusto. Se il codice usa funzioni di sistema (`sh`, `pwsh`, `run`, `ps`, file, `eval`…), la definisce ma **non** la prova sugli esempi, e lo dice: leggila prima di usarla. Le risposte dell'IA possono essere sbagliate, anche quando sembrano sicure.

## Windows
| | |
|---|---|
| `(clipboard)` | il testo negli appunti, o `nil` |
| `(set-clipboard x)` | mette `x` negli appunti: una stringa così com'è, una lista di plist come testo separato da tabulazioni con l'intestazione (si incolla in Excel), il resto come lo stampa `princ` |
| `(notify "titolo" ["testo"])` | una notifica di Windows; un clic riporta alla scheda |
| `(http-get url)` | il testo della risposta; `:headers '(:authorization "Bearer …")`, `:timeout` in secondi (30 se non lo dici) |
| `(http-post url corpo)` | come `http-get`, con un corpo: una stringa va così com'è, un dato (plist, lista) va come JSON |
| `(http-json url)`, `(http-json url :body dato)` | la risposta letta come `json-parse`; con `:body` è una POST |
| `(watch secondi forma...)` | ripete le forme e **ridisegna sul posto** quello che stampano e il risultato (come tabella, se lo è), finché non premi Ctrl+C; `:times n` si ferma dopo n volte, `(return x)` esce con `x` |
| `(every secondi forma...)` | ripete le forme ogni tanti secondi, senza ridisegnare: per controlli e avvisi; Ctrl+C o `(return x)` per finire |
| `(choose-file)` | la finestra per scegliere un file: il percorso, o `nil` se annulli; `:title`, `:filter "*.csv"`, `:save t` per salvare, `:multiple t` per una lista di file |
| `(choose-folder)` | la finestra per scegliere una cartella |

```lisp
λ> (watch 2 (->> (ps "Get-Process | Select-Object Name, CPU") (sort-by :cpu :desc t) (take 5)))
λ> (every 60 (when (< (:free (first (ps "Get-PSDrive C | Select-Object Free"))) 10e9)
               (notify "Disco C" "meno di 10 GB liberi")
               (return)))
λ> (set-clipboard (ps "Get-Service | Select-Object Name, Status"))
λ> (->> (http-json "https://api.github.com/repos/corbolma/CorbolPrompt/releases") (select :tag-name :published-at) (take 3))
λ> (length (read-lines (choose-file :filter "*.txt")))
```
`watch` ed `every` lavorano in primo piano: mentre girano il REPL aspetta. Ctrl+C ferma `watch` senza errore.

## init.lisp
Se crei `%APPDATA%\CorbolPrompt\init.lisp`, viene caricato all'apertura di ogni scheda Lisp: è il posto per le tue funzioni e macro di tutti i giorni.

## Differenze da Common Lisp
Non ci sono: CLOS (`defclass`, `defmethod`), package, il sistema di condizioni con i restart, valori multipli (`values` restituisce il primo; `multiple-value-bind` lega solo il primo), frazioni e numeri complessi, reader macro, `tagbody`/`go`.
In più rispetto a Common Lisp: i dati dalla shell (`ps`, `->>`, `{…}`, `#fn(…)`, le keyword come funzioni, JSON), l'IA (`ask`, `ask-data`, `ai-defun`, `why`), i grafici (`bar`, `hist`, `sparkline`), le funzioni per Windows (appunti, `notify`, `http-get`, `watch`, `choose-file`…), `#t`/`#f`, e `[ ]` come sinonimo di `( )`.

L'assistente conosce esattamente questo dialetto: chiedigli pure di scrivere o spiegare codice Lisp, i blocchi che propone si eseguono con un clic nella scheda Lisp.
