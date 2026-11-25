# AI assistant

There are three ways to use AI in CorbolPrompt:

| Where | How | What for |
|---|---|---|
| [The panel](#the-panel) | **Ctrl+Shift+A**, then write in plain English | asking for commands, explanations, scripts |
| [Clicks in the terminal](#clicks-in-the-terminal) | **✦ Explain the error**, right click | having what you see explained, without typing |
| [The Lisp tab](#ai-commands-in-lisp) | `(ask ...)`, `(ask-data ...)`, `(ai-defun ...)`, `(why)` | using AI inside a program, on real data |

**There are no special commands to learn** for the panel: just write a sentence. The AI **never runs anything by itself** in the terminal: it suggests, you press ▶.

## Getting started
1. **⋯ → Assistant settings...** and choose the [service](#choosing-the-service). If you already use Claude Code and are logged in, there is nothing to set up.
2. Press **Ctrl+Shift+A** and type, for example, *"which files here are larger than 100 MB?"*, then **Enter**.
3. The answer appears in the panel; below it, each suggested command is a **▶** button. A click runs it in the active tab.

## The panel
### What you can ask
| You want... | Type for example |
|---|---|
| a command | *"how do I find files larger than 1 GB?"* · *"which program is using port 8080?"* |
| to understand an error | *"what does this error mean?"* · *"why doesn't it work?"* |
| to understand output | *"summarize what is on the screen"* · *"which of these services can I stop?"* |
| to understand a command | *"what does `robocopy /MIR` do? is it dangerous?"* |
| a script | *"write a script that renames all photos with their date"* |
| to use git | *"create a git repository here"* · *"undo the last commit without losing the changes"* |
| to translate between shells | *"how do I write `grep -r` in PowerShell?"* |
| to look at another tab | *"look at the server tab: did it start?"* · *"compare the two tabs"* |
| Lisp code | *"write a Lisp function that counts the words in a file"* |
| to adjust the answer | *"do it with cmd"* · *"only .pdf files"* · *"explain it more simply"* |

Follow-up questions continue the conversation: the assistant remembers the last 12 messages (6 questions and 6 answers). Each tab has its own; **New chat** starts it over.

For better answers: work in the right tab and folder (it knows them), **select in the terminal** the text you are talking about, and say what you want to achieve rather than how.

### The ▶ buttons
Below the answer there is a button for each command (8 at most). They replace those of the previous answer.

| Action | What it does |
|---|---|
| **click** | runs the command in the active tab |
| **right click → Paste without running** | types it in the terminal: you edit it and press Enter yourself |
| **right click → Copy** | copies it to the clipboard |
| **right click → Add to favorites...** | saves it, with the assistant's explanation as the description |
| **hover** | shows the whole command |

Buttons marked **[Lisp]** open a Lisp tab if there isn't one. When **Type in all tiled tabs** (⇶) is on, the command goes to all of them.

**Read the command** before pressing ▶: the assistant warns you when a command deletes, overwrites or changes settings, but it can be wrong.

### Keys and buttons for the question
| Key or button | What it does |
|---|---|
| **Ctrl+Shift+A** | shows or hides the panel |
| **Enter** / **Ask** | sends the question |
| **Shift+Enter** | new line |
| **Stop** | stops the answer |
| **↑ / ↓**, **🕘 Recent** | previous questions (the last 200, shared by all tabs) |
| **🎤 Dictate** | Windows voice typing (Win+H) |
| **New chat** | starts the tab's conversation over |
| **Esc** | goes back to the terminal |

## Clicks in the terminal
| Action | What it asks the assistant |
|---|---|
| **✦ Explain the error** button (bottom right), or a click on the **red mark** in the margin | why that command failed, and the corrected command |
| right click → **Explain this command's error** | the same, for a command further up |
| right click → **Explain the screen with the assistant** | what happened in the last lines |
| right click with text selected → **Explain the selection with the assistant** | what that text means |
| right click → **Ask the assistant...** | opens the panel so you can type the question |
| ★ → Manage favorites → **Describe with the assistant** | writes the description of a favorite |

The red mark and **✦ Explain the error** are available in PowerShell and Lisp; in the other shells use *Explain the screen*.

## AI commands in Lisp
*In versions after 0.3.1.* Type them in a **Lisp** tab (the **⌄** arrow next to the +). **Ctrl+C** stops the wait.

| Command | What it does |
|---|---|
| `(ask "question")` | answers with text |
| `(ask "question" data)` | the same, about some data: a text, a list, rows from `ps` |
| `(ask-data "request" :fields '(:a :b))` | answers with a table of **data** the program can use |
| `(ask-data "request" data :fields '(:a :b))` | builds the table from a text, a list or rows from `ps` |
| `(ai-defun name (arg) "what it does" :examples (...))` | writes a function and tries it on the examples |
| `(ai-source 'name)` | the code of a function written by `ai-defun` |
| `(why)` | explains the last error in the Lisp tab and fixes it |

### Asking a question
```lisp
(ask "What is the command that shows the IP address?")

; about a file
(ask "Summarize in three lines" (read-file "notes.txt"))

; about the output of a command
(ask "Are there any errors? Which ones?" (run "git status"))

; about real data from the system
(ask "Which of these processes looks odd?"
     (take 20 (ps "Get-Process | Select-Object Name, Id, Path")))
```

### Getting data instead of text
```lisp
; a table from nothing
(ask-data "The 3 largest cities in Italy" :fields '(:city :population))

; data taken from free text
(ask-data "Extract the expenses"
          "yesterday 12 euros for lunch, Monday 45 for gas"
          :fields '(:what :euros))

; then work on it like any other table
(sum-by :euros *1)
```
`*1` is the last result. With `:fields` the result is always a list of rows with those fields.

### Having a function written
```lisp
(ai-defun days-between (a b) "days between two \"yyyy-mm-dd\" dates (b minus a)"
  :examples ((days-between "2026-01-01" "2026-01-31") 30
             (days-between "2024-02-01" "2024-03-01") 29))

(days-between "2026-01-01" "2026-12-25")   ; → 358
```
In the examples a call alternates with the expected result. If an example does not match, the AI gets the error and tries again, up to 3 times. If the function uses system commands (`sh`, `ps`, files...) it is **not tried**, and you are told: read it before using it.

To have it every time you open a Lisp tab, save it in `init.lisp`:
```lisp
(append-file (concatenate 'string (getenv "APPDATA") "/CorbolPrompt/init.lisp")
             (ai-source 'days-between))
```

### Understanding an error
```lisp
(car 5)     ; error
(why)       ; explains the cause and shows the corrected code
```

All the details are in the [[Lisp guide|Lisp-guide#ai-in-the-language]].

## Choosing the service
**⋯ → Assistant settings...**, or click the line at the top of the panel, which always shows the service in use. It applies to both the panel and Lisp.

| Service | What you need | Models |
|---|---|---|
| **Claude Code** (default) | [Claude Code](https://claude.com/claude-code) installed and logged in; uses your subscription, no key | the account's default, or opus, sonnet, haiku |
| **Claude (API)** | an Anthropic API key (pay as you go) | chosen from the list |
| **OpenAI-compatible** | address, model and, if the service asks for it, a key: OpenAI, OpenRouter, Ollama, LM Studio... | the one you type |
| **Local assistant** | a PC with at least 16 GB of RAM; downloaded once (about 6 GB) | Ministral 3 8B or Qwen3.5 9B |

- **Several Claude Code accounts:** choose one under **Account**; *Add...* creates one, *Log in again...* redoes the login.
- **Local assistant:** free, and data stays on the PC, but it is slower and less accurate: fine for simple questions. The first answer is slow because the model has to load; after 5 minutes without questions it closes and frees the memory.

## What is sent
| | When |
|---|---|
| the question, the tab's shell and folder, the names (with shell and folder) of the other tabs, the tab's conversation | always, with a question in the panel |
| the last 80 lines of the screen and the selected text | if **Send the assistant the last lines of the terminal** is on (it is by default) |
| the last 40 lines of another tab | as above, and only if you name it in the question or speak of "tabs" |
| the command, its exit code and its output | with **Explain the error**, **even if the option is off** |
| only the question and the data you pass | with `ask` and `ask-data` |
| the name, arguments, description and examples; if an attempt fails, the error | with `ai-defun` |
| the code and the error | with `(why)` |
| the shell and the command | with **Describe with the assistant** |

Data goes to the service you chose; with the local assistant it stays on the PC. If there are passwords or confidential data on the screen, turn the option off in the settings. API keys are encrypted with Windows DPAPI. With Claude Code the assistant runs without tools: it doesn't read files or run commands.

## If something goes wrong
| Message or problem | What to do |
|---|---|
| *Claude Code is not installed* | install [Claude Code](https://claude.com/claude-code) or choose another service |
| *not logged in* | **Assistant settings → Log in again...** and type `/login` in the window that opens |
| *invalid or missing API key* | check the key in the settings |
| *out of credit*, *too many requests* | check your account with the service, or wait a little |
| *model or address not found* | check the model name and the address |
| it doesn't see the screen or the selected text | turn on **Send the assistant the last lines of the terminal** |
| it answers for the wrong shell | switch to the right tab, or say so: *"in cmd"*, *"in bash"* |
| *IA non configurata* (AI not configured, in Lisp) | choose the service from **⋯ → Assistant settings...** |

AI answers can be wrong even when they sound sure: check commands before running them.
