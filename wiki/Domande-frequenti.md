# Domande frequenti

### Windows dice "Windows ha protetto il PC"
È SmartScreen: compare perché l'installer **non è firmato digitalmente** (un certificato di firma ha un costo). Clicca **Ulteriori informazioni → Esegui comunque**.
Puoi verificare che il file sia quello originale confrontando l'hash SHA-256 riportato nelle note della [release](https://github.com/corbolma/CorbolPrompt/releases):
```powershell
Get-FileHash .\CorbolPrompt-Setup-X.Y.Z.exe -Algorithm SHA256
```

### Nel menu della freccia non vedo PowerShell 7, WSL o Git Bash
Compaiono solo se sono installati: PowerShell 7 se `pwsh.exe` è nel PATH o in `C:\Program Files\PowerShell\7`, WSL se c'è `wsl.exe`, Git Bash se c'è `C:\Program Files\Git\bin\bash.exe`. Dopo averli installati riapri CorbolPrompt.

### La scheda WSL si chiude subito o mostra un errore
WSL non ha una distribuzione installata o non è avviabile. Prova in un Prompt dei comandi `wsl --list` e, se serve, `wsl --install`.

### Il nome della scheda non segue la cartella
Con PowerShell e Prompt dei comandi CorbolPrompt modifica il prompt per sapere in che cartella sei. Se il tuo profilo di PowerShell ridefinisce `prompt` dopo l'avvio, o in cmd la variabile `PROMPT` viene cambiata a metà, il nome resta quello di prima. In WSL e Git Bash il nome è il titolo dato dalla shell (di solito `utente@pc: cartella`).

### Ctrl+C non copia
Ctrl+C copia **solo se c'è del testo selezionato**; altrimenti, come in ogni terminale, interrompe il comando in corso. Per essere sicuri di copiare usa **Ctrl+Maiusc+C** o il tasto destro.

### Il carattere è troppo piccolo o troppo grande
**Ctrl++ / Ctrl+-** (o Ctrl+rotellina), **Ctrl+0** per tornare normale; anche dal menu **⋯**. Il carattere del terminale è Cascadia Mono, oppure Consolas se Cascadia non c'è.

### L'assistente dice che Claude Code non è installato o che il login non è fatto
Installa [Claude Code](https://claude.com/claude-code) e fai il login una volta (scrivi `claude` in una scheda e segui le istruzioni). Se hai più abbonamenti, scegli l'account in **⋯ → Impostazioni dell'assistente**. In alternativa usa la chiave API di Claude, un servizio compatibile OpenAI o l'assistente locale.

### Cosa vede l'assistente? Esegue comandi da solo?
Vede la tua domanda, la shell e la cartella della scheda, i nomi delle altre schede e, se l'opzione è accesa, le ultime righe dello schermo e il testo selezionato. **Non esegue niente da solo**: i comandi che propone partono solo quando premi il loro pulsante ▶. Con Claude Code l'assistente gira senza strumenti: non legge file e non esegue comandi. L'elenco completo è in [[Assistente IA|Assistente-IA#cosa-viene-mandato]].

### Le schede non si sono riaperte
Si riaprono le schede della finestra principale chiusa per ultima. Le finestre aperte con **Apri in CorbolPrompt** su una cartella non cambiano le schede da riaprire. I comandi che erano in corso non vengono rilanciati.

### Come faccio a usare il Lisp per automatizzare?
Vedi la [[Guida al Lisp|Lisp]]: `sh`, `pwsh` e `run` eseguono comandi, `ls`, `read-file`, `write-file` lavorano con i file. Le funzioni che usi sempre mettile in `%APPDATA%\CorbolPrompt\init.lisp`.

### Dove sono impostazioni, preferiti e cronologia?
In `%APPDATA%\CorbolPrompt`; vedi [[Guida utente|Guida-utente#dove-sono-i-file]]. Il menu **⋯ → Informazioni su CorbolPrompt** ha un collegamento alla cartella.
