# User guide

## The window
- **Top:** the tabs, the **+** and the **⌄** arrow to open new ones; on the right **★ Favorites**, **✦ Assistant** and the **⋯** menu.
- **Center:** the terminal of the active tab.
- **Below the terminal:** the favorites that fit the tab's shell (see [Favorites](#favorites)).
- **Right:** the assistant panel; show and hide it with **✦ Assistant** or **Ctrl+Shift+A**. Drag the border between terminal and panel to resize.

## Tabs
- **+** (or **Ctrl+Shift+T**, or double-click the empty part of the bar): new tab with the default shell, in the folder of the active tab.
- **⌄** next to the +: new tab choosing the shell: PowerShell, Windows PowerShell, Command Prompt, WSL (Linux), Git Bash, Lisp. Only installed shells appear.
- **⋯ → Default shell**: which shell the + opens.
- **Ctrl+Tab / Ctrl+Shift+Tab**: next / previous tab. Drag a tab to reorder.
- **×**, middle click or **Ctrl+Shift+W**: closes the tab. Typing `exit` in the shell closes the tab; if the program ends with an error the tab stays open: **Enter** restarts it.
- **Rename:** double-click the tab name (or right click → *Rename...*), type the name and press **Enter**; **Esc** cancels. Your own name stays fixed, is remembered when you reopen the app and can be used with the assistant (*"look at the server tab"*). Clear it to go back to the automatic name.
- **Right-click a tab:** *Rename...*, *Duplicate tab* (same shell and folder), *Restart*, *Close*, *Close other tabs*, *Close tabs to the right*, *Copy folder path*, *Open folder in File Explorer*.
- Unless you rename it, the tab name is the shell and the current folder (`PowerShell · project`), or the title set by the program (in WSL for example `user@pc: ~/folder`). Hover to see the full path.
- **Many tabs:** when they don't all fit, scroll them with the mouse wheel or the touchpad (also sideways) over the bar; the edge fades where more tabs are hidden, and the **≡** button on the right lists them all.
- The green line under the name means the assistant is answering for that tab.
- When you close CorbolPrompt the tabs are remembered: they come back when you reopen it, each in its folder (running commands do not).

## Tabs side by side
The **▦** button in the top bar (or **Ctrl+Shift+G**, or **⋯ → Show all tabs side by side**) shows all tabs together, in a grid of panes: 2 side by side, 4 in 2×2, and so on (the last row may have wider panes).
- **Drag the borders between panes** to resize them; each shell gets the new size.
- Each pane has a bar with its name at the top: the **green** one is the active tab, which the assistant and favorites work on. Click a pane to make it active; double-click the bar to rename, right-click for the tab menu.
- New tabs join the grid, closed ones leave it. Press **▦** again to go back to one tab at a time; the choice is remembered.

### Typing in all tabs
With the tabs side by side the **⇶** button appears (also **Ctrl+Shift+B** or **⋯ → Type in all tiled tabs**): what you type or paste in the active tab goes **to all tabs at once**, so you can run the same command in several folders or shells. The pane bars turn **orange** with the ⇶ sign while it is on; press ⇶ again to turn it off. Assistant commands and favorites you run go to all of them too. It is not remembered: it always starts off.

## The terminal
It is a real terminal, like Windows Terminal: the shells run in the Windows virtual console (ConPTY). Colors (16, 256 and 24-bit), full-screen programs like `vi`, `less`, `htop` or `edit`, accented characters and emoji all work.

| Action | How |
|---|---|
| Select | drag with the mouse; double-click selects a word (a whole path); Shift+click extends |
| Copy | **Ctrl+C** with text selected, **Ctrl+Shift+C**, or right click → *Copy*. Without a selection Ctrl+C interrupts the command, as usual |
| Paste | **Ctrl+V**, **Ctrl+Shift+V** or right click → *Paste* |
| Scroll the history | mouse wheel, scrollbar on the right, **Shift+PgUp / PgDn**; typing goes back to the bottom |
| Text size | **Ctrl++ / Ctrl+- / Ctrl+0** or Ctrl+wheel; also from the ⋯ menu |
| Clear the history | right click → *Clear the screen history* |
| Find text | **Ctrl+Shift+F** (see below) |
| Open an address or a file | **Ctrl+click** (see below) |

In full-screen programs (less, man, vim) the mouse wheel sends the up and down arrows.

### Finding text
The **🔍 Find** button in the top bar, **Ctrl+Shift+F** (or right click → *Find in text...*) opens a bar above the terminal; selected text becomes the search. As you type, every match on the screen and in the history is colored, the current one in orange, with the count next to it (*3 of 12*).
- **Enter** or **↑** goes to the previous result (higher up), **Shift+Enter** or **↓** to the next; **F3 / Shift+F3** do the same from the terminal too.
- **Aa** matches case.
- **Esc** or **✕** closes the bar. The search follows the active tab and updates with new output.

### Clickable links and paths
Hovering a **web address** (`https://...`, `www....`) or a **path** that exists (`C:\folder\file.txt`, a quoted path even with spaces, or the name of a file or folder in the current folder, for example in a `dir` listing) underlines it; **Ctrl+click** opens it:
- addresses in the browser;
- folders in File Explorer;
- files with their program. Programs and scripts (`.exe`, `.bat`, `.ps1`...) are **not started**: File Explorer opens with the file selected.

`file.cs:12` (the compiler error format) opens the file. Right-click a link: *Open*, *Copy link / path*, *Show in File Explorer*.

### Failed commands
In PowerShell, Command Prompt and Lisp the terminal knows where each command starts and ends. When a command **fails** (in PowerShell and Lisp), a **red mark** appears in the left margin along its lines and the **✦ Explain the error** button appears at the bottom right:
- the button (or a click on the red mark, or right click → *Explain this command's error*) sends the assistant the command, its exit code and its output; it explains what went wrong and suggests the corrected command as a ▶ button;
- the button disappears with the next command; the red marks stay while the lines are in the history.

Command Prompt does not report the exit code, so there are no red marks there; nor in WSL and Git Bash.

### Moving between commands
**Ctrl+Shift+↑** brings the previous command to the top of the screen, **Ctrl+Shift+↓** the next one (a light blue mark in the margin shows where you are); handy with long output. Right-click a command's lines: **Copy the command** and **Copy the command's output** (only its output, without the prompt).

### Notification when a command finishes
When a command that takes at least **10 seconds** finishes in a tab you are not looking at, a **dot** appears on the tab name: green if it succeeded, red if it failed; it goes away when you open the tab. If CorbolPrompt is in the background or minimized, its taskbar icon flashes and a **Windows notification** shows the command and how long it took: click it to go back to the tab. Turn it off with **⋯ → Notify when a long command finishes**. Works in PowerShell, Command Prompt and Lisp.

### Dragging files
Dragging files or folders from File Explorer onto the terminal types their **path**, quoted when needed and in the shell's format (`/mnt/c/...` in WSL, `/c/...` in Git Bash, a string in Lisp). The command does not run: complete it and press Enter.

## The terminal's right-click menu
*Open*, *Copy link*, *Show in File Explorer* (on a link), *Explain this command's error* (on a failed command), *Copy*, *Paste*, *Select all*, *Add to favorites...* (with text selected), *Find in text...*, *Explain the selection / the screen with the assistant*, *Ask the assistant...*, *Clear the screen history*.

## The assistant
The panel on the right. At the top it always shows the **service** in use and, for Claude Code, the **account** (email): click to change them.

- Type a question and press **Enter** (Shift+Enter for a new line, Esc goes back to the terminal). Examples: *"how do I find files larger than 1 GB?"*, *"what does this error mean?"*, *"create a git repository here"*.
- The assistant gets your question, the tab's shell and folder, the names of the other tabs and, if you allow it, the **last lines of the screen** and the selected text. If your question names another tab (or speaks of "the tabs"), it also gets that tab's last lines.
- Each command it suggests becomes a **▶** button below the conversation: **click** runs it in the active tab, **right-click** pastes it without running, copies it or adds it to favorites. Lisp code goes to a Lisp tab (opened if needed). Nothing runs without your click.
- The buttons below the question: **🎤 Dictate** (Windows voice typing, Win+H: speak and the text appears in the question), **🕘 Recent**, **New chat**, **Ask** (it becomes **Stop** while the assistant works).
- **Previous questions:** **↑ / ↓** in the question box bring them back, like in the terminal; **🕘 Recent** lists the latest ones in a menu. They are shared by all tabs and kept between runs (the last 200); the menu also has *Clear the question history*.
- Each tab has its own conversation; **New chat** starts it over.
- **Explain the screen with the assistant** (right-click the terminal) asks for an explanation of the last lines, or of the selected text.

### Assistant settings
**⋯ → Assistant settings...**, or click the service line:

| Service | What you need |
|---|---|
| **Claude Code** (default) | [Claude Code](https://claude.com/claude-code) installed and logged in: uses your subscription, no key. With several subscriptions choose the **Account**; *Add...* creates an account and opens Claude Code to log in, *Log in again...* redoes the login |
| **Claude (API)** | an Anthropic API key |
| **OpenAI-compatible** | address, key and model (OpenAI, OpenRouter, Ollama, LM Studio...) |
| **Local assistant** | downloaded once (model and llama.cpp); runs on the PC, data stays here. Needs a PC with at least 16 GB of RAM |

**Send the assistant the last lines of the terminal**: on by default. Turn it off if there are passwords or confidential data on the screen; the assistant will only see the question, the shell and the folder. API keys are encrypted with Windows DPAPI.

Claude Code accounts are shared with the other Corbol apps.

## Favorites
The commands you reuse, each with a **name**, **description**, **shell** and **command** (also on several lines).

- **Add:** select the command in the terminal → right click → **Add to favorites...**; or right-click an assistant ▶ button → **Add to favorites...**: in this case the description is already the assistant's explanation. The Favorites window opens with the new command: check name and shell and press OK.
- **Use:** the **bar below the terminal** shows the favorites for the active tab's shell (and those for *any shell*): **click** runs, **Shift+click** pastes without running, hover to see description and command. The same from the **★ Favorites** menu (**Ctrl+Shift+P**), where those for other shells are under *Other shells*.
- **Manage:** ★ → **Manage favorites...**: list on the left, name, shell, description and command on the right; **New**, **Delete**, **↑ ↓** for the order. **Describe with the assistant** has it write a one- or two-sentence description (including warnings like "deletes files").
- Turn the bar off with **⋯ → Suggest favorites below the terminal**.

## The Lisp tab
A small interpreted Common Lisp, with system commands: see the [[Lisp guide|Lisp-guide]].

## The ⋯ menu
| Item | What it does |
|---|---|
| Update to CorbolPrompt X.Y.Z... | appears when there is a new version |
| New tab, Default shell | like the ⌄ arrow; which shell the + opens |
| Assistant settings... | service, account, local assistant |
| Larger / Smaller / Normal text | terminal text size |
| Find in text... | the search bar |
| Show all tabs side by side, Type in all tiled tabs | see [Tabs side by side](#tabs-side-by-side) |
| Language | changes right away, without closing the tabs |
| Suggest favorites below the terminal | shows or hides the favorites bar |
| Notify when a long command finishes | dot on the tab and Windows notification |
| Welcome tour..., User guide, Lisp guide | the first-run tour and these pages |
| Keyboard shortcuts | the list of keys |
| Check for updates, Update automatically | see [[Installation]] |
| About CorbolPrompt | version, license, settings folder |

## Keys
| Key | Action |
|---|---|
| Ctrl+Shift+T / Ctrl+Shift+W | new tab / close the tab |
| Ctrl+Tab / Ctrl+Shift+Tab | next / previous tab |
| Ctrl+C (with a selection), Ctrl+Shift+C | copy |
| Ctrl+V, Ctrl+Shift+V | paste |
| Ctrl+Shift+A | assistant |
| Ctrl+Shift+P | favorites |
| Ctrl+Shift+G | tabs side by side / one at a time |
| Ctrl+Shift+B | type in all tiled tabs |
| Ctrl+Shift+F | find in text |
| F3 / Shift+F3 | previous / next result |
| Ctrl+Shift+↑ / ↓ | previous / next command |
| Ctrl+click | open the address or path under the mouse |
| Ctrl+F1 | this guide |
| Shift+PgUp / PgDn | scroll the screen history |
| Ctrl++ / Ctrl+- / Ctrl+0 | text size |

All other keys go to the shell: arrows, Tab, Alt+letter (bash and PSReadLine shortcuts), F1-F12 etc.

## Where the files are
In `%APPDATA%\CorbolPrompt`:

| File | Content |
|---|---|
| `settings.txt` | settings (including the assistant's; keys are encrypted) |
| `session.txt` | the tabs to reopen |
| `favorites.json` | favorites |
| `lisp-history.txt` | the Lisp tab history |
| `init.lisp` | if you create it, it is loaded when each Lisp tab opens |

The local assistant downloads its files to `%LOCALAPPDATA%\CorbolPrompt\ai` (or uses those already downloaded by another Corbol app).
