# Guida utente

## La finestra
- **In alto:** le schede, il **+** e la freccia **⌄** per aprirne di nuove; a destra **★ Preferiti**, **✦ Assistente** e il menu **⋯**.
- **Al centro:** il terminale della scheda attiva.
- **Sotto il terminale:** i preferiti adatti alla shell della scheda (vedi [Preferiti](#preferiti)).
- **A destra:** il pannello dell'assistente; si mostra e si nasconde con **✦ Assistente** o **Ctrl+Maiusc+A**. Il bordo tra terminale e pannello si trascina.

## Schede
- **+** (o **Ctrl+Maiusc+T**, o doppio clic sullo spazio vuoto della barra): nuova scheda con la shell predefinita, nella cartella della scheda attiva.
- **⌄** accanto al +: nuova scheda scegliendo la shell: PowerShell, Windows PowerShell, Prompt dei comandi, WSL (Linux), Git Bash, Lisp. Compaiono solo le shell installate.
- **⋯ → Shell predefinita**: quale shell apre il +.
- **Ctrl+Tab / Ctrl+Maiusc+Tab**: scheda successiva / precedente. Trascinando una scheda si cambia l'ordine.
- **×**, clic con la rotellina o **Ctrl+Maiusc+W**: chiude la scheda. Scrivere `exit` nella shell chiude la scheda; se il programma termina con un errore la scheda resta aperta: **Invio** lo riavvia.
- **Rinominare:** doppio clic sul nome della scheda (o tasto destro → *Rinomina...*), scrivi il nome e premi **Invio**; **Esc** annulla. Un nome tuo resta fisso, viene ricordato alla riapertura e lo puoi usare con l'assistente (*"guarda la scheda server"*). Svuotalo per tornare al nome automatico.
- **Tasto destro su una scheda:** *Rinomina...*, *Duplica scheda* (stessa shell e cartella), *Riavvia*, *Chiudi*, *Chiudi le altre*, *Chiudi le schede a destra*, *Copia il percorso della cartella*, *Apri la cartella in Esplora file*.
- Se non la rinomini, il nome della scheda è la shell e la cartella corrente (`PowerShell · progetto`), oppure il titolo dato dal programma (in WSL per esempio `utente@pc: ~/cartella`). Passandoci sopra si vede il percorso completo.
- **Molte schede:** quando non ci stanno tutte si scorrono con la rotellina o con il touchpad (anche orizzontale) sopra la barra; il bordo sfuma dove ce ne sono altre nascoste, e il pulsante **≡** a destra mostra l'elenco di tutte.
- La linea verde sotto il nome indica che l'assistente sta rispondendo per quella scheda.
- Chiudendo CorbolPrompt le schede vengono ricordate: alla riapertura tornano, ognuna nella sua cartella (i comandi in corso no).

## Il terminale
È un terminale vero, come Windows Terminal: le shell girano nella console virtuale di Windows (ConPTY). Funzionano colori (16, 256 e 24 bit), programmi a schermo intero come `vi`, `less`, `htop` o `edit`, caratteri accentati ed emoji.

| Azione | Come |
|---|---|
| Selezionare | trascina col mouse; doppio clic seleziona una parola (un percorso intero); Maiusc+clic estende |
| Copiare | **Ctrl+C** con del testo selezionato, **Ctrl+Maiusc+C**, o tasto destro → *Copia*. Senza selezione Ctrl+C interrompe il comando, come sempre |
| Incollare | **Ctrl+V**, **Ctrl+Maiusc+V** o tasto destro → *Incolla* |
| Scorrere la cronologia | rotellina, barra a destra, **Maiusc+PagSu / PagGiù**; scrivendo si torna in fondo |
| Dimensione del carattere | **Ctrl++ / Ctrl+- / Ctrl+0** o Ctrl+rotellina; anche dal menu ⋯ |
| Cancellare la cronologia | tasto destro → *Cancella la cronologia dello schermo* |

Nei programmi a schermo intero (less, man, vim) la rotellina manda le frecce su e giù.

### Il menu del tasto destro sul terminale
*Copia*, *Incolla*, *Seleziona tutto*, *Aggiungi ai preferiti...* (con del testo selezionato), *Spiega la selezione / lo schermo con l'assistente*, *Chiedi all'assistente...*, *Cancella la cronologia dello schermo*.

## L'assistente
Il pannello a destra. In cima c'è sempre scritto il **servizio** in uso e, per Claude Code, **l'account** (email): clic per cambiarli.

- Scrivi una domanda e premi **Invio** (Maiusc+Invio va a capo, Esc torna al terminale). Esempi: *"come trovo i file più grandi di 1 GB?"*, *"cosa vuol dire questo errore?"*, *"crea un repository git qui"*.
- L'assistente riceve la tua domanda, la shell e la cartella della scheda, i nomi delle altre schede e, se lo permetti, le **ultime righe dello schermo** e il testo selezionato. Se nella domanda nomini un'altra scheda (o parli delle "schede"), riceve anche le sue ultime righe.
- Ogni comando che propone diventa un pulsante **▶** sotto la conversazione: **clic** lo esegue nella scheda attiva, **tasto destro** lo incolla senza eseguirlo, lo copia o lo aggiunge ai preferiti. Il codice Lisp va in una scheda Lisp (la apre se non c'è). Niente viene eseguito senza il tuo clic.
- Ogni scheda ha la sua conversazione; **Nuova** la ricomincia.
- **Spiega lo schermo con l'assistente** (tasto destro sul terminale) chiede la spiegazione delle ultime righe, o del testo selezionato.

### Impostazioni dell'assistente
**⋯ → Impostazioni dell'assistente...**, oppure clic sulla riga del servizio:

| Servizio | Cosa serve |
|---|---|
| **Claude Code** (predefinito) | [Claude Code](https://claude.com/claude-code) installato e il login fatto: usa il tuo abbonamento, niente chiave. Con più abbonamenti scegli l'**Account**; *Aggiungi...* crea un account e apre Claude Code per il login, *Accedi di nuovo...* rifà il login |
| **Claude (API)** | una chiave API di Anthropic |
| **Compatibile OpenAI** | indirizzo, chiave e modello (OpenAI, OpenRouter, Ollama, LM Studio...) |
| **Assistente locale** | si scarica una volta (modello e llama.cpp); gira sul PC, i dati non escono. Serve un PC con almeno 16 GB di RAM |

**Manda all'assistente le ultime righe del terminale**: acceso di serie. Spegnilo se sullo schermo ci sono password o dati riservati; l'assistente vedrà solo la domanda, la shell e la cartella. Le chiavi API sono cifrate con DPAPI di Windows.

Gli account di Claude Code sono condivisi con le altre app Corbol.

## Preferiti
I comandi da riusare, ognuno con **nome**, **descrizione**, **shell** e **comando** (anche su più righe).

- **Aggiungere:** seleziona il comando nel terminale → tasto destro → **Aggiungi ai preferiti...**; oppure tasto destro su un pulsante ▶ dell'assistente → **Aggiungi ai preferiti...**: in questo caso la descrizione è già la spiegazione dell'assistente. Si apre la finestra Preferiti con il nuovo comando: controlla nome e shell e premi OK.
- **Usare:** la **barra sotto il terminale** mostra i preferiti della shell della scheda attiva (e quelli per *qualsiasi shell*): **clic** esegue, **Maiusc+clic** incolla senza eseguire, passandoci sopra si vedono descrizione e comando. Lo stesso dal menu **★ Preferiti** (**Ctrl+Maiusc+P**), dove quelli delle altre shell sono in *Altre shell*.
- **Gestire:** ★ → **Gestisci i preferiti...**: elenco a sinistra, a destra nome, shell, descrizione e comando; **Nuovo**, **Elimina**, **↑ ↓** per l'ordine. **Descrivi con l'assistente** fa scrivere a lui una descrizione di una o due frasi (anche avvisi come "cancella i file").
- La barra si spegne da **⋯ → Suggerisci i preferiti sotto il terminale**.

## La scheda Lisp
Un piccolo Common Lisp interpretato, con comandi per il sistema: vedi la [[Guida al Lisp|Lisp]].

## Il menu ⋯
| Voce | Cosa fa |
|---|---|
| Aggiorna a CorbolPrompt X.Y.Z... | compare quando c'è una versione nuova |
| Nuova scheda, Shell predefinita | come la freccia ⌄; quale shell apre il + |
| Impostazioni dell'assistente... | servizio, account, assistente locale |
| Carattere più grande / più piccolo / normale | dimensione del testo del terminale |
| Lingua | cambia subito, senza chiudere le schede |
| Suggerisci i preferiti sotto il terminale | mostra o nasconde la barra dei preferiti |
| Benvenuto..., Guida, Guida al Lisp | la presentazione iniziale e queste pagine |
| Scorciatoie da tastiera | l'elenco dei tasti |
| Controlla aggiornamenti, Aggiorna automaticamente | vedi [[Installazione]] |
| Informazioni su CorbolPrompt | versione, licenza, cartella delle impostazioni |

## Tasti
| Tasto | Azione |
|---|---|
| Ctrl+Maiusc+T / Ctrl+Maiusc+W | nuova scheda / chiude la scheda |
| Ctrl+Tab / Ctrl+Maiusc+Tab | scheda successiva / precedente |
| Ctrl+C (con selezione), Ctrl+Maiusc+C | copia |
| Ctrl+V, Ctrl+Maiusc+V | incolla |
| Ctrl+Maiusc+A | assistente |
| Ctrl+Maiusc+P | preferiti |
| Ctrl+F1 | questa guida |
| Maiusc+PagSu / PagGiù | scorre la cronologia dello schermo |
| Ctrl++ / Ctrl+- / Ctrl+0 | dimensione del carattere |

Tutti gli altri tasti vanno alla shell: frecce, Tab, Alt+lettera (scorciatoie di bash e PSReadLine), F1-F12 ecc.

## Dove sono i file
In `%APPDATA%\CorbolPrompt`:

| File | Contenuto |
|---|---|
| `settings.txt` | impostazioni (anche quelle dell'assistente; le chiavi sono cifrate) |
| `session.txt` | le schede da riaprire |
| `favorites.json` | i preferiti |
| `lisp-history.txt` | la cronologia della scheda Lisp |
| `init.lisp` | se lo crei, viene caricato all'apertura di ogni scheda Lisp |

L'assistente locale scarica i suoi file in `%LOCALAPPDATA%\CorbolPrompt\ai` (o usa quelli già scaricati da un'altra app Corbol).
