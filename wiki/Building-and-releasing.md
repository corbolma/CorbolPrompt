# Building and releasing

CorbolPrompt is written in C# 5 for .NET Framework 4.x and is compiled with the `csc.exe` compiler included in Windows: **nothing to install** (neither Visual Studio nor the .NET SDK).

## Build
```
build.bat
```
creates `CorbolPrompt.exe` in the project folder. If the `corbolprompt.ico` icon is missing, `make-icon.ps1` generates it.

## The files
| File | Content |
|---|---|
| `CorbolPrompt.cs` | startup, settings, main window, tabs, assistant panel, menus |
| `Pty.cs` | available shells and sessions in the Windows virtual console (ConPTY) |
| `Terminal.cs` | VT/xterm emulator: screen, history, colors, escape sequences |
| `TermView.cs` | terminal drawing, keyboard, selection, copy and paste |
| `Tabs.cs` | tab bar |
| `Lisp.cs`, `LispLib.cs` | the Lisp interpreter and its functions |
| `LispRepl.cs` | the command line of the Lisp tab |
| `Assistant.cs` | instructions for the assistant and reading its answers (code blocks → ▶ buttons) |
| `Ai.cs`, `AiLocal.cs` | assistant services (Claude Code, API, OpenAI-compatible, local) and settings |
| `Favorites.cs` | favorites and the window to manage them |
| `Welcome.cs` | first-run tour and links to the guide |
| `Updater.cs`, `Setup.cs` | updates from GitHub and installer |
| `Lang.cs`, `lang\` | interface languages |
| `Version.cs` | version |

For testing: the environment variable `CORBOLPROMPT_PTYLOG=<file>` writes the raw output of the virtual console to that file.

## Releasing a version
1. `build-installer.bat patch` (or `minor`, `major`, or an explicit version `1.2.0`): updates `Version.cs`, builds and creates `dist\CorbolPrompt-Setup-X.Y.Z.exe`, then prints the **SHA-256**.
2. Write the release notes in Italian and English and add the line `SHA-256: \`...\``: the automatic update downloads the installer only if the hash matches.
3. Tag and release:
   ```
   git tag vX.Y.Z
   git push origin main vX.Y.Z
   gh release create vX.Y.Z dist\CorbolPrompt-Setup-X.Y.Z.exe --title "CorbolPrompt X.Y.Z" --notes-file dist\notes-X.Y.Z.md
   ```

## Publishing the wiki
The wiki pages are in `wiki\`. After editing them:
```
powershell -ExecutionPolicy Bypass -File publish-wiki.ps1
```
copies new or changed pages to the [GitHub wiki](https://github.com/corbolma/CorbolPrompt/wiki) and removes deleted ones (if everything is already the same it does nothing).

## Translations
The texts are written in the code in Italian and English (`L.T("italiano", "English")`). For another language:
1. `powershell -ExecutionPolicy Bypass -File lang-check.ps1` updates `lang\_template.txt` with all the texts;
2. copy it to `lang\<code>.txt` (e.g. `de.txt`, `fr.txt`) and write the translation after ` = `;
3. rebuild: the file is embedded in the exe and the language appears in **⋯ → Language**.

`lang-check.ps1` also reports missing texts, texts no longer used and wrong `{0}` placeholders.
