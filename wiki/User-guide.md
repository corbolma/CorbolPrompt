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

In full-screen programs (less, man, vim) the mouse wheel sends the up and down arrows.

### Tabs side by side
The **▦** button in the top bar (or **Ctrl+Shift+G**, or **⋯ → Show all tabs side by side**) shows all tabs together, in a grid of panes: 2 side by side, 4 in 2×2, and so on (the last row may have wider panes).
- **Drag the borders between panes** to resize them; each shell gets the new size.
- Each pane has a bar with its name at the top: the **green** one is the active tab, which the assistant and favorites work on. Click a pane to make it active; double-click the bar to rename, right-click for the tab menu.
- New tabs join the grid, closed ones leave it. Press **▦** again to go back to one tab at a time; the choice is remembered.

## The terminal's right-click menu
*Copy*, *Paste*, *Select all*, *Add to favorites...* (with text selected), *Explain the selection / the screen with the assistant*, *Ask the assistant...*, *Clear the screen history*.

## The assistant
The panel on the right. At the top it always shows the **service** in use and, for Claude Code, the **account** (email): click to change them.

- Type a question and press **Enter** (Shift+Enter for a new line, Esc goes back to the terminal). Examples: *"how do I find files larger than 1 GB?"*, *"what does this error mean?"*, *"create a git repository here"*.
- The assistant gets your question, the tab's shell and folder, the names of the other tabs and, if you allow it, the **last lines of the screen** and the selected text. If your question names another tab (or speaks of "the tabs"), it also gets that tab's last lines.
- Each command it suggests becomes a **▶** button below the conversation: **click** runs it in the active tab, **right-click** pastes it without running, copies it or adds it to favorites. Lisp code goes to a Lisp tab (opened if needed). Nothing runs without your click.
- Each tab has its own conversation; **New** starts it over.
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
| Language | changes right away, without closing the tabs |
| Suggest favorites below the terminal | shows or hides the favorites bar |
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
