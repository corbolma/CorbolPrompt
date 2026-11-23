# Compilare e rilasciare

CorbolPrompt è scritto in C# 5 per .NET Framework 4.x e si compila con il compilatore `csc.exe` incluso in Windows: **non serve installare niente** (né Visual Studio né l'SDK .NET).

## Compilare
```
build.bat
```
crea `CorbolPrompt.exe` nella cartella del progetto. Se manca l'icona `corbolprompt.ico`, la genera `make-icon.ps1`.

## I file
| File | Contenuto |
|---|---|
| `CorbolPrompt.cs` | avvio, impostazioni, finestra principale, schede, pannello dell'assistente, menu |
| `Pty.cs` | shell disponibili e sessioni nella console virtuale di Windows (ConPTY) |
| `Terminal.cs` | emulatore VT/xterm: schermo, cronologia, colori, sequenze di escape |
| `TermView.cs` | disegno del terminale, tastiera, selezione, copia e incolla |
| `Tabs.cs` | barra delle schede |
| `Lisp.cs`, `LispLib.cs` | l'interprete Lisp e le sue funzioni |
| `LispRepl.cs` | la riga di comando della scheda Lisp |
| `Assistant.cs` | istruzioni per l'assistente e lettura delle risposte (blocchi di codice → pulsanti ▶) |
| `Ai.cs`, `AiLocal.cs` | servizi dell'assistente (Claude Code, API, compatibili OpenAI, locale) e impostazioni |
| `Favorites.cs` | preferiti e finestra per gestirli |
| `Welcome.cs` | presentazione del primo avvio e collegamenti alla guida |
| `Updater.cs`, `Setup.cs` | aggiornamenti da GitHub e installer |
| `Lang.cs`, `lang\` | lingue dell'interfaccia |
| `Version.cs` | versione |

Per le prove: la variabile d'ambiente `CORBOLPROMPT_PTYLOG=<file>` scrive in quel file l'output grezzo della console virtuale.

## Rilasciare una versione
1. `build-installer.bat patch` (oppure `minor`, `major`, o una versione esplicita `1.2.0`): aggiorna `Version.cs`, compila e crea `dist\CorbolPrompt-Setup-X.Y.Z.exe`, poi stampa lo **SHA-256**.
2. Scrivi le note della release in italiano e in inglese e aggiungi la riga `SHA-256: \`...\``: l'aggiornamento automatico scarica l'installer solo se l'hash corrisponde.
3. Tag e release:
   ```
   git tag vX.Y.Z
   git push origin main vX.Y.Z
   gh release create vX.Y.Z dist\CorbolPrompt-Setup-X.Y.Z.exe --title "CorbolPrompt X.Y.Z" --notes-file dist\notes-X.Y.Z.md
   ```

## Pubblicare la wiki
Le pagine della wiki sono in `wiki\`. Dopo averle modificate:
```
powershell -ExecutionPolicy Bypass -File publish-wiki.ps1
```
copia nella [wiki di GitHub](https://github.com/corbolma/CorbolPrompt/wiki) le pagine nuove o cambiate e toglie quelle eliminate (se è già tutto uguale non fa nulla).

## Traduzioni
I testi sono scritti nel codice in italiano e inglese (`L.T("italiano", "English")`). Per un'altra lingua:
1. `powershell -ExecutionPolicy Bypass -File lang-check.ps1` aggiorna `lang\_template.txt` con tutti i testi;
2. copialo in `lang\<codice>.txt` (es. `de.txt`, `fr.txt`) e scrivi la traduzione dopo ` = `;
3. ricompila: il file viene incorporato nell'exe e la lingua compare in **⋯ → Lingua**.

`lang-check.ps1` segnala anche i testi mancanti, quelli non più usati e i segnaposto `{0}` sbagliati.
