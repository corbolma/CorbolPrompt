# Installazione

## Requisiti
- Windows 10 versione 1809 o successiva, oppure Windows 11 (serve la console virtuale *ConPTY*)
- .NET Framework 4.x (già incluso in Windows)
- **Non servono permessi di amministratore**
- Facoltativi: PowerShell 7, WSL, Git per Windows (le schede relative compaiono solo se sono installati); [Claude Code](https://claude.com/claude-code) per l'assistente senza chiave API

## Installare
1. Scarica `CorbolPrompt-Setup-X.Y.Z.exe` dalla pagina [Releases](https://github.com/corbolma/CorbolPrompt/releases/latest).
2. Avvialo. Se Windows mostra *"Windows ha protetto il PC"*, clicca **Ulteriori informazioni → Esegui comunque** (vedi [[Domande frequenti|Domande-frequenti]]).
3. Scegli le opzioni:
   - **Aggiungi "Apri in CorbolPrompt" al menu delle cartelle** (consigliato)
   - **Crea un collegamento sul desktop**
   - **Avvia CorbolPrompt al termine**
4. Clicca **Installa**.

CorbolPrompt viene installata in `%LOCALAPPDATA%\Programs\CorbolPrompt` e compare nel menu Start. Al primo avvio una breve presentazione mostra le funzioni principali; si riapre da **⋯ → Benvenuto...**.

## Apri in CorbolPrompt
Con l'opzione attiva, il tasto destro in Esplora file su una cartella, sullo sfondo di una cartella o su un'unità mostra **Apri in CorbolPrompt**: si apre una finestra con una scheda della shell predefinita in quella cartella. Su Windows 11 la voce è in **Mostra altre opzioni**.

## Fissarla alla barra delle applicazioni
1. Apri **Start** e cerca **CorbolPrompt**
2. Tasto destro → **Aggiungi alla barra delle applicazioni**

## Aggiornare
- **Automaticamente:** CorbolPrompt controlla una volta al giorno se c'è una versione nuova. Quando c'è, il pulsante **⋯** diventa **⋯ •** e nel menu compare **Aggiorna a CorbolPrompt X.Y.Z...**. L'installer viene scaricato da GitHub e installato solo se il suo SHA-256 corrisponde a quello scritto nelle note della release. Si spegne da **⋯ → Aggiorna automaticamente**; **⋯ → Controlla aggiornamenti** controlla subito.
- **A mano:** scarica il nuovo installer e avvialo. Il pulsante indica cosa succederà:

| Pulsante | Significato |
|---|---|
| **Installa** | CorbolPrompt non è installata |
| **Aggiorna** | è installata una versione più vecchia |
| **Reinstalla** | è già installata la stessa versione |
| **Installa comunque** | è installata una versione **più recente**: attenzione, tornerai indietro |

L'installer chiude CorbolPrompt se è aperta: i comandi in corso si interrompono, le schede si riaprono nelle loro cartelle. Impostazioni, preferiti e cronologia del Lisp restano.

## Disinstallare
**Impostazioni → App → App installate → CorbolPrompt → Disinstalla**.

Vengono rimossi programma, collegamenti, la voce nel menu delle cartelle, le impostazioni, i preferiti, le schede da riaprire, la cronologia del Lisp e i file dell'assistente locale scaricati da CorbolPrompt. I tuoi file restano dove sono.

## Installazione silenziosa
```
CorbolPrompt-Setup-X.Y.Z.exe /silent
```
Installa (o aggiorna) senza finestre, mantenendo le scelte dell'installazione precedente. Non torna mai a una versione più vecchia, salvo `/force`. Per disinstallare: `"%LOCALAPPDATA%\Programs\CorbolPrompt\Uninstall.exe" /uninstall /silent`.
