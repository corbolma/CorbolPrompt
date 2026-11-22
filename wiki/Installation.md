# Installation

## Requirements
- Windows 10 version 1809 or later, or Windows 11 (the *ConPTY* virtual console is needed)
- .NET Framework 4.x (already included in Windows)
- **No administrator rights needed**
- Optional: PowerShell 7, WSL, Git for Windows (the matching tabs appear only if they are installed); [Claude Code](https://claude.com/claude-code) for the assistant without an API key

## Install
1. Download `CorbolPrompt-Setup-X.Y.Z.exe` from the [Releases](https://github.com/corbolma/CorbolPrompt/releases/latest) page.
2. Run it. If Windows shows *"Windows protected your PC"*, click **More info → Run anyway** (see the [[FAQ]]).
3. Choose the options:
   - **Add "Open in CorbolPrompt" to the folder menu** (recommended)
   - **Create a desktop shortcut**
   - **Launch CorbolPrompt when finished**
4. Click **Install**.

CorbolPrompt is installed in `%LOCALAPPDATA%\Programs\CorbolPrompt` and appears in the Start menu. On first launch a short tour shows the main features; reopen it from **⋯ → Welcome tour...**.

## Open in CorbolPrompt
With the option on, right-clicking a folder, the background of a folder or a drive in File Explorer shows **Open in CorbolPrompt**: a window opens with a tab of the default shell in that folder. On Windows 11 the item is under **Show more options**.

## Pin it to the taskbar
1. Open **Start** and search for **CorbolPrompt**
2. Right-click → **Pin to taskbar**

## Update
- **Automatically:** CorbolPrompt checks once a day for a new version. When there is one, the **⋯** button becomes **⋯ •** and the menu shows **Update to CorbolPrompt X.Y.Z...**. The installer is downloaded from GitHub and installed only if its SHA-256 matches the one written in the release notes. Turn it off with **⋯ → Update automatically**; **⋯ → Check for updates** checks right away.
- **By hand:** download the new installer and run it. The button tells you what will happen:

| Button | Meaning |
|---|---|
| **Install** | CorbolPrompt is not installed |
| **Update** | an older version is installed |
| **Reinstall** | the same version is already installed |
| **Install anyway** | a **newer** version is installed: careful, you will go back |

The installer closes CorbolPrompt if it is open: running commands stop, the tabs reopen in their folders. Settings, favorites and the Lisp history are kept.

## Uninstall
**Settings → Apps → Installed apps → CorbolPrompt → Uninstall**.

The program, shortcuts, the folder menu item, settings, favorites, the tabs to reopen, the Lisp history and the local assistant files downloaded by CorbolPrompt are removed. Your files stay where they are.

## Silent install
```
CorbolPrompt-Setup-X.Y.Z.exe /silent
```
Installs (or updates) without windows, keeping the choices of the previous installation. It never goes back to an older version unless you add `/force`. To uninstall: `"%LOCALAPPDATA%\Programs\CorbolPrompt\Uninstall.exe" /uninstall /silent`.
