# Lisp guide

The **Lisp (CorbolPrompt)** tab is a small interpreted **Common Lisp**, written for CorbolPrompt: open it from the **⌄** arrow next to the +. Use it for calculations, small programs and to automate the system with a pleasant language, without installing anything.

```lisp
λ> (defun fact (n) (if (= n 0) 1 (* n (fact (- n 1)))))
fact
λ> (fact 30)
265252859812191058636308480000000
λ> (loop for f in (ls) when (string-suffix-p ".txt" f) collect f)
("notes.txt" "todo.txt")
```

## The command line
| Key | Action |
|---|---|
| Enter | evaluates; if the parentheses are not closed you continue on the next line (`..` prompt) |
| ↑ ↓ | history (kept between sessions) |
| Tab | completes the name; two Tabs list the choices |
| Ctrl+C | interrupts the evaluation (even an endless loop or a system command); while typing it cancels the line |
| Ctrl+L | clears the screen |
| Ctrl+A / Ctrl+E, Home / End | start / end of line |
| Ctrl+← / Ctrl+→, Alt+B / Alt+F | previous / next word |
| Ctrl+W, Ctrl+Backspace | deletes the word before the cursor |
| Ctrl+K / Ctrl+U | deletes to the end / to the start of the line |
| Ctrl+D | on an empty line closes the tab |

You can paste several forms at once: they are evaluated one after the other. The result shows in light blue, errors in red. `(help)` shows a summary, `(exit)` closes the tab.

## Basics
- **Numbers:** integers of any size (`(expt 2 200)`) and floats. `/` gives an integer when the division is exact, otherwise a float: `(/ 10 4)` → `2.5`. There are no ratios.
- **Strings** `"hello"`, **characters** `#\a`, **symbols** and **keywords** `:name`, **lists** `'(1 2 3)`, **vectors** `#(1 2 3)`.
- Symbols are case-insensitive and print in lowercase. `nil` is the empty list and false, `t` is true.
- As in Common Lisp, functions have their own namespace: `#'square`, `(funcall f x)`, `(apply #'+ list)`. For convenience a variable holding a function can also be called directly.

## Functions and variables
```lisp
(defun greet (name &optional (greeting "Hello") &key bang)
  (format nil "~a, ~a~:[~;!~]" greeting name bang))
(greet "Mauro" "Good morning" :bang t)   ; → "Good morning, Mauro!"

(defvar *count* 0)
(defparameter *limit* 10)
(let ((x 1) (y 2)) (+ x y))
(let* ((x 1) (y (+ x 1))) (list x y))
(flet ((twice (x) (* 2 x))) (twice 21))
(labels ((f (n) (if (= n 0) 0 (+ n (f (- n 1)))))) (f 100))
(lambda (x) (* x x))
```
Parameters: `&optional`, `&rest`/`&body`, `&key`, `&aux`, with default values. Tail recursion uses no memory: a loop written as a recursive function can run millions of times. Non-tail recursion goes up to 20,000 nested calls.

Variables made with `defvar` and `defparameter` are *special* (dynamic), as in Common Lisp: a `let` that binds one changes its value for the functions called inside too, and when the `let` ends the old value comes back (even after an error).
```lisp
(defvar *indent* 0)
(defun show (s) (format t "~a~a~%" (make-string *indent* :initial-element #\space) s))
(let ((*indent* 4)) (show "indented"))   ; 4 spaces
(show "normal")                          ; none
```
`(declaim (special x))` also makes a variable special, and `(progv '(a b) '(1 2) ...)` binds symbols chosen while the program runs.

## Control
`if`, `when`, `unless`, `cond`, `case`/`ecase` (with `otherwise`), `and`, `or`, `not`, `progn`, `prog1`, `block`/`return-from`/`return`, `catch`/`throw`, `unwind-protect`, `typecase`/`etypecase` (types: `integer`, `string`, `(or symbol character)`, `(integer 0 10)`, `(member a b)`, structure names…), `check-type`.

Assignment: `setq`/`setf` (on variables, `car`, `cdr`, `cadr` and the other `c…r`, `nth`, `first` … `tenth`, `gethash`, `aref`, structure slots), `psetq`/`psetf` (in parallel: `(psetq a b b a)` swaps), `rotatef`, `shiftf`, `incf`, `decf`.

Loops: `dolist`, `dotimes`, `do`/`do*`, and `loop`:

```lisp
(loop for i from 1 to 10 collect (* i i))
(loop for x in '(3 1 4 1 5) maximize x)
(loop for (a b) in '((1 2) (3 4)) collect (+ a b))
(loop for c across "hello" collect (char-upcase c))
(loop for i from 10 downto 0 by 2 do (print i))
(loop with s = 0 for i below 5 do (incf s i) finally (return s))
(loop for x in list when (evenp x) collect x into evens finally (return evens))
(loop for x in '(2 4 6) always (evenp x))         ; → t  (never and thereis too)
(loop for k being the hash-keys of h using (hash-value v) collect (cons k v))
(loop named outer for i from 1 do (loop for j from 1 do (when (= (* i j) 6) (return-from outer (list i j)))))
```
`loop` clauses: `for … in / on / across / from / to / upto / below / downto / above / by / = … then`, `with`, `repeat`, `while`, `until`, `do`, `collect`, `append`, `sum`, `count`, `maximize`, `minimize`, `into`, `when` / `if` / `unless` (with `and` / `else` / `end`), `return`, `always`, `never`, `thereis`, `named`, `initially`, `finally`; `for … being the hash-keys / hash-values of … using (hash-value v)` for hash tables; `(loop-finish)` ends the loop through `finally`. A `loop` without keywords repeats its body until it meets `return`.

## Lists and sequences
`car`, `cdr`, `cons`, `list`, `list*`, `append`, `reverse`, `length`, `nth`, `nthcdr`, `first` … `tenth`, `last`, `butlast`, `member`, `assoc`, `rassoc`, `getf`, `acons`, `copy-list`, `push`, `pushnew`, `pop`, `mapcar`, `mapc`, `mapcan`, `maplist`, `map`, `reduce`, `remove`, `remove-if`, `remove-if-not`, `remove-duplicates`, `find`, `find-if`, `position`, `count`, `count-if`, `every`, `some`, `sort` (stable), `subseq`, `concatenate`, `coerce`, `search`, `elt`, `member-if`, `assoc-if`, `rassoc-if`, `copy-alist`, `mapl`, `mapcon`, `subst`, `subst-if`, `sublis`, `tree-equal`. Many accept `:test` and `:key`.

Sets (lists used as sets): `union`, `intersection`, `set-difference`, `set-exclusive-or`, `subsetp`, `adjoin`, also with `:test` and `:key`.

```lisp
(sort (list 3 1 2) #'<)                          ; → (1 2 3)
(remove-if #'oddp '(1 2 3 4 5 6))                ; → (2 4 6)
(reduce #'+ '(1 2 3 4) :initial-value 10)        ; → 20
(mapcar (lambda (x y) (+ x y)) '(1 2) '(10 20))  ; → (11 22)
```

## Strings and characters
`string-upcase`, `string-downcase`, `string-capitalize`, `string-trim`, `string=`, `string<`, `string-equal`, `char`, `char-code`, `code-char`, `parse-integer`, `write-to-string`, `prin1-to-string`, `princ-to-string`, `read-from-string`, `string-prefix-p`, `string-suffix-p`.

Extras (not Common Lisp, but handy): `(split-string "a b c")`, `(split-string "a,b" ",")`, `(join '("a" "b") ", ")`, `(string-contains s "x")`, `(string-replace s "old" "new")`, `(lines text)`.

## format
```lisp
(format t "Hello ~a!~%" "world")               ; prints
(format nil "~a + ~a = ~a" 1 2 3)              ; → "1 + 2 = 3"
(format nil "~{~a~^, ~}" '(1 2 3))              ; → "1, 2, 3"
(format nil "~5d|~,2f|~s" 42 3.14159 "x")       ; → "   42|3.14|\"x\""
(format nil "~:d" 1234567)                      ; → "1,234,567"
(format nil "~a file~:p" 3)                     ; → "3 files"
```
Directives: `~a ~s ~d ~b ~o ~x ~r ~f ~e ~$ ~c ~% ~& ~~ ~p ~* ~t ~{ ~} ~^ ~[ ~; ~] ~( ~)`, with width, padding character and the `:` and `@` modifiers.

## Streams and files
Besides the terminal you can write to a string or a file, and read from a string or a file. `print`, `princ`, `prin1`, `write-string`, `write-line`, `write-char`, `terpri`, `fresh-line` and `format` take the stream; `t` (or nothing) is `*standard-output*`, that is the terminal.
```lisp
(with-output-to-string (s)
  (format s "~a lines" 3) (terpri s))          ; → "3 lines\n"

(with-input-from-string (s "one\ntwo")
  (list (read-line s) (read-line s)))          ; → ("one" "two")

(with-open-file (f "notes.txt" :direction :output :if-exists :supersede)
  (format f "Done on ~a~%" (now)))

(with-open-file (f "notes.txt")
  (loop for line = (read-line f nil) while line collect line))
```
`:if-exists` can be `:supersede` (rewrite), `:append` (add at the end), `:error` (the default, as in Common Lisp) or `nil`; `:if-does-not-exist nil` returns `nil` instead of an error. Files are UTF-8 and `with-open-file` closes them even after an error. There are also `open`, `close`, `read-char`, `peek-char`, `unread-char`, `read` (reads a form from the stream), `make-string-output-stream`, `get-output-stream-string`, `make-string-input-stream`, `streamp`, and `(let ((*standard-output* s)) …)` to send to `s` what would go to the terminal.

## Hash tables, vectors, structures
```lisp
(let ((h (make-hash-table)))
  (setf (gethash "red" h) 1)
  (incf (gethash "red" h))
  (gethash "red" h))                           ; → 2  (keys are compared with equal)

(let ((v (make-array 3 :initial-element 0)))
  (setf (aref v 1) 9) v)                       ; → #(0 9 0)

(defstruct point x (y 0))
(let ((p (make-point :x 1)))
  (setf (point-x p) 5)
  (list (point-x p) (point-y p) (point-p p)))  ; → (5 0 t)
```
Tables: `gethash`, `remhash`, `clrhash`, `maphash`, `hash-table-count`, `hash-table-keys`, `hash-table-values`.

## Macros
```lisp
(defmacro while* (cond &body body)
  `(loop (unless ,cond (return)) ,@body))
(macroexpand-1 '(while* (< i 3) (incf i)))
```
Quasiquote with `,` and `,@`; `macroexpand`, `gensym`.

## Errors
```lisp
(handler-case (/ 1 0)
  (error (e) (format nil "error: ~a" e)))
(ignore-errors (car 5))                        ; → nil
(error "invalid value: ~a" 42)
```
Every `handler-case` clause catches any error. `assert` is there too. The interpreter's own error messages are in Italian.

## System commands
| Function | What it does |
|---|---|
| `(sh "dir /b")` | runs with cmd.exe in the current folder, shows the output as it comes, returns the exit code |
| `(pwsh "Get-Date")` | the same with PowerShell |
| `(run "git status")` | returns the output as a string, without showing it |
| `(run-lines "dir /b")` | the output as a list of lines |
| `(cd "..")`, `(cd "~")`, `(pwd)` | current folder (the tab name follows it) |
| `(ls)`, `(ls "C:/")` | list of names; folders end with `/` |
| `(read-file f)`, `(read-lines f)`, `(cat f)` | read a file (UTF-8) |
| `(write-file f text)`, `(append-file f text)` | write a file |
| `(file-exists-p f)`, `(directory-p d)`, `(file-size f)`, `(mkdir d)`, `(delete-file f)` | files and folders |
| `(getenv "PATH")`, `(setenv "X" "1")` | environment variables |
| `(load "script.lisp")` | loads and runs a Lisp file |
| `(now)`, `(sleep 1.5)`, `(time (form))` | date and time, pause, run time |
| `(read-line)`, `(y-or-n-p "Go on?")` | ask the user for input |
| `(clear)`, `(exit)` | clears the screen, closes the tab |
| `(apropos "str")` | finds functions by name |

Ctrl+C also interrupts a command started with `sh`, `pwsh` or `run`.

Example: the 5 largest files in the folder, in KB:
```lisp
(let ((files (remove-if (lambda (f) (string-suffix-p "/" f)) (ls))))
  (loop for f in (subseq (sort files #'> :key #'file-size) 0 (min 5 (length files)))
        do (format t "~8d KB  ~a~%" (floor (file-size f) 1024) f)))
```

## Data from the shell
`(ps "...")` runs a PowerShell command and returns the result **as data**: a list of plists, one per object, with field names in lowercase and dashed (`WorkingSet64` → `:working-set64`, `WS(M)` → `:ws-m`).
```lisp
λ> (take 2 (ps "Get-Service"))
status   name     display-name
───────  ───────  ────────────────────────
Stopped  AarSvc   Agent Activation Runtime
Running  Appinfo  Application Information
(2 rows)
λ> (car *1)
(:status "Stopped" :name "AarSvc" :display-name "Agent Activation Runtime")
λ> (->> (ps "Get-Process") (sort-by :ws-m :desc t) (take 3))
npm-k     pm-m     ws-m   cpu-s     id  si  process-name
─────  ───────  ───────  ──────  ─────  ──  ──────────────────
  237  1741.14  1773.14  311.84   4524   1  VBCSCompiler
  397  1797.93  1637.74  499.02  13700   1  devenv
    0     2.62  1042.31     0.0   7868   0  Memory Compression
(3 rows)
```
Without `Select-Object`, `ps` takes the columns PowerShell would show in the console (reading every property of objects like processes can take a minute). With `Select-Object` you choose, and the names are the same in PowerShell 5.1 and 7: the safer choice in scripts. `(ps "..." :all t)` takes every property (slow); `:depth n` sets how deep to go into nested objects (2).
Enums become text (`"Running"`), dates ISO text (`"2026-11-24T09:04:21+01:00"`). A PowerShell error becomes a Lisp error that `handler-case` can catch.

**Automatic table.** When the result is a list of plists, hash tables or structures, the REPL shows it as a table: at most 30 rows, fitted to the window width (columns that are too wide are shortened with `…`, those that do not fit are hidden and named at the bottom). The real data stays in `*1` (and the two results before in `*2`, `*3`): `(table *1)` shows every row, `(print *1)` the list as it is, `(select :name :id *1)` only some columns.

| | |
|---|---|
| `(:name row)` | a keyword used as a function reads that field (plist, hash table or structure); `(:name row "default")`; also `(mapcar :name rows)` and `:key :name` |
| `{:a 1 :b (+ 1 1)}` | a plist; it reads as `(list :a 1 :b (+ 1 1))`, so the values are evaluated |
| `#fn(> (:cpu %) 10)` | short function: `%` is the argument; `%1 %2` for more arguments, `%&` for the rest |
| `(-> x (f a) g)`, `(->> x (f a) g)` | chain steps: `->` passes the value as the first argument (`(g (f x a))`), `->>` as the last (`(g (f a x))`) |
| `(filter pred rows)` | the rows for which `pred` is true; with a keyword, those where the field is not nil |
| `(sort-by :field rows)`, `(sort-by :field :desc t rows)` | sort (the original is not changed): nil, then numbers, then text ignoring case |
| `(take n rows)`, `(drop n rows)` | the first n, all but the first n |
| `(select :name :id rows)` | keeps only those fields (also on a single plist) |
| `(count-by :field rows)` | how many rows for each value, most frequent first: `(("Stopped" . 177) ("Running" . 152))` |
| `(group-by :field rows)` | rows grouped by value: `((value row row...) ...)` |
| `(sum-by :field rows)` | the sum of the field (nils are skipped) |
| `(table rows)`, `(table :name :id rows)` | prints an aligned table with every row; works with lists of lists too |
| `(json-parse text)`, `(read-json file)` | from JSON: objects → plists, arrays → lists, `true` → t, `false`/`null` → nil |
| `(json-encode data)`, `(json-encode data :pretty t)` | to JSON: plists, hash tables and structures → objects, lists and vectors → arrays; `:false` writes `false` |

In all these functions the data comes last, so they chain with `->>`:
```lisp
(->> (ps "Get-ChildItem -File")
     (filter #fn(> (:length %) 1000000))
     (sort-by :length :desc t)
     (table :name :length :last-write-time))
(->> (ps "Get-Service") (count-by :status))
(json-encode (ps "Get-Process | Select-Object -First 3 Name, Id") :pretty t)
```
Note: since keys become lowercase, `json-encode` does not bring back the original capitals (`:process-name` → `"process-name"`).

## AI in the language
These functions use the service chosen in **⋯ → Assistant settings** (Claude Code, Claude API, an OpenAI-compatible service or the local model). Ctrl+C stops the wait.

| | |
|---|---|
| `(ask "question")`, `(ask "question" data)` | the answer as a string; the data (a list, rows from `ps`, a text) reaches the model as JSON |
| `(ask-data "request")`, `(ask-data "request" data :fields '(:a :b))` | the answer as **data**: the model replies in JSON and Lisp reads it like `json-parse`; with `:fields` it is always a list of plists with those fields, which the REPL shows as a table |
| `(ai-defun name (arg...) "what it does" :examples ((name 1 2) 3 ...))` | the model writes the function, Lisp defines it and tries it on the examples; if an example is wrong or signals an error, the problem goes back to the model for another try (up to 3). The code is shown |
| `(ai-source 'name)` | the text of the function written by `ai-defun`, to save it: `(append-file "init.lisp" (ai-source 'name))` |
| `(why)` | explains the last REPL error and suggests the fix |

```lisp
λ> (ask-data "The 3 largest cities in Italy" :fields '(:city :population))
city    population
──────  ──────────
Rome       2750000
Milan      1370000
Naples      910000
(3 rows)
λ> (ask "Which of these processes looks odd?" (take 20 (ps "Get-Process")))
λ> (ai-defun days-between (a b) "days between two \"yyyy-mm-dd\" dates (b minus a)"
     :examples ((days-between "2026-01-01" "2026-01-31") 30
                (days-between "2024-02-01" "2024-03-01") 29))
(defun days-between (a b) ...)
; tried on 2 examples
days-between
λ> (days-between "2026-01-01" "2026-12-25")
358
```
In `ai-defun` examples a call alternates with the expected result; the result is evaluated, so a list needs a quote: `(evens '(1 2 3 4)) '(2 4)`. Numbers are compared with `=` (2 and 2.0 match), everything else with `equal`.
Safety: `ai-defun` accepts only one `defun` with the right name. If the code uses system functions (`sh`, `pwsh`, `run`, `ps`, files, `eval`…), it defines it but does **not** try it on the examples, and says so: read it before using it. AI answers can be wrong, even when they sound sure.

## init.lisp
If you create `%APPDATA%\CorbolPrompt\init.lisp`, it is loaded when each Lisp tab opens: the place for your everyday functions and macros.

## Differences from Common Lisp
Not available: CLOS (`defclass`, `defmethod`), packages, the condition system with restarts, multiple values (`values` returns the first one; `multiple-value-bind` binds only the first), ratios and complex numbers, reader macros, `tagbody`/`go`.
Added on top of Common Lisp: data from the shell (`ps`, `->>`, `{…}`, `#fn(…)`, keywords as functions, JSON), AI (`ask`, `ask-data`, `ai-defun`, `why`), `#t`/`#f`, and `[ ]` as a synonym of `( )`.

The assistant knows exactly this dialect: ask it to write or explain Lisp code; the blocks it suggests run with a click in the Lisp tab.
