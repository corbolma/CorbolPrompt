# CorbolPrompt

Terminale a schede per Windows con un assistente IA e un interprete Lisp integrato.

- **Shell vere** in ogni scheda: PowerShell 7, Windows PowerShell, Prompt dei comandi, WSL (Linux) e Git Bash (se installati). Girano nella console virtuale di Windows (ConPTY) e quindi funzionano colori, programmi a schermo intero (vi, less, edit...), cronologia scorrevole e ridimensionamento.
- **Schede** come in CorbolPad: `+` o Ctrl+Maiusc+T per una nuova scheda, `▾` per scegliere la shell, Ctrl+Tab per passare dall'una all'altra, tasto destro per duplicare, riavviare, chiudere o aprire la cartella. Alla riapertura tornano le schede di prima, nelle loro cartelle.
- **Assistente** come negli altri Corbol (Claude Code con più account, Claude API, servizi compatibili OpenAI, assistente locale): vede la shell, la cartella e, se lo permetti, le ultime righe dello schermo. Spiega errori e output e propone comandi: ognuno diventa un pulsante ▶ che lo esegue nella scheda giusta, solo quando lo premi. Il servizio e l'account in uso sono sempre scritti in cima al pannello.
- **Preferiti** (★ nella barra o Ctrl+Maiusc+P): i comandi da riusare. Si aggiungono selezionando un comando nel terminale → tasto destro → *Aggiungi ai preferiti*, o dal tasto destro su un comando proposto dall'assistente. Clic = esegue, Maiusc+clic = incolla senza eseguire; *Gestisci i preferiti...* per nome, descrizione, shell, modifica e ordine. La descrizione si riempie da sola con la spiegazione dell'assistente quando salvi un suo comando; altrimenti *Descrivi con l'assistente* la fa scrivere a lui. Sotto il terminale una barra suggerisce i preferiti adatti alla shell della scheda attiva (si spegne dal menu ⋯); nel menu ★ quelli delle altre shell sono in *Altre shell*. Sono in `%APPDATA%\CorbolPrompt\favorites.json`.
- **Lisp**: una scheda "Lisp (CorbolPrompt)" con un piccolo Common Lisp interpretato: numeri interi grandi, liste, vettori, tabelle hash, `defun`, `defmacro` con quasiquote, `loop`, `defstruct`, `format`, `handler-case`, ricorsione in coda. In più comandi di sistema: `(sh "dir")`, `(pwsh "Get-Date")`, `(run "git status")`, `(cd "..")`, `(ls)`, `(read-file "f.txt")`. Scrivi `(help)` per un riepilogo. Il file `%APPDATA%\CorbolPrompt\init.lisp`, se c'è, viene caricato all'apertura di ogni scheda Lisp.

## Guida

La guida completa è la wiki del progetto (sorgenti in `wiki\`, da pubblicare su GitHub): installazione, guida utente, guida al Lisp, domande frequenti, come compilare. Dall'app: menu ⋯ → Guida (Ctrl+F1) e Guida al Lisp. Al primo avvio una presentazione di 4 pagine mostra le funzioni principali; si riapre da ⋯ → Benvenuto.

## Tasti

| Tasto | Azione |
|---|---|
| Ctrl+Maiusc+T / Ctrl+Maiusc+W | nuova scheda / chiude la scheda |
| Ctrl+Tab / Ctrl+Maiusc+Tab | scheda successiva / precedente |
| Ctrl+C con testo selezionato | copia (senza selezione interrompe il comando) |
| Ctrl+V, tasto destro → Incolla | incolla |
| Ctrl+Maiusc+A | assistente |
| Ctrl+Maiusc+P | preferiti |
| Maiusc+PagSu/PagGiù, rotellina | scorre la cronologia dello schermo |
| Ctrl++ / Ctrl+- / Ctrl+0, Ctrl+rotellina | dimensione del carattere |

## Installare e aggiornare

`build-installer.bat` crea `dist\CorbolPrompt-Setup-X.Y.Z.exe` (con `patch`, `minor` o `major` incrementa la versione). L'installer installa solo per l'utente, senza permessi di amministratore, in `%LOCALAPPDATA%\Programs\CorbolPrompt`, con collegamenti nel menu Start e sul desktop e, se lo lasci attivo, la voce **Apri in CorbolPrompt** nel menu del tasto destro delle cartelle. Si disinstalla da Impostazioni → App.

L'app controlla una volta al giorno le release su GitHub (menu ⋯ → Controlla aggiornamenti / Aggiorna automaticamente) e installa la nuova versione solo se lo SHA-256 corrisponde a quello scritto nelle note della release.

La lingua si cambia dal menu ⋯ → Lingua, subito, senza chiudere le schede.

## Compilare

`build.bat` usa il compilatore C# incluso in Windows (.NET Framework 4.x): non serve installare niente.
`make-icon.ps1` rigenera l'icona; `lang-check.ps1` aggiorna `lang\_template.txt` per le traduzioni.

Serve Windows 10 1809 o successivo (console virtuale ConPTY).

## Impostazioni

In `%APPDATA%\CorbolPrompt`: `settings.txt` (impostazioni), `session.txt` (schede da riaprire), `lisp-history.txt` (cronologia del Lisp).

Licenza MIT.
