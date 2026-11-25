# FAQ

### Windows says "Windows protected your PC"
That's SmartScreen: it appears because the installer **is not digitally signed** (a code-signing certificate costs money). Click **More info → Run anyway**.
You can check the file is the original one by comparing its SHA-256 hash with the one in the [release](https://github.com/corbolma/CorbolPrompt/releases) notes:
```powershell
Get-FileHash .\CorbolPrompt-Setup-X.Y.Z.exe -Algorithm SHA256
```

### The arrow menu doesn't show PowerShell 7, WSL or Git Bash
They appear only if installed: PowerShell 7 if `pwsh.exe` is in the PATH or in `C:\Program Files\PowerShell\7`, WSL if `wsl.exe` exists, Git Bash if `C:\Program Files\Git\bin\bash.exe` exists. Reopen CorbolPrompt after installing them.

### The WSL tab closes right away or shows an error
WSL has no distribution installed or cannot start. Try `wsl --list` in a Command Prompt and, if needed, `wsl --install`.

### The tab name doesn't follow the folder
With PowerShell and Command Prompt, CorbolPrompt adjusts the prompt to know which folder you are in. If your PowerShell profile redefines `prompt` after startup, or in cmd the `PROMPT` variable is changed later, the name stays as before. In WSL and Git Bash the name is the title set by the shell (usually `user@pc: folder`).

### Ctrl+C doesn't copy
Ctrl+C copies **only when text is selected**; otherwise, as in any terminal, it interrupts the running command. To be sure to copy use **Ctrl+Shift+C** or the right-click menu.

### The text is too small or too large
**Ctrl++ / Ctrl+-** (or Ctrl+mouse wheel), **Ctrl+0** to go back to normal; also from the **⋯** menu. The terminal font is Cascadia Mono, or Consolas if Cascadia is missing.

### The assistant says Claude Code is not installed or not logged in
Install [Claude Code](https://claude.com/claude-code) and log in once (type `claude` in a tab and follow the instructions). If you have several subscriptions, choose the account in **⋯ → Assistant settings**. Otherwise use a Claude API key, an OpenAI-compatible service or the local assistant.

### What does the assistant see? Does it run commands by itself?
It sees your question, the tab's shell and folder, the names of the other tabs and, if the option is on, the last lines of the screen and the selected text. **It runs nothing by itself**: the commands it suggests start only when you press their ▶ button. With Claude Code the assistant runs without tools: it doesn't read files or run commands. The full list is in [[AI assistant|AI-assistant#what-is-sent]].

### The tabs didn't come back
The tabs of the main window closed last come back. Windows opened with **Open in CorbolPrompt** on a folder don't change the tabs to reopen. Commands that were running are not started again.

### How do I use Lisp for automation?
See the [[Lisp guide|Lisp-guide]]: `sh`, `pwsh` and `run` run commands, `ls`, `read-file`, `write-file` work with files. Put the functions you always use in `%APPDATA%\CorbolPrompt\init.lisp`.

### Where are settings, favorites and history?
In `%APPDATA%\CorbolPrompt`; see the [[User guide|User-guide#where-the-files-are]]. The **⋯ → About CorbolPrompt** menu has a link to the folder.
