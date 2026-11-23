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
`if`, `when`, `unless`, `cond`, `case`/`ecase` (with `otherwise`), `and`, `or`, `not`, `progn`, `prog1`, `block`/`return-from`/`return`, `catch`/`throw`, `unwind-protect`.

Loops: `dolist`, `dotimes`, `do`/`do*`, and `loop`:

```lisp
(loop for i from 1 to 10 collect (* i i))
(loop for x in '(3 1 4 1 5) maximize x)
(loop for (a b) in '((1 2) (3 4)) collect (+ a b))
(loop for c across "hello" collect (char-upcase c))
(loop for i from 10 downto 0 by 2 do (print i))
(loop with s = 0 for i below 5 do (incf s i) finally (return s))
(loop for x in list when (evenp x) collect x into evens finally (return evens))
```
`loop` clauses: `for … in / on / across / from / to / upto / below / downto / above / by / = … then`, `with`, `repeat`, `while`, `until`, `do`, `collect`, `append`, `sum`, `count`, `maximize`, `minimize`, `into`, `when` / `if` / `unless` (with `and` / `else` / `end`), `return`, `initially`, `finally`. A `loop` without keywords repeats its body until it meets `return`.

## Lists and sequences
`car`, `cdr`, `cons`, `list`, `list*`, `append`, `reverse`, `length`, `nth`, `nthcdr`, `first` … `tenth`, `last`, `butlast`, `member`, `assoc`, `rassoc`, `getf`, `acons`, `copy-list`, `push`, `pushnew`, `pop`, `mapcar`, `mapc`, `mapcan`, `maplist`, `map`, `reduce`, `remove`, `remove-if`, `remove-if-not`, `remove-duplicates`, `find`, `find-if`, `position`, `count`, `count-if`, `every`, `some`, `sort` (stable), `subseq`, `concatenate`, `coerce`, `search`, `elt`. Many accept `:test` and `:key`.

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

## init.lisp
If you create `%APPDATA%\CorbolPrompt\init.lisp`, it is loaded when each Lisp tab opens: the place for your everyday functions and macros.

## Differences from Common Lisp
Not available: CLOS (`defclass`, `defmethod`), packages, the condition system with restarts, multiple values (`values` returns the first one; `multiple-value-bind` binds only the first), ratios and complex numbers, streams other than the terminal, reader macros. The `never`, `always` and `thereis` clauses of `loop` are missing.

The assistant knows exactly this dialect: ask it to write or explain Lisp code; the blocks it suggests run with a click in the Lisp tab.
